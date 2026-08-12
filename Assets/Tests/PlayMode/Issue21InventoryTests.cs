using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace FPS.Tests.PlayMode
{
    public sealed class Issue21InventoryTests
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
        public void InventoryStacksAcrossSlotsAndRejectsOverflowAtomically()
        {
            Type definitionType = Type.GetType(
                "ItemDefinition, Assembly-CSharp");
            Type inventoryType = Type.GetType(
                "InventoryState, Assembly-CSharp");
            ScriptableObject medkit = CreateItem(
                definitionType, "medical_kit", 3, 35f);

            try
            {
                object inventory = Activator.CreateInstance(inventoryType, 2);
                int changeCount = 0;
                EventInfo changed = inventoryType.GetEvent("Changed");
                Action handler = () => changeCount++;
                changed.AddEventHandler(inventory, handler);
                object spec = definitionType.GetMethod("ToSpec")
                    .Invoke(medkit, null);
                Assert.That(inventoryType.GetMethod("TryAdd").Invoke(
                    inventory, new[] { spec, (object)5 }), Is.EqualTo(true));
                Assert.That(inventoryType.GetMethod("GetQuantity").Invoke(
                    inventory, new object[] { "medical_kit" }), Is.EqualTo(5));
                Assert.That(inventoryType.GetProperty("OccupiedSlotCount")
                    .GetValue(inventory), Is.EqualTo(2));
                Assert.That(inventoryType.GetMethod("TryAdd").Invoke(
                    inventory, new[] { spec, (object)2 }), Is.EqualTo(false),
                    "An add that cannot fully fit must not partially mutate inventory.");
                Assert.That(changeCount, Is.EqualTo(1),
                    "A failed add must not publish an inventory change.");
                Assert.That(inventoryType.GetMethod("GetQuantity").Invoke(
                    inventory, new object[] { "medical_kit" }), Is.EqualTo(5));
                Assert.That(inventoryType.GetMethod("TryRemove").Invoke(
                    inventory, new object[] { "medical_kit", 4 }), Is.EqualTo(true));
                Assert.That(inventoryType.GetMethod("GetQuantity").Invoke(
                    inventory, new object[] { "medical_kit" }), Is.EqualTo(1));
                Assert.That(inventoryType.GetProperty("OccupiedSlotCount")
                    .GetValue(inventory), Is.EqualTo(1));
                Assert.That(changeCount, Is.EqualTo(2));
                changed.RemoveEventHandler(inventory, handler);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(medkit);
            }
        }

        [Test]
        public void MedicalKitOnlyConsumesAfterSuccessfulHealing()
        {
            Type definitionType = Type.GetType(
                "ItemDefinition, Assembly-CSharp");
            Type inventoryType = Type.GetType(
                "InventoryState, Assembly-CSharp");
            Type registryType = Type.GetType(
                "ItemEffectRegistry, Assembly-CSharp");
            Type healthType = Type.GetType("Health, Assembly-CSharp");
            Type damageType = Type.GetType("DamageInfo, Assembly-CSharp");
            ScriptableObject medkit = CreateItem(
                definitionType, "medical_kit", 5, 35f);
            GameObject player = new GameObject("Inventory Player");

            try
            {
                Component health = player.AddComponent(healthType);
                healthType.GetMethod("Initialize", new[]
                {
                    typeof(float), typeof(float)
                }).Invoke(health, new object[] { 100f, 0f });
                object inventory = Activator.CreateInstance(inventoryType, 6);
                object spec = definitionType.GetMethod("ToSpec")
                    .Invoke(medkit, null);
                inventoryType.GetMethod("TryAdd").Invoke(
                    inventory, new[] { spec, (object)2 });
                object registry = Activator.CreateInstance(registryType);

                Assert.That(registryType.GetMethod("TryApply").Invoke(
                    registry, new object[] { medkit, health }), Is.EqualTo(false));
                Assert.That(inventoryType.GetMethod("GetQuantity").Invoke(
                    inventory, new object[] { "medical_kit" }), Is.EqualTo(2));

                object damage = Activator.CreateInstance(
                    damageType, 60f, Vector3.zero, Vector3.forward, null);
                healthType.GetMethod("ApplyDamage").Invoke(
                    health, new[] { damage });
                Assert.That(registryType.GetMethod("TryApply").Invoke(
                    registry, new object[] { medkit, health }), Is.EqualTo(true));
                Assert.That(inventoryType.GetMethod("TryRemove").Invoke(
                    inventory, new object[] { "medical_kit", 1 }), Is.EqualTo(true));
                Assert.That(ReadFloat(healthType, health, "CurrentHealth"),
                    Is.EqualTo(75f));
                Assert.That(inventoryType.GetMethod("GetQuantity").Invoke(
                    inventory, new object[] { "medical_kit" }), Is.EqualTo(1));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(player);
                UnityEngine.Object.DestroyImmediate(medkit);
            }
        }

        [Test]
        public void WorldPickupSettlesOnceAndRemainsWhenInventoryIsFull()
        {
            Type definitionType = Type.GetType(
                "ItemDefinition, Assembly-CSharp");
            Type inventoryType = Type.GetType(
                "InventoryState, Assembly-CSharp");
            Type controllerType = Type.GetType(
                "PlayerInventoryController, Assembly-CSharp");
            Type pickupType = Type.GetType(
                "WorldItemPickup, Assembly-CSharp");
            ScriptableObject medkit = CreateItem(
                definitionType, "medical_kit", 1, 35f);
            GameObject player = new GameObject("Pickup Player");
            GameObject pickupObject = new GameObject("Medical Pickup");

            try
            {
                Component controller = player.AddComponent(controllerType);
                controllerType.GetMethod("ConfigureInventory").Invoke(
                    controller, new object[] { 1 });
                controllerType.GetMethod("RegisterItem").Invoke(
                    controller, new object[] { medkit });
                Component pickup = pickupObject.AddComponent(pickupType);
                pickupType.GetMethod("Configure").Invoke(
                    pickup, new object[] { medkit, 1 });
                Assert.That(pickupType.GetMethod("TryBegin").Invoke(
                    pickup, new object[] { player }), Is.EqualTo(true));
                Assert.That(pickupType.GetMethod("Advance").Invoke(
                    pickup, new object[] { player, 0.1f }), Is.EqualTo(true));
                Assert.That(pickupType.GetProperty("SettlementCount")
                    .GetValue(pickup), Is.EqualTo(1));
                Assert.That(pickupType.GetMethod("TryBegin").Invoke(
                    pickup, new object[] { player }), Is.EqualTo(false));

                object inventory = controllerType.GetProperty("Inventory")
                    .GetValue(controller);
                Assert.That(inventoryType.GetMethod("GetQuantity").Invoke(
                    inventory, new object[] { "medical_kit" }), Is.EqualTo(1));
                GameObject secondObject = new GameObject("Full Pickup");
                Component second = secondObject.AddComponent(pickupType);
                pickupType.GetMethod("Configure").Invoke(
                    second, new object[] { medkit, 1 });
                Assert.That(pickupType.GetMethod("TryBegin").Invoke(
                    second, new object[] { player }), Is.EqualTo(true));
                Assert.That(pickupType.GetMethod("Advance").Invoke(
                    second, new object[] { player, 0.1f }), Is.EqualTo(true));
                Assert.That(pickupType.GetProperty("IsClaimed")
                    .GetValue(second), Is.EqualTo(false));
                Assert.That(pickupType.GetProperty("SettlementCount")
                    .GetValue(second), Is.EqualTo(0));
                UnityEngine.Object.DestroyImmediate(secondObject);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(pickupObject);
                UnityEngine.Object.DestroyImmediate(player);
                UnityEngine.Object.DestroyImmediate(medkit);
            }
        }

        [Test]
        public void PausedPerceptionCannotTurnAlertIntoSearch()
        {
            Type awarenessType = Type.GetType(
                "EnemyAwarenessStateMachine, Assembly-CSharp");
            object awareness = Activator.CreateInstance(awarenessType);
            awarenessType.GetMethod("Configure").Invoke(
                awareness, new object[] { 1f, 0.5f, 4f });
            awarenessType.GetMethod("ApplySharedAlert").Invoke(
                awareness, new object[] { Vector3.forward, 0.8f });
            Assert.That(awarenessType.GetProperty("State").GetValue(awareness)
                .ToString(), Is.EqualTo("Alert"));

            awarenessType.GetMethod("Observe").Invoke(
                awareness, new object[] { Vector3.forward * 2f, 0f });
            awarenessType.GetMethod("ApplySharedAlert").Invoke(
                awareness, new object[] { Vector3.forward * 3f, 0.5f });

            Assert.That(awarenessType.GetProperty("State").GetValue(awareness)
                .ToString(), Is.EqualTo("Alert"),
                "A zero-time paused frame must not downgrade Alert and allow " +
                "a squad signal to force Search.");
        }

        [UnityTest]
        public IEnumerator CityNewPickupInventoryUseLockAndRestartFormAClosedLoop()
        {
            LogAssert.ignoreFailingMessages = true;
            yield return LoadCityNew();
            Type controllerType = Type.GetType(
                "PlayerInventoryController, Assembly-CSharp");
            Type bootstrapType = Type.GetType(
                "CityNewInventoryBootstrap, Assembly-CSharp");
            Type pickupType = Type.GetType(
                "WorldItemPickup, Assembly-CSharp");
            Type inventoryType = Type.GetType(
                "InventoryState, Assembly-CSharp");
            Type viewType = Type.GetType("InventoryView, Assembly-CSharp");
            Type inputType = Type.GetType("PlayerInputReader, Assembly-CSharp");
            Type hudType = Type.GetType("UnifiedGameHud, Assembly-CSharp");
            Type playerType = Type.GetType("PlayerController, Assembly-CSharp");
            Type combatType = Type.GetType(
                "PlayerCombatController, Assembly-CSharp");
            Type schedulerType = Type.GetType(
                "EnemyPerceptionScheduler, Assembly-CSharp");
            Type healthType = Type.GetType("Health, Assembly-CSharp");
            Type damageType = Type.GetType("DamageInfo, Assembly-CSharp");
            Type locksType = Type.GetType(
                "GameplayLockCoordinator, Assembly-CSharp");
            Type reasonType = Type.GetType(
                "GameplayLockReason, Assembly-CSharp");
            Component controller = (Component)UnityEngine.Object
                .FindAnyObjectByType(controllerType);
            Component bootstrap = controller.GetComponent(bootstrapType);
            Component pickup = (Component)bootstrapType.GetProperty("Pickup")
                .GetValue(bootstrap);
            Assert.That(pickup, Is.Not.Null);

            Assert.That(pickupType.GetMethod("TryBegin").Invoke(
                pickup, new object[] { controller.gameObject }), Is.EqualTo(true));
            Assert.That(pickupType.GetMethod("Advance").Invoke(
                pickup, new object[] { controller.gameObject, 0.1f }),
                Is.EqualTo(true));
            object inventory = controllerType.GetProperty("Inventory")
                .GetValue(controller);
            Assert.That(inventoryType.GetMethod("GetQuantity").Invoke(
                inventory, new object[] { "medical_kit" }), Is.EqualTo(1));
            Assert.That(controllerType.GetMethod("Open").Invoke(
                controller, null), Is.EqualTo(true));
            yield return null;
            Component scheduler = (Component)UnityEngine.Object
                .FindAnyObjectByType(schedulerType);
            long checksWhileOpening = (long)schedulerType
                .GetProperty("TotalCheckCount").GetValue(scheduler);

            for (int frame = 0; frame < 3; frame++)
            {
                yield return null;
            }

            Assert.That(schedulerType.GetProperty("TotalCheckCount")
                .GetValue(scheduler), Is.EqualTo(checksWhileOpening),
                "Enemy perception must be fully frozen while inventory is open.");
            Component view = (Component)UnityEngine.Object
                .FindAnyObjectByType(viewType);
            Assert.That(viewType.GetProperty("SlotCount").GetValue(view),
                Is.EqualTo(12));
            Assert.That(viewType.GetProperty("SelectedIndex").GetValue(view),
                Is.EqualTo(0));
            Assert.That(viewType.GetProperty("DetailName").GetValue(view),
                Is.EqualTo("医疗包"));
            Assert.That(viewType.GetProperty("DetailDescription").GetValue(view)
                .ToString(), Does.Contain("恢复生命值"));
            Assert.That(viewType.GetProperty("DetailQuantity").GetValue(view),
                Is.EqualTo("持有数量：1"));
            Assert.That(viewType.GetProperty("DetailIconVisible").GetValue(view),
                Is.EqualTo(true));
            Assert.That(Time.timeScale, Is.EqualTo(0f));
            Component input = controller.GetComponent(inputType);
            Assert.That(inputType.GetProperty("InventoryActionEnabled")
                .GetValue(input), Is.EqualTo(true),
                "The inventory action must remain enabled while gameplay is locked.");
            Assert.That(playerType.GetProperty("GameplayInputEnabled")
                .GetValue(controller.GetComponent(playerType)), Is.EqualTo(false));
            Assert.That(combatType.GetProperty("GameplayInputEnabled")
                .GetValue(controller.GetComponent(combatType)), Is.EqualTo(false));

            Component health = controller.GetComponent(healthType);
            Assert.That(controllerType.GetMethod("TryUse").Invoke(
                controller, new object[] { 0 }), Is.EqualTo(false),
                "A full-health use must not consume the medical kit.");
            Assert.That(inventoryType.GetMethod("GetQuantity").Invoke(
                inventory, new object[] { "medical_kit" }), Is.EqualTo(1));
            object damage = Activator.CreateInstance(
                damageType, 135f, Vector3.zero, Vector3.forward, null);
            healthType.GetMethod("ApplyDamage").Invoke(health, new[] { damage });
            Component hud = (Component)UnityEngine.Object
                .FindAnyObjectByType(hudType);
            int vitalsBefore = (int)hudType
                .GetProperty("VitalsRefreshCount").GetValue(hud);
            Assert.That(controllerType.GetMethod("TryUse").Invoke(
                controller, new object[] { 0 }), Is.EqualTo(true));
            Assert.That(ReadFloat(healthType, health, "CurrentHealth"),
                Is.EqualTo(100f));
            Assert.That(hudType.GetProperty("HealthText").GetValue(hud)
                .ToString(), Does.Contain("100 / 100"));
            Assert.That(hudType.GetProperty("VitalsRefreshCount").GetValue(hud),
                Is.GreaterThan(vitalsBefore));
            Assert.That(inventoryType.GetMethod("GetQuantity").Invoke(
                inventory, new object[] { "medical_kit" }), Is.EqualTo(0));

            Component locks = controller.GetComponent(locksType);
            object outerLease = locksType.GetMethod("Acquire").Invoke(
                locks,
                new[] { Enum.Parse(reasonType, "PauseMenu") });
            controllerType.GetMethod("Close").Invoke(controller, null);
            Assert.That(Time.timeScale, Is.EqualTo(0f),
                "Closing inventory must preserve an outer pause lock.");
            outerLease.GetType().GetMethod("Dispose").Invoke(outerLease, null);
            Assert.That(Time.timeScale, Is.EqualTo(1f).Within(0.001f));

            yield return LoadCityNew();
            Component fresh = (Component)UnityEngine.Object
                .FindAnyObjectByType(controllerType);
            object freshInventory = controllerType.GetProperty("Inventory")
                .GetValue(fresh);
            Assert.That(inventoryType.GetMethod("GetQuantity").Invoke(
                freshInventory, new object[] { "medical_kit" }), Is.EqualTo(0));
            Assert.That(controllerType.GetProperty("IsOpen").GetValue(fresh),
                Is.EqualTo(false));
        }

        private static ScriptableObject CreateItem(
            Type definitionType,
            string stableId,
            int maximumStack,
            float amount)
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
                    "医疗包",
                    "恢复生命值。",
                    null,
                    Enum.Parse(itemType, "Consumable"),
                    maximumStack,
                    Enum.Parse(effectType, "RestoreHealth"),
                    amount
                });
            return definition;
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
