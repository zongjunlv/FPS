using System.Collections;
using System.Collections.Generic;
using FPS.GameplayEffects;
using UnityEngine;
using UnityEngine.SceneManagement;

public enum EnemyFactoryBackend
{
    Pool,
    Instantiate
}

public sealed class CityNewWaveBootstrap : MonoBehaviour
{
    private static CityNewWaveBootstrap instance;

    private EnemyController sceneTemplate;
    [SerializeField] private EnemyFactoryBackend factoryBackend =
        EnemyFactoryBackend.Pool;
    private IEnemyFactory factory;
    private PooledEnemyFactory pooledFactory;
    private SceneEnemyFactory sceneFactory;
    private NavMeshEnemySpawnPointResolver resolver;
    private WaveDirector director;
    private readonly List<WaveDefinition> runtimeDefinitions = new();
    private WaveSequenceDefinition runtimeSequence;
    private LootDropTableDefinition runtimeDropTable;
    private EnemyAffixDefinition runtimeEliteAffix;
    private GameplayEffectDefinition runtimeEliteEffect;
    private RaiderApproachAbilityDefinition runtimeRaiderApproach;
    private EnemyAbilitySetDefinition runtimeRaiderAbilitySet;
    private SuppressorRangedAbilityDefinition runtimeSuppressorRanged;
    private EnemyAbilitySetDefinition runtimeSuppressorAbilitySet;
    private EnemySupportAuraAbilityDefinition runtimeSupportAura;
    private EnemyAbilitySetDefinition runtimeSupportAbilitySet;
    private GameplayEffectDefinition runtimeSupportBuffEffect;

    public static bool IsWaveModeActive => instance != null;
    public WaveDirector Director => director;
    public PooledEnemyFactory EnemyPool => pooledFactory;
    public EnemyFactoryBackend FactoryBackend => factoryBackend;

    private void Awake()
    {
        if (SceneManager.GetActiveScene().name != "CityNew" ||
            instance != null)
        {
            enabled = false;
            return;
        }

        instance = this;
        sceneTemplate = FindSceneTemplate();
        if (factoryBackend == EnemyFactoryBackend.Pool)
        {
            pooledFactory = GetComponent<PooledEnemyFactory>();
            pooledFactory ??= gameObject.AddComponent<PooledEnemyFactory>();
            factory = pooledFactory;
        }
        else
        {
            sceneFactory = GetComponent<SceneEnemyFactory>();
            sceneFactory ??= gameObject.AddComponent<SceneEnemyFactory>();
            factory = sceneFactory;
        }
        resolver = GetComponent<NavMeshEnemySpawnPointResolver>();
        resolver ??=
            gameObject.AddComponent<NavMeshEnemySpawnPointResolver>();
        director = GetComponent<WaveDirector>();
        director ??= gameObject.AddComponent<WaveDirector>();
    }

    private IEnumerator Start()
    {
        for (int frame = 0;
             frame < 300 && !RuntimeNavMeshBootstrap.IsSceneReady;
             frame++)
        {
            yield return null;
        }

        if (!RuntimeNavMeshBootstrap.IsSceneReady)
        {
            Debug.LogError("Wave mode could not start: NavMesh is not ready.");
            yield break;
        }

        sceneTemplate ??= FindSceneTemplate();
        GameObject playerObject = GameObject.FindGameObjectWithTag("Player");

        if (sceneTemplate == null || playerObject == null)
        {
            Debug.LogError(
                "Wave mode requires a scene enemy template and player.");
            yield break;
        }

        WaveDefinition waveOne = CreateWave(
            "CityNew Wave 1",
            5,
            3,
            0.8f);
        WaveDefinition waveTwo = CreateWave(
            "CityNew Wave 2",
            7,
            3,
            0.65f);
        WaveDefinition waveThree = CreateWave(
            "CityNew Wave 3",
            9,
            4,
            0.5f);
        runtimeSequence =
            ScriptableObject.CreateInstance<WaveSequenceDefinition>();
        runtimeSequence.name = "CityNew Three Wave Runtime Sequence";
        runtimeSequence.Configure(new[]
        {
            new WaveStageDefinition(waveOne, 3f),
            new WaveStageDefinition(waveTwo, 3f),
            new WaveStageDefinition(waveThree, 0f)
        });
        if (factoryBackend == EnemyFactoryBackend.Pool)
        {
            pooledFactory.Configure(sceneTemplate, 4, 64);
        }
        else
        {
            sceneFactory.Configure(sceneTemplate);
        }
        director.Configure(
            runtimeSequence,
            factory,
            resolver,
            playerObject.transform);
        ConfigureLootRewards(playerObject, director);
        if (!director.StartRun())
        {
            Debug.LogError(
                "CityNew wave run failed to start after configuration.");
        }
    }

    private void OnDestroy()
    {
        foreach (WaveDefinition definition in runtimeDefinitions)
        {
            if (definition != null)
            {
                Destroy(definition);
            }
        }

        runtimeDefinitions.Clear();

        if (runtimeSequence != null)
        {
            Destroy(runtimeSequence);
        }

        if (runtimeDropTable != null)
        {
            Destroy(runtimeDropTable);
        }

        if (runtimeEliteAffix != null)
        {
            Destroy(runtimeEliteAffix);
        }

        if (runtimeEliteEffect != null)
        {
            Destroy(runtimeEliteEffect);
        }

        if (runtimeRaiderAbilitySet != null)
        {
            Destroy(runtimeRaiderAbilitySet);
        }

        if (runtimeRaiderApproach != null)
        {
            Destroy(runtimeRaiderApproach);
        }

        if (runtimeSuppressorAbilitySet != null)
        {
            Destroy(runtimeSuppressorAbilitySet);
        }

        if (runtimeSuppressorRanged != null)
        {
            Destroy(runtimeSuppressorRanged);
        }

        if (runtimeSupportAbilitySet != null)
        {
            Destroy(runtimeSupportAbilitySet);
        }

        if (runtimeSupportAura != null)
        {
            Destroy(runtimeSupportAura);
        }

        if (runtimeSupportBuffEffect != null)
        {
            Destroy(runtimeSupportBuffEffect);
        }

        if (instance == this)
        {
            instance = null;
        }
    }

    private WaveDefinition CreateWave(
        string definitionName,
        int totalCount,
        int maximumAlive,
        float spawnInterval)
    {
        EnsureEliteAffix();
        EnsureRaiderAbilities();
        EnsureSuppressorAbilities();
        EnsureSupportAbilities();
        WaveDefinition definition =
            ScriptableObject.CreateInstance<WaveDefinition>();
        definition.name = definitionName;
        definition.Configure(
            totalCount,
            maximumAlive,
            spawnInterval,
            new[]
            {
                new WaveEnemyEntry(
                    sceneTemplate,
                    1,
                    LootRewardTier.Normal,
                    "spider_raider",
                    null,
                    runtimeRaiderAbilitySet),
                new WaveEnemyEntry(
                    sceneTemplate,
                    1,
                    LootRewardTier.Normal,
                    "spider_suppressor",
                    null,
                    runtimeSuppressorAbilitySet),
                new WaveEnemyEntry(
                    sceneTemplate,
                    1,
                    LootRewardTier.Normal,
                    "spider_support",
                    null,
                    runtimeSupportAbilitySet),
                new WaveEnemyEntry(
                    sceneTemplate,
                    Mathf.Max(1, totalCount - 4),
                    LootRewardTier.Normal,
                    "spider_bot"),
                new WaveEnemyEntry(
                    sceneTemplate,
                    1,
                    LootRewardTier.Elite,
                    "spider_bot",
                    runtimeEliteAffix)
            });
        runtimeDefinitions.Add(definition);
        return definition;
    }

    private void EnsureRaiderAbilities()
    {
        if (runtimeRaiderApproach != null &&
            runtimeRaiderAbilitySet != null)
        {
            return;
        }

        runtimeRaiderApproach =
            ScriptableObject.CreateInstance<RaiderApproachAbilityDefinition>();
        runtimeRaiderApproach.name = "Raider Flank Approach Ability";
        runtimeRaiderApproach.hideFlags = HideFlags.HideAndDontSave;
        runtimeRaiderApproach.Configure(
            "enemy.ability.raider_flank",
            5.5f,
            2.5f,
            2f,
            4.75f,
            1.35f,
            1.75f,
            1.5f,
            0.8f,
            2.3f,
            0.25f,
            0.9f,
            0.8f);

        runtimeRaiderAbilitySet =
            ScriptableObject.CreateInstance<EnemyAbilitySetDefinition>();
        runtimeRaiderAbilitySet.name = "Spider Raider Ability Set";
        runtimeRaiderAbilitySet.hideFlags = HideFlags.HideAndDontSave;
        runtimeRaiderAbilitySet.Configure(
            "enemy.role.spider_raider",
            "RAIDER",
            new Color(0.1f, 0.9f, 1f, 1f),
            new EnemyAbilityDefinition[] { runtimeRaiderApproach });
    }

    private void EnsureSuppressorAbilities()
    {
        if (runtimeSuppressorRanged != null &&
            runtimeSuppressorAbilitySet != null)
        {
            return;
        }

        runtimeSuppressorRanged =
            ScriptableObject.CreateInstance<
                SuppressorRangedAbilityDefinition>();
        runtimeSuppressorRanged.name =
            "Suppressor Tactical Ranged Ability";
        runtimeSuppressorRanged.hideFlags =
            HideFlags.HideAndDontSave;
        runtimeSuppressorRanged.Configure(
            "enemy.ability.suppressor_ranged",
            6f,
            10f,
            14f,
            4f,
            2f,
            1.15f,
            1.5f,
            0.5f,
            0.45f,
            1.2f,
            0.65f,
            280f);

        runtimeSuppressorAbilitySet =
            ScriptableObject.CreateInstance<
                EnemyAbilitySetDefinition>();
        runtimeSuppressorAbilitySet.name =
            "Spider Suppressor Ability Set";
        runtimeSuppressorAbilitySet.hideFlags =
            HideFlags.HideAndDontSave;
        runtimeSuppressorAbilitySet.Configure(
            "enemy.role.spider_suppressor",
            "SUPPRESSOR",
            new Color(1f, 0.28f, 0.08f, 1f),
            new EnemyAbilityDefinition[] { runtimeSuppressorRanged });
    }

    private void EnsureEliteAffix()
    {
        if (runtimeEliteAffix != null && runtimeEliteEffect != null)
        {
            return;
        }

        runtimeEliteEffect =
            ScriptableObject.CreateInstance<GameplayEffectDefinition>();
        runtimeEliteEffect.name = "Armored Elite Gameplay Effect";
        runtimeEliteEffect.hideFlags = HideFlags.HideAndDontSave;
        runtimeEliteEffect.Configure(
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

        runtimeEliteAffix =
            ScriptableObject.CreateInstance<EnemyAffixDefinition>();
        runtimeEliteAffix.name = "Armored Elite Affix";
        runtimeEliteAffix.hideFlags = HideFlags.HideAndDontSave;
        runtimeEliteAffix.Configure(
            "armored_elite",
            "ELITE ARMOR",
            new Color(1f, 0.72f, 0.12f, 1f),
            runtimeEliteEffect);
    }

    private void EnsureSupportAbilities()
    {
        if (runtimeSupportAura != null &&
            runtimeSupportAbilitySet != null &&
            runtimeSupportBuffEffect != null)
        {
            return;
        }

        runtimeSupportBuffEffect =
            ScriptableObject.CreateInstance<GameplayEffectDefinition>();
        runtimeSupportBuffEffect.name = "Support Attack Buff Effect";
        runtimeSupportBuffEffect.hideFlags = HideFlags.HideAndDontSave;
        runtimeSupportBuffEffect.Configure(
            "enemy.buff.support_attack",
            new GameplayEffectModifier(
                GameplayAttributeId.EnemyAttackDamage,
                GameplayModifierOperation.Multiply,
                0.25f));
        runtimeSupportBuffEffect.ConfigureTags(
            "effect.enemy.support_attack",
            "buff.support",
            "target.enemy");

        runtimeSupportAura =
            ScriptableObject.CreateInstance<
                EnemySupportAuraAbilityDefinition>();
        runtimeSupportAura.name = "Spider Support Aura";
        runtimeSupportAura.hideFlags = HideFlags.HideAndDontSave;
        runtimeSupportAura.Configure(
            "enemy.ability.support_aura",
            10f,
            2,
            3f,
            2f,
            EnemySupportTargetPriority.LowestHealthRatio,
            "enemy",
            runtimeSupportBuffEffect);

        runtimeSupportAbilitySet =
            ScriptableObject.CreateInstance<EnemyAbilitySetDefinition>();
        runtimeSupportAbilitySet.name = "Spider Support Ability Set";
        runtimeSupportAbilitySet.hideFlags = HideFlags.HideAndDontSave;
        runtimeSupportAbilitySet.Configure(
            "enemy.role.spider_support",
            "SUPPORT",
            new Color(0.35f, 1f, 0.42f, 1f),
            new EnemyAbilityDefinition[] { runtimeSupportAura });
    }

    private void ConfigureLootRewards(
        GameObject playerObject,
        WaveDirector configuredDirector)
    {
        PlayerLootRewardController rewards =
            playerObject.GetComponent<PlayerLootRewardController>();

        if (rewards == null)
        {
            rewards = playerObject.AddComponent<PlayerLootRewardController>();
        }

        runtimeDropTable = CreateDefaultDropTable();
        PlayerUpgradeController upgrades =
            playerObject.GetComponent<PlayerUpgradeController>();
        rewards.Configure(
            configuredDirector,
            runtimeDropTable,
            upgrades != null ? upgrades.RunSeed : 18018);
    }

    private static LootDropTableDefinition CreateDefaultDropTable()
    {
        LootDropEntry health = new(
            "medical_kit", 2, 1, 1, 0.35f);
        LootDropEntry armor = new(
            "armor_pack", 2, 1, 1, 0.35f);
        LootDropEntry rifle = new(
            "rifle_ammo", 4, 1, 2, 0.6f);
        LootDropEntry handgun = new(
            "handgun_ammo", 3, 1, 2, 0.55f);
        LootDropEntry eliteArmor = new(
            "armor_pack", 3, 1, 2, 1f);
        LootDropEntry eliteRifle = new(
            "rifle_ammo", 4, 2, 3, 1f);
        LootDropEntry eliteHealth = new(
            "medical_kit", 2, 1, 2, 1f);
        LootDropTableDefinition table =
            ScriptableObject.CreateInstance<LootDropTableDefinition>();
        table.name = "CityNew Runtime Loot Drop Table";
        table.Configure(new[]
        {
            new LootDropRule(
                "*", 1, 99, LootRewardTier.Normal, 1, 1,
                new[] { health, armor, rifle, handgun }),
            new LootDropRule(
                "*", 1, 99, LootRewardTier.Elite, 2, 2,
                new[] { eliteArmor, eliteRifle, eliteHealth }),
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

    private static EnemyController FindSceneTemplate()
    {
        EnemyController[] enemies =
            FindObjectsByType<EnemyController>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);

        foreach (EnemyController enemy in enemies)
        {
            if (enemy.name == "SPIDER_BOT")
            {
                return enemy;
            }
        }

        return enemies.Length > 0 ? enemies[0] : null;
    }
}
