using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using FPS.GameplayEffects;
using FPS.Simulation;
using FPS.Simulation.Offline;
using UnityEditor;
using UnityEngine;

namespace FPS.Editor.Balance
{
    /// <summary>
    /// Immutable, Unity-free input for one worker. All ScriptableObject reads
    /// happen before this object is handed to a background thread.
    /// </summary>
    public sealed class Issue63PreparedSeed
    {
        internal Issue63PreparedSeed(long seed, OfflineBalanceScenario scenario)
        {
            Seed = seed;
            Scenario = scenario ?? throw new ArgumentNullException(nameof(scenario));
        }

        public long Seed { get; }
        public OfflineBalanceScenario Scenario { get; }
    }

    public sealed class Issue63PreparedBatch
    {
        private readonly Issue63PreparedSeed[] entries;

        internal Issue63PreparedBatch(
            string contentFingerprint,
            int contentVersion,
            string policyVersion,
            Issue63PreparedSeed[] preparedEntries)
        {
            ContentFingerprint = contentFingerprint ?? string.Empty;
            ContentVersion = Math.Max(1, contentVersion);
            PolicyVersion = policyVersion ?? string.Empty;
            entries = preparedEntries ?? Array.Empty<Issue63PreparedSeed>();
        }

        public string ContentFingerprint { get; }
        public int ContentVersion { get; }
        public string PolicyVersion { get; }
        public IReadOnlyList<Issue63PreparedSeed> Entries => entries;
    }

    /// <summary>
    /// Projects the shipped CityNew content into the pure offline model. Wave
    /// rosters are resolved through WaveDefinition.PrepareForRun, which uses
    /// the same ThreatBudgetWaveComposer and named random stream as gameplay.
    /// </summary>
    public static class CityNewOfflineBalanceContentAdapter
    {
        public const int FixedTickRate = 60;
        public const string PolicyVersion =
            "city-new.offline-projection.v2";

        private const int MaximumTicks = FixedTickRate * 300;
        private const float DefaultEnemyAttackCooldownSeconds = 1.35f;
        private const float DefaultHitChance = 0.9f;
        private const float StartingArmor = 100f;
        // CityNew normally permits three simultaneously living enemies. The
        // pure model has no NavMesh, cover, line of sight or attack range, so
        // its named random stream approximates one effective attacker per
        // cooldown window (1 / 3) without changing formal per-hit damage.
        public const int EnemyAttackOpportunityBasisPoints = 3333;

        public static Issue63PreparedBatch PrepareDefault(
            IReadOnlyList<long> seeds)
        {
            CityNewContentCatalog catalog = CityNewContentCatalog.LoadDefault();
            if (catalog == null)
            {
                throw new InvalidOperationException(
                    "Cannot load the default CityNew content catalog at Resources/" +
                    CityNewContentCatalog.DefaultResourcePath + ".");
            }

            return Prepare(catalog, seeds);
        }

        public static Issue63PreparedBatch Prepare(
            CityNewContentCatalog catalog,
            IReadOnlyList<long> seeds)
        {
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            if (seeds == null) throw new ArgumentNullException(nameof(seeds));
            if (!catalog.TryValidate(out string error))
            {
                throw new InvalidOperationException(
                    "CityNew content is invalid: " + error);
            }

            long[] seedSnapshot = seeds.ToArray();
            if (seedSnapshot.Distinct().Count() != seedSnapshot.Length)
            {
                throw new ArgumentException(
                    "Offline balance seeds must be unique.", nameof(seeds));
            }

            WeaponDefinition weapon = FindPrimaryWeapon();
            OfflineEnemyArchetype[] enemies = ProjectEnemies(catalog);
            OfflineUpgradeSpec[] upgrades = ProjectUpgrades(catalog, weapon);
            EncounterDefinitionSpec[] encounters = ProjectEncounters(catalog);
            OfflineCombatBuildSpec[] combatBuilds =
                ProjectCombatBuilds(catalog);
            OfflinePlayerSpec player = ProjectPlayer(weapon);
            int contentVersion = ResolveContentVersion(catalog);
            string fingerprint = ComputeContentFingerprint(catalog, weapon);
            var entries = new Issue63PreparedSeed[seedSnapshot.Length];

            try
            {
                // PrepareForRun mutates only the cached resolved roster. Doing
                // this serially on the main thread preserves Unity safety while
                // each resulting OfflineWaveSpec remains immutable.
                for (int index = 0; index < seedSnapshot.Length; index++)
                {
                    long seed = seedSnapshot[index];
                    OfflineWaveSpec[] waves = ProjectWaves(
                        catalog.WaveSequence,
                        unchecked((int)seed));
                    entries[index] = new Issue63PreparedSeed(
                        seed,
                        new OfflineBalanceScenario(
                            catalog.StableId,
                            contentVersion,
                            FixedTickRate,
                            waves,
                            player,
                            enemies,
                            upgrades,
                            encounters,
                            CreateDirectorConfiguration(),
                            MaximumTicks,
                            1,
                            combatBuilds,
                            new OfflineWorldModelSpec(
                                EnemyAttackOpportunityBasisPoints)));
                }
            }
            finally
            {
                RestoreEditorWaveCaches(catalog.WaveSequence);
            }

            return new Issue63PreparedBatch(
                fingerprint,
                contentVersion,
                PolicyVersion,
                entries);
        }

        private static OfflineEnemyArchetype[] ProjectEnemies(
            CityNewContentCatalog catalog)
        {
            EnemyDefinition defaults = catalog.DefaultEnemy;
            var result = new List<OfflineEnemyArchetype>(
                catalog.EnemyArchetypes.Count);

            for (int index = 0; index < catalog.EnemyArchetypes.Count; index++)
            {
                EnemyArchetypeDefinition archetype =
                    catalog.EnemyArchetypes[index];
                if (archetype == null) continue;

                float armorEquivalent = EvaluateAffix(
                    archetype.Affix,
                    GameplayAttributeId.EnemyMaximumArmor,
                    0f);
                float attack = EvaluateAffix(
                    archetype.Affix,
                    GameplayAttributeId.EnemyAttackDamage,
                    Mathf.Max(1f, defaults.Attack));
                int attackInterval = Mathf.Max(
                    1,
                    Mathf.RoundToInt(
                        ResolveAttackCooldown(archetype) * FixedTickRate));

                result.Add(new OfflineEnemyArchetype(
                    archetype.StableId,
                    archetype.RoleTag,
                    archetype.ThreatCost,
                    Mathf.Max(1f, defaults.HP) +
                    Mathf.Max(0f, armorEquivalent),
                    Mathf.Max(0f, attack),
                    attackInterval,
                    attackInterval,
                    0,
                    archetype.RewardTier == LootRewardTier.Elite,
                    new[] { "enemy.alive" }));
            }

            result.Sort((left, right) => string.CompareOrdinal(
                left.StableId,
                right.StableId));
            return result.ToArray();
        }

        private static float ResolveAttackCooldown(
            EnemyArchetypeDefinition archetype)
        {
            EnemyAbilitySetDefinition set = archetype.AbilitySet;
            if (set == null) return DefaultEnemyAttackCooldownSeconds;

            RaiderApproachAbilityDefinition raider =
                set.FindAbility<RaiderApproachAbilityDefinition>();
            if (raider != null) return raider.AttackCooldown;

            SuppressorRangedAbilityDefinition suppressor =
                set.FindAbility<SuppressorRangedAbilityDefinition>();
            return suppressor != null
                ? suppressor.AttackCooldown
                : DefaultEnemyAttackCooldownSeconds;
        }

        private static float EvaluateAffix(
            EnemyAffixDefinition affix,
            GameplayAttributeId attribute,
            float baseValue)
        {
            IReadOnlyList<GameplayEffectModifier> modifiers =
                affix?.GameplayEffect?.Modifiers;
            if (modifiers == null || modifiers.Count == 0) return baseValue;

            float additive = 0f;
            float multiplicativeBonus = 0f;
            GameplayEffectModifier? selectedOverride = null;
            for (int index = 0; index < modifiers.Count; index++)
            {
                GameplayEffectModifier modifier = modifiers[index];
                if (modifier.Attribute != attribute ||
                    float.IsNaN(modifier.Magnitude) ||
                    float.IsInfinity(modifier.Magnitude))
                {
                    continue;
                }

                switch (modifier.Operation)
                {
                    case GameplayModifierOperation.Add:
                        additive += modifier.Magnitude;
                        break;
                    case GameplayModifierOperation.Multiply:
                        multiplicativeBonus += modifier.Magnitude;
                        break;
                    case GameplayModifierOperation.Override:
                        if (!selectedOverride.HasValue ||
                            modifier.Priority > selectedOverride.Value.Priority)
                        {
                            selectedOverride = modifier;
                        }
                        break;
                }
            }

            return selectedOverride?.Magnitude ??
                   (baseValue + additive) * Mathf.Max(0f, 1f + multiplicativeBonus);
        }

        private static OfflineUpgradeSpec[] ProjectUpgrades(
            CityNewContentCatalog catalog,
            WeaponDefinition weapon)
        {
            var result = new List<OfflineUpgradeSpec>(catalog.Upgrades.Count);
            float baseHealth = 100f;
            float baseArmor = StartingArmor;
            int baseAmmo = weapon.MagazineCapacity + weapon.InitialReserveAmmo;

            for (int index = 0; index < catalog.Upgrades.Count; index++)
            {
                UpgradeDefinition definition = catalog.Upgrades[index];
                if (definition == null) continue;

                float damageMultiplier = 1f;
                float healthBonus = 0f;
                float armorBonus = 0f;
                int ammoBonus = 0;
                switch (definition.EffectType)
                {
                    case UpgradeEffectType.WeaponDamage:
                        damageMultiplier += definition.EffectAmount;
                        break;
                    case UpgradeEffectType.MaximumHealth:
                        healthBonus = baseHealth * definition.EffectAmount;
                        break;
                    case UpgradeEffectType.MaximumArmor:
                        armorBonus = baseArmor * definition.EffectAmount;
                        break;
                    case UpgradeEffectType.HealthRestore:
                        healthBonus = definition.EffectAmount;
                        break;
                    case UpgradeEffectType.ArmorRestore:
                        armorBonus = definition.EffectAmount;
                        break;
                    case UpgradeEffectType.WeaponMagazineCapacity:
                        ammoBonus = Mathf.RoundToInt(
                            baseAmmo * definition.EffectAmount);
                        break;
                }

                result.Add(new OfflineUpgradeSpec(
                    definition.StableId,
                    damageMultiplier,
                    healthBonus,
                    armorBonus,
                    ammoBonus));
            }

            result.Sort((left, right) => string.CompareOrdinal(
                left.StableId,
                right.StableId));
            return result.ToArray();
        }

        private static EncounterDefinitionSpec[] ProjectEncounters(
            CityNewContentCatalog catalog)
        {
            EncounterSequenceDefinition sequence = catalog.EncounterSequence;
            if (sequence == null || sequence.Encounters == null)
                return Array.Empty<EncounterDefinitionSpec>();

            var result = new List<EncounterDefinitionSpec>(sequence.Count);
            for (int index = 0; index < sequence.Count; index++)
            {
                EncounterDefinition definition = sequence.Encounters[index];
                if (definition != null) result.Add(definition.ToSpec(FixedTickRate));
            }

            return result.ToArray();
        }

        private static OfflinePlayerSpec ProjectPlayer(WeaponDefinition weapon)
        {
            return new OfflinePlayerSpec(
                100f,
                StartingArmor,
                Mathf.Max(0f, weapon.Damage),
                Mathf.Max(1, Mathf.RoundToInt(
                    Mathf.Max(0.01f, weapon.FireIntervel) * FixedTickRate)),
                Mathf.Max(0,
                    weapon.MagazineCapacity + weapon.InitialReserveAmmo),
                DefaultHitChance,
                new[] { "entity.player" });
        }

        private static OfflineCombatBuildSpec[] ProjectCombatBuilds(
            CityNewContentCatalog catalog)
        {
            var builds = new List<OfflineCombatBuildSpec>();
            for (int buildIndex = 0;
                 buildIndex < catalog.CombatBuilds.Count;
                 buildIndex++)
            {
                CombatBuildDefinition build = catalog.CombatBuilds[buildIndex];
                if (build == null || !build.InstallOnRunStart) continue;

                var rules = new List<CombatRuleDecisionSpec>();
                for (int ruleIndex = 0;
                     ruleIndex < build.Rules.Count;
                     ruleIndex++)
                {
                    CombatRuleDefinition rule = build.Rules[ruleIndex];
                    if (rule == null) continue;
                    if (!Enum.TryParse(
                            rule.Trigger.ToString(),
                            out CombatRuleDecisionTrigger trigger))
                    {
                        throw new InvalidOperationException(
                            "Unsupported combat rule trigger '" +
                            rule.Trigger + "'.");
                    }

                    bool hasExecutableEffects = rule.Effects.Any(
                        effect => effect != null);
                    rules.Add(new CombatRuleDecisionSpec(
                        rule.StableId,
                        trigger,
                        rule.RequiredSourceTags,
                        rule.RequiredTargetTags,
                        rule.ExcludedTargetTags,
                        rule.MinimumHealthNormalized,
                        rule.MaximumHealthNormalized,
                        rule.CooldownTicks,
                        rule.ProbabilityBasisPoints,
                        hasExecutableEffects));
                }

                builds.Add(new OfflineCombatBuildSpec(
                    build.StableId,
                    rules));
            }

            builds.Sort((left, right) => string.CompareOrdinal(
                left.StableId,
                right.StableId));
            return builds.ToArray();
        }

        private static OfflineWaveSpec[] ProjectWaves(
            WaveSequenceDefinition sequence,
            int runSeed)
        {
            var result = new OfflineWaveSpec[sequence.WaveCount];
            for (int waveIndex = 0; waveIndex < sequence.WaveCount; waveIndex++)
            {
                WaveStageDefinition stage = sequence.GetStage(waveIndex);
                WaveDefinition wave = stage.Wave;
                wave.PrepareForRun(runSeed);

                var roster = new List<OfflineWaveEntry>(wave.TotalEnemyCount);
                for (int spawnIndex = 0;
                     spawnIndex < wave.TotalEnemyCount;
                     spawnIndex++)
                {
                    WaveEnemyEntry selected = wave.GetEntry(spawnIndex);
                    if (selected == null) continue;
                    string id = selected.Archetype != null
                        ? selected.Archetype.StableId
                        : selected.EnemyTypeId;
                    // Preserve the formal spawn order. Grouping equal IDs would
                    // change which role enters an occupied wave slot first.
                    roster.Add(new OfflineWaveEntry(id, 1));
                }

                if (roster.Count == 0)
                {
                    throw new InvalidOperationException(
                        "Resolved CityNew wave '" + wave.StableId +
                        "' has no enemies for seed " + runSeed + ".");
                }

                result[waveIndex] = new OfflineWaveSpec(
                    wave.MaximumAliveCount,
                    Mathf.RoundToInt(
                        stage.IntermissionAfterSeconds * FixedTickRate),
                    roster);
            }

            return result;
        }

        private static void RestoreEditorWaveCaches(
            WaveSequenceDefinition sequence)
        {
            if (sequence == null) return;
            for (int index = 0; index < sequence.WaveCount; index++)
            {
                WaveDefinition wave = sequence.GetStage(index).Wave;
                if (wave != null) wave.PrepareForRun(wave.CompositionSeed);
            }
        }

        private static CombatDirectorConfiguration CreateDirectorConfiguration()
        {
            return new CombatDirectorConfiguration(
                FixedTickRate / 2,
                FixedTickRate / 2,
                FixedTickRate * 3 / 2,
                FixedTickRate * 12,
                1,
                1,
                0.55f);
        }

        private static int ResolveContentVersion(CityNewContentCatalog catalog)
        {
            int encounterVersion = catalog.EncounterSequence != null
                ? catalog.EncounterSequence.ContentVersion
                : 1;
            int layoutVersion = catalog.LayoutSet != null
                ? catalog.LayoutSet.ContentVersion
                : 1;
            return Math.Max(encounterVersion, layoutVersion);
        }

        private static WeaponDefinition FindPrimaryWeapon()
        {
            string[] guids = AssetDatabase.FindAssets("t:WeaponDefinition");
            Array.Sort(guids, StringComparer.Ordinal);
            WeaponDefinition fallback = null;
            for (int index = 0; index < guids.Length; index++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[index]);
                WeaponDefinition definition =
                    AssetDatabase.LoadAssetAtPath<WeaponDefinition>(path);
                if (definition == null) continue;
                fallback ??= definition;
                if (string.Equals(
                        definition.StableId,
                        "weapon.rifle",
                        StringComparison.Ordinal))
                {
                    return definition;
                }
            }

            return fallback ?? throw new InvalidOperationException(
                "Issue 63 requires at least one WeaponDefinition asset.");
        }

        private static string ComputeContentFingerprint(
            CityNewContentCatalog catalog,
            WeaponDefinition weapon)
        {
            string catalogPath = AssetDatabase.GetAssetPath(catalog);
            string weaponPath = AssetDatabase.GetAssetPath(weapon);
            string canonical =
                "catalog=" + AssetDatabase.AssetPathToGUID(catalogPath) +
                ":" + AssetDatabase.GetAssetDependencyHash(catalogPath) +
                "|weapon=" + AssetDatabase.AssetPathToGUID(weaponPath) +
                ":" + AssetDatabase.GetAssetDependencyHash(weaponPath) +
                "|policy=" + PolicyVersion;
            using SHA256 hash = SHA256.Create();
            byte[] digest = hash.ComputeHash(Encoding.UTF8.GetBytes(canonical));
            var text = new StringBuilder(digest.Length * 2);
            for (int index = 0; index < digest.Length; index++)
                text.Append(digest[index].ToString("x2"));
            return text.ToString();
        }
    }
}
