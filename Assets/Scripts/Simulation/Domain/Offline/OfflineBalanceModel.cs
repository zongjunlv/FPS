using System;
using System.Collections;
using System.Collections.Generic;

namespace FPS.Simulation.Offline
{
    public enum OfflineRunOutcome
    {
        Victory,
        Defeat
    }

    public enum OfflineFailureReason
    {
        None,
        PlayerDefeated,
        ResourceDepleted,
        UnsolvableRoster,
        TickLimitExceeded
    }

    public sealed class OfflineWorldModelSpec
    {
        public const int CurrentVersion = 1;

        public OfflineWorldModelSpec(
            int enemyAttackOpportunityBasisPoints = 10000,
            int version = CurrentVersion)
        {
            if (version < 1)
                throw new ArgumentOutOfRangeException(nameof(version));
            if (enemyAttackOpportunityBasisPoints < 0 ||
                enemyAttackOpportunityBasisPoints > 10000)
                throw new ArgumentOutOfRangeException(
                    nameof(enemyAttackOpportunityBasisPoints));
            Version = version;
            EnemyAttackOpportunityBasisPoints =
                enemyAttackOpportunityBasisPoints;
        }

        public int Version { get; }
        public int EnemyAttackOpportunityBasisPoints { get; }
    }

    public sealed class OfflinePlayerSpec
    {
        private readonly string[] sourceTags;

        public OfflinePlayerSpec(
            float health,
            float armor,
            float damagePerShot,
            int shotIntervalTicks,
            int startingAmmo,
            float hitChance = 1f,
            IEnumerable<string> sourceTags = null)
        {
            if (!PositiveFinite(health))
                throw new ArgumentOutOfRangeException(nameof(health));
            if (!NonNegativeFinite(armor))
                throw new ArgumentOutOfRangeException(nameof(armor));
            if (!NonNegativeFinite(damagePerShot))
                throw new ArgumentOutOfRangeException(nameof(damagePerShot));
            if (shotIntervalTicks < 1)
                throw new ArgumentOutOfRangeException(nameof(shotIntervalTicks));
            if (startingAmmo < 0)
                throw new ArgumentOutOfRangeException(nameof(startingAmmo));
            if (!Finite(hitChance) || hitChance < 0f || hitChance > 1f)
                throw new ArgumentOutOfRangeException(nameof(hitChance));

            Health = health;
            Armor = armor;
            DamagePerShot = damagePerShot;
            ShotIntervalTicks = shotIntervalTicks;
            StartingAmmo = startingAmmo;
            HitChance = hitChance;
            this.sourceTags = CopyTags(sourceTags, "entity.player");
        }

        public float Health { get; }
        public float Armor { get; }
        public float DamagePerShot { get; }
        public int ShotIntervalTicks { get; }
        public int StartingAmmo { get; }
        public float HitChance { get; }
        public IReadOnlyList<string> SourceTags => sourceTags;

        private static bool PositiveFinite(float value) =>
            NonNegativeFinite(value) && value > 0f;

        private static bool NonNegativeFinite(float value) =>
            Finite(value) && value >= 0f;

        private static bool Finite(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value);

        private static string[] CopyTags(
            IEnumerable<string> values,
            string requiredTag)
        {
            var tags = new HashSet<string>(StringComparer.Ordinal)
            {
                requiredTag
            };
            if (values != null)
                foreach (string value in values)
                {
                    string tag = value?.Trim();
                    if (!string.IsNullOrWhiteSpace(tag)) tags.Add(tag);
                }
            string[] result = new string[tags.Count];
            tags.CopyTo(result);
            Array.Sort(result, StringComparer.Ordinal);
            return result;
        }
    }

    public sealed class OfflineEnemyArchetype
    {
        private readonly string[] targetTags;

        public OfflineEnemyArchetype(
            string stableId,
            string roleTag,
            int threatCost,
            float health,
            float damagePerHit,
            int attackIntervalTicks,
            int firstAttackDelayTicks = 0,
            int ammoDrop = 0,
            bool elite = false,
            IEnumerable<string> targetTags = null)
        {
            if (string.IsNullOrWhiteSpace(stableId))
                throw new ArgumentException(
                    "Enemy ID is required.", nameof(stableId));
            if (threatCost < 1)
                throw new ArgumentOutOfRangeException(nameof(threatCost));
            if (!PositiveFinite(health))
                throw new ArgumentOutOfRangeException(nameof(health));
            if (!NonNegativeFinite(damagePerHit))
                throw new ArgumentOutOfRangeException(nameof(damagePerHit));
            if (attackIntervalTicks < 1)
                throw new ArgumentOutOfRangeException(
                    nameof(attackIntervalTicks));
            if (firstAttackDelayTicks < 0)
                throw new ArgumentOutOfRangeException(
                    nameof(firstAttackDelayTicks));
            if (ammoDrop < 0)
                throw new ArgumentOutOfRangeException(nameof(ammoDrop));

            StableId = stableId.Trim();
            RoleTag = string.IsNullOrWhiteSpace(roleTag)
                ? "assault"
                : roleTag.Trim();
            ThreatCost = threatCost;
            Health = health;
            DamagePerHit = damagePerHit;
            AttackIntervalTicks = attackIntervalTicks;
            FirstAttackDelayTicks = firstAttackDelayTicks;
            AmmoDrop = ammoDrop;
            IsElite = elite;
            this.targetTags = CopyTags(targetTags, RoleTag, elite);
        }

        public string StableId { get; }
        public string RoleTag { get; }
        public int ThreatCost { get; }
        public float Health { get; }
        public float DamagePerHit { get; }
        public int AttackIntervalTicks { get; }
        public int FirstAttackDelayTicks { get; }
        public int AmmoDrop { get; }
        public bool IsElite { get; }
        public IReadOnlyList<string> TargetTags => targetTags;

        private static bool PositiveFinite(float value) =>
            NonNegativeFinite(value) && value > 0f;

        private static bool NonNegativeFinite(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0f;

        private static string[] CopyTags(
            IEnumerable<string> values,
            string roleTag,
            bool elite)
        {
            var tags = new HashSet<string>(StringComparer.Ordinal)
            {
                "entity.enemy",
                "role." + roleTag
            };
            if (elite) tags.Add("enemy.elite");
            if (values != null)
                foreach (string value in values)
                {
                    string tag = value?.Trim();
                    if (!string.IsNullOrWhiteSpace(tag)) tags.Add(tag);
                }
            string[] result = new string[tags.Count];
            tags.CopyTo(result);
            Array.Sort(result, StringComparer.Ordinal);
            return result;
        }
    }

    public readonly struct OfflineWaveEntry
    {
        public OfflineWaveEntry(string enemyTypeId, int count)
        {
            if (string.IsNullOrWhiteSpace(enemyTypeId))
                throw new ArgumentException(
                    "Enemy type ID is required.", nameof(enemyTypeId));
            if (count < 1)
                throw new ArgumentOutOfRangeException(nameof(count));
            EnemyTypeId = enemyTypeId.Trim();
            Count = count;
        }

        public string EnemyTypeId { get; }
        public int Count { get; }
    }

    public sealed class OfflineWaveSpec
    {
        private readonly OfflineWaveEntry[] roster;

        public OfflineWaveSpec(
            int maximumAliveCount,
            int intermissionTicks,
            IReadOnlyList<OfflineWaveEntry> roster)
        {
            if (maximumAliveCount < 1)
                throw new ArgumentOutOfRangeException(
                    nameof(maximumAliveCount));
            if (intermissionTicks < 0)
                throw new ArgumentOutOfRangeException(
                    nameof(intermissionTicks));
            if (roster == null || roster.Count == 0)
                throw new ArgumentException(
                    "A wave requires at least one roster entry.",
                    nameof(roster));

            this.roster = new OfflineWaveEntry[roster.Count];
            int total = 0;
            for (int index = 0; index < roster.Count; index++)
            {
                this.roster[index] = roster[index];
                checked { total += roster[index].Count; }
            }
            MaximumAliveCount = Math.Min(maximumAliveCount, total);
            IntermissionTicks = intermissionTicks;
            TotalEnemyCount = total;
        }

        public int MaximumAliveCount { get; }
        public int IntermissionTicks { get; }
        public int TotalEnemyCount { get; }
        public IReadOnlyList<OfflineWaveEntry> Roster => roster;
    }

    public sealed class OfflineUpgradeSpec
    {
        public OfflineUpgradeSpec(
            string stableId,
            float damageMultiplier = 1f,
            float healthBonus = 0f,
            float armorBonus = 0f,
            int ammoBonus = 0)
        {
            if (string.IsNullOrWhiteSpace(stableId))
                throw new ArgumentException(
                    "Upgrade ID is required.", nameof(stableId));
            if (!PositiveFinite(damageMultiplier))
                throw new ArgumentOutOfRangeException(
                    nameof(damageMultiplier));
            if (!NonNegativeFinite(healthBonus))
                throw new ArgumentOutOfRangeException(nameof(healthBonus));
            if (!NonNegativeFinite(armorBonus))
                throw new ArgumentOutOfRangeException(nameof(armorBonus));
            if (ammoBonus < 0)
                throw new ArgumentOutOfRangeException(nameof(ammoBonus));

            StableId = stableId.Trim();
            DamageMultiplier = damageMultiplier;
            HealthBonus = healthBonus;
            ArmorBonus = armorBonus;
            AmmoBonus = ammoBonus;
        }

        public string StableId { get; }
        public float DamageMultiplier { get; }
        public float HealthBonus { get; }
        public float ArmorBonus { get; }
        public int AmmoBonus { get; }

        private static bool PositiveFinite(float value) =>
            NonNegativeFinite(value) && value > 0f;

        private static bool NonNegativeFinite(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0f;
    }

    public sealed class OfflineCombatBuildSpec
    {
        private readonly CombatRuleDecisionSpec[] rules;

        public OfflineCombatBuildSpec(
            string stableId,
            IReadOnlyList<CombatRuleDecisionSpec> rules)
        {
            if (string.IsNullOrWhiteSpace(stableId))
                throw new ArgumentException(
                    "Combat build ID is required.", nameof(stableId));
            StableId = stableId.Trim();
            if (rules == null || rules.Count == 0)
            {
                this.rules = Array.Empty<CombatRuleDecisionSpec>();
                return;
            }
            this.rules = new CombatRuleDecisionSpec[rules.Count];
            for (int index = 0; index < rules.Count; index++)
                this.rules[index] = rules[index] ??
                    throw new ArgumentException(
                        "Combat build cannot contain a null rule.",
                        nameof(rules));
        }

        public string StableId { get; }
        public IReadOnlyList<CombatRuleDecisionSpec> Rules => rules;
    }

    public sealed class OfflineBalanceScenario
    {
        public const string CurrentRulesVersion = "offline-balance.v1";

        private readonly OfflineWaveSpec[] waves;
        private readonly OfflineEnemyArchetype[] enemies;
        private readonly OfflineUpgradeSpec[] upgrades;
        private readonly EncounterDefinitionSpec[] encounters;
        private readonly OfflineCombatBuildSpec[] combatBuilds;

        public OfflineBalanceScenario(
            string stableId,
            int contentVersion,
            int fixedTickRate,
            IReadOnlyList<OfflineWaveSpec> waves,
            OfflinePlayerSpec player,
            IReadOnlyList<OfflineEnemyArchetype> enemies,
            IReadOnlyList<OfflineUpgradeSpec> upgrades,
            IReadOnlyList<EncounterDefinitionSpec> encounters,
            CombatDirectorConfiguration directorConfiguration,
            int maxTicks = 18000,
            int requiredMissionTargets = 1,
            IReadOnlyList<OfflineCombatBuildSpec> combatBuilds = null,
            OfflineWorldModelSpec worldModel = null)
        {
            if (string.IsNullOrWhiteSpace(stableId))
                throw new ArgumentException(
                    "Scenario ID is required.", nameof(stableId));
            if (contentVersion < 1)
                throw new ArgumentOutOfRangeException(nameof(contentVersion));
            if (fixedTickRate < 1 || fixedTickRate > 1000)
                throw new ArgumentOutOfRangeException(nameof(fixedTickRate));
            if (waves == null || waves.Count == 0)
                throw new ArgumentException(
                    "A scenario requires waves.", nameof(waves));
            if (player == null)
                throw new ArgumentNullException(nameof(player));
            if (enemies == null || enemies.Count == 0)
                throw new ArgumentException(
                    "A scenario requires enemy archetypes.", nameof(enemies));
            if (maxTicks < 1)
                throw new ArgumentOutOfRangeException(nameof(maxTicks));
            if (requiredMissionTargets < 1)
                throw new ArgumentOutOfRangeException(
                    nameof(requiredMissionTargets));

            StableId = stableId.Trim();
            ContentVersion = contentVersion;
            FixedTickRate = fixedTickRate;
            Player = player;
            DirectorConfiguration = directorConfiguration;
            MaxTicks = maxTicks;
            RequiredMissionTargets = requiredMissionTargets;
            WorldModel = worldModel ?? new OfflineWorldModelSpec();

            this.waves = CopyRequired(waves, nameof(waves));
            this.enemies = CopyRequired(enemies, nameof(enemies));
            this.upgrades = CopyOptional(upgrades);
            this.encounters = CopyOptional(encounters);
            this.combatBuilds = CopyOptional(combatBuilds);
            EnsureUniqueContentIds();
        }

        public string StableId { get; }
        public int ContentVersion { get; }
        public string RulesVersion => CurrentRulesVersion;
        public int FixedTickRate { get; }
        public OfflinePlayerSpec Player { get; }
        public CombatDirectorConfiguration DirectorConfiguration { get; }
        public int MaxTicks { get; }
        public int RequiredMissionTargets { get; }
        public OfflineWorldModelSpec WorldModel { get; }
        public IReadOnlyList<OfflineWaveSpec> Waves => waves;
        public IReadOnlyList<OfflineEnemyArchetype> Enemies => enemies;
        public IReadOnlyList<OfflineUpgradeSpec> Upgrades => upgrades;
        public IReadOnlyList<EncounterDefinitionSpec> Encounters => encounters;
        public IReadOnlyList<OfflineCombatBuildSpec> CombatBuilds =>
            combatBuilds;

        internal RunSimulationConfiguration CreateRunConfiguration(long seed)
        {
            var stages = new WaveStageRules[waves.Length];
            for (int index = 0; index < waves.Length; index++)
            {
                OfflineWaveSpec wave = waves[index];
                stages[index] = new WaveStageRules(
                    wave.TotalEnemyCount,
                    wave.MaximumAliveCount,
                    (float)wave.IntermissionTicks / FixedTickRate);
            }
            return new RunSimulationConfiguration(
                seed,
                FixedTickRate,
                stages,
                RequiredMissionTargets);
        }

        private void EnsureUniqueContentIds()
        {
            var enemyIds = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < enemies.Length; index++)
                if (!enemyIds.Add(enemies[index].StableId))
                    throw new ArgumentException(
                        "Enemy IDs must be unique.", nameof(enemies));

            var upgradeIds = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < upgrades.Length; index++)
                if (!upgradeIds.Add(upgrades[index].StableId))
                    throw new ArgumentException(
                        "Upgrade IDs must be unique.", nameof(upgrades));

            var buildIds = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < combatBuilds.Length; index++)
                if (!buildIds.Add(combatBuilds[index].StableId))
                    throw new ArgumentException(
                        "Combat build IDs must be unique.",
                        nameof(combatBuilds));
        }

        private static T[] CopyRequired<T>(
            IReadOnlyList<T> source,
            string parameterName)
            where T : class
        {
            var result = new T[source.Count];
            for (int index = 0; index < source.Count; index++)
            {
                result[index] = source[index] ??
                    throw new ArgumentException(
                        "Scenario content cannot contain null.",
                        parameterName);
            }
            return result;
        }

        private static T[] CopyOptional<T>(IReadOnlyList<T> source)
            where T : class
        {
            if (source == null || source.Count == 0)
                return Array.Empty<T>();
            return CopyRequired(source, nameof(source));
        }
    }

    public readonly struct OfflineNamedCount
    {
        public OfflineNamedCount(string id, int count)
        {
            Id = id ?? string.Empty;
            Count = Math.Max(0, count);
        }

        public string Id { get; }
        public int Count { get; }
    }

    public sealed class OfflineUpgradeChoice
    {
        public OfflineUpgradeChoice(int afterWave, string upgradeId)
        {
            AfterWave = Math.Max(1, afterWave);
            UpgradeId = upgradeId ?? string.Empty;
        }

        public int AfterWave { get; }
        public string UpgradeId { get; }
    }

    public sealed class OfflineDirectorEvent
    {
        public OfflineDirectorEvent(
            long tick,
            CombatDirectorSignal signal,
            long eventId,
            string enemyTypeId,
            string roleTag,
            int requestedCount)
        {
            Tick = Math.Max(0L, tick);
            Signal = signal;
            EventId = Math.Max(0L, eventId);
            EnemyTypeId = enemyTypeId ?? string.Empty;
            RoleTag = roleTag ?? string.Empty;
            RequestedCount = Math.Max(0, requestedCount);
        }

        public long Tick { get; }
        public CombatDirectorSignal Signal { get; }
        public long EventId { get; }
        public string EnemyTypeId { get; }
        public string RoleTag { get; }
        public int RequestedCount { get; }
    }

    public sealed class OfflineEncounterEvent
    {
        public OfflineEncounterEvent(
            long tick,
            string encounterId,
            EncounterSignal signal,
            EncounterPhase phase,
            int progress,
            int target)
        {
            Tick = Math.Max(0L, tick);
            EncounterId = encounterId ?? string.Empty;
            Signal = signal;
            Phase = phase;
            Progress = Math.Max(0, progress);
            Target = Math.Max(0, target);
        }

        public long Tick { get; }
        public string EncounterId { get; }
        public EncounterSignal Signal { get; }
        public EncounterPhase Phase { get; }
        public int Progress { get; }
        public int Target { get; }
    }

    public sealed class OfflineCombatRuleEvent
    {
        public OfflineCombatRuleEvent(
            long eventId,
            long tick,
            CombatRuleDecisionTrigger trigger,
            string ruleId,
            string sourceId,
            string targetId)
        {
            EventId = Math.Max(0L, eventId);
            Tick = Math.Max(0L, tick);
            Trigger = trigger;
            RuleId = ruleId ?? string.Empty;
            SourceId = sourceId ?? string.Empty;
            TargetId = targetId ?? string.Empty;
        }

        public long EventId { get; }
        public long Tick { get; }
        public CombatRuleDecisionTrigger Trigger { get; }
        public string RuleId { get; }
        public string SourceId { get; }
        public string TargetId { get; }
    }

    public sealed class OfflineWaveResult
    {
        private readonly OfflineNamedCount[] roleDistribution;

        public OfflineWaveResult(
            int waveNumber,
            int spawnedCount,
            int defeatedCount,
            long durationTicks,
            float averageTtkTicks,
            IReadOnlyList<OfflineNamedCount> roleDistribution)
        {
            WaveNumber = Math.Max(1, waveNumber);
            SpawnedCount = Math.Max(0, spawnedCount);
            DefeatedCount = Math.Max(0, defeatedCount);
            DurationTicks = Math.Max(0L, durationTicks);
            AverageTtkTicks = Math.Max(0f, averageTtkTicks);
            this.roleDistribution = Copy(roleDistribution);
        }

        public int WaveNumber { get; }
        public int SpawnedCount { get; }
        public int DefeatedCount { get; }
        public long DurationTicks { get; }
        public float AverageTtkTicks { get; }
        public IReadOnlyList<OfflineNamedCount> RoleDistribution =>
            roleDistribution;

        private static OfflineNamedCount[] Copy(
            IReadOnlyList<OfflineNamedCount> source)
        {
            if (source == null || source.Count == 0)
                return Array.Empty<OfflineNamedCount>();
            var result = new OfflineNamedCount[source.Count];
            for (int index = 0; index < source.Count; index++)
                result[index] = source[index];
            return result;
        }
    }

    public sealed class OfflineRunResult
    {
        private readonly OfflineWaveResult[] waves;
        private readonly OfflineNamedCount[] waveDistribution;
        private readonly OfflineNamedCount[] roleDistribution;
        private readonly OfflineNamedCount[] encounterDistribution;
        private readonly OfflineNamedCount[] combatRuleTriggerDistribution;
        private readonly int[] enemyTtkTicks;
        private readonly OfflineUpgradeChoice[] upgradeChoices;
        private readonly OfflineDirectorEvent[] directorEvents;
        private readonly OfflineEncounterEvent[] encounterEvents;
        private readonly OfflineCombatRuleEvent[] combatRuleEvents;
        private readonly SimulationCommand[] commands;

        internal OfflineRunResult(
            string scenarioId,
            int contentVersion,
            string rulesVersion,
            int worldModelVersion,
            int enemyAttackOpportunityBasisPoints,
            long seed,
            OfflineRunOutcome outcome,
            OfflineFailureReason failureReason,
            string failureDetail,
            long durationTicks,
            int fixedTickRate,
            float averageTtkTicks,
            IReadOnlyList<int> enemyTtkTicks,
            float healthLost,
            float armorLost,
            int ammoConsumed,
            int ammoSupplied,
            IReadOnlyList<OfflineWaveResult> waves,
            IReadOnlyList<OfflineNamedCount> waveDistribution,
            IReadOnlyList<OfflineNamedCount> roleDistribution,
            IReadOnlyList<OfflineNamedCount> encounterDistribution,
            IReadOnlyList<OfflineNamedCount> combatRuleTriggerDistribution,
            IReadOnlyList<OfflineUpgradeChoice> upgradeChoices,
            IReadOnlyList<OfflineDirectorEvent> directorEvents,
            IReadOnlyList<OfflineEncounterEvent> encounterEvents,
            IReadOnlyList<OfflineCombatRuleEvent> combatRuleEvents,
            IReadOnlyList<SimulationCommand> commands,
            string digest)
        {
            ScenarioId = scenarioId ?? string.Empty;
            ContentVersion = contentVersion;
            RulesVersion = rulesVersion ?? string.Empty;
            WorldModelVersion = Math.Max(1, worldModelVersion);
            EnemyAttackOpportunityBasisPoints = Math.Max(
                0,
                Math.Min(10000, enemyAttackOpportunityBasisPoints));
            Seed = seed;
            Outcome = outcome;
            FailureReason = failureReason;
            FailureDetail = failureDetail ?? string.Empty;
            DurationTicks = Math.Max(0L, durationTicks);
            FixedTickRate = Math.Max(1, fixedTickRate);
            AverageTtkTicks = Math.Max(0f, averageTtkTicks);
            this.enemyTtkTicks = CopyNonNegative(enemyTtkTicks);
            HealthLost = Math.Max(0f, healthLost);
            ArmorLost = Math.Max(0f, armorLost);
            AmmoConsumed = Math.Max(0, ammoConsumed);
            AmmoSupplied = Math.Max(0, ammoSupplied);
            this.waves = Copy(waves);
            this.waveDistribution = Copy(waveDistribution);
            this.roleDistribution = Copy(roleDistribution);
            this.encounterDistribution = Copy(encounterDistribution);
            this.combatRuleTriggerDistribution = Copy(
                combatRuleTriggerDistribution);
            this.upgradeChoices = Copy(upgradeChoices);
            this.directorEvents = Copy(directorEvents);
            this.encounterEvents = Copy(encounterEvents);
            this.combatRuleEvents = Copy(combatRuleEvents);
            this.commands = Copy(commands);
            Digest = digest ?? string.Empty;
        }

        public string ScenarioId { get; }
        public int ContentVersion { get; }
        public string RulesVersion { get; }
        public int WorldModelVersion { get; }
        public int EnemyAttackOpportunityBasisPoints { get; }
        public long Seed { get; }
        public OfflineRunOutcome Outcome { get; }
        public OfflineFailureReason FailureReason { get; }
        public string FailureDetail { get; }
        public long DurationTicks { get; }
        public int FixedTickRate { get; }
        public float DurationSeconds => (float)DurationTicks / FixedTickRate;
        public float AverageTtkTicks { get; }
        public float AverageTtkSeconds => AverageTtkTicks / FixedTickRate;
        public IReadOnlyList<int> EnemyTtkTicks => enemyTtkTicks;
        public float HealthLost { get; }
        public float ArmorLost { get; }
        public int AmmoConsumed { get; }
        public int AmmoSupplied { get; }
        public IReadOnlyList<OfflineWaveResult> Waves => waves;
        public IReadOnlyList<OfflineNamedCount> WaveDistribution =>
            waveDistribution;
        public IReadOnlyList<OfflineNamedCount> RoleDistribution =>
            roleDistribution;
        public IReadOnlyList<OfflineNamedCount> EncounterDistribution =>
            encounterDistribution;
        public IReadOnlyList<OfflineNamedCount> CombatRuleTriggerDistribution =>
            combatRuleTriggerDistribution;
        public IReadOnlyList<OfflineUpgradeChoice> UpgradeChoices =>
            upgradeChoices;
        public IReadOnlyList<OfflineDirectorEvent> DirectorEvents =>
            directorEvents;
        public IReadOnlyList<OfflineEncounterEvent> EncounterEvents =>
            encounterEvents;
        public IReadOnlyList<OfflineCombatRuleEvent> CombatRuleEvents =>
            combatRuleEvents;
        public bool CombatRuleEffectsExpanded => false;
        public string CombatRuleAbstractionNote =>
            "规则判定与正式玩法一致；Unity 状态效果和物理表现未在抽象世界模型中展开。";
        public IReadOnlyList<SimulationCommand> Commands => commands;
        public string Digest { get; }

        private static T[] Copy<T>(IReadOnlyList<T> source)
        {
            if (source == null || source.Count == 0)
                return Array.Empty<T>();
            var result = new T[source.Count];
            for (int index = 0; index < source.Count; index++)
                result[index] = source[index];
            return result;
        }

        private static int[] CopyNonNegative(IReadOnlyList<int> source)
        {
            if (source == null || source.Count == 0)
                return Array.Empty<int>();
            var result = new int[source.Count];
            for (int index = 0; index < source.Count; index++)
                result[index] = Math.Max(0, source[index]);
            return result;
        }
    }

    public sealed class OfflineStateDivergence
    {
        public OfflineStateDivergence(
            int commandIndex,
            long tick,
            string reason)
        {
            CommandIndex = Math.Max(0, commandIndex);
            Tick = Math.Max(0L, tick);
            Reason = reason ?? string.Empty;
        }

        public int CommandIndex { get; }
        public long Tick { get; }
        public string Reason { get; }
    }

    public sealed class OfflineReplayVerification
    {
        internal OfflineReplayVerification(
            bool isMatch,
            string expectedDigest,
            string actualDigest,
            OfflineStateDivergence firstDivergence)
        {
            IsMatch = isMatch;
            ExpectedDigest = expectedDigest ?? string.Empty;
            ActualDigest = actualDigest ?? string.Empty;
            FirstDivergence = firstDivergence;
        }

        public bool IsMatch { get; }
        public string ExpectedDigest { get; }
        public string ActualDigest { get; }
        public OfflineStateDivergence FirstDivergence { get; }
    }

    public sealed class OfflineBatchResult : IReadOnlyList<OfflineRunResult>
    {
        private readonly OfflineRunResult[] results;

        internal OfflineBatchResult(
            string scenarioId,
            int contentVersion,
            string rulesVersion,
            OfflineRunResult[] results)
        {
            ScenarioId = scenarioId ?? string.Empty;
            ContentVersion = contentVersion;
            RulesVersion = rulesVersion ?? string.Empty;
            this.results = results == null
                ? Array.Empty<OfflineRunResult>()
                : (OfflineRunResult[])results.Clone();
            Digest = OfflineResultDigest.ComputeBatch(
                ScenarioId,
                ContentVersion,
                RulesVersion,
                this.results);
        }

        public string ScenarioId { get; }
        public int ContentVersion { get; }
        public string RulesVersion { get; }
        public string Digest { get; }
        public int Count => results.Length;
        public OfflineRunResult this[int index] => results[index];

        public IEnumerator<OfflineRunResult> GetEnumerator()
        {
            return ((IEnumerable<OfflineRunResult>)results).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return results.GetEnumerator();
        }
    }
}
