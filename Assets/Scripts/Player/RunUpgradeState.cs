using System;
using System.Collections.Generic;

public sealed class RunUpgradeState
{
    private readonly Dictionary<string, int> levels = new(
        StringComparer.Ordinal);
    private readonly List<string> selectionHistory = new();

    public IReadOnlyList<string> SelectionHistory => selectionHistory;
    public int SelectionCount => selectionHistory.Count;
    private float weaponDamageBonus;
    private float weaponFireRateBonus;
    private float weaponMagazineCapacityBonus;
    private float weaponReloadSpeedBonus;
    private float weaponRecoilControlBonus;
    private float weaponAccuracyBonus;

    public WeaponRuntimeModifiers WeaponModifiers =>
        new WeaponRuntimeModifiers(
            1f + weaponDamageBonus,
            1f + weaponFireRateBonus,
            1f + weaponMagazineCapacityBonus,
            1f + weaponReloadSpeedBonus,
            1f + weaponRecoilControlBonus,
            1f + weaponAccuracyBonus);
    public float WeaponDamageMultiplier =>
        WeaponModifiers.DamageMultiplier;

    public int GetLevel(string stableId)
    {
        return !string.IsNullOrEmpty(stableId) &&
               levels.TryGetValue(stableId, out int level)
            ? level
            : 0;
    }

    public bool IsMaximumLevel(UpgradeDefinition definition)
    {
        return definition == null ||
               GetLevel(definition.StableId) >= definition.MaximumLevel;
    }

    public bool TryApply(UpgradeDefinition definition)
    {
        if (definition == null ||
            string.IsNullOrWhiteSpace(definition.StableId) ||
            IsMaximumLevel(definition))
        {
            return false;
        }

        int nextLevel = GetLevel(definition.StableId) + 1;
        levels[definition.StableId] = nextLevel;
        selectionHistory.Add(definition.StableId);

        switch (definition.EffectType)
        {
            case UpgradeEffectType.WeaponDamage:
                weaponDamageBonus += definition.EffectAmount;
                break;
            case UpgradeEffectType.WeaponFireRate:
                weaponFireRateBonus += definition.EffectAmount;
                break;
            case UpgradeEffectType.WeaponMagazineCapacity:
                weaponMagazineCapacityBonus += definition.EffectAmount;
                break;
            case UpgradeEffectType.WeaponReloadSpeed:
                weaponReloadSpeedBonus += definition.EffectAmount;
                break;
            case UpgradeEffectType.WeaponRecoilControl:
                weaponRecoilControlBonus += definition.EffectAmount;
                break;
            case UpgradeEffectType.WeaponAccuracy:
                weaponAccuracyBonus += definition.EffectAmount;
                break;
        }

        return true;
    }
}

public readonly struct WeaponRuntimeModifiers
{
    public static WeaponRuntimeModifiers Identity =>
        new WeaponRuntimeModifiers(1f, 1f, 1f, 1f, 1f, 1f);

    public WeaponRuntimeModifiers(
        float damageMultiplier,
        float fireRateMultiplier,
        float magazineCapacityMultiplier,
        float reloadSpeedMultiplier,
        float recoilControlMultiplier,
        float accuracyMultiplier)
    {
        DamageMultiplier = Math.Max(0.01f, damageMultiplier);
        FireRateMultiplier = Math.Max(0.01f, fireRateMultiplier);
        MagazineCapacityMultiplier = Math.Max(
            0.01f,
            magazineCapacityMultiplier);
        ReloadSpeedMultiplier = Math.Max(0.01f, reloadSpeedMultiplier);
        RecoilControlMultiplier = Math.Max(0.01f, recoilControlMultiplier);
        AccuracyMultiplier = Math.Max(0.01f, accuracyMultiplier);
    }

    public float DamageMultiplier { get; }
    public float FireRateMultiplier { get; }
    public float MagazineCapacityMultiplier { get; }
    public float ReloadSpeedMultiplier { get; }
    public float RecoilControlMultiplier { get; }
    public float AccuracyMultiplier { get; }
}
