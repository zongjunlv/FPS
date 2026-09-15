using UnityEngine;

[DefaultExecutionOrder(-900)]
[DisallowMultipleComponent]
public sealed class CityNewPlayerModeInstaller : MonoBehaviour
{
    public bool IsInitialized { get; private set; }
    public string InitializationError { get; private set; }

    private void Start()
    {
        TryInstall();
    }

    public bool TryInstall()
    {
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
        IsInitialized = true;
        InitializationError = string.Empty;
        return true;
    }

    private static T GetOrAdd<T>(GameObject target) where T : Component
    {
        return target.TryGetComponent(out T component)
            ? component
            : target.AddComponent<T>();
    }
}
