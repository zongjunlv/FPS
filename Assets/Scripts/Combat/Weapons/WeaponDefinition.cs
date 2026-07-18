using System;
using UnityEngine;

[CreateAssetMenu(fileName = "Weapon", menuName = "Scriptable Objects/Weapon")]
public class WeaponDefinition : ScriptableObject
{
    public String WeaponName;
    public int MagazineCapcity;
    public float FireIntervel;
    public bool IsAutomatic;
    [Header("Recoil")]
    [Min(0f)] public float HorizontalRecoil = 2f;
    [Min(0f)] public float VerticalRecoil = 0.4f;
}
