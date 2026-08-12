using System;
using UnityEngine;

[RequireComponent(typeof(PlayerInputReader), typeof(PlayerRecoilController))]
[RequireComponent(typeof(PlayerController), typeof(WeaponLoadoutController))]
[RequireComponent(typeof(ShotTracerPool))]
public class PlayerCombatController : MonoBehaviour
{
    [SerializeField] private WeaponLoadoutController loadout;

    public WeaponController EquippedWeapon { get; private set; }
    public int EquippedWeaponIndex => loadout.CurrentIndex;
    public int WeaponCount => loadout.WeaponCount;
    public bool IsSwitching => loadout.IsSwitching;
    public bool GameplayInputEnabled { get; private set; } = true;
    public event Action<ShotResult> ShotResolved;
    public event Action<WeaponController> EquippedWeaponChanged;

    private PlayerRecoilController playerRecoil;
    private PlayerController playerController;
    private PlayerAnimatorController playerAnimator;
    private PlayerInputReader input;
    private AmmoHudPresenter ammoHud;
    private ShotTracerPool tracerPool;

    private void Start()
    {
        input = GetComponent<PlayerInputReader>();
        playerRecoil = GetComponent<PlayerRecoilController>();
        playerController = GetComponent<PlayerController>();
        playerAnimator = GetComponent<PlayerAnimatorController>();
        ammoHud = GetComponent<AmmoHudPresenter>();
        tracerPool = GetComponent<ShotTracerPool>();

        if (tracerPool == null)
        {
            tracerPool = gameObject.AddComponent<ShotTracerPool>();
        }

        if (loadout == null)
        {
            loadout = GetComponent<WeaponLoadoutController>();
        }

        if (ammoHud == null)
        {
            ammoHud = gameObject.AddComponent<AmmoHudPresenter>();
        }

        if (GetComponent<PlayerCombatFeedbackController>() == null)
        {
            gameObject.AddComponent<PlayerCombatFeedbackController>();
        }

        if (GetComponent<PlayerInteractionController>() == null)
        {
            gameObject.AddComponent<PlayerInteractionController>();
        }

        if (GetComponent<CityNewTerminalMissionBootstrap>() == null)
        {
            gameObject.AddComponent<CityNewTerminalMissionBootstrap>();
        }

        if (GetComponent<PlayerRunProgression>() == null)
        {
            gameObject.AddComponent<PlayerRunProgression>();
        }

        if (GetComponent<UnifiedGameHudBootstrap>() == null)
        {
            gameObject.AddComponent<UnifiedGameHudBootstrap>();
        }

        loadout.SwitchStarted += HandleSwitchStarted;
        loadout.SwitchInterrupted += HandleSwitchInterrupted;
        loadout.WeaponPresentationChanged +=
            HandleWeaponPresentationChanged;
        loadout.EquippedWeaponChanged += HandleEquippedWeaponChanged;

        BindEquippedWeapon(loadout.CurrentWeapon);
        playerAnimator.SetWeaponAnimatorController(
            EquippedWeapon.CharacterAnimatorController);
    }

    private void OnDestroy()
    {
        if (loadout != null)
        {
            loadout.SwitchStarted -= HandleSwitchStarted;
            loadout.SwitchInterrupted -= HandleSwitchInterrupted;
            loadout.WeaponPresentationChanged -=
                HandleWeaponPresentationChanged;
            loadout.EquippedWeaponChanged -= HandleEquippedWeaponChanged;
        }

        if (EquippedWeapon != null)
        {
            EquippedWeapon.ReloadStateChanged -=
                HandleReloadStateChanged;
            EquippedWeapon.ShotResolved -= HandleShotResolved;
        }
    }

    private void LateUpdate()
    {
        if (!GameplayInputEnabled)
        {
            return;
        }

        HandleWeaponSelectionInput();

        if (playerController.IsSprinting)
        {
            loadout.Interrupt();
            EquippedWeapon.CancelReload();
        }

        if (loadout.IsSwitching)
        {
            input.ConsumeReloadPressed();
            return;
        }

        if (input.ConsumeReloadPressed() &&
            !playerController.IsPaused &&
            !playerController.IsSprinting &&
            EquippedWeapon.TryStartReload())
        {
            playerController.TrySetAiming(false);
        }

        bool wantsToFire = EquippedWeapon.IsAutomatic
            ? input.AttackHeld
            : input.AttackPressed;

        EquippedWeapon.SetFiringContext(
            playerController.AimBlend,
            input.Move.magnitude,
            playerController.IsSprinting);
        playerRecoil.SetMovementAmount(input.Move.magnitude);

        if (wantsToFire &&
            !playerController.IsPaused &&
            !playerController.IsSprinting &&
            EquippedWeapon.TryFire())
        {
            playerRecoil.AddRecoil(
                EquippedWeapon.CurrentVerticalRecoil,
                EquippedWeapon.CurrentHorizontalRecoil);
        }
    }

    public bool TrySelectWeapon(int targetIndex)
    {
        if (playerController.IsPaused ||
            playerController.IsSprinting)
        {
            return false;
        }

        return loadout.TrySelect(targetIndex);
    }

    public bool CancelWeaponSwitch()
    {
        return loadout.Interrupt();
    }

    public void SetGameplayInputEnabled(bool enabled)
    {
        GameplayInputEnabled = enabled;

        if (enabled || loadout == null)
        {
            return;
        }

        loadout.Interrupt();
        EquippedWeapon?.CancelReload();
    }

    private void HandleWeaponSelectionInput()
    {
        int requestedSlot = input.ConsumeWeaponSelection();
        int cycleDirection = input.ConsumeWeaponCycleDirection();

        if (playerController.IsPaused ||
            playerController.IsSprinting)
        {
            return;
        }

        if (requestedSlot >= 0)
        {
            loadout.TrySelect(requestedSlot);
            return;
        }

        loadout.TryCycle(cycleDirection);
    }

    private void HandleSwitchStarted()
    {
        playerController.TrySetAiming(false);
        playerAnimator.StopReloadAnimation();
        playerAnimator.PlayHolsterAnimation();
    }

    private void HandleWeaponPresentationChanged(
        WeaponController weapon)
    {
        playerAnimator.SetWeaponAnimatorController(
            weapon.CharacterAnimatorController);
        playerAnimator.PlayUnholsterAnimation();
    }

    private void HandleSwitchInterrupted()
    {
        playerAnimator.SetWeaponAnimatorController(
            loadout.CurrentWeapon.CharacterAnimatorController);
        playerAnimator.PlayUnholsterAnimation();
    }

    private void HandleEquippedWeaponChanged(
        WeaponController weapon)
    {
        BindEquippedWeapon(weapon);
    }

    private void BindEquippedWeapon(WeaponController weapon)
    {
        if (EquippedWeapon != null)
        {
            EquippedWeapon.ReloadStateChanged -=
                HandleReloadStateChanged;
            EquippedWeapon.ShotResolved -= HandleShotResolved;
        }

        EquippedWeapon = weapon;
        EquippedWeapon.ReloadStateChanged +=
            HandleReloadStateChanged;
        EquippedWeapon.ShotResolved += HandleShotResolved;
        EquippedWeapon.ConfigureAiming(
            playerController.AimCamera,
            transform,
            tracerPool);
        ammoHud.Bind(EquippedWeapon);
        EquippedWeaponChanged?.Invoke(EquippedWeapon);
    }

    private void HandleShotResolved(ShotResult result)
    {
        ShotResolved?.Invoke(result);
    }

    private void HandleReloadStateChanged()
    {
        if (EquippedWeapon.IsReloading)
        {
            playerAnimator.PlayReloadAnimation(
                EquippedWeapon.CurrentAmmo == 0);
            return;
        }

        playerAnimator.StopReloadAnimation();
    }
}
