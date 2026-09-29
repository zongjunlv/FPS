using System;
using System.Collections.Generic;
using System.Globalization;
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
        private static readonly bool TraceEnabled = string.Equals(
            Environment.GetEnvironmentVariable("FPS_NETTRACE"), "1",
            StringComparison.Ordinal);
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
        private readonly CoopPlayerMovementCollision predictionCollision = new();
        private Vector3 predictionPresentationOffset;
        private const float PredictionPresentationTimeConstantSeconds = 0.08f;
        private int predictionTraceGeneration = -1;
        private int predictionTraceCount;
        private RemoteSnapshotInterpolator interpolation;
        private uint nextSequence = 1;
        private ulong nonceSalt = 0x65C00FUL;
        private uint nextPresentationSequence = 1;
        private uint nextEconomySequence = 1;
        private uint nextMissionSequence = 1;
        private long lastConsumedServerTick = -1;
        private long lastPresentationEventSequence;
        private long lastShotEventSequence;
        private readonly SortedSet<long> consumedShotSequences = new();
        private long retiredShotSequence;
        private long lastAmmoShotSequence;
        private bool lastAmmoFromSnapshot;
        private bool presentationBaselineInitialized;
        private bool shotBaselineInitialized;
        private int lastClockRevision;
        private int lastRunGeneration;
        private long lastAmmoServerTick = -1;
        private bool ownerTestHook;
        private string currentAppearanceId =
            NetworkPresentationIds.DefaultAppearance;
        private string currentWeaponId = NetworkPresentationIds.DefaultWeapon;
        private string localGameplayWeaponId =
            NetworkPresentationIds.RifleGameplay;
        private Vector3 localShotOrigin;
        private bool hasLocalShotOrigin;
        private bool usePredictedShotOrigin;
        private Vector3 localShotDirection;
        private long localShotViewTick;
        private bool hasLocalShotFrame;
        private const float PredictedShotOriginHeight = 1.25f;
        private readonly List<NetcodePresentationEvent>
            pendingPresentationEvents = new();
        private readonly List<NetcodeShotFeedbackEvent> pendingShotEvents =
            new();

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
        public AuthoritativePlayerLifeState PresentedLifeState {
            get;
            private set;
        } = AuthoritativePlayerLifeState.Alive;
        public int PredictionSampleCount { get; private set; }
        public int PredictionCorrectionCount { get; private set; }
        public double MaximumPredictionError { get; private set; }
        public double MeanPredictionError => PredictionSampleCount == 0
            ? 0d
            : predictionErrorSum / PredictionSampleCount;
        public PredictionCorrection LastPredictionCorrection { get; private set; }
        public RemoteInterpolationSample LastRemoteSample { get; private set; }
        public int PendingPredictionCount => prediction?.PendingCommands.Count ?? 0;
        public bool HasPredictionCapacity => EnsurePresentationBuffers() &&
            lastClockRevision == session.ClockRevision && prediction.HasCapacity;
        public double EstimatedServerTick => session?.EstimatedServerTick ?? 0d;
        public double PresentationTick => session?.PresentationTick ?? 0d;
        public NetworkCoopSessionAuthority Session => session;
        public float PresentationSprintSpeed => session?.Rules == null
            ? 6f
            : (float)session.Rules.SprintSpeed;
        public string AppearanceId => currentAppearanceId;
        public string WeaponId => currentWeaponId;
        public long LastPresentationEventSequence =>
            lastPresentationEventSequence;
        public long LastShotEventSequence => lastShotEventSequence;
        public int PresentedMagazineAmmo { get; private set; }
        public int PresentedReserveAmmo { get; private set; }
        public float PresentedHealth { get; private set; } = 100f;
        public float PresentedMaximumHealth { get; private set; } = 100f;
        public float PresentedArmor { get; private set; }
        public float PresentedMaximumArmor { get; private set; }
        public uint PresentedAcknowledgedSequence { get; private set; }
        public uint PresentedAmmoAcknowledgedSequence { get; private set; }
        public bool PresentedReloading { get; private set; }
        public bool PresentedSwitching { get; private set; }
        public string CombatWeaponId { get; private set; } =
            NetworkPresentationIds.RifleGameplay;
        public bool IsLocallyControlled => IsOwner || ownerTestHook;
        public bool IsPresentationReady => session != null &&
            session.Rules != null && (!IsSpawned || HasConsumedServerState);
        public bool HasConsumedServerState => lastConsumedServerTick >= 0;
        public event Action<Vector3, float, float, bool, bool> PosePresented;
        public event Action<string> AppearanceChanged;
        public event Action<string> WeaponChanged;
        public event Action<NetworkPresentationAction>
            PresentationActionReceived;
        public event Action<NetcodeShotFeedbackEvent> ShotFeedbackReceived;
        public event Action<long, PredictionCorrection> PredictionReconciled;
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
            if (session == null || !session.IsReplicatedSnapshotComplete ||
                !session.TryGetPlayerState(
                    playerId,
                    out NetcodePlayerState state))
            {
                return;
            }

            // The interpolator's public API retains its two-tick default for
            // fixtures. Runtime uses the shared, RTT-aware presentation clock.
            if (IsLocallyControlled && prediction != null)
                AdvancePredictionPresentation(Time.unscaledDeltaTime);
            ConsumeServerState(state, IsLocallyControlled,
                session.PresentationTick + 2d);
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
            if (!prediction.HasCapacity ||
                lastClockRevision != session.ClockRevision)
                throw new InvalidOperationException(
                    "Prediction awaits acknowledgement or a complete clock-epoch snapshot.");
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
            prediction.MovementSpeedMultiplier =
                ResolveMovementSpeedMultiplier();
            NetVector3 predicted = prediction.Predict(provisional);
            PlayerMovementState movement = prediction.PredictedMovement;
            PresentedPosition = NetcodeConversions.ToUnity(predicted) +
                predictionPresentationOffset;
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
                crouchRequested,
                localGameplayWeaponId,
                ResolveShotOrigin(predicted)));
            payload.AimingHeld = aimingHeld;
            if (fire && hasLocalShotFrame)
            {
                payload.ShotDirection = localShotDirection;
                payload.ShotViewTick = localShotViewTick;
                hasLocalShotFrame = false;
            }
            PresentedSprinting = sprintHeld && !crouchRequested &&
                moveZ > 0.1f;
            PresentedAiming = aimingHeld && !PresentedSprinting;
            return payload;
        }

        public void ConfigureLocalCombatContext(
            string gameplayWeaponId,
            Vector3 shotOrigin)
        {
            string normalized = gameplayWeaponId?.Trim() ?? string.Empty;
            localGameplayWeaponId = string.Equals(normalized,
                NetworkPresentationIds.HandgunGameplay,
                StringComparison.Ordinal)
                ? NetworkPresentationIds.HandgunGameplay
                : NetworkPresentationIds.RifleGameplay;
            localShotOrigin = shotOrigin;
            hasLocalShotOrigin = true;
            usePredictedShotOrigin = false;
        }

        public void ConfigureLocalShotFrame(Vector3 direction, long viewTick)
        {
            if (!float.IsNaN(direction.sqrMagnitude) &&
                !float.IsInfinity(direction.sqrMagnitude) &&
                direction.sqrMagnitude > 0.000001f && viewTick >= 0)
            {
                localShotDirection = direction.normalized;
                localShotViewTick = viewTick;
                hasLocalShotFrame = true;
            }
            else
            {
                hasLocalShotFrame = false;
            }
        }

        public void DiscardLocalShotFrame() => hasLocalShotFrame = false;

        /// <summary>
        /// Headless automation has no rendered weapon muzzle. Use the same
        /// predicted player pose plus the standard first-person eye height as
        /// its virtual muzzle. Using the feet as the muzzle would make the
        /// otherwise correct pitch point into the ground.
        /// Runtime clients continue to use ConfigureLocalCombatContext.
        /// </summary>
        public void ConfigurePredictedCombatContext(string gameplayWeaponId)
        {
            string normalized = gameplayWeaponId?.Trim() ?? string.Empty;
            localGameplayWeaponId = string.Equals(normalized,
                NetworkPresentationIds.HandgunGameplay,
                StringComparison.Ordinal)
                ? NetworkPresentationIds.HandgunGameplay
                : NetworkPresentationIds.RifleGameplay;
            hasLocalShotOrigin = false;
            usePredictedShotOrigin = true;
            hasLocalShotFrame = false;
        }

        private NetVector3 ResolveShotOrigin(NetVector3 predicted)
        {
            if (hasLocalShotOrigin)
                return NetcodeConversions.ToDomain(localShotOrigin);
            return usePredictedShotOrigin
                ? predicted + new NetVector3(
                    0d, PredictedShotOriginHeight, 0d)
                : predicted;
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
                if (TraceEnabled)
                {
                    Debug.Log(string.Format(CultureInfo.InvariantCulture,
                        "[NETTRACE-v1] role=client kind=shotSubmit " +
                        "utcMs={0} monoMs={1:F1} player={2} seq={3} " +
                        "clientTick={4} worldTick={5} connected={6} " +
                        "listening={7} spawned={8}",
                        DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                        Time.realtimeSinceStartupAsDouble * 1000d,
                        playerId, payload.Sequence, payload.ClientTick,
                        session.WorldState.ServerTick,
                        NetworkManager != null &&
                        NetworkManager.IsConnectedClient ? 1 : 0,
                        NetworkManager != null &&
                        NetworkManager.IsListening ? 1 : 0,
                        IsSpawned ? 1 : 0));
                }
                session.SubmitShotRpc(payload);
            }
            else if (jumpPressed)
            {
                session.SubmitActionInputRpc(payload);
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

        public NetcodeEconomyCommand BuildEconomyCommand(
            AuthoritativeEconomyCommandKind kind,
            int entityId = 0,
            int sourceSlot = -1,
            int destinationSlot = -1,
            int quantity = 0,
            int candidateIndex = -1,
            int expectedDropRevision = 0,
            string expectedItemId = "")
        {
            RequireLocalOwner();
            int inventoryRevision = 0;
            int choiceGeneration = 0;
            if (session != null && session.TryGetProgression(
                    playerId, out NetcodeProgressionState progression))
            {
                nextEconomySequence = Math.Max(nextEconomySequence,
                    progression.AcknowledgedEconomySequence + 1);
                inventoryRevision = progression.InventoryRevision;
                choiceGeneration = progression.ChoiceGeneration;
            }
            uint sequence = nextEconomySequence++;
            return NetcodeEconomyCommand.FromDomain(
                new AuthoritativeEconomyCommand(
                    playerId,
                    sequence,
                    NextNonce(sequence, session?.WorldState.ServerTick ?? 0),
                    kind,
                    entityId,
                    sourceSlot,
                    destinationSlot,
                    quantity,
                    candidateIndex,
                    inventoryRevision,
                    expectedDropRevision,
                    choiceGeneration,
                    expectedItemId));
        }

        public NetcodeEconomyCommand SubmitEconomyAction(
            AuthoritativeEconomyCommandKind kind,
            int entityId = 0,
            int sourceSlot = -1,
            int destinationSlot = -1,
            int quantity = 0,
            int candidateIndex = -1,
            int expectedDropRevision = 0,
            string expectedItemId = "")
        {
            NetcodeEconomyCommand payload = BuildEconomyCommand(
                kind, entityId, sourceSlot, destinationSlot, quantity,
                candidateIndex, expectedDropRevision, expectedItemId);
            if (session == null || !session.IsSpawned)
                throw new InvalidOperationException(
                    "A spawned session authority is required to submit RPCs.");
            session.SubmitEconomyRpc(payload);
            return payload;
        }

        public NetcodeMissionCommand BuildMissionCommand(
            AuthoritativeMissionCommandKind kind,
            int targetPlayerId = 0)
        {
            RequireLocalOwner();
            uint sequence = nextMissionSequence++;
            return NetcodeMissionCommand.FromDomain(
                new AuthoritativeMissionCommand(
                    playerId,
                    sequence,
                    NextNonce(sequence,
                        (session?.WorldState.ServerTick ?? 0) + 0x9800),
                    kind,
                    targetPlayerId));
        }

        public NetcodeMissionCommand SubmitMissionAction(
            AuthoritativeMissionCommandKind kind,
            int targetPlayerId = 0)
        {
            NetcodeMissionCommand payload = BuildMissionCommand(
                kind, targetPlayerId);
            if (session == null || !session.IsSpawned)
                throw new InvalidOperationException(
                    "A spawned session authority is required to submit RPCs.");
            session.SubmitMissionRpc(payload);
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
            bool wasReady = HasConsumedServerState;
            if (state.ServerTick > lastConsumedServerTick)
            {
                lastConsumedServerTick = state.ServerTick;
                if (treatAsLocalOwner)
                {
                    prediction.MovementSpeedMultiplier =
                        ResolveMovementSpeedMultiplier();
                    LastPredictionCorrection = prediction.Reconcile(
                        state.ToDomain());
                    PredictionReconciled?.Invoke(
                        state.ServerTick,
                        LastPredictionCorrection);
                    PredictionSampleCount++;
                    predictionErrorSum += LastPredictionCorrection.ErrorDistance;
                    MaximumPredictionError = Math.Max(
                        MaximumPredictionError,
                        LastPredictionCorrection.ErrorDistance);
                    if (LastPredictionCorrection.WasCorrected)
                    {
                        PredictionCorrectionCount++;
                        TracePredictionCorrection(state);
                    }
                    ReconcilePredictionPresentation(LastPredictionCorrection);
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
            ConsumeAvailableShotEvents();

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
            if (!wasReady && HasConsumedServerState)
                ApplyOwnershipPolicy();
        }

        private double ResolveMovementSpeedMultiplier()
        {
            if (session == null) return 1d;
            double bonus = 0d;
            for (int index = 0; index < session.ReplicatedUpgradeCount;
                 index++)
            {
                NetcodeUpgradeStackState stack =
                    session.GetReplicatedUpgrade(index);
                if (stack.PlayerId == playerId &&
                    stack.UpgradeId.ToString() ==
                    "survival_mobility_training")
                    bonus += 0.1d * stack.Level;
            }
            return 1d + bonus;
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
            predictionPresentationOffset = Vector3.zero;
            interpolation = null;
            LastPredictionCorrection = default;
            LastRemoteSample = default;
            lastConsumedServerTick = -1;
            lastClockRevision = 0;
            lastRunGeneration = 0;
            lastAmmoServerTick = -1;
            nextSequence = 1;
            nextPresentationSequence = 1;
            nextEconomySequence = 1;
            nextMissionSequence = 1;
            lastPresentationEventSequence = 0;
            lastShotEventSequence = 0;
            consumedShotSequences.Clear();
            retiredShotSequence = 0;
            lastAmmoShotSequence = 0;
            lastAmmoFromSnapshot = false;
            presentationBaselineInitialized = false;
            shotBaselineInitialized = false;
            pendingPresentationEvents.Clear();
            pendingShotEvents.Clear();
            PresentedPosition = transform.position;
            PresentedAimYaw = transform.eulerAngles.y;
            PresentedAimPitch = 0f;
            PresentedVelocity = Vector3.zero;
            PresentedCrouching = false;
            PresentedGrounded = true;
            PresentedSprinting = false;
            PresentedAiming = false;
            PresentedAlive = true;
            PresentedLifeState = AuthoritativePlayerLifeState.Alive;
            PresentedMagazineAmmo = 0;
            PresentedReserveAmmo = 0;
            PresentedHealth = 100f;
            PresentedMaximumHealth = 100f;
            PresentedArmor = 0f;
            PresentedMaximumArmor = 0f;
            PresentedAcknowledgedSequence = 0;
            PresentedAmmoAcknowledgedSequence = 0;
            PresentedReloading = false;
            PresentedSwitching = false;
            CombatWeaponId = NetworkPresentationIds.RifleGameplay;
            localGameplayWeaponId = NetworkPresentationIds.RifleGameplay;
            hasLocalShotOrigin = false;
            usePredictedShotOrigin = false;
            hasLocalShotFrame = false;
            localShotDirection = Vector3.zero;
            localShotViewTick = 0;
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
            if (driver != null)
                driver.enabled = IsLocallyControlled && IsPresentationReady;
        }

        private void ResolveSession()
        {
            if (session == null)
            {
                session = FindFirstObjectByType<NetworkCoopSessionAuthority>();
            }
        }

        private void TracePredictionCorrection(NetcodePlayerState state)
        {
            if (!TraceEnabled) return;
            int generation = session.WorldState.RunGeneration;
            if (generation != predictionTraceGeneration)
            {
                predictionTraceGeneration = generation;
                predictionTraceCount = 0;
            }
            if (predictionTraceCount >= CoopMovementTrace.MaximumSamplesPerPlayerRun)
                return;
            PredictionReconciliationDiagnostic value =
                prediction.LastReconciliationDiagnostic;
            PlayerInputCommand command = value.LastPendingCommand;
            Debug.Log(string.Format(CultureInfo.InvariantCulture,
                "[NETTRACE-v1] role=client kind=predictionCorrection utcMs={0} monoMs={1:F1} run={2} player={3} diagnosticIndex={4} serverTick={5} ack={6} lastAcceptedTick={7} previousAcceptedTick={8} lastPredictedTick={9} replayTick={10} pendingBefore={11} pendingAfter={12} correction={13} error={14:F6} speedMultiplier={15:F6} hasPending={16} cmdSeq={17} cmdTick={18} moveX={19:F6} moveZ={20:F6} sprint={21} crouch={22} jump={23} claimX={24:F6} claimY={25:F6} claimZ={26:F6} {27} {28} {29}",
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Time.realtimeSinceStartupAsDouble * 1000d,
                generation, playerId, ++predictionTraceCount, state.ServerTick,
                state.AcknowledgedSequence, state.LastAcceptedClientTick,
                value.PreviousAcceptedTick, value.PreviousPredictedTick,
                value.ReplayTick, value.PendingBefore, value.PendingAfter,
                LastPredictionCorrection.Kind, LastPredictionCorrection.ErrorDistance,
                value.SpeedMultiplier, value.HasPendingCommand ? 1 : 0,
                command.Sequence, command.ClientTick, command.MoveX, command.MoveZ,
                command.SprintHeld ? 1 : 0, command.CrouchRequested ? 1 : 0,
                command.JumpPressed ? 1 : 0, command.ClaimedPosition.X,
                command.ClaimedPosition.Y, command.ClaimedPosition.Z,
                CoopMovementTrace.MovementFields("before", value.Before),
                CoopMovementTrace.MovementFields("replay", value.Replay),
                CoopMovementTrace.MovementFields("authoritative", value.Authoritative.Movement)));
        }

        private void AdvancePredictionPresentation(float elapsedSeconds)
        {
            // A render-only, bounded offset. Never feed this value into
            // prediction, input claims, or acknowledgement replay.
            predictionPresentationOffset *= Mathf.Exp(
                -Mathf.Max(0f, elapsedSeconds) /
                PredictionPresentationTimeConstantSeconds);
            if (predictionPresentationOffset.sqrMagnitude < 0.00000001f)
                predictionPresentationOffset = Vector3.zero;
            PresentedPosition = NetcodeConversions.ToUnity(
                prediction.PredictedPosition) + predictionPresentationOffset;
        }

        private void ReconcilePredictionPresentation(PredictionCorrection correction)
        {
            Vector3 physical = NetcodeConversions.ToUnity(
                correction.ReplayTargetPosition);
            Vector3 visualBefore = NetcodeConversions.ToUnity(
                correction.PositionBeforeCorrection) + predictionPresentationOffset;
            predictionPresentationOffset = correction.Kind switch
            {
                PredictionCorrectionKind.Snap => Vector3.zero,
                PredictionCorrectionKind.Smooth => (visualBefore - physical) * 0.5f,
                _ => visualBefore - physical
            };
            predictionPresentationOffset = Vector3.ClampMagnitude(
                predictionPresentationOffset,
                (float)session.Rules.PredictionSnapThreshold);
            PresentedPosition = physical + predictionPresentationOffset;
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
                    initial,
                    predictionCollision.Resolve);
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
            SynchronizeClockRevision();
            return true;
        }

        private void SynchronizeClockRevision()
        {
            int revision = session.ClockRevision;
            if (revision == lastClockRevision) return;
            int generation = session.WorldState.RunGeneration;
            if (lastClockRevision != 0)
            {
                // World metadata may precede its player list in a replication
                // update. Do not consume a new clock epoch using an old pose.
                if (!session.IsReplicatedSnapshotComplete ||
                    !session.TryGetPlayerState(playerId,
                        out NetcodePlayerState state) ||
                    state.ServerTick != session.WorldState.ServerTick)
                    return;
                prediction.ResetToAuthoritative(state.ToDomain());
                predictionPresentationOffset = Vector3.zero;
                interpolation = new RemoteSnapshotInterpolator();
                lastConsumedServerTick = -1;
                if (generation != lastRunGeneration)
                {
                    lastAmmoServerTick = -1;
                    PresentedAmmoAcknowledgedSequence = 0;
                    lastAmmoShotSequence = 0;
                    lastAmmoFromSnapshot = false;
                    consumedShotSequences.Clear();
                    retiredShotSequence = 0;
                    lastShotEventSequence = state.LastShotEventSequence;
                }
                nextSequence = Math.Max(nextSequence,
                    state.AcknowledgedSequence + 1);
                PresentedPosition = state.Position;
                ApplyPresentedPose();
            }
            lastClockRevision = revision;
            lastRunGeneration = generation;
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
            if (state.ServerTick >= lastAmmoServerTick)
            {
                PresentedMagazineAmmo = state.MagazineAmmo;
                PresentedReserveAmmo = state.ReserveAmmo;
                PresentedAmmoAcknowledgedSequence = state.AcknowledgedSequence;
                lastAmmoServerTick = state.ServerTick;
                lastAmmoFromSnapshot = true;
                CombatWeaponId = state.CombatWeaponId.IsEmpty
                    ? NetworkPresentationIds.ToGameplayWeaponId(
                        state.WeaponId.ToString())
                    : state.CombatWeaponId.ToString();
            }
            PresentedHealth = state.Health;
            PresentedMaximumHealth = state.MaximumHealth;
            PresentedArmor = state.Armor;
            PresentedMaximumArmor = state.MaximumArmor;
            PresentedAcknowledgedSequence = state.AcknowledgedSequence;
            PresentedReloading = state.Reloading;
            PresentedSwitching = state.Switching;
            PresentedLifeState = state.LifeState;
            nextPresentationSequence = Math.Max(
                nextPresentationSequence,
                state.AcknowledgedPresentationCommandSequence + 1);
            nextMissionSequence = Math.Max(
                nextMissionSequence,
                state.AcknowledgedMissionSequence + 1);
            if (!presentationBaselineInitialized)
            {
                presentationBaselineInitialized = true;
                lastPresentationEventSequence = Math.Max(
                    lastPresentationEventSequence,
                    state.LastPresentationEventSequence);
            }
            if (!shotBaselineInitialized)
            {
                shotBaselineInitialized = true;
                lastShotEventSequence = Math.Max(
                    lastShotEventSequence,
                    state.LastShotEventSequence);
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

        public bool ConsumeShotFeedbackEvent(NetcodeShotFeedbackEvent value)
        {
            if (value.ShooterPlayerId != playerId ||
                value.Sequence <= lastShotEventSequence)
                return false;
            lastShotEventSequence = value.Sequence;
            return ApplyUniqueShotFeedback(value);
        }

        /// <summary>
        /// The lossy fast path does not advance the reliable journal cursor:
        /// a later UDP packet must not hide an earlier shot's eventual ACK.
        /// </summary>
        public bool ConsumeFastShotFeedbackEvent(NetcodeShotFeedbackEvent value)
        {
            if (!shotBaselineInitialized || session == null ||
                lastRunGeneration != session.WorldState.RunGeneration ||
                value.ShooterPlayerId != playerId ||
                value.Sequence <= lastShotEventSequence)
                return false;
            return ApplyUniqueShotFeedback(value);
        }

        private bool ApplyUniqueShotFeedback(NetcodeShotFeedbackEvent value)
        {
            if (value.Sequence <= retiredShotSequence ||
                !consumedShotSequences.Add(value.Sequence))
                return false;
            // The reliable journal retains 96 events. A 128-entry identity
            // window is bounded and covers duplicates from both paths.
            while (consumedShotSequences.Count > 128)
            {
                retiredShotSequence = consumedShotSequences.Min;
                consumedShotSequences.Remove(retiredShotSequence);
            }
            if (value.HasAmmoState &&
                (value.ServerTick > lastAmmoServerTick ||
                 value.ServerTick == lastAmmoServerTick &&
                 !lastAmmoFromSnapshot &&
                 value.Sequence >= lastAmmoShotSequence))
            {
                PresentedMagazineAmmo = value.MagazineAmmo;
                PresentedReserveAmmo = value.ReserveAmmo;
                PresentedAmmoAcknowledgedSequence = value.AmmoAcknowledgedSequence;
                CombatWeaponId = value.AmmoWeaponId.ToString();
                lastAmmoServerTick = value.ServerTick;
                lastAmmoShotSequence = value.Sequence;
                lastAmmoFromSnapshot = false;
            }
            if (TraceEnabled)
            {
                Debug.Log(string.Format(CultureInfo.InvariantCulture,
                    "[NETTRACE-v1] role=client kind=shotAck " +
                    "utcMs={0} monoMs={1:F1} player={2} seq={3} " +
                    "serverTick={4} target={5} result={6} damage={7:F2} " +
                    "accepted={8} rejection={9} magazine={10} reserve={11}",
                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    Time.realtimeSinceStartupAsDouble * 1000d,
                    playerId, value.ShotCommandSequence, value.ServerTick,
                    value.TargetId, value.Kind, value.AppliedDamage,
                    value.Accepted ? 1 : 0, value.RejectionReason,
                    value.MagazineAmmo, value.ReserveAmmo));
            }
            ShotFeedbackReceived?.Invoke(value);
            return true;
        }

        private void ConsumeAvailableShotEvents()
        {
            if (!shotBaselineInitialized || session == null) return;
            session.GetShotEventsAfter(
                playerId,
                lastShotEventSequence,
                pendingShotEvents);
            for (int index = 0; index < pendingShotEvents.Count; index++)
                ConsumeShotFeedbackEvent(pendingShotEvents[index]);
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
