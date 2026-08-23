using FPS.GameplayEffects;
using NUnit.Framework;
using UnityEngine;

namespace FPS.Tests.PlayMode
{
    public sealed class Issue32ConsumableGameplayEffectTests
    {
        [Test]
        public void MedicalAndArmorItemsUseInstantGameplayEffects()
        {
            GameObject player = new("Issue32 Player");
            ItemDefinition medicalKit = CreateItem(
                "medical_kit",
                ItemEffectType.RestoreHealth,
                35f);
            ItemDefinition armorPack = CreateItem(
                "armor_pack",
                ItemEffectType.RestoreArmor,
                30f);

            try
            {
                Health health = player.AddComponent<Health>();
                health.Initialize(100f, 100f);
                PlayerInventoryController inventory =
                    player.AddComponent<PlayerInventoryController>();
                Assert.That(inventory.TryAdd(medicalKit, 2), Is.True);
                Assert.That(inventory.TryAdd(armorPack, 2), Is.True);

                Assert.That(medicalKit.GameplayEffect.DurationPolicy,
                    Is.EqualTo(GameplayEffectDurationPolicy.Instant));
                Assert.That(medicalKit.GameplayEffect.Modifiers[0].Attribute,
                    Is.EqualTo(GameplayAttributeId.CurrentHealth));
                Assert.That(armorPack.GameplayEffect.Modifiers[0].Attribute,
                    Is.EqualTo(GameplayAttributeId.CurrentArmor));

                health.ApplyDamage(new DamageInfo(
                    140f,
                    Vector3.zero,
                    Vector3.forward,
                    null));
                Assert.That(health.CurrentHealth, Is.EqualTo(60f));
                Assert.That(health.CurrentArmor, Is.Zero);

                Assert.That(inventory.TryUse(0), Is.True);
                Assert.That(health.CurrentHealth, Is.EqualTo(95f));
                Assert.That(inventory.TryUse(1), Is.True);
                Assert.That(health.CurrentArmor, Is.EqualTo(30f));
                Assert.That(inventory.Inventory.GetQuantity("medical_kit"),
                    Is.EqualTo(1));
                Assert.That(inventory.Inventory.GetQuantity("armor_pack"),
                    Is.EqualTo(1));
            }
            finally
            {
                Object.DestroyImmediate(player);
                Object.DestroyImmediate(medicalKit);
                Object.DestroyImmediate(armorPack);
            }
        }

        [Test]
        public void EffectFailureRollsBackReservedInventoryItem()
        {
            GameObject player = new("Issue32 Rollback Player");
            GameObject wrongTarget = new("Wrong Effect Target");
            ItemDefinition medicalKit = CreateItem(
                "medical_kit",
                ItemEffectType.RestoreHealth,
                35f);

            try
            {
                Health health = player.AddComponent<Health>();
                health.Initialize(100f, 0f);
                health.ApplyDamage(new DamageInfo(
                    50f,
                    Vector3.zero,
                    Vector3.forward,
                    null));
                var inventory = new InventoryState(1);
                Assert.That(inventory.TryAdd(medicalKit.ToSpec(), 1), Is.True);
                var registry = new ItemEffectRegistry();
                var mismatchedRuntime = new GameplayEffectRuntime(wrongTarget);

                bool consumed = inventory.TryConsumeAt(
                    0,
                    1,
                    () => registry.Apply(
                        medicalKit,
                        new ItemUseContext(
                            health,
                            null,
                            mismatchedRuntime)).Succeeded);

                Assert.That(consumed, Is.False);
                Assert.That(inventory.GetQuantity("medical_kit"), Is.EqualTo(1),
                    "A failed gameplay effect must restore the reserved item.");
                Assert.That(health.CurrentHealth, Is.EqualTo(50f));
            }
            finally
            {
                Object.DestroyImmediate(player);
                Object.DestroyImmediate(wrongTarget);
                Object.DestroyImmediate(medicalKit);
            }
        }

        private static ItemDefinition CreateItem(
            string stableId,
            ItemEffectType effectType,
            float amount)
        {
            ItemDefinition item =
                ScriptableObject.CreateInstance<ItemDefinition>();
            item.Configure(
                stableId,
                stableId,
                "Issue 32 test item",
                null,
                ItemType.Consumable,
                5,
                effectType,
                amount);
            return item;
        }
    }
}
