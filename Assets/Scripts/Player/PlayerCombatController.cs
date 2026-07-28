using Unity.VisualScripting;
using UnityEngine;

[RequireComponent(typeof(PlayerInputReader), typeof(PlayerRecoilController))]
[RequireComponent(typeof(PlayerController))]

public class PlayerCombatController : MonoBehaviour
{
    [SerializeField] private WeaponController equippedWeapon;
    
    private PlayerRecoilController playerRecoil;
    private PlayerController playerController;

    private PlayerInputReader input;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        input = GetComponent<PlayerInputReader>();
        playerRecoil = GetComponent<PlayerRecoilController>();
        playerController = GetComponent<PlayerController>();
    }

    // 在 PlayerController 更新运动与暂停状态后处理战斗输入。
    private void LateUpdate()
    {
        bool wantsToFire = equippedWeapon.IsAutomatic? input.AttackHeld : input.AttackPressed;
        if(wantsToFire &&
           !playerController.IsPaused &&
           !playerController.IsSprinting &&
           equippedWeapon.TryFire())
        {
            playerRecoil.AddRecoil(equippedWeapon.VerticalRecoil, equippedWeapon.HorizontalRecoil);
        }

    }
}
