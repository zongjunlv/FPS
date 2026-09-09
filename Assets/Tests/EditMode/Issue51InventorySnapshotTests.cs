using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace FPS.Tests.Architecture
{
    public sealed class Issue51InventorySnapshotTests
    {
        private GameObject player;
        private PlayerInventoryController controller;
        private ItemDefinition medkit;
        private ItemDefinition armorPack;

        [SetUp]
        public void SetUp()
        {
            player = new GameObject("Issue51 Inventory Player");
            controller = player.AddComponent<PlayerInventoryController>();
            controller.ConfigureInventory(4);
            medkit = CreateItem("medical_kit", 5);
            armorPack = CreateItem("armor_pack", 2);
            controller.RegisterItem(medkit);
            controller.RegisterItem(armorPack);
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(player);
            UnityEngine.Object.DestroyImmediate(medkit);
            UnityEngine.Object.DestroyImmediate(armorPack);
        }

        [Test]
        public void CaptureAndRestorePreserveEmptySlotOrderAndResolveStackLimits()
        {
            InventoryState originalInventory = controller.Inventory;
            QuickSlotState originalQuickSlots = controller.QuickSlots;
            PlayerInventorySnapshot saved = Snapshot(
                Slot("medical_kit", 3),
                EmptySlot(),
                Slot("armor_pack", 2),
                EmptySlot());
            saved.QuickSlots.Bindings[0] = "medical_kit";
            saved.QuickSlots.Bindings[1] = string.Empty;

            Assert.That(controller.TryRestoreSnapshot(saved), Is.True);
            Assert.That(controller.Inventory, Is.SameAs(originalInventory));
            Assert.That(controller.QuickSlots, Is.SameAs(originalQuickSlots));
            Assert.That(controller.Inventory.GetSlot(0).MaximumStack, Is.EqualTo(5));
            Assert.That(controller.Inventory.GetSlot(1).IsEmpty, Is.True);
            Assert.That(controller.Inventory.GetSlot(2).MaximumStack, Is.EqualTo(2));
            Assert.That(controller.Inventory.GetSlot(3).IsEmpty, Is.True);
            Assert.That(controller.QuickSlots.GetBoundId(0), Is.EqualTo("medical_kit"));
            Assert.That(controller.QuickSlots.GetBoundId(1), Is.Empty);

            PlayerInventorySnapshot captured = controller.CaptureSnapshot();
            Assert.That(captured.Inventory.Slots, Has.Count.EqualTo(4));
            Assert.That(captured.Inventory.Slots[1].StableId, Is.Empty);
            Assert.That(captured.Inventory.Slots[1].Quantity, Is.Zero);
            Assert.That(captured.Inventory.Slots[2].StableId, Is.EqualTo("armor_pack"));
            Assert.That(captured.QuickSlots.Bindings, Is.EqualTo(saved.QuickSlots.Bindings));
            Assert.That(captured.SelectedIndex, Is.EqualTo(-1));
        }

        [Test]
        public void InvalidSnapshotChangesNothingAndPublishesNoEvents()
        {
            Assert.That(controller.TryRestoreSnapshot(Snapshot(
                Slot("medical_kit", 2), EmptySlot(), EmptySlot(), EmptySlot())),
                Is.True);
            controller.BindQuickSlot(0, "medical_kit");
            int inventoryEvents = 0;
            int quickSlotEvents = 0;
            controller.Inventory.Changed += () => inventoryEvents++;
            controller.QuickSlots.Changed += () => quickSlotEvents++;
            PlayerInventorySnapshot invalid = Snapshot(
                Slot("armor_pack", 1), EmptySlot(), EmptySlot(), EmptySlot());
            invalid.QuickSlots.Bindings[0] = "missing_item";

            Assert.That(controller.TryRestoreSnapshot(invalid), Is.False);
            Assert.That(controller.Inventory.GetSlot(0).StableId,
                Is.EqualTo("medical_kit"));
            Assert.That(controller.Inventory.GetSlot(0).Quantity, Is.EqualTo(2));
            Assert.That(controller.QuickSlots.GetBoundId(0),
                Is.EqualTo("medical_kit"));
            Assert.That(inventoryEvents, Is.Zero);
            Assert.That(quickSlotEvents, Is.Zero);
        }

        [Test]
        public void SuccessfulRestorePublishesAtMostOneEventPerStateAfterAtomicCommit()
        {
            int inventoryEvents = 0;
            int quickSlotEvents = 0;
            bool inventoryObserverSawQuickSlots = false;
            bool quickSlotObserverSawInventory = false;
            controller.Inventory.Changed += () =>
            {
                inventoryEvents++;
                inventoryObserverSawQuickSlots =
                    controller.QuickSlots.GetBoundId(1) == "armor_pack";
            };
            controller.QuickSlots.Changed += () =>
            {
                quickSlotEvents++;
                quickSlotObserverSawInventory =
                    controller.Inventory.GetSlot(2).StableId == "armor_pack";
            };
            PlayerInventorySnapshot saved = Snapshot(
                EmptySlot(),
                Slot("medical_kit", 1),
                Slot("armor_pack", 1),
                EmptySlot());
            saved.QuickSlots.Bindings[1] = "armor_pack";
            saved.CooldownRemainingSeconds = 2f;

            Assert.That(controller.TryRestoreSnapshot(saved), Is.True);
            Assert.That(inventoryEvents, Is.EqualTo(1));
            Assert.That(quickSlotEvents, Is.EqualTo(1));
            Assert.That(inventoryObserverSawQuickSlots, Is.True);
            Assert.That(quickSlotObserverSawInventory, Is.True);
            Assert.That(controller.UseCooldownRemaining,
                Is.InRange(1.8f, 2.01f));
            Assert.That(controller.IsUseCoolingDown, Is.True);

            Assert.That(controller.TryRestoreSnapshot(controller.CaptureSnapshot()),
                Is.True);
            Assert.That(inventoryEvents, Is.EqualTo(1));
            Assert.That(quickSlotEvents, Is.EqualTo(1));
        }

        [TestCase("missing_item", 1)]
        [TestCase("medical_kit", 6)]
        [TestCase("", 1)]
        public void UnknownOverstackedOrMalformedSlotsAreRejected(
            string stableId,
            int quantity)
        {
            PlayerInventorySnapshot invalid = Snapshot(
                Slot(stableId, quantity), EmptySlot(), EmptySlot(), EmptySlot());
            Assert.That(controller.TryRestoreSnapshot(invalid), Is.False);
            Assert.That(controller.Inventory.OccupiedSlotCount, Is.Zero);
        }

        private static PlayerInventorySnapshot Snapshot(
            params InventorySlotSnapshot[] slots)
        {
            return new PlayerInventorySnapshot
            {
                Inventory = new InventorySnapshot
                {
                    Slots = new List<InventorySlotSnapshot>(slots)
                },
                QuickSlots = new QuickSlotSnapshot
                {
                    Bindings = new List<string> { string.Empty, string.Empty }
                },
                SelectedIndex = -1
            };
        }

        private static InventorySlotSnapshot Slot(string stableId, int quantity)
        {
            return new InventorySlotSnapshot
            {
                StableId = stableId,
                Quantity = quantity
            };
        }

        private static InventorySlotSnapshot EmptySlot() => Slot(string.Empty, 0);

        private static ItemDefinition CreateItem(string stableId, int maximumStack)
        {
            ItemDefinition item = ScriptableObject.CreateInstance<ItemDefinition>();
            item.Configure(
                stableId,
                stableId,
                string.Empty,
                null,
                ItemType.Consumable,
                maximumStack,
                ItemEffectType.RestoreHealth,
                1f);
            return item;
        }
    }
}
