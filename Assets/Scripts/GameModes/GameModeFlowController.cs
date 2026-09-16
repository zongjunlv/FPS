using System;
using System.Collections;
using System.Threading.Tasks;
using FPS.Core.GameModes;
using FPS.GameplayEffects;
using FPS.Networking.Netcode;
using FPS.Networking.Session;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

[DefaultExecutionOrder(-31000)]
[DisallowMultipleComponent]
public sealed class GameModeFlowController : MonoBehaviour
{
    public static GameModeFlowController Instance { get; private set; }

    private GameModeCatalog catalog;
    private Coroutine transitionRoutine;

    public event Action StateChanged;

    public GameModeCatalog Catalog => catalog;
    public bool IsLoading { get; private set; }
    public float LoadingProgress { get; private set; }
    public string StatusMessage { get; private set; } = string.Empty;
    public string FailureMessage { get; private set; } = string.Empty;
    public string LastRequestedScenePath { get; private set; } = string.Empty;
    public int TransitionRequestCount { get; private set; }
    public int CompletedTransitionCount { get; private set; }
    public int FailedTransitionCount { get; private set; }

    public static GameModeFlowController Ensure(GameModeCatalog configuredCatalog)
    {
        if (Instance == null)
        {
            Instance = FindFirstObjectByType<GameModeFlowController>();
        }

        if (Instance == null)
        {
            var runtime = new GameObject("Game Mode Runtime");
            Instance = runtime.AddComponent<GameModeFlowController>();
        }

        Instance.Configure(configuredCatalog);
        return Instance;
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    public void Configure(GameModeCatalog configuredCatalog)
    {
        string error = string.Empty;
        if (configuredCatalog == null ||
            !configuredCatalog.TryValidate(out error))
        {
            Fail(string.IsNullOrEmpty(error)
                ? "模式配置资源缺失。"
                : error);
            return;
        }

        catalog = configuredCatalog;
    }

    public bool TryEnterMode(GameModeId mode)
    {
        if (IsLoading)
        {
            return false;
        }

        if (catalog == null || !catalog.TryGet(mode, out GameModeDefinition route))
        {
            Fail($"找不到模式入口：{GameModeIds.ToStableId(mode)}。");
            return false;
        }

        return TryStartTransition(
            route.Mode,
            route.EntryStage,
            route.EntryScenePath,
            $"正在进入{route.DisplayName}……");
    }

    public bool TryReturnToEntry()
    {
        if (IsLoading)
        {
            return false;
        }

        if (catalog == null)
        {
            Fail("模式配置资源缺失，无法返回入口。");
            return false;
        }

        return TryStartTransition(
            GameModeId.None,
            GameModeStage.Entry,
            catalog.EntryScenePath,
            "正在返回模式选择……");
    }

    public void ClearFailure()
    {
        FailureMessage = string.Empty;
        StatusMessage = string.Empty;
        NotifyStateChanged();
    }

    private bool TryStartTransition(
        GameModeId mode,
        GameModeStage stage,
        string scenePath,
        string loadingMessage)
    {
        if (string.IsNullOrWhiteSpace(scenePath) ||
            !Application.CanStreamedLevelBeLoaded(scenePath))
        {
            Fail($"目标场景不可用：{scenePath}。请检查构建场景配置。");
            return false;
        }

        TransitionRequestCount++;
        transitionRoutine = StartCoroutine(Transition(
            mode,
            stage,
            scenePath,
            loadingMessage));
        return true;
    }

    private IEnumerator Transition(
        GameModeId mode,
        GameModeStage stage,
        string scenePath,
        string loadingMessage)
    {
        IsLoading = true;
        LoadingProgress = 0f;
        FailureMessage = string.Empty;
        StatusMessage = loadingMessage;
        LastRequestedScenePath = scenePath;
        int transitionEpoch = GameModeContext.BeginTransition(mode, stage);
        NotifyStateChanged();

        yield return GameModeTransitionCleanup.ReleasePreviousMode();

        if (GameModeContext.Epoch != transitionEpoch)
        {
            Fail("模式切换已被新的请求取代。");
            yield break;
        }

        AsyncOperation operation;
        try
        {
            operation = SceneManager.LoadSceneAsync(
                scenePath,
                LoadSceneMode.Single);
        }
        catch (Exception exception)
        {
            Fail("场景加载失败：" + exception.Message);
            yield break;
        }

        if (operation == null)
        {
            Fail("Unity 未能创建场景加载任务。");
            yield break;
        }

        while (!operation.isDone)
        {
            LoadingProgress = Mathf.Clamp01(operation.progress / 0.9f);
            NotifyStateChanged();
            yield return null;
        }

        yield return null;
        GameModeSceneMarker marker =
            FindFirstObjectByType<GameModeSceneMarker>();
        if (marker == null || marker.Mode != mode || marker.Stage != stage)
        {
            Fail(
                "已加载场景缺少匹配的模式标识，已阻止半初始化流程。",
                preserveLoadedScene: true);
            yield break;
        }

        if (!marker.IsActivated && !marker.ActivateContext())
        {
            Fail(marker.ActivationError, preserveLoadedScene: true);
            yield break;
        }

        IsLoading = false;
        LoadingProgress = 1f;
        StatusMessage = string.Empty;
        FailureMessage = string.Empty;
        transitionRoutine = null;
        CompletedTransitionCount++;
        ApplyCursorPolicy(stage);
        NotifyStateChanged();
    }

    private void Fail(string message, bool preserveLoadedScene = false)
    {
        IsLoading = false;
        LoadingProgress = 0f;
        FailureMessage = string.IsNullOrWhiteSpace(message)
            ? "模式加载失败。"
            : message.Trim();
        StatusMessage = FailureMessage;
        transitionRoutine = null;
        FailedTransitionCount++;
        GameModeContext.FailTransition(FailureMessage);
        ApplyCursorPolicy(GameModeContext.CurrentStage);
        NotifyStateChanged();
    }

    private static void ApplyCursorPolicy(GameModeStage stage)
    {
        bool menuStage = stage != GameModeStage.Battle;
        Cursor.visible = menuStage;
        Cursor.lockState = menuStage
            ? CursorLockMode.None
            : CursorLockMode.Locked;
    }

    private void NotifyStateChanged()
    {
        StateChanged?.Invoke();
    }

#if UNITY_EDITOR
    public static void ResetRuntimeForTests()
    {
        if (Instance != null)
        {
            DestroyImmediate(Instance.gameObject);
        }

        Instance = null;
        GameModeContext.ResetForTests();
        Time.timeScale = 1f;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }
#endif
}

internal static class GameModeTransitionCleanup
{
    private const int NetworkShutdownFrameLimit = 300;

    public static IEnumerator ReleasePreviousMode()
    {
        Time.timeScale = 1f;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        RunSnapshotPresentationGate.HideImmediately();
        RunSnapshotSession.ResetForModeTransition();

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        GameplayEffectRuntimeInspector inspector =
            GameplayEffectRuntimeInspector.Instance;
        inspector?.SetVisible(false);
#endif

        foreach (PlayerInputReader input in
                 UnityEngine.Object.FindObjectsByType<PlayerInputReader>(
                     FindObjectsInactive.Include))
        {
            input.enabled = false;
        }

        foreach (UnifiedGameHud hud in
                 UnityEngine.Object.FindObjectsByType<UnifiedGameHud>(
                     FindObjectsInactive.Include))
        {
            UnityEngine.Object.Destroy(hud.gameObject);
        }

        CoopSessionController[] sessions =
            UnityEngine.Object.FindObjectsByType<CoopSessionController>(
                FindObjectsInactive.Include);
        Task[] shutdownTasks = new Task[sessions.Length];
        for (int index = 0; index < sessions.Length; index++)
        {
            shutdownTasks[index] = sessions[index].ShutdownForModeExitAsync();
        }

        int waitFrames = 0;
        while (!AllCompleted(shutdownTasks) &&
               waitFrames++ < NetworkShutdownFrameLimit)
        {
            yield return null;
        }

        foreach (OptionalNetworkBootstrap network in
                 UnityEngine.Object.FindObjectsByType<OptionalNetworkBootstrap>(
                     FindObjectsInactive.Include))
        {
            network.Shutdown();
            UnityEngine.Object.Destroy(network.gameObject);
        }

        foreach (NetworkManager manager in
                 UnityEngine.Object.FindObjectsByType<NetworkManager>(
                     FindObjectsInactive.Include))
        {
            if (manager.IsListening || manager.ShutdownInProgress)
            {
                manager.Shutdown(true);
            }

            UnityEngine.Object.Destroy(manager.gameObject);
        }

        foreach (CoopSessionController session in sessions)
        {
            if (session != null)
            {
                UnityEngine.Object.Destroy(session.gameObject);
            }
        }

        EnemyAiLodController.SetGlobalEnabled(true);
        EnemySpatialIndexService.SetGlobalEnabled(true);
        EnemyPerceptionScheduler.SetBatchEnabled(true);
        yield return null;
    }

    private static bool AllCompleted(Task[] tasks)
    {
        for (int index = 0; index < tasks.Length; index++)
        {
            if (tasks[index] != null && !tasks[index].IsCompleted)
            {
                return false;
            }
        }

        return true;
    }
}
