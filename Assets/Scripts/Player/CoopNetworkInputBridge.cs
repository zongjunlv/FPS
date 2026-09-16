using FPS.Core.GameModes;
using FPS.Networking.Netcode;
using FPS.Networking.Session;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class CoopNetworkInputBridge : MonoBehaviour
{
    private const float DriverSearchInterval = 0.25f;

    private PlayerInputReader input;
    private PlayerController player;
    private PlayerCombatController combat;
    private CoopSessionController session;
    private CoopSessionOverlay overlay;
    private NetworkVerticalSliceInputDriver networkDriver;
    private float nextDriverSearchTime;
    private bool combatSuppressed;
    private bool overlaySuppressed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        if (GameModeContext.RequestedMode != GameModeId.Coop &&
            !CoopSessionRuntimeBootstrap.ShouldInstallForCurrentMode)
        {
            return;
        }

        PlayerController player = FindFirstObjectByType<PlayerController>();
        if (player != null &&
            player.GetComponent<CoopNetworkInputBridge>() == null)
        {
            player.gameObject.AddComponent<CoopNetworkInputBridge>();
        }
    }

    private void Awake()
    {
        input = GetComponent<PlayerInputReader>();
        player = GetComponent<PlayerController>();
        combat = GetComponent<PlayerCombatController>();
    }

    private void Update()
    {
        ResolveSession();
        ResolveOverlay();
        SetOverlaySuppressed(overlay != null && overlay.IsVisible);
        bool connected = session != null && session.IsConnected;
        SetCombatSuppressed(connected);
        if (!connected || input == null || player == null)
        {
            networkDriver = null;
            return;
        }

        ResolveLocalDriver();
        if (networkDriver == null) return;
        networkDriver.SetInputFrame(
            input.Move,
            transform.eulerAngles.y,
            player.CameraPitch,
            input.AttackPressed);
    }

    private void OnDisable()
    {
        SetCombatSuppressed(false);
        SetOverlaySuppressed(false);
    }

    private void ResolveOverlay()
    {
        if (overlay == null)
        {
            overlay = FindFirstObjectByType<CoopSessionOverlay>();
        }
    }

    private void ResolveSession()
    {
        if (session == null)
        {
            session = FindFirstObjectByType<CoopSessionController>();
        }
    }

    private void ResolveLocalDriver()
    {
        if (networkDriver != null &&
            networkDriver.GetComponent<NetworkPlayerReplica>()
                .IsLocallyControlled)
        {
            return;
        }
        if (Time.unscaledTime < nextDriverSearchTime) return;
        nextDriverSearchTime = Time.unscaledTime + DriverSearchInterval;

        networkDriver = null;
        NetworkVerticalSliceInputDriver[] drivers =
            FindObjectsByType<NetworkVerticalSliceInputDriver>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);
        for (int index = 0; index < drivers.Length; index++)
        {
            NetworkPlayerReplica replica =
                drivers[index].GetComponent<NetworkPlayerReplica>();
            if (replica != null && replica.IsLocallyControlled)
            {
                networkDriver = drivers[index];
                break;
            }
        }
    }

    private void SetCombatSuppressed(bool suppressed)
    {
        if (combatSuppressed == suppressed) return;
        combatSuppressed = suppressed;
        combat?.SetGameplayInputEnabled(!suppressed);
    }

    private void SetOverlaySuppressed(bool suppressed)
    {
        if (overlaySuppressed == suppressed) return;
        overlaySuppressed = suppressed;
        player?.SetGameplayInputEnabled(!suppressed);
    }
}
