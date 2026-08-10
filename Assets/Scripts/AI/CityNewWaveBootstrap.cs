using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class CityNewWaveBootstrap : MonoBehaviour
{
    private static CityNewWaveBootstrap instance;

    private EnemyController sceneTemplate;
    private SceneEnemyFactory factory;
    private NavMeshEnemySpawnPointResolver resolver;
    private WaveDirector director;
    private WaveDefinition runtimeDefinition;

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

        runtimeDefinition = ScriptableObject.CreateInstance<WaveDefinition>();
        runtimeDefinition.name = "CityNew Single Wave Runtime Definition";
        runtimeDefinition.Configure(
            6,
            3,
            0.75f,
            new[] { new WaveEnemyEntry(sceneTemplate) });
        factory.Configure(sceneTemplate);
        resolver.Configure(runtimeDefinition);
        director.Configure(
            runtimeDefinition,
            factory,
            resolver,
            playerObject.transform);
    }

    private void OnDestroy()
    {
        if (runtimeDefinition != null)
        {
            Destroy(runtimeDefinition);
        }

        if (instance == this)
        {
            instance = null;
        }
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
