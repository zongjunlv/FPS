using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using FPS.Core.GameModes;
using FPS.Networking.Domain;
using FPS.Networking.Netcode;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using Unity.Collections;
using Unity.Netcode;

namespace FPS.Tests.PlayMode.Issue65
{
    public sealed class CoopFastAmmoProjectionTests
    {
        private GameObject rigObject;
        private GameObject authorityObject;
        private GameObject replicaObject;
        private NetworkCoopSessionAuthority authority;
        private NetworkPlayerReplica replica;
        private CoopNetworkInputBridge bridge;
        private WeaponController weapon;
        private NetcodePlayerState baseline;
        private readonly HashSet<UnifiedGameHud> oldHuds = new();
        private readonly HashSet<EventSystem> oldSystems = new();

        [SetUp]
        public void SetUp()
        {
            GameModeContext.ResetForTests();
            GameModeContext.BeginTransition(GameModeId.Coop, GameModeStage.CoopBattle);
            foreach (var hud in Object.FindObjectsByType<UnifiedGameHud>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None)) oldHuds.Add(hud);
            foreach (var system in Object.FindObjectsByType<EventSystem>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None)) oldSystems.Add(system);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (rigObject != null) Object.Destroy(rigObject);
            if (replicaObject != null) Object.Destroy(replicaObject);
            if (authorityObject != null) Object.Destroy(authorityObject);
            foreach (var hud in Object.FindObjectsByType<UnifiedGameHud>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (!oldHuds.Contains(hud)) Object.Destroy(hud.gameObject);
            foreach (var system in Object.FindObjectsByType<EventSystem>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (!oldSystems.Contains(system)) Object.Destroy(system.gameObject);
            GameModeContext.ResetForTests();
            oldHuds.Clear();
            oldSystems.Clear();
            yield return null;
            yield return null;
        }

        [Test]
        public void FastShotAmmoBeforeWorldAcknowledgementDoesNotSubtractTheSameShotTwice()
        {
            CreateRigAndAuthority();
            int originalMagazine = weapon.CurrentAmmo;
            Assert.That(weapon.TryPredictNetworkFire(), Is.True);
            NetcodePlayerCommand command = replica.BuildPredictedCommand(0, 0, 0, 0, true, 1);
            // Drive the same two bridge callbacks used by the runtime driver;
            // no local settlement or artificial ACK advancement is introduced.
            InvokeBridge(bridge, "HandleCommandSubmitted", command);
            InvokeBridge(bridge, "ReconcileCombatState");
            Assert.That(weapon.CurrentAmmo, Is.EqualTo(originalMagazine - 1));

            Assert.That(replica.ConsumeFastShotFeedbackEvent(new NetcodeShotFeedbackEvent
            {
                Sequence = 1,
                ServerTick = 1,
                ShooterPlayerId = 1,
                ShotCommandSequence = command.Sequence,
                Kind = ShotResolutionKind.Hit,
                HasAmmoState = true,
                AmmoWeaponId = NetworkPresentationIds.RifleGameplay,
                AmmoAcknowledgedSequence = command.Sequence,
                MagazineAmmo = originalMagazine - 1,
                ReserveAmmo = baseline.ReserveAmmo
            }), Is.True);
            Assert.That(replica.PresentedAcknowledgedSequence, Is.EqualTo(baseline.AcknowledgedSequence),
                "射击快速确认不得伪造移动/整体输入的确认进度。");
            InvokeBridge(bridge, "ReconcileCombatState");
            Assert.That(weapon.CurrentAmmo, Is.EqualTo(originalMagazine - 1),
                "快速反馈已经包含本枪扣弹，世界 ACK 尚未到达也不能再扣同一枪。");
        }

        [Test]
        public void FirstBatchedFeedbackUsesTheFinalSnapshotsAmmoPrefix()
        {
            CreateRigAndAuthority();
            int originalMagazine = weapon.CurrentAmmo;
            NetcodePlayerCommand first = SubmitPredictedShot(1);
            NetcodePlayerCommand second = SubmitPredictedShot(2);
            Assert.That(authority.TryQueueCommand(10, first, true), Is.True);
            Assert.That(authority.TryQueueCommand(10, second, true), Is.True);
            authority.ServerStep();
            var feedback = ReadFeedback();
            Assert.That(feedback.Count, Is.EqualTo(2));
            Assert.That(feedback[0].ShotCommandSequence, Is.EqualTo(first.Sequence));
            Assert.That(feedback[0].AmmoAcknowledgedSequence, Is.EqualTo(second.Sequence),
                "早一枪的反馈也携带同tick最终snapshot，ammo前缀必须对应最终处理序列。");
            Assert.That(feedback[1].RejectionReason, Is.EqualTo(CommandRejectionReason.FireRateExceeded));
            Assert.That(replica.ConsumeFastShotFeedbackEvent(feedback[0]), Is.True);
            InvokeBridge(bridge, "ReconcileCombatState");
            Assert.That(weapon.CurrentAmmo, Is.EqualTo(feedback[0].MagazineAmmo));
            Assert.That(weapon.CurrentAmmo, Is.EqualTo(originalMagazine - 1),
                "第二枪已经包含在最终snapshot中（射速校验拒绝），不能再对它做预测扣弹。");
            Assert.That(replica.PresentedAcknowledgedSequence, Is.Zero);
        }

        [Test]
        public void EarlyRejectedShotRestoresOnlyThatShotAndRetainsQueuedPrediction()
        {
            CreateRigAndAuthority();
            int originalMagazine = weapon.CurrentAmmo;
            NetcodePlayerCommand queued = SubmitPredictedShot(1);
            NetcodePlayerCommand earlyRejected = SubmitPredictedShot(1000);
            Assert.That(authority.TryQueueCommand(10, queued, true), Is.True);
            Assert.That(authority.TryQueueCommand(10, earlyRejected, true), Is.False);
            var feedback = ReadFeedback();
            Assert.That(feedback.Count, Is.EqualTo(1));
            Assert.That(feedback[0].RejectionReason, Is.EqualTo(CommandRejectionReason.TimestampInFuture));
            Assert.That(feedback[0].AmmoAcknowledgedSequence, Is.Zero,
                "排队前拒绝的反馈只含当前snapshot，不能声称较早已排队命令已执行。");
            Assert.That(replica.ConsumeFastShotFeedbackEvent(feedback[0]), Is.True);
            InvokeBridge(bridge, "ReconcileCombatState");
            Assert.That(weapon.CurrentAmmo, Is.EqualTo(originalMagazine - 1),
                "拒绝本枪应回补它的预测扣弹，同时保留尚在服务端排队的前一枪。");
            Assert.That(replica.PresentedAcknowledgedSequence, Is.Zero);
            authority.ServerStep();
            feedback = ReadFeedback();
            Assert.That(feedback.Count, Is.EqualTo(2));
            Assert.That(feedback[1].AmmoAcknowledgedSequence, Is.EqualTo(queued.Sequence));
            Assert.That(replica.ConsumeFastShotFeedbackEvent(feedback[1]), Is.True);
            InvokeBridge(bridge, "ReconcileCombatState");
            Assert.That(weapon.CurrentAmmo, Is.EqualTo(originalMagazine - 1));
        }

        [Test]
        public void OutOfOrderAndDuplicateFeedbackKeepsAmmoAndMovementAcknowledgementStable()
        {
            CreateRigAndAuthority();
            int originalMagazine = weapon.CurrentAmmo;
            NetcodePlayerCommand first = SubmitPredictedShot(1);
            Assert.That(authority.TryQueueCommand(10, first, true), Is.True);
            authority.ServerStep();
            for (int index = 0; index < 5; index++) authority.ServerStep();
            NetcodePlayerCommand second = SubmitPredictedShot(7);
            Assert.That(authority.TryQueueCommand(10, second, true), Is.True);
            authority.ServerStep();
            var feedback = ReadFeedback();
            Assert.That(feedback.Count, Is.EqualTo(2));
            Assert.That(feedback[0].Accepted && feedback[1].Accepted, Is.True);
            Assert.That(replica.ConsumeFastShotFeedbackEvent(feedback[1]), Is.True);
            InvokeBridge(bridge, "ReconcileCombatState");
            Assert.That(weapon.CurrentAmmo, Is.EqualTo(originalMagazine - 2));
            Assert.That(replica.ConsumeFastShotFeedbackEvent(feedback[1]), Is.False);
            Assert.That(replica.ConsumeFastShotFeedbackEvent(feedback[0]), Is.True);
            InvokeBridge(bridge, "ReconcileCombatState");
            Assert.That(replica.PresentedAmmoAcknowledgedSequence, Is.EqualTo(second.Sequence));
            Assert.That(weapon.CurrentAmmo, Is.EqualTo(originalMagazine - 2),
                "迟到旧枪及重复副本既不能多扣弹，也不能把ammo回滚为旧值。");
            Assert.That(replica.ConsumeShotFeedbackEvent(feedback[0]), Is.False);
            Assert.That(replica.ConsumeShotFeedbackEvent(feedback[1]), Is.False);
            InvokeBridge(bridge, "ReconcileCombatState");
            Assert.That(weapon.CurrentAmmo, Is.EqualTo(originalMagazine - 2));
            Assert.That(replica.PresentedAcknowledgedSequence, Is.Zero,
                "ammo独立确认不能推进移动输入ACK。");
            Assert.That(replica.LastShotEventSequence, Is.EqualTo(feedback[1].Sequence));
        }

        [Test]
        public void AmmoPrefixRoundTripsOnBothFeedbackTransports()
        {
            var feedback = new NetcodeShotFeedbackEvent
            {
                Sequence = 2, ShotCommandSequence = 3, AmmoAcknowledgedSequence = 7,
                ShooterPlayerId = 1, HasAmmoState = true, MagazineAmmo = 49,
                ReserveAmmo = 150, AmmoWeaponId = NetworkPresentationIds.RifleGameplay
            };
            using var writer = new FastBufferWriter(512, Allocator.Temp);
            writer.WriteNetworkSerializable(feedback);
            using var reader = new FastBufferReader(writer, Allocator.Temp);
            reader.ReadNetworkSerializable(out NetcodeShotFeedbackEvent copy);
            Assert.That(copy, Is.EqualTo(feedback));
            Assert.That(copy.AmmoAcknowledgedSequence, Is.EqualTo(7));
            copy.AmmoAcknowledgedSequence = 3;
            Assert.That(copy.Equals(feedback), Is.False,
                "可靠NetworkList的相等判断也必须包含ammo前缀。");
        }

        private void CreateRigAndAuthority()
        {
            PlayerGameplayRig rig = PlayerGameplayRig.Create(new Vector3(0, 50, 0), Quaternion.identity);
            rigObject = rig.gameObject;
            Assert.That(rig.CompositionRoot.TryInitialize(), Is.True);
            bridge = rig.GetComponent<CoopNetworkInputBridge>() ??
                rig.gameObject.AddComponent<CoopNetworkInputBridge>();
            bridge.enabled = false;
            authorityObject = new GameObject("Fast ammo projection authority");
            authority = authorityObject.AddComponent<NetworkCoopSessionAuthority>();
            authority.EnableServerTestHook();
            authority.ConfigureServer(new CoopServerRules(),
                new[] { new CoopPlayerSpawn(1, default) },
                new[] { new CoopTargetSpawn(1, new NetVector3(0, 0, 20), 1, 1000) }, 1);
            authority.RegisterPlayerClient(10, 1);
            replicaObject = new GameObject("Fast ammo projection replica");
            replica = replicaObject.AddComponent<NetworkPlayerReplica>();
            replica.EnableOwnerTestHook(authority, 1);
            authority.TryGetPlayerState(1, out baseline);
            replica.ConsumeServerState(baseline, true, baseline.ServerTick);
            InvokeBridge(bridge, "SubscribeReplica", replica);
            InvokeBridge(bridge, "ReconcileCombatState");
            weapon = rig.Combat.EquippedWeapon;
        }

        private NetcodePlayerCommand SubmitPredictedShot(long tick)
        {
            NetcodePlayerCommand command = replica.BuildPredictedCommand(0, 0, 0, 0, true, tick);
            InvokeBridge(bridge, "HandleCommandSubmitted", command);
            InvokeBridge(bridge, "ReconcileCombatState");
            return command;
        }

        private List<NetcodeShotFeedbackEvent> ReadFeedback()
        {
            var feedback = new List<NetcodeShotFeedbackEvent>();
            authority.GetShotEventsAfter(1, 0, feedback);
            return feedback;
        }

        private static void InvokeBridge(CoopNetworkInputBridge bridge, string methodName,
            params object[] arguments)
        {
            MethodInfo method = typeof(CoopNetworkInputBridge).GetMethod(methodName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            method.Invoke(bridge, arguments);
        }
    }
}
