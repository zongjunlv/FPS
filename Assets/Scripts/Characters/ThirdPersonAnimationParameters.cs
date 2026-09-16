using UnityEngine;

public enum ThirdPersonCombatAction : byte
{
    Shoot = 1,
    Reload = 2,
    SwitchWeapon = 3
}

public static class ThirdPersonAnimationParameters
{
    public const string BaseLayerName = "Base Locomotion";
    public const string UpperBodyLayerName = "Upper Body";
    public const string MoveXName = "MoveX";
    public const string MoveYName = "MoveY";
    public const string SpeedName = "Speed";
    public const string VerticalSpeedName = "VerticalSpeed";
    public const string GroundedName = "Grounded";
    public const string CrouchingName = "Crouching";
    public const string AimingName = "Aiming";
    public const string AimPitchName = "AimPitch";
    public const string ShootName = "Shoot";
    public const string ReloadName = "Reload";
    public const string SwitchWeaponName = "SwitchWeapon";

    public static readonly int MoveX = Animator.StringToHash(MoveXName);
    public static readonly int MoveY = Animator.StringToHash(MoveYName);
    public static readonly int Speed = Animator.StringToHash(SpeedName);
    public static readonly int VerticalSpeed =
        Animator.StringToHash(VerticalSpeedName);
    public static readonly int Grounded = Animator.StringToHash(GroundedName);
    public static readonly int Crouching =
        Animator.StringToHash(CrouchingName);
    public static readonly int Aiming = Animator.StringToHash(AimingName);
    public static readonly int AimPitch = Animator.StringToHash(AimPitchName);
    public static readonly int Shoot = Animator.StringToHash(ShootName);
    public static readonly int Reload = Animator.StringToHash(ReloadName);
    public static readonly int SwitchWeapon =
        Animator.StringToHash(SwitchWeaponName);
}
