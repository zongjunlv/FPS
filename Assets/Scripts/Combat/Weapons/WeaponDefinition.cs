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
}
