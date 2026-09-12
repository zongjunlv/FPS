using UnityEngine;

public enum RaiderTacticsPhase
{
    Disabled,
    Ready,
    Flanking,
    Retreating,
    Charging,
    Windup,
    Striking,
    Recovering,
    Regrouping
}

public sealed class RaiderTacticsStateMachine
{
    private float regroupRemaining;

    public RaiderTacticsPhase Phase { get; private set; } =
        RaiderTacticsPhase.Disabled;
    public float RegroupRemaining => regroupRemaining;

    public void Reset(bool enabled)
    {
        regroupRemaining = 0f;
        Phase = enabled
            ? RaiderTacticsPhase.Ready
            : RaiderTacticsPhase.Disabled;
    }

    public void BeginFlank()
    {
        if (Phase != RaiderTacticsPhase.Disabled)
        {
            Phase = RaiderTacticsPhase.Flanking;
        }
    }

    public void BeginCharge()
    {
        if (Phase != RaiderTacticsPhase.Disabled)
        {
            Phase = RaiderTacticsPhase.Charging;
        }
    }

    public void BeginRetreat()
    {
        if (Phase != RaiderTacticsPhase.Disabled)
        {
            Phase = RaiderTacticsPhase.Retreating;
        }
    }

    public void ApplyAttackDecision(EnemyAttackDecision decision)
    {
        if (Phase == RaiderTacticsPhase.Disabled)
        {
            return;
        }

        Phase = decision switch
        {
            EnemyAttackDecision.Aim => RaiderTacticsPhase.Windup,
            EnemyAttackDecision.Attack => RaiderTacticsPhase.Striking,
            EnemyAttackDecision.Cooldown => RaiderTacticsPhase.Recovering,
            _ => Phase
        };
    }

    public void BeginRegroup(float duration)
    {
        if (Phase == RaiderTacticsPhase.Disabled)
        {
            return;
        }

        regroupRemaining = Mathf.Max(0f, duration);
        Phase = RaiderTacticsPhase.Regrouping;
    }

    public bool TickRegroup(float deltaTime)
    {
        if (Phase != RaiderTacticsPhase.Regrouping)
        {
            return false;
        }

        regroupRemaining = Mathf.Max(
            0f,
            regroupRemaining - Mathf.Max(0f, deltaTime));

        if (regroupRemaining > 0f)
        {
            return false;
        }

        Phase = RaiderTacticsPhase.Ready;
        return true;
    }
}
