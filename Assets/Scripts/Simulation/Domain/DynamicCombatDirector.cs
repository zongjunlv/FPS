using System;
using System.Collections.Generic;
using System.Text;

namespace FPS.Simulation
{
    public enum CombatDirectorPhase
    {
        Observing,
        Warning,
        Deploying,
        Cooldown
    }

    public enum CombatDirectorSignal
    {
        None,
        WarningStarted,
        SpawnRequested,
        EventCompleted,
        EventCancelled
    }

    public sealed class CombatPressureSnapshot
    {
        public CombatPressureSnapshot(
            float healthRatio,
            float armorRatio,
            float ammoRatio,
            float recentDamageRatio,
            float clearRateNormalized,
            float activeThreatNormalized,
            float habitualHeatRatio,
            int heatCellX,
            int heatCellZ)
        {
            HealthRatio = Clamp01(healthRatio);
            ArmorRatio = Clamp01(armorRatio);
            AmmoRatio = Clamp01(ammoRatio);
            RecentDamageRatio = Clamp01(recentDamageRatio);
            ClearRateNormalized = Clamp01(clearRateNormalized);
            ActiveThreatNormalized = Clamp01(activeThreatNormalized);
            HabitualHeatRatio = Clamp01(habitualHeatRatio);
            HeatCellX = heatCellX;
            HeatCellZ = heatCellZ;
            OverallPressure = Clamp01(
                (1f - HealthRatio) * 0.35f +
                (1f - ArmorRatio) * 0.1f +
                (1f - AmmoRatio) * 0.15f +
                RecentDamageRatio * 0.2f +
                ActiveThreatNormalized * 0.2f);
        }

        public float HealthRatio { get; }
        public float ArmorRatio { get; }
        public float AmmoRatio { get; }
        public float RecentDamageRatio { get; }
        public float ClearRateNormalized { get; }
        public float ActiveThreatNormalized { get; }
        public float HabitualHeatRatio { get; }
        public int HeatCellX { get; }
        public int HeatCellZ { get; }
        public float OverallPressure { get; }

        private static float Clamp01(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                return 0f;
            }
            return Math.Max(0f, Math.Min(1f, value));
        }
    }

    public readonly struct CombatDirectorRoleCandidate
    {
        public CombatDirectorRoleCandidate(
            string enemyTypeId,
            string roleTag,
            int threatCost)
        {
            EnemyTypeId = string.IsNullOrWhiteSpace(enemyTypeId)
                ? "*"
                : enemyTypeId.Trim();
            RoleTag = string.IsNullOrWhiteSpace(roleTag)
                ? "assault"
                : roleTag.Trim();
            ThreatCost = Math.Max(1, threatCost);
        }

        public string EnemyTypeId { get; }
        public string RoleTag { get; }
        public int ThreatCost { get; }
    }

    public sealed class CombatDirectorResources
    {
        public CombatDirectorResources(
            int remainingWaveSlots,
            int aliveCapacity,
            int poolCapacity,
            bool directedSpawnReachable,
            IReadOnlyList<CombatDirectorRoleCandidate> roleCandidates)
        {
            RemainingWaveSlots = Math.Max(0, remainingWaveSlots);
            AliveCapacity = Math.Max(0, aliveCapacity);
            PoolCapacity = Math.Max(0, poolCapacity);
            DirectedSpawnReachable = directedSpawnReachable;
            RoleCandidates = roleCandidates ??
                Array.Empty<CombatDirectorRoleCandidate>();
        }

        public int RemainingWaveSlots { get; }
        public int AliveCapacity { get; }
        public int PoolCapacity { get; }
        public bool DirectedSpawnReachable { get; }
        public IReadOnlyList<CombatDirectorRoleCandidate> RoleCandidates { get; }
        public int AvailableCount => Math.Min(
            RemainingWaveSlots,
            Math.Min(AliveCapacity, PoolCapacity));
    }

    public sealed class CombatDirectorConfiguration
    {
        public CombatDirectorConfiguration(
            long initialDelayTicks,
            long evaluationIntervalTicks,
            long warningTicks,
            long cooldownTicks,
            int maximumReinforcements,
            int maximumIntensityDelta,
            float minimumCandidateScore)
        {
            InitialDelayTicks = Math.Max(0, initialDelayTicks);
            EvaluationIntervalTicks = Math.Max(1, evaluationIntervalTicks);
            WarningTicks = Math.Max(1, warningTicks);
            CooldownTicks = Math.Max(1, cooldownTicks);
            MaximumReinforcements = Math.Max(1, maximumReinforcements);
            MaximumIntensityDelta = Math.Max(1, maximumIntensityDelta);
            MinimumCandidateScore = Math.Max(
                0f,
                Math.Min(1f, minimumCandidateScore));
        }

        public long InitialDelayTicks { get; }
        public long EvaluationIntervalTicks { get; }
        public long WarningTicks { get; }
        public long CooldownTicks { get; }
        public int MaximumReinforcements { get; }
        public int MaximumIntensityDelta { get; }
        public float MinimumCandidateScore { get; }
    }

    public sealed class CombatDirectorInput
    {
        public CombatDirectorInput(
            long tick,
            CombatPressureSnapshot pressure,
            CombatDirectorResources resources)
        {
            Tick = Math.Max(0, tick);
            Pressure = pressure ?? throw new ArgumentNullException(nameof(pressure));
            Resources = resources ?? throw new ArgumentNullException(nameof(resources));
        }

        public long Tick { get; }
        public CombatPressureSnapshot Pressure { get; }
        public CombatDirectorResources Resources { get; }
    }

    public sealed class CombatDirectorEventState
    {
        public CombatDirectorEventState(
            long eventId,
            string enemyTypeId,
            string roleTag,
            int requestedCount,
            int spawnedCount,
            int signedDirectionDegrees,
            float selectedScore,
            string reason)
        {
            EventId = Math.Max(0, eventId);
            EnemyTypeId = enemyTypeId ?? string.Empty;
            RoleTag = roleTag ?? string.Empty;
            RequestedCount = Math.Max(0, requestedCount);
            SpawnedCount = Math.Max(0, spawnedCount);
            SignedDirectionDegrees = signedDirectionDegrees;
            SelectedScore = Math.Max(0f, Math.Min(1f, selectedScore));
            Reason = reason ?? string.Empty;
        }

        public long EventId { get; }
        public string EnemyTypeId { get; }
        public string RoleTag { get; }
        public int RequestedCount { get; }
        public int SpawnedCount { get; }
        public int PendingCount => Math.Max(0, RequestedCount - SpawnedCount);
        public int SignedDirectionDegrees { get; }
        public float SelectedScore { get; }
        public string Reason { get; }
    }

    public sealed class CombatDirectorOutput
    {
        public CombatDirectorOutput(
            CombatDirectorSignal signal,
            CombatDirectorPhase phase,
            CombatPressureSnapshot pressure,
            CombatDirectorEventState currentEvent)
        {
            Signal = signal;
            Phase = phase;
            Pressure = pressure;
            CurrentEvent = currentEvent;
        }

        public CombatDirectorSignal Signal { get; }
        public CombatDirectorPhase Phase { get; }
        public CombatPressureSnapshot Pressure { get; }
        public CombatDirectorEventState CurrentEvent { get; }
    }

    public sealed class CombatDirectorRuntimeSnapshot
    {
        public CombatDirectorRuntimeSnapshot(
            CombatDirectorPhase phase,
            ulong randomState,
            long nextEvaluationTick,
            long warningEndTick,
            long cooldownEndTick,
            long nextEventId,
            int lastIntensity,
            int failedSpawnAttempts,
            CombatDirectorEventState currentEvent)
        {
            Phase = phase;
            RandomState = randomState;
            NextEvaluationTick = nextEvaluationTick;
            WarningEndTick = warningEndTick;
            CooldownEndTick = cooldownEndTick;
            NextEventId = nextEventId;
            LastIntensity = lastIntensity;
            FailedSpawnAttempts = failedSpawnAttempts;
            CurrentEvent = currentEvent;
        }

        public CombatDirectorPhase Phase { get; }
        public ulong RandomState { get; }
        public long NextEvaluationTick { get; }
        public long WarningEndTick { get; }
        public long CooldownEndTick { get; }
        public long NextEventId { get; }
        public int LastIntensity { get; }
        public int FailedSpawnAttempts { get; }
        public CombatDirectorEventState CurrentEvent { get; }
    }

    /// <summary>
    /// Pure fixed-tick combat director. Callers provide world facts and consume
    /// its signals; scene lookup, spawning and presentation stay outside.
    /// </summary>
    public sealed class DynamicCombatDirector
    {
        private readonly CombatDirectorConfiguration configuration;
        private readonly DirectorRandomStream random;
        private long nextEvaluationTick;
        private long warningEndTick;
        private long cooldownEndTick;
        private long nextEventId = 1;
        private int lastIntensity;
        private int failedSpawnAttempts;

        public DynamicCombatDirector(
            long runSeed,
            CombatDirectorConfiguration configuredRules)
        {
            configuration = configuredRules ??
                throw new ArgumentNullException(nameof(configuredRules));
            random = new DirectorRandomStream(runSeed);
            Phase = CombatDirectorPhase.Observing;
            nextEvaluationTick = configuration.InitialDelayTicks;
        }

        public CombatDirectorPhase Phase { get; private set; }
        public CombatDirectorEventState CurrentEvent { get; private set; }

        public CombatDirectorOutput Advance(CombatDirectorInput input)
        {
            if (Phase == CombatDirectorPhase.Warning)
            {
                if (!input.Resources.DirectedSpawnReachable ||
                    input.Resources.AvailableCount <
                    CurrentEvent.PendingCount ||
                    input.Pressure.OverallPressure >= 0.85f)
                {
                    BeginCooldown(input.Tick);
                    return Output(
                        CombatDirectorSignal.EventCancelled,
                        input.Pressure);
                }

                if (input.Tick >= warningEndTick)
                {
                    Phase = CombatDirectorPhase.Deploying;
                    return Output(
                        CombatDirectorSignal.SpawnRequested,
                        input.Pressure);
                }
            }

            if (Phase == CombatDirectorPhase.Deploying &&
                CurrentEvent != null &&
                CurrentEvent.PendingCount > 0)
            {
                return Output(
                    CombatDirectorSignal.SpawnRequested,
                    input.Pressure);
            }

            if (Phase == CombatDirectorPhase.Cooldown &&
                input.Tick >= cooldownEndTick)
            {
                Phase = CombatDirectorPhase.Observing;
                CurrentEvent = null;
                nextEvaluationTick = checked(
                    input.Tick + configuration.EvaluationIntervalTicks);
            }

            if (Phase == CombatDirectorPhase.Observing &&
                input.Tick >= nextEvaluationTick)
            {
                nextEvaluationTick = checked(
                    input.Tick + configuration.EvaluationIntervalTicks);
                float score = ScoreRearFlank(input.Pressure);
                if (score >= configuration.MinimumCandidateScore &&
                    input.Resources.DirectedSpawnReachable &&
                    input.Resources.AvailableCount > 0 &&
                    input.Resources.RoleCandidates.Count > 0)
                {
                    int roleIndex = random.NextInt(
                        input.Resources.RoleCandidates.Count);
                    CombatDirectorRoleCandidate role =
                        input.Resources.RoleCandidates[roleIndex];
                    int requested = score >= 0.8f ? 2 : 1;
                    requested = Math.Min(
                        requested,
                        configuration.MaximumReinforcements);
                    requested = Math.Min(requested, input.Resources.AvailableCount);
                    requested = Math.Min(
                        requested,
                        lastIntensity + configuration.MaximumIntensityDelta);
                    int side = random.NextInt(2) == 0 ? -1 : 1;
                    int angle = side * (120 + random.NextInt(46));
                    CurrentEvent = new CombatDirectorEventState(
                        nextEventId++,
                        role.EnemyTypeId,
                        role.RoleTag,
                        requested,
                        0,
                        angle,
                        score,
                        "玩家状态稳定，侧后方存在可用增援窗口");
                    warningEndTick = checked(
                        input.Tick + configuration.WarningTicks);
                    Phase = CombatDirectorPhase.Warning;
                    return Output(
                        CombatDirectorSignal.WarningStarted,
                        input.Pressure);
                }
            }

            return new CombatDirectorOutput(
                CombatDirectorSignal.None,
                Phase,
                input.Pressure,
                CurrentEvent);
        }

        public void ReportSpawnOutcome(long tick, bool success)
        {
            if (Phase != CombatDirectorPhase.Deploying ||
                CurrentEvent == null || CurrentEvent.PendingCount == 0)
            {
                return;
            }

            if (!success)
            {
                failedSpawnAttempts++;
                if (failedSpawnAttempts >= 3)
                {
                    BeginCooldown(Math.Max(0, tick));
                }
                return;
            }

            failedSpawnAttempts = 0;
            CurrentEvent = new CombatDirectorEventState(
                CurrentEvent.EventId,
                CurrentEvent.EnemyTypeId,
                CurrentEvent.RoleTag,
                CurrentEvent.RequestedCount,
                CurrentEvent.SpawnedCount + 1,
                CurrentEvent.SignedDirectionDegrees,
                CurrentEvent.SelectedScore,
                CurrentEvent.Reason);
            if (CurrentEvent.PendingCount == 0)
            {
                lastIntensity = CurrentEvent.SpawnedCount;
                BeginCooldown(Math.Max(0, tick));
            }
        }

        public CombatDirectorRuntimeSnapshot CaptureState()
        {
            return new CombatDirectorRuntimeSnapshot(
                Phase,
                random.State,
                nextEvaluationTick,
                warningEndTick,
                cooldownEndTick,
                nextEventId,
                lastIntensity,
                failedSpawnAttempts,
                CurrentEvent);
        }

        public bool TryRestore(
            CombatDirectorRuntimeSnapshot snapshot,
            out string error)
        {
            if (!CanRestore(snapshot, out error))
            {
                return false;
            }

            Phase = snapshot.Phase;
            random.RestoreState(snapshot.RandomState);
            nextEvaluationTick = snapshot.NextEvaluationTick;
            warningEndTick = snapshot.WarningEndTick;
            cooldownEndTick = snapshot.CooldownEndTick;
            nextEventId = snapshot.NextEventId;
            lastIntensity = snapshot.LastIntensity;
            failedSpawnAttempts = snapshot.FailedSpawnAttempts;
            CurrentEvent = snapshot.CurrentEvent;
            return true;
        }

        public bool CanRestore(
            CombatDirectorRuntimeSnapshot snapshot,
            out string error)
        {
            error = string.Empty;
            if (snapshot == null ||
                !Enum.IsDefined(typeof(CombatDirectorPhase), snapshot.Phase) ||
                snapshot.NextEvaluationTick < 0 ||
                snapshot.WarningEndTick < 0 ||
                snapshot.CooldownEndTick < 0 ||
                snapshot.NextEventId < 1 ||
                snapshot.LastIntensity < 0 ||
                snapshot.LastIntensity > configuration.MaximumReinforcements ||
                snapshot.FailedSpawnAttempts < 0 ||
                snapshot.FailedSpawnAttempts >= 3)
            {
                error = "Dynamic combat director snapshot is invalid.";
                return false;
            }

            CombatDirectorEventState current = snapshot.CurrentEvent;
            if (snapshot.Phase == CombatDirectorPhase.Observing)
            {
                if (current != null)
                {
                    error = "Observing director snapshot cannot retain an event.";
                    return false;
                }
                return true;
            }

            if (current == null || current.EventId < 1 ||
                current.EventId >= snapshot.NextEventId ||
                string.IsNullOrWhiteSpace(current.EnemyTypeId) ||
                string.IsNullOrWhiteSpace(current.RoleTag) ||
                current.RequestedCount < 1 ||
                current.RequestedCount > configuration.MaximumReinforcements ||
                current.SpawnedCount < 0 ||
                current.SpawnedCount > current.RequestedCount ||
                Math.Abs(current.SignedDirectionDegrees) < 120 ||
                Math.Abs(current.SignedDirectionDegrees) > 165 ||
                float.IsNaN(current.SelectedScore) ||
                float.IsInfinity(current.SelectedScore))
            {
                error = "Dynamic combat director event snapshot is invalid.";
                return false;
            }

            if (snapshot.Phase == CombatDirectorPhase.Warning &&
                current.SpawnedCount != 0 ||
                snapshot.Phase == CombatDirectorPhase.Deploying &&
                current.PendingCount == 0)
            {
                error = "Dynamic combat director phase and event disagree.";
                return false;
            }
            return true;
        }

        private CombatDirectorOutput Output(
            CombatDirectorSignal signal,
            CombatPressureSnapshot pressure)
        {
            return new CombatDirectorOutput(
                signal,
                Phase,
                pressure,
                CurrentEvent);
        }

        private static float ScoreRearFlank(CombatPressureSnapshot pressure)
        {
            float readiness =
                pressure.HealthRatio * 0.25f +
                pressure.ArmorRatio * 0.05f +
                pressure.AmmoRatio * 0.15f +
                pressure.ClearRateNormalized * 0.3f +
                (1f - pressure.RecentDamageRatio) * 0.15f +
                pressure.HabitualHeatRatio * 0.1f;
            float dangerPenalty =
                pressure.ActiveThreatNormalized * 0.3f +
                (1f - pressure.HealthRatio) * 0.25f +
                pressure.RecentDamageRatio * 0.25f;
            return Math.Max(
                0f,
                Math.Min(1f, readiness - dangerPenalty + 0.15f));
        }

        private void BeginCooldown(long tick)
        {
            Phase = CombatDirectorPhase.Cooldown;
            cooldownEndTick = checked(tick + configuration.CooldownTicks);
            failedSpawnAttempts = 0;
        }

        /// <summary>
        /// Self-contained named stream keeps the Simulation assembly independent
        /// while using the same documented SplitMix64 transition.
        /// </summary>
        private sealed class DirectorRandomStream
        {
            private ulong state;

            public DirectorRandomStream(long runSeed)
            {
                state = Derive(runSeed, "director");
            }

            public ulong State => state;

            public void RestoreState(ulong value)
            {
                state = value;
            }

            public uint NextUInt32()
            {
                state += 0x9E3779B97F4A7C15UL;
                ulong value = state;
                value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
                value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
                value ^= value >> 31;
                return (uint)(value >> 32);
            }

            public int NextInt(int exclusiveMaximum)
            {
                if (exclusiveMaximum <= 0)
                    throw new ArgumentOutOfRangeException(
                        nameof(exclusiveMaximum));
                uint bound = (uint)exclusiveMaximum;
                uint threshold = unchecked((uint)(0U - bound)) % bound;
                uint value;
                do value = NextUInt32(); while (value < threshold);
                return (int)(value % bound);
            }

            private static ulong Derive(long seed, string name)
            {
                ulong hash = 14695981039346656037UL;
                ulong bits = unchecked((ulong)seed);
                for (int index = 0; index < 8; index++)
                {
                    hash ^= (byte)(bits >> (index * 8));
                    hash *= 1099511628211UL;
                }
                foreach (byte value in Encoding.UTF8.GetBytes(name))
                {
                    hash ^= value;
                    hash *= 1099511628211UL;
                }
                return hash;
            }
        }
    }
}
