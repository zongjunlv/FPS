using System.Collections;
using FPS.Networking.Netcode;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.TestTools;

namespace FPS.Tests.PlayMode.Issue65
{
    public sealed class Issue85DedicatedServerRuntimeTests
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
        public IEnumerator StartServerCreatesAuthorityWithoutHostPlayer()
        {
            var endpoint = NetworkEndpointSettings.Localhost;
            endpoint.Port = 17970;
            endpoint.TickRate = 30;
            bootstrap = OptionalNetworkBootstrap.CreateRuntime(endpoint,
                    "Issue85 Dedicated Server Test");
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
            installer.ConfigureMaximumPlayers(2);
            Assert.That(installer.RegisterConfiguredPrefabs(), Is.True,
                installer.LastFailure);

            Assert.That(bootstrap.StartServer(), Is.True,
                bootstrap.LastFailure);
            for (int frame = 0; frame < 5; frame++) yield return null;

            Assert.That(bootstrap.State,
                Is.EqualTo(OptionalNetworkState.DedicatedServer));
            Assert.That(bootstrap.NetworkManager.IsServer, Is.True);
            Assert.That(bootstrap.NetworkManager.IsClient, Is.False);
            Assert.That(installer.SessionAuthority, Is.Not.Null);
            Assert.That(installer.SessionAuthority.IsSpawned, Is.True);
            Assert.That(installer.SessionAuthority.Rules.TickRate, Is.EqualTo(30));
            Assert.That(installer.SpawnedPlayerCount, Is.Zero,
                "专用服务器不得占用一个本地玩家槽位。");
        }

        [UnityTest]
        public IEnumerator PresentationStripperDisablesClientOnlyComponents()
        {
            var root = new GameObject("Issue85 Presentation");
            Camera camera = root.AddComponent<Camera>();
            AudioListener listener = root.AddComponent<AudioListener>();
            AudioSource audioSource = root.AddComponent<AudioSource>();
            Canvas canvas = root.AddComponent<Canvas>();
            Light light = root.AddComponent<Light>();
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.transform.SetParent(root.transform);
            Renderer renderer = cube.GetComponent<Renderer>();

            DedicatedServerPresentationResult result =
                DedicatedServerPresentationStripper.Strip();
            yield return null;

            Assert.That(camera.enabled, Is.False);
            Assert.That(listener.enabled, Is.False);
            Assert.That(audioSource.enabled, Is.False);
            Assert.That(canvas.enabled, Is.False);
            Assert.That(light.enabled, Is.False);
            Assert.That(renderer.enabled, Is.False);
            Assert.That(result.Cameras, Is.GreaterThanOrEqualTo(1));
            Object.Destroy(root);
            yield return null;
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
