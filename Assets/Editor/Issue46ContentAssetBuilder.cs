using System;
using System.Collections.Generic;
using FPS.GameplayEffects;
using FPS.Simulation;
using UnityEditor;
using UnityEngine;

public static class Issue46ContentAssetBuilder
{
    private const string Root = "Assets/Resources/Content/CityNew";

    [MenuItem("FPS/Content/Rebuild CityNew Default Assets")]
    public static void Build()
    {
        EnsureFolders();

        EnemyDefinition enemy = AssetDatabase.LoadAssetAtPath<EnemyDefinition>(
            "Assets/DefineAssets/Enemy/Spider.asset");
        if (enemy == null)
        {
            throw new InvalidOperationException(
                "Spider enemy definition asset is missing.");
        }

        enemy.Configure("spider_bot", "Spider", 50f, 25f, 1f, 40);
        EditorUtility.SetDirty(enemy);

        GameplayEffectDefinition eliteEffect =
            Asset<GameplayEffectDefinition>(
                "Effects/ArmoredEliteEffect.asset");
        eliteEffect.Configure(
            "enemy.affix.armored_elite",
            new GameplayEffectModifier(
                GameplayAttributeId.EnemyMaximumArmor,
                GameplayModifierOperation.Add,
                60f),
            new GameplayEffectModifier(
                GameplayAttributeId.EnemyAttackDamage,
                GameplayModifierOperation.Multiply,
                0.5f),
            new GameplayEffectModifier(
                GameplayAttributeId.EnemyExperienceReward,
                GameplayModifierOperation.Multiply,
                1f),
            new GameplayEffectModifier(
                GameplayAttributeId.EnemyLootQuantity,
                GameplayModifierOperation.Multiply,
                0.5f));

        GameplayEffectDefinition supportEffect =
            Asset<GameplayEffectDefinition>(
                "Effects/SupportAttackBuff.asset");
        supportEffect.Configure(
            "enemy.buff.support_attack",
            new GameplayEffectModifier(
                GameplayAttributeId.EnemyAttackDamage,
                GameplayModifierOperation.Multiply,
                0.25f));
        supportEffect.ConfigureTags(
            "effect.enemy.support_attack", "buff.support", "target.enemy");

        GameplayEffectDefinition vitalityEffect =
            Asset<GameplayEffectDefinition>(
                "Effects/UpgradeMaximumHealth.asset");
        vitalityEffect.Configure(
            "upgrade.maximum_health",
            new GameplayEffectModifier(
                GameplayAttributeId.MaximumHealth,
                GameplayModifierOperation.Multiply,
                0.2f));

        GameplayEffectDefinition medicalEffect =
            Asset<GameplayEffectDefinition>("Effects/MedicalKit.asset");
        medicalEffect.ConfigureInstant(
            "item.medical_kit",
            new GameplayEffectModifier(
                GameplayAttributeId.CurrentHealth,
                GameplayModifierOperation.Add,
                35f));

        GameplayEffectDefinition armorEffect =
            Asset<GameplayEffectDefinition>("Effects/ArmorPack.asset");
        armorEffect.ConfigureInstant(
            "item.armor_pack",
            new GameplayEffectModifier(
                GameplayAttributeId.CurrentArmor,
                GameplayModifierOperation.Add,
                35f));

        GameplayEffectDefinition burnEffect =
            Asset<GameplayEffectDefinition>("Effects/EnemyBurn.asset");
        burnEffect.ConfigureTimed(
            "status.burn",
            4f,
            1f,
            4f,
            3,
            GameplayEffectStackRefreshPolicy.RefreshAllDurations);
        burnEffect.ConfigureTags(
            "status.burning",
            "status.damage_over_time",
            "target.enemy");
        CombatBuildDefinition emberBuild = BuildEmberChain(burnEffect);

        RaiderApproachAbilityDefinition raiderAbility =
            Asset<RaiderApproachAbilityDefinition>(
                "Enemies/Abilities/RaiderFlank.asset");
        raiderAbility.Configure(
            "enemy.ability.raider_flank",
            5.5f, 2.5f, 2f, 4.75f, 1.35f, 1.75f,
            1.5f, 0.8f, 2.3f, 0.25f, 0.9f, 0.8f);

        SuppressorRangedAbilityDefinition suppressorAbility =
            Asset<SuppressorRangedAbilityDefinition>(
                "Enemies/Abilities/SuppressorRanged.asset");
        suppressorAbility.Configure(
            "enemy.ability.suppressor_ranged",
            6f, 10f, 14f, 4f, 2f, 1.15f,
            1.5f, 0.5f, 0.45f, 1.2f, 0.65f, 280f);

        EnemySupportAuraAbilityDefinition supportAbility =
            Asset<EnemySupportAuraAbilityDefinition>(
                "Enemies/Abilities/SupportAura.asset");
        supportAbility.Configure(
            "enemy.ability.support_aura",
            10f, 2, 3f, 2f,
            EnemySupportTargetPriority.LowestHealthRatio,
            "enemy",
            supportEffect);

        EnemyAbilitySetDefinition raiderSet =
            AbilitySet(
                "Enemies/AbilitySets/SpiderRaider.asset",
                "enemy.role.spider_raider",
                "RAIDER",
                new Color(0.1f, 0.9f, 1f, 1f),
                raiderAbility);
        EnemyAbilitySetDefinition suppressorSet =
            AbilitySet(
                "Enemies/AbilitySets/SpiderSuppressor.asset",
                "enemy.role.spider_suppressor",
                "SUPPRESSOR",
                new Color(1f, 0.28f, 0.08f, 1f),
                suppressorAbility);
        EnemyAbilitySetDefinition supportSet =
            AbilitySet(
                "Enemies/AbilitySets/SpiderSupport.asset",
                "enemy.role.spider_support",
                "SUPPORT",
                new Color(0.35f, 1f, 0.42f, 1f),
                supportAbility);

        EnemyAffixDefinition eliteAffix =
            Asset<EnemyAffixDefinition>(
                "Enemies/Affixes/ArmoredElite.asset");
        eliteAffix.Configure(
            "armored_elite",
            "ELITE ARMOR",
            new Color(1f, 0.72f, 0.12f, 1f),
            eliteEffect);

        EnemyArchetypeDefinition raider = Archetype(
            "Enemies/Archetypes/SpiderRaider.asset",
            "enemy.archetype.spider_raider", "spider_raider",
            LootRewardTier.Normal, null, raiderSet, 3,
            "raider");
        EnemyArchetypeDefinition suppressor = Archetype(
            "Enemies/Archetypes/SpiderSuppressor.asset",
            "enemy.archetype.spider_suppressor", "spider_suppressor",
            LootRewardTier.Normal, null,
            suppressorSet, 3, "suppressor");
        EnemyArchetypeDefinition support = Archetype(
            "Enemies/Archetypes/SpiderSupport.asset",
            "enemy.archetype.spider_support", "spider_support",
            LootRewardTier.Normal, null, supportSet, 4,
            "support");
        EnemyArchetypeDefinition assault = Archetype(
            "Enemies/Archetypes/SpiderAssault.asset",
            "enemy.archetype.spider_assault", "spider_bot",
            LootRewardTier.Normal, null, null, 2,
            "assault");
        EnemyArchetypeDefinition elite = Archetype(
            "Enemies/Archetypes/SpiderElite.asset",
            "enemy.archetype.spider_elite", "spider_bot",
            LootRewardTier.Elite, eliteAffix, null, 5,
            "elite");
        EnemyArchetypeDefinition[] archetypes =
            { raider, suppressor, support, assault, elite };

        WaveDefinition waveOne = Wave(
            "Waves/CityNewWave01.asset", "city_new.wave.01",
            1, 12, 3, 0.8f, archetypes);
        WaveDefinition waveTwo = Wave(
            "Waves/CityNewWave02.asset", "city_new.wave.02",
            2, 20, 3, 0.65f, archetypes);
        WaveDefinition waveThree = Wave(
            "Waves/CityNewWave03.asset", "city_new.wave.03",
            3, 28, 4, 0.5f, archetypes);

        WaveSequenceDefinition sequence =
            Asset<WaveSequenceDefinition>(
                "Waves/CityNewWaveSequence.asset");
        sequence.ConfigureWithStableId(
            "city_new.sequence.default",
            new[]
            {
                new WaveStageDefinition(waveOne, 3f),
                new WaveStageDefinition(waveTwo, 3f),
                new WaveStageDefinition(waveThree, 0f)
            });

        LootDropTableDefinition loot = BuildLootTable();
        ItemDefinition[] items = BuildItems(medicalEffect, armorEffect);
        UpgradeDefinition[] upgrades = BuildUpgrades(vitalityEffect);
        EncounterSequenceDefinition encounters = BuildEncounters(
            archetypes,
            items);
        ModularCombatLayoutSet layouts = Issue62LayoutContentBuilder.Build();

        CityNewContentCatalog catalog = Asset<CityNewContentCatalog>(
            "CityNewContentCatalog.asset");
        catalog.Configure(
            "city_new.default",
            enemy,
            archetypes,
            sequence,
            loot,
            upgrades,
            items,
            new[] { emberBuild },
            encounters,
            layouts);

        MarkAllDirty();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        Debug.Log("CityNew formal content assets rebuilt successfully.");
    }

    private static EncounterSequenceDefinition BuildEncounters(
        IReadOnlyList<EnemyArchetypeDefinition> archetypes,
        IReadOnlyList<ItemDefinition> items)
    {
        EnemyArchetypeDefinition raider = archetypes[0];
        EnemyArchetypeDefinition suppressor = archetypes[1];
        EnemyArchetypeDefinition support = archetypes[2];
        EnemyArchetypeDefinition assault = archetypes[3];
        EnemyArchetypeDefinition elite = archetypes[4];
        ItemDefinition medical = Array.Find(
            items as ItemDefinition[] ?? new List<ItemDefinition>(items).ToArray(),
            item => item.StableId == "medical_kit");
        ItemDefinition armor = Array.Find(
            items as ItemDefinition[] ?? new List<ItemDefinition>(items).ToArray(),
            item => item.StableId == "armor_pack");

        EncounterDefinition ambush = Encounter(
            "Encounters/Ambush.asset",
            "city_new.encounter.ambush",
            EncounterKind.Ambush,
            "侧后伏击",
            "击退从两翼突入的伏击单位",
            EncounterTriggerKind.WaveReached,
            1,
            MissionFlowState.EliminateTargets,
            EncounterObjectiveKind.EliminateEnemies,
            2,
            1.5f,
            45f,
            new[]
            {
                new EncounterRosterEntry(raider, 1, -145),
                new EncounterRosterEntry(raider, 1, 145)
            },
            medical,
            1);
        EncounterDefinition escort = Encounter(
            "Encounters/EliteEscort.asset",
            "city_new.encounter.elite_escort",
            EncounterKind.EliteEscort,
            "精英护送",
            "突破护卫并击杀装甲精英",
            EncounterTriggerKind.WaveReached,
            2,
            MissionFlowState.EliminateTargets,
            EncounterObjectiveKind.EliminateElite,
            1,
            1.25f,
            55f,
            new[]
            {
                new EncounterRosterEntry(elite, 1, 0),
                new EncounterRosterEntry(support, 1, -35),
                new EncounterRosterEntry(assault, 2, 35)
            },
            armor,
            2);
        EncounterDefinition hold = Encounter(
            "Encounters/TimedHold.asset",
            "city_new.encounter.timed_hold",
            EncounterKind.TimedHold,
            "终端守点",
            "在终端附近坚守 8 秒，离开区域会重置进度",
            EncounterTriggerKind.MissionStateReached,
            1,
            MissionFlowState.ActivateTerminal,
            EncounterObjectiveKind.HoldArea,
            8 * WaveDirector.SimulationTickRate,
            1f,
            30f,
            new[]
            {
                new EncounterRosterEntry(suppressor, 1, 90),
                new EncounterRosterEntry(assault, 2, -90)
            },
            medical,
            2);
        EncounterDefinition pursuit = Encounter(
            "Encounters/ExtractionPursuit.asset",
            "city_new.encounter.extraction_pursuit",
            EncounterKind.ExtractionPursuit,
            "撤离追击",
            "在追击部队包围前抵达撤离区",
            EncounterTriggerKind.MissionStateReached,
            1,
            MissionFlowState.ExtractionAvailable,
            EncounterObjectiveKind.ReachExtraction,
            1,
            0.75f,
            35f,
            new[]
            {
                new EncounterRosterEntry(raider, 3, 180),
                new EncounterRosterEntry(suppressor, 1, 135)
            },
            armor,
            2);

        EncounterSequenceDefinition sequence =
            Asset<EncounterSequenceDefinition>(
                "Encounters/CityNewEncounterSequence.asset");
        sequence.Configure(
            "city_new.encounters.default",
            1,
            new[] { ambush, escort, hold, pursuit });
        return sequence;
    }

    private static EncounterDefinition Encounter(
        string path,
        string id,
        EncounterKind kind,
        string displayName,
        string objectiveText,
        EncounterTriggerKind triggerKind,
        int triggerWave,
        MissionFlowState triggerMission,
        EncounterObjectiveKind objectiveKind,
        int objectiveTarget,
        float introSeconds,
        float timeLimitSeconds,
        IEnumerable<EncounterRosterEntry> roster,
        ItemDefinition reward,
        int rewardQuantity)
    {
        EncounterDefinition definition = Asset<EncounterDefinition>(path);
        definition.Configure(
            id,
            1,
            kind,
            displayName,
            objectiveText,
            triggerKind,
            triggerWave,
            triggerMission,
            objectiveKind,
            objectiveTarget,
            EncounterMainFlowPolicy.Parallel,
            introSeconds,
            timeLimitSeconds,
            roster,
            reward,
            rewardQuantity);
        return definition;
    }

    private static WaveDefinition Wave(
        string path,
        string stableId,
        int waveNumber,
        int budget,
        int maximumAlive,
        float interval,
        IReadOnlyList<EnemyArchetypeDefinition> archetypes)
    {
        WaveDefinition wave = Asset<WaveDefinition>(path);
        wave.ConfigureIdentity(stableId);
        wave.ConfigureThreatBudget(
            budget,
            4100 + waveNumber,
            maximumAlive,
            interval,
            new[]
            {
                new WaveEnemyEntry(archetypes[0], 2),
                new WaveEnemyEntry(archetypes[1], 2),
                new WaveEnemyEntry(archetypes[2], 2),
                new WaveEnemyEntry(archetypes[3], 8),
                new WaveEnemyEntry(archetypes[4], 1)
            },
            new[]
            {
                new ThreatRoleConstraint("raider", 1, 1),
                new ThreatRoleConstraint("suppressor", 1, 1),
                new ThreatRoleConstraint("support", 1, 1),
                new ThreatRoleConstraint(
                    "elite", waveNumber >= 3 ? 1 : 0, 1)
            },
            waveNumber == 1 ? 0f : 0.25f);
        return wave;
    }

    private static EnemyAbilitySetDefinition AbilitySet(
        string path,
        string stableId,
        string label,
        Color color,
        EnemyAbilityDefinition ability)
    {
        EnemyAbilitySetDefinition set = Asset<EnemyAbilitySetDefinition>(path);
        set.Configure(stableId, label, color, new[] { ability });
        return set;
    }

    private static EnemyArchetypeDefinition Archetype(
        string path,
        string stableId,
        string enemyTypeId,
        LootRewardTier tier,
        EnemyAffixDefinition affix,
        EnemyAbilitySetDefinition abilities,
        int threatCost,
        string role)
    {
        EnemyArchetypeDefinition archetype =
            Asset<EnemyArchetypeDefinition>(path);
        archetype.Configure(
            stableId, enemyTypeId, null, tier, affix, abilities,
            threatCost, role);
        return archetype;
    }

    private static LootDropTableDefinition BuildLootTable()
    {
        LootDropEntry health = new("medical_kit", 2, 1, 1, 0.35f);
        LootDropEntry armor = new("armor_pack", 2, 1, 1, 0.35f);
        LootDropEntry rifle = new("rifle_ammo", 4, 1, 2, 0.6f);
        LootDropEntry handgun = new("handgun_ammo", 3, 1, 2, 0.55f);
        LootDropTableDefinition table = Asset<LootDropTableDefinition>(
            "Loot/CityNewLootTable.asset");
        table.ConfigureWithStableId(
            "city_new.loot.default",
            new[]
            {
                new LootDropRule(
                    "*", 1, 99, LootRewardTier.Normal, 1, 1,
                    new[] { health, armor, rifle, handgun }),
                new LootDropRule(
                    "*", 1, 99, LootRewardTier.Elite, 2, 2,
                    new[]
                    {
                        new LootDropEntry("armor_pack", 3, 1, 2, 1f),
                        new LootDropEntry("rifle_ammo", 4, 2, 3, 1f),
                        new LootDropEntry("medical_kit", 2, 1, 2, 1f)
                    }),
                new LootDropRule(
                    "*", 1, 99, LootRewardTier.WaveClear, 2, 2,
                    new[]
                    {
                        new LootDropEntry("medical_kit", 2, 1, 1, 1f),
                        new LootDropEntry("armor_pack", 2, 1, 1, 1f),
                        new LootDropEntry("rifle_ammo", 3, 1, 2, 1f),
                        new LootDropEntry("handgun_ammo", 2, 1, 2, 1f)
                    }),
                new LootDropRule(
                    "*", 1, 99, LootRewardTier.FinalWave, 4, 4,
                    new[]
                    {
                        new LootDropEntry("medical_kit", 2, 2, 3, 1f),
                        new LootDropEntry("armor_pack", 2, 2, 3, 1f),
                        new LootDropEntry("rifle_ammo", 3, 3, 5, 1f),
                        new LootDropEntry("handgun_ammo", 2, 3, 5, 1f)
                    })
            });
        return table;
    }

    private static ItemDefinition[] BuildItems(
        GameplayEffectDefinition medicalEffect,
        GameplayEffectDefinition armorEffect)
    {
        ItemDefinition medical = Item(
            "Items/MedicalKit.asset", "medical_kit", "医疗包",
            "战地急救物资，使用后立即恢复生命值。", HudIconId.Health,
            5, ItemEffectType.RestoreHealth, 35f, medicalEffect);
        ItemDefinition armor = Item(
            "Items/ArmorPack.asset", "armor_pack", "护甲包",
            "便携式护甲修复组件，可恢复受损护甲。", HudIconId.Armor,
            5, ItemEffectType.RestoreArmor, 35f, armorEffect);
        ItemDefinition rifle = Item(
            "Items/RifleAmmo.asset", "rifle_ammo", "步枪弹药",
            "标准步枪弹药箱，只补充步枪备弹。", HudIconId.Rifle,
            4, ItemEffectType.AddRifleAmmo, 60f, null);
        ItemDefinition handgun = Item(
            "Items/HandgunAmmo.asset", "handgun_ammo", "手枪弹药",
            "轻型手枪弹药盒，只补充手枪备弹。", HudIconId.Handgun,
            6, ItemEffectType.AddHandgunAmmo, 24f, null);
        return new[] { medical, armor, rifle, handgun };
    }

    private static ItemDefinition Item(
        string path,
        string id,
        string title,
        string description,
        HudIconId icon,
        int stack,
        ItemEffectType effectType,
        float amount,
        GameplayEffectDefinition gameplayEffect)
    {
        ItemDefinition item = Asset<ItemDefinition>(path);
        item.Configure(
            id, title, description, ContentIconAssetUtility.Get(icon),
            ItemType.Consumable, stack, effectType, amount);
        item.ConfigureGameplayEffect(gameplayEffect);
        return item;
    }

    private static UpgradeDefinition[] BuildUpgrades(
        GameplayEffectDefinition vitalityEffect)
    {
        var entries = new (string Path, string Id, string Title,
            string Description, UpgradeRarity Rarity, int Levels,
            UpgradeEffectType Type, float Amount, HudIconId Icon)[]
        {
            ("HardenedRounds", "damage_hardened_rounds", "强化弹头", "每层使武器伤害提高 25%。", UpgradeRarity.Common, 3, UpgradeEffectType.WeaponDamage, 0.25f, HudIconId.Ammo),
            ("WeakpointAnalysis", "damage_weakpoint_analysis", "弱点分析", "每层使武器伤害提高 20%。", UpgradeRarity.Rare, 2, UpgradeEffectType.WeaponDamage, 0.2f, HudIconId.Rifle),
            ("OverchargedCore", "damage_overcharged_core", "过载核心", "使武器伤害提高 35%。", UpgradeRarity.Epic, 1, UpgradeEffectType.WeaponDamage, 0.35f, HudIconId.Handgun),
            ("RapidCycling", "fire_rate_rapid_cycling", "快速枪机", "每层使武器射速提高 15%。", UpgradeRarity.Common, 3, UpgradeEffectType.WeaponFireRate, 0.15f, HudIconId.Rifle),
            ("ExtendedCapacity", "magazine_extended_capacity", "扩容弹匣", "每层使弹匣容量提高 20%。", UpgradeRarity.Rare, 3, UpgradeEffectType.WeaponMagazineCapacity, 0.2f, HudIconId.Ammo),
            ("QuickHands", "reload_quick_hands", "快速换弹", "每层使换弹速度提高 20%。", UpgradeRarity.Common, 3, UpgradeEffectType.WeaponReloadSpeed, 0.2f, HudIconId.Handgun),
            ("RecoilDampening", "recoil_dampening", "后坐力抑制", "每层使后坐力控制提高 18%。", UpgradeRarity.Rare, 3, UpgradeEffectType.WeaponRecoilControl, 0.18f, HudIconId.Rifle),
            ("TightGrouping", "accuracy_tight_grouping", "精准射击", "每层使射击精准度提高 20%。", UpgradeRarity.Epic, 2, UpgradeEffectType.WeaponAccuracy, 0.2f, HudIconId.Ammo),
            ("Vitality", "survival_vitality_reinforcement", "生命强化", "每层使最大生命值提高 20%。", UpgradeRarity.Common, 3, UpgradeEffectType.MaximumHealth, 0.2f, HudIconId.Health),
            ("ReinforcedPlating", "survival_reinforced_plating", "强化护甲", "每层使最大护甲值提高 20%。", UpgradeRarity.Rare, 3, UpgradeEffectType.MaximumArmor, 0.2f, HudIconId.Armor),
            ("EmergencyTreatment", "survival_emergency_treatment", "紧急治疗", "立即恢复 30 点生命值。", UpgradeRarity.Common, 5, UpgradeEffectType.HealthRestore, 30f, HudIconId.Health),
            ("FieldArmorRepair", "survival_field_armor_repair", "战地护甲修复", "立即恢复 30 点护甲值。", UpgradeRarity.Common, 5, UpgradeEffectType.ArmorRestore, 30f, HudIconId.Armor),
            ("MobilityTraining", "survival_mobility_training", "机动训练", "每层使移动速度提高 10%。", UpgradeRarity.Rare, 3, UpgradeEffectType.MovementSpeed, 0.1f, HudIconId.Health)
        };
        var result = new UpgradeDefinition[entries.Length];
        for (int index = 0; index < entries.Length; index++)
        {
            var entry = entries[index];
            UpgradeDefinition upgrade = Asset<UpgradeDefinition>(
                $"Upgrades/{entry.Path}.asset");
            upgrade.Configure(
                entry.Id, entry.Title, entry.Description,
                ContentIconAssetUtility.Get(entry.Icon), entry.Rarity, entry.Levels,
                entry.Type, entry.Amount);
            if (entry.Type == UpgradeEffectType.MaximumHealth)
            {
                upgrade.ConfigureGameplayEffect(vitalityEffect);
            }

            result[index] = upgrade;
        }

        return result;
    }

    private static CombatBuildDefinition BuildEmberChain(
        GameplayEffectDefinition burnEffect)
    {
        CombatRuleEffectDefinition burnOnHit =
            Asset<CombatRuleEffectDefinition>(
                "Builds/Effects/ApplyBurnOnHit.asset");
        burnOnHit.Configure(
            "build.effect.apply_burn_on_hit",
            CombatRuleEffectKind.ApplyStatus,
            CombatRuleTarget.EventTarget,
            burnEffect,
            "status.burning",
            0f,
            1,
            string.Empty);

        CombatRuleEffectDefinition spreadHud =
            Asset<CombatRuleEffectDefinition>(
                "Builds/Effects/BurnSpreadHud.asset");
        spreadHud.Configure(
            "build.effect.burn_spread_hud",
            CombatRuleEffectKind.ShowHudMessage,
            CombatRuleTarget.EventSource,
            null,
            string.Empty,
            0f,
            1,
            "余烬扩散  ×{count}");

        CombatRuleEffectDefinition spreadBurn =
            Asset<CombatRuleEffectDefinition>(
                "Builds/Effects/SpreadBurn.asset");
        spreadBurn.Configure(
            "build.effect.spread_burn",
            CombatRuleEffectKind.SpreadStatus,
            CombatRuleTarget.NearbyEnemies,
            burnEffect,
            "status.burning",
            7f,
            4,
            string.Empty,
            spreadHud);

        CombatRuleDefinition igniteRule = Asset<CombatRuleDefinition>(
            "Builds/Rules/IgniteOnHit.asset");
        igniteRule.Configure(
            "build.rule.ignite_on_hit",
            CombatTriggerType.Hit,
            new[] { "entity.player" },
            new[] { "entity.enemy", "enemy.alive" },
            Array.Empty<string>(),
            0f,
            1f,
            0,
            10000,
            burnOnHit);

        CombatRuleDefinition spreadRule = Asset<CombatRuleDefinition>(
            "Builds/Rules/SpreadBurnOnKill.asset");
        spreadRule.Configure(
            "build.rule.spread_burn_on_kill",
            CombatTriggerType.Kill,
            new[] { "entity.player" },
            new[] { "entity.enemy", "status.burning" },
            Array.Empty<string>(),
            0f,
            1f,
            1,
            10000,
            spreadBurn);

        CombatBuildDefinition build = Asset<CombatBuildDefinition>(
            "Builds/EmberChain.asset");
        build.Configure(
            "build.ember_chain",
            "余烬连锁",
            true,
            igniteRule,
            spreadRule);
        return build;
    }

    private static T Asset<T>(string relativePath) where T : ScriptableObject
    {
        string path = $"{Root}/{relativePath}";
        T asset = AssetDatabase.LoadAssetAtPath<T>(path);
        if (asset != null)
        {
            return asset;
        }

        if (AssetDatabase.LoadMainAssetAtPath(path) != null)
        {
            AssetDatabase.DeleteAsset(path);
        }

        asset = ScriptableObject.CreateInstance<T>();
        asset.name = System.IO.Path.GetFileNameWithoutExtension(path);
        AssetDatabase.CreateAsset(asset, path);
        return asset;
    }

    private static void EnsureFolders()
    {
        string[] folders =
        {
            "Assets/Resources/Content",
            Root,
            $"{Root}/Effects",
            $"{Root}/Enemies",
            $"{Root}/Enemies/Abilities",
            $"{Root}/Enemies/AbilitySets",
            $"{Root}/Enemies/Affixes",
            $"{Root}/Enemies/Archetypes",
            $"{Root}/Waves",
            $"{Root}/Encounters",
            $"{Root}/Loot",
            $"{Root}/Items",
            $"{Root}/Upgrades",
            $"{Root}/Builds",
            $"{Root}/Builds/Effects",
            $"{Root}/Builds/Rules"
        };
        foreach (string folder in folders)
        {
            EnsureFolder(folder);
        }
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
        {
            return;
        }

        int split = path.LastIndexOf('/');
        string parent = path.Substring(0, split);
        string name = path.Substring(split + 1);
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, name);
    }

    private static void MarkAllDirty()
    {
        string[] guids = AssetDatabase.FindAssets(string.Empty, new[] { Root });
        foreach (string guid in guids)
        {
            UnityEngine.Object asset = AssetDatabase.LoadMainAssetAtPath(
                AssetDatabase.GUIDToAssetPath(guid));
            if (asset != null)
            {
                EditorUtility.SetDirty(asset);
            }
        }
    }
}
