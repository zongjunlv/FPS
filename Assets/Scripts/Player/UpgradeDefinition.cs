using System;
using UnityEngine;

public enum UpgradeRarity
{
    Common,
    Rare,
    Epic
}

public enum UpgradeEffectType
{
    WeaponDamage
}

[CreateAssetMenu(
    fileName = "UpgradeDefinition",
    menuName = "FPS/Progression/Upgrade Definition")]
public sealed class UpgradeDefinition : ScriptableObject
{
    [SerializeField] private string stableId;
    [SerializeField] private string title;
    [SerializeField, TextArea] private string description;
    [SerializeField] private Sprite icon;
    [SerializeField] private UpgradeRarity rarity;
    [SerializeField, Min(1)] private int maximumLevel = 1;
    [SerializeField] private UpgradeEffectType effectType;
    [SerializeField, Min(0f)] private float effectAmount = 0.2f;

    public string StableId => stableId;
    public string Title => title;
    public string Description => description;
    public Sprite Icon => icon;
    public UpgradeRarity Rarity => rarity;
    public int MaximumLevel => Mathf.Max(1, maximumLevel);
    public UpgradeEffectType EffectType => effectType;
    public float EffectAmount => Mathf.Max(0f, effectAmount);

    public void Configure(
        string id,
        string displayTitle,
        string displayDescription,
        Sprite displayIcon,
        UpgradeRarity displayRarity,
        int maxLevel,
        UpgradeEffectType type,
        float amount)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException(
                "An upgrade requires a stable ID.",
                nameof(id));
        }

        stableId = id.Trim();
        title = displayTitle ?? string.Empty;
        description = displayDescription ?? string.Empty;
        icon = displayIcon;
        rarity = displayRarity;
        maximumLevel = Mathf.Max(1, maxLevel);
        effectType = type;
        effectAmount = Mathf.Max(0f, amount);
    }
}
