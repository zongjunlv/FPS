using System;
using System.Collections.Generic;
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
        private readonly List<PlayerInputCommand> pendingCommands = new();
        private readonly List<AuthoritativeEconomyCommand>
            pendingEconomyCommands = new();
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
        private readonly Collider[] standingOverlaps = new Collider[16];
        private readonly RaycastHit[] shotObstructionHits = new RaycastHit[32];

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
        public int ReplicatedPlayerCount => playerStates.Count;
        public int ReplicatedTargetCount => targetStates.Count;
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
            int requiredKills = 0)
        {
            RequireServerWrite();
            simulation = new AuthoritativeCoopSimulation(
                rules,
                players,
                targets,
                requiredKills);
            simulation.SetStandingClearanceValidator(HasStandingClearance);
            simulation.SetShotObstructionResolver(ResolveShotObstruction);
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

        public void RegisterPlayerClient(ulong clientId, int playerId)
        {
            RequireServerWrite();
            if (playerId <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(playerId));
            }

            playerByClient[clientId] = playerId;
        }

        public void UnregisterPlayerClient(ulong clientId)
        {
            RequireServerWrite();
            playerByClient.Remove(clientId);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone,
            Delivery = RpcDelivery.Unreliable)]
        public void SubmitInputRpc(
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
            lastResult = simulation.Step(commands, economyCommands);
            ApplyAcceptedPresentationInputs(lastResult.Commands);
            PublishShotEvents(lastResult);
            pendingPresentationInputs.Clear();
            lastSnapshot = lastResult.Snapshot;
            PublishSnapshot(lastResult.Snapshot, lastResult.Events);
            ServerTickCompleted?.Invoke(lastResult);
            return lastResult;
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

        private static NetVector3 ResolveEnemyMovement(
            int _,
            NetVector3 current,
            NetVector3 desired)
        {
            Vector3 start = NetcodeConversions.ToUnity(current);
            Vector3 end = NetcodeConversions.ToUnity(desired);
            if (!NavMesh.SamplePosition(start, out NavMeshHit startHit,
                    1.5f, NavMesh.AllAreas) ||
                !NavMesh.SamplePosition(end, out NavMeshHit endHit,
                    1.5f, NavMesh.AllAreas))
                return desired;

            if (!NavMesh.Raycast(startHit.position, endHit.position,
                    out NavMeshHit obstruction, NavMesh.AllAreas))
                return NetcodeConversions.ToDomain(endHit.position);

            Vector3 step = endHit.position - startHit.position;
            Vector3 slide = Vector3.ProjectOnPlane(step, obstruction.normal);
            Vector3 candidate = startHit.position + slide;
            return NavMesh.SamplePosition(candidate, out NavMeshHit slideHit,
                    0.75f, NavMesh.AllAreas)
                ? NetcodeConversions.ToDomain(slideHit.position)
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
                            snapshot.Players[index]);
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

        public NetcodeTargetState GetReplicatedTarget(int index) =>
            targetStates[index];
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
            pendingPresentationInputs.Clear();
            playerByClient.Clear();
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

        private void PublishSnapshot(
            AuthoritativeWorldSnapshot snapshot,
            IReadOnlyList<AuthoritativeEvent> events)
        {
            if (!IsSpawned || !IsServer)
            {
                return;
            }

            long lastEventSequence = worldState.Value.LastEventSequence;
            for (int index = 0; index < snapshot.Players.Count; index++)
            {
                NetcodePlayerState player = NetcodePlayerState.FromDomain(
                    snapshot.Tick,
                    snapshot.Players[index]);
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

            worldState.Value = new NetcodeWorldState
            {
                ServerTick = snapshot.Tick,
                WaveStatus = snapshot.WaveStatus,
                KilledTargets = snapshot.KilledTargets,
                RequiredKills = snapshot.RequiredKills,
                EnemyPoolCapacity = snapshot.EnemyPoolCapacity,
                ActiveEnemyCount = snapshot.ActiveTargets,
                PendingEnemyCount = snapshot.PendingTargets,
                RemainingEnemyCount = snapshot.RemainingTargets,
                LastEventSequence = lastEventSequence,
                EconomyRevision = worldState.Value.EconomyRevision + 1
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
