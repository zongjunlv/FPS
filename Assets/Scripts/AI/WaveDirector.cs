using System;
using System.Collections.Generic;
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

        if (!flow.CanSpawn)
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
        if (!configured || flow == null || !flow.StartRun())
        {
            return false;
        }

        StopReason = WaveStopReason.None;
        BeginCurrentWavePresentation();
        return true;
    }

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
            handle.RewardTier);
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
