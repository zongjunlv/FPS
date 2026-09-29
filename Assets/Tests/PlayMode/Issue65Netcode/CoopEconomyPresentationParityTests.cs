using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using FPS.Networking.Domain;
using FPS.Networking.Netcode;
using FPS.Networking.Session;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FPS.Tests.PlayMode.Issue65
{
    public sealed class CoopEconomyPresentationParityTests
    {
        private readonly List<GameObject> created = new();

        [Test]
        public void UpgradeClickIsLockedUntilAckAndRejectionCanRetry()
        {
            var gate = new CoopEconomyRequestGate();
            var offer = new NetcodeProgressionState
            {
                PlayerId = 1, InventoryRevision = 1, ChoiceGeneration = 4,
                PendingUpgradeChoices = 2
            };
            var command = new NetcodeEconomyCommand
            {
                Sequence = 9, Kind = AuthoritativeEconomyCommandKind.SelectUpgrade
            };
            Assert.That(gate.Begin(command, offer), Is.True);
            Assert.That(gate.Begin(command, offer), Is.False,
                "同一帧连续点击必须只提交一次。");
            Assert.That(gate.Observe(offer), Is.False);
            Assert.That(gate.IsPending, Is.True,
                "旧快照不能提前关闭升级选择或本地结算。");
            offer.AcknowledgedEconomySequence = 9;
            Assert.That(gate.Observe(offer), Is.True);
            Assert.That(gate.WasAccepted, Is.False,
                "ack仅说明已处理；候选批次不变表示没有成功应用。");
            command.Sequence = 10;
            Assert.That(gate.Begin(command, offer), Is.True);
            offer.AcknowledgedEconomySequence = 10;
            offer.ChoiceGeneration = 5;
            offer.PendingUpgradeChoices = 1;
            Assert.That(gate.Observe(offer), Is.True);
            Assert.That(gate.WasAccepted, Is.True);
        }

        [Test]
        public void InventoryDoesNotConfirmUntilAuthoritativeRevisionChanges()
        {
            var gate = new CoopEconomyRequestGate();
            var state = new NetcodeProgressionState { InventoryRevision = 2 };
            var command = new NetcodeEconomyCommand
            {
                Sequence = 3, Kind = AuthoritativeEconomyCommandKind.Drop
            };
            Assert.That(gate.Begin(command, state), Is.True);
            state.AcknowledgedEconomySequence = 3;
            Assert.That(gate.Observe(state), Is.True);
            Assert.That(gate.WasAccepted, Is.False);
            command.Sequence = 4;
            gate.Begin(command, state);
            state.AcknowledgedEconomySequence = 4;
            state.InventoryRevision = 3;
            Assert.That(gate.Observe(state), Is.True);
            Assert.That(gate.WasAccepted, Is.True);
        }

        [Test]
        public void GrowingPickupListCanVisitEveryItemOneStepPerScroll()
        {
            var selection = new CoopNearbyDropSelection();
            var drops = new List<NetcodeWorldDropState>();
            for (int index = 1; index <= 2; index++) drops.Add(Drop(index));
            selection.Refresh(drops, 1, Vector3.zero);
            selection.Cycle(1);
            Assert.That(selection.Selected.DropId, Is.EqualTo(2));
            for (int index = 3; index <= 5; index++) drops.Add(Drop(index));
            selection.Refresh(drops, 1, Vector3.zero);
            Assert.That(selection.Selected.DropId, Is.EqualTo(2));
            int[] expected = { 3, 4, 5, 1, 2 };
            foreach (int id in expected)
            {
                selection.Cycle(1);
                Assert.That(selection.Selected.DropId, Is.EqualTo(id),
                    "一个已归一化的滚轮事件只能移动一格，且新增物品必须可选。");
            }
            drops[1] = default;
            selection.Refresh(drops, 1, Vector3.zero);
            Assert.That(selection.Selected.DropId, Is.Not.EqualTo(2));
        }

        [Test]
        public void NearbySelectionFiltersOwnershipAvailabilityAndDistance()
        {
            NetcodeWorldDropState shared = Drop(1);
            NetcodeWorldDropState otherPlayer = Drop(2);
            otherPlayer.OwnerPlayerId = 2;
            NetcodeWorldDropState unavailable = Drop(3);
            unavailable.Available = false;
            NetcodeWorldDropState distant = Drop(4);
            distant.Position = new Vector3(4f, 0f, 0f);
            var selection = new CoopNearbyDropSelection();
            selection.Refresh(new[] { shared, otherPlayer, unavailable, distant },
                1, Vector3.zero);
            Assert.That(selection.Nearby.Count, Is.EqualTo(1));
            Assert.That(selection.Selected.DropId, Is.EqualTo(1));
            selection.Cycle(1);
            Assert.That(selection.Selected.DropId, Is.EqualTo(1),
                "单项列表也要消费滚轮事件，不能转交切枪。");
            selection.Refresh(new[] { otherPlayer, unavailable, distant },
                1, Vector3.zero);
            Assert.That(selection.HasSelection, Is.False);
        }

        private static NetcodeWorldDropState Drop(int id) => new()
        {
            DropId = id, ItemId = "medical_kit", Quantity = 1,
            Position = Vector3.zero, Available = true, Revision = 1
        };

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            foreach (GameObject value in created)
                if (value != null) Object.Destroy(value);
            created.Clear();
            yield return null;
            yield return null;
        }

        [UnityTest]
        public IEnumerator ReplicatedDropUsesSolidPackageWithoutLocalSettlement()
        {
            NetworkCoopSessionAuthority authority = CreateAuthority();
            authority.SpawnServerWorldDrop("medical_kit", 2, Vector3.zero);
            GameObject host = Track(new GameObject("Drop presentation parity"));
            var presenter = host.AddComponent<CoopNetworkDropPresenter>();
            SetField(presenter, "authority", authority);
            yield return null;
            // Primitive colliders are disabled immediately and destroyed at the
            // frame boundary; check the settled hierarchy, not deferred objects.
            yield return null;
            Assert.That(presenter.ViewCount, Is.EqualTo(1));
            Assert.That(host.GetComponentsInChildren<MeshRenderer>().Length,
                Is.GreaterThan(0), "联机掉落应复用立体包体，不能只显示二维图标。");
            Assert.That(host.GetComponentsInChildren<SpriteRenderer>().Length,
                Is.Zero);
            Assert.That(host.GetComponentsInChildren<WorldItemPickup>().Length,
                Is.Zero, "表现包体不能执行单机本地库存结算。");
            Assert.That(host.GetComponentsInChildren<Collider>().Length,
                Is.Zero);
        }

        [UnityTest]
        public IEnumerator ReplacingAuthorityDoesNotReusePreviousRunDropVisual()
        {
            NetworkCoopSessionAuthority firstAuthority = CreateAuthority();
            firstAuthority.SpawnServerWorldDrop("medical_kit", 1, Vector3.zero);
            GameObject host = Track(new GameObject("Drop authority replacement"));
            var presenter = host.AddComponent<CoopNetworkDropPresenter>();
            SetField(presenter, "authority", firstAuthority);
            yield return null;
            MeshRenderer firstPackage = host.GetComponentInChildren<MeshRenderer>();
            Assert.That(firstPackage, Is.Not.Null);
            Color healthColor = firstPackage.sharedMaterial.color;
            NetworkCoopSessionAuthority secondAuthority = CreateAuthority();
            secondAuthority.SpawnServerWorldDrop("armor_pack", 1,
                new Vector3(2f, 0f, 0f));
            SetField(presenter, "authority", secondAuthority);
            yield return null;
            yield return null;
            MeshRenderer newPackage = host.GetComponentInChildren<MeshRenderer>();
            Assert.That(presenter.ViewCount, Is.EqualTo(1));
            Assert.That(newPackage, Is.Not.Null);
            Assert.That(newPackage.sharedMaterial.color, Is.Not.EqualTo(healthColor),
                "重连/新局即使复用 DropId，也不能留下上一局的医疗包模型。");
            Assert.That(newPackage.transform.position.x, Is.EqualTo(2f).Within(0.01f));
            Assert.That(host.GetComponentsInChildren<WorldItemPickup>().Length, Is.Zero);
        }

        [UnityTest]
        public IEnumerator ServerUpgradeOfferUsesSinglePlayerCardView()
        {
            NetworkCoopSessionAuthority authority = CreateAuthority();
            authority.GrantServerExperience(1, 100);
            GameObject hudHost = Track(new GameObject("Economy parity HUD",
                typeof(RectTransform), typeof(Canvas), typeof(UnifiedGameHud)));
            GameObject player = Track(new GameObject("Economy parity player"));
            var replica = player.AddComponent<NetworkPlayerReplica>();
            replica.EnableOwnerTestHook(authority, 1);
            authority.TryGetPlayerState(1, out NetcodePlayerState state);
            replica.ConsumeServerState(state, true, state.ServerTick);
            var presenter = Track(new GameObject("Economy parity presenter"))
                .AddComponent<CoopEconomyHudPresenter>();
            SetField(presenter, "authority", authority);
            SetField(presenter, "localPlayer", replica);
            yield return null;
            yield return null;
            UpgradeChoiceView cards = hudHost.GetComponent<UpgradeChoiceView>();
            Assert.That(cards, Is.Not.Null,
                "服务器候选必须使用单机升级卡片，不得保留独立OnGUI按钮。");
            Assert.That(cards.IsVisible, Is.True);
            Assert.That(cards.CardCount, Is.EqualTo(3));
        }

        [UnityTest]
        public IEnumerator SharedCardClickOnlySubmitsIntentAndRejectionCanRetry()
        {
            NetworkCoopSessionAuthority authority = CreateAuthority();
            authority.GrantServerExperience(1, 100);
            authority.TryGetProgression(1, out NetcodeProgressionState offer);
            GameObject hudHost = Track(new GameObject("Card intent HUD",
                typeof(RectTransform), typeof(Canvas), typeof(UnifiedGameHud)));
            var adapter = Track(new GameObject("Card intent projection"))
                .AddComponent<CoopEconomyPresentationAdapter>();
            int requests = 0;
            int candidateIndex = -1;
            adapter.Configure(intent =>
            {
                requests++;
                candidateIndex = intent.CandidateIndex;
                return true;
            }, () => { });
            adapter.Present(new CoopEconomyPresentationFrame { Progression = offer });
            UpgradeChoiceView cards = hudHost.GetComponent<UpgradeChoiceView>();
            Transform firstCard = hudHost.GetComponent<UnifiedGameHud>()
                .ModalLayer.Find("UpgradeChoicePanel/UpgradeCards/UpgradeCard1");
            Assert.That(cards.IsVisible, Is.True);
            Invoke(cards, "Commit", 0);
            Invoke(cards, "Commit", 0);
            Assert.That(requests, Is.EqualTo(1));
            Assert.That(candidateIndex, Is.Zero);
            Assert.That(cards.IsVisible, Is.True,
                "提交意图后不能在本地提前关闭卡片或应用强化。");
            authority.TryGetProgression(1, out NetcodeProgressionState unchanged);
            Assert.That(unchanged.ChoiceGeneration, Is.EqualTo(offer.ChoiceGeneration));
            Assert.That(unchanged.PendingUpgradeChoices, Is.EqualTo(offer.PendingUpgradeChoices));

            adapter.Present(new CoopEconomyPresentationFrame
                { Progression = offer, Pending = true });
            Assert.That(hudHost.GetComponent<UnifiedGameHud>().ModalLayer
                .Find("UpgradeChoicePanel/UpgradeCards/UpgradeCard1"), Is.SameAs(firstCard),
                "相同服务器候选不能逐帧重建并解除提交锁。");
            Invoke(cards, "Commit", 1);
            Assert.That(requests, Is.EqualTo(1));
            adapter.Present(new CoopEconomyPresentationFrame
                { Progression = offer, RetryAfterRejection = true });
            Invoke(cards, "Commit", 1);
            Assert.That(requests, Is.EqualTo(2), "明确拒绝后允许重新选择。");
            Assert.That(candidateIndex, Is.EqualTo(1));
            yield return null;
        }

        [UnityTest]
        public IEnumerator InventoryUsesSnapshotUntilServerConfirmsItemConsumption()
        {
            GameObject hudHost = Track(new GameObject("Inventory projection HUD",
                typeof(RectTransform), typeof(Canvas), typeof(UnifiedGameHud)));
            var adapter = Track(new GameObject("Inventory projection adapter"))
                .AddComponent<CoopEconomyPresentationAdapter>();
            int requests = 0;
            adapter.Configure(intent =>
            {
                Assert.That(intent.Kind, Is.EqualTo(AuthoritativeEconomyCommandKind.Use));
                Assert.That(intent.SourceSlot, Is.Zero);
                requests++;
                return true;
            }, () => { });
            var slots = new NetcodeInventorySlotState[12];
            for (int index = 0; index < slots.Length; index++)
                slots[index] = new NetcodeInventorySlotState { PlayerId = 1, SlotIndex = index };
            slots[0] = new NetcodeInventorySlotState
            {
                PlayerId = 1, SlotIndex = 0, ItemId = "medical_kit",
                Quantity = 2, MaximumStack = 5
            };
            var frame = new CoopEconomyPresentationFrame
            {
                Progression = new NetcodeProgressionState
                    { PlayerId = 1, Level = 1, InventoryRevision = 1 },
                Inventory = slots, InventoryVisible = true
            };
            adapter.Present(frame);
            InventoryView view = hudHost.GetComponent<InventoryView>();
            Assert.That(view.SlotCount, Is.EqualTo(12));
            view.SelectSlot(0);
            Assert.That(view.DetailQuantity, Does.Contain("2"));
            Invoke(view, "UseSelected");
            Invoke(view, "UseSelected");
            Assert.That(requests, Is.EqualTo(1));
            Assert.That(view.DetailQuantity, Does.Contain("2"),
                "客户端按钮只能提交意图，不能提前消耗本地投影。");
            Assert.That(view.OperationMessage, Does.Contain("服务器确认"));
            slots[0].Quantity = 1;
            frame.Progression.InventoryRevision = 2;
            frame.Pending = false;
            frame.Status = "服务器已确认";
            adapter.Present(frame);
            Assert.That(view.DetailQuantity, Does.Contain("1"));
            Assert.That(requests, Is.EqualTo(1));
            yield return null;
        }

        [UnityTest]
        public IEnumerator ReplicatedPickupListShowsNamesAndOwnsItsCleanup()
        {
            GameObject hudHost = Track(new GameObject("Pickup list parity HUD",
                typeof(RectTransform), typeof(Canvas), typeof(UnifiedGameHud)));
            UnifiedGameHud hud = hudHost.GetComponent<UnifiedGameHud>();
            GameObject owner = Track(new GameObject("Pickup list owner"));
            var list = owner.AddComponent<WorldPickupListHud>();
            list.Initialize(hud.HudLayer);
            var entries = new List<WorldPickupListEntry>();
            for (int index = 1; index <= 8; index++)
                entries.Add(new WorldPickupListEntry($"医疗包{index}", index));
            list.RefreshEntries(entries, 7);
            Assert.That(list.HasOuterPanel, Is.False);
            Assert.That(list.HasHeaderOrFooter, Is.False);
            Assert.That(list.SelectedIndex, Is.EqualTo(7));
            Assert.That(list.GetRowText(5), Does.Contain("医疗包8"));
            Assert.That(list.GetRowText(5), Does.Contain("×8"));
            Object.Destroy(owner);
            yield return null;
            yield return null;
            Assert.That(hud.HudLayer.Find("WorldPickupListHud"), Is.Null,
                "断线或重连销毁投影组件后，HUD不能残留叠加列表。");
        }

        private NetworkCoopSessionAuthority CreateAuthority()
        {
            var authority = Track(new GameObject("Economy parity authority"))
                .AddComponent<NetworkCoopSessionAuthority>();
            authority.EnableServerTestHook();
            authority.ConfigureServer(new CoopServerRules(tickRate: 60),
                new[] { new CoopPlayerSpawn(1, default) },
                new[] { new CoopTargetSpawn(1,
                    new NetVector3(0d, 0d, 30d), 0.5d, 100d,
                    moveSpeed: 0d, attackDamage: 0d) });
            return authority;
        }

        private GameObject Track(GameObject value)
        {
            created.Add(value);
            return value;
        }

        private static void SetField(object target, string name, object value)
        {
            target.GetType().GetField(name,
                BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(target, value);
        }

        private static void Invoke(object target, string name, params object[] arguments) =>
            target.GetType().GetMethod(name,
                BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, arguments);
    }
}
