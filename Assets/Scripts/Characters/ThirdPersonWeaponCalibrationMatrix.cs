using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

[Serializable]
public struct ThirdPersonWeaponCalibrationEntry
{
    [SerializeField] private string appearanceId;
    [SerializeField] private string weaponId;

    public string AppearanceId => appearanceId;
    public string WeaponId => weaponId;

    public ThirdPersonWeaponCalibrationEntry(string character, string weapon)
    {
        appearanceId = character?.Trim() ?? string.Empty;
        weaponId = weapon?.Trim() ?? string.Empty;
    }
}

[CreateAssetMenu(fileName = "ThirdPersonWeaponCalibrationMatrix",
    menuName = "FPS/Characters/Third Person Weapon Calibration Matrix")]
public sealed class ThirdPersonWeaponCalibrationMatrix : ScriptableObject
{
    public const string DefaultAssetPath =
        "Assets/Resources/Content/Weapons/ThirdPerson/" +
        "ThirdPersonWeaponCalibrationMatrix.asset";
    public const string ResourcesPath =
        "Content/Weapons/ThirdPerson/ThirdPersonWeaponCalibrationMatrix";

    [SerializeField] private ThirdPersonWeaponCalibrationEntry[] entries =
        Array.Empty<ThirdPersonWeaponCalibrationEntry>();

    public IReadOnlyList<ThirdPersonWeaponCalibrationEntry> Entries =>
        entries ?? Array.Empty<ThirdPersonWeaponCalibrationEntry>();

    public void Configure(IEnumerable<ThirdPersonWeaponCalibrationEntry> values)
    {
        entries = values?.ToArray() ??
                  Array.Empty<ThirdPersonWeaponCalibrationEntry>();
    }
}
