using System;
using System.Collections.Generic;

namespace FPS.Simulation
{
    public enum EncounterKind
    {
        Ambush,
        EliteEscort,
        TimedHold,
        ExtractionPursuit
    }

    public enum EncounterTriggerKind
    {
        WaveReached,
        MissionStateReached
    }

    public enum EncounterObjectiveKind
    {
        EliminateEnemies,
        EliminateElite,
        HoldArea,
        ReachExtraction
    }

    public enum EncounterMainFlowPolicy
    {
        Parallel,
        Exclusive
    }

    public enum EncounterPhase
    {
        Pending,
        Intro,
        Active,
        Succeeded,
        Failed,
        Skipped,
        Completed
    }

    public enum EncounterSignal
    {
        None,
        Started,
        Activated,
        Progressed,
        Succeeded,
        Failed,
        Skipped,
        RewardAcknowledged
    }

    public readonly struct EncounterTrigger
    {
        private EncounterTrigger(
            EncounterTriggerKind kind,
            int waveNumber,
            global::MissionFlowState missionState)
        {
            Kind = kind;
            WaveNumber = waveNumber;
            MissionState = missionState;
        }

        public EncounterTriggerKind Kind { get; }
        public int WaveNumber { get; }
        public global::MissionFlowState MissionState { get; }

        public static EncounterTrigger Wave(int waveNumber)
        {
            if (waveNumber < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(waveNumber));
            }
            return new EncounterTrigger(
                EncounterTriggerKind.WaveReached,
                waveNumber,
                global::MissionFlowState.EliminateTargets);
        }

        public static EncounterTrigger Mission(global::MissionFlowState state)
        {
            return new EncounterTrigger(
                EncounterTriggerKind.MissionStateReached,
                0,
                state);
        }

        internal bool IsSatisfied(EncounterFacts facts)
        {
            return Kind switch
            {
                EncounterTriggerKind.WaveReached =>
                    facts.CurrentWave >= WaveNumber &&
                    facts.WavePhase != global::WaveRunPhase.Idle &&
                    facts.WavePhase != global::WaveRunPhase.Stopped,
                EncounterTriggerKind.MissionStateReached =>
                    HasReachedMissionState(
                        facts.MissionState,
                        MissionState),
                _ => false
            };
        }

        private static bool HasReachedMissionState(
            global::MissionFlowState current,
            global::MissionFlowState required)
        {
            if (current == global::MissionFlowState.Defeat)
                return required == global::MissionFlowState.Defeat;
            if (required == global::MissionFlowState.Defeat)
                return false;
            return MissionProgress(current) >= MissionProgress(required);
        }

        private static int MissionProgress(global::MissionFlowState state) =>
            state switch
            {
                global::MissionFlowState.EliminateTargets => 0,
                global::MissionFlowState.ActivateTerminal => 1,
                global::MissionFlowState.ExtractionAvailable => 2,
                global::MissionFlowState.Victory => 3,
                _ => -1
            };
    }

    public readonly struct EncounterObjective
    {
        private EncounterObjective(EncounterObjectiveKind kind, int target)
        {
            Kind = kind;
            Target = Math.Max(1, target);
        }

        public EncounterObjectiveKind Kind { get; }
        public int Target { get; }

        public static EncounterObjective Eliminate(int count) =>
            new EncounterObjective(EncounterObjectiveKind.EliminateEnemies, count);

        public static EncounterObjective EliminateElite() =>
            new EncounterObjective(EncounterObjectiveKind.EliminateElite, 1);

        public static EncounterObjective HoldTicks(int ticks) =>
            new EncounterObjective(EncounterObjectiveKind.HoldArea, ticks);

        public static EncounterObjective ReachExtraction() =>
            new EncounterObjective(EncounterObjectiveKind.ReachExtraction, 1);
    }

    public sealed class EncounterDefinitionSpec
    {
        public EncounterDefinitionSpec(
            string stableId,
            int version,
            EncounterKind kind,
            string displayName,
            string objectiveText,
            EncounterTrigger trigger,
            EncounterObjective objective,
            EncounterMainFlowPolicy mainFlowPolicy,
            int introTicks,
            int timeLimitTicks)
        {
            if (string.IsNullOrWhiteSpace(stableId))
                throw new ArgumentException("Encounter ID is required.", nameof(stableId));
            if (version < 1)
                throw new ArgumentOutOfRangeException(nameof(version));
            StableId = stableId.Trim();
            Version = version;
            Kind = kind;
            DisplayName = displayName?.Trim() ?? string.Empty;
            ObjectiveText = objectiveText?.Trim() ?? string.Empty;
            Trigger = trigger;
            Objective = objective;
            MainFlowPolicy = mainFlowPolicy;
            IntroTicks = Math.Max(0, introTicks);
            TimeLimitTicks = Math.Max(0, timeLimitTicks);
        }

        public string StableId { get; }
        public int Version { get; }
        public EncounterKind Kind { get; }
        public string DisplayName { get; }
        public string ObjectiveText { get; }
        public EncounterTrigger Trigger { get; }
        public EncounterObjective Objective { get; }
        public EncounterMainFlowPolicy MainFlowPolicy { get; }
        public int IntroTicks { get; }
        public int TimeLimitTicks { get; }
    }

    public readonly struct EncounterFacts
    {
        public EncounterFacts(
            long tick,
            int currentWave,
            global::WaveRunPhase wavePhase,
            global::MissionFlowState missionState,
            bool resourcesReady,
            int enemyKillDelta = 0,
            int eliteKillDelta = 0,
            bool insideObjectiveArea = false,
            bool reachedExtraction = false,
            bool playerDefeated = false)
        {
            Tick = Math.Max(0L, tick);
            CurrentWave = Math.Max(0, currentWave);
            WavePhase = wavePhase;
            MissionState = missionState;
            ResourcesReady = resourcesReady;
            EnemyKillDelta = Math.Max(0, enemyKillDelta);
            EliteKillDelta = Math.Max(0, eliteKillDelta);
            InsideObjectiveArea = insideObjectiveArea;
            ReachedExtraction = reachedExtraction;
            PlayerDefeated = playerDefeated;
        }

        public long Tick { get; }
        public int CurrentWave { get; }
        public global::WaveRunPhase WavePhase { get; }
        public global::MissionFlowState MissionState { get; }
        public bool ResourcesReady { get; }
        public int EnemyKillDelta { get; }
        public int EliteKillDelta { get; }
        public bool InsideObjectiveArea { get; }
        public bool ReachedExtraction { get; }
        public bool PlayerDefeated { get; }
    }

    public readonly struct EncounterTransition
    {
        internal EncounterTransition(
            EncounterSignal signal,
            string encounterId,
            EncounterPhase phase,
            int progress,
            int target,
            long deadlineTick)
        {
            Signal = signal;
            EncounterId = encounterId ?? string.Empty;
            Phase = phase;
            Progress = progress;
            Target = target;
            DeadlineTick = deadlineTick;
        }

        public EncounterSignal Signal { get; }
        public string EncounterId { get; }
        public EncounterPhase Phase { get; }
        public int Progress { get; }
        public int Target { get; }
        public long DeadlineTick { get; }

        public static EncounterTransition None => new EncounterTransition(
            EncounterSignal.None,
            string.Empty,
            EncounterPhase.Pending,
            0,
            0,
            0);
    }

    public sealed class EncounterRuntimeSnapshot
    {
        public EncounterRuntimeSnapshot(
            int nextIndex,
            string activeEncounterId,
            int activeDefinitionVersion,
            EncounterPhase phase,
            long startedTick,
            long activeTick,
            long deadlineTick,
            int progress,
            IReadOnlyList<string> resolvedEncounterIds,
            IReadOnlyList<string> rewardedEncounterIds)
        {
            NextIndex = nextIndex;
            ActiveEncounterId = activeEncounterId ?? string.Empty;
            ActiveDefinitionVersion = activeDefinitionVersion;
            Phase = phase;
            StartedTick = startedTick;
            ActiveTick = activeTick;
            DeadlineTick = deadlineTick;
            Progress = progress;
            ResolvedEncounterIds = Copy(resolvedEncounterIds);
            RewardedEncounterIds = Copy(rewardedEncounterIds);
        }

        public int NextIndex { get; }
        public string ActiveEncounterId { get; }
        public int ActiveDefinitionVersion { get; }
        public EncounterPhase Phase { get; }
        public long StartedTick { get; }
        public long ActiveTick { get; }
        public long DeadlineTick { get; }
        public int Progress { get; }
        public IReadOnlyList<string> ResolvedEncounterIds { get; }
        public IReadOnlyList<string> RewardedEncounterIds { get; }

        private static IReadOnlyList<string> Copy(IReadOnlyList<string> source)
        {
            if (source == null) return Array.Empty<string>();
            var result = new string[source.Count];
            for (int index = 0; index < source.Count; index++)
                result[index] = source[index] ?? string.Empty;
            return result;
        }
    }

    /// <summary>
    /// Fixed-tick, data-only encounter authority. Presentation, spawning and
    /// rewards react to transitions; they never own encounter progression.
    /// </summary>
    public sealed class EncounterSequence
    {
        private readonly EncounterDefinitionSpec[] definitions;
        private readonly HashSet<string> resolved = new(StringComparer.Ordinal);
        private readonly HashSet<string> rewarded = new(StringComparer.Ordinal);
        private int nextIndex;
        private int activeIndex = -1;
        private EncounterPhase phase = EncounterPhase.Pending;
        private long startedTick;
        private long activeTick;
        private long deadlineTick;
        private int progress;

        public EncounterSequence(IReadOnlyList<EncounterDefinitionSpec> configured)
        {
            if (configured == null || configured.Count == 0)
                throw new ArgumentException("Encounter sequence requires definitions.", nameof(configured));
            definitions = new EncounterDefinitionSpec[configured.Count];
            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < configured.Count; index++)
            {
                EncounterDefinitionSpec definition = configured[index] ??
                    throw new ArgumentException("Encounter definition cannot be null.", nameof(configured));
                if (!ids.Add(definition.StableId))
                    throw new ArgumentException("Encounter IDs must be unique.", nameof(configured));
                definitions[index] = definition;
            }
        }

        public EncounterDefinitionSpec CurrentDefinition =>
            activeIndex >= 0 && activeIndex < definitions.Length
                ? definitions[activeIndex]
                : null;
        public EncounterPhase Phase => phase;
        public int Progress => progress;
        public int Target => CurrentDefinition?.Objective.Target ?? 0;
        public long DeadlineTick => deadlineTick;
        public bool HasActiveEncounter => CurrentDefinition != null &&
            (phase == EncounterPhase.Intro || phase == EncounterPhase.Active);
        public bool IsComplete => nextIndex >= definitions.Length && !HasActiveEncounter;

        public EncounterTransition Advance(EncounterFacts facts)
        {
            if (!HasActiveEncounter)
                return TryStart(facts);

            EncounterDefinitionSpec definition = CurrentDefinition;
            if (facts.PlayerDefeated)
                return Resolve(EncounterSignal.Failed, EncounterPhase.Failed);

            if (phase == EncounterPhase.Intro)
            {
                if (facts.Tick < activeTick)
                    return EncounterTransition.None;
                phase = EncounterPhase.Active;
                return Transition(EncounterSignal.Activated);
            }

            if (definition.TimeLimitTicks > 0 && facts.Tick > deadlineTick)
                return Resolve(EncounterSignal.Failed, EncounterPhase.Failed);

            int before = progress;
            switch (definition.Objective.Kind)
            {
                case EncounterObjectiveKind.EliminateEnemies:
                    progress = Math.Min(
                        definition.Objective.Target,
                        progress + facts.EnemyKillDelta);
                    break;
                case EncounterObjectiveKind.EliminateElite:
                    progress = Math.Min(
                        definition.Objective.Target,
                        progress + facts.EliteKillDelta);
                    break;
                case EncounterObjectiveKind.HoldArea:
                    progress = facts.InsideObjectiveArea
                        ? Math.Min(definition.Objective.Target, progress + 1)
                        : 0;
                    break;
                case EncounterObjectiveKind.ReachExtraction:
                    progress = facts.ReachedExtraction ? 1 : 0;
                    break;
            }

            if (progress >= definition.Objective.Target)
                return Resolve(EncounterSignal.Succeeded, EncounterPhase.Succeeded);
            return progress != before
                ? Transition(EncounterSignal.Progressed)
                : EncounterTransition.None;
        }

        public EncounterTransition AcknowledgeReward(string encounterId)
        {
            if (string.IsNullOrWhiteSpace(encounterId) ||
                !resolved.Contains(encounterId) ||
                !rewarded.Add(encounterId))
                return EncounterTransition.None;
            return new EncounterTransition(
                EncounterSignal.RewardAcknowledged,
                encounterId,
                EncounterPhase.Completed,
                progress,
                Target,
                deadlineTick);
        }

        public EncounterTransition CancelActive(bool skipped)
        {
            if (!HasActiveEncounter) return EncounterTransition.None;
            return Resolve(
                skipped ? EncounterSignal.Skipped : EncounterSignal.Failed,
                skipped ? EncounterPhase.Skipped : EncounterPhase.Failed);
        }

        public bool IsRewarded(string encounterId) =>
            !string.IsNullOrWhiteSpace(encounterId) && rewarded.Contains(encounterId);

        public EncounterRuntimeSnapshot CaptureState()
        {
            string id = CurrentDefinition?.StableId ?? string.Empty;
            int version = CurrentDefinition?.Version ?? 0;
            return new EncounterRuntimeSnapshot(
                nextIndex,
                id,
                version,
                phase,
                startedTick,
                activeTick,
                deadlineTick,
                progress,
                Sorted(resolved),
                Sorted(rewarded));
        }

        public bool TryRestore(EncounterRuntimeSnapshot snapshot, out string error)
        {
            if (!CanRestore(snapshot, out error)) return false;
            nextIndex = snapshot.NextIndex;
            activeIndex = string.IsNullOrEmpty(snapshot.ActiveEncounterId)
                ? -1
                : FindDefinition(snapshot.ActiveEncounterId);
            phase = snapshot.Phase;
            startedTick = snapshot.StartedTick;
            activeTick = snapshot.ActiveTick;
            deadlineTick = snapshot.DeadlineTick;
            progress = snapshot.Progress;
            resolved.Clear();
            rewarded.Clear();
            AddAll(resolved, snapshot.ResolvedEncounterIds);
            AddAll(rewarded, snapshot.RewardedEncounterIds);
            return true;
        }

        public bool CanRestore(EncounterRuntimeSnapshot snapshot, out string error)
        {
            error = string.Empty;
            if (snapshot == null || snapshot.NextIndex < 0 ||
                snapshot.NextIndex > definitions.Length || snapshot.Progress < 0 ||
                snapshot.StartedTick < 0 || snapshot.ActiveTick < 0 ||
                snapshot.DeadlineTick < 0 ||
                snapshot.ActiveTick < snapshot.StartedTick ||
                snapshot.DeadlineTick > 0 &&
                snapshot.DeadlineTick < snapshot.ActiveTick)
            {
                error = "遭遇存档基础状态无效。";
                return false;
            }
            int index = string.IsNullOrEmpty(snapshot.ActiveEncounterId)
                ? -1
                : FindDefinition(snapshot.ActiveEncounterId);
            if (index < 0 && !string.IsNullOrEmpty(snapshot.ActiveEncounterId))
            {
                error = "遭遇存档引用了未知事件。";
                return false;
            }
            if (index >= 0 &&
                (definitions[index].Version != snapshot.ActiveDefinitionVersion ||
                 snapshot.Progress > definitions[index].Objective.Target ||
                 snapshot.NextIndex != index))
            {
                error = "遭遇存档版本、顺序或进度与当前内容不兼容。";
                return false;
            }
            bool activePhase = snapshot.Phase == EncounterPhase.Intro ||
                               snapshot.Phase == EncounterPhase.Active;
            if ((index >= 0) != activePhase ||
                index < 0 && snapshot.ActiveDefinitionVersion != 0)
            {
                error = "遭遇存档活动阶段与事件标识不一致。";
                return false;
            }
            if (!ValidateIds(
                    snapshot.ResolvedEncounterIds,
                    snapshot.NextIndex,
                    out error) ||
                !ValidateIds(
                    snapshot.RewardedEncounterIds,
                    snapshot.NextIndex,
                    out error))
                return false;
            var resolvedSnapshot = new HashSet<string>(
                snapshot.ResolvedEncounterIds,
                StringComparer.Ordinal);
            if (resolvedSnapshot.Count != snapshot.NextIndex)
            {
                error = "遭遇存档结算账本与序列游标不一致。";
                return false;
            }
            for (int definitionIndex = 0;
                 definitionIndex < snapshot.NextIndex;
                 definitionIndex++)
                if (!resolvedSnapshot.Contains(
                        definitions[definitionIndex].StableId))
                {
                    error = "遭遇存档缺少已推进事件的结算记录。";
                    return false;
                }
            for (int rewardIndex = 0;
                 rewardIndex < snapshot.RewardedEncounterIds.Count;
                 rewardIndex++)
                if (!resolvedSnapshot.Contains(
                        snapshot.RewardedEncounterIds[rewardIndex]))
                {
                    error = "遭遇奖励账本引用了未结算事件。";
                    return false;
                }
            return true;
        }

        private EncounterTransition TryStart(EncounterFacts facts)
        {
            if (nextIndex >= definitions.Length)
                return EncounterTransition.None;
            EncounterDefinitionSpec definition = definitions[nextIndex];
            if (!definition.Trigger.IsSatisfied(facts))
                return EncounterTransition.None;
            activeIndex = nextIndex;
            phase = definition.IntroTicks > 0
                ? EncounterPhase.Intro
                : EncounterPhase.Active;
            startedTick = facts.Tick;
            activeTick = facts.Tick + definition.IntroTicks;
            deadlineTick = definition.TimeLimitTicks > 0
                ? activeTick + definition.TimeLimitTicks
                : 0L;
            progress = 0;
            if (!facts.ResourcesReady)
                return Resolve(EncounterSignal.Skipped, EncounterPhase.Skipped);
            return Transition(EncounterSignal.Started);
        }

        private EncounterTransition Resolve(
            EncounterSignal signal,
            EncounterPhase resolvedPhase)
        {
            EncounterTransition transition = new EncounterTransition(
                signal,
                CurrentDefinition.StableId,
                resolvedPhase,
                progress,
                CurrentDefinition.Objective.Target,
                deadlineTick);
            resolved.Add(CurrentDefinition.StableId);
            nextIndex = activeIndex + 1;
            activeIndex = -1;
            phase = EncounterPhase.Completed;
            return transition;
        }

        private EncounterTransition Transition(EncounterSignal signal)
        {
            return new EncounterTransition(
                signal,
                CurrentDefinition.StableId,
                phase,
                progress,
                CurrentDefinition.Objective.Target,
                deadlineTick);
        }

        private int FindDefinition(string id)
        {
            for (int index = 0; index < definitions.Length; index++)
                if (string.Equals(definitions[index].StableId, id, StringComparison.Ordinal))
                    return index;
            return -1;
        }

        private bool ValidateIds(
            IReadOnlyList<string> ids,
            int restoredNextIndex,
            out string error)
        {
            error = string.Empty;
            if (ids == null)
            {
                error = "遭遇存档集合缺失。";
                return false;
            }
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < ids.Count; index++)
            {
                string id = ids[index];
                int definitionIndex = FindDefinition(id);
                if (definitionIndex < 0 ||
                    definitionIndex >= restoredNextIndex ||
                    !seen.Add(id))
                {
                    error = "遭遇存档包含未知、重复或未结算的奖励事件。";
                    return false;
                }
            }
            return true;
        }

        private static IReadOnlyList<string> Sorted(HashSet<string> values)
        {
            var result = new List<string>(values);
            result.Sort(StringComparer.Ordinal);
            return result;
        }

        private static void AddAll(HashSet<string> target, IReadOnlyList<string> source)
        {
            for (int index = 0; index < source.Count; index++) target.Add(source[index]);
        }
    }
}
