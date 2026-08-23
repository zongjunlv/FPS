using System;
using System.Collections.Generic;
using FPS.GameplayEffects;
using UnityEngine;

public enum ItemUseFailureReason
{
    None,
    InvalidItem,
    PlayerDead,
    HealthFull,
    ArmorFull,
    TargetWeaponMissing,
    RifleAmmoFull,
    HandgunAmmoFull,
    CooldownActive,
    EffectUnavailable
}

public readonly struct ItemUseContext
{
    public ItemUseContext(
        Health health,
        WeaponLoadoutController loadout)
        : this(health, loadout, null)
    {
    }

    public ItemUseContext(
        Health health,
        WeaponLoadoutController loadout,
        GameplayEffectRuntime gameplayEffects)
    {
        Health = health;
        Loadout = loadout;
        GameplayEffects = gameplayEffects ?? (health != null
            ? new GameplayEffectRuntime(health)
            : null);
    }

    public Health Health { get; }
    public WeaponLoadoutController Loadout { get; }
    public GameplayEffectRuntime GameplayEffects { get; }
}

public readonly struct ItemUseResult
{
    public ItemUseResult(
        bool succeeded,
        ItemUseFailureReason failureReason,
        float appliedAmount)
    {
        Succeeded = succeeded;
        FailureReason = failureReason;
        AppliedAmount = Mathf.Max(0f, appliedAmount);
    }

    public bool Succeeded { get; }
    public ItemUseFailureReason FailureReason { get; }
    public float AppliedAmount { get; }

    public static ItemUseResult Available => new(
        true,
        ItemUseFailureReason.None,
        0f);

    public static ItemUseResult Success(float amount) => new(
        true,
        ItemUseFailureReason.None,
        amount);

    public static ItemUseResult Failure(ItemUseFailureReason reason) => new(
        false,
        reason,
        0f);
}

public interface IItemEffect
{
    ItemUseResult Evaluate(ItemDefinition definition, ItemUseContext context);
    ItemUseResult Apply(ItemDefinition definition, ItemUseContext context);
}

public sealed class HealthRestoreItemEffect : IItemEffect
{
    public ItemUseResult Evaluate(
        ItemDefinition definition,
        ItemUseContext context)
    {
        if (definition == null || context.Health == null)
        {
            return ItemUseResult.Failure(ItemUseFailureReason.InvalidItem);
        }

        if (context.Health.IsDead)
        {
            return ItemUseResult.Failure(ItemUseFailureReason.PlayerDead);
        }

        return Preview(
            definition,
            context,
            ItemUseFailureReason.HealthFull);
    }

    public ItemUseResult Apply(
        ItemDefinition definition,
        ItemUseContext context)
    {
        ItemUseResult availability = Evaluate(definition, context);

        if (!availability.Succeeded)
        {
            return availability;
        }

        return Execute(
            definition,
            context,
            ItemUseFailureReason.HealthFull);
    }

    internal static ItemUseResult Preview(
        ItemDefinition definition,
        ItemUseContext context,
        ItemUseFailureReason noChangeReason)
    {
        if (definition?.GameplayEffect == null ||
            context.GameplayEffects == null)
        {
            return ItemUseResult.Failure(
                ItemUseFailureReason.EffectUnavailable);
        }

        GameplayEffectExecutionResult result =
            context.GameplayEffects.PreviewInstant(
                definition.GameplayEffect,
                new GameplayEffectContext(
                    definition.StableId,
                    definition,
                    context.Health),
                context.Health);
        return Convert(result, noChangeReason);
    }

    internal static ItemUseResult Execute(
        ItemDefinition definition,
        ItemUseContext context,
        ItemUseFailureReason noChangeReason)
    {
        if (definition?.GameplayEffect == null ||
            context.GameplayEffects == null)
        {
            return ItemUseResult.Failure(
                ItemUseFailureReason.EffectUnavailable);
        }

        GameplayEffectExecutionResult result =
            context.GameplayEffects.ExecuteInstant(
                definition.GameplayEffect,
                new GameplayEffectContext(
                    definition.StableId,
                    definition,
                    context.Health),
                context.Health);
        return Convert(result, noChangeReason);
    }

    private static ItemUseResult Convert(
        GameplayEffectExecutionResult result,
        ItemUseFailureReason noChangeReason)
    {
        if (result.Succeeded)
        {
            return ItemUseResult.Success(result.AppliedAmount);
        }

        return result.Failure == GameplayEffectExecutionFailure.NoChange
            ? ItemUseResult.Failure(noChangeReason)
            : ItemUseResult.Failure(ItemUseFailureReason.EffectUnavailable);
    }
}

public sealed class ArmorRestoreItemEffect : IItemEffect
{
    public ItemUseResult Evaluate(
        ItemDefinition definition,
        ItemUseContext context)
    {
        if (definition == null || context.Health == null)
        {
            return ItemUseResult.Failure(ItemUseFailureReason.InvalidItem);
        }

        if (context.Health.IsDead)
        {
            return ItemUseResult.Failure(ItemUseFailureReason.PlayerDead);
        }

        return HealthRestoreItemEffect.Preview(
            definition,
            context,
            ItemUseFailureReason.ArmorFull);
    }

    public ItemUseResult Apply(
        ItemDefinition definition,
        ItemUseContext context)
    {
        ItemUseResult availability = Evaluate(definition, context);

        if (!availability.Succeeded)
        {
            return availability;
        }

        return HealthRestoreItemEffect.Execute(
            definition,
            context,
            ItemUseFailureReason.ArmorFull);
    }
}

public sealed class AmmoRestoreItemEffect : IItemEffect
{
    private readonly WeaponAmmoType ammoType;

    public AmmoRestoreItemEffect(WeaponAmmoType targetAmmoType)
    {
        ammoType = targetAmmoType;
    }

    public ItemUseResult Evaluate(
        ItemDefinition definition,
        ItemUseContext context)
    {
        if (definition == null)
        {
            return ItemUseResult.Failure(ItemUseFailureReason.InvalidItem);
        }

        WeaponController weapon = FindWeapon(context.Loadout);

        if (weapon == null)
        {
            return ItemUseResult.Failure(
                ItemUseFailureReason.TargetWeaponMissing);
        }

        if (weapon.ReserveAmmo >= weapon.MaximumReserveAmmo)
        {
            return ItemUseResult.Failure(ammoType == WeaponAmmoType.Rifle
                ? ItemUseFailureReason.RifleAmmoFull
                : ItemUseFailureReason.HandgunAmmoFull);
        }

        return ItemUseResult.Available;
    }

    public ItemUseResult Apply(
        ItemDefinition definition,
        ItemUseContext context)
    {
        ItemUseResult availability = Evaluate(definition, context);

        if (!availability.Succeeded)
        {
            return availability;
        }

        WeaponController weapon = FindWeapon(context.Loadout);
        int accepted = weapon.AddReserveAmmo(
            Mathf.Max(1, Mathf.RoundToInt(definition.EffectAmount)));
        return accepted > 0
            ? ItemUseResult.Success(accepted)
            : ItemUseResult.Failure(ammoType == WeaponAmmoType.Rifle
                ? ItemUseFailureReason.RifleAmmoFull
                : ItemUseFailureReason.HandgunAmmoFull);
    }

    private WeaponController FindWeapon(WeaponLoadoutController loadout)
    {
        if (loadout == null)
        {
            return null;
        }

        for (int index = 0; index < loadout.WeaponCount; index++)
        {
            WeaponController weapon = loadout.GetWeapon(index);

            if (weapon != null && weapon.AmmoType == ammoType)
            {
                return weapon;
            }
        }

        return null;
    }
}

public sealed class ItemEffectRegistry
{
    private readonly Dictionary<ItemEffectType, Func<IItemEffect>> factories =
        new();

    public ItemEffectRegistry()
    {
        Register(
            ItemEffectType.RestoreHealth,
            () => new HealthRestoreItemEffect());
        Register(
            ItemEffectType.RestoreArmor,
            () => new ArmorRestoreItemEffect());
        Register(
            ItemEffectType.AddRifleAmmo,
            () => new AmmoRestoreItemEffect(WeaponAmmoType.Rifle));
        Register(
            ItemEffectType.AddHandgunAmmo,
            () => new AmmoRestoreItemEffect(WeaponAmmoType.Handgun));
    }

    public void Register(
        ItemEffectType effectType,
        Func<IItemEffect> factory)
    {
        factories[effectType] = factory ??
            throw new ArgumentNullException(nameof(factory));
    }

    public ItemUseResult Evaluate(
        ItemDefinition definition,
        ItemUseContext context)
    {
        return TryCreate(definition, out IItemEffect effect)
            ? effect.Evaluate(definition, context)
            : ItemUseResult.Failure(ItemUseFailureReason.EffectUnavailable);
    }

    public ItemUseResult Apply(
        ItemDefinition definition,
        ItemUseContext context)
    {
        return TryCreate(definition, out IItemEffect effect)
            ? effect.Apply(definition, context)
            : ItemUseResult.Failure(ItemUseFailureReason.EffectUnavailable);
    }

    // Issue 21 compatibility: keep a single, unambiguous TryApply overload.
    public bool TryApply(ItemDefinition definition, Health health)
    {
        return Apply(definition, new ItemUseContext(health, null)).Succeeded;
    }

    private bool TryCreate(
        ItemDefinition definition,
        out IItemEffect effect)
    {
        effect = null;
        return definition != null &&
               factories.TryGetValue(definition.EffectType, out var factory) &&
               (effect = factory()) != null;
    }
}

public static class ItemUsePresentation
{
    public static string GetFailureText(ItemUseFailureReason reason)
    {
        return reason switch
        {
            ItemUseFailureReason.PlayerDead => "角色已阵亡",
            ItemUseFailureReason.HealthFull => "生命值已满",
            ItemUseFailureReason.ArmorFull => "护甲值已满",
            ItemUseFailureReason.RifleAmmoFull => "步枪备弹已满",
            ItemUseFailureReason.HandgunAmmoFull => "手枪备弹已满",
            ItemUseFailureReason.CooldownActive => "物品冷却中",
            ItemUseFailureReason.TargetWeaponMissing => "未找到对应武器",
            ItemUseFailureReason.EffectUnavailable => "物品效果不可用",
            _ => "当前无法使用该物品"
        };
    }

    public static string GetEffectText(ItemDefinition definition)
    {
        if (definition == null)
        {
            return string.Empty;
        }

        int amount = Mathf.RoundToInt(definition.EffectAmount);
        return definition.EffectType switch
        {
            ItemEffectType.RestoreHealth => $"恢复 {amount} 点生命值",
            ItemEffectType.RestoreArmor => $"恢复 {amount} 点护甲值",
            ItemEffectType.AddRifleAmmo => $"补充 {amount} 发步枪备弹",
            ItemEffectType.AddHandgunAmmo => $"补充 {amount} 发手枪备弹",
            _ => string.Empty
        };
    }
}
