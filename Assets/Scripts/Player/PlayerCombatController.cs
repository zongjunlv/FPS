using Unity.VisualScripting;
using UnityEngine;

[RequireComponent(typeof(PlayerInputReader), typeof(PlayerRecoilController))]

public class PlayerCombatController : MonoBehaviour
{
    [SerializeField] private WeaponController equippedWeapon;
    
    private PlayerRecoilController playerRecoil;

    private PlayerInputReader input;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        input = GetComponent<PlayerInputReader>();
        playerRecoil = GetComponent<PlayerRecoilController>();
    }

    // Update is called once per frame
    void Update()
    {
        bool wantsToFire = equippedWeapon.IsAutomatic? input.AttackHeld : input.AttackPressed;
        if(wantsToFire && !input.SprintHeld && equippedWeapon.TryFire())
        {
            playerRecoil.AddRecoil(equippedWeapon.VerticalRecoil, equippedWeapon.HorizontalRecoil);
        }

    }
}
