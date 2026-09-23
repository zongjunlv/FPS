using System;
using System.Collections.Generic;
using System.Linq;
using FPS.Networking.Domain;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace FPS.Networking.Netcode
{
    /// <summary>
    /// NGO transport adapter around the domain's authoritative simulation.
    /// All combat, rewind, damage, kill, loot and wave rules remain in Domain.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class NetworkCoopSessionAuthority : NetworkBehaviour
    {
        private const int MaximumReplicatedEvents = 64;
        private const int MaximumPresentationEvents = 64;
        private const int MaximumShotEvents = 96;

        [SerializeField] private bool autoSimulate = true;

        private readonly NetworkVariable<NetcodeWorldState> worldState = new(
            default,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<NetcodeRulesState> rulesState = new(
            default,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);
        private readonly NetworkList<NetcodePlayerState> playerStates = new(
            null,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);
        private readonly NetworkList<NetcodeTargetState> targetStates = new(
            null,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);
        private readonly NetworkList<NetcodeAuthorityEvent> authorityEvents = new(
            null,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);
        private readonly NetworkList<NetcodePresentationEvent>
            presentationEvents = new(
                null,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server);
        private readonly NetworkList<NetcodeShotFeedbackEvent> shotEvents =
            new(null, NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server);
        private readonly NetworkList<NetcodeInventorySlotState>
            inventoryStates = new(null,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server);
        private readonly NetworkList<NetcodeWorldDropState> worldDropStates =
            new(null, NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server);
        private readonly NetworkList<NetcodeProgressionState>
            progressionStates = new(null,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server);
        private readonly NetworkList<NetcodeUpgradeStackState> upgradeStates =
            new(null, NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server);

        private readonly Dictionary<ulong, int> playerByClient = new();
        private readonly Dictionary<int, ulong> clientByPlayer = new();
        private readonly List<PlayerInputCommand> pendingCommands = new();
        private readonly List<AuthoritativeEconomyCommand>
            pendingEconomyCommands = new();
        private readonly List<AuthoritativeMissionCommand>
            pendingMissionCommands = new();
        private readonly Dictionary<(int playerId, uint sequence),
            NetcodePlayerCommand> pendingPresentationInputs = new();
        private readonly Dictionary<int, ServerPresentationState>
            playerPresentation = new();
        private readonly List<NetcodePresentationEvent>
            offlinePresentationEvents = new();
        private readonly List<NetcodeShotFeedbackEvent> offlineShotEvents =
            new();
        private AuthoritativeCoopSimulation simulation;
        private AuthoritativeTickResult lastResult;
        private AuthoritativeWorldSnapshot lastSnapshot;
        private double accumulatedSeconds;
        private bool testServerAuthority;
        private long nextPresentationEventSequence;
        private long nextShotEventSequence;
        private CoopServerRules configuredRules;
        private CoopPlayerSpawn[] configuredPlayers = Array.Empty<CoopPlayerSpawn>();
        private CoopTargetSpawn[] configuredTargets = Array.Empty<CoopTargetSpawn>();
        private AuthoritativeWaveDefinition[] configuredWaves =
            Array.Empty<AuthoritativeWaveDefinition>();
        private AuthoritativeMissionDefinition configuredMission;
        private int configuredRequiredKills;
        private int runGeneration = 1;
        private readonly Collider[] standingOverlaps = new Collider[16];
        private readonly RaycastHit[] shotObstructionHits = new RaycastHit[32];
        private readonly RaycastHit[] playerMovementHits = new RaycastHit[24];
        private NavMeshPath enemyPath;
        private readonly Vector3[] enemyPathCorners = new Vector3[32];

        public event Action<AuthoritativeTickResult> ServerTickCompleted;
        public event Action<ulong, int> UnauthorizedCommandRejected;

        public bool AutoSimulate
        {
            get => autoSimulate;
            set => autoSimulate = value;
        }

        public bool IsConfigured => simulation != null;
        public CoopServerRules Rules => simulation?.Rules ??
            rulesState.Value.ToDomain();
        public NetcodeWorldState WorldState => worldState.Value;
        public int ReplicatedPlayerCount => IsSpawned
            ? playerStates.Count
            : LastAuthoritativeSnapshot?.Players.Count ?? 0;
        public int ReplicatedTargetCount => IsSpawned
            ? targetStates.Count
            : LastAuthoritativeSnapshot?.Targets.Count ?? 0;
        public int ReplicatedEventCount => authorityEvents.Count;
        public int ReplicatedPresentationEventCount =>
            IsSpawned ? presentationEvents.Count : offlinePresentationEvents.Count;
        public int ReplicatedShotEventCount => IsSpawned
            ? shotEvents.Count
            : offlineShotEvents.Count;
        public int ReplicatedInventorySlotCount => IsSpawned
            ? inventoryStates.Count
            : LastAuthoritativeSnapshot?.Economy.InventorySlots.Count ?? 0;
        public int ReplicatedWorldDropCount => IsSpawned
            ? worldDropStates.Count
            : LastAuthoritativeSnapshot?.Economy.WorldDrops.Count ?? 0;
        public int ReplicatedProgressionCount => IsSpawned
            ? progressionStates.Count
            : LastAuthoritativeSnapshot?.Economy.Progression.Count ?? 0;
        public int ReplicatedUpgradeCount => IsSpawned
            ? upgradeStates.Count
            : LastAuthoritativeSnapshot?.Economy.Upgrades.Count ?? 0;
        public AuthoritativeTickResult LastServerResult => lastResult;
        public AuthoritativeWorldSnapshot LastAuthoritativeSnapshot =>
            lastSnapshot ?? simulation?.CaptureSnapshot();
        public bool IsReplicatedSnapshotComplete
        {
            get
            {
                NetcodeWorldState committed = worldState.Value;
                return committed.SnapshotPlayerCount > 0 &&
                    ReplicatedPlayerCount == committed.SnapshotPlayerCount &&
                    ReplicatedTargetCount == committed.SnapshotTargetCount &&
                    ReplicatedInventorySlotCount ==
                        committed.SnapshotInventoryCount &&
                    ReplicatedWorldDropCount == committed.SnapshotDropCount &&
                    ReplicatedProgressionCount ==
                        committed.SnapshotProgressionCount &&
                    ReplicatedUpgradeCount == committed.SnapshotUpgradeCount;
            }
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            if (IsServer && simulation != null)
            {
                PublishSnapshot(simulation.CaptureSnapshot(),
                    Array.Empty<AuthoritativeEvent>());
            }
        }

        private void Update()
        {
            if (!autoSimulate || !IsServer || simulation == null)
            {
                return;
            }

            accumulatedSeconds += Time.unscaledDeltaTime;
            double fixedDelta = simulation.Rules.FixedDeltaSeconds;
            int safety = 0;
            while (accumulatedSeconds >= fixedDelta && safety++ < 8)
            {
                accumulatedSeconds -= fixedDelta;
                ServerStep();
            }
        }

        public void ConfigureServer(
            CoopServerRules rules,
            IEnumerable<CoopPlayerSpawn> players,
            IEnumerable<CoopTargetSpawn> targets,
            int requiredKills = 0,
            AuthoritativeMissionDefinition mission = null,
            IEnumerable<AuthoritativeWaveDefinition> waves = null)
        {
            RequireServerWrite();
            configuredRules = rules ?? throw new ArgumentNullException(
                nameof(rules));
            configuredPlayers = (players ?? throw new ArgumentNullException(
                nameof(players))).ToArray();
            configuredTargets = (targets ?? throw new ArgumentNullException(
                nameof(targets))).ToArray();
            configuredWaves = (waves ??
                Array.Empty<AuthoritativeWaveDefinition>()).ToArray();
            configuredRequiredKills = requiredKills;
            configuredMission = mission ?? AuthoritativeMissionDefinition.Default;
            simulation = new AuthoritativeCoopSimulation(
                configuredRules,
                configuredPlayers,
                configuredTargets,
                configuredRequiredKills,
                configuredMission: configuredMission,
                configuredWaves: configuredWaves);
            simulation.SetStandingClearanceValidator(HasStandingClearance);
            simulation.SetShotObstructionResolver(ResolveShotObstruction);
            simulation.SetPlayerMovementResolver(ResolvePlayerMovement);
            simulation.SetEnemyMovementResolver(ResolveEnemyMovement);
            NetcodeRulesState replicatedRules =
                NetcodeRulesState.FromDomain(rules);
            if (IsSpawned)
            {
                rulesState.Value = replicatedRules;
            }
            else
            {
                rulesState.Reset(replicatedRules);
            }
            pendingCommands.Clear();
            pendingEconomyCommands.Clear();
            pendingMissionCommands.Clear();
            pendingPresentationInputs.Clear();
            playerPresentation.Clear();
            offlinePresentationEvents.Clear();
            offlineShotEvents.Clear();
            nextPresentationEventSequence = 0;
            nextShotEventSequence = 0;
            accumulatedSeconds = 0d;
            lastResult = null;
            lastSnapshot = simulation.CaptureSnapshot();
            for (int index = 0; index < lastSnapshot.Players.Count; index++)
            {
                int playerId = lastSnapshot.Players[index].PlayerId;
                playerPresentation[playerId] = ServerPresentationState.Default;
            }
            if (IsSpawned && IsServer)
            {
                PublishSnapshot(lastSnapshot,
                    Array.Empty<AuthoritativeEvent>());
            }
        }

        public void RegisterPlayerClient(
            ulong clientId,
            int playerId,
            bool resetInputClock = false)
        {
            RequireServerWrite();
            if (playerId <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(playerId));
            }

            if (clientByPlayer.TryGetValue(playerId, out ulong previousClient) &&
                previousClient != clientId)
            {
                playerByClient.Remove(previousClient);
                ClearPendingCommands(playerId);
            }
            if (playerByClient.TryGetValue(clientId, out int previousPlayer) &&
                previousPlayer != playerId)
                clientByPlayer.Remove(previousPlayer);
            playerByClient[clientId] = playerId;
            clientByPlayer[playerId] = clientId;
            if (simulation != null)
            {
                if (resetInputClock)
                    simulation.ResetPlayerInputClock(playerId);
                IReadOnlyList<AuthoritativeEvent> events =
                    simulation.SetPlayerConnected(playerId, true);
                lastSnapshot = simulation.CaptureSnapshot();
                PublishSnapshot(lastSnapshot, events);
            }
        }

        /// <summary>
        /// Establishes the authoritative connection baseline before a remote
        /// allocation is handed to clients. Dedicated servers begin with no
        /// active players so AI and mission failure cannot advance during the
        /// deployment/connection gap.
        /// </summary>
        public void InitializePlayerConnections(
            IEnumerable<int> connectedPlayerIds)
        {
            RequireServerWrite();
            if (simulation == null)
                throw new InvalidOperationException(
                    "ConfigureServer must be called before initializing connections.");
            simulation.InitializePlayerConnections(connectedPlayerIds);
            lastSnapshot = simulation.CaptureSnapshot();
            PublishSnapshot(lastSnapshot, Array.Empty<AuthoritativeEvent>());
        }

        public void UnregisterPlayerClient(ulong clientId)
        {
            RequireServerWrite();
            if (playerByClient.TryGetValue(clientId, out int playerId) &&
                clientByPlayer.TryGetValue(playerId, out ulong currentClient) &&
                currentClient == clientId)
            {
                playerByClient.Remove(clientId);
                clientByPlayer.Remove(playerId);
                FinalizeDisconnectedPlayer(playerId);
                return;
            }
            playerByClient.Remove(clientId);
        }

        public bool SuspendPlayerClient(ulong clientId)
        {
            RequireServerWrite();
            if (!playerByClient.TryGetValue(clientId, out int playerId) ||
                !clientByPlayer.TryGetValue(playerId, out ulong currentClient) ||
                currentClient != clientId)
            {
                playerByClient.Remove(clientId);
                return false;
            }
            playerByClient.Remove(clientId);
            clientByPlayer.Remove(playerId);
            ClearPendingCommands(playerId);
            return true;
        }

        public bool FinalizeDisconnectedPlayer(int playerId)
        {
            RequireServerWrite();
            if (playerId <= 0 || simulation == null ||
                clientByPlayer.ContainsKey(playerId))
                return false;
            ClearPendingCommands(playerId);
            IReadOnlyList<AuthoritativeEvent> events =
                simulation.SetPlayerConnected(playerId, false);
            lastSnapshot = simulation.CaptureSnapshot();
            PublishSnapshot(lastSnapshot, events);
            return true;
        }

        public bool TryGetBoundClient(int playerId, out ulong clientId) =>
            clientByPlayer.TryGetValue(playerId, out clientId);

        public bool TryGetBoundPlayer(ulong clientId, out int playerId) =>
            playerByClient.TryGetValue(clientId, out playerId);

        // Player commands share one ordered reliable stream. Movement, jump
        // and fire use the same domain sequence, so mixing reliable and
        // unreliable RPC channels can let a later movement command overtake a
        // discrete action under packet loss and poison reconciliation.
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone,
            Delivery = RpcDelivery.Reliable)]
        public void SubmitInputRpc(
            NetcodePlayerCommand payload,
            RpcParams rpcParams = default)
        {
            TryQueueCommand(rpcParams.Receive.SenderClientId, payload,
                forceFire: false);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone,
            Delivery = RpcDelivery.Reliable)]
        public void SubmitActionInputRpc(
            NetcodePlayerCommand payload,
            RpcParams rpcParams = default)
        {
            TryQueueCommand(rpcParams.Receive.SenderClientId, payload,
                forceFire: false);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone,
            Delivery = RpcDelivery.Reliable)]
        public void SubmitShotRpc(
            NetcodePlayerCommand payload,
            RpcParams rpcParams = default)
        {
            TryQueueCommand(rpcParams.Receive.SenderClientId, payload,
                forceFire: true);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone,
            Delivery = RpcDelivery.Reliable)]
        public void SubmitPresentationRpc(
            NetcodePresentationCommand payload,
            RpcParams rpcParams = default)
        {
            TryApplyPresentationCommand(
                rpcParams.Receive.SenderClientId,
                payload);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone,
            Delivery = RpcDelivery.Reliable)]
        public void SubmitEconomyRpc(
            NetcodeEconomyCommand payload,
            RpcParams rpcParams = default)
        {
            TryQueueEconomyCommand(
                rpcParams.Receive.SenderClientId,
                payload);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone,
            Delivery = RpcDelivery.Unreliable)]
        public void SubmitMissionRpc(
            NetcodeMissionCommand payload,
            RpcParams rpcParams = default)
        {
            TryQueueMissionCommand(
                rpcParams.Receive.SenderClientId,
                payload);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone,
            Delivery = RpcDelivery.Reliable)]
        public void RequestRestartMissionRpc(RpcParams rpcParams = default)
        {
            TryRestartMission(rpcParams.Receive.SenderClientId);
        }

        /// <summary>
        /// Server-side transport/authentication seam. This is public so a
        /// multi-process PlayMode fixture can inject the actual sender id.
        /// </summary>
        public bool TryQueueCommand(
            ulong senderClientId,
            NetcodePlayerCommand payload,
            bool forceFire)
        {
            if (!CanServerWrite || simulation == null ||
                !playerByClient.TryGetValue(senderClientId, out int playerId) ||
                payload.PlayerId != playerId)
            {
                UnauthorizedCommandRejected?.Invoke(
                    senderClientId,
                    payload.PlayerId);
                return false;
            }

            pendingCommands.Add(payload.ToDomain(forceFire));
            pendingPresentationInputs[(payload.PlayerId, payload.Sequence)] =
                payload;
            return true;
        }

        public bool TryQueueEconomyCommand(
            ulong senderClientId,
            NetcodeEconomyCommand payload)
        {
            if (!CanServerWrite || simulation == null ||
                !playerByClient.TryGetValue(senderClientId, out int playerId) ||
                payload.PlayerId != playerId)
            {
                UnauthorizedCommandRejected?.Invoke(
                    senderClientId,
                    payload.PlayerId);
                return false;
            }
            pendingEconomyCommands.Add(payload.ToDomain());
            return true;
        }

        public bool TryQueueMissionCommand(
            ulong senderClientId,
            NetcodeMissionCommand payload)
        {
            if (!CanServerWrite || simulation == null ||
                !playerByClient.TryGetValue(senderClientId, out int playerId) ||
                payload.PlayerId != playerId)
            {
                UnauthorizedCommandRejected?.Invoke(
                    senderClientId, payload.PlayerId);
                return false;
            }
            pendingMissionCommands.Add(payload.ToDomain());
            return true;
        }

        public bool TryApplyPresentationCommand(
            ulong senderClientId,
            NetcodePresentationCommand payload)
        {
            if (!CanServerWrite || simulation == null ||
                !playerByClient.TryGetValue(senderClientId, out int playerId) ||
                payload.PlayerId != playerId ||
                !playerPresentation.TryGetValue(playerId,
                    out ServerPresentationState state) ||
                payload.Sequence == 0 ||
                payload.Sequence <= state.LastPresentationCommandSequence)
            {
                UnauthorizedCommandRejected?.Invoke(
                    senderClientId,
                    payload.PlayerId);
                return false;
            }

            if (payload.Action != NetworkPresentationAction.Reload &&
                payload.Action != NetworkPresentationAction.SwitchWeapon)
            {
                return false;
            }

            string weaponId = state.WeaponId;
            string gameplayWeaponId = NetworkPresentationIds
                .ToGameplayWeaponId(weaponId);
            if (payload.Action == NetworkPresentationAction.SwitchWeapon)
            {
                if (!NetworkPresentationIds.TryResolveWeapon(
                        payload.WeaponId.ToString(), out weaponId))
                {
                    return false;
                }
                gameplayWeaponId = NetworkPresentationIds
                    .ToGameplayWeaponId(weaponId);
            }

            var combatEvents = new List<AuthoritativeEvent>();
            WeaponActionResolution combat = simulation.ApplyWeaponAction(
                playerId,
                payload.Action == NetworkPresentationAction.Reload
                    ? AuthoritativeWeaponAction.Reload
                    : AuthoritativeWeaponAction.SwitchWeapon,
                gameplayWeaponId,
                combatEvents);
            if (!combat.Accepted) return false;
            if (payload.Action == NetworkPresentationAction.SwitchWeapon)
                state.WeaponId = weaponId;

            state.LastPresentationCommandSequence = payload.Sequence;
            playerPresentation[playerId] = state;
            EmitPresentationEvent(playerId, payload.Action, weaponId);
            lastSnapshot = simulation.CaptureSnapshot();
            PublishSnapshot(lastSnapshot, combatEvents);
            RefreshReplicatedPlayerState(playerId);
            return true;
        }

        public void ConfigurePlayerPresentation(
            int playerId,
            string appearanceId,
            string weaponId)
        {
            RequireServerWrite();
            if (!playerPresentation.TryGetValue(playerId,
                    out ServerPresentationState state))
            {
                throw new ArgumentOutOfRangeException(nameof(playerId));
            }
            state.AppearanceId = NetworkPresentationIds.ResolveAppearance(
                appearanceId);
            state.WeaponId = NetworkPresentationIds.ResolveWeaponOrDefault(
                weaponId);
            playerPresentation[playerId] = state;
            RefreshReplicatedPlayerState(playerId);
        }

        public void ConfigurePlayerAppearance(int playerId, string appearanceId)
        {
            if (!playerPresentation.TryGetValue(playerId,
                    out ServerPresentationState state))
            {
                throw new ArgumentOutOfRangeException(nameof(playerId));
            }
            ConfigurePlayerPresentation(playerId, appearanceId, state.WeaponId);
        }

        public AuthoritativeTickResult ServerStep()
        {
            RequireServerWrite();
            if (simulation == null)
            {
                throw new InvalidOperationException(
                    "ConfigureServer must be called before ticking.");
            }

            PlayerInputCommand[] commands = pendingCommands.ToArray();
            pendingCommands.Clear();
            AuthoritativeEconomyCommand[] economyCommands =
                pendingEconomyCommands.ToArray();
            pendingEconomyCommands.Clear();
            AuthoritativeMissionCommand[] missionCommands =
                pendingMissionCommands.ToArray();
            pendingMissionCommands.Clear();
            lastResult = simulation.Step(
                commands, economyCommands, missionCommands);
            ApplyAcceptedPresentationInputs(lastResult.Commands);
            PublishShotEvents(lastResult);
            pendingPresentationInputs.Clear();
            lastSnapshot = lastResult.Snapshot;
            PublishSnapshot(lastResult.Snapshot, lastResult.Events);
            ServerTickCompleted?.Invoke(lastResult);
            return lastResult;
        }

        public bool TryRestartMission(ulong senderClientId)
        {
            RequireServerWrite();
            if (!playerByClient.TryGetValue(senderClientId, out int playerId) ||
                playerId != 1 || configuredRules == null ||
                configuredPlayers.Length == 0 ||
                configuredTargets.Length == 0 || simulation == null ||
                (lastSnapshot?.Mission?.Phase !=
                     AuthoritativeMissionPhase.Victory &&
                 lastSnapshot?.Mission?.Phase !=
                     AuthoritativeMissionPhase.Defeat))
                return false;
            runGeneration++;
            simulation = new AuthoritativeCoopSimulation(
                configuredRules,
                configuredPlayers,
                configuredTargets,
                configuredRequiredKills,
                configuredMission: configuredMission,
                configuredWaves: configuredWaves);
            simulation.InitializePlayerConnections(
                playerByClient.Values.Distinct());
            simulation.SetStandingClearanceValidator(HasStandingClearance);
            simulation.SetShotObstructionResolver(ResolveShotObstruction);
            simulation.SetPlayerMovementResolver(ResolvePlayerMovement);
            simulation.SetEnemyMovementResolver(ResolveEnemyMovement);
            pendingCommands.Clear();
            pendingEconomyCommands.Clear();
            pendingMissionCommands.Clear();
            pendingPresentationInputs.Clear();
            if (IsSpawned && IsServer) authorityEvents.Clear();
            lastResult = null;
            lastSnapshot = simulation.CaptureSnapshot();
            PublishSnapshot(lastSnapshot, Array.Empty<AuthoritativeEvent>());
            return true;
        }

        public int SpawnServerWorldDrop(
            string itemId,
            int quantity,
            Vector3 position,
            int ownerPlayerId = 0)
        {
            RequireServerWrite();
            if (simulation == null)
                throw new InvalidOperationException(
                    "ConfigureServer must be called before spawning drops.");
            int dropId = simulation.SpawnServerWorldDrop(
                itemId, quantity, NetcodeConversions.ToDomain(position),
                ownerPlayerId);
            lastSnapshot = simulation.CaptureSnapshot();
            PublishSnapshot(lastSnapshot,
                Array.Empty<AuthoritativeEvent>());
            return dropId;
        }

        public int GrantServerExperience(int playerId, int amount)
        {
            RequireServerWrite();
            if (simulation == null)
                throw new InvalidOperationException(
                    "ConfigureServer must be called before granting XP.");
            int levels = simulation.GrantServerExperience(playerId, amount);
            lastSnapshot = simulation.CaptureSnapshot();
            PublishSnapshot(lastSnapshot,
                Array.Empty<AuthoritativeEvent>());
            return levels;
        }

        public IReadOnlyList<AuthoritativeEvent> ApplyServerDamageToPlayer(
            int playerId,
            double damage)
        {
            RequireServerWrite();
            if (simulation == null)
            {
                throw new InvalidOperationException(
                    "ConfigureServer must be called before applying damage.");
            }

            IReadOnlyList<AuthoritativeEvent> events =
                simulation.ApplyServerDamageToPlayer(playerId, damage);
            lastSnapshot = simulation.CaptureSnapshot();
            PublishSnapshot(lastSnapshot, events);
            return events;
        }

        public void SetServerTargetPosition(int targetId, Vector3 position)
        {
            RequireServerWrite();
            if (simulation == null)
            {
                throw new InvalidOperationException(
                    "ConfigureServer must be called before moving a target.");
            }

            simulation.SetAuthoritativeTargetPosition(
                targetId,
                NetcodeConversions.ToDomain(position));
            lastSnapshot = simulation.CaptureSnapshot();
            PublishSnapshot(lastSnapshot,
                Array.Empty<AuthoritativeEvent>());
        }

        private bool HasStandingClearance(int _, NetVector3 position)
        {
            Vector3 feet = NetcodeConversions.ToUnity(position);
            const float radius = 0.28f;
            const float standingHeight = 1.8f;
            Vector3 bottom = feet + Vector3.up * (radius + 0.05f);
            Vector3 top = feet + Vector3.up *
                (standingHeight - radius - 0.05f);
            int count = Physics.OverlapCapsuleNonAlloc(
                bottom,
                top,
                radius,
                standingOverlaps,
                ~0,
                QueryTriggerInteraction.Ignore);
            for (int index = 0; index < count; index++)
            {
                Collider candidate = standingOverlaps[index];
                if (candidate == null ||
                    candidate.transform.IsChildOf(transform)) continue;
                if (candidate.GetComponentInParent<CharacterController>() !=
                    null) continue;
                NetworkPlayerReplica replica =
                    candidate.GetComponentInParent<NetworkPlayerReplica>();
                if (replica != null && replica.Session == this) continue;
                return false;
            }
            return true;
        }

        private PlayerMovementState ResolvePlayerMovement(
            int _,
            PlayerMovementState current,
            PlayerMovementState desired)
        {
            Vector3 from = NetcodeConversions.ToUnity(current.Position);
            Vector3 to = NetcodeConversions.ToUnity(desired.Position);
            Vector3 horizontal = new(to.x - from.x, 0f, to.z - from.z);
            if (horizontal.sqrMagnitude <= 0.00000001f)
                return desired;

            Vector3 resolvedHorizontal = ResolveCapsuleDisplacement(
                from, horizontal);
            Vector3 resolvedPosition = from + resolvedHorizontal;
            resolvedPosition.y = to.y;
            return new PlayerMovementState(
                NetcodeConversions.ToDomain(resolvedPosition),
                desired.Velocity,
                desired.AimYawDegrees,
                desired.AimPitchDegrees,
                desired.Stance,
                desired.Grounded,
                desired.LastJumpTick,
                desired.GroundHeight);
        }

        private Vector3 ResolveCapsuleDisplacement(
            Vector3 origin,
            Vector3 requestedDisplacement)
        {
            const float radius = 0.28f;
            const float height = 1.8f;
            const float skin = 0.03f;
            Vector3 resolved = Vector3.zero;
            Vector3 remaining = requestedDisplacement;

            // Resolve the initial impact and one secondary corner impact.
            // A hard clamp makes a player stick to walls whenever input has
            // even a small component into the surface; projecting the
            // remainder onto the contact plane gives standard FPS wall slide.
            for (int pass = 0; pass < 2; pass++)
            {
                float distance = remaining.magnitude;
                if (distance <= 0.0001f) break;
                Vector3 direction = remaining / distance;
                Vector3 cursor = origin + resolved;
                Vector3 bottom = cursor + Vector3.up * (radius + skin);
                Vector3 top = cursor +
                    Vector3.up * (height - radius - skin);
                int count = Physics.CapsuleCastNonAlloc(
                    bottom,
                    top,
                    radius,
                    direction,
                    playerMovementHits,
                    distance + skin,
                    Physics.DefaultRaycastLayers,
                    QueryTriggerInteraction.Ignore);
                RaycastHit? nearest = null;
                for (int index = 0; index < count; index++)
                {
                    RaycastHit hit = playerMovementHits[index];
                    if (!IsWorldMovementObstacle(hit.collider)) continue;
                    if (nearest == null ||
                        hit.distance < nearest.Value.distance)
                        nearest = hit;
                }
                if (nearest == null)
                {
                    resolved += remaining;
                    break;
                }

                float allowed = Mathf.Max(0f,
                    nearest.Value.distance - skin);
                Vector3 advanced = direction *
                    Mathf.Min(distance, allowed);
                resolved += advanced;
                remaining -= advanced;

                Vector3 surfaceNormal = nearest.Value.normal;
                surfaceNormal.y = 0f;
                if (surfaceNormal.sqrMagnitude <= 0.0001f) break;
                surfaceNormal.Normalize();
                remaining = Vector3.ProjectOnPlane(
                    remaining, surfaceNormal);
            }
            return resolved;
        }

        private static bool IsWorldMovementObstacle(Collider candidate)
        {
            if (candidate == null) return false;
            if (candidate.GetComponentInParent<NetworkPlayerReplica>() != null)
                return false;
            if (candidate.GetComponentInParent<CharacterController>() != null)
                return false;
            return true;
        }

        private NetVector3 ResolveEnemyMovement(
            int _,
            NetVector3 current,
            NetVector3 destination,
            double maximumTravel)
        {
            enemyPath ??= new NavMeshPath();
            Vector3 start = NetcodeConversions.ToUnity(current);
            Vector3 end = NetcodeConversions.ToUnity(destination);
            if (!NavMesh.SamplePosition(start, out NavMeshHit startHit,
                    2f, NavMesh.AllAreas) ||
                !NavMesh.SamplePosition(end, out NavMeshHit endHit,
                    4f, NavMesh.AllAreas))
                return AdvanceEnemyFallback(current, destination,
                    maximumTravel);

            if (!NavMesh.CalculatePath(
                    startHit.position,
                    endHit.position,
                    NavMesh.AllAreas,
                    enemyPath))
                return current;
            int cornerCount = enemyPath.GetCornersNonAlloc(enemyPathCorners);
            if (cornerCount < 2)
                return current;

            float remaining = Mathf.Max(0f, (float)maximumTravel);
            Vector3 cursor = startHit.position;
            for (int index = 1;
                 index < cornerCount && remaining > 0.0001f;
                 index++)
            {
                Vector3 delta = enemyPathCorners[index] - cursor;
                float segment = delta.magnitude;
                if (segment <= remaining)
                {
                    cursor = enemyPathCorners[index];
                    remaining -= segment;
                    continue;
                }
                cursor += delta / segment * remaining;
                remaining = 0f;
            }

            return NavMesh.SamplePosition(cursor, out NavMeshHit resolved,
                    0.75f, NavMesh.AllAreas)
                ? NetcodeConversions.ToDomain(resolved.position)
                : current;
        }

        private static NetVector3 AdvanceEnemyFallback(
            NetVector3 current,
            NetVector3 destination,
            double maximumTravel)
        {
            NetVector3 delta = destination - current;
            double travel = Math.Max(0d,
                Math.Min(maximumTravel, delta.Magnitude));
            return delta.SqrMagnitude > 0.0000001d
                ? current + delta.Normalized * travel
                : current;
        }

        private AuthoritativeShotObstruction ResolveShotObstruction(
            int shooterPlayerId,
            NetVector3 origin,
            NetVector3 endPoint)
        {
            Vector3 start = NetcodeConversions.ToUnity(origin);
            Vector3 end = NetcodeConversions.ToUnity(endPoint);
            Vector3 delta = end - start;
            float distance = delta.magnitude;
            if (distance <= 0.001f)
                return AuthoritativeShotObstruction.Clear;
            int count = Physics.RaycastNonAlloc(
                start,
                delta / distance,
                shotObstructionHits,
                distance - 0.01f,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);
            RaycastHit? nearest = null;
            for (int index = 0; index < count; index++)
            {
                RaycastHit hit = shotObstructionHits[index];
                Collider candidate = hit.collider;
                if (candidate == null) continue;
                NetworkPlayerReplica replica =
                    candidate.GetComponentInParent<NetworkPlayerReplica>();
                if (replica != null)
                    continue;
                if (nearest == null || hit.distance < nearest.Value.distance)
                    nearest = hit;
            }
            if (nearest == null) return AuthoritativeShotObstruction.Clear;
            RaycastHit obstruction = nearest.Value;
            return AuthoritativeShotObstruction.At(
                NetcodeConversions.ToDomain(obstruction.point),
                NetcodeConversions.ToDomain(obstruction.normal),
                ResolveAuthoritativeSurface(obstruction.collider));
        }

        private static AuthoritativeSurface ResolveAuthoritativeSurface(
            Collider collider)
        {
            if (collider == null) return AuthoritativeSurface.Concrete;
            string objectName = collider.gameObject.name.ToLowerInvariant();
            Renderer renderer = collider.GetComponentInParent<Renderer>();
            string materialName = renderer != null &&
                                  renderer.sharedMaterial != null
                ? renderer.sharedMaterial.name.ToLowerInvariant()
                : string.Empty;
            return objectName.Contains("metal") ||
                   materialName.Contains("metal")
                ? AuthoritativeSurface.Metal
                : AuthoritativeSurface.Concrete;
        }

        public bool TryGetPlayerState(int playerId, out NetcodePlayerState state)
        {
            for (int index = 0; index < playerStates.Count; index++)
            {
                if (playerStates[index].PlayerId == playerId)
                {
                    state = playerStates[index];
                    return true;
                }
            }

            AuthoritativeWorldSnapshot snapshot = LastAuthoritativeSnapshot;
            if (snapshot != null)
            {
                for (int index = 0; index < snapshot.Players.Count; index++)
                {
                    if (snapshot.Players[index].PlayerId == playerId)
                    {
                        state = NetcodePlayerState.FromDomain(
                            snapshot.Tick,
                            snapshot.Players[index],
                            snapshot.Mission.Player(playerId));
                        ApplyPresentationState(ref state);
                        return true;
                    }
                }
            }

            state = default;
            return false;
        }

        public int GetPresentationEventsAfter(
            int playerId,
            long afterSequence,
            List<NetcodePresentationEvent> destination)
        {
            if (destination == null)
                throw new ArgumentNullException(nameof(destination));
            destination.Clear();
            if (IsSpawned)
            {
                for (int index = 0; index < presentationEvents.Count; index++)
                {
                    NetcodePresentationEvent value = presentationEvents[index];
                    if (value.PlayerId == playerId &&
                        value.Sequence > afterSequence)
                        destination.Add(value);
                }
            }
            else
            {
                for (int index = 0;
                     index < offlinePresentationEvents.Count;
                     index++)
                {
                    NetcodePresentationEvent value =
                        offlinePresentationEvents[index];
                    if (value.PlayerId == playerId &&
                        value.Sequence > afterSequence)
                        destination.Add(value);
                }
            }
            destination.Sort((left, right) =>
                left.Sequence.CompareTo(right.Sequence));
            return destination.Count;
        }

        public int GetShotEventsAfter(
            int shooterPlayerId,
            long afterSequence,
            List<NetcodeShotFeedbackEvent> destination)
        {
            if (destination == null)
                throw new ArgumentNullException(nameof(destination));
            destination.Clear();
            if (IsSpawned)
            {
                for (int index = 0; index < shotEvents.Count; index++)
                {
                    NetcodeShotFeedbackEvent value = shotEvents[index];
                    if (value.ShooterPlayerId == shooterPlayerId &&
                        value.Sequence > afterSequence)
                        destination.Add(value);
                }
            }
            else
            {
                for (int index = 0; index < offlineShotEvents.Count; index++)
                {
                    NetcodeShotFeedbackEvent value = offlineShotEvents[index];
                    if (value.ShooterPlayerId == shooterPlayerId &&
                        value.Sequence > afterSequence)
                        destination.Add(value);
                }
            }
            destination.Sort((left, right) =>
                left.Sequence.CompareTo(right.Sequence));
            return destination.Count;
        }

        public NetcodeTargetState GetReplicatedTarget(int index) => IsSpawned
            ? targetStates[index]
            : NetcodeTargetState.FromDomain(
                LastAuthoritativeSnapshot.Targets[index]);
        public NetcodeAuthorityEvent GetReplicatedEvent(int index) =>
            authorityEvents[index];

        public NetcodeInventorySlotState GetReplicatedInventorySlot(int index)
        {
            if (IsSpawned) return inventoryStates[index];
            return NetcodeInventorySlotState.FromDomain(
                LastAuthoritativeSnapshot.Economy.InventorySlots[index]);
        }

        public NetcodeWorldDropState GetReplicatedWorldDrop(int index)
        {
            if (IsSpawned) return worldDropStates[index];
            return NetcodeWorldDropState.FromDomain(
                LastAuthoritativeSnapshot.Economy.WorldDrops[index]);
        }

        public NetcodeProgressionState GetReplicatedProgression(int index)
        {
            if (IsSpawned) return progressionStates[index];
            return NetcodeProgressionState.FromDomain(
                LastAuthoritativeSnapshot.Economy.Progression[index]);
        }

        public NetcodeUpgradeStackState GetReplicatedUpgrade(int index)
        {
            if (IsSpawned) return upgradeStates[index];
            return NetcodeUpgradeStackState.FromDomain(
                LastAuthoritativeSnapshot.Economy.Upgrades[index]);
        }

        public bool TryGetProgression(
            int playerId,
            out NetcodeProgressionState state)
        {
            for (int index = 0; index < ReplicatedProgressionCount; index++)
            {
                NetcodeProgressionState candidate =
                    GetReplicatedProgression(index);
                if (candidate.PlayerId != playerId) continue;
                state = candidate;
                return true;
            }
            state = default;
            return false;
        }

        /// <summary>Enables an offline authoritative fixture without NGO.</summary>
        public void EnableServerTestHook()
        {
            if (IsSpawned)
            {
                throw new InvalidOperationException(
                    "The offline authority hook cannot be enabled after spawn.");
            }

            testServerAuthority = true;
            autoSimulate = false;
        }

        public void ResetServerTestHook()
        {
            pendingCommands.Clear();
            pendingEconomyCommands.Clear();
            pendingMissionCommands.Clear();
            pendingPresentationInputs.Clear();
            playerByClient.Clear();
            clientByPlayer.Clear();
            playerPresentation.Clear();
            offlinePresentationEvents.Clear();
            offlineShotEvents.Clear();
            simulation = null;
            lastResult = null;
            lastSnapshot = null;
            testServerAuthority = false;
            accumulatedSeconds = 0d;
            nextPresentationEventSequence = 0;
            nextShotEventSequence = 0;
            configuredRules = null;
            configuredPlayers = Array.Empty<CoopPlayerSpawn>();
            configuredTargets = Array.Empty<CoopTargetSpawn>();
            configuredMission = null;
            configuredRequiredKills = 0;
            runGeneration = 1;
        }

        private bool CanServerWrite => IsServer || testServerAuthority ||
            (!IsSpawned && Unity.Netcode.NetworkManager.Singleton != null &&
                Unity.Netcode.NetworkManager.Singleton.IsServer);

        private void RequireServerWrite()
        {
            if (!CanServerWrite)
            {
                throw new InvalidOperationException(
                    "Authoritative state can only be changed by the server.");
            }
        }

        private void ClearPendingCommands(int playerId)
        {
            pendingCommands.RemoveAll(value => value.PlayerId == playerId);
            pendingEconomyCommands.RemoveAll(value =>
                value.PlayerId == playerId);
            pendingMissionCommands.RemoveAll(value =>
                value.PlayerId == playerId);
            foreach (var key in new List<(int playerId, uint sequence)>(
                         pendingPresentationInputs.Keys))
                if (key.playerId == playerId)
                    pendingPresentationInputs.Remove(key);
        }

        private void PublishSnapshot(
            AuthoritativeWorldSnapshot snapshot,
            IReadOnlyList<AuthoritativeEvent> events)
        {
            if (!IsSpawned || !IsServer)
            {
                worldState.Reset(BuildWorldState(snapshot,
                    worldState.Value.LastEventSequence,
                    worldState.Value.EconomyRevision + 1));
                return;
            }

            long lastEventSequence = worldState.Value.LastEventSequence;
            for (int index = 0; index < snapshot.Players.Count; index++)
            {
                NetcodePlayerState player = NetcodePlayerState.FromDomain(
                    snapshot.Tick,
                    snapshot.Players[index],
                    snapshot.Mission.Player(
                        snapshot.Players[index].PlayerId));
                ApplyPresentationState(ref player);
                int existingIndex = FindReplicatedPlayerIndex(player.PlayerId);
                if (existingIndex >= 0) playerStates[existingIndex] = player;
                else playerStates.Add(player);
            }
            for (int index = playerStates.Count - 1; index >= 0; index--)
            {
                bool found = false;
                for (int playerIndex = 0;
                     playerIndex < snapshot.Players.Count;
                     playerIndex++)
                {
                    if (snapshot.Players[playerIndex].PlayerId !=
                        playerStates[index].PlayerId) continue;
                    found = true;
                    break;
                }
                if (!found) playerStates.RemoveAt(index);
            }

            for (int index = 0; index < snapshot.Targets.Count; index++)
            {
                NetcodeTargetState replicated =
                    NetcodeTargetState.FromDomain(snapshot.Targets[index]);
                if (runGeneration > 1)
                    replicated.SpawnGeneration +=
                        (runGeneration - 1) * 1000;
                if (index >= targetStates.Count)
                {
                    targetStates.Add(replicated);
                }
                else if (!targetStates[index].Equals(replicated))
                {
                    targetStates[index] = replicated;
                }
            }
            while (targetStates.Count > snapshot.Targets.Count)
            {
                targetStates.RemoveAt(targetStates.Count - 1);
            }

            AuthoritativeEconomySnapshot economy = snapshot.Economy;
            for (int index = 0; index < economy.InventorySlots.Count; index++)
            {
                NetcodeInventorySlotState replicated =
                    NetcodeInventorySlotState.FromDomain(
                        economy.InventorySlots[index]);
                if (index >= inventoryStates.Count)
                    inventoryStates.Add(replicated);
                else if (!inventoryStates[index].Equals(replicated))
                    inventoryStates[index] = replicated;
            }
            while (inventoryStates.Count > economy.InventorySlots.Count)
                inventoryStates.RemoveAt(inventoryStates.Count - 1);

            for (int index = 0; index < economy.WorldDrops.Count; index++)
            {
                NetcodeWorldDropState replicated =
                    NetcodeWorldDropState.FromDomain(economy.WorldDrops[index]);
                if (index >= worldDropStates.Count)
                    worldDropStates.Add(replicated);
                else if (!worldDropStates[index].Equals(replicated))
                    worldDropStates[index] = replicated;
            }
            while (worldDropStates.Count > economy.WorldDrops.Count)
                worldDropStates.RemoveAt(worldDropStates.Count - 1);

            for (int index = 0; index < economy.Progression.Count; index++)
            {
                NetcodeProgressionState replicated =
                    NetcodeProgressionState.FromDomain(
                        economy.Progression[index]);
                if (index >= progressionStates.Count)
                    progressionStates.Add(replicated);
                else if (!progressionStates[index].Equals(replicated))
                    progressionStates[index] = replicated;
            }
            while (progressionStates.Count > economy.Progression.Count)
                progressionStates.RemoveAt(progressionStates.Count - 1);

            for (int index = 0; index < economy.Upgrades.Count; index++)
            {
                NetcodeUpgradeStackState replicated =
                    NetcodeUpgradeStackState.FromDomain(economy.Upgrades[index]);
                if (index >= upgradeStates.Count)
                    upgradeStates.Add(replicated);
                else if (!upgradeStates[index].Equals(replicated))
                    upgradeStates[index] = replicated;
            }
            while (upgradeStates.Count > economy.Upgrades.Count)
                upgradeStates.RemoveAt(upgradeStates.Count - 1);

            for (int index = 0; index < events.Count; index++)
            {
                NetcodeAuthorityEvent replicated =
                    NetcodeAuthorityEvent.FromDomain(events[index]);
                authorityEvents.Add(replicated);
                lastEventSequence = replicated.Sequence;
            }

            while (authorityEvents.Count > MaximumReplicatedEvents)
            {
                authorityEvents.RemoveAt(0);
            }

            worldState.Value = BuildWorldState(snapshot, lastEventSequence,
                worldState.Value.EconomyRevision + 1);
        }

        private NetcodeWorldState BuildWorldState(
            AuthoritativeWorldSnapshot snapshot,
            long lastEventSequence,
            int economyRevision)
        {
            AuthoritativeMissionState mission = snapshot.Mission;
            AuthoritativeMissionDefinition definition = mission.Definition;
            return new NetcodeWorldState
            {
                ServerTick = snapshot.Tick,
                WaveStatus = snapshot.WaveStatus,
                KilledTargets = snapshot.KilledTargets,
                RequiredKills = snapshot.RequiredKills,
                EnemyPoolCapacity = snapshot.EnemyPoolCapacity,
                ActiveEnemyCount = snapshot.ActiveTargets,
                PendingEnemyCount = snapshot.PendingTargets,
                RemainingEnemyCount = snapshot.RemainingTargets,
                CurrentWave = snapshot.CurrentWave,
                TotalWaves = snapshot.TotalWaves,
                WaveSpawnedCount = snapshot.WaveSpawned,
                WaveTotalCount = snapshot.WaveTotal,
                WaveMaximumAlive = snapshot.WaveMaximumAlive,
                IntermissionRemainingTicks =
                    snapshot.IntermissionRemainingTicks,
                LastEventSequence = lastEventSequence,
                EconomyRevision = economyRevision,
                RunGeneration = runGeneration,
                MissionPhase = mission.Phase,
                MissionOutcomeReason = mission.OutcomeReason,
                MissionRevision = mission.Revision,
                TerminalProgressTicks = mission.TerminalProgressTicks,
                TerminalRequiredTicks = definition.TerminalHoldTicks,
                ExtractionProgressTicks = mission.ExtractionProgressTicks,
                ExtractionRequiredTicks = definition.ExtractionHoldTicks,
                ReviveProgressTicks = mission.ReviveProgressTicks,
                ReviveRequiredTicks = definition.ReviveHoldTicks,
                TerminalPlayerId = mission.TerminalPlayerId,
                RevivePlayerId = mission.RevivePlayerId,
                DownedPlayerId = mission.DownedPlayerId,
                TerminalPosition = NetcodeConversions.ToUnity(
                    definition.TerminalPosition),
                ExtractionPosition = NetcodeConversions.ToUnity(
                    definition.ExtractionPosition),
                TerminalRadius = (float)definition.TerminalRadius,
                ExtractionRadius = (float)definition.ExtractionRadius,
                ReviveRadius = (float)definition.ReviveRadius,
                SnapshotPlayerCount = snapshot.Players.Count,
                SnapshotTargetCount = snapshot.Targets.Count,
                SnapshotInventoryCount = snapshot.Economy.InventorySlots.Count,
                SnapshotDropCount = snapshot.Economy.WorldDrops.Count,
                SnapshotProgressionCount = snapshot.Economy.Progression.Count,
                SnapshotUpgradeCount = snapshot.Economy.Upgrades.Count
            };
        }

        private void ApplyAcceptedPresentationInputs(
            IReadOnlyList<CommandResolution> resolutions)
        {
            for (int index = 0; index < resolutions.Count; index++)
            {
                CommandResolution resolution = resolutions[index];
                PlayerInputCommand command = resolution.Command;
                if (!pendingPresentationInputs.TryGetValue(
                        (command.PlayerId, command.Sequence),
                        out NetcodePlayerCommand payload) ||
                    !resolution.Accepted ||
                    !playerPresentation.TryGetValue(command.PlayerId,
                        out ServerPresentationState state))
                {
                    continue;
                }

                state.Sprinting = payload.SprintHeld &&
                    !payload.CrouchRequested && payload.MoveZ > 0.1f;
                state.Aiming = payload.AimingHeld && !state.Sprinting;
                playerPresentation[command.PlayerId] = state;
                if (command.JumpPressed)
                    EmitPresentationEvent(command.PlayerId,
                        NetworkPresentationAction.Jump, state.WeaponId);
                if (command.Fire &&
                    resolution.Shot.Kind != ShotResolutionKind.NotRequested)
                    EmitPresentationEvent(command.PlayerId,
                        NetworkPresentationAction.Shoot, state.WeaponId);
            }
        }

        private void PublishShotEvents(AuthoritativeTickResult result)
        {
            for (int index = 0; index < result.Commands.Count; index++)
            {
                CommandResolution resolution = result.Commands[index];
                if (!resolution.Accepted ||
                    resolution.Shot.Kind == ShotResolutionKind.NotRequested)
                    continue;
                var value = NetcodeShotFeedbackEvent.FromDomain(
                    result.Tick,
                    ++nextShotEventSequence,
                    resolution.Command.PlayerId,
                    resolution.Shot);
                offlineShotEvents.Add(value);
                while (offlineShotEvents.Count > MaximumShotEvents)
                    offlineShotEvents.RemoveAt(0);
                if (playerPresentation.TryGetValue(
                        resolution.Command.PlayerId,
                        out ServerPresentationState state))
                {
                    state.LastShotEventSequence = value.Sequence;
                    playerPresentation[resolution.Command.PlayerId] = state;
                }
                if (!IsSpawned || !IsServer) continue;
                shotEvents.Add(value);
                while (shotEvents.Count > MaximumShotEvents)
                    shotEvents.RemoveAt(0);
            }
        }

        private void EmitPresentationEvent(
            int playerId,
            NetworkPresentationAction action,
            string weaponId)
        {
            var value = new NetcodePresentationEvent
            {
                ServerTick = simulation?.CurrentTick ?? 0,
                Sequence = ++nextPresentationEventSequence,
                PlayerId = playerId,
                Action = action,
                WeaponId = NetworkPresentationIds.ResolveWeaponOrDefault(
                    weaponId)
            };
            offlinePresentationEvents.Add(value);
            while (offlinePresentationEvents.Count >
                   MaximumPresentationEvents)
                offlinePresentationEvents.RemoveAt(0);
            if (playerPresentation.TryGetValue(playerId,
                    out ServerPresentationState state))
            {
                state.LastEventSequence = value.Sequence;
                playerPresentation[playerId] = state;
            }
            if (!IsSpawned || !IsServer) return;
            presentationEvents.Add(value);
            while (presentationEvents.Count > MaximumPresentationEvents)
                presentationEvents.RemoveAt(0);
        }

        private void ApplyPresentationState(ref NetcodePlayerState state)
        {
            if (!playerPresentation.TryGetValue(state.PlayerId,
                    out ServerPresentationState presentation))
                presentation = ServerPresentationState.Default;
            state.AppearanceId = presentation.AppearanceId;
            state.WeaponId = presentation.WeaponId;
            state.Sprinting = presentation.Sprinting;
            state.Aiming = presentation.Aiming;
            state.AcknowledgedPresentationCommandSequence =
                presentation.LastPresentationCommandSequence;
            state.LastPresentationEventSequence =
                presentation.LastEventSequence;
            state.LastShotEventSequence = presentation.LastShotEventSequence;
        }

        private void RefreshReplicatedPlayerState(int playerId)
        {
            if (!IsSpawned || !IsServer || lastSnapshot == null) return;
            for (int index = 0; index < lastSnapshot.Players.Count; index++)
            {
                if (lastSnapshot.Players[index].PlayerId != playerId) continue;
                NetcodePlayerState state = NetcodePlayerState.FromDomain(
                    lastSnapshot.Tick, lastSnapshot.Players[index]);
                ApplyPresentationState(ref state);
                int replicatedIndex = FindReplicatedPlayerIndex(playerId);
                if (replicatedIndex >= 0)
                    playerStates[replicatedIndex] = state;
                else
                    playerStates.Add(state);
                return;
            }
        }

        private int FindReplicatedPlayerIndex(int playerId)
        {
            for (int index = 0; index < playerStates.Count; index++)
            {
                if (playerStates[index].PlayerId == playerId) return index;
            }
            return -1;
        }

        private struct ServerPresentationState
        {
            public string AppearanceId;
            public string WeaponId;
            public bool Sprinting;
            public bool Aiming;
            public uint LastPresentationCommandSequence;
            public long LastEventSequence;
            public long LastShotEventSequence;

            public static ServerPresentationState Default => new()
            {
                AppearanceId = NetworkPresentationIds.DefaultAppearance,
                WeaponId = NetworkPresentationIds.DefaultWeapon
            };
        }
    }
}
