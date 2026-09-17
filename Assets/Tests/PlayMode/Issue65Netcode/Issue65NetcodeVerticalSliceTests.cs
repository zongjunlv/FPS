using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FPS.Networking.Domain;
using FPS.Networking.Netcode;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.TestTools;

namespace FPS.Tests.PlayMode.Issue65
{
    public sealed class Issue65NetcodeVerticalSliceTests
    {
        private readonly List<GameObject> created = new();

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            for (int index = created.Count - 1; index >= 0; index--)
            {
                if (created[index] != null)
                {
                    UnityEngine.Object.Destroy(created[index]);
                }
            }
            created.Clear();
            yield return null;
            yield return null;
        }

        [UnityTest]
        public IEnumerator Bootstrap_DefaultsToOffline_AndDoesNotStartNetwork()
        {
            OptionalNetworkBootstrap bootstrap = Track(
                OptionalNetworkBootstrap.CreateRuntime(
                    new NetworkEndpointSettings
                    {
                        Address = "127.0.0.1",
                        ListenAddress = "127.0.0.1",
                        Port = 17965,
                        TickRate = 60
                    }));

            yield return null;

            Assert.That(bootstrap.AutoStart, Is.False);
            Assert.That(bootstrap.State, Is.EqualTo(OptionalNetworkState.Offline));
            Assert.That(bootstrap.IsListening, Is.False);
            Assert.That(bootstrap.NetworkManager.IsServer, Is.False);
            Assert.That(bootstrap.NetworkManager.IsClient, Is.False);
        }

        [UnityTest]
        public IEnumerator Bootstrap_ClientCredentialEnablesApprovalConfig()
        {
            OptionalNetworkBootstrap bootstrap = Track(
                OptionalNetworkBootstrap.CreateRuntime(
                    new NetworkEndpointSettings
                    {
                        Address = "127.0.0.1",
                        ListenAddress = "127.0.0.1",
                        Port = 17968,
                        TickRate = 60
                    }));

            bootstrap.ConfigureClientCredential("signed-short-lived-ticket");
            yield return null;

            Assert.That(bootstrap.NetworkManager.NetworkConfig
                .ConnectionApproval, Is.True);
            Assert.That(bootstrap.NetworkManager.NetworkConfig.ConnectionData,
                Is.Not.Empty);
        }

        [Test]
        public void InputDriver_ExclusiveOwnerPreventsGameplayWriterOverride()
        {
            GameObject gameObject = Track(new GameObject(
                "Issue100 Exclusive Input Driver"));
            gameObject.AddComponent<NetworkPlayerReplica>();
            NetworkVerticalSliceInputDriver driver = gameObject.AddComponent<
                NetworkVerticalSliceInputDriver>();
            var owner = new object();

            Assert.That(driver.TryAcquireExclusiveInput(owner), Is.True);
            Assert.That(driver.SetExclusiveInputFrame(owner, Vector2.zero,
                0f, 0f, false, false, false, true, false), Is.True);
            driver.SetInputFrame(Vector2.zero, 0f, 0f, false, false,
                false, false, false);

            FieldInfo crouch = typeof(NetworkVerticalSliceInputDriver)
                .GetField("crouchRequested",
                    BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(crouch, Is.Not.Null);
            Assert.That((bool)crouch.GetValue(driver), Is.True,
                "普通玩家输入不得覆盖自动验收持有的独占输入帧。");
            Assert.That(driver.ReleaseExclusiveInput(owner), Is.True);
            driver.SetInputFrame(Vector2.zero, 0f, 0f, false, false,
                false, false, false);
            Assert.That((bool)crouch.GetValue(driver), Is.False);
        }

        [Test]
        public void InputDriver_WaitsWhenClientTickReachesServerFutureBudget()
        {
            NetworkCoopSessionAuthority authority = CreateAuthority();
            authority.RegisterPlayerClient(10, 1);
            GameObject gameObject = Track(new GameObject(
                "Issue100 Tick Budget Input Driver"));
            NetworkPlayerReplica replica = gameObject.AddComponent<
                NetworkPlayerReplica>();
            NetworkVerticalSliceInputDriver driver = gameObject.AddComponent<
                NetworkVerticalSliceInputDriver>();
            replica.EnableOwnerTestHook(authority, 1);

            FieldInfo clientTick = typeof(NetworkVerticalSliceInputDriver)
                .GetField("clientTick",
                    BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(clientTick, Is.Not.Null);
            clientTick.SetValue(driver, 1L);

            Assert.That(driver.CanSubmitCurrentFrame, Is.False,
                "客户端不得发送超过服务端允许未来窗口的输入 Tick。");
            authority.ServerStep();
            Assert.That(driver.CanSubmitCurrentFrame, Is.True,
                "服务端 Tick 推进后应重新开放一个输入发送配额。");
        }

        [UnityTest]
        public IEnumerator Bootstrap_CanStartHost_AndShutdownIdempotently()
        {
            OptionalNetworkBootstrap bootstrap = Track(
                OptionalNetworkBootstrap.CreateRuntime(
                    new NetworkEndpointSettings
                    {
                        Address = "127.0.0.1",
                        ListenAddress = "127.0.0.1",
                        Port = 17966,
                        TickRate = 60
                    }));

            Assert.That(bootstrap.StartHost(), Is.True, bootstrap.LastFailure);
            yield return null;
            Assert.That(bootstrap.NetworkManager.IsHost, Is.True);
            Assert.That(bootstrap.IsListening, Is.True);

            bootstrap.Shutdown();
            bootstrap.Shutdown();
            for (int frame = 0; frame < 20 && bootstrap.IsListening; frame++)
            {
                yield return null;
            }

            Assert.That(bootstrap.State, Is.EqualTo(OptionalNetworkState.Offline));
        }

        [UnityTest]
        public IEnumerator Installer_SpawnsAuthorityAndHostPlayer_OnlyAfterHostStarts()
        {
            OptionalNetworkBootstrap bootstrap = Track(
                OptionalNetworkBootstrap.CreateRuntime(
                    new NetworkEndpointSettings
                    {
                        Address = "127.0.0.1",
                        ListenAddress = "127.0.0.1",
                        Port = 17967,
                        TickRate = 60
                    }));
            CoopNetworkRuntimeInstaller installer =
                bootstrap.gameObject.AddComponent<CoopNetworkRuntimeInstaller>();
            NetworkObject authorityPrefab = Resources.Load<GameObject>(
                "Networking/CoopSessionAuthority")?.GetComponent<NetworkObject>();
            NetworkObject replicaPrefab = Resources.Load<GameObject>(
                "Networking/CoopPlayerReplica")?.GetComponent<NetworkObject>();
            Assert.That(authorityPrefab, Is.Not.Null);
            Assert.That(replicaPrefab, Is.Not.Null);
            installer.ConfigurePrefabs(authorityPrefab, replicaPrefab);
            Assert.That(installer.RegisterConfiguredPrefabs(), Is.True,
                installer.LastFailure);
            Assert.That(installer.SessionAuthority, Is.Null);

            Assert.That(bootstrap.StartHost(), Is.True, bootstrap.LastFailure);
            yield return null;
            yield return null;

            Assert.That(installer.LastFailure, Is.Empty);
            Assert.That(installer.SessionAuthority, Is.Not.Null);
            Assert.That(installer.SessionAuthority.IsSpawned, Is.True);
            Assert.That(installer.SpawnedPlayerCount, Is.EqualTo(1));

            bootstrap.Shutdown();
            yield return null;
        }

        [Test]
        public void Authority_RejectsMutation_WhenNotServer()
        {
            NetworkCoopSessionAuthority authority = CreateAuthority(
                enableServerHook: false);

            Assert.Throws<InvalidOperationException>(() =>
                authority.ConfigureServer(
                    new CoopServerRules(),
                    PlayerSpawns(),
                    TargetSpawns()));
        }

        [Test]
        public void Authority_RejectsSpoofedClientIdentity_BeforeDomainRules()
        {
            NetworkCoopSessionAuthority authority = CreateAuthority();
            authority.RegisterPlayerClient(10, 1);
            NetcodePlayerCommand spoofed = NetcodePlayerCommand.FromDomain(
                Command(2, 1, 1, 1, false));

            Assert.That(authority.TryQueueCommand(10, spoofed, false), Is.False);
            Assert.That(authority.ServerStep().Commands, Is.Empty);
        }

        [Test]
        public void Authority_UsesDomainReplayProtection_ForDuplicateNonce()
        {
            NetworkCoopSessionAuthority authority = CreateAuthority();
            authority.RegisterPlayerClient(10, 1);
            NetcodePlayerCommand first = NetcodePlayerCommand.FromDomain(
                Command(1, 1, 77, 1, false));
            NetcodePlayerCommand duplicate = NetcodePlayerCommand.FromDomain(
                Command(1, 2, 77, 2, false));

            Assert.That(authority.TryQueueCommand(10, first, false), Is.True);
            Assert.That(authority.ServerStep().Commands.Single().Accepted, Is.True);
            Assert.That(authority.TryQueueCommand(10, duplicate, false), Is.True);
            CommandResolution rejected =
                authority.ServerStep().Commands.Single();

            Assert.That(rejected.Accepted, Is.False);
            Assert.That(rejected.RejectionReason,
                Is.EqualTo(CommandRejectionReason.DuplicateNonce));
        }

        [Test]
        public void HostAndClientCommands_ResolveInOneAuthoritativeTick()
        {
            NetworkCoopSessionAuthority authority = CreateAuthority();
            authority.RegisterPlayerClient(10, 1);
            authority.RegisterPlayerClient(11, 2);

            Assert.That(authority.TryQueueCommand(
                10,
                NetcodePlayerCommand.FromDomain(Command(1, 1, 201, 1, false)),
                false), Is.True);
            Assert.That(authority.TryQueueCommand(
                11,
                NetcodePlayerCommand.FromDomain(Command(2, 1, 202, 1, false)),
                false), Is.True);

            AuthoritativeTickResult result = authority.ServerStep();

            Assert.That(result.Commands.Count, Is.EqualTo(2));
            Assert.That(result.Commands.All(value => value.Accepted), Is.True);
            Assert.That(result.Snapshot.Players.Count, Is.EqualTo(2));
        }

        [Test]
        public void ServerShots_DamageKillDropAndCompleteWave_InDomainOrder()
        {
            NetworkCoopSessionAuthority authority = CreateAuthority();
            authority.RegisterPlayerClient(10, 1);

            Assert.That(authority.TryQueueCommand(
                10,
                NetcodePlayerCommand.FromDomain(Command(1, 1, 101, 1, true)),
                true), Is.True);
            AuthoritativeTickResult first = authority.ServerStep();
            Assert.That(first.Commands.Single().Shot.Kind,
                Is.EqualTo(ShotResolutionKind.Hit));
            for (int index = 0; index < 5; index++)
            {
                authority.ServerStep();
            }

            Assert.That(authority.TryQueueCommand(
                10,
                NetcodePlayerCommand.FromDomain(Command(1, 2, 102, 7, true)),
                true), Is.True);
            AuthoritativeTickResult killed = authority.ServerStep();

            Assert.That(killed.Commands.Single().Shot.Kind,
                Is.EqualTo(ShotResolutionKind.Killed));
            Assert.That(killed.Snapshot.WaveStatus,
                Is.EqualTo(AuthoritativeWaveStatus.Completed));
            CollectionAssert.IsSubsetOf(
                new[]
                {
                    AuthoritativeEventKind.TargetDamaged,
                    AuthoritativeEventKind.TargetKilled,
                    AuthoritativeEventKind.LootDropped,
                    AuthoritativeEventKind.WaveCompleted
                },
                killed.Events.Select(value => value.Kind).ToArray());
        }

        [Test]
        public void LocalOwner_PredictsThenConsumesServerAcknowledgement()
        {
            NetworkCoopSessionAuthority authority = CreateAuthority();
            authority.RegisterPlayerClient(10, 1);
            NetworkPlayerReplica replica = CreateReplica();
            replica.EnableOwnerTestHook(authority, 1);

            NetcodePlayerCommand predicted = replica.BuildPredictedCommand(
                1f, 0f, 0f, 0f, false, 1);
            Assert.That(replica.PendingPredictionCount, Is.EqualTo(1));
            Assert.That(predicted.ClaimedPosition.x, Is.GreaterThan(0f));
            Assert.That(authority.TryQueueCommand(10, predicted, false), Is.True);
            AuthoritativeTickResult result = authority.ServerStep();
            NetcodePlayerState state = NetcodePlayerState.FromDomain(
                result.Tick,
                result.Snapshot.Player(1));

            replica.ConsumeServerState(state, true, result.Tick);

            Assert.That(replica.PendingPredictionCount, Is.Zero);
            Assert.That(replica.LastPredictionCorrection.Kind,
                Is.EqualTo(PredictionCorrectionKind.None));
            Assert.That(Vector3.Distance(
                replica.PresentedPosition,
                state.Position), Is.LessThan(0.0001f));
        }

        [Test]
        public void RemotePlayer_InterpolatesBetweenReplicatedSnapshots()
        {
            NetworkCoopSessionAuthority authority = CreateAuthority();
            NetworkPlayerReplica replica = CreateReplica();
            replica.Bind(authority, 2);
            var from = new NetcodePlayerState
            {
                ServerTick = 10,
                PlayerId = 2,
                Position = Vector3.zero,
                Health = 100f
            };
            var to = new NetcodePlayerState
            {
                ServerTick = 12,
                PlayerId = 2,
                Position = new Vector3(2f, 0f, 0f),
                Health = 100f
            };

            replica.ConsumeServerState(from, false, 10d);
            replica.ConsumeServerState(to, false, 13d);

            Assert.That(replica.LastRemoteSample.Available, Is.True);
            Assert.That(replica.LastRemoteSample.Ratio, Is.EqualTo(0.5d)
                .Within(0.0001d));
            Assert.That(replica.PresentedPosition.x,
                Is.EqualTo(1f).Within(0.0001f));
        }

        [Test]
        public void DisconnectReset_ClearsPredictionAndInterpolationState()
        {
            NetworkCoopSessionAuthority authority = CreateAuthority();
            NetworkPlayerReplica replica = CreateReplica();
            replica.EnableOwnerTestHook(authority, 1);
            replica.BuildPredictedCommand(1f, 0f, 0f, 0f, false, 1);

            replica.ResetPresentation();

            Assert.That(replica.PendingPredictionCount, Is.Zero);
            Assert.That(replica.LastRemoteSample.Available, Is.False);
        }

        private NetworkCoopSessionAuthority CreateAuthority(
            bool enableServerHook = true)
        {
            GameObject gameObject = Track(new GameObject("Issue65 Authority"));
            NetworkCoopSessionAuthority authority =
                gameObject.AddComponent<NetworkCoopSessionAuthority>();
            if (enableServerHook)
            {
                authority.EnableServerTestHook();
                authority.ConfigureServer(
                    new CoopServerRules(),
                    PlayerSpawns(),
                    TargetSpawns(),
                    requiredKills: 1);
            }
            return authority;
        }

        private NetworkPlayerReplica CreateReplica()
        {
            GameObject gameObject = Track(new GameObject("Issue65 Replica"));
            return gameObject.AddComponent<NetworkPlayerReplica>();
        }

        private static CoopPlayerSpawn[] PlayerSpawns()
        {
            return new[]
            {
                new CoopPlayerSpawn(1, new NetVector3(0d, 0d, 0d)),
                new CoopPlayerSpawn(2, new NetVector3(2d, 0d, 0d))
            };
        }

        private static CoopTargetSpawn[] TargetSpawns()
        {
            return new[]
            {
                new CoopTargetSpawn(
                    1,
                    new NetVector3(0d, 0d, 5d),
                    1d,
                    68d,
                    "medkit")
            };
        }

        private static PlayerInputCommand Command(
            int playerId,
            uint sequence,
            ulong nonce,
            long tick,
            bool fire)
        {
            return new PlayerInputCommand(
                playerId,
                sequence,
                nonce,
                tick,
                0d,
                0d,
                0d,
                0d,
                fire,
                playerId == 1
                    ? new NetVector3(0d, 0d, 0d)
                    : new NetVector3(2d, 0d, 0d));
        }

        private T Track<T>(T component) where T : Component
        {
            created.Add(component.gameObject);
            return component;
        }

        private GameObject Track(GameObject gameObject)
        {
            created.Add(gameObject);
            return gameObject;
        }
    }
}
