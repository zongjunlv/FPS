using System;
using UnityEngine;

public enum ThirdPersonWeaponKind
{
    Rifle,
    Handgun
}

[CreateAssetMenu(fileName = "ThirdPersonWeapon",
    menuName = "FPS/Characters/Third Person Weapon Definition")]
public sealed class ThirdPersonWeaponDefinition : ScriptableObject
{
    [SerializeField] private string stableId;
    [SerializeField] private string displayName;
    [SerializeField] private ThirdPersonWeaponKind kind;
    [SerializeField] private GameObject calibratedPrefab;
    [SerializeField] private bool supportsCasingEjection = true;

    public string StableId => stableId;
    public string DisplayName => displayName;
    public ThirdPersonWeaponKind Kind => kind;
    public GameObject CalibratedPrefab => calibratedPrefab;
    public bool SupportsCasingEjection => supportsCasingEjection;

    public void Configure(
        string id,
        string name,
        ThirdPersonWeaponKind weaponKind,
        GameObject prefab,
        bool hasCasingEjection)
    {
        stableId = id?.Trim() ?? string.Empty;
        displayName = name?.Trim() ?? string.Empty;
        kind = weaponKind;
        calibratedPrefab = prefab;
        supportsCasingEjection = hasCasingEjection;
    }

    public bool TryValidate(out string error)
    {
        if (string.IsNullOrWhiteSpace(stableId) ||
            string.IsNullOrWhiteSpace(displayName) || calibratedPrefab == null)
        {
            error = "武器定义缺少稳定 ID、显示名称或校准 Prefab。";
            return false;
        }
        ThirdPersonWeaponRig rig =
            calibratedPrefab.GetComponent<ThirdPersonWeaponRig>();
        if (rig == null)
        {
            error = $"武器 '{stableId}' 校准 Prefab 缺少 ThirdPersonWeaponRig。";
            return false;
        }
        if (!rig.TryValidate(out error))
        {
            error = $"武器 '{stableId}' 校准无效：{error}";
            return false;
        }
        if (supportsCasingEjection && rig.CasingEjectionPoint == null)
        {
            error = $"武器 '{stableId}' 声明支持抛壳但缺少抛壳点。";
            return false;
        }
        error = string.Empty;
        return true;
    }
}
