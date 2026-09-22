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
    private string loadingEpoch = string.Empty;

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
    }

    private void OnDisable()
    {
        if (session == null) return;
        session.LobbyStartRequested -= HandleLoadRequested;
        session.BattleSceneReady -= HandleBattleReady;
        session.SceneLoadCancelled -= HandleLoadCancelled;
        session.ReturnedToLobby -= HandleReturnedToLobby;
    }

    private void Update()
    {
        session?.TickSceneLoadTimeout(Time.realtimeSinceStartupAsDouble);
        session?.EnsureBattleTransportForCurrentPhase();
    }

    private void HandleLoadRequested()
    {
        if (session == null || string.IsNullOrWhiteSpace(
                session.SceneLoadEpoch)) return;
        if (loadRoutine != null && string.Equals(loadingEpoch,
                session.SceneLoadEpoch, StringComparison.Ordinal)) return;
        if (loadRoutine != null) StopCoroutine(loadRoutine);
        loadingEpoch = session.SceneLoadEpoch;
        loadRoutine = StartCoroutine(LoadCityNew(loadingEpoch));
    }

    private IEnumerator LoadCityNew(string epoch)
    {
        LastFailure = string.Empty;
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
            while (!operation.isDone) yield return null;
        }
        yield return null;

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
                   Time.realtimeSinceStartup < environmentDeadline)
                yield return null;
            if (!CoopEnvironmentReadinessRegistry.IsCityNewReady)
            {
                LastFailure = "CityNew 导航环境准备超时，已阻止不完整战局启动。";
                _ = session.CancelSceneLoadAsync(LastFailure);
                loadRoutine = null;
                yield break;
            }
        }

        Task<bool> report = session.ReportSceneReadyAsync(epoch);
        while (!report.IsCompleted) yield return null;
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
        if (loadRoutine != null)
        {
            StopCoroutine(loadRoutine);
            loadRoutine = null;
        }
        if (returnRoutine == null)
            returnRoutine = StartCoroutine(ReturnToLobby());
    }

    private void HandleReturnedToLobby()
    {
        NetworkManager manager = NetworkManager.Singleton;
        if (manager != null && manager.IsServer)
        {
            CoopNetworkRuntimeInstaller installer =
                FindFirstObjectByType<CoopNetworkRuntimeInstaller>();
            installer?.PrepareForLobbyReturn();
        }
        if (returnRoutine == null)
            returnRoutine = StartCoroutine(ReturnToLobby());
    }

    private IEnumerator ReturnToLobby()
    {
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
                yield break;
            }
            while (!operation.isDone) yield return null;
        }
        returnRoutine = null;
    }
}
