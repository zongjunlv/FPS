using System;
using System.Collections.Generic;
using FPS.Networking.Domain;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace FPS.Networking.Netcode
{
    /// <summary>
    /// Player presentation adapter: the owner predicts/reconciles and remote
    /// peers interpolate. Movement math is delegated to Networking.Domain.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class NetworkPlayerReplica : NetworkBehaviour
    {
        [SerializeField] private int playerId = 1;
        [SerializeField] private NetworkCoopSessionAuthority session;
        [SerializeField] private bool applyPositionToTransform = true;
        [SerializeField] private bool applyYawToTransform = true;

        private readonly NetworkVariable<int> replicatedPlayerId = new(
            1,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<FixedString64Bytes>
            replicatedAppearanceId = new(
                default,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server);

        private LocalPredictionBuffer prediction;
        private RemoteSnapshotInterpolator interpolation;
        private uint nextSequence = 1;
        private ulong nonceSalt = 0x65C00FUL;
        private uint nextPresentationSequence = 1;
        private long lastConsumedServerTick = -1;
        private long lastPresentationEventSequence;
        private bool presentationBaselineInitialized;
        private double estimatedServerTick;
        private bool ownerTestHook;
        private string currentAppearanceId =
            NetworkPresentationIds.DefaultAppearance;
        private string currentWeaponId = NetworkPresentationIds.DefaultWeapon;
        private readonly List<NetcodePresentationEvent>
            pendingPresentationEvents = new();

        public int PlayerId => playerId;
        public Vector3 PresentedPosition { get; private set; }
        public float PresentedAimYaw { get; private set; }
        public float PresentedAimPitch { get; private set; }
        public Vector3 PresentedVelocity { get; private set; }
        public bool PresentedCrouching { get; private set; }
        public bool PresentedGrounded { get; private set; } = true;
        public bool PresentedSprinting { get; private set; }
        public bool PresentedAiming { get; private set; }
        public bool PresentedAlive { get; private set; } = true;
        public int PredictionSampleCount { get; private set; }
        public int PredictionCorrectionCount { get; private set; }
        public double MaximumPredictionError { get; private set; }
        public double MeanPredictionError => PredictionSampleCount == 0
            ? 0d
            : predictionErrorSum / PredictionSampleCount;
        public PredictionCorrection LastPredictionCorrection { get; private set; }
        public RemoteInterpolationSample LastRemoteSample { get; private set; }
        public int PendingPredictionCount => prediction?.PendingCommands.Count ?? 0;
        public NetworkCoopSessionAuthority Session => session;
        public float PresentationSprintSpeed => session?.Rules == null
            ? 6f
            : (float)session.Rules.SprintSpeed;
        public string AppearanceId => currentAppearanceId;
        public string WeaponId => currentWeaponId;
        public long LastPresentationEventSequence =>
            lastPresentationEventSequence;
        public bool IsLocallyControlled => IsOwner || ownerTestHook;
        public bool IsPresentationReady => session != null &&
            session.Rules != null;
        public event Action<Vector3, float, float, bool, bool> PosePresented;
        public event Action<string> AppearanceChanged;
        public event Action<string> WeaponChanged;
        public event Action<NetworkPresentationAction>
            PresentationActionReceived;
        private double predictionErrorSum;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            replicatedAppearanceId.OnValueChanged += HandleAppearanceChanged;
            playerId = replicatedPlayerId.Value;
            ResolveSession();
            EnsurePresentationBuffers();
            ApplyOwnershipPolicy();
            currentAppearanceId = NetworkPresentationIds.ResolveAppearance(
                replicatedAppearanceId.Value.ToString());
            AppearanceChanged?.Invoke(AppearanceId);
        }

        public override void OnNetworkDespawn()
        {
            replicatedAppearanceId.OnValueChanged -= HandleAppearanceChanged;
            ResetPresentation();
            base.OnNetworkDespawn();
        }

        private void Update()
        {
            if (session == null || !session.TryGetPlayerState(
                    playerId,
                    out NetcodePlayerState state))
            {
                return;
            }

            estimatedServerTick = Math.Max(
                estimatedServerTick + Time.unscaledDeltaTime *
                    (session.Rules?.TickRate ?? 60),
                state.ServerTick);
            ConsumeServerState(state, IsLocallyControlled,
                estimatedServerTick);
        }

        public void Bind(
            NetworkCoopSessionAuthority sessionAuthority,
            int authoritativePlayerId)
        {
            session = sessionAuthority != null
                ? sessionAuthority
                : throw new ArgumentNullException(nameof(sessionAuthority));
            if (authoritativePlayerId <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(authoritativePlayerId));
            }

            playerId = authoritativePlayerId;
            ResetPresentation();
            if (!EnsurePresentationBuffers())
            {
                throw new InvalidOperationException(
                    "The server session must be configured before binding.");
            }
        }

        public void ConfigureServerIdentity(
            NetworkCoopSessionAuthority sessionAuthority,
            int authoritativePlayerId)
        {
            ConfigureServerIdentity(sessionAuthority, authoritativePlayerId,
                string.Empty);
        }

        public void ConfigureServerIdentity(
            NetworkCoopSessionAuthority sessionAuthority,
            int authoritativePlayerId,
            string appearanceId)
        {
            if (IsSpawned && !IsServer)
            {
                throw new InvalidOperationException(
                    "Only the server may configure a spawned replica.");
            }

            Bind(sessionAuthority, authoritativePlayerId);
            if (IsSpawned)
            {
                replicatedPlayerId.Value = authoritativePlayerId;
            }
            else
            {
                replicatedPlayerId.Reset(authoritativePlayerId);
            }
            ConfigureServerAppearance(appearanceId);
            ApplyOwnershipPolicy();
        }

        public void ConfigureServerAppearance(string appearanceId)
        {
            if (IsSpawned && !IsServer)
            {
                throw new InvalidOperationException(
                    "Only the server may configure player appearance.");
            }

            string safeAppearance = NetworkPresentationIds.ResolveAppearance(
                appearanceId);
            var normalized = new FixedString64Bytes(safeAppearance);
            if (IsSpawned)
            {
                if (replicatedAppearanceId.Value.Equals(normalized))
                    ApplyAppearance(normalized.ToString());
                else
                    replicatedAppearanceId.Value = normalized;
            }
            else
            {
                replicatedAppearanceId.Reset(normalized);
                ApplyAppearance(normalized.ToString());
            }
            if (session != null && session.IsConfigured)
                session.ConfigurePlayerAppearance(playerId, safeAppearance);
        }

        public NetcodePlayerCommand BuildPredictedCommand(
            float moveX,
            float moveZ,
            float aimYawDegrees,
            float aimPitchDegrees,
            bool fire,
            long clientTick)
        {
            return BuildPredictedCommand(moveX, moveZ, aimYawDegrees,
                aimPitchDegrees, fire, clientTick, jumpPressed: false,
                sprintHeld: false, crouchRequested: false);
        }

        public NetcodePlayerCommand BuildPredictedCommand(
            float moveX,
            float moveZ,
            float aimYawDegrees,
            float aimPitchDegrees,
            bool fire,
            long clientTick,
            bool jumpPressed,
            bool sprintHeld,
            bool crouchRequested)
        {
            return BuildPredictedCommand(moveX, moveZ, aimYawDegrees,
                aimPitchDegrees, fire, clientTick, jumpPressed, sprintHeld,
                crouchRequested, aimingHeld: false);
        }

        public NetcodePlayerCommand BuildPredictedCommand(
            float moveX,
            float moveZ,
            float aimYawDegrees,
            float aimPitchDegrees,
            bool fire,
            long clientTick,
            bool jumpPressed,
            bool sprintHeld,
            bool crouchRequested,
            bool aimingHeld)
        {
            RequireLocalOwner();
            if (!EnsurePresentationBuffers())
            {
                throw new InvalidOperationException(
                    "The replicated session rules are not ready yet.");
            }
            uint sequence = nextSequence++;
            ulong nonce = NextNonce(sequence, clientTick);
            NetVector3 current = prediction.PredictedPosition;
            var provisional = new PlayerInputCommand(
                playerId,
                sequence,
                nonce,
                clientTick,
                moveX,
                moveZ,
                aimYawDegrees,
                aimPitchDegrees,
                fire,
                current,
                jumpPressed,
                sprintHeld,
                crouchRequested);
            NetVector3 predicted = prediction.Predict(provisional);
            PlayerMovementState movement = prediction.PredictedMovement;
            PresentedPosition = NetcodeConversions.ToUnity(predicted);
            PresentedAimYaw = aimYawDegrees;
            PresentedAimPitch = aimPitchDegrees;
            PresentedVelocity = NetcodeConversions.ToUnity(movement.Velocity);
            PresentedCrouching = movement.IsCrouching;
            PresentedGrounded = movement.Grounded;
            ApplyPresentedPose();
            NetcodePlayerCommand payload =
                NetcodePlayerCommand.FromDomain(new PlayerInputCommand(
                playerId,
                sequence,
                nonce,
                clientTick,
                moveX,
                moveZ,
                aimYawDegrees,
                aimPitchDegrees,
                fire,
                predicted,
                jumpPressed,
                sprintHeld,
                crouchRequested));
            payload.AimingHeld = aimingHeld;
            PresentedSprinting = sprintHeld && !crouchRequested &&
                moveZ > 0.1f;
            PresentedAiming = aimingHeld && !PresentedSprinting;
            return payload;
        }

        public NetcodePlayerCommand SubmitLocalCommand(
            float moveX,
            float moveZ,
            float aimYawDegrees,
            float aimPitchDegrees,
            bool fire,
            long clientTick)
        {
            return SubmitLocalCommand(moveX, moveZ, aimYawDegrees,
                aimPitchDegrees, fire, clientTick, jumpPressed: false,
                sprintHeld: false, crouchRequested: false);
        }

        public NetcodePlayerCommand SubmitLocalCommand(
            float moveX,
            float moveZ,
            float aimYawDegrees,
            float aimPitchDegrees,
            bool fire,
            long clientTick,
            bool jumpPressed,
            bool sprintHeld,
            bool crouchRequested)
        {
            return SubmitLocalCommand(moveX, moveZ, aimYawDegrees,
                aimPitchDegrees, fire, clientTick, jumpPressed, sprintHeld,
                crouchRequested, aimingHeld: false);
        }

        public NetcodePlayerCommand SubmitLocalCommand(
            float moveX,
            float moveZ,
            float aimYawDegrees,
            float aimPitchDegrees,
            bool fire,
            long clientTick,
            bool jumpPressed,
            bool sprintHeld,
            bool crouchRequested,
            bool aimingHeld)
        {
            NetcodePlayerCommand payload = BuildPredictedCommand(
                moveX,
                moveZ,
                aimYawDegrees,
                aimPitchDegrees,
                fire,
                clientTick,
                jumpPressed,
                sprintHeld,
                crouchRequested,
                aimingHeld);
            if (session == null || !session.IsSpawned)
            {
                throw new InvalidOperationException(
                    "A spawned session authority is required to submit RPCs.");
            }

            if (fire)
            {
                session.SubmitShotRpc(payload);
            }
            else
            {
                session.SubmitInputRpc(payload);
            }

            return payload;
        }

        public NetcodePresentationCommand BuildPresentationCommand(
            NetworkPresentationAction action,
            string weaponId = null)
        {
            RequireLocalOwner();
            string safeWeapon = currentWeaponId;
            if (action == NetworkPresentationAction.SwitchWeapon)
            {
                if (!NetworkPresentationIds.TryResolveWeapon(
                        weaponId, out safeWeapon))
                {
                    throw new ArgumentException(
                        "Weapon id is not permitted by the server whitelist.",
                        nameof(weaponId));
                }
            }
            return new NetcodePresentationCommand
            {
                PlayerId = playerId,
                Sequence = nextPresentationSequence++,
                Action = action,
                WeaponId = safeWeapon
            };
        }

        public NetcodePresentationCommand SubmitPresentationAction(
            NetworkPresentationAction action,
            string weaponId = null)
        {
            NetcodePresentationCommand payload = BuildPresentationCommand(
                action, weaponId);
            if (session == null || !session.IsSpawned)
            {
                throw new InvalidOperationException(
                    "A spawned session authority is required to submit RPCs.");
            }
            session.SubmitPresentationRpc(payload);
            return payload;
        }

        public void ConsumeServerState(
            NetcodePlayerState state,
            bool treatAsLocalOwner,
            double currentEstimatedServerTick)
        {
            if (state.PlayerId != playerId)
            {
                throw new ArgumentException(
                    "The state belongs to a different player.",
                    nameof(state));
            }

            if (!EnsurePresentationBuffers())
            {
                return;
            }
            if (state.ServerTick >= lastConsumedServerTick)
            {
                PresentedAlive = state.IsAlive;
                ApplyPresentationState(state);
            }
            if (state.ServerTick > lastConsumedServerTick)
            {
                lastConsumedServerTick = state.ServerTick;
                if (treatAsLocalOwner)
                {
                    LastPredictionCorrection = prediction.Reconcile(
                        state.ToDomain());
                    PredictionSampleCount++;
                    predictionErrorSum += LastPredictionCorrection.ErrorDistance;
                    MaximumPredictionError = Math.Max(
                        MaximumPredictionError,
                        LastPredictionCorrection.ErrorDistance);
                    if (LastPredictionCorrection.WasCorrected)
                        PredictionCorrectionCount++;
                    PresentedPosition = NetcodeConversions.ToUnity(
                        LastPredictionCorrection.AppliedPosition);
                    PlayerMovementState movement = prediction.PredictedMovement;
                    PresentedAimYaw = (float)movement.AimYawDegrees;
                    PresentedAimPitch = (float)movement.AimPitchDegrees;
                    PresentedVelocity = NetcodeConversions.ToUnity(
                        movement.Velocity);
                    PresentedCrouching = movement.IsCrouching;
                    PresentedGrounded = movement.Grounded;
                    nextSequence = Math.Max(
                        nextSequence,
                        state.AcknowledgedSequence + 1);
                }
                else
                {
                    interpolation.Push(state.ToRemoteSnapshot());
                }
            }

            ConsumeAvailablePresentationEvents();

            if (!treatAsLocalOwner)
            {
                LastRemoteSample = interpolation.Sample(
                    currentEstimatedServerTick);
                if (LastRemoteSample.Available)
                {
                    PresentedPosition = NetcodeConversions.ToUnity(
                        LastRemoteSample.Position);
                    PresentedAimYaw = (float)LastRemoteSample.AimYawDegrees;
                    PresentedAimPitch = (float)LastRemoteSample.AimPitchDegrees;
                    PresentedVelocity = NetcodeConversions.ToUnity(
                        LastRemoteSample.Velocity);
                    PresentedCrouching =
                        LastRemoteSample.Stance == PlayerStance.Crouching;
                    PresentedGrounded = LastRemoteSample.Grounded;
                }
            }

            ApplyPresentedPose();
        }

        public void EnableOwnerTestHook(
            NetworkCoopSessionAuthority sessionAuthority,
            int authoritativePlayerId)
        {
            ownerTestHook = true;
            Bind(sessionAuthority, authoritativePlayerId);
            ApplyOwnershipPolicy();
        }

        public void ResetPresentation()
        {
            prediction = null;
            interpolation = null;
            LastPredictionCorrection = default;
            LastRemoteSample = default;
            lastConsumedServerTick = -1;
            estimatedServerTick = 0d;
            nextSequence = 1;
            nextPresentationSequence = 1;
            lastPresentationEventSequence = 0;
            presentationBaselineInitialized = false;
            pendingPresentationEvents.Clear();
            PresentedPosition = transform.position;
            PresentedAimYaw = transform.eulerAngles.y;
            PresentedAimPitch = 0f;
            PresentedVelocity = Vector3.zero;
            PresentedCrouching = false;
            PresentedGrounded = true;
            PresentedSprinting = false;
            PresentedAiming = false;
            PresentedAlive = true;
            PredictionSampleCount = 0;
            PredictionCorrectionCount = 0;
            MaximumPredictionError = 0d;
            predictionErrorSum = 0d;
        }

        public void ResetTestHook()
        {
            ownerTestHook = false;
            ResetPresentation();
            ApplyOwnershipPolicy();
        }

        public void ApplyOwnershipPolicy()
        {
            NetworkVerticalSliceInputDriver driver =
                GetComponent<NetworkVerticalSliceInputDriver>();
            if (driver != null) driver.enabled = IsLocallyControlled;
        }

        private void ResolveSession()
        {
            if (session == null)
            {
                session = FindFirstObjectByType<NetworkCoopSessionAuthority>();
            }
        }

        private bool EnsurePresentationBuffers()
        {
            ResolveSession();
            CoopServerRules rules = session?.Rules;
            if (rules == null)
            {
                return false;
            }

            if (prediction == null)
            {
                NetVector3 initial = NetcodeConversions.ToDomain(
                    transform.position);
                NetcodePlayerState state = default;
                bool hasState = false;
                if (session.TryGetPlayerState(playerId,
                        out state))
                {
                    hasState = true;
                    initial = NetcodeConversions.ToDomain(state.Position);
                    PresentedAimYaw = state.AimYawDegrees;
                    PresentedAimPitch = state.AimPitchDegrees;
                    nextSequence = Math.Max(
                        nextSequence,
                        state.AcknowledgedSequence + 1);
                }

                prediction = new LocalPredictionBuffer(
                    rules,
                    playerId,
                    initial);
                if (hasState)
                {
                    prediction.Reconcile(state.ToDomain());
                    ApplyPresentationState(state);
                }
                PlayerMovementState movement = prediction.PredictedMovement;
                PresentedPosition = NetcodeConversions.ToUnity(initial);
                PresentedVelocity = NetcodeConversions.ToUnity(
                    movement.Velocity);
                PresentedCrouching = movement.IsCrouching;
                PresentedGrounded = movement.Grounded;
            }

            interpolation ??= new RemoteSnapshotInterpolator();
            return true;
        }

        private void RequireLocalOwner()
        {
            if (!IsOwner && !ownerTestHook)
            {
                throw new InvalidOperationException(
                    "Only the locally owned player may submit input.");
            }
        }

        private ulong NextNonce(uint sequence, long clientTick)
        {
            unchecked
            {
                ulong value = nonceSalt ^ ((ulong)sequence << 32) ^
                    (ulong)clientTick ^ ((ulong)playerId *
                        0x9E3779B97F4A7C15UL);
                value ^= value >> 30;
                value *= 0xBF58476D1CE4E5B9UL;
                value ^= value >> 27;
                value *= 0x94D049BB133111EBUL;
                value ^= value >> 31;
                nonceSalt = value == 0 ? 1UL : value;
                return nonceSalt;
            }
        }

        private void ApplyPresentedPose()
        {
            if (applyPositionToTransform)
            {
                transform.position = PresentedPosition;
            }
            if (applyYawToTransform)
            {
                transform.rotation = Quaternion.Euler(
                    0f,
                    PresentedAimYaw,
                    0f);
            }
            PosePresented?.Invoke(
                PresentedPosition,
                PresentedAimYaw,
                PresentedAimPitch,
                PresentedCrouching,
                PresentedGrounded);
        }

        private void HandleAppearanceChanged(
            FixedString64Bytes _,
            FixedString64Bytes current)
        {
            ApplyAppearance(current.ToString());
        }

        public bool ConsumePresentationEvent(NetcodePresentationEvent value)
        {
            if (value.PlayerId != playerId ||
                value.Sequence <= lastPresentationEventSequence)
                return false;
            lastPresentationEventSequence = value.Sequence;
            if (value.Action == NetworkPresentationAction.SwitchWeapon)
                ApplyWeapon(value.WeaponId.ToString());
            PresentationActionReceived?.Invoke(value.Action);
            return true;
        }

        private void ApplyPresentationState(NetcodePlayerState state)
        {
            ApplyAppearance(state.AppearanceId.ToString());
            ApplyWeapon(state.WeaponId.ToString());
            PresentedSprinting = state.Sprinting;
            PresentedAiming = state.Aiming;
            nextPresentationSequence = Math.Max(
                nextPresentationSequence,
                state.AcknowledgedPresentationCommandSequence + 1);
            if (!presentationBaselineInitialized)
            {
                presentationBaselineInitialized = true;
                lastPresentationEventSequence = Math.Max(
                    lastPresentationEventSequence,
                    state.LastPresentationEventSequence);
            }
        }

        private void ConsumeAvailablePresentationEvents()
        {
            if (!presentationBaselineInitialized || session == null) return;
            session.GetPresentationEventsAfter(
                playerId,
                lastPresentationEventSequence,
                pendingPresentationEvents);
            for (int index = 0;
                 index < pendingPresentationEvents.Count;
                 index++)
                ConsumePresentationEvent(pendingPresentationEvents[index]);
        }

        private void ApplyAppearance(string value)
        {
            string safe = NetworkPresentationIds.ResolveAppearance(value);
            if (string.Equals(currentAppearanceId, safe,
                    StringComparison.Ordinal)) return;
            currentAppearanceId = safe;
            AppearanceChanged?.Invoke(currentAppearanceId);
        }

        private void ApplyWeapon(string value)
        {
            string safe = NetworkPresentationIds.ResolveWeaponOrDefault(value);
            if (string.Equals(currentWeaponId, safe,
                    StringComparison.Ordinal)) return;
            currentWeaponId = safe;
            WeaponChanged?.Invoke(currentWeaponId);
        }
    }
}
