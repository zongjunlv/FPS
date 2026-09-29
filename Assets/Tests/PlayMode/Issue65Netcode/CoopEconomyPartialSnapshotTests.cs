using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using FPS.Networking.Domain;
using FPS.Networking.Netcode;
using FPS.Networking.Session;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.TestTools;

namespace FPS.Tests.PlayMode.Issue65
{
    public sealed class CoopEconomyPartialSnapshotTests
    {
        private readonly List<GameObject> created = new();
        private CursorLockMode previousCursorLock;
        private bool previousCursorVisible;
        private bool previousEconomyGate;
        private bool previousPauseGate;

        [SetUp]
        public void SetUp()
        {
            previousCursorLock = Cursor.lockState;
            previousCursorVisible = Cursor.visible;
            previousEconomyGate = CoopUiInputGate.EconomyModalVisible;
            previousPauseGate = CoopUiInputGate.PauseMenuVisible;
            CoopUiInputGate.EconomyModalVisible = false;
            CoopUiInputGate.PauseMenuVisible = false;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            CoopEconomyPresentationRegistration.Register();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            for (int index = created.Count - 1; index >= 0; index--)
                if (created[index] != null) Object.Destroy(created[index]);
            created.Clear();
            yield return null;
            yield return null;
            CoopUiInputGate.EconomyModalVisible = previousEconomyGate;
            CoopUiInputGate.PauseMenuVisible = previousPauseGate;
            CoopEconomyPresentationRegistry.PickupScrollOwned = false;
            Cursor.lockState = previousCursorLock;
            Cursor.visible = previousCursorVisible;
        }

        [UnityTest]
        public IEnumerator PartialSnapshotPreservesOpenIntentAndRestoresInventory()
        {
            Fixture fixture = CreateFixture();
            yield return OpenInventory(fixture);
            NetcodeWorldState complete = fixture.Authority.WorldState;
            MakeSnapshotIncomplete(fixture.Authority);
            yield return null;
            Assert.That(fixture.Authority.IsReplicatedSnapshotComplete, Is.False,
                "前置条件：真实 presenter Update 必须经过不完整快照分支。");

            SetWorldState(fixture.Authority, complete);
            yield return null;
            yield return null;
            Assert.That(fixture.Authority.IsReplicatedSnapshotComplete, Is.True);
            Assert.That(fixture.Presenter.InventoryVisible, Is.True,
                "同 authority/run 的暂时不完整帧不能清除玩家打开背包的意图。");
            Assert.That(fixture.Hud.GetComponent<InventoryView>().IsVisible, Is.True,
                "完整快照恢复后，原背包应恢复，不应要求玩家重新按 TAB。");
        }

        [UnityTest]
        public IEnumerator PartialSnapshotBlocksCommandsButRetainsEconomyCursorGate()
        {
            Fixture fixture = CreateFixture();
            yield return OpenInventory(fixture);
            MakeSnapshotIncomplete(fixture.Authority);
            yield return null;
            Assert.That(fixture.Authority.IsReplicatedSnapshotComplete, Is.False);
            bool submitted = (bool)Invoke(fixture.Presenter, "SubmitIntent",
                new CoopEconomyIntent(AuthoritativeEconomyCommandKind.Use, sourceSlot: 0));
            Assert.That(submitted, Is.False,
                "挂起期间必须拒绝提交，不能按旧格位向服务器发送经济操作。");
            Assert.That(fixture.Presenter.RequestPending, Is.False);
            Assert.That(CoopUiInputGate.EconomyModalVisible, Is.True,
                "暂时隐藏/挂起背包不能把输入重新交给移动和射击。");
            Assert.That(CoopUiInputGate.GameplayInputSuppressed, Is.True);
            Assert.That(Cursor.lockState, Is.EqualTo(CursorLockMode.None));
            Assert.That(Cursor.visible, Is.True);
        }

        [UnityTest]
        public IEnumerator NewRunAfterPartialSnapshotClosesPreviousInventory()
        {
            Fixture fixture = CreateFixture();
            yield return OpenInventory(fixture);
            NetcodeWorldState nextRun = fixture.Authority.WorldState;
            MakeSnapshotIncomplete(fixture.Authority);
            yield return null;
            nextRun.RunGeneration++;
            SetWorldState(fixture.Authority, nextRun);
            yield return null;
            yield return null;
            Assert.That(fixture.Authority.IsReplicatedSnapshotComplete, Is.True);
            Assert.That(fixture.Presenter.InventoryVisible, Is.False,
                "新战局不能继承旧战局的背包打开意图。");
            Assert.That(fixture.Hud.GetComponent<InventoryView>().IsVisible, Is.False);
            Assert.That(CoopUiInputGate.EconomyModalVisible, Is.False);
        }

        [UnityTest]
        public IEnumerator NewAuthorityAfterPartialSnapshotClosesPreviousInventory()
        {
            Fixture fixture = CreateFixture();
            yield return OpenInventory(fixture);
            MakeSnapshotIncomplete(fixture.Authority);
            yield return null;
            NetworkCoopSessionAuthority replacement = CreateAuthority();
            NetworkPlayerReplica replacementPlayer = CreateReplica(replacement);
            SetField(fixture.Presenter, "authority", replacement);
            SetField(fixture.Presenter, "localPlayer", replacementPlayer);
            yield return null;
            yield return null;
            Assert.That(replacement.IsReplicatedSnapshotComplete, Is.True);
            Assert.That(fixture.Presenter.InventoryVisible, Is.False,
                "重连到另一 authority，即使 PlayerId/RunGeneration 相同，也必须清理旧界面意图。");
            Assert.That(fixture.Hud.GetComponent<InventoryView>().IsVisible, Is.False);
            Assert.That(CoopUiInputGate.EconomyModalVisible, Is.False);
        }

        [UnityTest]
        public IEnumerator ExitingPresenterDuringPartialSnapshotClearsInventoryIntent()
        {
            Fixture fixture = CreateFixture();
            yield return OpenInventory(fixture);
            MakeSnapshotIncomplete(fixture.Authority);
            yield return null;
            fixture.Presenter.enabled = false;
            Assert.That(fixture.Presenter.InventoryVisible, Is.False,
                "退出组件是终止界面意图，不是暂时等待快照。");
            Assert.That(fixture.Hud.GetComponent<InventoryView>().IsVisible, Is.False);
            Assert.That(CoopUiInputGate.EconomyModalVisible, Is.False);
        }

        private Fixture CreateFixture()
        {
            NetworkCoopSessionAuthority authority = CreateAuthority();
            var hud = Track(new GameObject("Partial snapshot shared HUD",
                typeof(RectTransform), typeof(Canvas), typeof(UnifiedGameHud)))
                .GetComponent<UnifiedGameHud>();
            NetworkPlayerReplica replica = CreateReplica(authority);
            var presenter = Track(new GameObject("Partial snapshot economy presenter"))
                .AddComponent<CoopEconomyHudPresenter>();
            SetField(presenter, "authority", authority);
            SetField(presenter, "localPlayer", replica);
            return new Fixture(authority, hud, presenter);
        }

        private static IEnumerator OpenInventory(Fixture fixture)
        {
            // Establish this authority/run in the actual Update, then emulate
            // only the already-normalized UI open intent. Do not invoke Update
            // manually or bypass its completeness/context checks.
            yield return null;
            SetField(fixture.Presenter, "inventoryVisible", true);
            yield return null;
            yield return null;
            Assert.That(fixture.Authority.IsReplicatedSnapshotComplete, Is.True);
            Assert.That(fixture.Presenter.InventoryVisible, Is.True);
            Assert.That(fixture.Hud.GetComponent<InventoryView>().IsVisible, Is.True);
            Assert.That(CoopUiInputGate.EconomyModalVisible, Is.True);
        }

        private NetworkCoopSessionAuthority CreateAuthority()
        {
            var authority = Track(new GameObject("Partial snapshot authority"))
                .AddComponent<NetworkCoopSessionAuthority>();
            authority.EnableServerTestHook();
            authority.ConfigureServer(new CoopServerRules(tickRate: 60),
                new[] { new CoopPlayerSpawn(1, default) },
                new[] { new CoopTargetSpawn(1,
                    new NetVector3(0d, 0d, 30d), 0.5d, 100d,
                    moveSpeed: 0d, attackDamage: 0d) });
            authority.AutoSimulate = false;
            return authority;
        }

        private NetworkPlayerReplica CreateReplica(NetworkCoopSessionAuthority authority)
        {
            var replica = Track(new GameObject("Partial snapshot local replica"))
                .AddComponent<NetworkPlayerReplica>();
            replica.EnableOwnerTestHook(authority, 1);
            Assert.That(authority.TryGetPlayerState(1, out NetcodePlayerState state), Is.True);
            replica.ConsumeServerState(state, true, state.ServerTick);
            return replica;
        }

        private static void MakeSnapshotIncomplete(NetworkCoopSessionAuthority authority)
        {
            NetcodeWorldState incomplete = authority.WorldState;
            // Model NGO applying the committed count before its list update.
            // This changes real replicated data, not a fake presenter flag.
            incomplete.SnapshotInventoryCount++;
            SetWorldState(authority, incomplete);
            Assert.That(authority.IsReplicatedSnapshotComplete, Is.False);
        }

        private static void SetWorldState(NetworkCoopSessionAuthority authority,
            NetcodeWorldState value)
        {
            var variable = (NetworkVariable<NetcodeWorldState>)typeof(NetworkCoopSessionAuthority)
                .GetField("worldState", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(authority);
            variable.Value = value;
        }

        private GameObject Track(GameObject value)
        {
            created.Add(value);
            return value;
        }

        private static void SetField(object target, string name, object value) =>
            target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(target, value);

        private static object Invoke(object target, string name, params object[] arguments) =>
            target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(target, arguments);

        private sealed class Fixture
        {
            public Fixture(NetworkCoopSessionAuthority authority, UnifiedGameHud hud,
                CoopEconomyHudPresenter presenter)
            {
                Authority = authority;
                Hud = hud;
                Presenter = presenter;
            }

            public NetworkCoopSessionAuthority Authority { get; }
            public UnifiedGameHud Hud { get; }
            public CoopEconomyHudPresenter Presenter { get; }
        }
    }
}
