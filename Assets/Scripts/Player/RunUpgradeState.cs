using System;
using System.Collections.Generic;

public sealed class RunUpgradeState
{
    private readonly Dictionary<string, int> levels = new(
        StringComparer.Ordinal);
    private readonly List<string> selectionHistory = new();

    public IReadOnlyList<string> SelectionHistory => selectionHistory;
    public int SelectionCount => selectionHistory.Count;
    public float WeaponDamageMultiplier { get; private set; } = 1f;

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

        if (definition.EffectType == UpgradeEffectType.WeaponDamage)
        {
            WeaponDamageMultiplier += definition.EffectAmount;
        }

        return true;
    }
}
