using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class CityNewWaveBootstrap : MonoBehaviour
{
    private static CityNewWaveBootstrap instance;

    private EnemyController sceneTemplate;
    private SceneEnemyFactory factory;
    private NavMeshEnemySpawnPointResolver resolver;
    private WaveDirector director;
    private readonly List<WaveDefinition> runtimeDefinitions = new();
    private WaveSequenceDefinition runtimeSequence;
    private LootDropTableDefinition runtimeDropTable;

    public static bool IsWaveModeActive => instance != null;
    public WaveDirector Director => director;

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
        factory = GetComponent<SceneEnemyFactory>();
        factory ??= gameObject.AddComponent<SceneEnemyFactory>();
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
            4,
            3,
            0.8f);
        WaveDefinition waveTwo = CreateWave(
            "CityNew Wave 2",
            6,
            3,
            0.65f);
        WaveDefinition waveThree = CreateWave(
            "CityNew Wave 3",
            8,
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
        factory.Configure(sceneTemplate);
        director.Configure(
            runtimeSequence,
            factory,
            resolver,
            playerObject.transform);
        ConfigureLootRewards(playerObject, director);
        director.StartRun();
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
                    Mathf.Max(1, totalCount - 1),
                    LootRewardTier.Normal,
                    "spider_bot"),
                new WaveEnemyEntry(
                    sceneTemplate,
                    1,
                    LootRewardTier.Elite,
                    "spider_bot")
            });
        runtimeDefinitions.Add(definition);
        return definition;
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
