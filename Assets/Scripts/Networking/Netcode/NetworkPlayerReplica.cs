using System;
using FPS.Networking.Domain;
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

        private LocalPredictionBuffer prediction;
        private RemoteSnapshotInterpolator interpolation;
        private uint nextSequence = 1;
        private ulong nonceSalt = 0x65C00FUL;
        private long lastConsumedServerTick = -1;
        private double estimatedServerTick;
        private bool ownerTestHook;

        public int PlayerId => playerId;
        public Vector3 PresentedPosition { get; private set; }
        public float PresentedAimYaw { get; private set; }
        public float PresentedAimPitch { get; private set; }
        public PredictionCorrection LastPredictionCorrection { get; private set; }
        public RemoteInterpolationSample LastRemoteSample { get; private set; }
        public int PendingPredictionCount => prediction?.PendingCommands.Count ?? 0;
        public NetworkCoopSessionAuthority Session => session;
        public bool IsLocallyControlled => IsOwner || ownerTestHook;
        public bool IsPresentationReady => session != null &&
            session.Rules != null;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            playerId = replicatedPlayerId.Value;
            ResolveSession();
            EnsurePresentationBuffers();
        }

        public override void OnNetworkDespawn()
        {
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
            ConsumeServerState(state, IsOwner, estimatedServerTick);
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
        }

        public NetcodePlayerCommand BuildPredictedCommand(
            float moveX,
            float moveZ,
            float aimYawDegrees,
            float aimPitchDegrees,
            bool fire,
            long clientTick)
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
                current);
            NetVector3 predicted = prediction.Predict(provisional);
            PresentedPosition = NetcodeConversions.ToUnity(predicted);
            PresentedAimYaw = aimYawDegrees;
            PresentedAimPitch = aimPitchDegrees;
            ApplyPresentedPose();
            return NetcodePlayerCommand.FromDomain(new PlayerInputCommand(
                playerId,
                sequence,
                nonce,
                clientTick,
                moveX,
                moveZ,
                aimYawDegrees,
                aimPitchDegrees,
                fire,
                predicted));
        }

        public NetcodePlayerCommand SubmitLocalCommand(
            float moveX,
            float moveZ,
            float aimYawDegrees,
            float aimPitchDegrees,
            bool fire,
            long clientTick)
        {
            NetcodePlayerCommand payload = BuildPredictedCommand(
                moveX,
                moveZ,
                aimYawDegrees,
                aimPitchDegrees,
                fire,
                clientTick);
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
            if (state.ServerTick > lastConsumedServerTick)
            {
                lastConsumedServerTick = state.ServerTick;
                if (treatAsLocalOwner)
                {
                    LastPredictionCorrection = prediction.Reconcile(
                        state.ToDomain());
                    PresentedPosition = NetcodeConversions.ToUnity(
                        LastPredictionCorrection.AppliedPosition);
                    PresentedAimYaw = state.AimYawDegrees;
                    PresentedAimPitch = state.AimPitchDegrees;
                    nextSequence = Math.Max(
                        nextSequence,
                        state.AcknowledgedSequence + 1);
                }
                else
                {
                    interpolation.Push(state.ToRemoteSnapshot());
                }
            }

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
            PresentedPosition = transform.position;
            PresentedAimYaw = transform.eulerAngles.y;
            PresentedAimPitch = 0f;
        }

        public void ResetTestHook()
        {
            ownerTestHook = false;
            ResetPresentation();
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
                if (session.TryGetPlayerState(playerId,
                        out NetcodePlayerState state))
                {
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
                PresentedPosition = NetcodeConversions.ToUnity(initial);
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
        }
    }
}
