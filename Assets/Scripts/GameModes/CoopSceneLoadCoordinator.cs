using System;
using System.Collections;
using System.Threading.Tasks;
using FPS.Core.GameModes;
using FPS.Networking.Netcode;
using FPS.Networking.Session;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
[RequireComponent(typeof(CoopSessionController))]
public sealed class CoopSceneLoadCoordinator : MonoBehaviour
{
    private const string CoopLoginScenePath =
        "Assets/Scenes/Modes/CoopLogin.unity";

    private CoopSessionController session;
    private Coroutine loadRoutine;
    private Coroutine returnRoutine;
    private bool returningToLobby;
    private string loadingEpoch = string.Empty;
    private int loadGeneration;
    private AsyncOperation pendingBattleSceneLoad;

    public bool IsLoading => loadRoutine != null;
    public string LastFailure { get; private set; } = string.Empty;

    private void Awake()
    {
        session = GetComponent<CoopSessionController>();
    }

    private void OnEnable()
    {
        session ??= GetComponent<CoopSessionController>();
        session.LobbyStartRequested += HandleLoadRequested;
        session.BattleSceneReady += HandleBattleReady;
        session.SceneLoadCancelled += HandleLoadCancelled;
        session.ReturnedToLobby += HandleReturnedToLobby;
        session.StateChanged += HandleSessionStateChanged;
    }

    private void OnDisable()
    {
        if (session != null)
        {
            session.LobbyStartRequested -= HandleLoadRequested;
            session.BattleSceneReady -= HandleBattleReady;
            session.SceneLoadCancelled -= HandleLoadCancelled;
            session.ReturnedToLobby -= HandleReturnedToLobby;
            session.StateChanged -= HandleSessionStateChanged;
        }
        bool wasTransitioning = loadRoutine != null || returnRoutine != null;
        CancelPendingLoad();
        if (returnRoutine != null) StopCoroutine(returnRoutine);
        returnRoutine = null;
        returningToLobby = false;
        if (wasTransitioning) RestoreLobbyInteraction();
    }

    private void Update()
    {
        session?.TickSceneLoadTimeout(Time.realtimeSinceStartupAsDouble);
        session?.EnsureBattleTransportForCurrentPhase();
    }

    private void HandleSessionStateChanged(CoopSessionState state)
    {
        if (state == CoopSessionState.Leaving || state == CoopSessionState.Offline ||
            state == CoopSessionState.Failed)
            RestoreLobbyInteraction();
    }

    private void HandleLoadRequested()
    {
        if (session == null || string.IsNullOrWhiteSpace(
                session.SceneLoadEpoch)) return;
        if (returningToLobby) return;
        if (loadRoutine != null && string.Equals(loadingEpoch,
                session.SceneLoadEpoch, StringComparison.Ordinal)) return;
        CancelPendingLoad();
        loadingEpoch = session.SceneLoadEpoch;
        loadRoutine = StartCoroutine(LoadCityNew(loadingEpoch, loadGeneration));
    }

    private IEnumerator LoadCityNew(string epoch, int generation)
    {
        LastFailure = string.Empty;
        // Stopping a coroutine does not cancel Unity's native scene operation.
        // Serialize scene loads so a cancelled CityNew cannot land after lobby.
        while (pendingBattleSceneLoad != null && !pendingBattleSceneLoad.isDone)
            yield return null;
        pendingBattleSceneLoad = null;
        if (!IsCurrentLoad(epoch, generation)) yield break;
        if (!GameModeContext.IsActive(GameModeId.Coop,
                GameModeStage.CoopBattle))
            GameModeContext.BeginTransition(GameModeId.Coop,
                GameModeStage.CoopBattle);

        if (SceneManager.GetActiveScene().path !=
            DedicatedServerConfiguration.CityNewScenePath)
        {
            AsyncOperation operation = SceneManager.LoadSceneAsync(
                DedicatedServerConfiguration.CityNewScenePath,
                LoadSceneMode.Single);
            if (operation == null)
            {
                LastFailure = "Unity 无法创建 CityNew 加载任务。";
                _ = session.CancelSceneLoadAsync(LastFailure);
                loadRoutine = null;
                yield break;
            }
            pendingBattleSceneLoad = operation;
            while (!operation.isDone) yield return null;
            pendingBattleSceneLoad = null;
        }
        yield return null;
        if (!IsCurrentLoad(epoch, generation)) yield break;

        if (!GameModeContext.IsActive(GameModeId.Coop,
                GameModeStage.CoopBattle) &&
            !GameModeContext.TryActivate(GameModeId.Coop,
                GameModeStage.CoopBattle, out string error))
        {
            LastFailure = error;
            _ = session.CancelSceneLoadAsync(error);
            loadRoutine = null;
            yield break;
        }

        NetworkManager manager = NetworkManager.Singleton;
        if (manager != null && manager.IsServer)
        {
            CoopEnvironmentReadinessRegistry.EnsureCityNewEnvironment();
            float environmentDeadline = Time.realtimeSinceStartup + 30f;
            while (!CoopEnvironmentReadinessRegistry.IsCityNewReady &&
                   Time.realtimeSinceStartup < environmentDeadline &&
                   IsCurrentLoad(epoch, generation))
                yield return null;
            if (!IsCurrentLoad(epoch, generation)) yield break;
            if (!CoopEnvironmentReadinessRegistry.IsCityNewReady)
            {
                LastFailure = "CityNew 导航环境准备超时，已阻止不完整战局启动。";
                _ = session.CancelSceneLoadAsync(LastFailure);
                loadRoutine = null;
                yield break;
            }
        }

        Task<bool> report = session.ReportSceneReadyAsync(epoch);
        while (!report.IsCompleted && IsCurrentLoad(epoch, generation))
            yield return null;
        if (!IsCurrentLoad(epoch, generation)) yield break;
        if (report.IsFaulted || !report.Result)
            LastFailure = session.SceneLoadFailure;
        loadRoutine = null;
    }

    private void HandleBattleReady()
    {
        NetworkManager manager = NetworkManager.Singleton;
        if (manager == null || !manager.IsServer) return;
        CoopNetworkRuntimeInstaller installer =
            FindFirstObjectByType<CoopNetworkRuntimeInstaller>();
        if (installer == null) return;
        if (!installer.SpawnAuthoritativeSlice())
        {
            LastFailure = installer.LastFailure;
            return;
        }
        ConfigurePlayerAppearances(installer);
        if (installer.IsPlayerSpawnDeferred)
            installer.ReleasePlayerSpawnBarrier();
    }

    private void ConfigurePlayerAppearances(
        CoopNetworkRuntimeInstaller installer)
    {
        int playerId = 1;
        if (session.LobbyMembers.Count > 0)
        {
            for (int index = 0;
                 index < session.LobbyMembers.Count &&
                 playerId <= installer.MaximumPlayers;
                 index++, playerId++)
            {
                installer.ConfigurePlayerAppearance(
                    playerId,
                    session.LobbyMembers[index].AppearanceId);
            }
            return;
        }

        string localAppearance = string.IsNullOrWhiteSpace(
            PlayerAppearanceSelection.CurrentAppearanceId)
            ? session.PendingAppearanceId
            : PlayerAppearanceSelection.CurrentAppearanceId;
        installer.ConfigurePlayerAppearance(1, localAppearance);
    }

    private void HandleLoadCancelled(string reason)
    {
        LastFailure = reason ?? string.Empty;
        CancelPendingLoad();
        if (!returningToLobby)
        {
            returningToLobby = true;
            returnRoutine = StartCoroutine(ReturnToLobby());
        }
    }

    private void HandleReturnedToLobby()
    {
        CancelPendingLoad();
        NetworkManager manager = NetworkManager.Singleton;
        if (manager != null && manager.IsServer)
        {
            CoopNetworkRuntimeInstaller installer =
                FindFirstObjectByType<CoopNetworkRuntimeInstaller>();
            installer?.PrepareForLobbyReturn();
        }
        if (!returningToLobby)
        {
            returningToLobby = true;
            returnRoutine = StartCoroutine(ReturnToLobby());
        }
    }

    private IEnumerator ReturnToLobby()
    {
        RestoreLobbyInteraction();
        Task shutdown = session != null
            ? session.StopBattleTransportAsync()
            : Task.CompletedTask;
        while (!shutdown.IsCompleted) yield return null;
        if (shutdown.IsFaulted)
            LastFailure = shutdown.Exception?.GetBaseException().Message ??
                          "本地战斗连接关闭失败。";
        while (pendingBattleSceneLoad != null && !pendingBattleSceneLoad.isDone)
            yield return null;
        pendingBattleSceneLoad = null;
        if (SceneManager.GetActiveScene().path != CoopLoginScenePath)
        {
            GameModeContext.BeginTransition(GameModeId.Coop,
                GameModeStage.CoopLogin);
            AsyncOperation operation = SceneManager.LoadSceneAsync(
                CoopLoginScenePath, LoadSceneMode.Single);
            if (operation == null)
            {
                LastFailure = "服务器已取消加载，但合作房间界面恢复失败。";
                returnRoutine = null;
                returningToLobby = false;
                yield break;
            }
            while (!operation.isDone) yield return null;
        }
        else if (GameModeContext.IsTransitioning ||
                 !GameModeContext.IsActive(GameModeId.Coop,
                     GameModeStage.CoopLogin))
        {
            // Cancellation can arrive before CityNew replaces CoopLogin.
            // Loading the same scene is unnecessary, but the pending battle
            // transition must still be completed as a lobby transition.
            GameModeContext.BeginTransition(GameModeId.Coop,
                GameModeStage.CoopLogin);
            GameModeContext.TryActivate(GameModeId.Coop,
                GameModeStage.CoopLogin, out _);
        }
        // This route does not go through GameModeFlowController. In particular,
        // a failed connection may leave no PlayerController to unlock the cursor
        // when CityNew unloads, and scene teardown can change it once more.
        RestoreLobbyInteraction();
        returnRoutine = null;
        returningToLobby = false;
        // A new allocation can arrive while the previous scene was unloading.
        // Only the currently advertised epoch may now start a fresh load.
        if (session != null && session.HasActiveSession &&
            string.Equals(session.LobbyPhase, CoopSessionController.PhaseLoading,
                StringComparison.Ordinal))
            HandleLoadRequested();
    }

    private void CancelPendingLoad()
    {
        loadGeneration++;
        if (loadRoutine != null) StopCoroutine(loadRoutine);
        loadRoutine = null;
        loadingEpoch = string.Empty;
    }

    private bool IsCurrentLoad(string epoch, int generation)
    {
        return isActiveAndEnabled && session != null &&
               generation == loadGeneration && session.HasActiveSession &&
               string.Equals(epoch, session.SceneLoadEpoch, StringComparison.Ordinal) &&
               (string.Equals(session.LobbyPhase, CoopSessionController.PhaseLoading,
                    StringComparison.Ordinal) ||
                string.Equals(session.LobbyPhase, CoopSessionController.PhaseBattle,
                    StringComparison.Ordinal));
    }

    private void RestoreLobbyInteraction()
    {
        foreach (GameplayLockCoordinator coordinator in FindObjectsByType<
                     GameplayLockCoordinator>(FindObjectsInactive.Include,
                     FindObjectsSortMode.None))
            coordinator.ResetForSceneTransition();
        session?.RestoreLobbyInteraction();
        Time.timeScale = 1f;
        CoopUiInputGate.PauseMenuVisible = false;
        CoopUiInputGate.EconomyModalVisible = false;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }
}
