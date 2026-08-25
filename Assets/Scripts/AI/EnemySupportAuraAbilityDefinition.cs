using FPS.GameplayEffects;
using UnityEngine;

public enum EnemySupportTargetPriority
{
    LowestHealthRatio,
    Nearest,
    HighestAttackDamage
}

[CreateAssetMenu(
    fileName = "EnemySupportAuraAbility",
    menuName = "FPS/Enemies/Abilities/Support Aura")]
public sealed class EnemySupportAuraAbilityDefinition :
    EnemyAbilityDefinition
{
    [SerializeField, Min(0.5f)] private float radius = 10f;
    [SerializeField, Min(1)] private int maximumTargets = 2;
    [SerializeField, Min(0.05f)] private float effectDuration = 3f;
    [SerializeField, Min(0.05f)] private float cooldown = 2f;
    [SerializeField] private EnemySupportTargetPriority targetPriority =
        EnemySupportTargetPriority.LowestHealthRatio;
    [SerializeField] private string requiredTargetTag = "enemy";
    [SerializeField] private GameplayEffectDefinition buffEffect;

    public float Radius => radius;
    public int MaximumTargets => maximumTargets;
    public float EffectDuration => effectDuration;
    public float Cooldown => cooldown;
    public EnemySupportTargetPriority TargetPriority => targetPriority;
    public string RequiredTargetTag =>
        string.IsNullOrWhiteSpace(requiredTargetTag)
            ? "enemy"
            : requiredTargetTag.Trim();
    public GameplayEffectDefinition BuffEffect => buffEffect;

    public void Configure(
        string id,
        float configuredRadius,
        int configuredMaximumTargets,
        float configuredEffectDuration,
        float configuredCooldown,
        EnemySupportTargetPriority configuredPriority,
        string configuredRequiredTag,
        GameplayEffectDefinition configuredBuffEffect)
    {
        ConfigureStableId(id);
        radius = Mathf.Max(0.5f, configuredRadius);
        maximumTargets = Mathf.Max(1, configuredMaximumTargets);
        effectDuration = Mathf.Max(0.05f, configuredEffectDuration);
        cooldown = Mathf.Max(0.05f, configuredCooldown);
        targetPriority = configuredPriority;
        requiredTargetTag = string.IsNullOrWhiteSpace(configuredRequiredTag)
            ? "enemy"
            : configuredRequiredTag.Trim();
        buffEffect = configuredBuffEffect;
    }
}
