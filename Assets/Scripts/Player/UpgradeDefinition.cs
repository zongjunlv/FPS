using System;
using FPS.GameplayEffects;
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
    WeaponAccuracy,
    MaximumHealth,
    MaximumArmor,
    HealthRestore,
    ArmorRestore,
    MovementSpeed
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
    [SerializeField] private GameplayEffectDefinition gameplayEffect;
    private bool ownsGeneratedGameplayEffect;

    public string StableId => stableId;
    public string Title => title;
    public string Description => description;
    public Sprite Icon => icon;
    public UpgradeRarity Rarity => rarity;
    public int MaximumLevel => Mathf.Max(1, maximumLevel);
    public UpgradeEffectType EffectType => effectType;
    public float EffectAmount => Mathf.Max(0f, effectAmount);
    public GameplayEffectDefinition GameplayEffect => gameplayEffect;
    public string EffectValueText =>
        effectType == UpgradeEffectType.HealthRestore ||
        effectType == UpgradeEffectType.ArmorRestore
            ? $"{GetEffectLabel(effectType)} +{Mathf.RoundToInt(EffectAmount)}"
            : $"{GetEffectLabel(effectType)} +{Mathf.RoundToInt(EffectAmount * 100f)}%";

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
        ConfigureGeneratedGameplayEffect();
    }

    public void ConfigureGameplayEffect(
        GameplayEffectDefinition configuredEffect)
    {
        ReleaseGeneratedGameplayEffect();
        gameplayEffect = configuredEffect;
        ownsGeneratedGameplayEffect = false;
    }

    public string GetLevelText(int currentLevel)
    {
        int level = Mathf.Clamp(currentLevel, 0, MaximumLevel);

        if (level >= MaximumLevel)
        {
            return $"已满级  {MaximumLevel} / {MaximumLevel}";
        }

        int nextLevel = level + 1;
        return nextLevel >= MaximumLevel
            ? $"等级 {level}  →  满级  ({MaximumLevel} / {MaximumLevel})"
            : $"等级 {level}  →  {nextLevel} / {MaximumLevel}";
    }

    private static string GetEffectLabel(UpgradeEffectType type)
    {
        return type switch
        {
            UpgradeEffectType.WeaponDamage => "武器伤害",
            UpgradeEffectType.WeaponFireRate => "射击速度",
            UpgradeEffectType.WeaponMagazineCapacity => "弹匣容量",
            UpgradeEffectType.WeaponReloadSpeed => "换弹速度",
            UpgradeEffectType.WeaponRecoilControl => "后坐力控制",
            UpgradeEffectType.WeaponAccuracy => "射击精准度",
            UpgradeEffectType.MaximumHealth => "最大生命值",
            UpgradeEffectType.MaximumArmor => "最大护甲值",
            UpgradeEffectType.HealthRestore => "生命恢复",
            UpgradeEffectType.ArmorRestore => "护甲恢复",
            UpgradeEffectType.MovementSpeed => "移动速度",
            _ => "武器"
        };
    }

    private void ConfigureGeneratedGameplayEffect()
    {
        ReleaseGeneratedGameplayEffect();

        if (effectType != UpgradeEffectType.MaximumHealth)
        {
            gameplayEffect = null;
            return;
        }

        gameplayEffect = CreateInstance<GameplayEffectDefinition>();
        gameplayEffect.name = $"{stableId} Maximum Health Effect";
        gameplayEffect.hideFlags = HideFlags.DontSave;
        gameplayEffect.Configure(
            $"{stableId}.maximum-health",
            new GameplayEffectModifier(
                GameplayAttributeId.MaximumHealth,
                GameplayModifierOperation.Multiply,
                effectAmount));
        ownsGeneratedGameplayEffect = true;
    }

    private void ReleaseGeneratedGameplayEffect()
    {
        if (!ownsGeneratedGameplayEffect || gameplayEffect == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Destroy(gameplayEffect);
        }
        else
        {
            DestroyImmediate(gameplayEffect);
        }

        gameplayEffect = null;
        ownsGeneratedGameplayEffect = false;
    }

    private void OnDestroy()
    {
        ReleaseGeneratedGameplayEffect();
    }
}
