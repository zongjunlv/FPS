using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

public enum EnemyFactoryBackend
{
    Pool,
    Instantiate,
    Addressables
}

public sealed class CityNewWaveBootstrap : MonoBehaviour
{
    private static CityNewWaveBootstrap instance;

    [SerializeField] private EnemyFactoryBackend factoryBackend =
        EnemyFactoryBackend.Addressables;
    [SerializeField] private CityNewContentCatalog contentCatalog;

    private EnemyController sceneTemplate;
    private IEnemyFactory factory;
    private PooledEnemyFactory pooledFactory;
    private SceneEnemyFactory sceneFactory;
    private NavMeshEnemySpawnPointResolver resolver;
    private WaveDirector director;
    private AddressableEnemyFactory addressableFactory;
    private Font preparationFont;
    private GUIStyle preparationStyle;

    public static bool IsWaveModeActive => instance != null;
    public WaveDirector Director => director;
    public PooledEnemyFactory EnemyPool => pooledFactory;
    public EnemyFactoryBackend FactoryBackend => factoryBackend;
    public CityNewContentCatalog ContentCatalog => contentCatalog;
    public string ConfigurationError { get; private set; }
    public IAsyncEnemyFactory AsyncFactory => addressableFactory;
    public string PreparationStatus => addressableFactory == null ? string.Empty :
        addressableFactory.PreparationState == EnemyFactoryPreparationState.Loading
            ? $"正在加载敌人资源 {addressableFactory.PreparationProgress:P0}"
            : addressableFactory.PreparationState == EnemyFactoryPreparationState.Prewarming
                ? $"正在准备敌人对象池 {addressableFactory.PreparationProgress:P0}"
                : addressableFactory.PreparationState == EnemyFactoryPreparationState.Failed
                    ? "敌人资源准备失败，请检查配置后重试" : string.Empty;

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

        if ((sceneTemplate == null && factoryBackend != EnemyFactoryBackend.Addressables) || playerObject == null)
        {
            FailConfiguration(
                "A scene enemy template and tagged player are required.");
            yield break;
        }

        try
        {
            ConfigureFactory();
        }
        catch (System.Exception exception)
        {
            FailConfiguration(exception.Message);
            yield break;
        }
        if (addressableFactory != null)
        {
            yield return addressableFactory.PrepareAsync();
            if (addressableFactory.PreparationState != EnemyFactoryPreparationState.Ready)
            {
                FailConfiguration(addressableFactory.PreparationError);
                yield break;
            }
        }
        ConfigureUpgrades(playerObject);
        if (!RunSnapshotSession.InitializePlayer(playerObject, out string restoreError))
        {
            FailConfiguration(restoreError);
            yield break;
        }
        PlayerUpgradeController upgrades =
            playerObject.GetComponent<PlayerUpgradeController>();
        int runSeed = upgrades != null ? upgrades.RunSeed : 18018;
        PrepareWavesForRun(runSeed);
        director.Configure(
            contentCatalog.WaveSequence,
            factory,
            resolver,
            playerObject.transform);
        ConfigureLootRewards(playerObject, director);
        ConfigureRunRecording(playerObject, director, runSeed);

        string waveRestoreError = string.Empty;
        bool restoredWave = RunSnapshotSession.HasPendingWorldRestore &&
                            RunSnapshotSession.TryRestoreWave(
                                director,
                                playerObject,
                                out waveRestoreError);
        if (RunSnapshotSession.HasPendingWorldRestore && !restoredWave)
        {
            FailConfiguration(waveRestoreError);
            yield break;
        }

        // The mission bootstrap may have configured itself one frame before the
        // player snapshot created the pending world restore. Finish that half of
        // the restore here when it is already ready; otherwise its own coroutine
        // will consume the pending mission state later.
        if (restoredWave && RunSnapshotSession.HasPendingWorldRestore)
        {
            CityNewMissionController mission =
                playerObject.GetComponent<CityNewMissionController>();
            if (mission != null && mission.Terminal != null &&
                !RunSnapshotSession.TryRestoreMission(
                    mission,
                    out string missionRestoreError))
            {
                FailConfiguration(missionRestoreError);
                yield break;
            }
        }

        if (!restoredWave && !director.StartRun())
        {
            FailConfiguration(
                "WaveDirector rejected the configured content assets.");
        }
    }

    private void OnDestroy()
    {
        if (addressableFactory != null)
        {
            director?.StopRun(WaveStopReason.Destroyed);
            addressableFactory.DisposeFactory();
        }
        if (preparationFont != null) Destroy(preparationFont);
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
        if (factoryBackend == EnemyFactoryBackend.Addressables)
        {
            if (sceneTemplate != null)
            {
                sceneTemplate.SetFactoryManaged(true);
                // Preserve collider/component baselines for emergency clones.
                sceneTemplate.gameObject.SetActive(false);
            }
            addressableFactory = GetComponent<AddressableEnemyFactory>() ??
                gameObject.AddComponent<AddressableEnemyFactory>();
            addressableFactory.Configure(contentCatalog.EnemyArchetypes, 4, 64,
                safeFallbackTemplate: sceneTemplate);
            pooledFactory = addressableFactory.Pool;
            factory = addressableFactory;
            return;
        }
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

    private void OnGUI()
    {
        string status = PreparationStatus;
        if (string.IsNullOrEmpty(status)) return;
        if (preparationStyle == null)
        {
            preparationFont = Font.CreateDynamicFontFromOSFont(
                new[] { "PingFang SC", "Microsoft YaHei", "Noto Sans CJK SC", "Arial Unicode MS" }, 22);
            preparationStyle = new GUIStyle(GUI.skin.box)
            {
                font = preparationFont,
                fontSize = 22,
                alignment = TextAnchor.MiddleCenter
            };
        }
        GUI.Box(new Rect((Screen.width - 480f) * 0.5f, 60f, 480f, 56f), status, preparationStyle);
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

    private void PrepareWavesForRun(int runSeed)
    {
        WaveSequenceDefinition sequence = contentCatalog.WaveSequence;
        for (int index = 0; index < sequence.WaveCount; index++)
        {
            sequence.GetStage(index).Wave.PrepareForRun(runSeed);
        }
    }

    private void ConfigureRunRecording(
        GameObject playerObject,
        WaveDirector configuredDirector,
        int runSeed)
    {
        RunDeterminismRecorder recorder =
            playerObject.GetComponent<RunDeterminismRecorder>();
        recorder ??= playerObject.AddComponent<RunDeterminismRecorder>();
        recorder.Configure(
            runSeed,
            playerObject.GetComponent<PlayerInputReader>(),
            playerObject.GetComponent<PlayerUpgradeController>(),
            configuredDirector,
            playerObject.GetComponent<PlayerLootRewardController>(),
            contentCatalog.WaveSequence);
        RunReplayRuntimeAdapter replayAdapter =
            playerObject.GetComponent<RunReplayRuntimeAdapter>();
        replayAdapter ??= playerObject.AddComponent<RunReplayRuntimeAdapter>();
        replayAdapter.Configure(
            playerObject.GetComponent<PlayerInputReader>(),
            configuredDirector,
            contentCatalog.WaveSequence);
        recorder.ConfigureStateCapture(replayAdapter, 50);
        RunReplayController replayController =
            playerObject.GetComponent<RunReplayController>();
        replayController ??= playerObject.AddComponent<RunReplayController>();
        replayController.enabled = false;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        RunReplayDebugTimeline debugTimeline =
            playerObject.GetComponent<RunReplayDebugTimeline>();
        debugTimeline ??= playerObject.AddComponent<RunReplayDebugTimeline>();
        debugTimeline.Configure(recorder, replayController);
        CombatRuntimeDiagnosticsPanel diagnosticsPanel =
            playerObject.GetComponent<CombatRuntimeDiagnosticsPanel>();
        diagnosticsPanel ??=
            playerObject.AddComponent<CombatRuntimeDiagnosticsPanel>();
        diagnosticsPanel.Configure(configuredDirector);
#endif
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
