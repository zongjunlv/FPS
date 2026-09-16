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
        public const string DefaultAppearanceId =
            NetworkPresentationIds.DefaultAppearance;

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
        [SerializeField] private int maximumPlayers = 2;

        private readonly Dictionary<ulong, NetworkObject> playerObjects = new();
        private readonly Dictionary<ulong, int> playerIds = new();
        private readonly Dictionary<int, string> appearanceIds = new();
        private readonly HashSet<ulong> waitingClients = new();
        private OptionalNetworkBootstrap bootstrap;
        private NetworkCoopSessionAuthority sessionAuthority;
        private bool callbacksBound;
        private bool deferPlayerSpawns;
        private bool playerSpawnBarrierReleased;

        public NetworkCoopSessionAuthority SessionAuthority => sessionAuthority;
        public string LastFailure { get; private set; } = string.Empty;
        public int SpawnedPlayerCount => playerObjects.Count;
        public int MaximumPlayers => maximumPlayers;
        public bool IsPlayerSpawnDeferred => deferPlayerSpawns &&
                                             !playerSpawnBarrierReleased;

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
            maximumPlayers = Mathf.Clamp(players.Length, 1, 16);
        }

        public void ConfigureMaximumPlayers(int value)
        {
            if (bootstrap != null && bootstrap.IsListening)
                throw new InvalidOperationException(
                    "Maximum players must be configured before startup.");
            maximumPlayers = Mathf.Clamp(value, 1, 16);
        }

        public void ConfigurePlayerSpawnBarrier(bool defer)
        {
            if (bootstrap != null && bootstrap.IsListening)
                throw new InvalidOperationException(
                    "Player spawn barrier must be configured before startup.");
            deferPlayerSpawns = defer;
            playerSpawnBarrierReleased = !defer;
        }

        public void ConfigurePlayerAppearance(int playerId, string appearanceId)
        {
            if (playerId <= 0)
                throw new ArgumentOutOfRangeException(nameof(playerId));
            string normalized = NetworkPresentationIds.ResolveAppearance(
                appearanceId);
            appearanceIds[playerId] = normalized;
            if (sessionAuthority != null && sessionAuthority.IsConfigured)
                sessionAuthority.ConfigurePlayerAppearance(
                    playerId, normalized);

            foreach (KeyValuePair<ulong, int> pair in playerIds)
            {
                if (pair.Value != playerId ||
                    !playerObjects.TryGetValue(pair.Key,
                        out NetworkObject playerObject) ||
                    playerObject == null) continue;
                NetworkPlayerReplica replica =
                    playerObject.GetComponent<NetworkPlayerReplica>();
                replica?.ConfigureServerAppearance(normalized);
            }
        }

        public string AppearanceForPlayer(int playerId) =>
            appearanceIds.TryGetValue(playerId, out string appearanceId)
                ? appearanceId
                : DefaultAppearanceId;

        public void ReleasePlayerSpawnBarrier()
        {
            if (bootstrap?.NetworkManager == null ||
                !bootstrap.NetworkManager.IsServer)
                throw new InvalidOperationException(
                    "Only the server can release the player spawn barrier.");
            playerSpawnBarrierReleased = true;
            ulong[] clients = new ulong[waitingClients.Count];
            waitingClients.CopyTo(clients);
            waitingClients.Clear();
            for (int index = 0; index < clients.Length; index++)
                SpawnApprovedClient(clients[index]);
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

            DontDestroyOnLoad(instance.gameObject);
            instance.Spawn(destroyWithScene: false);
            try
            {
                sessionAuthority.ConfigureServer(
                    new CoopServerRules(
                        tickRate: (int)bootstrap.NetworkManager.NetworkConfig.TickRate,
                        maximumMoveSpeed: 5d,
                        claimedPositionTolerance: 0.45d,
                        predictionCorrectionThreshold: 0.08d,
                        predictionSnapThreshold: 1.5d,
                        walkSpeed: 2d,
                        sprintSpeed: 5d,
                        crouchSpeed: 1.5d,
                        maximumAcceleration: 30d,
                        gravity: 20d,
                        jumpSpeed: 7.75d,
                        minimumJumpIntervalTicks: 12),
                    ConvertPlayers(players),
                    ConvertTargets(targets),
                    requiredKills);
            }
            catch
            {
                if (instance.IsSpawned)
                    instance.Despawn(destroy: true);
                sessionAuthority = null;
                throw;
            }
            if (bootstrap.NetworkManager.IsHost)
            {
                if (IsPlayerSpawnDeferred)
                    waitingClients.Add(NetworkManager.ServerClientId);
                else
                {
                    sessionAuthority.RegisterPlayerClient(
                        NetworkManager.ServerClientId,
                        1);
                    SpawnPlayer(NetworkManager.ServerClientId, 1);
                }
            }
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
            if (deferPlayerSpawns && playerSpawnBarrierReleased)
            {
                bootstrap.NetworkManager.DisconnectClient(clientId,
                    "战局已经开始，请返回房间等待下一局。");
                return;
            }
            if (IsPlayerSpawnDeferred)
            {
                waitingClients.Add(clientId);
                return;
            }
            SpawnApprovedClient(clientId);
        }

        private void SpawnApprovedClient(ulong clientId)
        {
            if (playerObjects.ContainsKey(clientId) || sessionAuthority == null)
                return;
            if (clientId == NetworkManager.ServerClientId)
            {
                sessionAuthority.RegisterPlayerClient(clientId, 1);
                SpawnPlayer(clientId, 1);
                return;
            }

            if (bootstrap.AdmissionService != null)
            {
                if (!bootstrap.TryGetApprovedIdentity(clientId,
                        out CoopApprovedIdentity approved))
                {
                    bootstrap.NetworkManager.DisconnectClient(clientId,
                        CoopAdmissionMessages.Describe(
                            CoopAdmissionFailure.InvalidCredential));
                    return;
                }

                sessionAuthority.RegisterPlayerClient(clientId,
                    approved.SimulationPlayerId);
                SpawnPlayer(clientId, approved.SimulationPlayerId);
                return;
            }
            if (playerObjects.Count >= maximumPlayers)
            {
                bootstrap.NetworkManager.DisconnectClient(
                    clientId,
                    "The dedicated server has reached its player limit.");
                return;
            }

            int playerId = ResolveAvailablePlayerId();
            if (playerId <= 0)
            {
                bootstrap.NetworkManager.DisconnectClient(clientId,
                    "No authoritative player slot is available.");
                return;
            }
            sessionAuthority.RegisterPlayerClient(clientId, playerId);
            SpawnPlayer(clientId, playerId);
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
            playerIds.Remove(clientId);
            waitingClients.Remove(clientId);
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

            replica.ConfigureServerIdentity(sessionAuthority, playerId,
                AppearanceForPlayer(playerId));
            DontDestroyOnLoad(playerObject.gameObject);
            playerObject.SpawnAsPlayerObject(
                clientId,
                destroyWithScene: false);
            playerObjects.Add(clientId, playerObject);
            playerIds[clientId] = playerId;
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
            playerIds.Clear();
            waitingClients.Clear();
            if (sessionAuthority != null && sessionAuthority.IsSpawned)
            {
                sessionAuthority.NetworkObject.Despawn(destroy: true);
            }
            sessionAuthority = null;
        }

        private int ResolveAvailablePlayerId()
        {
            for (int index = 0; index < players.Length; index++)
            {
                int candidate = players[index].PlayerId;
                if (candidate > 0 && !playerIds.ContainsValue(candidate))
                    return candidate;
            }
            return -1;
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
