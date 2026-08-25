using System.Collections.Generic;
using FPS.GameplayEffects;
using UnityEngine;

public sealed class PlayerRuntimeCombatStats : MonoBehaviour
{
    public event System.Action ModifiersChanged;

    private WeaponRuntimeModifiers baseWeaponModifiers =
        WeaponRuntimeModifiers.Identity;
    private GameplayEffectRuntime weaponEffects;

    public WeaponRuntimeModifiers WeaponModifiers =>
        CreateEffectiveWeaponModifiers();
    public SurvivalRuntimeModifiers SurvivalModifiers { get; private set; } =
        SurvivalRuntimeModifiers.Identity;
    public float WeaponDamageMultiplier =>
        WeaponModifiers.DamageMultiplier;
    public float FireRateMultiplier => WeaponModifiers.FireRateMultiplier;
    public IReadOnlyList<GameplayEffectInstance> ActiveGameplayEffects =>
        WeaponEffectRuntime.ActiveInstances;

    private GameplayEffectRuntime WeaponEffectRuntime =>
        weaponEffects ??= new GameplayEffectRuntime(gameObject);

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
            baseWeaponModifiers.FireRateMultiplier,
            baseWeaponModifiers.MagazineCapacityMultiplier,
            baseWeaponModifiers.ReloadSpeedMultiplier,
            baseWeaponModifiers.RecoilControlMultiplier,
            baseWeaponModifiers.AccuracyMultiplier));
    }

    public void SetWeaponModifiers(WeaponRuntimeModifiers modifiers)
    {
        baseWeaponModifiers = new WeaponRuntimeModifiers(
            modifiers.DamageMultiplier,
            modifiers.FireRateMultiplier,
            modifiers.MagazineCapacityMultiplier,
            modifiers.ReloadSpeedMultiplier,
            modifiers.RecoilControlMultiplier,
            modifiers.AccuracyMultiplier);
        ModifiersChanged?.Invoke();
    }

    public GameplayEffectInstance ApplyGameplayEffect(
        GameplayEffectDefinition definition,
        string sourceId,
        UnityEngine.Object source)
    {
        if (definition == null || definition.DurationPolicy !=
            GameplayEffectDurationPolicy.Persistent)
        {
            return null;
        }

        GameplayEffectInstance instance = WeaponEffectRuntime.Apply(
            definition,
            new GameplayEffectContext(
                sourceId,
                source,
                gameObject));
        ModifiersChanged?.Invoke();
        return instance;
    }

    public bool RemoveGameplayEffect(long instanceId)
    {
        if (!WeaponEffectRuntime.Remove(instanceId))
        {
            return false;
        }

        ModifiersChanged?.Invoke();
        return true;
    }

    public void ClearGameplayEffects()
    {
        if (weaponEffects == null || weaponEffects.ActiveInstances.Count == 0)
        {
            return;
        }

        weaponEffects.Clear();
        ModifiersChanged?.Invoke();
    }

    public void ResetRuntimeModifiers()
    {
        ClearGameplayEffects();
        SetWeaponModifiers(WeaponRuntimeModifiers.Identity);
        SetSurvivalModifiers(SurvivalRuntimeModifiers.Identity);
    }

    private WeaponRuntimeModifiers CreateEffectiveWeaponModifiers()
    {
        float fireRate = weaponEffects != null
            ? weaponEffects.Evaluate(
                GameplayAttributeId.WeaponFireRate,
                baseWeaponModifiers.FireRateMultiplier)
            : baseWeaponModifiers.FireRateMultiplier;
        return new WeaponRuntimeModifiers(
            baseWeaponModifiers.DamageMultiplier,
            fireRate,
            baseWeaponModifiers.MagazineCapacityMultiplier,
            baseWeaponModifiers.ReloadSpeedMultiplier,
            baseWeaponModifiers.RecoilControlMultiplier,
            baseWeaponModifiers.AccuracyMultiplier);
    }
}
