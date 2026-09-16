using System;
using System.Collections.Generic;
using FPS.Networking.Domain;
using Unity.Netcode;
using UnityEngine;

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

        private readonly Dictionary<ulong, int> playerByClient = new();
        private readonly List<PlayerInputCommand> pendingCommands = new();
        private readonly Dictionary<(int playerId, uint sequence),
            NetcodePlayerCommand> pendingPresentationInputs = new();
        private readonly Dictionary<int, ServerPresentationState>
            playerPresentation = new();
        private readonly List<NetcodePresentationEvent>
            offlinePresentationEvents = new();
        private AuthoritativeCoopSimulation simulation;
        private AuthoritativeTickResult lastResult;
        private AuthoritativeWorldSnapshot lastSnapshot;
        private double accumulatedSeconds;
        private bool testServerAuthority;
        private long nextPresentationEventSequence;
        private readonly Collider[] standingOverlaps = new Collider[16];

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
            pendingPresentationInputs.Clear();
            playerPresentation.Clear();
            offlinePresentationEvents.Clear();
            nextPresentationEventSequence = 0;
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
            if (payload.Action == NetworkPresentationAction.SwitchWeapon)
            {
                if (!NetworkPresentationIds.TryResolveWeapon(
                        payload.WeaponId.ToString(), out weaponId))
                {
                    return false;
                }
                state.WeaponId = weaponId;
            }

            state.LastPresentationCommandSequence = payload.Sequence;
            playerPresentation[playerId] = state;
            EmitPresentationEvent(playerId, payload.Action, weaponId);
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
            lastResult = simulation.Step(commands);
            ApplyAcceptedPresentationInputs(lastResult.Commands);
            pendingPresentationInputs.Clear();
            lastSnapshot = lastResult.Snapshot;
            PublishSnapshot(lastResult.Snapshot, lastResult.Events);
            ServerTickCompleted?.Invoke(lastResult);
            return lastResult;
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

        public NetcodeTargetState GetReplicatedTarget(int index) =>
            targetStates[index];
        public NetcodeAuthorityEvent GetReplicatedEvent(int index) =>
            authorityEvents[index];

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
            pendingPresentationInputs.Clear();
            playerByClient.Clear();
            playerPresentation.Clear();
            offlinePresentationEvents.Clear();
            simulation = null;
            lastResult = null;
            lastSnapshot = null;
            testServerAuthority = false;
            accumulatedSeconds = 0d;
            nextPresentationEventSequence = 0;
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

            targetStates.Clear();
            for (int index = 0; index < snapshot.Targets.Count; index++)
            {
                targetStates.Add(NetcodeTargetState.FromDomain(
                    snapshot.Targets[index]));
            }

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
                LastEventSequence = lastEventSequence
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
                if (command.Fire)
                    EmitPresentationEvent(command.PlayerId,
                        NetworkPresentationAction.Shoot, state.WeaponId);
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

            public static ServerPresentationState Default => new()
            {
                AppearanceId = NetworkPresentationIds.DefaultAppearance,
                WeaponId = NetworkPresentationIds.DefaultWeapon
            };
        }
    }
}
