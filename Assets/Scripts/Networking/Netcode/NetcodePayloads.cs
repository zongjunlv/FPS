using System;
using FPS.Networking.Domain;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace FPS.Networking.Netcode
{
    public struct NetcodeRulesState : INetworkSerializable,
        IEquatable<NetcodeRulesState>
    {
        public int TickRate;
        public int MaximumPastCommandTicks;
        public int MaximumFutureCommandTicks;
        public int HistoryCapacity;
        public float MaximumMoveSpeed;
        public float ClaimedPositionTolerance;
        public float MaximumAimDegreesPerSecond;
        public int FireCooldownTicks;
        public float HitscanRange;
        public float ShotDamage;
        public float PredictionCorrectionThreshold;
        public float PredictionSnapThreshold;
        public int NonceHistoryCapacity;
        public float WalkSpeed;
        public float SprintSpeed;
        public float CrouchSpeed;
        public float MaximumAcceleration;
        public float Gravity;
        public float JumpSpeed;
        public int MinimumJumpIntervalTicks;

        public bool IsValid => TickRate > 0;

        public static NetcodeRulesState FromDomain(CoopServerRules value)
        {
            return new NetcodeRulesState
            {
                TickRate = value.TickRate,
                MaximumPastCommandTicks = value.MaximumPastCommandTicks,
                MaximumFutureCommandTicks = value.MaximumFutureCommandTicks,
                HistoryCapacity = value.HistoryCapacity,
                MaximumMoveSpeed = (float)value.MaximumMoveSpeed,
                ClaimedPositionTolerance = (float)value.ClaimedPositionTolerance,
                MaximumAimDegreesPerSecond =
                    (float)value.MaximumAimDegreesPerSecond,
                FireCooldownTicks = value.FireCooldownTicks,
                HitscanRange = (float)value.HitscanRange,
                ShotDamage = (float)value.ShotDamage,
                PredictionCorrectionThreshold =
                    (float)value.PredictionCorrectionThreshold,
                PredictionSnapThreshold = (float)value.PredictionSnapThreshold,
                NonceHistoryCapacity = value.NonceHistoryCapacity,
                WalkSpeed = (float)value.WalkSpeed,
                SprintSpeed = (float)value.SprintSpeed,
                CrouchSpeed = (float)value.CrouchSpeed,
                MaximumAcceleration = (float)value.MaximumAcceleration,
                Gravity = (float)value.Gravity,
                JumpSpeed = (float)value.JumpSpeed,
                MinimumJumpIntervalTicks = value.MinimumJumpIntervalTicks
            };
        }

        public CoopServerRules ToDomain()
        {
            if (!IsValid)
            {
                return null;
            }

            return new CoopServerRules(
                TickRate,
                MaximumPastCommandTicks,
                MaximumFutureCommandTicks,
                HistoryCapacity,
                MaximumMoveSpeed,
                ClaimedPositionTolerance,
                MaximumAimDegreesPerSecond,
                FireCooldownTicks,
                HitscanRange,
                ShotDamage,
                PredictionCorrectionThreshold,
                PredictionSnapThreshold,
                NonceHistoryCapacity,
                WalkSpeed,
                SprintSpeed,
                CrouchSpeed,
                MaximumAcceleration,
                Gravity,
                JumpSpeed,
                MinimumJumpIntervalTicks);
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer)
            where T : IReaderWriter
        {
            serializer.SerializeValue(ref TickRate);
            serializer.SerializeValue(ref MaximumPastCommandTicks);
            serializer.SerializeValue(ref MaximumFutureCommandTicks);
            serializer.SerializeValue(ref HistoryCapacity);
            serializer.SerializeValue(ref MaximumMoveSpeed);
            serializer.SerializeValue(ref ClaimedPositionTolerance);
            serializer.SerializeValue(ref MaximumAimDegreesPerSecond);
            serializer.SerializeValue(ref FireCooldownTicks);
            serializer.SerializeValue(ref HitscanRange);
            serializer.SerializeValue(ref ShotDamage);
            serializer.SerializeValue(ref PredictionCorrectionThreshold);
            serializer.SerializeValue(ref PredictionSnapThreshold);
            serializer.SerializeValue(ref NonceHistoryCapacity);
            serializer.SerializeValue(ref WalkSpeed);
            serializer.SerializeValue(ref SprintSpeed);
            serializer.SerializeValue(ref CrouchSpeed);
            serializer.SerializeValue(ref MaximumAcceleration);
            serializer.SerializeValue(ref Gravity);
            serializer.SerializeValue(ref JumpSpeed);
            serializer.SerializeValue(ref MinimumJumpIntervalTicks);
        }

        public bool Equals(NetcodeRulesState other)
        {
            return TickRate == other.TickRate &&
                MaximumPastCommandTicks == other.MaximumPastCommandTicks &&
                MaximumFutureCommandTicks == other.MaximumFutureCommandTicks &&
                HistoryCapacity == other.HistoryCapacity &&
                MaximumMoveSpeed.Equals(other.MaximumMoveSpeed) &&
                ClaimedPositionTolerance.Equals(other.ClaimedPositionTolerance) &&
                MaximumAimDegreesPerSecond.Equals(
                    other.MaximumAimDegreesPerSecond) &&
                FireCooldownTicks == other.FireCooldownTicks &&
                HitscanRange.Equals(other.HitscanRange) &&
                ShotDamage.Equals(other.ShotDamage) &&
                PredictionCorrectionThreshold.Equals(
                    other.PredictionCorrectionThreshold) &&
                PredictionSnapThreshold.Equals(other.PredictionSnapThreshold) &&
                NonceHistoryCapacity == other.NonceHistoryCapacity &&
                WalkSpeed.Equals(other.WalkSpeed) &&
                SprintSpeed.Equals(other.SprintSpeed) &&
                CrouchSpeed.Equals(other.CrouchSpeed) &&
                MaximumAcceleration.Equals(other.MaximumAcceleration) &&
                Gravity.Equals(other.Gravity) &&
                JumpSpeed.Equals(other.JumpSpeed) &&
                MinimumJumpIntervalTicks == other.MinimumJumpIntervalTicks;
        }
    }

    public struct NetcodePlayerCommand : INetworkSerializable,
        IEquatable<NetcodePlayerCommand>
    {
        public int PlayerId;
        public uint Sequence;
        public ulong Nonce;
        public long ClientTick;
        public float MoveX;
        public float MoveZ;
        public float AimYawDegrees;
        public float AimPitchDegrees;
        public bool Fire;
        public Vector3 ClaimedPosition;
        public bool JumpPressed;
        public bool SprintHeld;
        public bool CrouchRequested;

        public static NetcodePlayerCommand FromDomain(PlayerInputCommand value)
        {
            return new NetcodePlayerCommand
            {
                PlayerId = value.PlayerId,
                Sequence = value.Sequence,
                Nonce = value.Nonce,
                ClientTick = value.ClientTick,
                MoveX = (float)value.MoveX,
                MoveZ = (float)value.MoveZ,
                AimYawDegrees = (float)value.AimYawDegrees,
                AimPitchDegrees = (float)value.AimPitchDegrees,
                Fire = value.Fire,
                ClaimedPosition = NetcodeConversions.ToUnity(value.ClaimedPosition),
                JumpPressed = value.JumpPressed,
                SprintHeld = value.SprintHeld,
                CrouchRequested = value.CrouchRequested
            };
        }

        public PlayerInputCommand ToDomain(bool? forceFire = null)
        {
            return new PlayerInputCommand(
                PlayerId,
                Sequence,
                Nonce,
                ClientTick,
                MoveX,
                MoveZ,
                AimYawDegrees,
                AimPitchDegrees,
                forceFire ?? Fire,
                NetcodeConversions.ToDomain(ClaimedPosition),
                JumpPressed,
                SprintHeld,
                CrouchRequested);
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer)
            where T : IReaderWriter
        {
            serializer.SerializeValue(ref PlayerId);
            serializer.SerializeValue(ref Sequence);
            serializer.SerializeValue(ref Nonce);
            serializer.SerializeValue(ref ClientTick);
            serializer.SerializeValue(ref MoveX);
            serializer.SerializeValue(ref MoveZ);
            serializer.SerializeValue(ref AimYawDegrees);
            serializer.SerializeValue(ref AimPitchDegrees);
            serializer.SerializeValue(ref Fire);
            serializer.SerializeValue(ref ClaimedPosition);
            serializer.SerializeValue(ref JumpPressed);
            serializer.SerializeValue(ref SprintHeld);
            serializer.SerializeValue(ref CrouchRequested);
        }

        public bool Equals(NetcodePlayerCommand other)
        {
            return PlayerId == other.PlayerId && Sequence == other.Sequence &&
                Nonce == other.Nonce && ClientTick == other.ClientTick &&
                MoveX.Equals(other.MoveX) && MoveZ.Equals(other.MoveZ) &&
                AimYawDegrees.Equals(other.AimYawDegrees) &&
                AimPitchDegrees.Equals(other.AimPitchDegrees) &&
                Fire == other.Fire &&
                ClaimedPosition.Equals(other.ClaimedPosition) &&
                JumpPressed == other.JumpPressed &&
                SprintHeld == other.SprintHeld &&
                CrouchRequested == other.CrouchRequested;
        }
    }

    public struct NetcodePlayerState : INetworkSerializable,
        IEquatable<NetcodePlayerState>
    {
        public long ServerTick;
        public int PlayerId;
        public Vector3 Position;
        public float Health;
        public uint AcknowledgedSequence;
        public float AimYawDegrees;
        public float AimPitchDegrees;
        public Vector3 Velocity;
        public byte Stance;
        public bool Grounded;
        public long LastJumpTick;
        public float GroundHeight;

        public bool IsAlive => Health > 0f;

        public static NetcodePlayerState FromDomain(
            long serverTick,
            AuthoritativePlayerState value)
        {
            return new NetcodePlayerState
            {
                ServerTick = serverTick,
                PlayerId = value.PlayerId,
                Position = NetcodeConversions.ToUnity(value.Position),
                Health = (float)value.Health,
                AcknowledgedSequence = value.AcknowledgedSequence,
                AimYawDegrees = (float)value.AimYawDegrees,
                AimPitchDegrees = (float)value.AimPitchDegrees,
                Velocity = NetcodeConversions.ToUnity(value.Velocity),
                Stance = (byte)value.Stance,
                Grounded = value.Grounded,
                LastJumpTick = value.LastJumpTick,
                GroundHeight = (float)value.GroundHeight
            };
        }

        public AuthoritativePlayerState ToDomain()
        {
            return new AuthoritativePlayerState(
                PlayerId,
                NetcodeConversions.ToDomain(Position),
                Health,
                AcknowledgedSequence,
                AimYawDegrees,
                AimPitchDegrees,
                NetcodeConversions.ToDomain(Velocity),
                (PlayerStance)Stance,
                Grounded,
                LastJumpTick,
                GroundHeight);
        }

        public RemotePlayerSnapshot ToRemoteSnapshot()
        {
            return new RemotePlayerSnapshot(
                ServerTick,
                PlayerId,
                NetcodeConversions.ToDomain(Position),
                AimYawDegrees,
                AimPitchDegrees,
                NetcodeConversions.ToDomain(Velocity),
                (PlayerStance)Stance,
                Grounded);
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer)
            where T : IReaderWriter
        {
            serializer.SerializeValue(ref ServerTick);
            serializer.SerializeValue(ref PlayerId);
            serializer.SerializeValue(ref Position);
            serializer.SerializeValue(ref Health);
            serializer.SerializeValue(ref AcknowledgedSequence);
            serializer.SerializeValue(ref AimYawDegrees);
            serializer.SerializeValue(ref AimPitchDegrees);
            serializer.SerializeValue(ref Velocity);
            serializer.SerializeValue(ref Stance);
            serializer.SerializeValue(ref Grounded);
            serializer.SerializeValue(ref LastJumpTick);
            serializer.SerializeValue(ref GroundHeight);
        }

        public bool Equals(NetcodePlayerState other)
        {
            return ServerTick == other.ServerTick &&
                PlayerId == other.PlayerId && Position.Equals(other.Position) &&
                Health.Equals(other.Health) &&
                AcknowledgedSequence == other.AcknowledgedSequence &&
                AimYawDegrees.Equals(other.AimYawDegrees) &&
                AimPitchDegrees.Equals(other.AimPitchDegrees) &&
                Velocity.Equals(other.Velocity) &&
                Stance == other.Stance &&
                Grounded == other.Grounded &&
                LastJumpTick == other.LastJumpTick &&
                GroundHeight.Equals(other.GroundHeight);
        }
    }

    public struct NetcodeTargetState : INetworkSerializable,
        IEquatable<NetcodeTargetState>
    {
        public int TargetId;
        public Vector3 Position;
        public float Radius;
        public float Health;
        public FixedString64Bytes DropDefinitionId;

        public bool IsAlive => Health > 0f;

        public static NetcodeTargetState FromDomain(
            AuthoritativeTargetState value)
        {
            return new NetcodeTargetState
            {
                TargetId = value.TargetId,
                Position = NetcodeConversions.ToUnity(value.Position),
                Radius = (float)value.Radius,
                Health = (float)value.Health,
                DropDefinitionId = value.DropDefinitionId
            };
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer)
            where T : IReaderWriter
        {
            serializer.SerializeValue(ref TargetId);
            serializer.SerializeValue(ref Position);
            serializer.SerializeValue(ref Radius);
            serializer.SerializeValue(ref Health);
            serializer.SerializeValue(ref DropDefinitionId);
        }

        public bool Equals(NetcodeTargetState other)
        {
            return TargetId == other.TargetId &&
                Position.Equals(other.Position) && Radius.Equals(other.Radius) &&
                Health.Equals(other.Health) &&
                DropDefinitionId.Equals(other.DropDefinitionId);
        }
    }

    public struct NetcodeWorldState : INetworkSerializable,
        IEquatable<NetcodeWorldState>
    {
        public long ServerTick;
        public AuthoritativeWaveStatus WaveStatus;
        public int KilledTargets;
        public long LastEventSequence;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer)
            where T : IReaderWriter
        {
            serializer.SerializeValue(ref ServerTick);
            serializer.SerializeValue(ref WaveStatus);
            serializer.SerializeValue(ref KilledTargets);
            serializer.SerializeValue(ref LastEventSequence);
        }

        public bool Equals(NetcodeWorldState other)
        {
            return ServerTick == other.ServerTick &&
                WaveStatus == other.WaveStatus &&
                KilledTargets == other.KilledTargets &&
                LastEventSequence == other.LastEventSequence;
        }
    }

    public struct NetcodeAuthorityEvent : INetworkSerializable,
        IEquatable<NetcodeAuthorityEvent>
    {
        public long Tick;
        public long Sequence;
        public AuthoritativeEventKind Kind;
        public int SubjectId;
        public int TargetId;
        public float Value;
        public FixedString64Bytes DefinitionId;
        public CommandRejectionReason RejectionReason;

        public static NetcodeAuthorityEvent FromDomain(AuthoritativeEvent value)
        {
            return new NetcodeAuthorityEvent
            {
                Tick = value.Tick,
                Sequence = value.Sequence,
                Kind = value.Kind,
                SubjectId = value.SubjectId,
                TargetId = value.TargetId,
                Value = (float)value.Value,
                DefinitionId = value.DefinitionId,
                RejectionReason = value.RejectionReason
            };
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer)
            where T : IReaderWriter
        {
            serializer.SerializeValue(ref Tick);
            serializer.SerializeValue(ref Sequence);
            serializer.SerializeValue(ref Kind);
            serializer.SerializeValue(ref SubjectId);
            serializer.SerializeValue(ref TargetId);
            serializer.SerializeValue(ref Value);
            serializer.SerializeValue(ref DefinitionId);
            serializer.SerializeValue(ref RejectionReason);
        }

        public bool Equals(NetcodeAuthorityEvent other)
        {
            return Tick == other.Tick && Sequence == other.Sequence &&
                Kind == other.Kind && SubjectId == other.SubjectId &&
                TargetId == other.TargetId && Value.Equals(other.Value) &&
                DefinitionId.Equals(other.DefinitionId) &&
                RejectionReason == other.RejectionReason;
        }
    }

    public static class NetcodeConversions
    {
        public static Vector3 ToUnity(NetVector3 value)
        {
            return new Vector3((float)value.X, (float)value.Y, (float)value.Z);
        }

        public static NetVector3 ToDomain(Vector3 value)
        {
            return new NetVector3(value.x, value.y, value.z);
        }
    }
}
