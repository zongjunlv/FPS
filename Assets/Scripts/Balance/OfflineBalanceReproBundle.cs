using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using FPS.Simulation;
using FPS.Simulation.Offline;
using UnityEngine;

namespace FPS.Balance
{
    /// <summary>
    /// Self-contained anomaly reproduction package. It deliberately contains
    /// pure simulation input rather than scene references, so replaying it
    /// cannot mutate the active run or a save slot.
    /// </summary>
    [DataContract]
    public sealed class OfflineBalanceReproBundle
    {
        public const string CurrentSchemaVersion =
            "issue63.balance-reproduction.v1";

        [DataMember(Name = "schemaVersion", Order = 0, IsRequired = true)]
        public string SchemaVersion = CurrentSchemaVersion;

        [DataMember(Name = "seed", Order = 1, IsRequired = true)]
        public long Seed;

        [DataMember(Name = "scenarioId", Order = 2, IsRequired = true)]
        public string ScenarioId = string.Empty;

        [DataMember(Name = "rulesVersion", Order = 3, IsRequired = true)]
        public string RulesVersion = string.Empty;

        [DataMember(Name = "policyVersion", Order = 4, IsRequired = true)]
        public string PolicyVersion = string.Empty;

        [DataMember(Name = "contentVersion", Order = 5, IsRequired = true)]
        public int ContentVersion;

        [DataMember(Name = "contentFingerprint", Order = 6, IsRequired = true)]
        public string ContentFingerprint = string.Empty;

        [DataMember(Name = "expectedDigest", Order = 7, IsRequired = true)]
        public string ExpectedDigest = string.Empty;

        [DataMember(Name = "actualDigest", Order = 8, IsRequired = true)]
        public string ActualDigest = string.Empty;

        [DataMember(Name = "anomalyCodes", Order = 9, IsRequired = true)]
        public string[] AnomalyCodes = Array.Empty<string>();

        [DataMember(
            Name = "firstDivergence",
            Order = 10,
            EmitDefaultValue = false)]
        public OfflineBalanceReproDivergenceData FirstDivergence;

        [DataMember(Name = "commands", Order = 11, IsRequired = true)]
        public OfflineBalanceReproCommandData[] Commands =
            Array.Empty<OfflineBalanceReproCommandData>();

        [DataMember(
            Name = "authoritativeCommandStream",
            Order = 12,
            IsRequired = true)]
        public string AuthoritativeCommandStream = string.Empty;

        [DataMember(Name = "scenario", Order = 13, IsRequired = true)]
        public OfflineBalanceReproScenarioData Scenario =
            new OfflineBalanceReproScenarioData();

        [DataMember(Name = "worldModelVersion", Order = 14, IsRequired = true)]
        public int WorldModelVersion;

        [DataMember(
            Name = "enemyAttackOpportunityBasisPoints",
            Order = 15,
            IsRequired = true)]
        public int EnemyAttackOpportunityBasisPoints;

        public static OfflineBalanceReproBundle Create(
            OfflineBalanceScenario scenario,
            OfflineRunResult expected,
            string policyVersion,
            string contentFingerprint,
            IReadOnlyList<string> anomalyCodes,
            OfflineReplayVerification verification = null)
        {
            if (scenario == null)
                throw new ArgumentNullException(nameof(scenario));
            if (expected == null)
                throw new ArgumentNullException(nameof(expected));
            if (scenario.StableId != expected.ScenarioId ||
                scenario.ContentVersion != expected.ContentVersion ||
                scenario.RulesVersion != expected.RulesVersion ||
                scenario.RulesVersion != OfflineBalanceScenario.CurrentRulesVersion)
            {
                throw new InvalidDataException(
                    "The expected result does not belong to this scenario.");
            }

            var bundle = new OfflineBalanceReproBundle
            {
                Seed = expected.Seed,
                ScenarioId = scenario.StableId,
                RulesVersion = scenario.RulesVersion,
                PolicyVersion = policyVersion ?? string.Empty,
                ContentVersion = scenario.ContentVersion,
                ContentFingerprint = contentFingerprint ?? string.Empty,
                ExpectedDigest = expected.Digest,
                ActualDigest = verification?.ActualDigest ?? expected.Digest,
                AnomalyCodes = CopyStrings(anomalyCodes),
                Commands = CopyCommands(expected.Commands),
                AuthoritativeCommandStream =
                    SimulationCommandCodec.Serialize(expected.Commands),
                Scenario = OfflineBalanceReproScenarioData.From(scenario),
                WorldModelVersion = expected.WorldModelVersion,
                EnemyAttackOpportunityBasisPoints =
                    expected.EnemyAttackOpportunityBasisPoints
            };
            if (verification?.FirstDivergence != null)
            {
                bundle.FirstDivergence =
                    OfflineBalanceReproDivergenceData.From(
                        verification.FirstDivergence);
            }
            return bundle;
        }

        private static string[] CopyStrings(IReadOnlyList<string> values)
        {
            if (values == null || values.Count == 0)
                return Array.Empty<string>();
            var result = new string[values.Count];
            for (int index = 0; index < values.Count; index++)
                result[index] = values[index] ?? string.Empty;
            return result;
        }

        private static OfflineBalanceReproCommandData[] CopyCommands(
            IReadOnlyList<SimulationCommand> commands)
        {
            var result = new OfflineBalanceReproCommandData[commands.Count];
            for (int index = 0; index < commands.Count; index++)
                result[index] = OfflineBalanceReproCommandData.From(
                    commands[index]);
            return result;
        }
    }

    [DataContract]
    public sealed class OfflineBalanceReproDivergenceData
    {
        [DataMember(Name = "commandIndex", Order = 0, IsRequired = true)]
        public int CommandIndex;

        [DataMember(Name = "tick", Order = 1, IsRequired = true)]
        public long Tick;

        [DataMember(Name = "reason", Order = 2, IsRequired = true)]
        public string Reason = string.Empty;

        internal static OfflineBalanceReproDivergenceData From(
            OfflineStateDivergence divergence)
        {
            return new OfflineBalanceReproDivergenceData
            {
                CommandIndex = divergence.CommandIndex,
                Tick = divergence.Tick,
                Reason = divergence.Reason
            };
        }
    }

    [DataContract]
    public sealed class OfflineBalanceReproCommandData
    {
        [DataMember(Name = "tick", Order = 0, IsRequired = true)]
        public long Tick;

        [DataMember(Name = "type", Order = 1, IsRequired = true)]
        public string Type = string.Empty;

        [DataMember(Name = "entityId", Order = 2, IsRequired = true)]
        public int EntityId;

        [DataMember(Name = "primaryValue", Order = 3, IsRequired = true)]
        public float PrimaryValue;

        [DataMember(Name = "secondaryValue", Order = 4, IsRequired = true)]
        public float SecondaryValue;

        internal static OfflineBalanceReproCommandData From(
            SimulationCommand command)
        {
            return new OfflineBalanceReproCommandData
            {
                Tick = command.Tick,
                Type = command.Type.ToString(),
                EntityId = command.EntityId,
                PrimaryValue = command.PrimaryValue,
                SecondaryValue = command.SecondaryValue
            };
        }

        internal SimulationCommand ToCommand()
        {
            if (!Enum.TryParse(Type, true, out SimulationCommandType type))
            {
                throw new InvalidDataException(
                    "Unknown simulation command type: " + Type);
            }
            return SimulationCommand.Create(
                Tick,
                type,
                EntityId,
                PrimaryValue,
                SecondaryValue);
        }
    }

    [DataContract]
    public sealed class OfflineBalanceReproScenarioData
    {
        [DataMember(Name = "stableId", Order = 0, IsRequired = true)]
        public string StableId = string.Empty;

        [DataMember(Name = "contentVersion", Order = 1, IsRequired = true)]
        public int ContentVersion;

        [DataMember(Name = "fixedTickRate", Order = 2, IsRequired = true)]
        public int FixedTickRate;

        [DataMember(Name = "maxTicks", Order = 3, IsRequired = true)]
        public int MaxTicks;

        [DataMember(
            Name = "requiredMissionTargets",
            Order = 4,
            IsRequired = true)]
        public int RequiredMissionTargets;

        [DataMember(Name = "player", Order = 5, IsRequired = true)]
        public OfflineBalanceReproPlayerData Player =
            new OfflineBalanceReproPlayerData();

        [DataMember(Name = "enemies", Order = 6, IsRequired = true)]
        public OfflineBalanceReproEnemyData[] Enemies =
            Array.Empty<OfflineBalanceReproEnemyData>();

        [DataMember(Name = "waves", Order = 7, IsRequired = true)]
        public OfflineBalanceReproWaveData[] Waves =
            Array.Empty<OfflineBalanceReproWaveData>();

        [DataMember(Name = "upgrades", Order = 8, IsRequired = true)]
        public OfflineBalanceReproUpgradeData[] Upgrades =
            Array.Empty<OfflineBalanceReproUpgradeData>();

        [DataMember(Name = "encounters", Order = 9, IsRequired = true)]
        public OfflineBalanceReproEncounterData[] Encounters =
            Array.Empty<OfflineBalanceReproEncounterData>();

        [DataMember(Name = "director", Order = 10, IsRequired = true)]
        public OfflineBalanceReproDirectorData Director =
            new OfflineBalanceReproDirectorData();

        [DataMember(Name = "combatBuilds", Order = 11, IsRequired = true)]
        public OfflineBalanceReproCombatBuildData[] CombatBuilds =
            Array.Empty<OfflineBalanceReproCombatBuildData>();

        [DataMember(Name = "worldModel", Order = 12, IsRequired = true)]
        public OfflineBalanceReproWorldModelData WorldModel =
            new OfflineBalanceReproWorldModelData();

        internal static OfflineBalanceReproScenarioData From(
            OfflineBalanceScenario scenario)
        {
            return new OfflineBalanceReproScenarioData
            {
                StableId = scenario.StableId,
                ContentVersion = scenario.ContentVersion,
                FixedTickRate = scenario.FixedTickRate,
                MaxTicks = scenario.MaxTicks,
                RequiredMissionTargets = scenario.RequiredMissionTargets,
                Player = OfflineBalanceReproPlayerData.From(scenario.Player),
                Enemies = Convert(
                    scenario.Enemies,
                    OfflineBalanceReproEnemyData.From),
                Waves = Convert(
                    scenario.Waves,
                    OfflineBalanceReproWaveData.From),
                Upgrades = Convert(
                    scenario.Upgrades,
                    OfflineBalanceReproUpgradeData.From),
                Encounters = Convert(
                    scenario.Encounters,
                    OfflineBalanceReproEncounterData.From),
                Director = OfflineBalanceReproDirectorData.From(
                    scenario.DirectorConfiguration),
                CombatBuilds = Convert(
                    scenario.CombatBuilds,
                    OfflineBalanceReproCombatBuildData.From),
                WorldModel = OfflineBalanceReproWorldModelData.From(
                    scenario.WorldModel)
            };
        }

        internal OfflineBalanceScenario ToScenario()
        {
            return new OfflineBalanceScenario(
                StableId,
                ContentVersion,
                FixedTickRate,
                Convert(Waves, item => item.ToWave()),
                Player.ToPlayer(),
                Convert(Enemies, item => item.ToEnemy()),
                Convert(Upgrades, item => item.ToUpgrade()),
                Convert(Encounters, item => item.ToEncounter()),
                Director.ToDirector(),
                MaxTicks,
                RequiredMissionTargets,
                Convert(CombatBuilds, item => item.ToBuild()),
                WorldModel.ToWorldModel());
        }

        private static TOutput[] Convert<TInput, TOutput>(
            IReadOnlyList<TInput> source,
            Func<TInput, TOutput> converter)
        {
            if (source == null || source.Count == 0)
                return Array.Empty<TOutput>();
            var result = new TOutput[source.Count];
            for (int index = 0; index < source.Count; index++)
                result[index] = converter(source[index]);
            return result;
        }
    }

    [DataContract]
    public sealed class OfflineBalanceReproWorldModelData
    {
        [DataMember(Name = "version", Order = 0, IsRequired = true)]
        public int Version = OfflineWorldModelSpec.CurrentVersion;

        [DataMember(
            Name = "enemyAttackOpportunityBasisPoints",
            Order = 1,
            IsRequired = true)]
        public int EnemyAttackOpportunityBasisPoints = 10000;

        internal static OfflineBalanceReproWorldModelData From(
            OfflineWorldModelSpec value)
        {
            return new OfflineBalanceReproWorldModelData
            {
                Version = value.Version,
                EnemyAttackOpportunityBasisPoints =
                    value.EnemyAttackOpportunityBasisPoints
            };
        }

        internal OfflineWorldModelSpec ToWorldModel()
        {
            return new OfflineWorldModelSpec(
                EnemyAttackOpportunityBasisPoints,
                Version);
        }
    }

    [DataContract]
    public sealed class OfflineBalanceReproCombatBuildData
    {
        [DataMember(Name = "stableId", Order = 0, IsRequired = true)]
        public string StableId = string.Empty;

        [DataMember(Name = "rules", Order = 1, IsRequired = true)]
        public OfflineBalanceReproCombatRuleData[] Rules =
            Array.Empty<OfflineBalanceReproCombatRuleData>();

        internal static OfflineBalanceReproCombatBuildData From(
            OfflineCombatBuildSpec value)
        {
            var rules = new OfflineBalanceReproCombatRuleData[
                value.Rules.Count];
            for (int index = 0; index < rules.Length; index++)
                rules[index] = OfflineBalanceReproCombatRuleData.From(
                    value.Rules[index]);
            return new OfflineBalanceReproCombatBuildData
            {
                StableId = value.StableId,
                Rules = rules
            };
        }

        internal OfflineCombatBuildSpec ToBuild()
        {
            var rules = new CombatRuleDecisionSpec[Rules.Length];
            for (int index = 0; index < rules.Length; index++)
                rules[index] = Rules[index].ToRule();
            return new OfflineCombatBuildSpec(StableId, rules);
        }
    }

    [DataContract]
    public sealed class OfflineBalanceReproCombatRuleData
    {
        [DataMember(Name = "stableId", Order = 0, IsRequired = true)]
        public string StableId = string.Empty;
        [DataMember(Name = "trigger", Order = 1, IsRequired = true)]
        public CombatRuleDecisionTrigger Trigger;
        [DataMember(Name = "requiredSourceTags", Order = 2, IsRequired = true)]
        public string[] RequiredSourceTags = Array.Empty<string>();
        [DataMember(Name = "requiredTargetTags", Order = 3, IsRequired = true)]
        public string[] RequiredTargetTags = Array.Empty<string>();
        [DataMember(Name = "excludedTargetTags", Order = 4, IsRequired = true)]
        public string[] ExcludedTargetTags = Array.Empty<string>();
        [DataMember(
            Name = "minimumHealthNormalized",
            Order = 5,
            IsRequired = true)]
        public float MinimumHealthNormalized;
        [DataMember(
            Name = "maximumHealthNormalized",
            Order = 6,
            IsRequired = true)]
        public float MaximumHealthNormalized = 1f;
        [DataMember(Name = "cooldownTicks", Order = 7, IsRequired = true)]
        public int CooldownTicks;
        [DataMember(
            Name = "probabilityBasisPoints",
            Order = 8,
            IsRequired = true)]
        public int ProbabilityBasisPoints;
        [DataMember(
            Name = "hasExecutableEffects",
            Order = 9,
            IsRequired = true)]
        public bool HasExecutableEffects = true;

        internal static OfflineBalanceReproCombatRuleData From(
            CombatRuleDecisionSpec value)
        {
            return new OfflineBalanceReproCombatRuleData
            {
                StableId = value.StableId,
                Trigger = value.Trigger,
                RequiredSourceTags = Copy(value.RequiredSourceTags),
                RequiredTargetTags = Copy(value.RequiredTargetTags),
                ExcludedTargetTags = Copy(value.ExcludedTargetTags),
                MinimumHealthNormalized = value.MinimumHealthNormalized,
                MaximumHealthNormalized = value.MaximumHealthNormalized,
                CooldownTicks = value.CooldownTicks,
                ProbabilityBasisPoints = value.ProbabilityBasisPoints,
                HasExecutableEffects = value.HasExecutableEffects
            };
        }

        internal CombatRuleDecisionSpec ToRule()
        {
            return new CombatRuleDecisionSpec(
                StableId,
                Trigger,
                RequiredSourceTags,
                RequiredTargetTags,
                ExcludedTargetTags,
                MinimumHealthNormalized,
                MaximumHealthNormalized,
                CooldownTicks,
                ProbabilityBasisPoints,
                HasExecutableEffects);
        }

        private static string[] Copy(IReadOnlyList<string> values)
        {
            var result = new string[values.Count];
            for (int index = 0; index < values.Count; index++)
                result[index] = values[index];
            return result;
        }
    }

    [DataContract]
    public sealed class OfflineBalanceReproPlayerData
    {
        [DataMember(Name = "health", Order = 0, IsRequired = true)]
        public float Health;
        [DataMember(Name = "armor", Order = 1, IsRequired = true)]
        public float Armor;
        [DataMember(Name = "damagePerShot", Order = 2, IsRequired = true)]
        public float DamagePerShot;
        [DataMember(Name = "shotIntervalTicks", Order = 3, IsRequired = true)]
        public int ShotIntervalTicks;
        [DataMember(Name = "startingAmmo", Order = 4, IsRequired = true)]
        public int StartingAmmo;
        [DataMember(Name = "hitChance", Order = 5, IsRequired = true)]
        public float HitChance;
        [DataMember(Name = "sourceTags", Order = 6, IsRequired = true)]
        public string[] SourceTags = Array.Empty<string>();

        internal static OfflineBalanceReproPlayerData From(
            OfflinePlayerSpec value)
        {
            return new OfflineBalanceReproPlayerData
            {
                Health = value.Health,
                Armor = value.Armor,
                DamagePerShot = value.DamagePerShot,
                ShotIntervalTicks = value.ShotIntervalTicks,
                StartingAmmo = value.StartingAmmo,
                HitChance = value.HitChance,
                SourceTags = Copy(value.SourceTags)
            };
        }

        internal OfflinePlayerSpec ToPlayer()
        {
            return new OfflinePlayerSpec(
                Health,
                Armor,
                DamagePerShot,
                ShotIntervalTicks,
                StartingAmmo,
                HitChance,
                SourceTags);
        }

        private static string[] Copy(IReadOnlyList<string> values)
        {
            var result = new string[values.Count];
            for (int index = 0; index < values.Count; index++)
                result[index] = values[index];
            return result;
        }
    }

    [DataContract]
    public sealed class OfflineBalanceReproEnemyData
    {
        [DataMember(Name = "stableId", Order = 0, IsRequired = true)]
        public string StableId = string.Empty;
        [DataMember(Name = "roleTag", Order = 1, IsRequired = true)]
        public string RoleTag = string.Empty;
        [DataMember(Name = "threatCost", Order = 2, IsRequired = true)]
        public int ThreatCost;
        [DataMember(Name = "health", Order = 3, IsRequired = true)]
        public float Health;
        [DataMember(Name = "damagePerHit", Order = 4, IsRequired = true)]
        public float DamagePerHit;
        [DataMember(
            Name = "attackIntervalTicks",
            Order = 5,
            IsRequired = true)]
        public int AttackIntervalTicks;
        [DataMember(
            Name = "firstAttackDelayTicks",
            Order = 6,
            IsRequired = true)]
        public int FirstAttackDelayTicks;
        [DataMember(Name = "ammoDrop", Order = 7, IsRequired = true)]
        public int AmmoDrop;
        [DataMember(Name = "elite", Order = 8, IsRequired = true)]
        public bool Elite;
        [DataMember(Name = "targetTags", Order = 9, IsRequired = true)]
        public string[] TargetTags = Array.Empty<string>();

        internal static OfflineBalanceReproEnemyData From(
            OfflineEnemyArchetype value)
        {
            return new OfflineBalanceReproEnemyData
            {
                StableId = value.StableId,
                RoleTag = value.RoleTag,
                ThreatCost = value.ThreatCost,
                Health = value.Health,
                DamagePerHit = value.DamagePerHit,
                AttackIntervalTicks = value.AttackIntervalTicks,
                FirstAttackDelayTicks = value.FirstAttackDelayTicks,
                AmmoDrop = value.AmmoDrop,
                Elite = value.IsElite,
                TargetTags = Copy(value.TargetTags)
            };
        }

        internal OfflineEnemyArchetype ToEnemy()
        {
            return new OfflineEnemyArchetype(
                StableId,
                RoleTag,
                ThreatCost,
                Health,
                DamagePerHit,
                AttackIntervalTicks,
                FirstAttackDelayTicks,
                AmmoDrop,
                Elite,
                TargetTags);
        }

        private static string[] Copy(IReadOnlyList<string> values)
        {
            var result = new string[values.Count];
            for (int index = 0; index < values.Count; index++)
                result[index] = values[index];
            return result;
        }
    }

    [DataContract]
    public sealed class OfflineBalanceReproWaveEntryData
    {
        [DataMember(Name = "enemyTypeId", Order = 0, IsRequired = true)]
        public string EnemyTypeId = string.Empty;
        [DataMember(Name = "count", Order = 1, IsRequired = true)]
        public int Count;

        internal static OfflineBalanceReproWaveEntryData From(
            OfflineWaveEntry value)
        {
            return new OfflineBalanceReproWaveEntryData
            {
                EnemyTypeId = value.EnemyTypeId,
                Count = value.Count
            };
        }

        internal OfflineWaveEntry ToEntry()
        {
            return new OfflineWaveEntry(EnemyTypeId, Count);
        }
    }

    [DataContract]
    public sealed class OfflineBalanceReproWaveData
    {
        [DataMember(
            Name = "maximumAliveCount",
            Order = 0,
            IsRequired = true)]
        public int MaximumAliveCount;
        [DataMember(
            Name = "intermissionTicks",
            Order = 1,
            IsRequired = true)]
        public int IntermissionTicks;
        [DataMember(Name = "roster", Order = 2, IsRequired = true)]
        public OfflineBalanceReproWaveEntryData[] Roster =
            Array.Empty<OfflineBalanceReproWaveEntryData>();

        internal static OfflineBalanceReproWaveData From(OfflineWaveSpec value)
        {
            var roster = new OfflineBalanceReproWaveEntryData[
                value.Roster.Count];
            for (int index = 0; index < roster.Length; index++)
                roster[index] = OfflineBalanceReproWaveEntryData.From(
                    value.Roster[index]);
            return new OfflineBalanceReproWaveData
            {
                MaximumAliveCount = value.MaximumAliveCount,
                IntermissionTicks = value.IntermissionTicks,
                Roster = roster
            };
        }

        internal OfflineWaveSpec ToWave()
        {
            var roster = new OfflineWaveEntry[Roster.Length];
            for (int index = 0; index < roster.Length; index++)
                roster[index] = Roster[index].ToEntry();
            return new OfflineWaveSpec(
                MaximumAliveCount,
                IntermissionTicks,
                roster);
        }
    }

    [DataContract]
    public sealed class OfflineBalanceReproUpgradeData
    {
        [DataMember(Name = "stableId", Order = 0, IsRequired = true)]
        public string StableId = string.Empty;
        [DataMember(Name = "damageMultiplier", Order = 1, IsRequired = true)]
        public float DamageMultiplier = 1f;
        [DataMember(Name = "healthBonus", Order = 2, IsRequired = true)]
        public float HealthBonus;
        [DataMember(Name = "armorBonus", Order = 3, IsRequired = true)]
        public float ArmorBonus;
        [DataMember(Name = "ammoBonus", Order = 4, IsRequired = true)]
        public int AmmoBonus;

        internal static OfflineBalanceReproUpgradeData From(
            OfflineUpgradeSpec value)
        {
            return new OfflineBalanceReproUpgradeData
            {
                StableId = value.StableId,
                DamageMultiplier = value.DamageMultiplier,
                HealthBonus = value.HealthBonus,
                ArmorBonus = value.ArmorBonus,
                AmmoBonus = value.AmmoBonus
            };
        }

        internal OfflineUpgradeSpec ToUpgrade()
        {
            return new OfflineUpgradeSpec(
                StableId,
                DamageMultiplier,
                HealthBonus,
                ArmorBonus,
                AmmoBonus);
        }
    }

    [DataContract]
    public sealed class OfflineBalanceReproEncounterData
    {
        [DataMember(Name = "stableId", Order = 0, IsRequired = true)]
        public string StableId = string.Empty;
        [DataMember(Name = "version", Order = 1, IsRequired = true)]
        public int Version;
        [DataMember(Name = "kind", Order = 2, IsRequired = true)]
        public EncounterKind Kind;
        [DataMember(Name = "displayName", Order = 3, IsRequired = true)]
        public string DisplayName = string.Empty;
        [DataMember(Name = "objectiveText", Order = 4, IsRequired = true)]
        public string ObjectiveText = string.Empty;
        [DataMember(Name = "triggerKind", Order = 5, IsRequired = true)]
        public EncounterTriggerKind TriggerKind;
        [DataMember(Name = "triggerWaveNumber", Order = 6, IsRequired = true)]
        public int TriggerWaveNumber;
        [DataMember(Name = "triggerMissionState", Order = 7, IsRequired = true)]
        public MissionFlowState TriggerMissionState;
        [DataMember(Name = "objectiveKind", Order = 8, IsRequired = true)]
        public EncounterObjectiveKind ObjectiveKind;
        [DataMember(Name = "objectiveTarget", Order = 9, IsRequired = true)]
        public int ObjectiveTarget;
        [DataMember(Name = "mainFlowPolicy", Order = 10, IsRequired = true)]
        public EncounterMainFlowPolicy MainFlowPolicy;
        [DataMember(Name = "introTicks", Order = 11, IsRequired = true)]
        public int IntroTicks;
        [DataMember(Name = "timeLimitTicks", Order = 12, IsRequired = true)]
        public int TimeLimitTicks;

        internal static OfflineBalanceReproEncounterData From(
            EncounterDefinitionSpec value)
        {
            return new OfflineBalanceReproEncounterData
            {
                StableId = value.StableId,
                Version = value.Version,
                Kind = value.Kind,
                DisplayName = value.DisplayName,
                ObjectiveText = value.ObjectiveText,
                TriggerKind = value.Trigger.Kind,
                TriggerWaveNumber = value.Trigger.WaveNumber,
                TriggerMissionState = value.Trigger.MissionState,
                ObjectiveKind = value.Objective.Kind,
                ObjectiveTarget = value.Objective.Target,
                MainFlowPolicy = value.MainFlowPolicy,
                IntroTicks = value.IntroTicks,
                TimeLimitTicks = value.TimeLimitTicks
            };
        }

        internal EncounterDefinitionSpec ToEncounter()
        {
            EncounterTrigger trigger = TriggerKind ==
                EncounterTriggerKind.WaveReached
                ? EncounterTrigger.Wave(TriggerWaveNumber)
                : EncounterTrigger.Mission(TriggerMissionState);
            EncounterObjective objective = ObjectiveKind switch
            {
                EncounterObjectiveKind.EliminateEnemies =>
                    EncounterObjective.Eliminate(ObjectiveTarget),
                EncounterObjectiveKind.EliminateElite =>
                    EncounterObjective.EliminateElite(),
                EncounterObjectiveKind.HoldArea =>
                    EncounterObjective.HoldTicks(ObjectiveTarget),
                EncounterObjectiveKind.ReachExtraction =>
                    EncounterObjective.ReachExtraction(),
                _ => throw new InvalidDataException(
                    "Unknown encounter objective kind: " + ObjectiveKind)
            };
            return new EncounterDefinitionSpec(
                StableId,
                Version,
                Kind,
                DisplayName,
                ObjectiveText,
                trigger,
                objective,
                MainFlowPolicy,
                IntroTicks,
                TimeLimitTicks);
        }
    }

    [DataContract]
    public sealed class OfflineBalanceReproDirectorData
    {
        [DataMember(Name = "initialDelayTicks", Order = 0, IsRequired = true)]
        public long InitialDelayTicks;
        [DataMember(
            Name = "evaluationIntervalTicks",
            Order = 1,
            IsRequired = true)]
        public long EvaluationIntervalTicks;
        [DataMember(Name = "warningTicks", Order = 2, IsRequired = true)]
        public long WarningTicks;
        [DataMember(Name = "cooldownTicks", Order = 3, IsRequired = true)]
        public long CooldownTicks;
        [DataMember(
            Name = "maximumReinforcements",
            Order = 4,
            IsRequired = true)]
        public int MaximumReinforcements;
        [DataMember(
            Name = "maximumIntensityDelta",
            Order = 5,
            IsRequired = true)]
        public int MaximumIntensityDelta;
        [DataMember(
            Name = "minimumCandidateScore",
            Order = 6,
            IsRequired = true)]
        public float MinimumCandidateScore;

        internal static OfflineBalanceReproDirectorData From(
            CombatDirectorConfiguration value)
        {
            return new OfflineBalanceReproDirectorData
            {
                InitialDelayTicks = value.InitialDelayTicks,
                EvaluationIntervalTicks = value.EvaluationIntervalTicks,
                WarningTicks = value.WarningTicks,
                CooldownTicks = value.CooldownTicks,
                MaximumReinforcements = value.MaximumReinforcements,
                MaximumIntensityDelta = value.MaximumIntensityDelta,
                MinimumCandidateScore = value.MinimumCandidateScore
            };
        }

        internal CombatDirectorConfiguration ToDirector()
        {
            return new CombatDirectorConfiguration(
                InitialDelayTicks,
                EvaluationIntervalTicks,
                WarningTicks,
                CooldownTicks,
                MaximumReinforcements,
                MaximumIntensityDelta,
                MinimumCandidateScore);
        }
    }

    public static class OfflineBalanceReproBundleCodec
    {
        public static string Serialize(OfflineBalanceReproBundle bundle)
        {
            if (bundle == null) throw new ArgumentNullException(nameof(bundle));
            using var stream = new MemoryStream();
            CreateSerializer().WriteObject(stream, bundle);
            return Encoding.UTF8.GetString(stream.ToArray());
        }

        public static OfflineBalanceReproBundle Deserialize(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                throw new InvalidDataException("The reproduction package is empty.");
            try
            {
                using var stream = new MemoryStream(
                    Encoding.UTF8.GetBytes(json));
                var bundle = (OfflineBalanceReproBundle)
                    CreateSerializer().ReadObject(stream);
                Validate(bundle);
                return bundle;
            }
            catch (SerializationException exception)
            {
                throw new InvalidDataException(
                    "The reproduction package cannot be decoded.",
                    exception);
            }
        }

        public static void Save(string path, OfflineBalanceReproBundle bundle)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("A path is required.", nameof(path));
            string fullPath = Path.GetFullPath(path);
            string directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            string temporaryPath = fullPath + ".tmp";
            File.WriteAllText(temporaryPath, Serialize(bundle), Encoding.UTF8);
            if (File.Exists(fullPath)) File.Delete(fullPath);
            File.Move(temporaryPath, fullPath);
        }

        public static OfflineBalanceReproBundle Load(string path)
        {
            return Deserialize(File.ReadAllText(path, Encoding.UTF8));
        }

        private static DataContractJsonSerializer CreateSerializer()
        {
            return new DataContractJsonSerializer(
                typeof(OfflineBalanceReproBundle));
        }

        private static void Validate(OfflineBalanceReproBundle bundle)
        {
            if (bundle == null)
                throw new InvalidDataException("The reproduction package is null.");
            if (bundle.SchemaVersion !=
                OfflineBalanceReproBundle.CurrentSchemaVersion)
            {
                throw new InvalidDataException(
                    "Unsupported reproduction schema: " +
                    bundle.SchemaVersion);
            }
            if (bundle.Scenario == null)
                throw new InvalidDataException(
                    "The reproduction package has no scenario.");
            if (string.IsNullOrWhiteSpace(bundle.ExpectedDigest))
                throw new InvalidDataException(
                    "The reproduction package has no expected digest.");
            bundle.AnomalyCodes ??= Array.Empty<string>();
            bundle.Commands ??= Array.Empty<OfflineBalanceReproCommandData>();
        }
    }

    public static class OfflineBalanceReproStorage
    {
        public const string DirectoryName = "Issue63Balance";
        public const string LatestFileName = "latest-reproduction.json";

        public static string LatestPath => Path.Combine(
            Application.persistentDataPath,
            DirectoryName,
            LatestFileName);

        public static void SaveLatest(OfflineBalanceReproBundle bundle)
        {
            OfflineBalanceReproBundleCodec.Save(LatestPath, bundle);
        }

        public static bool TryLoadLatest(
            out OfflineBalanceReproBundle bundle,
            out string sourcePath,
            out string error)
        {
            sourcePath = LatestPath;
            bundle = null;
            error = string.Empty;
            if (!File.Exists(sourcePath))
            {
                error = "尚未生成异常 Seed 复现包。请先运行离线平衡门禁。";
                return false;
            }
            try
            {
                bundle = OfflineBalanceReproBundleCodec.Load(sourcePath);
                return true;
            }
            catch (Exception exception)
            {
                error = "异常 Seed 复现包无效：" + exception.Message;
                return false;
            }
        }
    }

    public sealed class OfflineBalanceReproVerification
    {
        internal OfflineBalanceReproVerification(
            OfflineBalanceReproBundle bundle,
            string sourcePath,
            bool isMatch,
            string actualDigest,
            OfflineBalanceReproDivergenceData firstDivergence)
        {
            Bundle = bundle;
            SourcePath = sourcePath ?? string.Empty;
            IsMatch = isMatch;
            ActualDigest = actualDigest ?? string.Empty;
            FirstDivergence = firstDivergence;
        }

        public OfflineBalanceReproBundle Bundle { get; }
        public string SourcePath { get; }
        public bool IsMatch { get; }
        public string ActualDigest { get; }
        public OfflineBalanceReproDivergenceData FirstDivergence { get; }
    }

    public static class OfflineBalanceReproRuntime
    {
        public static bool TryVerifyLatest(
            out OfflineBalanceReproVerification verification,
            out string error)
        {
            if (!OfflineBalanceReproStorage.TryLoadLatest(
                    out OfflineBalanceReproBundle bundle,
                    out string sourcePath,
                    out error))
            {
                verification = null;
                return false;
            }
            try
            {
                verification = Verify(bundle, sourcePath);
                error = string.Empty;
                return true;
            }
            catch (Exception exception)
            {
                verification = null;
                error = "异常 Seed 权威离线复现失败：" + exception.Message;
                return false;
            }
        }

        public static OfflineBalanceReproVerification Verify(
            OfflineBalanceReproBundle bundle,
            string sourcePath = "")
        {
            if (bundle == null) throw new ArgumentNullException(nameof(bundle));
            OfflineBalanceScenario scenario = bundle.Scenario.ToScenario();
            if (bundle.ScenarioId != scenario.StableId ||
                bundle.ContentVersion != scenario.ContentVersion ||
                bundle.WorldModelVersion != scenario.WorldModel.Version ||
                bundle.EnemyAttackOpportunityBasisPoints !=
                scenario.WorldModel.EnemyAttackOpportunityBasisPoints)
            {
                return FailedIdentity(
                    bundle,
                    sourcePath,
                    "场景、内容版本或世界模型参数与复现输入不一致。");
            }
            if (bundle.RulesVersion != scenario.RulesVersion)
            {
                return FailedIdentity(
                    bundle,
                    sourcePath,
                    "规则版本与当前离线模拟器不一致。");
            }

            IReadOnlyList<SimulationCommand> expectedCommands =
                ReadExpectedCommands(bundle);
            OfflineRunResult actual = OfflineBalanceSimulator.Run(
                scenario,
                bundle.Seed);
            if (actual.Digest == bundle.ExpectedDigest)
            {
                return new OfflineBalanceReproVerification(
                    bundle,
                    sourcePath,
                    true,
                    actual.Digest,
                    null);
            }

            OfflineBalanceReproDivergenceData divergence =
                FindFirstDivergence(
                    expectedCommands,
                    actual.Commands,
                    actual.DurationTicks);
            return new OfflineBalanceReproVerification(
                bundle,
                sourcePath,
                false,
                actual.Digest,
                divergence);
        }

        private static OfflineBalanceReproVerification FailedIdentity(
            OfflineBalanceReproBundle bundle,
            string sourcePath,
            string reason)
        {
            return new OfflineBalanceReproVerification(
                bundle,
                sourcePath,
                false,
                string.Empty,
                new OfflineBalanceReproDivergenceData
                {
                    CommandIndex = 0,
                    Tick = 0,
                    Reason = reason
                });
        }

        private static IReadOnlyList<SimulationCommand> ReadExpectedCommands(
            OfflineBalanceReproBundle bundle)
        {
            IReadOnlyList<SimulationCommand> commands;
            if (!string.IsNullOrWhiteSpace(bundle.AuthoritativeCommandStream))
            {
                commands = SimulationCommandCodec.Deserialize(
                    bundle.AuthoritativeCommandStream);
            }
            else
            {
                var reconstructed = new SimulationCommand[
                    bundle.Commands?.Length ?? 0];
                for (int index = 0; index < reconstructed.Length; index++)
                    reconstructed[index] = bundle.Commands[index].ToCommand();
                commands = reconstructed;
            }

            if (commands.Count == 0)
            {
                throw new InvalidDataException(
                    "复现包缺少权威命令流，无法定位真实分歧。");
            }
            return commands;
        }

        private static OfflineBalanceReproDivergenceData FindFirstDivergence(
            IReadOnlyList<SimulationCommand> expected,
            IReadOnlyList<SimulationCommand> actual,
            long actualDurationTicks)
        {
            int common = Math.Min(expected.Count, actual.Count);
            for (int index = 0; index < common; index++)
            {
                if (CommandsEqual(expected[index], actual[index])) continue;
                return new OfflineBalanceReproDivergenceData
                {
                    CommandIndex = index,
                    Tick = Math.Min(expected[index].Tick, actual[index].Tick),
                    Reason = "首条权威命令不一致。"
                };
            }

            if (expected.Count != actual.Count)
            {
                long tick = common == 0
                    ? 0
                    : expected.Count > common
                        ? expected[common].Tick
                        : actual[common].Tick;
                return new OfflineBalanceReproDivergenceData
                {
                    CommandIndex = common,
                    Tick = tick,
                    Reason = "权威命令流长度不一致。"
                };
            }

            return new OfflineBalanceReproDivergenceData
            {
                CommandIndex = common,
                Tick = actualDurationTicks,
                Reason = "权威命令一致，但衍生平衡指标摘要不同。"
            };
        }

        private static bool CommandsEqual(
            SimulationCommand expected,
            SimulationCommand actual)
        {
            return expected != null && actual != null &&
                   expected.Tick == actual.Tick &&
                   expected.Type == actual.Type &&
                   expected.EntityId == actual.EntityId &&
                   SameBits(expected.PrimaryValue, actual.PrimaryValue) &&
                   SameBits(expected.SecondaryValue, actual.SecondaryValue);
        }

        private static bool SameBits(float left, float right)
        {
            return BitConverter.SingleToInt32Bits(left) ==
                   BitConverter.SingleToInt32Bits(right);
        }
    }
}
