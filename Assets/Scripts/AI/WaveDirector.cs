using System;
using System.Collections.Generic;
using FPS.GameplayEffects;
using UnityEngine;

public enum WaveStopReason
{
    None,
    PlayerDied,
    Reconfigured,
    Disabled,
    Destroyed
}

public sealed class WaveDirector : MonoBehaviour, IWaveProgressSource
{
    private const float CueDuration = 1.6f;

    private readonly Dictionary<int, EnemySpawnHandle> activeEnemies = new();
    private readonly List<Vector3> occupiedPositions = new();
    private readonly List<WaveStageDefinition> stages = new();
    private readonly List<int> completedWaveSpawnCounts = new();
    private readonly List<int> peakAliveByWave = new();

    private IEnemyFactory enemyFactory;
    private IEnemySpawnPointResolver spawnPointResolver;
    private Transform player;
    private Health playerHealth;
    private MultiWaveFlowState flow;
    private float spawnCooldown;
    private float cueRemaining;
    private int nextSpawnId = 1;
    private int lastPublishedCountdown = -1;
    private bool configured;
    private bool destroying;
    private WavePresentationCue presentationCue;
    private int presentationWave;

    public static WaveDirector Active { get; private set; }

    public event Action<WaveProgressSnapshot> ProgressChanged;
    public event Action<int> WaveStarted;
    public event Action<int> WaveEnded;
    public event Action WaveCompleted;
    public event Action<EnemyDeathEvent> EnemyDied;
    public event Action<EnemySpawnedEvent> EnemySpawned;

    public WaveProgressSnapshot CurrentProgress => CreateProgress();
    public WaveRunPhase Phase => flow != null
        ? flow.Phase
        : WaveRunPhase.Idle;
    public bool IsRunning => flow != null && flow.IsRunning;
    public bool IsCompleted => flow != null && flow.IsCompleted;
    public int PeakAliveCount { get; private set; }
    public int SpawnAttemptCount { get; private set; }
    public int CompletionEventCount { get; private set; }
    public int WaveStartedEventCount { get; private set; }
    public int WaveEndedEventCount { get; private set; }
    public int EnemyDeathEventCount { get; private set; }
    public EnemyDeathEvent LastEnemyDeath { get; private set; }
    public int CompletedWaveCount => completedWaveSpawnCounts.Count;
    public float MinimumSpawnSafetyDistanceObserved { get; private set; }
    public float MinimumSpawnEnemySpacingObserved { get; private set; }
    public WaveStopReason StopReason { get; private set; }
    public IReadOnlyDictionary<int, EnemySpawnHandle> ActiveEnemies =>
        activeEnemies;
    public IReadOnlyList<int> CompletedWaveSpawnCounts =>
        completedWaveSpawnCounts;
    public IReadOnlyList<int> PeakAliveByWave => peakAliveByWave;

    public WaveRuntimeSnapshot CaptureRuntimeState()
    {
        if (!configured || flow == null ||
            (!flow.IsRunning && !flow.IsCompleted))
        {
            throw new InvalidOperationException(
                "Wave runtime is not in a saveable state.");
        }

        var enemies = new List<EnemyRuntimeSnapshot>(activeEnemies.Count);
        foreach (EnemySpawnHandle handle in activeEnemies.Values)
        {
            Health health = handle.Controller != null
                ? handle.Controller.GetComponent<Health>()
                : null;
            if (health == null || health.IsDead)
            {
                throw new InvalidOperationException(
                    "An active enemy has no saveable health state.");
            }

            EnemyBurnEffectController burn =
                handle.Controller.GetComponent<EnemyBurnEffectController>();
            GameplayEffectRuntimeSnapshot effects = null;
            if (burn != null && !burn.TryCaptureGameplayEffectSnapshot(
                    EncodeEffectSource,
                    out effects,
                    out string effectError))
            {
                throw new InvalidOperationException(effectError);
            }

            enemies.Add(new EnemyRuntimeSnapshot(
                handle.WaveNumber,
                handle.SpawnId,
                handle.EnemyTypeId,
                handle.Controller.transform.position,
                handle.Controller.transform.rotation,
                health.CurrentHealth,
                health.CurrentArmor,
                effects));
        }

        enemies.Sort((left, right) => left.SpawnId.CompareTo(right.SpawnId));
        return new WaveRuntimeSnapshot(
            flow.CaptureState(),
            Mathf.Max(0f, spawnCooldown),
            nextSpawnId,
            enemies);
    }

    public bool TryRestoreRuntimeState(
        WaveRuntimeSnapshot snapshot,
        out string error)
    {
        error = string.Empty;
        if (!configured || flow == null || snapshot?.Flow == null ||
            snapshot.Enemies == null || snapshot.NextSpawnId < 1 ||
            float.IsNaN(snapshot.SpawnCooldownRemaining) ||
            float.IsInfinity(snapshot.SpawnCooldownRemaining) ||
            snapshot.SpawnCooldownRemaining < 0f || activeEnemies.Count > 0)
        {
            error = "Wave runtime restore state is invalid.";
            return false;
        }

        var staged = new List<EnemySpawnHandle>(snapshot.Enemies.Count);
        var stagedIds = new HashSet<int>();
        var expectedActiveIds = new HashSet<int>(
            snapshot.Flow.CurrentWaveState?.ActiveIds ?? Array.Empty<int>());
        if (expectedActiveIds.Count != snapshot.Enemies.Count)
        {
            error = "Saved active enemies do not match wave state.";
            return false;
        }
        int maximumSpawnId = 0;

        for (int index = 0; index < snapshot.Enemies.Count; index++)
        {
            EnemyRuntimeSnapshot enemy = snapshot.Enemies[index];
            maximumSpawnId = Mathf.Max(maximumSpawnId, enemy.SpawnId);
            if (enemy.SpawnId < 1 || !stagedIds.Add(enemy.SpawnId) ||
                !expectedActiveIds.Contains(enemy.SpawnId) ||
                enemy.WaveNumber < 1 || enemy.WaveNumber > stages.Count ||
                !TryResolveEnemyEntry(
                    enemy.WaveNumber,
                    enemy.EnemyTypeId,
                    out WaveEnemyEntry entry))
            {
                error = "Saved enemy identity does not match wave content.";
                ReleaseStaged(staged);
                return false;
            }

            var request = new EnemySpawnRequest(
                enemy.SpawnId,
                enemy.WaveNumber,
                entry,
                enemy.Position,
                enemy.Rotation,
                player);
            if (!enemyFactory.TrySpawn(
                    request,
                    HandleEnemyEnded,
                    out EnemySpawnHandle handle))
            {
                error = "Enemy factory could not restore every saved enemy.";
                ReleaseStaged(staged);
                return false;
            }

            Health health = handle.Controller.GetComponent<Health>();
            if (health == null ||
                !health.TryRestoreSnapshotVitals(enemy.Health, enemy.Armor))
            {
                enemyFactory.Release(handle);
                error = "Saved enemy vitals are outside current content limits.";
                ReleaseStaged(staged);
                return false;
            }

            EnemyBurnEffectController burn =
                handle.Controller.GetComponent<EnemyBurnEffectController>();
            if (burn != null &&
                !burn.TryRestoreGameplayEffectSnapshot(
                    enemy.Effects,
                    ResolveEffectSource,
                    out error))
            {
                enemyFactory.Release(handle);
                ReleaseStaged(staged);
                return false;
            }

            staged.Add(handle);
        }

        if (snapshot.NextSpawnId <= maximumSpawnId ||
            !flow.TryRestore(snapshot.Flow, out error))
        {
            ReleaseStaged(staged);
            return false;
        }

        for (int index = 0; index < staged.Count; index++)
        {
            EnemySpawnHandle handle = staged[index];
            activeEnemies.Add(handle.SpawnId, handle);
        }

        nextSpawnId = snapshot.NextSpawnId;
        spawnCooldown = snapshot.SpawnCooldownRemaining;
        completedWaveSpawnCounts.Clear();
        for (int wave = 1; wave < flow.CurrentWave; wave++)
        {
            completedWaveSpawnCounts.Add(stages[wave - 1].Wave.TotalEnemyCount);
        }
        StopReason = WaveStopReason.None;
        PublishProgress();
        return true;
    }

    private string EncodeEffectSource(UnityEngine.Object source)
    {
        GameObject sourceObject = source switch
        {
            GameObject value => value,
            Component value => value.gameObject,
            _ => null
        };
        if (sourceObject != null && player != null &&
            (sourceObject == player.gameObject ||
             sourceObject.transform.IsChildOf(player)))
        {
            return "player";
        }
        return string.Empty;
    }

    private UnityEngine.Object ResolveEffectSource(string sourceKey)
    {
        return string.Equals(sourceKey, "player", StringComparison.Ordinal) &&
               player != null
            ? player.gameObject
            : null;
    }

    private bool TryResolveEnemyEntry(
        int waveNumber,
        string enemyTypeId,
        out WaveEnemyEntry entry)
    {
        entry = null;
        if (waveNumber < 1 || waveNumber > stages.Count ||
            string.IsNullOrWhiteSpace(enemyTypeId)) return false;
        IReadOnlyList<WaveEnemyEntry> entries =
            stages[waveNumber - 1].Wave.ResolvedEntries;
        for (int index = 0; index < entries.Count; index++)
        {
            if (entries[index] != null && string.Equals(
                    entries[index].EnemyTypeId,
                    enemyTypeId,
                    StringComparison.Ordinal))
            {
                entry = entries[index];
                return true;
            }
        }
        return false;
    }

    private void ReleaseStaged(IReadOnlyList<EnemySpawnHandle> staged)
    {
        for (int index = 0; index < staged.Count; index++)
        {
            staged[index].Lifecycle?.Disarm();
            enemyFactory.Release(staged[index]);
        }
    }

    private WaveDefinition CurrentDefinition =>
        flow != null && flow.CurrentWave > 0 && flow.CurrentWave <= stages.Count
            ? stages[flow.CurrentWave - 1].Wave
            : null;

    private void Awake()
    {
        if (Active != null && Active != this)
        {
            enabled = false;
            return;
        }

        Active = this;
    }

    private void Update()
    {
        if (!configured || flow == null)
        {
            return;
        }

        UpdatePresentationCue();

        if (flow.Phase == WaveRunPhase.Intermission)
        {
            UpdateIntermission();
            return;
        }

        if (!flow.CanSpawn || !IsFactoryReady)
        {
            return;
        }

        spawnCooldown -= Time.deltaTime;

        if (spawnCooldown <= 0f)
        {
            TrySpawnNext();
        }
    }

    private void OnDisable()
    {
        if (!destroying && configured && IsRunning)
        {
            StopRun(WaveStopReason.Disabled);
        }
    }

    private void OnDestroy()
    {
        destroying = true;
        StopRunInternal(WaveStopReason.Destroyed, false);
        UnbindPlayerHealth();

        if (Active == this)
        {
            Active = null;
        }
    }

    public void Configure(
        WaveDefinition waveDefinition,
        IEnemyFactory factory,
        IEnemySpawnPointResolver resolver,
        Transform playerTarget)
    {
        if (waveDefinition == null)
        {
            throw new ArgumentNullException(nameof(waveDefinition));
        }

        ConfigureCore(
            new[] { new WaveStageDefinition(waveDefinition, 0f) },
            factory,
            resolver,
            playerTarget);
    }

    public void Configure(
        WaveSequenceDefinition sequence,
        IEnemyFactory factory,
        IEnemySpawnPointResolver resolver,
        Transform playerTarget)
    {
        if (sequence == null || sequence.WaveCount == 0)
        {
            throw new ArgumentException(
                "A configured wave sequence is required.",
                nameof(sequence));
        }

        ConfigureCore(sequence.Stages, factory, resolver, playerTarget);
    }

    public bool StartRun()
    {
        if (!configured || flow == null || !IsFactoryReady || !flow.StartRun())
        {
            return false;
        }

        StopReason = WaveStopReason.None;
        BeginCurrentWavePresentation();
        return true;
    }

    private bool IsFactoryReady => !(enemyFactory is IAsyncEnemyFactory asynchronous) ||
        asynchronous.PreparationState == EnemyFactoryPreparationState.Ready;

    public bool StopRun(WaveStopReason reason)
    {
        return StopRunInternal(reason, true);
    }

    private void ConfigureCore(
        IReadOnlyList<WaveStageDefinition> configuredStages,
        IEnemyFactory factory,
        IEnemySpawnPointResolver resolver,
        Transform playerTarget)
    {
        if (configuredStages == null || configuredStages.Count == 0)
        {
            throw new ArgumentException(
                "At least one wave stage is required.",
                nameof(configuredStages));
        }

        StopRunInternal(WaveStopReason.Reconfigured, false);
        UnbindPlayerHealth();
        enemyFactory = factory ??
            throw new ArgumentNullException(nameof(factory));
        spawnPointResolver = resolver ??
            throw new ArgumentNullException(nameof(resolver));
        player = playerTarget ??
            throw new ArgumentNullException(nameof(playerTarget));
        stages.Clear();
        var rules = new WaveStageRules[configuredStages.Count];

        for (int index = 0; index < configuredStages.Count; index++)
        {
            WaveStageDefinition stage = configuredStages[index];

            if (stage?.Wave == null)
            {
                throw new ArgumentException(
                    $"Wave stage {index + 1} has no definition.",
                    nameof(configuredStages));
            }

            stages.Add(stage);
            rules[index] = new WaveStageRules(
                stage.Wave.TotalEnemyCount,
                stage.Wave.MaximumAliveCount,
                stage.IntermissionAfterSeconds);
        }

        flow = new MultiWaveFlowState(rules);
        completedWaveSpawnCounts.Clear();
        peakAliveByWave.Clear();

        for (int index = 0; index < stages.Count; index++)
        {
            peakAliveByWave.Add(0);
        }

        nextSpawnId = 1;
        PeakAliveCount = 0;
        SpawnAttemptCount = 0;
        CompletionEventCount = 0;
        WaveStartedEventCount = 0;
        WaveEndedEventCount = 0;
        EnemyDeathEventCount = 0;
        LastEnemyDeath = default;
        MinimumSpawnSafetyDistanceObserved = float.PositiveInfinity;
        MinimumSpawnEnemySpacingObserved = float.PositiveInfinity;
        spawnCooldown = 0f;
        cueRemaining = 0f;
        presentationCue = WavePresentationCue.None;
        presentationWave = 0;
        lastPublishedCountdown = -1;
        StopReason = WaveStopReason.None;
        playerHealth = player.GetComponent<Health>();

        if (playerHealth != null)
        {
            playerHealth.Died += HandlePlayerDied;
        }

        configured = true;
        player.GetComponent<PlayerRunProgression>()
            ?.BindKillSource(this);
        player.GetComponent<PlayerCombatEventRouter>()
            ?.BindKillSource(this);
        UnityEngine.Object.FindAnyObjectByType<UnifiedGameHud>()
            ?.BindWave(this);
        PublishProgress();
    }

    private void UpdatePresentationCue()
    {
        if (presentationCue == WavePresentationCue.None)
        {
            return;
        }

        cueRemaining -= Time.unscaledDeltaTime;

        if (cueRemaining > 0f)
        {
            return;
        }

        presentationCue = WavePresentationCue.None;
        presentationWave = 0;
        cueRemaining = 0f;
        PublishProgress();
    }

    private void UpdateIntermission()
    {
        int previousCountdown = Mathf.CeilToInt(
            flow.IntermissionRemaining);
        bool startedNextWave = flow.Tick(Time.deltaTime);
        int currentCountdown = Mathf.CeilToInt(
            flow.IntermissionRemaining);

        if (startedNextWave)
        {
            BeginCurrentWavePresentation();
            return;
        }

        if (currentCountdown != previousCountdown ||
            currentCountdown != lastPublishedCountdown)
        {
            lastPublishedCountdown = currentCountdown;
            PublishProgress();
        }
    }

    private void BeginCurrentWavePresentation()
    {
        WaveDefinition definition = CurrentDefinition;
        spawnPointResolver.Configure(definition);
        spawnCooldown = 0f;
        lastPublishedCountdown = -1;
        SetCue(WavePresentationCue.WaveStarted, flow.CurrentWave);
        WaveStartedEventCount++;
        PublishProgress();
        WaveStarted?.Invoke(flow.CurrentWave);
    }

    private void TrySpawnNext()
    {
        WaveDefinition definition = CurrentDefinition;

        if (definition == null)
        {
            StopRun(WaveStopReason.Disabled);
            return;
        }

        SpawnAttemptCount++;
        occupiedPositions.Clear();

        foreach (EnemySpawnHandle active in activeEnemies.Values)
        {
            if (active.Controller != null)
            {
                occupiedPositions.Add(active.Controller.transform.position);
            }
        }

        if (!spawnPointResolver.TryResolve(
                player,
                occupiedPositions,
                out Vector3 spawnPoint))
        {
            spawnCooldown = definition.RetryInterval;
            return;
        }

        WaveEnemyEntry entry = definition.GetEntry(flow.SpawnedCount);
        int spawnId = nextSpawnId;
        Vector3 facing = player.position - spawnPoint;
        facing.y = 0f;
        Quaternion rotation = facing.sqrMagnitude > 0.001f
            ? Quaternion.LookRotation(facing.normalized)
            : Quaternion.identity;
        var request = new EnemySpawnRequest(
            spawnId,
            flow.CurrentWave,
            entry,
            spawnPoint,
            rotation,
            player);

        if (!enemyFactory.TrySpawn(
                request,
                HandleEnemyEnded,
                out EnemySpawnHandle handle))
        {
            spawnCooldown = definition.RetryInterval;
            return;
        }

        if (!flow.TryRegisterSpawn(spawnId))
        {
            enemyFactory.Release(handle);
            spawnCooldown = definition.RetryInterval;
            return;
        }

        activeEnemies.Add(spawnId, handle);
        EnemySpawned?.Invoke(new EnemySpawnedEvent(request, handle));
        RecordSpawnClearances(spawnPoint);
        nextSpawnId++;
        PeakAliveCount = Mathf.Max(PeakAliveCount, flow.AliveCount);
        int waveIndex = flow.CurrentWave - 1;
        peakAliveByWave[waveIndex] = Mathf.Max(
            peakAliveByWave[waveIndex],
            flow.AliveCount);
        spawnCooldown = definition.SpawnInterval;
        PublishProgress();
    }

    private void RecordSpawnClearances(Vector3 spawnPoint)
    {
        Vector3 playerOffset = spawnPoint - player.position;
        playerOffset.y = 0f;
        MinimumSpawnSafetyDistanceObserved = Mathf.Min(
            MinimumSpawnSafetyDistanceObserved,
            playerOffset.magnitude);

        for (int index = 0; index < occupiedPositions.Count; index++)
        {
            Vector3 enemyOffset = spawnPoint - occupiedPositions[index];
            enemyOffset.y = 0f;
            MinimumSpawnEnemySpacingObserved = Mathf.Min(
                MinimumSpawnEnemySpacingObserved,
                enemyOffset.magnitude);
        }
    }

    private void HandleEnemyEnded(
        EnemySpawnHandle handle,
        EnemyExitReason reason)
    {
        if (flow == null || !flow.IsRunning)
        {
            return;
        }

        int endingWave = flow.CurrentWave;

        if (!flow.TrySettle(handle.SpawnId))
        {
            return;
        }

        WaveRunPhase phaseAfterSettle = flow.Phase;
        activeEnemies.Remove(handle.SpawnId);
        PublishEnemyDeath(handle, reason, endingWave);
        enemyFactory.Release(handle);

        if (phaseAfterSettle == WaveRunPhase.Intermission ||
            phaseAfterSettle == WaveRunPhase.Completed ||
            (phaseAfterSettle == WaveRunPhase.Spawning &&
             endingWave != flow.CurrentWave))
        {
            completedWaveSpawnCounts.Add(
                stages[endingWave - 1].Wave.TotalEnemyCount);
            WaveEndedEventCount++;
            SetCue(
                phaseAfterSettle == WaveRunPhase.Completed
                    ? WavePresentationCue.RunCompleted
                    : WavePresentationCue.WaveCleared,
                endingWave);
            PublishProgress();
            WaveEnded?.Invoke(endingWave);

            if (phaseAfterSettle == WaveRunPhase.Completed)
            {
                CompletionEventCount++;
                WaveCompleted?.Invoke();
                return;
            }

            if (phaseAfterSettle == WaveRunPhase.Spawning)
            {
                BeginCurrentWavePresentation();
            }

            return;
        }

        PublishProgress();
        spawnCooldown = Mathf.Min(
            spawnCooldown,
            CurrentDefinition.SpawnInterval);
    }

    private void PublishEnemyDeath(
        EnemySpawnHandle handle,
        EnemyExitReason reason,
        int endingWave)
    {
        if (reason != EnemyExitReason.Died || handle.Controller == null)
        {
            return;
        }

        Health enemyHealth = handle.Controller.GetComponent<Health>();

        if (enemyHealth == null || !enemyHealth.HasLastAppliedDamage)
        {
            return;
        }

        LastEnemyDeath = new EnemyDeathEvent(
            handle.Controller,
            handle.SpawnId,
            handle.WaveNumber > 0
                ? handle.WaveNumber
                : endingWave,
            enemyHealth.LastAppliedDamage,
            handle.Controller.RewardExperience,
            handle.Controller.transform.position,
            handle.EnemyTypeId,
            handle.RewardTier,
            handle.Controller.LootQuantityMultiplier);
        EnemyDeathEventCount++;
        EnemyDied?.Invoke(LastEnemyDeath);
    }

    private void HandlePlayerDied()
    {
        StopRun(WaveStopReason.PlayerDied);
    }

    private bool StopRunInternal(
        WaveStopReason reason,
        bool publish)
    {
        bool stopped = flow != null && flow.StopRun();

        if (!stopped && activeEnemies.Count == 0)
        {
            return false;
        }

        var handles = new List<EnemySpawnHandle>(activeEnemies.Values);

        foreach (EnemySpawnHandle handle in handles)
        {
            handle.Lifecycle?.Disarm();
            enemyFactory?.Release(handle);
        }

        activeEnemies.Clear();
        spawnCooldown = 0f;
        cueRemaining = 0f;
        presentationCue = WavePresentationCue.None;
        presentationWave = 0;
        StopReason = reason;

        if (publish)
        {
            PublishProgress();
        }

        return true;
    }

    private void UnbindPlayerHealth()
    {
        if (playerHealth != null)
        {
            playerHealth.Died -= HandlePlayerDied;
            playerHealth = null;
        }
    }

    private void SetCue(WavePresentationCue cue, int waveNumber)
    {
        presentationCue = cue;
        presentationWave = waveNumber;
        cueRemaining = CueDuration;
    }

    private WaveProgressSnapshot CreateProgress()
    {
        if (flow == null)
        {
            return new WaveProgressSnapshot(0, 0, 0, 0);
        }

        return new WaveProgressSnapshot(
            flow.CurrentWave,
            flow.TotalWaves,
            flow.Phase,
            flow.TotalEnemyCount,
            flow.SpawnedCount,
            flow.AliveCount,
            flow.SettledCount,
            flow.IntermissionRemaining,
            presentationCue,
            presentationWave);
    }

    private void PublishProgress()
    {
        ProgressChanged?.Invoke(CreateProgress());
    }
}
