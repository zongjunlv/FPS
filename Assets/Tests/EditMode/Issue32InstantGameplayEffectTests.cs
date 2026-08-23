using FPS.GameplayEffects;
using NUnit.Framework;
using UnityEngine;

namespace FPS.Tests.Architecture
{
    public sealed class Issue32InstantGameplayEffectTests
    {
        [Test]
        public void InstantEffectPreviewsThenAppliesWithoutActiveInstance()
        {
            AttributeTarget target = ScriptableObject.CreateInstance<AttributeTarget>();
            GameplayEffectDefinition effect = CreateRestoreEffect(
                "medical-kit",
                GameplayAttributeId.CurrentHealth,
                35f);

            try
            {
                target.Health = 40f;
                var runtime = new GameplayEffectRuntime(target);
                var context = new GameplayEffectContext(
                    "medical_kit",
                    effect,
                    target);

                GameplayEffectExecutionResult preview = runtime.PreviewInstant(
                    effect,
                    context,
                    target);
                Assert.That(preview.Succeeded, Is.True);
                Assert.That(preview.AppliedAmount, Is.EqualTo(35f));
                Assert.That(target.Health, Is.EqualTo(40f),
                    "Preview must not mutate the target.");

                GameplayEffectExecutionResult result = runtime.ExecuteInstant(
                    effect,
                    context,
                    target);
                Assert.That(result.Succeeded, Is.True);
                Assert.That(result.AppliedAmount, Is.EqualTo(35f));
                Assert.That(target.Health, Is.EqualTo(75f));
                Assert.That(runtime.ActiveInstances, Is.Empty,
                    "Instant effects must not leak into persistent instances.");
            }
            finally
            {
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(effect);
            }
        }

        [Test]
        public void FullAttributeReturnsNoChangeAndDoesNotMutate()
        {
            AttributeTarget target = ScriptableObject.CreateInstance<AttributeTarget>();
            GameplayEffectDefinition effect = CreateRestoreEffect(
                "armor-pack",
                GameplayAttributeId.CurrentArmor,
                30f);

            try
            {
                target.Armor = 100f;
                var runtime = new GameplayEffectRuntime(target);
                GameplayEffectExecutionResult result = runtime.ExecuteInstant(
                    effect,
                    new GameplayEffectContext("armor_pack", effect, target),
                    target);

                Assert.That(result.Succeeded, Is.False);
                Assert.That(result.Failure,
                    Is.EqualTo(GameplayEffectExecutionFailure.NoChange));
                Assert.That(target.Armor, Is.EqualTo(100f));
            }
            finally
            {
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(effect);
            }
        }

        [Test]
        public void RejectedCommitRollsBackPreviouslyAppliedAttributes()
        {
            AttributeTarget target = ScriptableObject.CreateInstance<AttributeTarget>();
            GameplayEffectDefinition effect =
                ScriptableObject.CreateInstance<GameplayEffectDefinition>();
            effect.ConfigureInstant(
                "compound-restore",
                new GameplayEffectModifier(
                    GameplayAttributeId.CurrentHealth,
                    GameplayModifierOperation.Add,
                    20f),
                new GameplayEffectModifier(
                    GameplayAttributeId.CurrentArmor,
                    GameplayModifierOperation.Add,
                    20f));

            try
            {
                target.Health = 50f;
                target.Armor = 50f;
                target.RejectArmorWrites = true;
                var runtime = new GameplayEffectRuntime(target);
                GameplayEffectExecutionResult result = runtime.ExecuteInstant(
                    effect,
                    new GameplayEffectContext("compound", effect, target),
                    target);

                Assert.That(result.Succeeded, Is.False);
                Assert.That(result.Failure,
                    Is.EqualTo(GameplayEffectExecutionFailure.CommitRejected));
                Assert.That(target.Health, Is.EqualTo(50f),
                    "A later rejected attribute must roll back earlier writes.");
                Assert.That(target.Armor, Is.EqualTo(50f));
            }
            finally
            {
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(effect);
            }
        }

        private static GameplayEffectDefinition CreateRestoreEffect(
            string id,
            GameplayAttributeId attribute,
            float amount)
        {
            GameplayEffectDefinition effect =
                ScriptableObject.CreateInstance<GameplayEffectDefinition>();
            effect.ConfigureInstant(
                id,
                new GameplayEffectModifier(
                    attribute,
                    GameplayModifierOperation.Add,
                    amount));
            return effect;
        }

        private sealed class AttributeTarget : ScriptableObject,
            IGameplayEffectAttributeTarget
        {
            public float Health { get; set; } = 100f;
            public float Armor { get; set; } = 100f;
            public bool RejectArmorWrites { get; set; }

            public bool TryGetGameplayAttribute(
                GameplayAttributeId attribute,
                out float currentValue,
                out float minimumValue,
                out float maximumValue)
            {
                minimumValue = 0f;
                maximumValue = 100f;

                switch (attribute)
                {
                    case GameplayAttributeId.CurrentHealth:
                        currentValue = Health;
                        return true;
                    case GameplayAttributeId.CurrentArmor:
                        currentValue = Armor;
                        return true;
                    default:
                        currentValue = 0f;
                        return false;
                }
            }

            public bool TrySetGameplayAttribute(
                GameplayAttributeId attribute,
                float value,
                out float appliedAmount)
            {
                appliedAmount = 0f;

                if (attribute == GameplayAttributeId.CurrentArmor &&
                    RejectArmorWrites)
                {
                    return false;
                }

                switch (attribute)
                {
                    case GameplayAttributeId.CurrentHealth:
                        appliedAmount = value - Health;
                        Health = value;
                        return true;
                    case GameplayAttributeId.CurrentArmor:
                        appliedAmount = value - Armor;
                        Armor = value;
                        return true;
                    default:
                        return false;
                }
            }
        }
    }
}
