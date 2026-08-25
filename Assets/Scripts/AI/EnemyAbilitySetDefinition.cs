using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(
    fileName = "EnemyAbilitySet",
    menuName = "FPS/Enemies/Ability Set")]
public sealed class EnemyAbilitySetDefinition : ScriptableObject
{
    [SerializeField] private string stableId;
    [SerializeField] private string statusLabel = "RAIDER";
    [SerializeField] private Color statusColor =
        new(0.1f, 0.9f, 1f, 1f);
    [SerializeField] private List<EnemyAbilityDefinition> abilities = new();

    public string StableId => stableId;
    public string StatusLabel => string.IsNullOrWhiteSpace(statusLabel)
        ? "RAIDER"
        : statusLabel.Trim();
    public Color StatusColor => statusColor;
    public IReadOnlyList<EnemyAbilityDefinition> Abilities => abilities;

    public void Configure(
        string id,
        string label,
        Color color,
        IEnumerable<EnemyAbilityDefinition> configuredAbilities)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException(
                "An enemy ability set requires a stable ID.",
                nameof(id));
        }

        stableId = id.Trim();
        statusLabel = string.IsNullOrWhiteSpace(label)
            ? "RAIDER"
            : label.Trim();
        statusColor = color;
        abilities = configuredAbilities != null
            ? new List<EnemyAbilityDefinition>(configuredAbilities)
            : new List<EnemyAbilityDefinition>();
        abilities.RemoveAll(ability => ability == null);
    }

    public T FindAbility<T>() where T : EnemyAbilityDefinition
    {
        foreach (EnemyAbilityDefinition ability in abilities)
        {
            if (ability is T typed)
            {
                return typed;
            }
        }

        return null;
    }
}
