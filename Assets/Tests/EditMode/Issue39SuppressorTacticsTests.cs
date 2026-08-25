using NUnit.Framework;
using UnityEngine;

public sealed class Issue39SuppressorTacticsTests
{
    [Test]
    public void MovementIntentMaintainsConfiguredTacticalRange()
    {
        var state = new SuppressorTacticsStateMachine();
        state.Reset(true);

        Assert.That(
            state.EvaluateMovement(18f, true, 6f, 14f),
            Is.EqualTo(SuppressorMovementIntent.Advance));
        Assert.That(state.Phase,
            Is.EqualTo(SuppressorTacticsPhase.Advancing));

        Assert.That(
            state.EvaluateMovement(10f, true, 6f, 14f),
            Is.EqualTo(SuppressorMovementIntent.Hold));
        Assert.That(state.Phase,
            Is.EqualTo(SuppressorTacticsPhase.Holding));

        Assert.That(
            state.EvaluateMovement(3f, true, 6f, 14f),
            Is.EqualTo(SuppressorMovementIntent.Retreat));
        Assert.That(state.Phase,
            Is.EqualTo(SuppressorTacticsPhase.Retreating));
    }

    [Test]
    public void BlockedLineOfSightRelocatesBeforeAttacking()
    {
        var state = new SuppressorTacticsStateMachine();
        state.Reset(true);

        Assert.That(
            state.EvaluateMovement(10f, false, 6f, 14f),
            Is.EqualTo(SuppressorMovementIntent.Relocate));
        Assert.That(state.Phase,
            Is.EqualTo(SuppressorTacticsPhase.Relocating));
    }

    [Test]
    public void FailedPathRecoversAndPoolResetDisablesTransientState()
    {
        var state = new SuppressorTacticsStateMachine();
        state.Reset(true);
        state.EvaluateMovement(3f, true, 6f, 14f);
        state.BeginRecovery(0.5f);

        Assert.That(state.TickRecovery(0.49f), Is.False);
        Assert.That(state.TickRecovery(0.01f), Is.True);
        Assert.That(state.Phase,
            Is.EqualTo(SuppressorTacticsPhase.Ready));

        state.Reset(false);
        Assert.That(state.Phase,
            Is.EqualTo(SuppressorTacticsPhase.Disabled));
        Assert.That(state.RecoveryRemaining, Is.Zero);
    }

    [Test]
    public void AbilitySetFindsIndependentSuppressorDefinition()
    {
        SuppressorRangedAbilityDefinition suppressor =
            ScriptableObject.CreateInstance<
                SuppressorRangedAbilityDefinition>();
        EnemyAbilitySetDefinition set =
            ScriptableObject.CreateInstance<EnemyAbilitySetDefinition>();

        try
        {
            suppressor.Configure(
                "enemy.ability.test_suppressor",
                6f, 10f, 14f, 4f, 2f, 1.1f,
                1.5f, 0.5f, 0.4f, 1.2f, 0.65f, 260f);
            set.Configure(
                "enemy.role.test_suppressor",
                "SUPPRESSOR",
                Color.yellow,
                new EnemyAbilityDefinition[] { suppressor });

            Assert.That(
                set.FindAbility<SuppressorRangedAbilityDefinition>(),
                Is.SameAs(suppressor));
            Assert.That(suppressor.MinimumRange, Is.EqualTo(6f));
            Assert.That(suppressor.PreferredRange, Is.EqualTo(10f));
            Assert.That(suppressor.MaximumRange, Is.EqualTo(14f));
        }
        finally
        {
            Object.DestroyImmediate(set);
            Object.DestroyImmediate(suppressor);
        }
    }
}
