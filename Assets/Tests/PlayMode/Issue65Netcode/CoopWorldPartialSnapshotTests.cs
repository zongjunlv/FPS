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
    public sealed class CoopWorldPartialSnapshotTests
    {
        private const string EnemyAddress = "enemy/trilobite-assault";
        private readonly List<GameObject> created = new();

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            for (int index = created.Count - 1; index >= 0; index--)
                if (created[index] != null) Object.Destroy(created[index]);
            created.Clear();
            yield return null;
            yield return null;
        }

        [UnityTest]
        public IEnumerator BriefPartialTargetSnapshotKeepsLoadedFormalEnemyVisibleAndAtLastTrustedPosition()
        {
            Fixture fixture = CreateFixture();
            yield return WaitForLoadedFormalView(fixture);
            GameObject view = GetView(fixture);
            var model = view.GetComponentInChildren<CoopEnemyPresentationDefinition>(true);
            Vector3 trustedPosition = view.transform.position;
            int createdViews = fixture.Presenter.CreatedViewCount;
            int recreatedViews = fixture.Presenter.RecreatedViewCount;

            // Advance real authoritative data before the committed count arrives:
            // the presenter must not consume these partial target fields yet.
            for (int index = 0; index < 3; index++) fixture.Authority.ServerStep();
            fixture.Authority.SetServerTargetPosition(1, trustedPosition + Vector3.right * 4f);
            NetcodeWorldState complete = fixture.Authority.WorldState;
            MakeSnapshotIncomplete(fixture.Authority);
            yield return null;
            yield return null;

            Assert.That(fixture.Authority.IsReplicatedSnapshotComplete, Is.False,
                "前置条件：真实 Update 必须经过 NGO 不完整快照分支。");
            Assert.That(view.activeInHierarchy, Is.True,
                "同 authority/run 的短暂部分到达不能让已加载的正式敌人闪灭。");
            Assert.That(model.GetComponentInChildren<Renderer>().gameObject.activeInHierarchy, Is.True);
            Assert.That(Vector3.Distance(view.transform.position, trustedPosition), Is.LessThan(0.0001f),
                "不完整快照只能保留最后可信位置，不能采用尚未提交的新目标字段。");

            SetWorldState(fixture.Authority, complete);
            yield return null;
            yield return null;
            Assert.That(fixture.Authority.IsReplicatedSnapshotComplete, Is.True);
            Assert.That(view.activeInHierarchy, Is.True);
            Assert.That(GetView(fixture), Is.SameAs(view));
            Assert.That(view.GetComponentInChildren<CoopEnemyPresentationDefinition>(true), Is.SameAs(model),
                "完整快照恢复应复用原正式模型，不能通过重建掩盖闪灭。");
            Assert.That(fixture.Presenter.CreatedViewCount, Is.EqualTo(createdViews));
            Assert.That(fixture.Presenter.RecreatedViewCount, Is.EqualTo(recreatedViews));
        }

        [UnityTest]
        public IEnumerator NewRunDuringPartialSnapshotImmediatelyHidesThePreviousRunEnemy()
        {
            Fixture fixture = CreateFixture();
            yield return WaitForLoadedFormalView(fixture);
            GameObject previousRunView = GetView(fixture);
            NetcodeWorldState nextRun = fixture.Authority.WorldState;
            nextRun.RunGeneration++;
            nextRun.SnapshotTargetCount++;

            SetWorldState(fixture.Authority, nextRun);
            yield return null;
            yield return null;

            Assert.That(fixture.Authority.IsReplicatedSnapshotComplete, Is.False);
            Assert.That(previousRunView.activeInHierarchy, Is.False,
                "新 run 的列表尚未完整到达时也必须隐藏旧战局敌人，不能应用短暂保留策略。");
            Assert.That(fixture.Presenter.VisibleViewCount, Is.Zero);
        }

        [UnityTest]
        public IEnumerator ReplacementAuthorityDuringPartialSnapshotImmediatelyHidesThePreviousEnemy()
        {
            Fixture fixture = CreateFixture();
            yield return WaitForLoadedFormalView(fixture);
            GameObject previousAuthorityView = GetView(fixture);
            NetworkCoopSessionAuthority replacement = CreateAuthority();
            MakeSnapshotIncomplete(replacement);

            fixture.Presenter.BindForTests(replacement);
            yield return null;
            yield return null;

            Assert.That(replacement.IsReplicatedSnapshotComplete, Is.False);
            Assert.That(previousAuthorityView.activeInHierarchy, Is.False,
                "切换 authority 即使 targetId/run 数值相同，也不能保留上一连接的可见敌人。");
            Assert.That(fixture.Presenter.VisibleViewCount, Is.Zero);
        }

        [UnityTest]
        public IEnumerator DisablingPresenterDuringPartialSnapshotImmediatelyHidesItsFormalEnemy()
        {
            Fixture fixture = CreateFixture();
            yield return WaitForLoadedFormalView(fixture);
            GameObject view = GetView(fixture);
            MakeSnapshotIncomplete(fixture.Authority);

            fixture.Presenter.enabled = false;

            Assert.That(view.activeInHierarchy, Is.False,
                "停用 presenter 是结束展示，不是允许保留旧模型的临时 partial 等待。");
            Assert.That(fixture.Presenter.VisibleViewCount, Is.Zero);
            yield return null;
            Assert.That(view.activeInHierarchy, Is.False);
        }

        [UnityTest]
        public IEnumerator DisconnectedTransportCannotRetainFormalEnemyDuringPartialSnapshot()
        {
            Fixture fixture = CreateFixture();
            yield return WaitForLoadedFormalView(fixture);
            GameObject view = GetView(fixture);
            MakeSnapshotIncomplete(fixture.Authority);
            // Exercise the real disconnected NetworkManager branch without
            // opening sockets or faking IsConnectedClient / presenter flags.
            var bootstrap = fixture.Presenter.gameObject.AddComponent<OptionalNetworkBootstrap>();
            bootstrap.AutoStart = false;
            Assert.That(bootstrap.NetworkManager, Is.Not.Null);
            Assert.That(bootstrap.NetworkManager.IsServer, Is.False);
            Assert.That(bootstrap.NetworkManager.IsConnectedClient, Is.False);

            yield return null;
            yield return null;

            Assert.That(view.activeInHierarchy, Is.False,
                "真实 transport 已未连接时不能把 partial 的短暂保留误用于旧战局展示。");
            Assert.That(fixture.Presenter.VisibleViewCount, Is.Zero);
        }

        [UnityTest]
        public IEnumerator PartialSnapshotPastTheHoldLimitHidesEnemyAndRecoveryReusesItsLoadedModel()
        {
            Fixture fixture = CreateFixture();
            yield return WaitForLoadedFormalView(fixture);
            GameObject view = GetView(fixture);
            var model = view.GetComponentInChildren<CoopEnemyPresentationDefinition>(true);
            Vector3 trustedPosition = view.transform.position;
            int createdViews = fixture.Presenter.CreatedViewCount;
            int recreatedViews = fixture.Presenter.RecreatedViewCount;
            NetcodeWorldState complete = fixture.Authority.WorldState;
            MakeSnapshotIncomplete(fixture.Authority);
            double started = Time.realtimeSinceStartupAsDouble;

            // Wait real time through real Update frames, beyond the approved
            // 0.75-second hold. Do not patch timestamps or invoke Update by reflection.
            while (Time.realtimeSinceStartupAsDouble - started < 0.85d)
                yield return null;

            Assert.That(fixture.Authority.IsReplicatedSnapshotComplete, Is.False);
            Assert.That(view.activeInHierarchy, Is.False,
                "长期缺少完整快照不能无限保留旧敌人，超过0.75秒应隐藏并等待恢复。");
            Assert.That(fixture.Presenter.VisibleViewCount, Is.Zero);
            Assert.That(Vector3.Distance(view.transform.position, trustedPosition), Is.LessThan(0.0001f));

            SetWorldState(fixture.Authority, complete);
            yield return null;
            yield return null;

            Assert.That(fixture.Authority.IsReplicatedSnapshotComplete, Is.True);
            Assert.That(view.activeInHierarchy, Is.True);
            Assert.That(GetView(fixture), Is.SameAs(view));
            Assert.That(view.GetComponentInChildren<CoopEnemyPresentationDefinition>(true), Is.SameAs(model));
            Assert.That(fixture.Presenter.CreatedViewCount, Is.EqualTo(createdViews));
            Assert.That(fixture.Presenter.RecreatedViewCount, Is.EqualTo(recreatedViews));
        }

        private Fixture CreateFixture()
        {
            var camera = Track(new GameObject("Partial world snapshot camera"))
                .AddComponent<Camera>();
            camera.tag = "MainCamera";
            camera.transform.position = new Vector3(0f, 1.5f, -8f);
            NetworkCoopSessionAuthority authority = CreateAuthority();
            var presenter = Track(new GameObject("Partial world snapshot presenter"))
                .AddComponent<CoopNetworkWorldPresenter>();
            presenter.BindForTests(authority);
            return new Fixture(authority, presenter);
        }

        private NetworkCoopSessionAuthority CreateAuthority()
        {
            var authority = Track(new GameObject("Partial world snapshot authority"))
                .AddComponent<NetworkCoopSessionAuthority>();
            authority.EnableServerTestHook();
            authority.ConfigureServer(new CoopServerRules(tickRate: 60),
                new[] { new CoopPlayerSpawn(1, default) },
                new[] { new CoopTargetSpawn(1, new NetVector3(0d, 0d, 10d), 0.8d, 100d,
                    moveSpeed: 0d, attackDamage: 0d,
                    archetypeId: "enemy.archetype.spider_assault", presentationAddress: EnemyAddress) });
            authority.AutoSimulate = false;
            return authority;
        }

        private static IEnumerator WaitForLoadedFormalView(Fixture fixture)
        {
            float deadline = Time.realtimeSinceStartup + 15f;
            while (Time.realtimeSinceStartup < deadline)
            {
                if (fixture.Presenter.TryGetTargetView(1, out GameObject view) && view.activeInHierarchy &&
                    view.GetComponentInChildren<Renderer>(true) != null &&
                    view.GetComponentInChildren<CoopEnemyPresentationDefinition>(true) != null)
                    break;
                yield return null;
            }
            GameObject loaded = GetView(fixture);
            Assert.That(fixture.Authority.IsReplicatedSnapshotComplete, Is.True);
            Assert.That(loaded.activeInHierarchy, Is.True, "正式模型必须已由真实 Update 显示后才制造 partial 帧。");
            var model = loaded.GetComponentInChildren<CoopEnemyPresentationDefinition>(true);
            Assert.That(model, Is.Not.Null, "不得用测试专用 primitive 代替正式敌人资源。");
            Assert.That(model.SourceAddress, Is.EqualTo(EnemyAddress));
            Assert.That(model.GetComponentInChildren<Renderer>(true), Is.Not.Null);
        }

        private static GameObject GetView(Fixture fixture)
        {
            Assert.That(fixture.Presenter.TryGetTargetView(1, out GameObject view), Is.True);
            return view;
        }

        private static void MakeSnapshotIncomplete(NetworkCoopSessionAuthority authority)
        {
            NetcodeWorldState incomplete = authority.WorldState;
            incomplete.SnapshotTargetCount++;
            SetWorldState(authority, incomplete);
            Assert.That(authority.IsReplicatedSnapshotComplete, Is.False);
        }

        private static void SetWorldState(NetworkCoopSessionAuthority authority, NetcodeWorldState value)
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

        private sealed class Fixture
        {
            public Fixture(NetworkCoopSessionAuthority authority, CoopNetworkWorldPresenter presenter)
            {
                Authority = authority;
                Presenter = presenter;
            }

            public NetworkCoopSessionAuthority Authority { get; }
            public CoopNetworkWorldPresenter Presenter { get; }
        }
    }
}
