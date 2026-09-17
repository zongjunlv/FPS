using System.Collections;
using FPS.Networking.Domain;
using FPS.Networking.Netcode;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FPS.Tests.PlayMode.Issue65
{
    public sealed class Issue89MovementAuthorityTests
    {
        private GameObject authorityObject;
        private GameObject ceiling;
        private GameObject playerBody;
        private GameObject replicaObject;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (authorityObject != null) Object.Destroy(authorityObject);
            if (ceiling != null) Object.Destroy(ceiling);
            if (playerBody != null) Object.Destroy(playerBody);
            if (replicaObject != null) Object.Destroy(replicaObject);
            yield return null;
        }

        [Test]
        public void LocalOwnerPredictionPublishesPoseBeforeServerRoundTrip()
        {
            NetworkCoopSessionAuthority authority = CreateAuthority();
            replicaObject = new GameObject("Issue89 Local Replica");
            NetworkPlayerReplica replica =
                replicaObject.AddComponent<NetworkPlayerReplica>();
            replica.EnableOwnerTestHook(authority, 1);
            int presentationEvents = 0;
            replica.PosePresented += (_, _, _, _, _) => presentationEvents++;

            NetcodePlayerCommand command = replica.BuildPredictedCommand(
                0f, 1f, 0f, 0f, false, 1,
                jumpPressed: false, sprintHeld: true,
                crouchRequested: false);

            Assert.That(presentationEvents, Is.EqualTo(1));
            Assert.That(command.SprintHeld, Is.True);
            Assert.That(replica.PresentedPosition.z, Is.GreaterThan(0f));
            Assert.That(replica.PendingPredictionCount, Is.EqualTo(1));
            Assert.That(authority.LastServerResult, Is.Null,
                "本地预测应在服务器处理命令前立即呈现。");
        }

        [UnityTest]
        public IEnumerator ServerPhysicsBlocksStandingUnderCeiling()
        {
            NetworkCoopSessionAuthority authority = CreateAuthority();
            authority.RegisterPlayerClient(10UL, 1);

            NetcodePlayerCommand crouch = NetcodePlayerCommand.FromDomain(
                new PlayerInputCommand(1, 1, 101, 1,
                    0d, 0d, 0d, 0d, false, default,
                    false, false, true));
            Assert.That(authority.TryQueueCommand(10UL, crouch, false), Is.True);
            Assert.That(authority.ServerStep().Commands[0].Accepted, Is.True);

            playerBody = new GameObject("Issue89 Player Body");
            playerBody.AddComponent<CharacterController>();
            Physics.SyncTransforms();

            NetcodePlayerCommand clearStand = NetcodePlayerCommand.FromDomain(
                new PlayerInputCommand(1, 2, 102, 2,
                    0d, 0d, 0d, 0d, false, default,
                    false, false, false));
            Assert.That(authority.TryQueueCommand(
                10UL, clearStand, false), Is.True);
            Assert.That(authority.ServerStep().Commands[0].Accepted, Is.True,
                "玩家自身 CharacterController 不应阻挡起身。");

            NetcodePlayerCommand crouchAgain =
                NetcodePlayerCommand.FromDomain(
                    new PlayerInputCommand(1, 3, 103, 3,
                        0d, 0d, 0d, 0d, false, default,
                        false, false, true));
            Assert.That(authority.TryQueueCommand(
                10UL, crouchAgain, false), Is.True);
            Assert.That(authority.ServerStep().Commands[0].Accepted, Is.True);

            ceiling = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ceiling.name = "Issue89 Low Ceiling";
            ceiling.transform.SetPositionAndRotation(
                new Vector3(0f, 1.55f, 0f), Quaternion.identity);
            ceiling.transform.localScale = new Vector3(2f, 0.2f, 2f);
            Physics.SyncTransforms();
            yield return new WaitForFixedUpdate();

            NetcodePlayerCommand stand = NetcodePlayerCommand.FromDomain(
                new PlayerInputCommand(1, 4, 104, 4,
                    0d, 0d, 0d, 0d, false, default,
                    false, false, false));
            Assert.That(authority.TryQueueCommand(10UL, stand, false), Is.True);
            CommandResolution resolution = authority.ServerStep().Commands[0];

            Assert.That(resolution.Accepted, Is.True,
                "空间不足时应接受输入并保持下蹲，而不是锁死后续操作。");
            Assert.That(authority.LastAuthoritativeSnapshot.Player(1).IsCrouching,
                Is.True);
        }

        private NetworkCoopSessionAuthority CreateAuthority()
        {
            authorityObject = new GameObject("Issue89 Authority");
            NetworkCoopSessionAuthority authority =
                authorityObject.AddComponent<NetworkCoopSessionAuthority>();
            authority.EnableServerTestHook();
            authority.ConfigureServer(
                new CoopServerRules(
                    tickRate: 10,
                    maximumPastCommandTicks: 8,
                    historyCapacity: 16,
                    maximumMoveSpeed: 5d,
                    claimedPositionTolerance: 0.1d),
                new[] { new CoopPlayerSpawn(1, default) },
                new[]
                {
                    new CoopTargetSpawn(1,
                        new NetVector3(0d, 0d, 20d), 1d, 100d)
                });
            return authority;
        }
    }
}
