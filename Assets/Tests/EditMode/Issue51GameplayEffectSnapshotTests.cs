using System;
using System.Collections.Generic;
using FPS.GameplayEffects;
using NUnit.Framework;
using UnityEngine;

namespace FPS.Tests.Architecture
{
    public sealed class Issue51GameplayEffectSnapshotTests
    {
        [Test]
        public void PersistentMultiplicityAndSourcesRoundTrip()
        {
            GameObject originalTarget = new("Original Target");
            GameObject restoredTarget = new("Restored Target");
            GameObject sourceA = new("Source A");
            GameObject sourceB = new("Source B");
            GameplayEffectDefinition effect = CreatePersistent("upgrade.vitality");

            try
            {
                var sourceKeys = new Dictionary<UnityEngine.Object, string>
                {
                    [sourceA] = "source-a",
                    [sourceB] = "source-b"
                };
                var sources = new Dictionary<string, UnityEngine.Object>
                {
                    ["source-a"] = sourceA,
                    ["source-b"] = sourceB
                };
                var original = new GameplayEffectRuntime(originalTarget);
                original.Apply(effect, new GameplayEffectContext(
                    "card-a", sourceA, originalTarget));
                original.Apply(effect, new GameplayEffectContext(
                    "card-b", sourceB, originalTarget));

                Assert.That(original.TryCaptureSnapshot(
                    source => sourceKeys[source],
                    out GameplayEffectRuntimeSnapshot snapshot,
                    out string captureError), Is.True, captureError);

                var restored = new GameplayEffectRuntime(restoredTarget);
                Assert.That(restored.TryRestoreSnapshot(
                    snapshot,
                    id => id == effect.StableId ? effect : null,
                    key => sources.TryGetValue(key, out UnityEngine.Object source)
                        ? source
                        : null,
                    out string restoreError), Is.True, restoreError);

                Assert.That(restored.ActiveInstances, Has.Count.EqualTo(2));
                Assert.That(restored.ActiveInstances[0].Context.SourceId,
                    Is.EqualTo("card-a"));
                Assert.That(restored.ActiveInstances[0].Context.Source,
                    Is.SameAs(sourceA));
                Assert.That(restored.ActiveInstances[0].Context.Target,
                    Is.SameAs(restoredTarget));
                Assert.That(restored.ActiveInstances[1].Context.Source,
                    Is.SameAs(sourceB));
                Assert.That(restored.Evaluate(
                    GameplayAttributeId.MaximumHealth, 100f),
                    Is.EqualTo(120f).Within(0.0001f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(originalTarget);
                UnityEngine.Object.DestroyImmediate(restoredTarget);
                UnityEngine.Object.DestroyImmediate(sourceA);
                UnityEngine.Object.DestroyImmediate(sourceB);
                UnityEngine.Object.DestroyImmediate(effect);
            }
        }

        [Test]
        public void TimedStacksRoundTripRemainingTickAndReplaceOldestOrder()
        {
            GameObject originalTarget = new("Original Target");
            GameObject restoredTarget = new("Restored Target");
            GameObject sourceA = new("Source A");
            GameObject sourceB = new("Source B");
            GameObject sourceC = new("Source C");
            GameplayEffectDefinition effect = CreateTimed(
                "status.burn", 1f, 5f, 2,
                GameplayEffectStackRefreshPolicy.ReplaceOldestStack);

            try
            {
                var keys = new Dictionary<UnityEngine.Object, string>
                {
                    [sourceA] = "a", [sourceB] = "b", [sourceC] = "c"
                };
                var sources = new Dictionary<string, UnityEngine.Object>
                {
                    ["a"] = sourceA, ["b"] = sourceB, ["c"] = sourceC
                };
                var original = new GameplayEffectRuntime(originalTarget);
                ApplyTimed(original, effect, sourceA, originalTarget);
                original.AdvanceTimed(0.25f, null);
                ApplyTimed(original, effect, sourceB, originalTarget);

                Assert.That(original.TryCaptureSnapshot(
                    source => keys[source],
                    out GameplayEffectRuntimeSnapshot snapshot,
                    out string captureError), Is.True, captureError);
                Assert.That(snapshot.Instances, Has.Count.EqualTo(1));
                Assert.That(snapshot.Instances[0].TickRemaining,
                    Is.EqualTo(0.75f).Within(0.0001f));
                Assert.That(snapshot.Instances[0].TimedStacks[0].RemainingDuration,
                    Is.EqualTo(4.75f).Within(0.0001f));
                Assert.That(snapshot.Instances[0].TimedStacks[1].RemainingDuration,
                    Is.EqualTo(5f).Within(0.0001f));
                Assert.That(snapshot.Instances[0].TimedStacks[0].Order,
                    Is.LessThan(snapshot.Instances[0].TimedStacks[1].Order));

                var restored = new GameplayEffectRuntime(restoredTarget);
                Assert.That(restored.TryRestoreSnapshot(
                    snapshot,
                    id => id == effect.StableId ? effect : null,
                    key => sources.TryGetValue(key, out UnityEngine.Object source)
                        ? source
                        : null,
                    out string restoreError), Is.True, restoreError);

                int ticks = 0;
                restored.AdvanceTimed(0.74f, _ => ticks++);
                Assert.That(ticks, Is.Zero,
                    "Restoring must preserve the next-tick countdown without ticking.");
                ApplyTimed(restored, effect, sourceC, restoredTarget);
                var tickSources = new List<UnityEngine.Object>();
                restored.AdvanceTimed(
                    0.01f,
                    tick => tickSources.Add(tick.Context.Source));
                CollectionAssert.AreEquivalent(
                    new UnityEngine.Object[] { sourceB, sourceC },
                    tickSources,
                    "The stack that was oldest before saving must still be replaced.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(originalTarget);
                UnityEngine.Object.DestroyImmediate(restoredTarget);
                UnityEngine.Object.DestroyImmediate(sourceA);
                UnityEngine.Object.DestroyImmediate(sourceB);
                UnityEngine.Object.DestroyImmediate(sourceC);
                UnityEngine.Object.DestroyImmediate(effect);
            }
        }

        [Test]
        public void CaptureAndRestoreFailuresLeaveRuntimeUnchanged()
        {
            GameObject target = new("Target");
            GameObject source = new("Source");
            GameplayEffectDefinition currentEffect =
                CreatePersistent("upgrade.current");
            GameplayEffectDefinition savedEffect =
                CreatePersistent("upgrade.saved");

            try
            {
                var runtime = new GameplayEffectRuntime(target);
                GameplayEffectInstance current = runtime.Apply(
                    currentEffect,
                    new GameplayEffectContext("current", source, target));

                Assert.That(runtime.TryCaptureSnapshot(
                    _ => string.Empty,
                    out GameplayEffectRuntimeSnapshot failedCapture,
                    out string captureError), Is.False);
                Assert.That(failedCapture, Is.Null);
                Assert.That(captureError, Is.Not.Empty);

                var invalidSnapshot = new GameplayEffectRuntimeSnapshot(new[]
                {
                    new GameplayEffectInstanceSnapshot(
                        savedEffect.StableId,
                        new GameplayEffectContextSnapshot("saved", "missing-source"),
                        0f,
                        Array.Empty<GameplayEffectTimedStackSnapshot>())
                });

                Assert.That(runtime.TryRestoreSnapshot(
                    invalidSnapshot,
                    id => id == savedEffect.StableId ? savedEffect : null,
                    _ => null,
                    out string restoreError), Is.False);
                Assert.That(restoreError, Is.Not.Empty);
                Assert.That(runtime.ActiveInstances, Has.Count.EqualTo(1));
                Assert.That(runtime.ActiveInstances[0], Is.SameAs(current));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(source);
                UnityEngine.Object.DestroyImmediate(currentEffect);
                UnityEngine.Object.DestroyImmediate(savedEffect);
            }
        }

        private static GameplayEffectDefinition CreatePersistent(string id)
        {
            GameplayEffectDefinition effect =
                ScriptableObject.CreateInstance<GameplayEffectDefinition>();
            effect.Configure(
                id,
                new GameplayEffectModifier(
                    GameplayAttributeId.MaximumHealth,
                    GameplayModifierOperation.Add,
                    10f));
            return effect;
        }

        private static GameplayEffectDefinition CreateTimed(
            string id,
            float tickInterval,
            float duration,
            int maximumStacks,
            GameplayEffectStackRefreshPolicy refreshPolicy)
        {
            GameplayEffectDefinition effect =
                ScriptableObject.CreateInstance<GameplayEffectDefinition>();
            effect.ConfigureTimed(
                id,
                1f,
                tickInterval,
                duration,
                maximumStacks,
                refreshPolicy);
            return effect;
        }

        private static void ApplyTimed(
            GameplayEffectRuntime runtime,
            GameplayEffectDefinition definition,
            GameObject source,
            GameObject target)
        {
            Assert.That(runtime.ApplyTimed(
                definition,
                new GameplayEffectContext(source.name, source, target)).Succeeded,
                Is.True);
        }
    }
}
