using System.Collections.Generic;
using FPS.GameplayEffects;
using NUnit.Framework;
using UnityEngine;

public sealed class Issue37GameplayEffectDiagnosticsTests
{
    private readonly List<Object> cleanup = new();

    [SetUp]
    public void SetUp()
    {
        GameplayEffectDebugRegistry.ClearForTesting();
    }

    [TearDown]
    public void TearDown()
    {
        for (int index = cleanup.Count - 1; index >= 0; index--)
        {
            if (cleanup[index] != null)
            {
                Object.DestroyImmediate(cleanup[index]);
            }
        }

        cleanup.Clear();
        GameplayEffectDebugRegistry.ClearForTesting();
    }

    [Test]
    public void DefinitionProvidesStableGameplayTags()
    {
        GameplayEffectDefinition effect = CreatePersistentEffect(
            "player.test-bonus",
            new GameplayEffectModifier(
                GameplayAttributeId.WeaponFireRate,
                GameplayModifierOperation.Add,
                0.2f));

        Assert.That(effect.GameplayTags,
            Does.Contain("effect.player.test-bonus"));
        Assert.That(effect.GameplayTags,
            Does.Contain("duration.persistent"));

        effect.ConfigureTags("state.low-health", "state.low-health", "combat");
        Assert.That(effect.GameplayTags.Count, Is.EqualTo(2));
        Assert.That(effect.GameplayTags[0], Is.EqualTo("state.low-health"));
    }

    [Test]
    public void TraceMatchesAddThenCombinedMultiplierAggregation()
    {
        GameObject target = Track(new GameObject("Trace Target"));
        var runtime = new GameplayEffectRuntime(target, "Trace Channel");
        GameplayEffectDefinition additive = CreatePersistentEffect(
            "add",
            new GameplayEffectModifier(
                GameplayAttributeId.MaximumHealth,
                GameplayModifierOperation.Add,
                20f));
        GameplayEffectDefinition multiplier = CreatePersistentEffect(
            "multiply",
            new GameplayEffectModifier(
                GameplayAttributeId.MaximumHealth,
                GameplayModifierOperation.Multiply,
                0.5f));
        runtime.Apply(additive, Context(additive, target));
        runtime.Apply(multiplier, Context(multiplier, target));

        float evaluated = runtime.Evaluate(
            GameplayAttributeId.MaximumHealth,
            100f);
        GameplayAttributeEvaluationTrace trace =
            runtime.CaptureAttributeTraces()[0];

        Assert.That(evaluated, Is.EqualTo(180f));
        Assert.That(trace.BaseValue, Is.EqualTo(100f));
        Assert.That(trace.FinalValue, Is.EqualTo(evaluated));
        Assert.That(trace.Steps.Count, Is.EqualTo(2));
        Assert.That(trace.Steps[0].Operation,
            Is.EqualTo(GameplayModifierOperation.Add));
        Assert.That(trace.Steps[0].OutputValue, Is.EqualTo(120f));
        Assert.That(trace.Steps[1].Operation,
            Is.EqualTo(GameplayModifierOperation.Multiply));
        Assert.That(trace.Steps[1].OutputValue, Is.EqualTo(180f));
    }

    [Test]
    public void TraceMarksOnlyWinningOverrideAsApplied()
    {
        GameObject target = Track(new GameObject("Override Target"));
        var runtime = new GameplayEffectRuntime(target);
        GameplayEffectDefinition low = CreatePersistentEffect(
            "override.low",
            new GameplayEffectModifier(
                GameplayAttributeId.MaximumHealth,
                GameplayModifierOperation.Override,
                50f,
                1));
        GameplayEffectDefinition high = CreatePersistentEffect(
            "override.high",
            new GameplayEffectModifier(
                GameplayAttributeId.MaximumHealth,
                GameplayModifierOperation.Override,
                80f,
                2));
        runtime.Apply(low, Context(low, target));
        runtime.Apply(high, Context(high, target));
        runtime.Evaluate(GameplayAttributeId.MaximumHealth, 100f);

        GameplayAttributeEvaluationTrace trace =
            runtime.CaptureAttributeTraces()[0];
        Assert.That(trace.FinalValue, Is.EqualTo(80f));
        Assert.That(trace.Steps[0].Applied, Is.False);
        Assert.That(trace.Steps[1].Applied, Is.True);
    }

    [Test]
    public void RegistryGroupsChannelsAndExcludesInactiveTargets()
    {
        GameObject target = Track(new GameObject("Player Target"));
        _ = new GameplayEffectRuntime(target, "Upgrades");
        _ = new GameplayEffectRuntime(target, "Weapon");

        IReadOnlyList<GameplayEffectDebugTargetSnapshot> active =
            GameplayEffectDebugRegistry.CaptureActiveTargets();
        Assert.That(active.Count, Is.EqualTo(1));
        Assert.That(active[0].Runtimes.Count, Is.EqualTo(2));
        Assert.That(active[0].Runtimes[0].DebugChannel,
            Is.EqualTo("Upgrades"));

        target.SetActive(false);
        Assert.That(GameplayEffectDebugRegistry.CaptureActiveTargets(),
            Is.Empty);
    }

    [Test]
    public void TimedInstanceReportsStacksSourceAndRemainingTime()
    {
        GameObject target = Track(new GameObject("Timed Target"));
        GameplayEffectDefinition effect = Track(
            ScriptableObject.CreateInstance<GameplayEffectDefinition>());
        effect.ConfigureTimed(
            "status.burn",
            4f,
            1f,
            4f,
            3,
            GameplayEffectStackRefreshPolicy.RefreshAllDurations);
        var runtime = new GameplayEffectRuntime(target, "Status");
        GameplayEffectApplicationResult result = runtime.ApplyTimed(
            effect,
            new GameplayEffectContext("rifle", target, target));
        runtime.AdvanceTimed(1.25f, null);

        Assert.That(result.Instance.Context.SourceId, Is.EqualTo("rifle"));
        Assert.That(result.Instance.EffectiveStackCount, Is.EqualTo(1));
        Assert.That(result.Instance.RemainingDuration,
            Is.EqualTo(2.75f).Within(0.001f));
    }

    private GameplayEffectDefinition CreatePersistentEffect(
        string id,
        params GameplayEffectModifier[] modifiers)
    {
        GameplayEffectDefinition effect = Track(
            ScriptableObject.CreateInstance<GameplayEffectDefinition>());
        effect.Configure(id, modifiers);
        return effect;
    }

    private static GameplayEffectContext Context(
        GameplayEffectDefinition definition,
        GameObject target)
    {
        return new GameplayEffectContext(
            definition.StableId,
            definition,
            target);
    }

    private T Track<T>(T instance) where T : Object
    {
        cleanup.Add(instance);
        return instance;
    }
}
