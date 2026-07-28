using UnityEngine;

[RequireComponent(typeof(PlayerInputReader), typeof(PlayerRecoilController))]
[RequireComponent(typeof(PlayerController))]

public class PlayerCombatController : MonoBehaviour
{
    [SerializeField] private WeaponController equippedWeapon;
    
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

        if (ammoHud == null)
        {
            ammoHud = gameObject.AddComponent<AmmoHudPresenter>();
        }

        ammoHud.Bind(equippedWeapon);
        equippedWeapon.ReloadStateChanged += HandleReloadStateChanged;
    }

    private void OnDestroy()
    {
        if (equippedWeapon != null)
        {
            equippedWeapon.ReloadStateChanged -= HandleReloadStateChanged;
        }
    }

    // 在 PlayerController 更新运动与暂停状态后处理战斗输入。
    private void LateUpdate()
    {
        if (playerController.IsSprinting)
        {
            equippedWeapon.CancelReload();
        }

        if (input.ConsumeReloadPressed() &&
            !playerController.IsPaused &&
            !playerController.IsSprinting &&
            equippedWeapon.TryStartReload())
        {
            playerController.TrySetAiming(false);
        }

        bool wantsToFire = equippedWeapon.IsAutomatic
            ? input.AttackHeld
            : input.AttackPressed;

        if (wantsToFire &&
            !playerController.IsPaused &&
            !playerController.IsSprinting &&
            equippedWeapon.TryFire())
        {
            playerRecoil.AddRecoil(
                equippedWeapon.VerticalRecoil,
                equippedWeapon.HorizontalRecoil);
        }
    }

    private void HandleReloadStateChanged()
    {
        if (equippedWeapon.IsReloading)
        {
            playerAnimator.PlayReloadAnimation(
                equippedWeapon.CurrentAmmo == 0);
            return;
        }

        playerAnimator.StopReloadAnimation();
    }
}
