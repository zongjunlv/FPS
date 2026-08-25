using FPS.GameplayEffects;
using NUnit.Framework;
using UnityEngine;

namespace FPS.Tests.Architecture
{
    public sealed class Issue35ConditionalGameplayEffectTests
    {
        [Test]
        public void ThresholdTransitionsAreStrictAndIdempotent()
        {
            var condition = new GameplayEffectConditionState(0.35f);

            Assert.That(condition.Evaluate(35f, 100f),
                Is.EqualTo(GameplayEffectConditionTransition.None));
            Assert.That(condition.IsActive, Is.False,
                "Exactly at the threshold must remain inactive.");
            Assert.That(condition.Evaluate(34.99f, 100f),
                Is.EqualTo(GameplayEffectConditionTransition.Activated));
            Assert.That(condition.Evaluate(20f, 100f),
                Is.EqualTo(GameplayEffectConditionTransition.None));
            Assert.That(condition.Evaluate(35f, 100f),
                Is.EqualTo(GameplayEffectConditionTransition.Deactivated));
            Assert.That(condition.Evaluate(80f, 100f),
                Is.EqualTo(GameplayEffectConditionTransition.None));
            Assert.That(condition.Evaluate(34f, 100f),
                Is.EqualTo(GameplayEffectConditionTransition.Activated));
            Assert.That(condition.Evaluate(36f, 100f),
                Is.EqualTo(GameplayEffectConditionTransition.Deactivated));
            Assert.That(condition.Evaluate(0f, 100f),
                Is.EqualTo(GameplayEffectConditionTransition.None));
        }

        [Test]
        public void MultipleFireRateSourcesUseOneDeterministicAggregator()
        {
            var player = new GameObject("Fire Rate Effect Target");
            GameplayEffectDefinition lowHealth = CreateFireRateEffect(
                "low-health",
                0.35f);
            GameplayEffectDefinition stimulant = CreateFireRateEffect(
                "stimulant",
                0.1f);

            try
            {
                PlayerRuntimeCombatStats stats =
                    player.AddComponent<PlayerRuntimeCombatStats>();
                stats.SetWeaponModifiers(new WeaponRuntimeModifiers(
                    1f,
                    1.15f,
                    1f,
                    1f,
                    1f,
                    1f));
                GameplayEffectInstance low = stats.ApplyGameplayEffect(
                    lowHealth,
                    "low-health",
                    lowHealth);
                GameplayEffectInstance extra = stats.ApplyGameplayEffect(
                    stimulant,
                    "stimulant",
                    stimulant);

                Assert.That(stats.FireRateMultiplier,
                    Is.EqualTo(1.6f).Within(0.0001f));
                Assert.That(stats.ApplyFireInterval(0.16f),
                    Is.EqualTo(0.1f).Within(0.0001f));
                Assert.That(stats.RemoveGameplayEffect(low.InstanceId), Is.True);
                Assert.That(stats.FireRateMultiplier,
                    Is.EqualTo(1.25f).Within(0.0001f));
                Assert.That(stats.RemoveGameplayEffect(extra.InstanceId), Is.True);
                Assert.That(stats.FireRateMultiplier,
                    Is.EqualTo(1.15f).Within(0.0001f));
            }
            finally
            {
                Object.DestroyImmediate(player);
                Object.DestroyImmediate(lowHealth);
                Object.DestroyImmediate(stimulant);
            }
        }

        private static GameplayEffectDefinition CreateFireRateEffect(
            string id,
            float bonus)
        {
            GameplayEffectDefinition effect =
                ScriptableObject.CreateInstance<GameplayEffectDefinition>();
            effect.Configure(
                id,
                new GameplayEffectModifier(
                    GameplayAttributeId.WeaponFireRate,
                    GameplayModifierOperation.Add,
                    bonus));
            return effect;
        }
    }
}
