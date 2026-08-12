using UnityEngine;

public sealed class PlayerRuntimeCombatStats : MonoBehaviour
{
    public event System.Action ModifiersChanged;

    public WeaponRuntimeModifiers WeaponModifiers { get; private set; } =
        WeaponRuntimeModifiers.Identity;
    public SurvivalRuntimeModifiers SurvivalModifiers { get; private set; } =
        SurvivalRuntimeModifiers.Identity;
    public float WeaponDamageMultiplier =>
        WeaponModifiers.DamageMultiplier;

    public float ApplyWeaponDamage(float baseDamage)
    {
        return Mathf.Max(0f, baseDamage) *
               WeaponModifiers.DamageMultiplier;
    }

    public float ApplyFireInterval(float baseInterval)
    {
        return Mathf.Max(0.001f, baseInterval) /
               WeaponModifiers.FireRateMultiplier;
    }

    public int ApplyMagazineCapacity(int baseCapacity)
    {
        return Mathf.Max(
            1,
            Mathf.RoundToInt(
                Mathf.Max(1, baseCapacity) *
                WeaponModifiers.MagazineCapacityMultiplier));
    }

    public float ApplyReloadDuration(float baseDuration)
    {
        return Mathf.Max(0.01f, baseDuration) /
               WeaponModifiers.ReloadSpeedMultiplier;
    }

    public float ApplyRecoil(float baseRecoil)
    {
        return Mathf.Max(0f, baseRecoil) /
               WeaponModifiers.RecoilControlMultiplier;
    }

    public float ApplySpread(float baseSpread)
    {
        return Mathf.Max(0f, baseSpread) /
               WeaponModifiers.AccuracyMultiplier;
    }

    public float ApplyMovementSpeed(float baseSpeed)
    {
        return Mathf.Max(0f, baseSpeed) *
               SurvivalModifiers.MovementSpeedMultiplier;
    }

    public void SetSurvivalModifiers(SurvivalRuntimeModifiers modifiers)
    {
        SurvivalModifiers = new SurvivalRuntimeModifiers(
            modifiers.MaximumHealthMultiplier,
            modifiers.MaximumArmorMultiplier,
            modifiers.MovementSpeedMultiplier);
        ModifiersChanged?.Invoke();
    }

    public void SetWeaponDamageMultiplier(float multiplier)
    {
        SetWeaponModifiers(new WeaponRuntimeModifiers(
            multiplier,
            WeaponModifiers.FireRateMultiplier,
            WeaponModifiers.MagazineCapacityMultiplier,
            WeaponModifiers.ReloadSpeedMultiplier,
            WeaponModifiers.RecoilControlMultiplier,
            WeaponModifiers.AccuracyMultiplier));
    }

    public void SetWeaponModifiers(WeaponRuntimeModifiers modifiers)
    {
        WeaponModifiers = new WeaponRuntimeModifiers(
            modifiers.DamageMultiplier,
            modifiers.FireRateMultiplier,
            modifiers.MagazineCapacityMultiplier,
            modifiers.ReloadSpeedMultiplier,
            modifiers.RecoilControlMultiplier,
            modifiers.AccuracyMultiplier);
        ModifiersChanged?.Invoke();
    }

    public void ResetRuntimeModifiers()
    {
        SetWeaponModifiers(WeaponRuntimeModifiers.Identity);
        SetSurvivalModifiers(SurvivalRuntimeModifiers.Identity);
    }
}
