using NUnit.Framework;
using UnityEngine;

public sealed class Issue38RaiderTacticsTests
{
    [Test]
    public void StateMachineCoversFlankChargeStrikeAndRecovery()
    {
        var state = new RaiderTacticsStateMachine();

        state.Reset(true);
        Assert.That(state.Phase, Is.EqualTo(RaiderTacticsPhase.Ready));

        state.BeginFlank();
        Assert.That(state.Phase, Is.EqualTo(RaiderTacticsPhase.Flanking));

        state.BeginCharge();
        Assert.That(state.Phase, Is.EqualTo(RaiderTacticsPhase.Charging));

        state.ApplyAttackDecision(EnemyAttackDecision.Aim);
        Assert.That(state.Phase, Is.EqualTo(RaiderTacticsPhase.Windup));
        state.ApplyAttackDecision(EnemyAttackDecision.Attack);
        Assert.That(state.Phase, Is.EqualTo(RaiderTacticsPhase.Striking));
        state.ApplyAttackDecision(EnemyAttackDecision.Cooldown);
        Assert.That(state.Phase, Is.EqualTo(RaiderTacticsPhase.Recovering));
    }

    [Test]
    public void FailedPathRegroupsThenBecomesReadyToRetry()
    {
        var state = new RaiderTacticsStateMachine();
        state.Reset(true);
        state.BeginFlank();
        state.BeginRegroup(0.5f);

        Assert.That(state.Phase, Is.EqualTo(RaiderTacticsPhase.Regrouping));
        Assert.That(state.TickRegroup(0.49f), Is.False);
        Assert.That(state.TickRegroup(0.01f), Is.True);
        Assert.That(state.Phase, Is.EqualTo(RaiderTacticsPhase.Ready));
    }

    [Test]
    public void ResetDisablesEveryTransientCombatPhase()
    {
        var state = new RaiderTacticsStateMachine();
        state.Reset(true);
        state.BeginFlank();
        state.ApplyAttackDecision(EnemyAttackDecision.Attack);

        state.Reset(false);

        Assert.That(state.Phase, Is.EqualTo(RaiderTacticsPhase.Disabled));
        Assert.That(state.RegroupRemaining, Is.Zero);
    }

    [Test]
    public void AbilitySetFindsComposableRaiderDefinition()
    {
        RaiderApproachAbilityDefinition raider =
            ScriptableObject.CreateInstance<RaiderApproachAbilityDefinition>();
        EnemyAbilitySetDefinition set =
            ScriptableObject.CreateInstance<EnemyAbilitySetDefinition>();

        try
        {
            raider.Configure(
                "enemy.ability.test_raider",
                4f, 1f, 2f, 5f, 1.25f, 1.75f,
                1f, 0.5f, 2f, 0.2f, 0.8f, 0.75f);
            set.Configure(
                "enemy.role.test_raider",
                "RAIDER",
                Color.cyan,
                new EnemyAbilityDefinition[] { raider });

            Assert.That(
                set.FindAbility<RaiderApproachAbilityDefinition>(),
                Is.SameAs(raider));
            Assert.That(set.StatusLabel, Is.EqualTo("RAIDER"));
        }
        finally
        {
            Object.DestroyImmediate(set);
            Object.DestroyImmediate(raider);
        }
    }
}
