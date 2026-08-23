using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace FPS.Tests.PlayMode
{
    public sealed class Issue23InventoryOperationsTests
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
        public void MoveSwapMergeAndSplitPreserveEveryStableId()
        {
            Type inventoryType = RuntimeTypeResolver.GetType(
                "InventoryState");
            Type specType = RuntimeTypeResolver.GetType(
                "InventoryItemSpec");
            object inventory = Activator.CreateInstance(inventoryType, 6);
            object medkit = Activator.CreateInstance(
                specType, "medical_kit", 5);
            object armor = Activator.CreateInstance(
                specType, "armor_pack", 5);
            inventoryType.GetMethod("TryAdd").Invoke(
                inventory, new[] { medkit, (object)5 });
            inventoryType.GetMethod("TryAdd").Invoke(
                inventory, new[] { medkit, (object)4 });
            inventoryType.GetMethod("TryAdd").Invoke(
                inventory, new[] { armor, (object)2 });
            int changes = 0;
            Action changed = () => changes++;
            inventoryType.GetEvent("Changed").AddEventHandler(
                inventory, changed);

            object split = inventoryType.GetMethod("Split").Invoke(
                inventory, new object[] { 0, 3, 2 });
            AssertOperation(split, true, "Split", 2);
            object merge = inventoryType.GetMethod("Merge").Invoke(
                inventory, new object[] { 3, 1 });
            AssertOperation(merge, true, "Merge", 1);
            object move = inventoryType.GetMethod("Move").Invoke(
                inventory, new object[] { 3, 4 });
            AssertOperation(move, true, "Move", 1);
            object swap = inventoryType.GetMethod("Swap").Invoke(
                inventory, new object[] { 2, 4 });
            AssertOperation(swap, true, "Swap", 0);

            Assert.That(ReadQuantity(inventoryType, inventory, "medical_kit"),
                Is.EqualTo(9));
            Assert.That(ReadQuantity(inventoryType, inventory, "armor_pack"),
                Is.EqualTo(2));
            Assert.That(ReadSlotId(inventoryType, inventory, 2),
                Is.EqualTo("medical_kit"));
            Assert.That(ReadSlotId(inventoryType, inventory, 4),
                Is.EqualTo("armor_pack"));
            Assert.That(changes, Is.EqualTo(4),
                "Every successful inventory transaction must notify once.");
            inventoryType.GetEvent("Changed").RemoveEventHandler(
                inventory, changed);
        }

        [Test]
        public void InvalidInventoryOperationsAreAtomicAndSilent()
        {
            Type inventoryType = RuntimeTypeResolver.GetType(
                "InventoryState");
            Type specType = RuntimeTypeResolver.GetType(
                "InventoryItemSpec");
            object inventory = Activator.CreateInstance(inventoryType, 3);
            object medkit = Activator.CreateInstance(
                specType, "medical_kit", 5);
            object armor = Activator.CreateInstance(
                specType, "armor_pack", 5);
            inventoryType.GetMethod("TryAdd").Invoke(
                inventory, new[] { medkit, (object)5 });
            inventoryType.GetMethod("TryAdd").Invoke(
                inventory, new[] { armor, (object)1 });
            int changes = 0;
            Action changed = () => changes++;
            inventoryType.GetEvent("Changed").AddEventHandler(
                inventory, changed);

            AssertOperation(inventoryType.GetMethod("Move").Invoke(
                inventory, new object[] { 0, 1 }), false, "None", 0);
            AssertOperation(inventoryType.GetMethod("Merge").Invoke(
                inventory, new object[] { 0, 1 }), false, "None", 0);
            AssertOperation(inventoryType.GetMethod("Split").Invoke(
                inventory, new object[] { 0, 2, 5 }), false, "None", 0);
            AssertOperation(inventoryType.GetMethod("Split").Invoke(
                inventory, new object[] { 0, 0, 2 }), false, "None", 0);
            Assert.That(ReadQuantity(inventoryType, inventory, "medical_kit"),
                Is.EqualTo(5));
            Assert.That(ReadQuantity(inventoryType, inventory, "armor_pack"),
                Is.EqualTo(1));
            Assert.That(changes, Is.Zero);
            inventoryType.GetEvent("Changed").RemoveEventHandler(
                inventory, changed);
        }

        [Test]
        public void CompactFillsGapsInStableOrderAndPublishesOnce()
        {
            Type inventoryType = RuntimeTypeResolver.GetType(
                "InventoryState");
            Type specType = RuntimeTypeResolver.GetType(
                "InventoryItemSpec");
            object inventory = Activator.CreateInstance(inventoryType, 6);
            object medkit = Activator.CreateInstance(
                specType, "medical_kit", 5);
            object armor = Activator.CreateInstance(
                specType, "armor_pack", 5);
            inventoryType.GetMethod("TryAdd").Invoke(
                inventory, new[] { medkit, (object)2 });
            inventoryType.GetMethod("TryAdd").Invoke(
                inventory, new[] { armor, (object)1 });
            inventoryType.GetMethod("Move").Invoke(
                inventory, new object[] { 0, 4 });
            inventoryType.GetMethod("Move").Invoke(
                inventory, new object[] { 1, 5 });
            int changes = 0;
            Action changed = () => changes++;
            inventoryType.GetEvent("Changed").AddEventHandler(
                inventory, changed);

            object compact = inventoryType.GetMethod("Compact").Invoke(
                inventory, null);

            AssertOperation(compact, true, "Compact", 2);
            Assert.That(ReadSlotId(inventoryType, inventory, 0),
                Is.EqualTo("medical_kit"));
            Assert.That(ReadSlotId(inventoryType, inventory, 1),
                Is.EqualTo("armor_pack"));
            Assert.That(ReadSlotId(inventoryType, inventory, 4), Is.Null);
            Assert.That(ReadSlotId(inventoryType, inventory, 5), Is.Null);
            Assert.That(ReadQuantity(inventoryType, inventory, "medical_kit"),
                Is.EqualTo(2));
            Assert.That(ReadQuantity(inventoryType, inventory, "armor_pack"),
                Is.EqualTo(1));
            Assert.That(changes, Is.EqualTo(1));

            object noChange = inventoryType.GetMethod("Compact").Invoke(
                inventory, null);
            AssertOperation(noChange, false, "None", 0);
            Assert.That(changes, Is.EqualTo(1),
                "Repeated compact must not publish a no-op change.");
            inventoryType.GetEvent("Changed").RemoveEventHandler(
                inventory, changed);
        }

        [Test]
        public void DragTransferMovesMergesOrSwapsByDestinationContent()
        {
            Type definitionType = RuntimeTypeResolver.GetType(
                "ItemDefinition");
            Type controllerType = RuntimeTypeResolver.GetType(
                "PlayerInventoryController");
            Type inventoryType = RuntimeTypeResolver.GetType(
                "InventoryState");
            ScriptableObject medkit = CreateItem(definitionType);
            ScriptableObject armor = CreateItem(
                definitionType,
                "armor_pack",
                "护甲包");
            GameObject player = new GameObject("Drag Transfer Player");

            try
            {
                Component controller = player.AddComponent(controllerType);
                Assert.That(controllerType.GetMethod("TryAdd").Invoke(
                    controller, new object[] { medkit, 6 }), Is.EqualTo(true));
                Assert.That(controllerType.GetMethod("TryAdd").Invoke(
                    controller, new object[] { armor, 1 }), Is.EqualTo(true));
                object inventory = controllerType.GetProperty("Inventory")
                    .GetValue(controller);

                AssertOperation(controllerType.GetMethod("DragTransfer").Invoke(
                    controller, new object[] { 0, 3 }), true, "Move", 5);
                AssertOperation(controllerType.GetMethod("DragTransfer").Invoke(
                    controller, new object[] { 3, 1 }), true, "Merge", 4);
                string armorBefore = ReadSlotId(inventoryType, inventory, 2);
                string medkitBefore = ReadSlotId(inventoryType, inventory, 1);
                AssertOperation(controllerType.GetMethod("DragTransfer").Invoke(
                    controller, new object[] { 2, 1 }), true, "Swap", 0);
                Assert.That(ReadSlotId(inventoryType, inventory, 2),
                    Is.EqualTo(medkitBefore));
                Assert.That(ReadSlotId(inventoryType, inventory, 1),
                    Is.EqualTo(armorBefore));
                Assert.That(ReadQuantity(
                    inventoryType, inventory, "medical_kit"), Is.EqualTo(6));
                Assert.That(ReadQuantity(
                    inventoryType, inventory, "armor_pack"), Is.EqualTo(1));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(player);
                UnityEngine.Object.DestroyImmediate(medkit);
                UnityEngine.Object.DestroyImmediate(armor);
            }
        }

        [Test]
        public void QuickSlotsBindStableIdsAndSurviveInventoryReordering()
        {
            Type quickType = RuntimeTypeResolver.GetType(
                "QuickSlotState");
            object quick = Activator.CreateInstance(quickType, 2);
            int changes = 0;
            Action changed = () => changes++;
            quickType.GetEvent("Changed").AddEventHandler(quick, changed);

            Assert.That(quickType.GetMethod("Bind").Invoke(
                quick, new object[] { 0, "medical_kit" }), Is.EqualTo(true));
            Assert.That(quickType.GetMethod("Bind").Invoke(
                quick, new object[] { 1, "armor_pack" }), Is.EqualTo(true));
            Assert.That(quickType.GetMethod("GetBoundId").Invoke(
                quick, new object[] { 0 }), Is.EqualTo("medical_kit"));
            Assert.That(quickType.GetMethod("GetBoundId").Invoke(
                quick, new object[] { 1 }), Is.EqualTo("armor_pack"));
            Assert.That(quickType.GetMethod("Bind").Invoke(
                quick, new object[] { 1, "armor_pack" }), Is.EqualTo(false));
            Assert.That(changes, Is.EqualTo(2));
            quickType.GetEvent("Changed").RemoveEventHandler(quick, changed);
        }

        [Test]
        public void ConsumeTransactionRejectsReentrantInventoryMutation()
        {
            Type inventoryType = RuntimeTypeResolver.GetType(
                "InventoryState");
            Type specType = RuntimeTypeResolver.GetType(
                "InventoryItemSpec");
            object inventory = Activator.CreateInstance(inventoryType, 3);
            object medkit = Activator.CreateInstance(
                specType, "medical_kit", 5);
            inventoryType.GetMethod("TryAdd").Invoke(
                inventory, new[] { medkit, (object)2 });
            int changes = 0;
            Action changed = () => changes++;
            inventoryType.GetEvent("Changed").AddEventHandler(
                inventory, changed);
            bool reentrantMoveSucceeded = true;
            Func<bool> effect = () =>
            {
                object move = inventoryType.GetMethod("Move").Invoke(
                    inventory, new object[] { 0, 1 });
                reentrantMoveSucceeded = (bool)move.GetType()
                    .GetProperty("Succeeded").GetValue(move);
                return true;
            };

            Assert.That(inventoryType.GetMethod("TryConsumeAt").Invoke(
                inventory, new object[] { 0, 1, effect }), Is.EqualTo(true));
            Assert.That(reentrantMoveSucceeded, Is.False);
            Assert.That(ReadQuantity(
                inventoryType, inventory, "medical_kit"), Is.EqualTo(1));
            Assert.That(ReadSlotId(inventoryType, inventory, 0),
                Is.EqualTo("medical_kit"));
            Assert.That(changes, Is.EqualTo(1));
            inventoryType.GetEvent("Changed").RemoveEventHandler(
                inventory, changed);
        }

        [UnityTest]
        public IEnumerator DroppingAndPickingUpAgainFormsAQuantityClosedLoop()
        {
            LogAssert.ignoreFailingMessages = true;
            yield return LoadCityNew();
            Type controllerType = RuntimeTypeResolver.GetType(
                "PlayerInventoryController");
            Type bootstrapType = RuntimeTypeResolver.GetType(
                "CityNewInventoryBootstrap");
            Type factoryType = RuntimeTypeResolver.GetType(
                "WorldItemFactory");
            Type inventoryType = RuntimeTypeResolver.GetType(
                "InventoryState");
            Type pickupType = RuntimeTypeResolver.GetType(
                "WorldItemPickup");
            Component controller = (Component)UnityEngine.Object
                .FindAnyObjectByType(controllerType);
            Component bootstrap = controller.GetComponent(bootstrapType);
            Array definitions = (Array)bootstrapType
                .GetProperty("Definitions").GetValue(bootstrap);
            object medkit = FindDefinition(definitions, "medical_kit");
            Assert.That(controllerType.GetMethod("TryAdd").Invoke(
                controller, new[] { medkit, (object)3 }), Is.EqualTo(true));
            object state = controllerType.GetProperty("Inventory")
                .GetValue(controller);

            Assert.That(controllerType.GetMethod("TryDrop").Invoke(
                controller, new object[] { 0, 2 }), Is.EqualTo(true));
            Component factory = controller.GetComponent(factoryType);
            Component pickup = (Component)factoryType
                .GetProperty("LastSpawnedPickup").GetValue(factory);
            Assert.That(pickup, Is.Not.Null);
            Assert.That(pickup.gameObject.activeSelf, Is.True);
            Assert.That(Vector3.Distance(
                controller.transform.position,
                pickup.transform.position), Is.LessThan(3.2f));
            Collider collider = pickup.GetComponent<Collider>();
            Assert.That(collider, Is.Not.Null);
            Assert.That(collider.isTrigger, Is.False);
            Assert.That(pickupType.GetProperty("RemainingQuantity")
                .GetValue(pickup), Is.EqualTo(2));
            Assert.That(ReadQuantity(inventoryType, state, "medical_kit"),
                Is.EqualTo(1));

            Assert.That(pickupType.GetMethod("TryBegin").Invoke(
                pickup, new object[] { controller.gameObject }), Is.EqualTo(true));
            pickupType.GetMethod("Advance").Invoke(
                pickup, new object[] { controller.gameObject, 0.1f });
            Assert.That(ReadQuantity(inventoryType, state, "medical_kit"),
                Is.EqualTo(3));
            Assert.That(pickupType.GetProperty("IsClaimed")
                .GetValue(pickup), Is.EqualTo(true));
        }

        [UnityTest]
        public IEnumerator FailedDropDoesNotRemoveInventoryOrCreateWorldItem()
        {
            Type definitionType = RuntimeTypeResolver.GetType(
                "ItemDefinition");
            Type controllerType = RuntimeTypeResolver.GetType(
                "PlayerInventoryController");
            Type factoryType = RuntimeTypeResolver.GetType(
                "WorldItemFactory");
            Type inventoryType = RuntimeTypeResolver.GetType(
                "InventoryState");
            ScriptableObject medkit = CreateItem(definitionType);
            GameObject player = new GameObject("No Ground Player");
            player.transform.position = new Vector3(10000f, 500f, 10000f);

            try
            {
                Component controller = player.AddComponent(controllerType);
                Assert.That(controllerType.GetMethod("TryAdd").Invoke(
                    controller, new object[] { medkit, 2 }), Is.EqualTo(true));
                object state = controllerType.GetProperty("Inventory")
                    .GetValue(controller);
                Assert.That(controllerType.GetMethod("TryDrop").Invoke(
                    controller, new object[] { 0, 1 }), Is.EqualTo(false));
                Assert.That(ReadQuantity(inventoryType, state, "medical_kit"),
                    Is.EqualTo(2));
                Component factory = player.GetComponent(factoryType);
                Assert.That(factoryType.GetProperty("LastSpawnedPickup")
                    .GetValue(factory), Is.Null);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(player);
                UnityEngine.Object.DestroyImmediate(medkit);
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator QuickUseAndInventoryUseShareCooldownAndValidation()
        {
            LogAssert.ignoreFailingMessages = true;
            yield return LoadCityNew();
            Type controllerType = RuntimeTypeResolver.GetType(
                "PlayerInventoryController");
            Type bootstrapType = RuntimeTypeResolver.GetType(
                "CityNewInventoryBootstrap");
            Type inventoryType = RuntimeTypeResolver.GetType(
                "InventoryState");
            Type healthType = RuntimeTypeResolver.GetType("Health");
            Type damageType = RuntimeTypeResolver.GetType("DamageInfo");
            Type quickHudType = RuntimeTypeResolver.GetType(
                "ConsumableQuickSlotHud");
            Component controller = (Component)UnityEngine.Object
                .FindAnyObjectByType(controllerType);
            Component bootstrap = controller.GetComponent(bootstrapType);
            Array definitions = (Array)bootstrapType
                .GetProperty("Definitions").GetValue(bootstrap);
            object medkit = FindDefinition(definitions, "medical_kit");
            controllerType.GetMethod("ConfigureUseCooldown").Invoke(
                controller, new object[] { 0.25f });
            Assert.That(controllerType.GetMethod("TryAdd").Invoke(
                controller, new[] { medkit, (object)2 }), Is.EqualTo(true));
            Assert.That(controllerType.GetMethod("BindQuickSlot").Invoke(
                controller, new object[] { 0, "medical_kit" }), Is.EqualTo(true));
            Component health = controller.GetComponent(healthType);
            object damage = Activator.CreateInstance(
                damageType, 150f, Vector3.zero, Vector3.forward, null);
            healthType.GetMethod("ApplyDamage").Invoke(health, new[] { damage });
            object state = controllerType.GetProperty("Inventory")
                .GetValue(controller);
            Component quickHud = (Component)UnityEngine.Object
                .FindAnyObjectByType(quickHudType);

            Assert.That(controllerType.GetMethod("TryUseQuickSlot").Invoke(
                controller, new object[] { 0 }), Is.EqualTo(true));
            Assert.That(quickHudType.GetMethod("IsUsable").Invoke(
                quickHud, new object[] { 0 }), Is.EqualTo(false),
                "Quick HUD must immediately show the active item cooldown.");
            Assert.That(controllerType.GetMethod("TryUse").Invoke(
                controller, new object[] { 0 }), Is.EqualTo(false),
                "Inventory use must not bypass a quick-use cooldown.");
            Assert.That(ReadQuantity(inventoryType, state, "medical_kit"),
                Is.EqualTo(1));
            yield return new WaitForSecondsRealtime(0.28f);
            yield return null;
            Assert.That(quickHudType.GetMethod("IsUsable").Invoke(
                quickHud, new object[] { 0 }), Is.EqualTo(true));
            Assert.That(controllerType.GetMethod("TryUse").Invoke(
                controller, new object[] { 0 }), Is.EqualTo(true));
            Assert.That(ReadQuantity(inventoryType, state, "medical_kit"),
                Is.Zero);
        }

        [UnityTest]
        public IEnumerator HudInputAndNestedModalPriorityRemainCoherent()
        {
            LogAssert.ignoreFailingMessages = true;
            yield return LoadCityNew();
            Type controllerType = RuntimeTypeResolver.GetType(
                "PlayerInventoryController");
            Type quickHudType = RuntimeTypeResolver.GetType(
                "ConsumableQuickSlotHud");
            Type viewType = RuntimeTypeResolver.GetType("InventoryView");
            Type locksType = RuntimeTypeResolver.GetType(
                "GameplayLockCoordinator");
            Type reasonType = RuntimeTypeResolver.GetType(
                "GameplayLockReason");
            Type inputModuleType = RuntimeTypeResolver.GetType(
                "UnityEngine.InputSystem.UI.InputSystemUIInputModule, " +
                "Unity.InputSystem");
            Component controller = (Component)UnityEngine.Object
                .FindAnyObjectByType(controllerType);

            for (int frame = 0; frame < 3; frame++)
            {
                yield return null;
            }

            Component quickHud = (Component)UnityEngine.Object
                .FindAnyObjectByType(quickHudType);
            Assert.That(quickHud, Is.Not.Null);
            Assert.That(quickHudType.GetProperty("QuickSlotCount")
                .GetValue(quickHud), Is.EqualTo(2));
            Assert.That(quickHudType.GetMethod("GetBoundId").Invoke(
                quickHud, new object[] { 0 }), Is.EqualTo("medical_kit"));
            Assert.That(quickHudType.GetMethod("GetBoundId").Invoke(
                quickHud, new object[] { 1 }), Is.EqualTo("armor_pack"));
            Component inputModule = (Component)UnityEngine.Object
                .FindAnyObjectByType(inputModuleType);
            object moveReference = inputModuleType.GetProperty("move")
                .GetValue(inputModule);
            object moveAction = moveReference.GetType().GetProperty("action")
                .GetValue(moveReference);
            object moveActionMap = moveAction.GetType()
                .GetProperty("actionMap").GetValue(moveAction);
            Assert.That(moveActionMap.GetType().GetProperty("name")
                .GetValue(moveActionMap), Is.EqualTo("UI"));

            Assert.That(controllerType.GetMethod("Open").Invoke(
                controller, null), Is.EqualTo(true));
            Component view = (Component)UnityEngine.Object
                .FindAnyObjectByType(viewType);
            Component locks = controller.GetComponent(locksType);
            object upgradeLease = locksType.GetMethod("Acquire").Invoke(
                locks, new[] { Enum.Parse(reasonType, "UpgradeChoice") });
            Assert.That(viewType.GetProperty("IsSuspended").GetValue(view),
                Is.EqualTo(true));
            Assert.That(locksType.GetProperty("TopReason").GetValue(locks)
                .ToString(), Is.EqualTo("UpgradeChoice"));
            upgradeLease.GetType().GetMethod("Dispose").Invoke(
                upgradeLease, null);
            Assert.That(viewType.GetProperty("IsSuspended").GetValue(view),
                Is.EqualTo(false));
            Assert.That(Time.timeScale, Is.Zero);

            ExecuteEvents.Execute(
                view.gameObject,
                new BaseEventData(EventSystem.current),
                ExecuteEvents.cancelHandler);
            Assert.That(controllerType.GetProperty("IsOpen").GetValue(controller),
                Is.EqualTo(false));
            Assert.That(Time.timeScale, Is.EqualTo(1f).Within(0.001f));
        }

        private static ScriptableObject CreateItem(
            Type definitionType,
            string stableId = "medical_kit",
            string displayName = "医疗包")
        {
            Type itemType = RuntimeTypeResolver.GetType("ItemType");
            Type effectType = RuntimeTypeResolver.GetType("ItemEffectType");
            ScriptableObject item = ScriptableObject.CreateInstance(
                definitionType);
            definitionType.GetMethod("Configure").Invoke(
                item,
                new object[]
                {
                    stableId,
                    displayName,
                    "恢复生命值。",
                    null,
                    Enum.Parse(itemType, "Consumable"),
                    5,
                    Enum.Parse(effectType, "RestoreHealth"),
                    35f
                });
            return item;
        }

        private static object FindDefinition(Array definitions, string stableId)
        {
            foreach (object definition in definitions)
            {
                if (definition != null &&
                    definition.GetType().GetProperty("StableId")
                        .GetValue(definition)?.ToString() == stableId)
                {
                    return definition;
                }
            }

            Assert.Fail($"Missing definition {stableId}.");
            return null;
        }

        private static int ReadQuantity(
            Type inventoryType,
            object inventory,
            string stableId)
        {
            return (int)inventoryType.GetMethod("GetQuantity").Invoke(
                inventory, new object[] { stableId });
        }

        private static string ReadSlotId(
            Type inventoryType,
            object inventory,
            int index)
        {
            object slot = inventoryType.GetMethod("GetSlot").Invoke(
                inventory, new object[] { index });
            return slot.GetType().GetProperty("StableId")
                .GetValue(slot)?.ToString();
        }

        private static void AssertOperation(
            object result,
            bool succeeded,
            string kind,
            int transferred)
        {
            Type type = result.GetType();
            Assert.That(type.GetProperty("Succeeded").GetValue(result),
                Is.EqualTo(succeeded));
            Assert.That(type.GetProperty("Kind").GetValue(result).ToString(),
                Is.EqualTo(kind));
            Assert.That(type.GetProperty("TransferredQuantity").GetValue(result),
                Is.EqualTo(transferred));
        }

        private static IEnumerator LoadCityNew()
        {
            yield return SceneManager.LoadSceneAsync(
                CityNewScene,
                LoadSceneMode.Single);

            for (int frame = 0; frame < 8; frame++)
            {
                yield return null;
            }
        }
    }
}
