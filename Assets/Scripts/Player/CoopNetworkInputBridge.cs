using System.Collections.Generic;
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
    private Health health;
    private CoopSessionController session;
    private CoopSessionOverlay overlay;
    private NetworkVerticalSliceInputDriver networkDriver;
    private NetworkVerticalSliceInputDriver subscribedDriver;
    private NetworkPlayerReplica subscribedReplica;
    private float nextDriverSearchTime;
    private bool combatSuppressed;
    private bool overlaySuppressed;
    private bool networkCrouching;
    private bool networkControlApplied;
    private string lastAmmoWeaponId;
    private int lastServerMagazine = -1;
    private int lastServerReserve = -1;
    private uint lastServerAcknowledgedSequence;
    private float lastServerHealth = -1f;
    private float lastServerMaximumHealth = -1f;
    private float lastServerArmor = -1f;
    private float lastServerMaximumArmor = -1f;
    private bool lastServerAlive;
    private readonly List<PredictedShot> pendingPredictedShots = new();

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
        health = GetComponent<Health>();
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
            SubscribeDriver(null);
            return;
        }

        ResolveLocalDriver();
        if (networkDriver == null) return;
        ReconcileCombatState();
        HandleCombatPresentationInput();
        bool jumpRequested = input.JumpPressed;
        if (input.CrouchPressed)
            networkCrouching = !networkCrouching;
        if (jumpRequested && networkCrouching)
        {
            networkCrouching = false;
            jumpRequested = false;
        }
        WeaponController weapon = combat?.EquippedWeapon;
        if (weapon != null)
            networkDriver.SetCombatFrame(
                weapon.StableId,
                weapon.MuzzleTransform.position);
        bool wantsToFire = weapon != null &&
            (weapon.IsAutomatic ? input.AttackHeld : input.AttackPressed);
        bool predictedFire = wantsToFire &&
            combat.TryPredictNetworkFire();
        networkDriver.SetInputFrame(
            input.Move,
            transform.eulerAngles.y,
            player.CameraPitch,
            predictedFire,
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
        SubscribeDriver(null);
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

        SubscribeDriver(null);
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
                SubscribeDriver(drivers[index]);
                SubscribeReplica(replica);
                break;
            }
        }
    }

    private void SubscribeDriver(NetworkVerticalSliceInputDriver driver)
    {
        if (subscribedDriver == driver)
        {
            networkDriver = driver;
            return;
        }
        if (subscribedDriver != null)
            subscribedDriver.CommandSubmitted -= HandleCommandSubmitted;
        subscribedDriver = driver;
        networkDriver = driver;
        pendingPredictedShots.Clear();
        if (subscribedDriver != null)
            subscribedDriver.CommandSubmitted += HandleCommandSubmitted;
    }

    private void HandleCommandSubmitted(NetcodePlayerCommand command)
    {
        if (!command.Fire) return;
        pendingPredictedShots.Add(new PredictedShot(
            command.Sequence,
            command.WeaponId.ToString()));
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
        lastAmmoWeaponId = null;
        lastServerMagazine = -1;
        lastServerReserve = -1;
        lastServerAcknowledgedSequence = 0;
        lastServerHealth = -1f;
        lastServerMaximumHealth = -1f;
        lastServerArmor = -1f;
        lastServerMaximumArmor = -1f;
        lastServerAlive = false;
        if (subscribedReplica != null)
            subscribedReplica.PosePresented += HandleNetworkPose;
    }

    private void ReconcileCombatState()
    {
        if (subscribedReplica == null || combat == null ||
            !subscribedReplica.HasConsumedServerState) return;
        string weaponId = subscribedReplica.CombatWeaponId;
        int magazine = subscribedReplica.PresentedMagazineAmmo;
        int reserve = subscribedReplica.PresentedReserveAmmo;
        uint acknowledged =
            subscribedReplica.PresentedAcknowledgedSequence;
        ReconcileVitals();
        for (int index = pendingPredictedShots.Count - 1;
             index >= 0;
             index--)
        {
            if (pendingPredictedShots[index].Sequence <= acknowledged)
                pendingPredictedShots.RemoveAt(index);
        }
        int outstandingShots = 0;
        for (int index = 0; index < pendingPredictedShots.Count; index++)
        {
            if (string.Equals(pendingPredictedShots[index].WeaponId,
                    weaponId, System.StringComparison.Ordinal))
                outstandingShots++;
        }
        int predictedMagazine = Mathf.Max(0, magazine - outstandingShots);
        if (string.Equals(lastAmmoWeaponId, weaponId,
                System.StringComparison.Ordinal) &&
            lastServerMagazine == magazine &&
            lastServerReserve == reserve &&
            lastServerAcknowledgedSequence == acknowledged) return;
        lastAmmoWeaponId = weaponId;
        lastServerMagazine = magazine;
        lastServerReserve = reserve;
        lastServerAcknowledgedSequence = acknowledged;
        combat.ReconcileAuthoritativeAmmo(
            weaponId, predictedMagazine, reserve);
    }

    private void ReconcileVitals()
    {
        if (health == null || subscribedReplica == null) return;
        float currentHealth = subscribedReplica.PresentedHealth;
        float maximumHealth = subscribedReplica.PresentedMaximumHealth;
        float armor = subscribedReplica.PresentedArmor;
        float maximumArmor = subscribedReplica.PresentedMaximumArmor;
        bool alive = subscribedReplica.PresentedAlive;
        if (Mathf.Approximately(lastServerHealth, currentHealth) &&
            Mathf.Approximately(lastServerMaximumHealth, maximumHealth) &&
            Mathf.Approximately(lastServerArmor, armor) &&
            Mathf.Approximately(lastServerMaximumArmor, maximumArmor) &&
            lastServerAlive == alive)
            return;
        lastServerHealth = currentHealth;
        lastServerMaximumHealth = maximumHealth;
        lastServerArmor = armor;
        lastServerMaximumArmor = maximumArmor;
        lastServerAlive = alive;
        health.ReconcileAuthoritativeVitals(
            maximumHealth, currentHealth, maximumArmor, armor, alive);
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

    private readonly struct PredictedShot
    {
        public PredictedShot(uint sequence, string weaponId)
        {
            Sequence = sequence;
            WeaponId = weaponId ?? string.Empty;
        }

        public uint Sequence { get; }
        public string WeaponId { get; }
    }
}
