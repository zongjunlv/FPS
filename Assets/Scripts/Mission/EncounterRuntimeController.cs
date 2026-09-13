using System;
using System.Collections.Generic;
using FPS.Simulation;
using UnityEngine;

public readonly struct EncounterHudSnapshot
{
    public EncounterHudSnapshot(
        bool visible,
        string displayName,
        string objective,
        EncounterPhase phase,
        int progress,
        int target,
        int secondsRemaining)
    {
        Visible = visible;
        DisplayName = displayName ?? string.Empty;
        Objective = objective ?? string.Empty;
        Phase = phase;
        Progress = progress;
        Target = target;
        SecondsRemaining = secondsRemaining;
    }

    public bool Visible { get; }
    public string DisplayName { get; }
    public string Objective { get; }
    public EncounterPhase Phase { get; }
    public int Progress { get; }
    public int Target { get; }
    public int SecondsRemaining { get; }
}

public sealed class EncounterRuntimeRestoreSnapshot
{
    public EncounterRuntimeRestoreSnapshot(
        int sequenceContentVersion,
        EncounterRuntimeSnapshot sequence,
        long currentTick,
        IReadOnlyList<int> activeRosterTokens)
    {
        SequenceContentVersion = sequenceContentVersion;
        Sequence = sequence;
        CurrentTick = currentTick;
        ActiveRosterTokens = activeRosterTokens ?? Array.Empty<int>();
    }

    public int SequenceContentVersion { get; }
    public EncounterRuntimeSnapshot Sequence { get; }
    public long CurrentTick { get; }
    public IReadOnlyList<int> ActiveRosterTokens { get; }
}

/// <summary>
/// Unity-facing orchestration for the data-only encounter sequence. It owns
/// presentation and pooled encounter enemies, never the main mission state.
/// </summary>
public sealed class EncounterRuntimeController : MonoBehaviour
{
    private const int FixedTickRate = WaveDirector.SimulationTickRate;
    private const int SpawnRetryLimit = 90;
    private const float HoldRadius = 4.75f;

    private readonly Queue<PendingSpawn> pendingSpawns = new();
    private readonly Dictionary<int, int> activeRosterTokens = new();
    private readonly Dictionary<string, EncounterDefinition> definitions =
        new(StringComparer.Ordinal);
    private EncounterSequenceDefinition sequenceDefinition;
    private EncounterSequence sequence;
    private WaveDirector waves;
    private CityNewMissionController mission;
    private PlayerInventoryController inventory;
    private WorldItemFactory worldItems;
    private Health health;
    private UnifiedGameHud hud;
    private float accumulator;
    private long tick;
    private int pendingEnemyKills;
    private int pendingEliteKills;
    private bool extractionReached;
    private bool configured;

    public bool IsConfigured => configured;
    public EncounterSequence Sequence => sequence;
    public EncounterDefinition CurrentDefinition =>
        sequence?.CurrentDefinition != null &&
        definitions.TryGetValue(
            sequence.CurrentDefinition.StableId,
            out EncounterDefinition definition)
            ? definition
            : null;
    public EncounterHudSnapshot CurrentHud { get; private set; }
    public int ActiveEncounterEnemyCount => activeRosterTokens.Count;

    public void Configure(
        EncounterSequenceDefinition configuredSequence,
        WaveDirector waveDirector)
    {
        if (health != null) health.Died -= HandlePlayerDied;
        if (configuredSequence == null || configuredSequence.Count == 0)
            throw new ArgumentException(
                "Encounter sequence is required.",
                nameof(configuredSequence));
        sequenceDefinition = configuredSequence;
        waves = waveDirector ?? throw new ArgumentNullException(nameof(waveDirector));
        sequence = sequenceDefinition.CreateRuntime(FixedTickRate);
        definitions.Clear();
        for (int index = 0; index < sequenceDefinition.Count; index++)
        {
            EncounterDefinition definition =
                sequenceDefinition.Encounters[index];
            definitions.Add(definition.StableId, definition);
        }
        inventory = GetComponent<PlayerInventoryController>();
        worldItems = GetComponent<WorldItemFactory>();
        health = GetComponent<Health>();
        mission = GetComponent<CityNewMissionController>();
        hud = FindAnyObjectByType<UnifiedGameHud>();
        if (health != null) health.Died += HandlePlayerDied;
        accumulator = 0f;
        tick = waves.Simulation?.Tick ?? 0L;
        configured = true;
        PublishHud();
    }

    public EncounterRuntimeRestoreSnapshot CaptureRuntimeState()
    {
        if (!configured || sequence == null)
            throw new InvalidOperationException("Encounter runtime is not configured.");
        var tokens = new List<int>(activeRosterTokens.Values);
        foreach (PendingSpawn pending in pendingSpawns)
            tokens.Add(pending.Token);
        tokens.Sort();
        return new EncounterRuntimeRestoreSnapshot(
            sequenceDefinition.ContentVersion,
            sequence.CaptureState(),
            tick,
            tokens);
    }

    public bool TryRestoreRuntimeState(
        EncounterRuntimeRestoreSnapshot snapshot,
        out string error)
    {
        error = string.Empty;
        if (!configured || snapshot?.Sequence == null ||
            snapshot.CurrentTick < 0L ||
            snapshot.CurrentTick < snapshot.Sequence.StartedTick ||
            snapshot.SequenceContentVersion != sequenceDefinition.ContentVersion ||
            !sequence.CanRestore(snapshot.Sequence, out error))
        {
            if (string.IsNullOrEmpty(error))
                error = "遭遇序列版本与当前内容不兼容。";
            return false;
        }
        EncounterDefinition restoredDefinition = null;
        if (!string.IsNullOrEmpty(snapshot.Sequence.ActiveEncounterId) &&
            !definitions.TryGetValue(
                snapshot.Sequence.ActiveEncounterId,
                out restoredDefinition))
        {
            error = "遭遇存档对应的数据定义不存在。";
            return false;
        }
        if (!ValidateRosterTokens(
                restoredDefinition,
                snapshot.ActiveRosterTokens,
                out error))
            return false;

        waves.ReleaseEncounterEnemies();
        pendingSpawns.Clear();
        activeRosterTokens.Clear();
        pendingEnemyKills = 0;
        pendingEliteKills = 0;
        extractionReached = false;
        if (!sequence.TryRestore(snapshot.Sequence, out error)) return false;
        tick = snapshot.CurrentTick;
        for (int index = 0; index < snapshot.ActiveRosterTokens.Count; index++)
            pendingSpawns.Enqueue(new PendingSpawn(
                snapshot.ActiveRosterTokens[index],
                0));
        PublishHud();
        return true;
    }

    public void NotifyExtractionReached()
    {
        extractionReached = true;
        if (configured && sequence.HasActiveEncounter &&
            CurrentDefinition?.ObjectiveKind ==
            EncounterObjectiveKind.ReachExtraction)
            AdvanceOneTick();
    }

    private void Update()
    {
        if (!configured || sequence == null ||
            Time.timeScale <= 0f || health == null || health.IsDead)
            return;
        mission ??= GetComponent<CityNewMissionController>();
        hud ??= FindAnyObjectByType<UnifiedGameHud>();
        accumulator += Time.unscaledDeltaTime;
        float fixedDelta = 1f / FixedTickRate;
        while (accumulator + 0.000001f >= fixedDelta)
        {
            accumulator -= fixedDelta;
            AdvanceOneTick();
        }
    }

    private void AdvanceOneTick()
    {
        tick++;
        TrySpawnPending();
        WaveProgressSnapshot wave = waves.CurrentProgress;
        bool inHoldArea = IsInsideHoldArea();
        bool reached = extractionReached || IsInsideExtraction();
        var facts = new EncounterFacts(
            tick,
            wave.CurrentWave,
            waves.Phase,
            mission != null
                ? mission.State
                : MissionFlowState.EliminateTargets,
            waves.IsEncounterSpawnReady,
            pendingEnemyKills,
            pendingEliteKills,
            inHoldArea,
            reached,
            health.IsDead);
        pendingEnemyKills = 0;
        pendingEliteKills = 0;
        extractionReached = false;
        EncounterTransition transition = sequence.Advance(facts);
        if (transition.Signal != EncounterSignal.None)
            HandleTransition(transition);
        PublishHud();
    }

    private void HandleTransition(EncounterTransition transition)
    {
        EncounterDefinition definition = definitions.TryGetValue(
            transition.EncounterId,
            out EncounterDefinition found)
            ? found
            : null;
        switch (transition.Signal)
        {
            case EncounterSignal.Started:
                hud?.ShowEncounterOutcome(
                    $"遭遇预警 · {definition?.DisplayName}",
                    true);
                if (transition.Phase == EncounterPhase.Active)
                    QueueRoster(definition);
                break;
            case EncounterSignal.Activated:
                QueueRoster(definition);
                break;
            case EncounterSignal.Succeeded:
                waves.ReleaseEncounterEnemies();
                pendingSpawns.Clear();
                activeRosterTokens.Clear();
                DeliverReward(definition);
                hud?.ShowEncounterOutcome(
                    $"遭遇完成 · {definition?.DisplayName}",
                    false);
                break;
            case EncounterSignal.Failed:
                ClearEncounterEnemies();
                hud?.ShowEncounterOutcome(
                    $"遭遇失败 · {definition?.DisplayName}（主线继续）",
                    true);
                break;
            case EncounterSignal.Skipped:
                ClearEncounterEnemies();
                hud?.ShowEncounterOutcome(
                    $"遭遇跳过 · 资源暂不可用（主线继续）",
                    true);
                break;
        }
        RunDeterminismRecorder.Active?.RecordEncounterTransition(
            transition,
            definition?.Kind ?? EncounterKind.Ambush);
    }

    private void QueueRoster(EncounterDefinition definition)
    {
        pendingSpawns.Clear();
        activeRosterTokens.Clear();
        if (definition == null || !waves.IsEncounterSpawnReady)
        {
            HandleTransition(sequence.CancelActive(true));
            return;
        }
        for (int rosterIndex = 0;
             rosterIndex < definition.Roster.Count;
             rosterIndex++)
        {
            EncounterRosterEntry roster = definition.Roster[rosterIndex];
            for (int copy = 0; copy < roster.Count; copy++)
                pendingSpawns.Enqueue(new PendingSpawn(
                    EncodeRosterToken(rosterIndex, copy),
                    0));
        }
    }

    private void TrySpawnPending()
    {
        if (pendingSpawns.Count == 0 || CurrentDefinition == null) return;
        PendingSpawn pending = pendingSpawns.Dequeue();
        int rosterIndex = DecodeRosterIndex(pending.Token);
        int copyIndex = DecodeCopyIndex(pending.Token);
        if (rosterIndex < 0 || rosterIndex >= CurrentDefinition.Roster.Count)
            return;
        EncounterRosterEntry entry = CurrentDefinition.Roster[rosterIndex];
        if (waves.TrySpawnEncounterEnemy(
                entry,
                copyIndex,
                HandleEncounterEnemyEnded,
                out EnemySpawnHandle handle))
        {
            activeRosterTokens.Add(handle.SpawnId, pending.Token);
            return;
        }
        pending = new PendingSpawn(pending.Token, pending.Attempts + 1);
        if (pending.Attempts < SpawnRetryLimit)
        {
            pendingSpawns.Enqueue(pending);
            return;
        }
        HandleTransition(sequence.CancelActive(true));
    }

    private void HandleEncounterEnemyEnded(
        EnemySpawnHandle handle,
        EnemyExitReason reason)
    {
        if (!activeRosterTokens.Remove(handle.SpawnId) ||
            reason != EnemyExitReason.Died)
            return;
        pendingEnemyKills++;
        if (handle.RewardTier == LootRewardTier.Elite)
            pendingEliteKills++;
    }

    private void DeliverReward(EncounterDefinition definition)
    {
        if (definition?.RewardItem == null || definition.RewardQuantity <= 0)
        {
            sequence.AcknowledgeReward(definition?.StableId);
            return;
        }
        inventory ??= GetComponent<PlayerInventoryController>();
        worldItems ??= GetComponent<WorldItemFactory>();
        InventoryAddResult result = inventory != null
            ? inventory.Add(definition.RewardItem, definition.RewardQuantity)
            : new InventoryAddResult(definition.RewardQuantity, 0);
        if (result.Remaining > 0)
            worldItems?.TrySpawnRewardDrop(
                definition.RewardItem,
                result.Remaining,
                transform.position,
                transform,
                out _);
        sequence.AcknowledgeReward(definition.StableId);
        hud?.ShowRewardCue(
            $"遭遇奖励 · {definition.RewardItem.DisplayName} ×{definition.RewardQuantity}",
            definition.Kind == EncounterKind.EliteEscort);
    }

    private bool IsInsideHoldArea()
    {
        if (CurrentDefinition?.ObjectiveKind != EncounterObjectiveKind.HoldArea ||
            mission?.Terminal == null)
            return false;
        Vector3 offset = transform.position - mission.Terminal.transform.position;
        offset.y = 0f;
        return offset.sqrMagnitude <= HoldRadius * HoldRadius;
    }

    private bool IsInsideExtraction()
    {
        MissionExtractionZone zone = mission?.ExtractionZone;
        if (CurrentDefinition?.ObjectiveKind !=
                EncounterObjectiveKind.ReachExtraction || zone == null)
            return mission != null && mission.State == MissionFlowState.Victory;
        Vector3 offset = transform.position - zone.transform.position;
        offset.y = 0f;
        return offset.sqrMagnitude <= zone.Radius * zone.Radius ||
               mission.State == MissionFlowState.Victory;
    }

    private void PublishHud()
    {
        EncounterDefinition definition = CurrentDefinition;
        if (definition == null || !sequence.HasActiveEncounter)
        {
            CurrentHud = new EncounterHudSnapshot(false, "", "",
                EncounterPhase.Pending, 0, 0, 0);
            hud?.ShowEncounter(CurrentHud);
            return;
        }
        int remaining = sequence.DeadlineTick > 0
            ? Mathf.Max(0, Mathf.CeilToInt(
                (sequence.DeadlineTick - tick) / (float)FixedTickRate))
            : 0;
        int displayProgress = definition.ObjectiveKind ==
            EncounterObjectiveKind.HoldArea
            ? sequence.Progress / FixedTickRate
            : sequence.Progress;
        int displayTarget = definition.ObjectiveKind ==
            EncounterObjectiveKind.HoldArea
            ? Mathf.CeilToInt(sequence.Target / (float)FixedTickRate)
            : sequence.Target;
        CurrentHud = new EncounterHudSnapshot(
            true,
            definition.DisplayName,
            definition.ObjectiveText,
            sequence.Phase,
            displayProgress,
            displayTarget,
            remaining);
        hud?.ShowEncounter(CurrentHud);
    }

    private void ClearEncounterEnemies()
    {
        waves?.ReleaseEncounterEnemies();
        pendingSpawns.Clear();
        activeRosterTokens.Clear();
    }

    private void HandlePlayerDied()
    {
        if (sequence?.HasActiveEncounter == true)
            HandleTransition(sequence.CancelActive(false));
    }

    private void OnDestroy()
    {
        if (health != null) health.Died -= HandlePlayerDied;
        ClearEncounterEnemies();
        hud?.ShowEncounter(new EncounterHudSnapshot());
    }

    private bool ValidateRosterTokens(
        EncounterDefinition definition,
        IReadOnlyList<int> tokens,
        out string error)
    {
        error = string.Empty;
        if (tokens == null || (definition == null && tokens.Count > 0))
        {
            error = "遭遇存档的活动敌人列表无效。";
            return false;
        }
        var unique = new HashSet<int>();
        for (int index = 0; index < tokens.Count; index++)
        {
            int rosterIndex = DecodeRosterIndex(tokens[index]);
            int copyIndex = DecodeCopyIndex(tokens[index]);
            if (!unique.Add(tokens[index]) || rosterIndex < 0 ||
                rosterIndex >= definition.Roster.Count || copyIndex < 0 ||
                copyIndex >= definition.Roster[rosterIndex].Count)
            {
                error = "遭遇存档包含未知或重复的敌人槽位。";
                return false;
            }
        }
        return true;
    }

    private static int EncodeRosterToken(int rosterIndex, int copyIndex) =>
        rosterIndex * 1000 + copyIndex;
    private static int DecodeRosterIndex(int token) => token / 1000;
    private static int DecodeCopyIndex(int token) => token % 1000;

    private readonly struct PendingSpawn
    {
        public PendingSpawn(int token, int attempts)
        {
            Token = token;
            Attempts = attempts;
        }
        public int Token { get; }
        public int Attempts { get; }
    }
}
