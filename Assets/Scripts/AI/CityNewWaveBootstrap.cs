using System.Collections;
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

    [SerializeField] private EnemyFactoryBackend factoryBackend =
        EnemyFactoryBackend.Pool;
    [SerializeField] private CityNewContentCatalog contentCatalog;

    private EnemyController sceneTemplate;
    private IEnemyFactory factory;
    private PooledEnemyFactory pooledFactory;
    private SceneEnemyFactory sceneFactory;
    private NavMeshEnemySpawnPointResolver resolver;
    private WaveDirector director;

    public static bool IsWaveModeActive => instance != null;
    public WaveDirector Director => director;
    public PooledEnemyFactory EnemyPool => pooledFactory;
    public EnemyFactoryBackend FactoryBackend => factoryBackend;
    public CityNewContentCatalog ContentCatalog => contentCatalog;
    public string ConfigurationError { get; private set; }

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
            FailConfiguration("NavMesh is not ready.");
            yield break;
        }

        if (!TryResolveContent())
        {
            yield break;
        }

        sceneTemplate ??= FindSceneTemplate();
        GameObject playerObject = GameObject.FindGameObjectWithTag("Player");

        if (sceneTemplate == null || playerObject == null)
        {
            FailConfiguration(
                "A scene enemy template and tagged player are required.");
            yield break;
        }

        ConfigureFactory();
        director.Configure(
            contentCatalog.WaveSequence,
            factory,
            resolver,
            playerObject.transform);
        ConfigureLootRewards(playerObject, director);
        ConfigureUpgrades(playerObject);

        if (!director.StartRun())
        {
            FailConfiguration(
                "WaveDirector rejected the configured content assets.");
        }
    }

    private void OnDestroy()
    {
        if (instance == this)
        {
            instance = null;
        }
    }

    public void SetContentCatalog(CityNewContentCatalog catalog)
    {
        contentCatalog = catalog;
    }

    public bool ValidateContent()
    {
        return TryResolveContent();
    }

    private bool TryResolveContent()
    {
        contentCatalog ??= CityNewContentCatalog.LoadDefault();

        if (contentCatalog == null)
        {
            return FailConfiguration(
                $"Missing Resources/{CityNewContentCatalog.DefaultResourcePath}.asset.");
        }

        if (!contentCatalog.TryValidate(out string error))
        {
            return FailConfiguration(error);
        }

        ConfigurationError = string.Empty;
        return true;
    }

    private void ConfigureFactory()
    {
        if (factoryBackend == EnemyFactoryBackend.Pool)
        {
            pooledFactory = GetComponent<PooledEnemyFactory>();
            pooledFactory ??= gameObject.AddComponent<PooledEnemyFactory>();
            pooledFactory.Configure(sceneTemplate, 4, 64);
            factory = pooledFactory;
            return;
        }

        sceneFactory = GetComponent<SceneEnemyFactory>();
        sceneFactory ??= gameObject.AddComponent<SceneEnemyFactory>();
        sceneFactory.Configure(sceneTemplate);
        factory = sceneFactory;
    }

    private void ConfigureLootRewards(
        GameObject playerObject,
        WaveDirector configuredDirector)
    {
        PlayerLootRewardController rewards =
            playerObject.GetComponent<PlayerLootRewardController>();
        rewards ??=
            playerObject.AddComponent<PlayerLootRewardController>();
        PlayerUpgradeController upgrades =
            playerObject.GetComponent<PlayerUpgradeController>();
        rewards.Configure(
            configuredDirector,
            contentCatalog.LootDropTable,
            upgrades != null ? upgrades.RunSeed : 18018);
    }

    private void ConfigureUpgrades(GameObject playerObject)
    {
        PlayerUpgradeController upgrades =
            playerObject.GetComponent<PlayerUpgradeController>();

        if (upgrades != null && upgrades.SelectedUpgradeCount == 0 &&
            upgrades.PendingChoiceCount == 0)
        {
            upgrades.ConfigureRun(upgrades.RunSeed, contentCatalog.Upgrades);
        }
    }

    private bool FailConfiguration(string reason)
    {
        ConfigurationError =
            $"CityNew content configuration is invalid: {reason}";
        Debug.LogError(ConfigurationError, this);
        return false;
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
