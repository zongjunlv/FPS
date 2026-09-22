using FPS.Core.GameModes;
using FPS.Networking.Session;
using UnityEngine;
using UnityEngine.SceneManagement;

[DefaultExecutionOrder(-500)]
[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerController), typeof(CoopNetworkInputBridge))]
public sealed class CoopBattlePauseController : MonoBehaviour
{
    private PlayerController player;
    private PlayerInputReader input;
    private CoopNetworkInputBridge networkInput;
    private CoopSessionOverlay overlay;
    private CoopReconnectPresentationGate presentationGate;
    private CoopSessionController session;
    private GameModeFlowController flow;
    private bool leaving;

    public CoopBattlePauseView View { get; private set; }
    public bool IsMenuOpen => View != null && View.IsVisible;
    public bool IsLeaving => leaving;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void RegisterSceneInstaller()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    private static void HandleSceneLoaded(
        Scene scene,
        LoadSceneMode loadMode)
    {
        Install();
    }

    private static void Install()
    {
        bool coopBattle = GameModeContext.IsActive(
                              GameModeId.Coop,
                              GameModeStage.CoopBattle) ||
                          GameModeContext.RequestedMode == GameModeId.Coop &&
                          GameModeContext.RequestedStage ==
                          GameModeStage.CoopBattle;
        if (!coopBattle) return;

        PlayerController player = FindFirstObjectByType<PlayerController>();
        if (player != null &&
            player.GetComponent<CoopBattlePauseController>() == null)
        {
            player.gameObject.AddComponent<CoopBattlePauseController>();
        }
    }

    private void Awake()
    {
        player = GetComponent<PlayerController>();
        input = GetComponent<PlayerInputReader>();
        networkInput = GetComponent<CoopNetworkInputBridge>();
        player.SetPauseInputHandledExternally(true);
        View = CoopBattlePauseView.Create(transform, this);
    }

    private void Update()
    {
        ResolveDependencies();
        if (leaving || input == null || !input.PausePressed) return;
        if (IsMenuOpen)
        {
            CloseMenu();
        }
        else
        {
            OpenMenu();
        }
    }

    private void LateUpdate()
    {
        if (!IsMenuOpen) return;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    public void OpenMenu()
    {
        if (leaving || View == null || IsMenuOpen) return;
        ResolveDependencies();
        overlay?.SetVisible(false);
        player.TrySetAiming(false);
        networkInput?.SetLocalMenuSuppressed(true);
        if (presentationGate != null)
            presentationGate.MenuOverlayVisible = true;
        View.SetStatus(string.Empty);
        View.SetVisible(true);
    }

    public void CloseMenu()
    {
        if (leaving || View == null || !IsMenuOpen) return;
        View.SetVisible(false);
        if (presentationGate != null)
            presentationGate.MenuOverlayVisible = false;
        networkInput?.SetLocalMenuSuppressed(false);
    }

    public void TryContinueBattle()
    {
        ResolveDependencies();
        if (session == null || session.State != CoopSessionState.Failed)
        {
            CloseMenu();
            return;
        }
        _ = RetryAndContinueAsync();
    }

    private async System.Threading.Tasks.Task RetryAndContinueAsync()
    {
        View?.SetInteractionEnabled(false);
        View?.SetStatus("正在重新连接专用服务器…");
        bool connected = await session.RetryBattleTransportAsync();
        if (this == null || View == null) return;
        View.SetInteractionEnabled(true);
        if (connected)
        {
            CloseMenu();
            return;
        }
        View.SetStatus(string.IsNullOrWhiteSpace(session.LastFailure)
            ? "重新连接失败，请返回大厅后重试。"
            : session.LastFailure);
    }

    public bool TryReturnToModeEntry()
    {
        if (leaving) return false;
        ResolveDependencies();
        if (flow == null || flow.IsLoading || !flow.TryReturnToEntry())
        {
            View?.SetStatus(flow != null &&
                            !string.IsNullOrWhiteSpace(flow.FailureMessage)
                ? flow.FailureMessage
                : "暂时无法退出联机战斗，请稍后重试。");
            return false;
        }

        leaving = true;
        View?.SetInteractionEnabled(false);
        networkInput?.SetLocalMenuSuppressed(true);
        return true;
    }

    private void ResolveDependencies()
    {
        networkInput ??= GetComponent<CoopNetworkInputBridge>();
        overlay ??= FindFirstObjectByType<CoopSessionOverlay>();
        presentationGate ??=
            FindFirstObjectByType<CoopReconnectPresentationGate>();
        session ??= FindFirstObjectByType<CoopSessionController>();
        flow ??= GameModeFlowController.Instance ??
                 GameModeFlowController.Ensure(GameModeCatalog.LoadDefault());
    }

    private void OnDestroy()
    {
        if (presentationGate != null)
            presentationGate.MenuOverlayVisible = false;
        networkInput?.SetLocalMenuSuppressed(false);
        player?.SetPauseInputHandledExternally(false);
    }
}
