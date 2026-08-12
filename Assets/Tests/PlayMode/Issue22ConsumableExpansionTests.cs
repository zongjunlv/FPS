using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace FPS.Tests.PlayMode
{
    public sealed class Issue22ConsumableExpansionTests
    {
        private const string CityNewScene =
            "Assets/ImportPackages/CSAssets2026/Scenes/CityNew.unity";

        [TearDown]
        public void RestoreRuntimeState()
        {
            Time.timeScale = 1f;
            LogAssert.ignoreFailingMessages = false;
        }

        [Test]
        public void AddReportsAcceptedAndRemainingAfterFillingExistingStacks()
        {
            Type inventoryType = Type.GetType(
                "InventoryState, Assembly-CSharp");
            Type specType = Type.GetType(
                "InventoryItemSpec, Assembly-CSharp");
            object inventory = Activator.CreateInstance(inventoryType, 2);
            object spec = Activator.CreateInstance(
                specType, "armor_pack", 3);
            int changes = 0;
            EventInfo changed = inventoryType.GetEvent("Changed");
            Action handler = () => changes++;
            changed.AddEventHandler(inventory, handler);

            object first = inventoryType.GetMethod("Add").Invoke(
                inventory, new[] { spec, (object)2 });
            object second = inventoryType.GetMethod("Add").Invoke(
                inventory, new[] { spec, (object)5 });

            AssertAddResult(first, 2, 0);
            AssertAddResult(second, 4, 1);
            Assert.That(inventoryType.GetMethod("GetQuantity").Invoke(
                inventory, new object[] { "armor_pack" }), Is.EqualTo(6));
            Assert.That(ReadSlotQuantity(inventoryType, inventory, 0),
                Is.EqualTo(3), "The existing stack must be filled first.");
            Assert.That(ReadSlotQuantity(inventoryType, inventory, 1),
                Is.EqualTo(3));
            Assert.That(changes, Is.EqualTo(2),
                "Each successful add transaction must publish exactly once.");

            object rejected = inventoryType.GetMethod("Add").Invoke(
                inventory, new[] { spec, (object)1 });
            AssertAddResult(rejected, 0, 1);
            Assert.That(changes, Is.EqualTo(2),
                "A fully rejected add must not publish a change.");
            changed.RemoveEventHandler(inventory, handler);
        }

        [Test]
        public void ArmorPackOnlySucceedsWhenArmorCanBeRestored()
        {
            Type definitionType = Type.GetType(
                "ItemDefinition, Assembly-CSharp");
            Type registryType = Type.GetType(
                "ItemEffectRegistry, Assembly-CSharp");
            Type contextType = Type.GetType(
                "ItemUseContext, Assembly-CSharp");
            Type healthType = Type.GetType("Health, Assembly-CSharp");
            Type damageType = Type.GetType("DamageInfo, Assembly-CSharp");
            ScriptableObject armorPack = CreateItem(
                definitionType,
                "armor_pack",
                "护甲包",
                "RestoreArmor",
                30f,
                5);
            GameObject player = new GameObject("Armor Item Player");

            try
            {
                Component health = player.AddComponent(healthType);
                healthType.GetMethod("Initialize", new[]
                {
                    typeof(float), typeof(float)
                }).Invoke(health, new object[] { 100f, 100f });
                object context = Activator.CreateInstance(
                    contextType, health, null);
                object registry = Activator.CreateInstance(registryType);
                object fullResult = registryType.GetMethod("Apply", new[]
                {
                    definitionType, contextType
                }).Invoke(registry, new[] { armorPack, context });
                Assert.That(ReadResultSuccess(fullResult), Is.False);
                Assert.That(ReadResultReason(fullResult), Is.EqualTo("ArmorFull"));

                object damage = Activator.CreateInstance(
                    damageType, 40f, Vector3.zero, Vector3.forward, null);
                healthType.GetMethod("ApplyDamage").Invoke(
                    health, new[] { damage });
                object restored = registryType.GetMethod("Apply", new[]
                {
                    definitionType, contextType
                }).Invoke(registry, new[] { armorPack, context });
                Assert.That(ReadResultSuccess(restored), Is.True);
                Assert.That(ReadFloat(healthType, health, "CurrentArmor"),
                    Is.EqualTo(90f));
                Assert.That(ReadFloat(restored.GetType(), restored, "AppliedAmount"),
                    Is.EqualTo(30f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(player);
                UnityEngine.Object.DestroyImmediate(armorPack);
            }
        }

        [Test]
        public void ReserveAmmoApiClampsToConfiguredMaximumAndPublishesOnce()
        {
            Type ammoStateType = Type.GetType(
                "WeaponAmmoState, Assembly-CSharp");
            object state = Activator.CreateInstance(
                ammoStateType, 30, 80, 100);

            Assert.That(ammoStateType.GetMethod("AddReserveAmmo").Invoke(
                state, new object[] { 35 }), Is.EqualTo(20));
            Assert.That(ammoStateType.GetProperty("ReserveAmmo").GetValue(state),
                Is.EqualTo(100));
            Assert.That(ammoStateType.GetProperty("MaximumReserveAmmo")
                .GetValue(state), Is.EqualTo(100));
            Assert.That(ammoStateType.GetMethod("AddReserveAmmo").Invoke(
                state, new object[] { 10 }), Is.EqualTo(0));
            Assert.That(ammoStateType.GetMethod("AddReserveAmmo").Invoke(
                state, new object[] { -1 }), Is.EqualTo(0));
        }

        [Test]
        public void PartialWorldPickupPreservesEveryItemUntilFullyClaimed()
        {
            Type definitionType = Type.GetType(
                "ItemDefinition, Assembly-CSharp");
            Type controllerType = Type.GetType(
                "PlayerInventoryController, Assembly-CSharp");
            Type inventoryType = Type.GetType(
                "InventoryState, Assembly-CSharp");
            Type pickupType = Type.GetType(
                "WorldItemPickup, Assembly-CSharp");
            ScriptableObject item = CreateItem(
                definitionType,
                "rifle_ammo",
                "步枪弹药",
                "AddRifleAmmo",
                60f,
                3);
            GameObject player = new GameObject("Partial Pickup Player");
            GameObject pickupObject = new GameObject("Partial Pickup");

            try
            {
                Component controller = player.AddComponent(controllerType);
                controllerType.GetMethod("ConfigureInventory").Invoke(
                    controller, new object[] { 1 });
                Assert.That(controllerType.GetMethod("TryAdd").Invoke(
                    controller, new object[] { item, 2 }), Is.EqualTo(true));
                Component pickup = pickupObject.AddComponent(pickupType);
                pickupType.GetMethod("Configure").Invoke(
                    pickup, new object[] { item, 4 });

                Assert.That(pickupType.GetMethod("TryBegin").Invoke(
                    pickup, new object[] { player }), Is.EqualTo(true));
                pickupType.GetMethod("Advance").Invoke(
                    pickup, new object[] { player, 0.1f });
                Assert.That(pickupType.GetProperty("RemainingQuantity")
                    .GetValue(pickup), Is.EqualTo(3));
                Assert.That(pickupType.GetProperty("TotalAccepted")
                    .GetValue(pickup), Is.EqualTo(1));
                Assert.That(pickupType.GetProperty("IsClaimed")
                    .GetValue(pickup), Is.EqualTo(false));
                object state = controllerType.GetProperty("Inventory")
                    .GetValue(controller);
                Assert.That(inventoryType.GetMethod("GetQuantity").Invoke(
                    state, new object[] { "rifle_ammo" }), Is.EqualTo(3));
                Assert.That(inventoryType.GetMethod("TryRemove").Invoke(
                    state, new object[] { "rifle_ammo", 3 }), Is.EqualTo(true));

                Assert.That(pickupType.GetMethod("TryBegin").Invoke(
                    pickup, new object[] { player }), Is.EqualTo(true));
                pickupType.GetMethod("Advance").Invoke(
                    pickup, new object[] { player, 0.1f });
                Assert.That(pickupType.GetProperty("RemainingQuantity")
                    .GetValue(pickup), Is.EqualTo(0));
                Assert.That(pickupType.GetProperty("TotalAccepted")
                    .GetValue(pickup), Is.EqualTo(4));
                Assert.That(pickupType.GetProperty("IsClaimed")
                    .GetValue(pickup), Is.EqualTo(true));
                Assert.That(inventoryType.GetMethod("GetQuantity").Invoke(
                    state, new object[] { "rifle_ammo" }), Is.EqualTo(3));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(pickupObject);
                UnityEngine.Object.DestroyImmediate(player);
                UnityEngine.Object.DestroyImmediate(item);
            }
        }

        [Test]
        public void ArmorItemsConsumeOneOnlyAfterActualRestoration()
        {
            Type definitionType = Type.GetType(
                "ItemDefinition, Assembly-CSharp");
            Type controllerType = Type.GetType(
                "PlayerInventoryController, Assembly-CSharp");
            Type inventoryType = Type.GetType(
                "InventoryState, Assembly-CSharp");
            Type healthType = Type.GetType("Health, Assembly-CSharp");
            Type damageType = Type.GetType("DamageInfo, Assembly-CSharp");
            ScriptableObject armorPack = CreateItem(
                definitionType,
                "armor_pack",
                "护甲包",
                "RestoreArmor",
                30f,
                5);
            GameObject player = new GameObject("Armor Inventory Player");

            try
            {
                Component health = player.AddComponent(healthType);
                healthType.GetMethod("Initialize", new[]
                {
                    typeof(float), typeof(float)
                }).Invoke(health, new object[] { 100f, 100f });
                Component controller = player.AddComponent(controllerType);
                Assert.That(controllerType.GetMethod("TryAdd").Invoke(
                    controller, new object[] { armorPack, 3 }), Is.EqualTo(true));
                object damage = Activator.CreateInstance(
                    damageType, 40f, Vector3.zero, Vector3.forward, null);
                healthType.GetMethod("ApplyDamage").Invoke(
                    health, new[] { damage });
                object state = controllerType.GetProperty("Inventory")
                    .GetValue(controller);

                Assert.That(controllerType.GetMethod("TryUse").Invoke(
                    controller, new object[] { 0 }), Is.EqualTo(true));
                Assert.That(ReadFloat(healthType, health, "CurrentArmor"),
                    Is.EqualTo(90f));
                Assert.That(controllerType.GetMethod("TryUse").Invoke(
                    controller, new object[] { 0 }), Is.EqualTo(true));
                Assert.That(ReadFloat(healthType, health, "CurrentArmor"),
                    Is.EqualTo(100f));
                Assert.That(controllerType.GetMethod("TryUse").Invoke(
                    controller, new object[] { 0 }), Is.EqualTo(false));
                Assert.That(inventoryType.GetMethod("GetQuantity").Invoke(
                    state, new object[] { "armor_pack" }), Is.EqualTo(1));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(player);
                UnityEngine.Object.DestroyImmediate(armorPack);
            }
        }

        [UnityTest]
        public IEnumerator RifleAndHandgunAmmoItemsOnlyAffectTheirTargetWeapon()
        {
            LogAssert.ignoreFailingMessages = true;
            yield return LoadCityNew();
            Type inventoryControllerType = Type.GetType(
                "PlayerInventoryController, Assembly-CSharp");
            Type bootstrapType = Type.GetType(
                "CityNewInventoryBootstrap, Assembly-CSharp");
            Type combatType = Type.GetType(
                "PlayerCombatController, Assembly-CSharp");
            Component inventory = (Component)UnityEngine.Object
                .FindAnyObjectByType(inventoryControllerType);
            Component bootstrap = inventory.GetComponent(bootstrapType);
            Component combat = inventory.GetComponent(combatType);
            Array definitions = (Array)bootstrapType
                .GetProperty("Definitions").GetValue(bootstrap);
            object rifleAmmo = FindDefinition(definitions, "rifle_ammo");
            object handgunAmmo = FindDefinition(definitions, "handgun_ammo");
            object rifle = combatType.GetMethod("GetWeapon").Invoke(
                combat, new object[] { 0 });
            object handgun = combatType.GetMethod("GetWeapon").Invoke(
                combat, new object[] { 1 });
            Type weaponType = rifle.GetType();
            int rifleBefore = (int)weaponType.GetProperty("ReserveAmmo")
                .GetValue(rifle);
            int handgunBefore = (int)weaponType.GetProperty("ReserveAmmo")
                .GetValue(handgun);
            int rifleEvents = 0;
            Action rifleChanged = () => rifleEvents++;
            weaponType.GetEvent("AmmoChanged").AddEventHandler(
                rifle, rifleChanged);

            Assert.That(inventoryControllerType.GetMethod("TryAdd").Invoke(
                inventory, new[] { rifleAmmo, (object)1 }), Is.EqualTo(true));
            Assert.That(inventoryControllerType.GetMethod("TryUse").Invoke(
                inventory, new object[] { 0 }), Is.EqualTo(true));
            Assert.That(weaponType.GetProperty("ReserveAmmo").GetValue(rifle),
                Is.EqualTo(rifleBefore + 60));
            Assert.That(weaponType.GetProperty("ReserveAmmo").GetValue(handgun),
                Is.EqualTo(handgunBefore));
            Assert.That(rifleEvents, Is.EqualTo(1));

            Assert.That(inventoryControllerType.GetMethod("TryAdd").Invoke(
                inventory, new[] { handgunAmmo, (object)1 }), Is.EqualTo(true));
            Assert.That(inventoryControllerType.GetMethod("TryUse").Invoke(
                inventory, new object[] { 0 }), Is.EqualTo(true));
            Assert.That(weaponType.GetProperty("ReserveAmmo").GetValue(rifle),
                Is.EqualTo(rifleBefore + 60));
            Assert.That(weaponType.GetProperty("ReserveAmmo").GetValue(handgun),
                Is.EqualTo(handgunBefore + 24));
            weaponType.GetEvent("AmmoChanged").RemoveEventHandler(
                rifle, rifleChanged);
        }

        [UnityTest]
        public IEnumerator FullVitalsAndAmmoDoNotConsumeItemsAndUiExplainsWhy()
        {
            LogAssert.ignoreFailingMessages = true;
            yield return LoadCityNew();
            Type controllerType = Type.GetType(
                "PlayerInventoryController, Assembly-CSharp");
            Type bootstrapType = Type.GetType(
                "CityNewInventoryBootstrap, Assembly-CSharp");
            Type combatType = Type.GetType(
                "PlayerCombatController, Assembly-CSharp");
            Type inventoryType = Type.GetType(
                "InventoryState, Assembly-CSharp");
            Type viewType = Type.GetType("InventoryView, Assembly-CSharp");
            Component controller = (Component)UnityEngine.Object
                .FindAnyObjectByType(controllerType);
            Component bootstrap = controller.GetComponent(bootstrapType);
            Component combat = controller.GetComponent(combatType);
            Array definitions = (Array)bootstrapType
                .GetProperty("Definitions").GetValue(bootstrap);
            object armorPack = FindDefinition(definitions, "armor_pack");
            object rifleAmmo = FindDefinition(definitions, "rifle_ammo");
            object rifle = combatType.GetMethod("GetWeapon").Invoke(
                combat, new object[] { 0 });
            Type weaponType = rifle.GetType();
            int reserveMaximum = (int)weaponType
                .GetProperty("MaximumReserveAmmo").GetValue(rifle);
            int reserveCurrent = (int)weaponType
                .GetProperty("ReserveAmmo").GetValue(rifle);
            weaponType.GetMethod("AddReserveAmmo").Invoke(
                rifle, new object[] { reserveMaximum - reserveCurrent });

            Assert.That(controllerType.GetMethod("TryAdd").Invoke(
                controller, new[] { armorPack, (object)1 }), Is.EqualTo(true));
            Assert.That(controllerType.GetMethod("TryAdd").Invoke(
                controller, new[] { rifleAmmo, (object)1 }), Is.EqualTo(true));
            object state = controllerType.GetProperty("Inventory")
                .GetValue(controller);
            Assert.That(controllerType.GetMethod("TryUse").Invoke(
                controller, new object[] { 0 }), Is.EqualTo(false));
            Assert.That(inventoryType.GetMethod("GetQuantity").Invoke(
                state, new object[] { "armor_pack" }), Is.EqualTo(1));
            Assert.That(controllerType.GetMethod("Open").Invoke(
                controller, null), Is.EqualTo(true));
            yield return null;
            Component view = (Component)UnityEngine.Object
                .FindAnyObjectByType(viewType);
            Assert.That(viewType.GetProperty("UseButtonInteractable")
                .GetValue(view), Is.EqualTo(false));
            Assert.That(viewType.GetProperty("UseFailureReason")
                .GetValue(view).ToString(), Does.Contain("护甲"));
            viewType.GetMethod("SelectSlot").Invoke(view, new object[] { 1 });
            Assert.That(viewType.GetProperty("DetailEffect").GetValue(view)
                .ToString(), Does.Contain("步枪备弹"));
            Assert.That(viewType.GetProperty("UseFailureReason")
                .GetValue(view).ToString(), Does.Contain("备弹已满"));
            Assert.That(controllerType.GetMethod("TryUse").Invoke(
                controller, new object[] { 1 }), Is.EqualTo(false));
            Assert.That(inventoryType.GetMethod("GetQuantity").Invoke(
                state, new object[] { "rifle_ammo" }), Is.EqualTo(1));
            controllerType.GetMethod("Close").Invoke(controller, null);
        }

        private static ScriptableObject CreateItem(
            Type definitionType,
            string stableId,
            string displayName,
            string effectName,
            float amount,
            int maximumStack)
        {
            Type itemType = Type.GetType("ItemType, Assembly-CSharp");
            Type effectType = Type.GetType("ItemEffectType, Assembly-CSharp");
            ScriptableObject definition = ScriptableObject.CreateInstance(
                definitionType);
            definitionType.GetMethod("Configure").Invoke(
                definition,
                new object[]
                {
                    stableId,
                    displayName,
                    "测试物品说明。",
                    null,
                    Enum.Parse(itemType, "Consumable"),
                    maximumStack,
                    Enum.Parse(effectType, effectName),
                    amount
                });
            return definition;
        }

        private static object FindDefinition(Array definitions, string stableId)
        {
            foreach (object definition in definitions)
            {
                if (definition != null &&
                    string.Equals(
                        definition.GetType().GetProperty("StableId")
                            .GetValue(definition)?.ToString(),
                        stableId,
                        StringComparison.Ordinal))
                {
                    return definition;
                }
            }

            Assert.Fail($"Missing item definition: {stableId}");
            return null;
        }

        private static void AssertAddResult(
            object result,
            int accepted,
            int remaining)
        {
            Type type = result.GetType();
            Assert.That(type.GetProperty("Accepted").GetValue(result),
                Is.EqualTo(accepted));
            Assert.That(type.GetProperty("Remaining").GetValue(result),
                Is.EqualTo(remaining));
        }

        private static int ReadSlotQuantity(
            Type inventoryType,
            object inventory,
            int index)
        {
            object slot = inventoryType.GetMethod("GetSlot").Invoke(
                inventory, new object[] { index });
            return (int)slot.GetType().GetProperty("Quantity").GetValue(slot);
        }

        private static bool ReadResultSuccess(object result)
        {
            return (bool)result.GetType().GetProperty("Succeeded")
                .GetValue(result);
        }

        private static string ReadResultReason(object result)
        {
            return result.GetType().GetProperty("FailureReason")
                .GetValue(result)?.ToString() ?? string.Empty;
        }

        private static float ReadFloat(Type type, object target, string name)
        {
            return (float)type.GetProperty(name).GetValue(target);
        }

        private static IEnumerator LoadCityNew()
        {
            yield return SceneManager.LoadSceneAsync(
                CityNewScene, LoadSceneMode.Single);

            for (int frame = 0; frame < 8; frame++)
            {
                yield return null;
            }
        }
    }
}
