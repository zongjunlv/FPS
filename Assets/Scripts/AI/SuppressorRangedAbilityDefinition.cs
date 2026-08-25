using UnityEngine;

[CreateAssetMenu(
    fileName = "SuppressorRangedAbility",
    menuName = "FPS/Enemies/Abilities/Suppressor Ranged")]
public sealed class SuppressorRangedAbilityDefinition :
    EnemyAbilityDefinition
{
    [Header("Tactical range")]
    [SerializeField, Min(0.5f)] private float minimumRange = 6f;
    [SerializeField, Min(1f)] private float preferredRange = 10f;
    [SerializeField, Min(1f)] private float maximumRange = 14f;
    [SerializeField, Min(0.5f)] private float repositionLateralOffset = 4f;
    [SerializeField, Min(0.1f)] private float positionSampleRadius = 2f;
    [SerializeField, Min(0.1f)] private float movementSpeedMultiplier = 1.1f;
    [SerializeField, Min(0.1f)] private float noProgressTimeout = 1.5f;
    [SerializeField, Min(0f)] private float recoveryDuration = 0.5f;
    [Header("Ranged attack")]
    [SerializeField, Min(0f)] private float windupDuration = 0.45f;
    [SerializeField, Min(0.05f)] private float attackCooldown = 1.2f;
    [SerializeField, Min(0f)] private float damageMultiplier = 0.65f;
    [SerializeField, Min(10f)] private float tracerSpeed = 260f;

    public float MinimumRange => minimumRange;
    public float PreferredRange => preferredRange;
    public float MaximumRange => maximumRange;
    public float RepositionLateralOffset => repositionLateralOffset;
    public float PositionSampleRadius => positionSampleRadius;
    public float MovementSpeedMultiplier => movementSpeedMultiplier;
    public float NoProgressTimeout => noProgressTimeout;
    public float RecoveryDuration => recoveryDuration;
    public float WindupDuration => windupDuration;
    public float AttackCooldown => attackCooldown;
    public float DamageMultiplier => damageMultiplier;
    public float TracerSpeed => tracerSpeed;

    public void Configure(
        string id,
        float configuredMinimumRange,
        float configuredPreferredRange,
        float configuredMaximumRange,
        float configuredLateralOffset,
        float configuredSampleRadius,
        float configuredMovementSpeedMultiplier,
        float configuredNoProgressTimeout,
        float configuredRecoveryDuration,
        float configuredWindupDuration,
        float configuredAttackCooldown,
        float configuredDamageMultiplier,
        float configuredTracerSpeed)
    {
        ConfigureStableId(id);
        minimumRange = Mathf.Max(0.5f, configuredMinimumRange);
        preferredRange = Mathf.Max(
            minimumRange + 0.5f,
            configuredPreferredRange);
        maximumRange = Mathf.Max(
            preferredRange + 0.5f,
            configuredMaximumRange);
        repositionLateralOffset = Mathf.Max(0.5f, configuredLateralOffset);
        positionSampleRadius = Mathf.Max(0.1f, configuredSampleRadius);
        movementSpeedMultiplier = Mathf.Max(
            0.1f,
            configuredMovementSpeedMultiplier);
        noProgressTimeout = Mathf.Max(0.1f, configuredNoProgressTimeout);
        recoveryDuration = Mathf.Max(0f, configuredRecoveryDuration);
        windupDuration = Mathf.Max(0f, configuredWindupDuration);
        attackCooldown = Mathf.Max(0.05f, configuredAttackCooldown);
        damageMultiplier = Mathf.Max(0f, configuredDamageMultiplier);
        tracerSpeed = Mathf.Max(10f, configuredTracerSpeed);
    }
}
