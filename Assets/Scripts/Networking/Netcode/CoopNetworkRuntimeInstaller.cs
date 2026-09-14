using System;
using System.Collections.Generic;
using FPS.Networking.Domain;
using Unity.Netcode;
using UnityEngine;

namespace FPS.Networking.Netcode
{
    [Serializable]
    public struct CoopPlayerSpawnDefinition
    {
        public int PlayerId;
        public Vector3 Position;
        public float Health;

        public CoopPlayerSpawn ToDomain()
        {
            return new CoopPlayerSpawn(
                PlayerId,
                NetcodeConversions.ToDomain(Position),
                Health <= 0f ? 100d : Health);
        }
    }

    [Serializable]
    public struct CoopTargetSpawnDefinition
    {
        public int TargetId;
        public Vector3 Position;
        public float Radius;
        public float Health;
        public string DropDefinitionId;

        public CoopTargetSpawn ToDomain()
        {
            return new CoopTargetSpawn(
                TargetId,
                NetcodeConversions.ToDomain(Position),
                Radius <= 0f ? 0.75d : Radius,
                Health <= 0f ? 100d : Health,
                DropDefinitionId);
        }
    }

    /// <summary>
    /// Server runtime composition for the two-player vertical slice. Prefabs
    /// are supplied by the project builder, keeping this assembly scene-free.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(OptionalNetworkBootstrap))]
    public sealed class CoopNetworkRuntimeInstaller : MonoBehaviour
    {
        [SerializeField] private NetworkObject sessionAuthorityPrefab;
        [SerializeField] private NetworkObject playerReplicaPrefab;
        [SerializeField] private CoopPlayerSpawnDefinition[] players =
        {
            new()
            {
                PlayerId = 1,
                Position = new Vector3(-1.5f, 0f, 0f),
                Health = 100f
            },
            new()
            {
                PlayerId = 2,
                Position = new Vector3(1.5f, 0f, 0f),
                Health = 100f
            }
        };
        [SerializeField] private CoopTargetSpawnDefinition[] targets =
        {
            new()
            {
                TargetId = 1,
                Position = new Vector3(0f, 0f, 15f),
                Radius = 1f,
                Health = 68f,
                DropDefinitionId = "medkit"
            }
        };
        [SerializeField] private int requiredKills = 1;

        private readonly Dictionary<ulong, NetworkObject> playerObjects = new();
        private OptionalNetworkBootstrap bootstrap;
        private NetworkCoopSessionAuthority sessionAuthority;
        private bool callbacksBound;

        public NetworkCoopSessionAuthority SessionAuthority => sessionAuthority;
        public string LastFailure { get; private set; } = string.Empty;
        public int SpawnedPlayerCount => playerObjects.Count;

        private void Awake()
        {
            bootstrap = GetComponent<OptionalNetworkBootstrap>();
        }

        private void OnEnable()
        {
            BindCallbacks();
        }

        private void OnDisable()
        {
            UnbindCallbacks();
        }

        private void OnDestroy()
        {
            UnbindCallbacks();
            if (bootstrap != null && bootstrap.NetworkManager != null &&
                bootstrap.NetworkManager.IsServer)
            {
                DespawnAll();
            }
        }

        public void ConfigurePrefabs(
            NetworkObject authorityPrefab,
            NetworkObject replicaPrefab)
        {
            if (bootstrap != null && bootstrap.IsListening)
            {
                throw new InvalidOperationException(
                    "Network prefabs must be configured before startup.");
            }

            sessionAuthorityPrefab = authorityPrefab;
            playerReplicaPrefab = replicaPrefab;
        }

        public void ConfigureScenario(
            CoopPlayerSpawnDefinition[] playerDefinitions,
            CoopTargetSpawnDefinition[] targetDefinitions,
            int killsRequired)
        {
            if (bootstrap != null && bootstrap.IsListening)
            {
                throw new InvalidOperationException(
                    "The scenario must be configured before startup.");
            }

            players = playerDefinitions ?? throw new ArgumentNullException(
                nameof(playerDefinitions));
            targets = targetDefinitions ?? throw new ArgumentNullException(
                nameof(targetDefinitions));
            requiredKills = killsRequired;
        }

        public bool RegisterConfiguredPrefabs()
        {
            if (bootstrap == null)
            {
                bootstrap = GetComponent<OptionalNetworkBootstrap>();
            }

            if (sessionAuthorityPrefab == null || playerReplicaPrefab == null)
            {
                LastFailure = "Session authority and player replica prefabs are required.";
                return false;
            }

            bootstrap.RegisterNetworkPrefab(sessionAuthorityPrefab.gameObject);
            bootstrap.RegisterNetworkPrefab(playerReplicaPrefab.gameObject);
            LastFailure = string.Empty;
            return true;
        }

        /// <summary>
        /// Explicit host-side hook used both by OnServerStarted and PlayMode
        /// fixtures. It never runs for a normal offline scene.
        /// </summary>
        public bool SpawnAuthoritativeSlice()
        {
            if (bootstrap == null || bootstrap.NetworkManager == null ||
                !bootstrap.NetworkManager.IsServer)
            {
                LastFailure = "Only a running server can spawn the co-op slice.";
                return false;
            }
            if (sessionAuthority != null)
            {
                return true;
            }
            if (sessionAuthorityPrefab == null || playerReplicaPrefab == null)
            {
                LastFailure = "Network prefabs were not configured.";
                return false;
            }

            NetworkObject instance = Instantiate(sessionAuthorityPrefab);
            sessionAuthority = instance.GetComponent<NetworkCoopSessionAuthority>();
            if (sessionAuthority == null)
            {
                Destroy(instance.gameObject);
                LastFailure = "Authority prefab is missing NetworkCoopSessionAuthority.";
                return false;
            }

            sessionAuthority.ConfigureServer(
                new CoopServerRules(
                    tickRate: (int)bootstrap.NetworkManager.NetworkConfig.TickRate),
                ConvertPlayers(players),
                ConvertTargets(targets),
                requiredKills);
            sessionAuthority.RegisterPlayerClient(
                NetworkManager.ServerClientId,
                1);
            instance.Spawn(destroyWithScene: true);
            SpawnPlayer(NetworkManager.ServerClientId, 1);
            LastFailure = string.Empty;
            return true;
        }

        private void BindCallbacks()
        {
            if (callbacksBound)
            {
                return;
            }
            if (bootstrap == null)
            {
                bootstrap = GetComponent<OptionalNetworkBootstrap>();
            }
            if (bootstrap?.NetworkManager == null)
            {
                return;
            }

            bootstrap.NetworkManager.OnServerStarted += HandleServerStarted;
            bootstrap.NetworkManager.OnClientConnectedCallback +=
                HandleClientConnected;
            bootstrap.NetworkManager.OnClientDisconnectCallback +=
                HandleClientDisconnected;
            callbacksBound = true;
        }

        private void UnbindCallbacks()
        {
            if (!callbacksBound || bootstrap?.NetworkManager == null)
            {
                return;
            }

            bootstrap.NetworkManager.OnServerStarted -= HandleServerStarted;
            bootstrap.NetworkManager.OnClientConnectedCallback -=
                HandleClientConnected;
            bootstrap.NetworkManager.OnClientDisconnectCallback -=
                HandleClientDisconnected;
            callbacksBound = false;
        }

        private void HandleServerStarted()
        {
            SpawnAuthoritativeSlice();
        }

        private void HandleClientConnected(ulong clientId)
        {
            if (bootstrap?.NetworkManager == null ||
                !bootstrap.NetworkManager.IsServer)
            {
                return;
            }
            if (!SpawnAuthoritativeSlice() || playerObjects.ContainsKey(clientId))
            {
                return;
            }
            if (clientId == NetworkManager.ServerClientId)
            {
                SpawnPlayer(clientId, 1);
                return;
            }
            if (playerObjects.Count >= 2)
            {
                bootstrap.NetworkManager.DisconnectClient(
                    clientId,
                    "This vertical slice supports Host + 1 Client.");
                return;
            }

            sessionAuthority.RegisterPlayerClient(clientId, 2);
            SpawnPlayer(clientId, 2);
        }

        private void HandleClientDisconnected(ulong clientId)
        {
            if (bootstrap?.NetworkManager == null ||
                !bootstrap.NetworkManager.IsServer)
            {
                return;
            }

            if (playerObjects.Remove(clientId, out NetworkObject playerObject) &&
                playerObject != null && playerObject.IsSpawned)
            {
                playerObject.Despawn(destroy: true);
            }
            if (sessionAuthority != null)
            {
                sessionAuthority.UnregisterPlayerClient(clientId);
            }
        }

        private void SpawnPlayer(ulong clientId, int playerId)
        {
            if (playerObjects.ContainsKey(clientId))
            {
                return;
            }

            NetworkObject playerObject = Instantiate(playerReplicaPrefab);
            NetworkPlayerReplica replica =
                playerObject.GetComponent<NetworkPlayerReplica>();
            if (replica == null)
            {
                Destroy(playerObject.gameObject);
                throw new InvalidOperationException(
                    "Player prefab is missing NetworkPlayerReplica.");
            }

            replica.ConfigureServerIdentity(sessionAuthority, playerId);
            playerObject.SpawnAsPlayerObject(
                clientId,
                destroyWithScene: true);
            playerObjects.Add(clientId, playerObject);
        }

        private void DespawnAll()
        {
            foreach (NetworkObject player in playerObjects.Values)
            {
                if (player != null && player.IsSpawned)
                {
                    player.Despawn(destroy: true);
                }
            }
            playerObjects.Clear();
            if (sessionAuthority != null && sessionAuthority.IsSpawned)
            {
                sessionAuthority.NetworkObject.Despawn(destroy: true);
            }
            sessionAuthority = null;
        }

        private static IEnumerable<CoopPlayerSpawn> ConvertPlayers(
            IReadOnlyList<CoopPlayerSpawnDefinition> definitions)
        {
            for (int index = 0; index < definitions.Count; index++)
            {
                yield return definitions[index].ToDomain();
            }
        }

        private static IEnumerable<CoopTargetSpawn> ConvertTargets(
            IReadOnlyList<CoopTargetSpawnDefinition> definitions)
        {
            for (int index = 0; index < definitions.Count; index++)
            {
                yield return definitions[index].ToDomain();
            }
        }
    }
}
