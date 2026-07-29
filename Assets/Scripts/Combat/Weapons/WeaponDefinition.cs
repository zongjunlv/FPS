using System;
using UnityEngine;
using UnityEngine.Serialization;

[CreateAssetMenu(fileName = "Weapon", menuName = "Scriptable Objects/Weapon")]
public class WeaponDefinition : ScriptableObject
{
    public String WeaponName;
    [FormerlySerializedAs("MagazineCapcity")]
    [Min(1)] public int MagazineCapacity = 30;
    [Min(0)] public int InitialReserveAmmo = 120;
    [Min(0.01f)] public float ReloadDuration = 2.4f;
    [Min(0f)] public float Damage = 10f;
    public AudioClip FireSound;
    public AudioClip DryFireSound;
    public AudioClip ReloadSound;
    public AudioClip EmptyReloadSound;
    public AudioClip HolsterSound;
    public AudioClip UnholsterSound;
    public RuntimeAnimatorController CharacterAnimatorController;
    public float FireIntervel;
    public bool IsAutomatic;
    [Header("Recoil")]
    [Min(0f)] public float HorizontalRecoil = 2f;
    [Min(0f)] public float VerticalRecoil = 0.4f;
    [Range(0.1f, 1f)] public float AdsRecoilMultiplier = 0.55f;
    [Header("Accuracy")]
    [Min(0f)] public float HipSpreadDegrees = 0.65f;
    [Min(0f)] public float AdsSpreadDegrees = 0.12f;
    [Min(0f)] public float MovementSpreadBonus = 0.8f;
    [Min(0f)] public float SprintSpreadBonus = 2.5f;
    [Min(0f)] public float SpreadPerShot = 0.18f;
    [Min(0f)] public float MaxShotSpread = 1.5f;
    [Min(0f)] public float SpreadRecoverySpeed = 3f;
}
