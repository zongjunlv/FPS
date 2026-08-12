using System;
using System.Collections.Generic;

public interface IItemEffect
{
    bool TryApply(ItemDefinition definition, Health health);
}

public sealed class HealthRestoreItemEffect : IItemEffect
{
    public bool TryApply(ItemDefinition definition, Health health)
    {
        return definition != null &&
               health != null &&
               health.RestoreHealth(definition.EffectAmount) > 0f;
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
    }

    public void Register(
        ItemEffectType effectType,
        Func<IItemEffect> factory)
    {
        factories[effectType] = factory ??
            throw new ArgumentNullException(nameof(factory));
    }

    public bool TryApply(ItemDefinition definition, Health health)
    {
        return definition != null &&
               factories.TryGetValue(definition.EffectType, out var factory) &&
               factory()?.TryApply(definition, health) == true;
    }
}
