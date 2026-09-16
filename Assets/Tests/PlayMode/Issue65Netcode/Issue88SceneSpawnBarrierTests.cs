using System.Collections;
using FPS.Networking.Netcode;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.TestTools;

namespace FPS.Tests.PlayMode.Issue65
{
    public sealed class Issue88SceneSpawnBarrierTests
    {
        private OptionalNetworkBootstrap bootstrap;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            NetworkManager[] managers = Object.FindObjectsByType<NetworkManager>(
                FindObjectsInactive.Include);
            for (int index = 0; index < managers.Length; index++)
            {
                if (managers[index].IsListening)
                    managers[index].Shutdown(discardMessageQueue: true);
                Object.Destroy(managers[index].gameObject);
            }
            yield return null;
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (bootstrap != null)
            {
                bootstrap.ResetForTests();
                Object.Destroy(bootstrap.gameObject);
                bootstrap = null;
            }
            yield return null;
            yield return null;
        }

        [UnityTest]
        public IEnumerator HostPlayerSpawnsOnlyAfterSceneBarrierRelease()
        {
            var endpoint = NetworkEndpointSettings.Localhost;
            endpoint.Port = 17988;
            bootstrap = OptionalNetworkBootstrap.CreateRuntime(endpoint,
                "Issue88 Scene Spawn Barrier Test");
            CoopNetworkRuntimeInstaller installer =
                bootstrap.gameObject.AddComponent<CoopNetworkRuntimeInstaller>();
            NetworkObject authority = Resources.Load<GameObject>(
                    "Networking/CoopSessionAuthority")
                .GetComponent<NetworkObject>();
            NetworkObject player = Resources.Load<GameObject>(
                    "Networking/CoopPlayerReplica")
                .GetComponent<NetworkObject>();
            installer.ConfigurePrefabs(authority, player);
            installer.ConfigureScenario(new[]
            {
                Player(1, -1.25f), Player(2, 1.25f)
            }, new[]
            {
                new CoopTargetSpawnDefinition
                {
                    TargetId = 1,
                    Position = new Vector3(0f, 0f, 15f),
                    Radius = 1f,
                    Health = 68f,
                    DropDefinitionId = "medkit"
                }
            }, 1);
            installer.ConfigurePlayerSpawnBarrier(defer: true);
            Assert.That(installer.RegisterConfiguredPrefabs(), Is.True,
                installer.LastFailure);

            Assert.That(bootstrap.StartHost(), Is.True,
                bootstrap.LastFailure);
            for (int frame = 0; frame < 5; frame++) yield return null;

            Assert.That(installer.SessionAuthority, Is.Not.Null);
            Assert.That(installer.SessionAuthority.IsSpawned, Is.True);
            Assert.That(installer.IsPlayerSpawnDeferred, Is.True);
            Assert.That(installer.SpawnedPlayerCount, Is.Zero,
                "CityNew 未完成同步加载前不得生成玩家对象。");

            installer.ReleasePlayerSpawnBarrier();
            yield return null;
            yield return null;

            Assert.That(installer.IsPlayerSpawnDeferred, Is.False);
            Assert.That(installer.SpawnedPlayerCount, Is.EqualTo(1),
                "服务器释放屏障后，每个连接只能生成一个权威玩家对象。");
            Assert.That(bootstrap.NetworkManager.ConnectedClientsList.Count,
                Is.EqualTo(installer.SpawnedPlayerCount));
        }

        private static CoopPlayerSpawnDefinition Player(int id, float x)
        {
            return new CoopPlayerSpawnDefinition
            {
                PlayerId = id,
                Position = new Vector3(x, 0f, 0f),
                Health = 100f
            };
        }
    }
}
