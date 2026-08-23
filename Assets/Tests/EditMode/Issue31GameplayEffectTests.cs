using FPS.GameplayEffects;
using NUnit.Framework;
using UnityEngine;

namespace FPS.Tests.Architecture
{
    public sealed class Issue31GameplayEffectTests
    {
        [Test]
        public void AggregationUsesAddMultiplyOverrideOrderDeterministically()
        {
            var target = new GameObject("Effect Target");
            GameplayEffectDefinition add = CreateEffect(
                "add",
                GameplayModifierOperation.Add,
                10f);
            GameplayEffectDefinition multiply = CreateEffect(
                "multiply",
                GameplayModifierOperation.Multiply,
                0.5f);
            GameplayEffectDefinition lowOverride = CreateEffect(
                "override-low",
                GameplayModifierOperation.Override,
                200f,
                1);
            GameplayEffectDefinition highOverride = CreateEffect(
                "override-high",
                GameplayModifierOperation.Override,
                240f,
                2);

            try
            {
                var runtime = new GameplayEffectRuntime(target);
                GameplayEffectContext context = new(
                    "test",
                    add,
                    target);
                GameplayEffectInstance multiplyInstance = runtime.Apply(
                    multiply,
                    new GameplayEffectContext("multiply", multiply, target));
                GameplayEffectInstance addInstance = runtime.Apply(
                    add,
                    context);

                Assert.That(
                    runtime.Evaluate(GameplayAttributeId.MaximumHealth, 100f),
                    Is.EqualTo(165f).Within(0.0001f));

                GameplayEffectInstance low = runtime.Apply(
                    lowOverride,
                    new GameplayEffectContext("low", lowOverride, target));
                GameplayEffectInstance high = runtime.Apply(
                    highOverride,
                    new GameplayEffectContext("high", highOverride, target));
                Assert.That(
                    runtime.Evaluate(GameplayAttributeId.MaximumHealth, 100f),
                    Is.EqualTo(240f));

                Assert.That(runtime.Remove(high.InstanceId), Is.True);
                Assert.That(
                    runtime.Evaluate(GameplayAttributeId.MaximumHealth, 100f),
                    Is.EqualTo(200f));
                Assert.That(runtime.Remove(low.InstanceId), Is.True);
                Assert.That(runtime.Remove(addInstance.InstanceId), Is.True);
                Assert.That(runtime.Remove(multiplyInstance.InstanceId), Is.True);
                Assert.That(
                    runtime.Evaluate(GameplayAttributeId.MaximumHealth, 100f),
                    Is.EqualTo(100f));
            }
            finally
            {
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(add);
                Object.DestroyImmediate(multiply);
                Object.DestroyImmediate(lowOverride);
                Object.DestroyImmediate(highOverride);
            }
        }

        [Test]
        public void RuntimeInstancesTrackUniqueIdSourceAndTarget()
        {
            var target = new GameObject("Effect Target");
            GameplayEffectDefinition effect = CreateEffect(
                "vitality",
                GameplayModifierOperation.Multiply,
                0.2f);

            try
            {
                var runtime = new GameplayEffectRuntime(target);
                var context = new GameplayEffectContext(
                    "vitality-card",
                    effect,
                    target);
                GameplayEffectInstance first = runtime.Apply(effect, context);
                GameplayEffectInstance second = runtime.Apply(effect, context);

                Assert.That(first.InstanceId, Is.Not.EqualTo(second.InstanceId));
                Assert.That(first.Context.SourceId, Is.EqualTo("vitality-card"));
                Assert.That(first.Context.Source, Is.SameAs(effect));
                Assert.That(first.Context.Target, Is.SameAs(target));
                Assert.That(runtime.ActiveInstances, Has.Count.EqualTo(2));
                runtime.Clear();
                Assert.That(runtime.ActiveInstances, Is.Empty);
            }
            finally
            {
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(effect);
            }
        }

        private static GameplayEffectDefinition CreateEffect(
            string id,
            GameplayModifierOperation operation,
            float magnitude,
            int priority = 0)
        {
            GameplayEffectDefinition effect =
                ScriptableObject.CreateInstance<GameplayEffectDefinition>();
            effect.Configure(
                id,
                new GameplayEffectModifier(
                    GameplayAttributeId.MaximumHealth,
                    operation,
                    magnitude,
                    priority));
            return effect;
        }
    }
}
