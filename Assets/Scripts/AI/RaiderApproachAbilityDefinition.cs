using UnityEngine;

[CreateAssetMenu(
    fileName = "RaiderApproachAbility",
    menuName = "FPS/Enemies/Abilities/Raider Approach")]
public sealed class RaiderApproachAbilityDefinition :
    EnemyAbilityDefinition
{
    [Header("Utility AI")]
    [SerializeField] private EnemyUtilityProfileDefinition utilityProfile;
    [SerializeField, Min(0.5f)] private float flankDistance = 4.5f;
    [SerializeField, Min(0f)] private float flankRearOffset = 1.5f;
    [SerializeField, Min(0.1f)] private float flankSampleRadius = 2f;
    [SerializeField, Min(0.5f)] private float chargeDistance = 5f;
    [SerializeField, Min(0.1f)] private float flankSpeedMultiplier = 1.35f;
    [SerializeField, Min(0.1f)] private float chargeSpeedMultiplier = 1.75f;
    [SerializeField, Min(0.1f)] private float noProgressTimeout = 1.5f;
    [SerializeField, Min(0f)] private float regroupDuration = 0.8f;
    [Header("Melee profile")]
    [SerializeField, Min(0.1f)] private float attackRange = 2.3f;
    [SerializeField, Min(0f)] private float windupDuration = 0.25f;
    [SerializeField, Min(0.05f)] private float attackCooldown = 0.9f;
    [SerializeField, Min(0f)] private float damageMultiplier = 0.8f;

    public float FlankDistance => flankDistance;
    public EnemyUtilityProfileDefinition UtilityProfile => utilityProfile;
    public float FlankRearOffset => flankRearOffset;
    public float FlankSampleRadius => flankSampleRadius;
    public float ChargeDistance => chargeDistance;
    public float FlankSpeedMultiplier => flankSpeedMultiplier;
    public float ChargeSpeedMultiplier => chargeSpeedMultiplier;
    public float NoProgressTimeout => noProgressTimeout;
    public float RegroupDuration => regroupDuration;
    public float AttackRange => attackRange;
    public float WindupDuration => windupDuration;
    public float AttackCooldown => attackCooldown;
    public float DamageMultiplier => damageMultiplier;

    public void ConfigureUtilityProfile(
        EnemyUtilityProfileDefinition configuredProfile)
    {
        utilityProfile = configuredProfile;
    }

    public void Configure(
        string id,
        float configuredFlankDistance,
        float configuredRearOffset,
        float configuredSampleRadius,
        float configuredChargeDistance,
        float configuredFlankSpeedMultiplier,
        float configuredChargeSpeedMultiplier,
        float configuredNoProgressTimeout,
        float configuredRegroupDuration,
        float configuredAttackRange,
        float configuredWindupDuration,
        float configuredAttackCooldown,
        float configuredDamageMultiplier)
    {
        ConfigureStableId(id);
        flankDistance = Mathf.Max(0.5f, configuredFlankDistance);
        flankRearOffset = Mathf.Max(0f, configuredRearOffset);
        flankSampleRadius = Mathf.Max(0.1f, configuredSampleRadius);
        chargeDistance = Mathf.Max(0.5f, configuredChargeDistance);
        flankSpeedMultiplier = Mathf.Max(
            0.1f,
            configuredFlankSpeedMultiplier);
        chargeSpeedMultiplier = Mathf.Max(
            0.1f,
            configuredChargeSpeedMultiplier);
        noProgressTimeout = Mathf.Max(0.1f, configuredNoProgressTimeout);
        regroupDuration = Mathf.Max(0f, configuredRegroupDuration);
        attackRange = Mathf.Max(0.1f, configuredAttackRange);
        windupDuration = Mathf.Max(0f, configuredWindupDuration);
        attackCooldown = Mathf.Max(0.05f, configuredAttackCooldown);
        damageMultiplier = Mathf.Max(0f, configuredDamageMultiplier);
    }
}
