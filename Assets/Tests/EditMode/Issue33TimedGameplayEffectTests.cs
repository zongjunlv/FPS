using System.Collections.Generic;
using FPS.GameplayEffects;
using NUnit.Framework;
using UnityEngine;

namespace FPS.Tests.Architecture
{
    public sealed class Issue33TimedGameplayEffectTests
    {
        [Test]
        public void TimedEffectStacksRefreshesTicksPerSourceAndExpires()
        {
            GameObject target = new("Burn Target");
            GameObject sourceA = new("Source A");
            GameObject sourceB = new("Source B");
            GameplayEffectDefinition burn = CreateBurn(
                1f,
                2.5f,
                3,
                GameplayEffectStackRefreshPolicy.RefreshAllDurations);

            try
            {
                var runtime = new GameplayEffectRuntime(target);
                Assert.That(Apply(runtime, burn, sourceA, target).StackCount,
                    Is.EqualTo(1));
                runtime.AdvanceTimed(0.75f, null);
                Assert.That(Apply(runtime, burn, sourceB, target).StackCount,
                    Is.EqualTo(2));
                Assert.That(Apply(runtime, burn, sourceA, target).StackCount,
                    Is.EqualTo(3));
                GameplayEffectApplicationResult capped = Apply(
                    runtime,
                    burn,
                    sourceB,
                    target);
                Assert.That(capped.StackAdded, Is.False);
                Assert.That(capped.DurationRefreshed, Is.True);

                var sources = new List<GameObject>();
                runtime.AdvanceTimed(
                    1f,
                    tick => sources.Add((GameObject)tick.Context.Source));
                CollectionAssert.AreEqual(
                    new[] { sourceA, sourceB, sourceA },
                    sources,
                    "Each stack must tick with the source that applied it.");

                runtime.AdvanceTimed(1.5f, _ => { });
                Assert.That(runtime.ActiveInstances, Is.Empty,
                    "All stacks must be removed when their duration expires.");
            }
            finally
            {
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(sourceA);
                Object.DestroyImmediate(sourceB);
                Object.DestroyImmediate(burn);
            }
        }

        [Test]
        public void ReplaceOldestRefreshPolicyTransfersSourceAtStackCap()
        {
            GameObject target = new("Burn Target");
            GameObject sourceA = new("Source A");
            GameObject sourceB = new("Source B");
            GameObject sourceC = new("Source C");
            GameplayEffectDefinition burn = CreateBurn(
                1f,
                3f,
                2,
                GameplayEffectStackRefreshPolicy.ReplaceOldestStack);

            try
            {
                var runtime = new GameplayEffectRuntime(target);
                Apply(runtime, burn, sourceA, target);
                runtime.AdvanceTimed(0.2f, null);
                Apply(runtime, burn, sourceB, target);
                GameplayEffectApplicationResult replaced = Apply(
                    runtime,
                    burn,
                    sourceC,
                    target);
                Assert.That(replaced.StackAdded, Is.False);
                Assert.That(replaced.DurationRefreshed, Is.True);

                var sources = new List<GameObject>();
                runtime.AdvanceTimed(
                    0.8f,
                    tick => sources.Add((GameObject)tick.Context.Source));
                CollectionAssert.AreEquivalent(
                    new[] { sourceB, sourceC },
                    sources);
                CollectionAssert.DoesNotContain(sources, sourceA);
            }
            finally
            {
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(sourceA);
                Object.DestroyImmediate(sourceB);
                Object.DestroyImmediate(sourceC);
                Object.DestroyImmediate(burn);
            }
        }

        private static GameplayEffectApplicationResult Apply(
            GameplayEffectRuntime runtime,
            GameplayEffectDefinition definition,
            GameObject source,
            GameObject target)
        {
            return runtime.ApplyTimed(
                definition,
                new GameplayEffectContext(source.name, source, target));
        }

        private static GameplayEffectDefinition CreateBurn(
            float tickInterval,
            float duration,
            int maximumStacks,
            GameplayEffectStackRefreshPolicy refreshPolicy)
        {
            GameplayEffectDefinition effect =
                ScriptableObject.CreateInstance<GameplayEffectDefinition>();
            effect.ConfigureTimed(
                "status.burn",
                4f,
                tickInterval,
                duration,
                maximumStacks,
                refreshPolicy);
            return effect;
        }
    }
}
