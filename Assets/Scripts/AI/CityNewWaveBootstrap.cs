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
            new[] { new WaveEnemyEntry(sceneTemplate) });
        runtimeDefinitions.Add(definition);
        return definition;
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
