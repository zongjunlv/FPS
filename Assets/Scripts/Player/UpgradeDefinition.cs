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
    WeaponDamage,
    WeaponFireRate,
    WeaponMagazineCapacity,
    WeaponReloadSpeed,
    WeaponRecoilControl,
    WeaponAccuracy
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
    public string EffectValueText =>
        $"{GetEffectLabel(effectType)} +{Mathf.RoundToInt(EffectAmount * 100f)}%";

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

    public string GetLevelText(int currentLevel)
    {
        int level = Mathf.Clamp(currentLevel, 0, MaximumLevel);

        if (level >= MaximumLevel)
        {
            return $"MAX LEVEL  {MaximumLevel} / {MaximumLevel}";
        }

        int nextLevel = level + 1;
        return nextLevel >= MaximumLevel
            ? $"LEVEL {level}  →  MAX  ({MaximumLevel} / {MaximumLevel})"
            : $"LEVEL {level}  →  {nextLevel} / {MaximumLevel}";
    }

    private static string GetEffectLabel(UpgradeEffectType type)
    {
        return type switch
        {
            UpgradeEffectType.WeaponDamage => "DAMAGE",
            UpgradeEffectType.WeaponFireRate => "FIRE RATE",
            UpgradeEffectType.WeaponMagazineCapacity => "MAGAZINE",
            UpgradeEffectType.WeaponReloadSpeed => "RELOAD SPEED",
            UpgradeEffectType.WeaponRecoilControl => "RECOIL CONTROL",
            UpgradeEffectType.WeaponAccuracy => "ACCURACY",
            _ => "WEAPON"
        };
    }
}
