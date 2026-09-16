using FPS.Core.GameModes;
using UnityEngine;

[DefaultExecutionOrder(-900)]
[DisallowMultipleComponent]
public sealed class CityNewPlayerModeInstaller : MonoBehaviour
{
    public bool IsInitialized { get; private set; }
    public string InitializationError { get; private set; }
    public PlayerAppearanceHost AppearanceHost { get; private set; }

    private void Start()
    {
        TryInstall();
    }

    public bool TryInstall()
    {
        if (!GameModeContext.IsActive(
                GameModeId.SoloBattle,
                GameModeStage.Battle))
        {
            InitializationError =
                "当前模式不是单人战斗，已跳过 CityNew 玩家安装。";
            enabled = false;
            return false;
        }

        if (IsInitialized)
        {
            return true;
        }

        GameObject player = GameObject.FindGameObjectWithTag("Player");

        string rigError = string.Empty;
        PlayerGameplayRig rig = player != null
            ? player.GetComponent<PlayerGameplayRig>()
            : null;

        if (player == null || rig == null || !rig.TryValidate(out rigError))
        {
            InitializationError = player == null
                ? "CityNew could not find the tagged player gameplay rig."
                : $"CityNew player gameplay rig is invalid: {rigError}";
            Debug.LogError(
                $"[{nameof(CityNewPlayerModeInstaller)}] {InitializationError}",
                this);
            enabled = false;
            return false;
        }

        GetOrAdd<CityNewTerminalMissionBootstrap>(player);
        GetOrAdd<CityNewInventoryBootstrap>(player);
        if (!TryInstallAppearance(player))
        {
            enabled = false;
            return false;
        }
        IsInitialized = true;
        InitializationError = string.Empty;
        return true;
    }

    private bool TryInstallAppearance(GameObject player)
    {
        PlayerAppearanceCatalog catalog = Resources.Load<PlayerAppearanceCatalog>(
            PlayerAppearanceCatalog.ResourcesPath);
        string catalogError = "外观目录资源不存在。";
        if (catalog == null || !catalog.TryValidate(out catalogError))
        {
            InitializationError = string.IsNullOrWhiteSpace(catalogError)
                ? "CityNew 玩家外观目录缺失。"
                : $"CityNew 玩家外观目录无效：{catalogError}";
            Debug.LogError($"[{nameof(CityNewPlayerModeInstaller)}] " +
                           InitializationError, this);
            return false;
        }

        PlayerAppearanceDefinition selected =
            PlayerAppearanceSelection.LoadOrDefault(catalog,
                out bool usedFallback);
        Transform visualRoot = player.transform.Find(
            PlayerAppearanceHost.VisualRootName);
        if (visualRoot == null)
        {
            var visualObject = new GameObject(PlayerAppearanceHost.VisualRootName);
            visualRoot = visualObject.transform;
            visualRoot.SetParent(player.transform, false);
        }

        AppearanceHost = player.GetComponent<PlayerAppearanceHost>();
        if (AppearanceHost == null)
            AppearanceHost = player.AddComponent<PlayerAppearanceHost>();
        AppearanceHost.Configure(catalog, visualRoot, selected.StableId, false);
        AppearanceHost.Apply(selected.StableId);
        AppearanceHost.SetPresentationVisible(false);
        if (usedFallback && !string.IsNullOrWhiteSpace(
                PlayerAppearanceSelection.LastWarning))
            Debug.LogWarning(PlayerAppearanceSelection.LastWarning, this);
        return true;
    }

    private static T GetOrAdd<T>(GameObject target) where T : Component
    {
        return target.TryGetComponent(out T component)
            ? component
            : target.AddComponent<T>();
    }
}
