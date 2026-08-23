using UnityEngine;

public enum EnemyAttackDecision
{
    Chase,
    Aim,
    Attack,
    Cooldown
}

public sealed class EnemyAttackStateMachine
{
    private float attackRange = 2f;
    private float aimDuration = 0.5f;
    private float attackCooldown = 1f;
    private float aimElapsed;
    private float cooldownRemaining;

    public void Configure(
        float configuredAttackRange,
        float configuredAimDuration,
        float configuredCooldown)
    {
        attackRange = Mathf.Max(0.1f, configuredAttackRange);
        aimDuration = Mathf.Max(0f, configuredAimDuration);
        attackCooldown = Mathf.Max(0.01f, configuredCooldown);
        aimElapsed = 0f;
        cooldownRemaining = 0f;
    }

    public EnemyAttackDecision Evaluate(
        float distance,
        bool hasLineOfSight,
        float deltaTime)
    {
        float safeDeltaTime = Mathf.Max(0f, deltaTime);

        if (distance > attackRange || !hasLineOfSight)
        {
            aimElapsed = 0f;
            return EnemyAttackDecision.Chase;
        }

        if (cooldownRemaining > 0f)
        {
            cooldownRemaining = Mathf.Max(
                0f,
                cooldownRemaining - safeDeltaTime);

            if (cooldownRemaining > 0.0001f)
            {
                return EnemyAttackDecision.Cooldown;
            }

            cooldownRemaining = 0f;
            return EnemyAttackDecision.Aim;
        }

        aimElapsed += safeDeltaTime;

        if (aimElapsed + 0.0001f < aimDuration)
        {
            return EnemyAttackDecision.Aim;
        }

        aimElapsed = 0f;
        cooldownRemaining = attackCooldown;
        return EnemyAttackDecision.Attack;
    }

    public void CancelAim()
    {
        aimElapsed = 0f;
    }
}
