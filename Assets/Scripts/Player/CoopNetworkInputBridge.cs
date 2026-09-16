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
    private NetworkPlayerReplica subscribedReplica;
    private float nextDriverSearchTime;
    private bool combatSuppressed;
    private bool overlaySuppressed;
    private bool networkCrouching;
    private bool networkControlApplied;

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
        SetNetworkMovementControlled(connected);
        if (!connected || input == null || player == null)
        {
            SubscribeReplica(null);
            networkDriver = null;
            return;
        }

        ResolveLocalDriver();
        if (networkDriver == null) return;
        HandleCombatPresentationInput();
        bool jumpRequested = input.JumpPressed;
        if (input.CrouchPressed)
            networkCrouching = !networkCrouching;
        if (jumpRequested && networkCrouching)
        {
            networkCrouching = false;
            jumpRequested = false;
        }
        networkDriver.SetInputFrame(
            input.Move,
            transform.eulerAngles.y,
            player.CameraPitch,
            input.AttackPressed,
            jumpRequested,
            input.SprintHeld,
            networkCrouching,
            player.IsAiming);
    }

    private void OnDisable()
    {
        SetCombatSuppressed(false);
        SetOverlaySuppressed(false);
        SetNetworkMovementControlled(false);
        SubscribeReplica(null);
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
                SubscribeReplica(replica);
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

    private void SetNetworkMovementControlled(bool controlled)
    {
        if (networkControlApplied == controlled) return;
        networkControlApplied = controlled;
        networkCrouching = player != null && player.IsCrouching;
        player?.SetNetworkMovementControlled(controlled);
    }

    private void SubscribeReplica(NetworkPlayerReplica replica)
    {
        if (subscribedReplica == replica) return;
        if (subscribedReplica != null)
            subscribedReplica.PosePresented -= HandleNetworkPose;
        subscribedReplica = replica;
        if (subscribedReplica != null)
            subscribedReplica.PosePresented += HandleNetworkPose;
    }

    private void HandleNetworkPose(
        Vector3 position,
        float yaw,
        float pitch,
        bool crouching,
        bool grounded)
    {
        networkCrouching = crouching;
        player?.ApplyNetworkMovementPose(
            position, yaw, pitch, crouching, grounded);
    }

    private void HandleCombatPresentationInput()
    {
        if (combat == null || networkDriver == null) return;

        int requestedSlot = input.ConsumeWeaponSelection();
        int cycleDirection = input.ConsumeWeaponCycleDirection();
        int targetIndex = requestedSlot;
        if (targetIndex < 0 && cycleDirection != 0 &&
            combat.WeaponCount > 0)
        {
            int step = cycleDirection > 0 ? 1 : -1;
            targetIndex = (combat.EquippedWeaponIndex + step +
                combat.WeaponCount) % combat.WeaponCount;
        }
        if (targetIndex >= 0 && targetIndex < combat.WeaponCount &&
            combat.TrySelectWeapon(targetIndex))
        {
            string weaponId = NetworkPresentationIds.ResolveWeaponOrDefault(
                combat.GetWeapon(targetIndex)?.StableId);
            networkDriver.SubmitPresentationAction(
                NetworkPresentationAction.SwitchWeapon,
                weaponId);
        }

        if (!input.ConsumeReloadPressed() || combat.IsSwitching ||
            combat.EquippedWeapon == null ||
            !combat.EquippedWeapon.TryStartReload()) return;
        player.TrySetAiming(false);
        networkDriver.SubmitPresentationAction(
            NetworkPresentationAction.Reload,
            NetworkPresentationIds.ResolveWeaponOrDefault(
                combat.EquippedWeapon.StableId));
    }
}
