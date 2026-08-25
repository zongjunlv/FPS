using UnityEngine;

public enum SuppressorTacticsPhase
{
    Disabled,
    Ready,
    Advancing,
    Holding,
    Retreating,
    Relocating,
    Aiming,
    Firing,
    Cooldown,
    Recovering
}

public enum SuppressorMovementIntent
{
    Hold,
    Advance,
    Retreat,
    Relocate
}

public sealed class SuppressorTacticsStateMachine
{
    private float recoveryRemaining;

    public SuppressorTacticsPhase Phase { get; private set; } =
        SuppressorTacticsPhase.Disabled;
    public float RecoveryRemaining => recoveryRemaining;

    public void Reset(bool enabled)
    {
        recoveryRemaining = 0f;
        Phase = enabled
            ? SuppressorTacticsPhase.Ready
            : SuppressorTacticsPhase.Disabled;
    }

    public SuppressorMovementIntent EvaluateMovement(
        float distance,
        bool hasLineOfSight,
        float minimumRange,
        float maximumRange)
    {
        if (Phase == SuppressorTacticsPhase.Disabled)
        {
            return SuppressorMovementIntent.Hold;
        }

        if (!hasLineOfSight)
        {
            Phase = SuppressorTacticsPhase.Relocating;
            return SuppressorMovementIntent.Relocate;
        }

        if (distance < minimumRange)
        {
            Phase = SuppressorTacticsPhase.Retreating;
            return SuppressorMovementIntent.Retreat;
        }

        if (distance > maximumRange)
        {
            Phase = SuppressorTacticsPhase.Advancing;
            return SuppressorMovementIntent.Advance;
        }

        Phase = SuppressorTacticsPhase.Holding;
        return SuppressorMovementIntent.Hold;
    }

    public void ApplyAttackDecision(EnemyAttackDecision decision)
    {
        if (Phase == SuppressorTacticsPhase.Disabled ||
            Phase == SuppressorTacticsPhase.Recovering)
        {
            return;
        }

        Phase = decision switch
        {
            EnemyAttackDecision.Aim => SuppressorTacticsPhase.Aiming,
            EnemyAttackDecision.Attack => SuppressorTacticsPhase.Firing,
            EnemyAttackDecision.Cooldown => SuppressorTacticsPhase.Cooldown,
            _ => Phase
        };
    }

    public void BeginRecovery(float duration)
    {
        if (Phase == SuppressorTacticsPhase.Disabled)
        {
            return;
        }

        recoveryRemaining = Mathf.Max(0f, duration);
        Phase = SuppressorTacticsPhase.Recovering;
    }

    public bool TickRecovery(float deltaTime)
    {
        if (Phase != SuppressorTacticsPhase.Recovering)
        {
            return false;
        }

        recoveryRemaining = Mathf.Max(
            0f,
            recoveryRemaining - Mathf.Max(0f, deltaTime));

        if (recoveryRemaining > 0f)
        {
            return false;
        }

        Phase = SuppressorTacticsPhase.Ready;
        return true;
    }
}
