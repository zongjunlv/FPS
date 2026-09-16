using FPS.Core.GameModes;
using FPS.Networking.Session;
using UnityEngine;

[DefaultExecutionOrder(-30000)]
[DisallowMultipleComponent]
public sealed class GameModeSceneBootstrap : MonoBehaviour
{
    [SerializeField] private GameModeCatalog catalog;
    [SerializeField] private GameModeSceneMarker marker;

    public GameModeFlowController Flow { get; private set; }
    public Component View { get; private set; }
    public bool IsInitialized { get; private set; }
    public string InitializationError { get; private set; } = string.Empty;

    public void Configure(
        GameModeCatalog configuredCatalog,
        GameModeSceneMarker configuredMarker)
    {
        catalog = configuredCatalog;
        marker = configuredMarker;
    }

    private void Awake()
    {
        Initialize();
    }

    public bool Initialize()
    {
        if (IsInitialized)
        {
            return true;
        }

        catalog ??= GameModeCatalog.LoadDefault();
        marker ??= GetComponent<GameModeSceneMarker>();
        string catalogError = string.Empty;
        if (catalog == null || marker == null ||
            !catalog.TryValidate(out catalogError))
        {
            InitializationError = catalog == null
                ? "模式配置资源缺失。"
                : marker == null
                    ? "当前场景缺少模式标识。"
                    : catalogError;
            Debug.LogError(
                $"[{nameof(GameModeSceneBootstrap)}] {InitializationError}",
                this);
            return false;
        }

        Flow = GameModeFlowController.Ensure(catalog);
        CoopSessionController coopSession = null;
        if (marker.Mode == GameModeId.Coop)
        {
            coopSession = CoopSessionRuntimeBootstrap.EnsureForCurrentMode();
            if (coopSession.GetComponent<CoopSceneLoadCoordinator>() == null)
                coopSession.gameObject.AddComponent<CoopSceneLoadCoordinator>();
        }

        if (marker.Stage == GameModeStage.Entry)
        {
            View = ModeEntryView.Create(Flow, catalog);
        }
        else if (marker.Stage == GameModeStage.BattlePreparation)
        {
            PlayerAppearanceCatalog appearances =
                Resources.Load<PlayerAppearanceCatalog>(
                    PlayerAppearanceCatalog.ResourcesPath);
            View = BattleCharacterSelectionView.Create(Flow, appearances);
        }
        else if (marker.Stage == GameModeStage.CoopLogin)
        {
            PlayerAppearanceCatalog appearances =
                Resources.Load<PlayerAppearanceCatalog>(
                    PlayerAppearanceCatalog.ResourcesPath);
            View = CoopAccountView.Create(Flow,
                new UnityAuthenticationGateway(),
                Debug.isDebugBuild || Application.isEditor,
                coopSession,
                appearances);
        }
        else
        {
            View = ModeDestinationView.Create(Flow, marker.Mode, marker.Stage);
        }
        IsInitialized = View != null;
        InitializationError = IsInitialized
            ? string.Empty
            : "模式界面创建失败。";
        return IsInitialized;
    }
}
