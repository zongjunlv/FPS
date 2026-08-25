using FPS.GameplayEffects;
using NUnit.Framework;
using UnityEngine;

public sealed class Issue40SupportEffectTests
{
    [Test]
    public void SameSourceRefreshesWhileDifferentSourcesStackIndependently()
    {
        GameObject target = new("Support Target");
        GameObject sourceA = new("Support Source A");
        GameObject sourceB = new("Support Source B");
        target.AddComponent<Health>();
        EnemySupportEffectReceiver receiver =
            target.AddComponent<EnemySupportEffectReceiver>();
        GameplayEffectDefinition effect = CreateEffect();

        try
        {
            Assert.That(receiver.ApplyOrRefresh(
                "source-a:1", sourceA, effect, 3f), Is.True);
            Assert.That(receiver.ApplyOrRefresh(
                "source-a:1", sourceA, effect, 3f), Is.True);
            Assert.That(receiver.ActiveSourceCount, Is.EqualTo(1));
            Assert.That(receiver.ApplicationCount, Is.EqualTo(1));
            Assert.That(receiver.RefreshCount, Is.EqualTo(1));
            Assert.That(receiver.Evaluate(
                GameplayAttributeId.EnemyAttackDamage, 20f),
                Is.EqualTo(25f).Within(0.001f));

            Assert.That(receiver.ApplyOrRefresh(
                "source-b:1", sourceB, effect, 3f), Is.True);
            Assert.That(receiver.ActiveSourceCount, Is.EqualTo(2));
            Assert.That(receiver.Evaluate(
                GameplayAttributeId.EnemyAttackDamage, 20f),
                Is.EqualTo(30f).Within(0.001f));

            Assert.That(receiver.RemoveSource("source-a:1"), Is.True);
            Assert.That(receiver.ActiveSourceCount, Is.EqualTo(1));
            Assert.That(receiver.Evaluate(
                GameplayAttributeId.EnemyAttackDamage, 20f),
                Is.EqualTo(25f).Within(0.001f));
        }
        finally
        {
            Object.DestroyImmediate(effect);
            Object.DestroyImmediate(sourceB);
            Object.DestroyImmediate(sourceA);
            Object.DestroyImmediate(target);
        }
    }

    [Test]
    public void LeaseExpiryAndPoolClearRemoveExactSupportEffect()
    {
        GameObject target = new("Support Target");
        GameObject source = new("Support Source");
        target.AddComponent<Health>();
        EnemySupportEffectReceiver receiver =
            target.AddComponent<EnemySupportEffectReceiver>();
        GameplayEffectDefinition effect = CreateEffect();

        try
        {
            receiver.ApplyOrRefresh("source:1", source, effect, 0.1f);
            receiver.AdvanceLeases(0.11f);
            Assert.That(receiver.ActiveSourceCount, Is.Zero);
            Assert.That(receiver.Evaluate(
                GameplayAttributeId.EnemyAttackDamage, 20f),
                Is.EqualTo(20f).Within(0.001f));

            receiver.ApplyOrRefresh("source:2", source, effect, 3f);
            receiver.ClearAll();
            Assert.That(receiver.ActiveSourceCount, Is.Zero);
        }
        finally
        {
            Object.DestroyImmediate(effect);
            Object.DestroyImmediate(source);
            Object.DestroyImmediate(target);
        }
    }

    [Test]
    public void SupportDefinitionClampsUnsafeConfiguration()
    {
        EnemySupportAuraAbilityDefinition aura =
            ScriptableObject.CreateInstance<
                EnemySupportAuraAbilityDefinition>();

        try
        {
            aura.Configure(
                "enemy.ability.test_support",
                -1f,
                0,
                0f,
                0f,
                EnemySupportTargetPriority.Nearest,
                string.Empty,
                null);
            Assert.That(aura.Radius, Is.EqualTo(0.5f));
            Assert.That(aura.MaximumTargets, Is.EqualTo(1));
            Assert.That(aura.EffectDuration, Is.EqualTo(0.05f));
            Assert.That(aura.Cooldown, Is.EqualTo(0.05f));
            Assert.That(aura.RequiredTargetTag, Is.EqualTo("enemy"));
        }
        finally
        {
            Object.DestroyImmediate(aura);
        }
    }

    private static GameplayEffectDefinition CreateEffect()
    {
        GameplayEffectDefinition effect =
            ScriptableObject.CreateInstance<GameplayEffectDefinition>();
        effect.Configure(
            "enemy.buff.test_support",
            new GameplayEffectModifier(
                GameplayAttributeId.EnemyAttackDamage,
                GameplayModifierOperation.Multiply,
                0.25f));
        return effect;
    }
}
