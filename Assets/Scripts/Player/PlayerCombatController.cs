using UnityEngine;

[RequireComponent(typeof(PlayerInputReader), typeof(PlayerRecoilController))]
[RequireComponent(typeof(PlayerController), typeof(WeaponLoadoutController))]
public class PlayerCombatController : MonoBehaviour
{
    [SerializeField] private WeaponLoadoutController loadout;

    public WeaponController EquippedWeapon { get; private set; }
    public int EquippedWeaponIndex => loadout.CurrentIndex;
    public int WeaponCount => loadout.WeaponCount;
    public bool IsSwitching => loadout.IsSwitching;

    private PlayerRecoilController playerRecoil;
    private PlayerController playerController;
    private PlayerAnimatorController playerAnimator;
    private PlayerInputReader input;
    private AmmoHudPresenter ammoHud;

    private void Start()
    {
        input = GetComponent<PlayerInputReader>();
        playerRecoil = GetComponent<PlayerRecoilController>();
        playerController = GetComponent<PlayerController>();
        playerAnimator = GetComponent<PlayerAnimatorController>();
        ammoHud = GetComponent<AmmoHudPresenter>();

        if (loadout == null)
        {
            loadout = GetComponent<WeaponLoadoutController>();
        }

        if (ammoHud == null)
        {
            ammoHud = gameObject.AddComponent<AmmoHudPresenter>();
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
        }
    }

    private void LateUpdate()
    {
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

        if (wantsToFire &&
            !playerController.IsPaused &&
            !playerController.IsSprinting &&
            EquippedWeapon.TryFire())
        {
            playerRecoil.AddRecoil(
                EquippedWeapon.VerticalRecoil,
                EquippedWeapon.HorizontalRecoil);
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
        }

        EquippedWeapon = weapon;
        EquippedWeapon.ReloadStateChanged +=
            HandleReloadStateChanged;
        EquippedWeapon.ConfigureAiming(
            playerController.AimCamera,
            transform);
        ammoHud.Bind(EquippedWeapon);
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
