using System;
using FPS.Networking.Domain;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace FPS.Networking.Netcode
{
    public enum NetworkPresentationAction : byte
    {
        None = 0,
        Shoot = 1,
        Reload = 2,
        SwitchWeapon = 3,
        Jump = 4
    }

    /// <summary>
    /// Server-side whitelist for identifiers that are allowed to cross the
    /// gameplay network boundary. Unknown appearance ids fall back safely;
    /// unknown weapon ids are rejected for live switch commands.
    /// </summary>
    public static class NetworkPresentationIds
    {
        public const string DefaultAppearance =
            "character.quaternius.male-light";
        public const string DefaultWeapon = "weapon.lpsp.ar";
        public const string Rifle = "weapon.lpsp.ar";
        public const string Handgun = "weapon.lpsp.handgun";
        public const string RifleGameplay = "weapon.rifle";
        public const string HandgunGameplay = "weapon.pistol";

        private static readonly string[] AppearanceWhitelist =
        {
            DefaultAppearance,
            "character.quaternius.male-dark",
            "character.quaternius.female-dark"
        };

        public static bool IsAllowedAppearance(string value)
        {
            string normalized = value?.Trim() ?? string.Empty;
            for (int index = 0; index < AppearanceWhitelist.Length; index++)
            {
                if (string.Equals(normalized, AppearanceWhitelist[index],
                        StringComparison.Ordinal)) return true;
            }
            return false;
        }

        public static string ResolveAppearance(string value) =>
            IsAllowedAppearance(value) ? value.Trim() : DefaultAppearance;

        public static bool TryResolveWeapon(
            string value,
            out string resolved)
        {
            string normalized = value?.Trim() ?? string.Empty;
            if (string.Equals(normalized, Rifle, StringComparison.Ordinal) ||
                string.Equals(normalized, "weapon.rifle",
                    StringComparison.Ordinal))
            {
                resolved = Rifle;
                return true;
            }
            if (string.Equals(normalized, Handgun, StringComparison.Ordinal) ||
                string.Equals(normalized, "weapon.pistol",
                    StringComparison.Ordinal))
            {
                resolved = Handgun;
                return true;
            }
            resolved = DefaultWeapon;
            return false;
        }

        public static string ResolveWeaponOrDefault(string value) =>
            TryResolveWeapon(value, out string resolved)
                ? resolved
                : DefaultWeapon;

        public static string ToGameplayWeaponId(string value)
        {
            string presentation = ResolveWeaponOrDefault(value);
            return string.Equals(presentation, Handgun,
                StringComparison.Ordinal)
                ? HandgunGameplay
                : RifleGameplay;
        }

        public static string ToPresentationWeaponId(string value)
        {
            string normalized = value?.Trim() ?? string.Empty;
            return string.Equals(normalized, HandgunGameplay,
                StringComparison.Ordinal)
                ? Handgun
                : Rifle;
        }
    }

    public struct NetcodePresentationCommand : INetworkSerializable,
        IEquatable<NetcodePresentationCommand>
    {
        public int PlayerId;
        public uint Sequence;
        public NetworkPresentationAction Action;
        public FixedString64Bytes WeaponId;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer)
            where T : IReaderWriter
        {
            serializer.SerializeValue(ref PlayerId);
            serializer.SerializeValue(ref Sequence);
            serializer.SerializeValue(ref Action);
            serializer.SerializeValue(ref WeaponId);
        }

        public bool Equals(NetcodePresentationCommand other) =>
            PlayerId == other.PlayerId && Sequence == other.Sequence &&
            Action == other.Action && WeaponId.Equals(other.WeaponId);
    }

    public struct NetcodePresentationEvent : INetworkSerializable,
        IEquatable<NetcodePresentationEvent>
    {
        public long ServerTick;
        public long Sequence;
        public int PlayerId;
        public NetworkPresentationAction Action;
        public FixedString64Bytes WeaponId;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer)
            where T : IReaderWriter
        {
            serializer.SerializeValue(ref ServerTick);
            serializer.SerializeValue(ref Sequence);
            serializer.SerializeValue(ref PlayerId);
            serializer.SerializeValue(ref Action);
            serializer.SerializeValue(ref WeaponId);
        }

        public bool Equals(NetcodePresentationEvent other) =>
            ServerTick == other.ServerTick && Sequence == other.Sequence &&
            PlayerId == other.PlayerId && Action == other.Action &&
            WeaponId.Equals(other.WeaponId);
    }

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
        public bool AimingHeld;
        public FixedString64Bytes WeaponId;
        public Vector3 ShotOrigin;

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
                CrouchRequested = value.CrouchRequested,
                WeaponId = value.WeaponId,
                ShotOrigin = NetcodeConversions.ToUnity(value.ShotOrigin)
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
                CrouchRequested,
                WeaponId.IsEmpty
                    ? NetworkPresentationIds.RifleGameplay
                    : WeaponId.ToString(),
                NetcodeConversions.ToDomain(ShotOrigin));
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
            serializer.SerializeValue(ref AimingHeld);
            serializer.SerializeValue(ref WeaponId);
            serializer.SerializeValue(ref ShotOrigin);
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
                CrouchRequested == other.CrouchRequested &&
                AimingHeld == other.AimingHeld &&
                WeaponId.Equals(other.WeaponId) &&
                ShotOrigin.Equals(other.ShotOrigin);
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
        public bool Sprinting;
        public bool Aiming;
        public FixedString64Bytes AppearanceId;
        public FixedString64Bytes WeaponId;
        public uint AcknowledgedPresentationCommandSequence;
        public long LastPresentationEventSequence;
        public FixedString64Bytes CombatWeaponId;
        public int MagazineAmmo;
        public int ReserveAmmo;
        public bool Reloading;
        public long ReloadEndTick;
        public bool Switching;
        public FixedString64Bytes PendingCombatWeaponId;
        public long SwitchEndTick;
        public long LastShotEventSequence;

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
                GroundHeight = (float)value.GroundHeight,
                AppearanceId = NetworkPresentationIds.DefaultAppearance,
                WeaponId = NetworkPresentationIds.DefaultWeapon,
                CombatWeaponId = value.EquippedWeaponId,
                MagazineAmmo = value.MagazineAmmo,
                ReserveAmmo = value.ReserveAmmo,
                Reloading = value.Reloading,
                ReloadEndTick = value.ReloadEndTick,
                Switching = value.Switching,
                PendingCombatWeaponId = value.PendingWeaponId,
                SwitchEndTick = value.SwitchEndTick
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
            serializer.SerializeValue(ref AcknowledgedSequence);
            serializer.SerializeValue(ref Stance);
            serializer.SerializeValue(ref LastJumpTick);
            serializer.SerializeValue(ref AppearanceId);
            serializer.SerializeValue(ref WeaponId);
            serializer.SerializeValue(
                ref AcknowledgedPresentationCommandSequence);
            serializer.SerializeValue(ref LastPresentationEventSequence);
            serializer.SerializeValue(ref CombatWeaponId);
            serializer.SerializeValue(ref MagazineAmmo);
            serializer.SerializeValue(ref ReserveAmmo);
            serializer.SerializeValue(ref Reloading);
            serializer.SerializeValue(ref ReloadEndTick);
            serializer.SerializeValue(ref Switching);
            serializer.SerializeValue(ref PendingCombatWeaponId);
            serializer.SerializeValue(ref SwitchEndTick);
            serializer.SerializeValue(ref LastShotEventSequence);

            int positionX = 0;
            int positionY = 0;
            int positionZ = 0;
            short velocityX = 0;
            short velocityY = 0;
            short velocityZ = 0;
            ushort health = 0;
            ushort yaw = 0;
            short pitch = 0;
            int groundHeight = 0;
            byte flags = 0;
            if (serializer.IsWriter)
            {
                positionX = QuantizeInt(Position.x, 100f);
                positionY = QuantizeInt(Position.y, 100f);
                positionZ = QuantizeInt(Position.z, 100f);
                velocityX = QuantizeShort(Velocity.x, 100f);
                velocityY = QuantizeShort(Velocity.y, 100f);
                velocityZ = QuantizeShort(Velocity.z, 100f);
                health = (ushort)Mathf.Clamp(
                    Mathf.RoundToInt(Health * 10f), 0, ushort.MaxValue);
                yaw = (ushort)Mathf.Clamp(Mathf.RoundToInt(
                    Mathf.Repeat(AimYawDegrees, 360f) * 100f), 0, 35999);
                pitch = QuantizeShort(
                    Mathf.Clamp(AimPitchDegrees, -89f, 89f), 100f);
                groundHeight = QuantizeInt(GroundHeight, 100f);
                if (Grounded) flags |= 1;
                if (Sprinting) flags |= 2;
                if (Aiming) flags |= 4;
            }
            serializer.SerializeValue(ref positionX);
            serializer.SerializeValue(ref positionY);
            serializer.SerializeValue(ref positionZ);
            serializer.SerializeValue(ref velocityX);
            serializer.SerializeValue(ref velocityY);
            serializer.SerializeValue(ref velocityZ);
            serializer.SerializeValue(ref health);
            serializer.SerializeValue(ref yaw);
            serializer.SerializeValue(ref pitch);
            serializer.SerializeValue(ref groundHeight);
            serializer.SerializeValue(ref flags);
            if (serializer.IsReader)
            {
                Position = new Vector3(
                    positionX / 100f,
                    positionY / 100f,
                    positionZ / 100f);
                Velocity = new Vector3(
                    velocityX / 100f,
                    velocityY / 100f,
                    velocityZ / 100f);
                Health = health / 10f;
                AimYawDegrees = yaw / 100f;
                AimPitchDegrees = pitch / 100f;
                GroundHeight = groundHeight / 100f;
                Grounded = (flags & 1) != 0;
                Sprinting = (flags & 2) != 0;
                Aiming = (flags & 4) != 0;
            }
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
                GroundHeight.Equals(other.GroundHeight) &&
                Sprinting == other.Sprinting && Aiming == other.Aiming &&
                AppearanceId.Equals(other.AppearanceId) &&
                WeaponId.Equals(other.WeaponId) &&
                AcknowledgedPresentationCommandSequence ==
                    other.AcknowledgedPresentationCommandSequence &&
                LastPresentationEventSequence ==
                    other.LastPresentationEventSequence &&
                CombatWeaponId.Equals(other.CombatWeaponId) &&
                MagazineAmmo == other.MagazineAmmo &&
                ReserveAmmo == other.ReserveAmmo &&
                Reloading == other.Reloading &&
                ReloadEndTick == other.ReloadEndTick &&
                Switching == other.Switching &&
                PendingCombatWeaponId.Equals(other.PendingCombatWeaponId) &&
                SwitchEndTick == other.SwitchEndTick &&
                LastShotEventSequence == other.LastShotEventSequence;
        }

        private static int QuantizeInt(float value, float scale)
        {
            double scaled = Math.Round((double)value * scale);
            return (int)Math.Max(int.MinValue,
                Math.Min(int.MaxValue, scaled));
        }

        private static short QuantizeShort(float value, float scale)
        {
            int scaled = Mathf.RoundToInt(value * scale);
            return (short)Mathf.Clamp(scaled, short.MinValue, short.MaxValue);
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
        public Vector3 HeadOffset;
        public float HeadRadius;
        public bool Active;
        public float YawDegrees;
        public AuthoritativeEnemyRole Role;
        public AuthoritativeEnemyBehavior Behavior;
        public int TargetPlayerId;
        public int SpawnGeneration;

        public bool IsAlive => Active && Health > 0f;

        public static NetcodeTargetState FromDomain(
            AuthoritativeTargetState value)
        {
            return new NetcodeTargetState
            {
                TargetId = value.TargetId,
                Position = NetcodeConversions.ToUnity(value.Position),
                Radius = (float)value.Radius,
                Health = (float)value.Health,
                DropDefinitionId = value.DropDefinitionId,
                HeadOffset = NetcodeConversions.ToUnity(value.HeadOffset),
                HeadRadius = (float)value.HeadRadius,
                Active = value.Active,
                YawDegrees = (float)value.YawDegrees,
                Role = value.Role,
                Behavior = value.Behavior,
                TargetPlayerId = value.TargetPlayerId,
                SpawnGeneration = value.SpawnGeneration
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
            serializer.SerializeValue(ref HeadOffset);
            serializer.SerializeValue(ref HeadRadius);
            serializer.SerializeValue(ref Active);
            serializer.SerializeValue(ref YawDegrees);
            serializer.SerializeValue(ref Role);
            serializer.SerializeValue(ref Behavior);
            serializer.SerializeValue(ref TargetPlayerId);
            serializer.SerializeValue(ref SpawnGeneration);
        }

        public bool Equals(NetcodeTargetState other)
        {
            return TargetId == other.TargetId &&
                Position.Equals(other.Position) && Radius.Equals(other.Radius) &&
                Health.Equals(other.Health) &&
                DropDefinitionId.Equals(other.DropDefinitionId) &&
                HeadOffset.Equals(other.HeadOffset) &&
                HeadRadius.Equals(other.HeadRadius) &&
                Active == other.Active &&
                YawDegrees.Equals(other.YawDegrees) &&
                Role == other.Role &&
                Behavior == other.Behavior &&
                TargetPlayerId == other.TargetPlayerId &&
                SpawnGeneration == other.SpawnGeneration;
        }
    }

    public struct NetcodeWorldState : INetworkSerializable,
        IEquatable<NetcodeWorldState>
    {
        public long ServerTick;
        public AuthoritativeWaveStatus WaveStatus;
        public int KilledTargets;
        public int RequiredKills;
        public int EnemyPoolCapacity;
        public int ActiveEnemyCount;
        public int PendingEnemyCount;
        public int RemainingEnemyCount;
        public long LastEventSequence;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer)
            where T : IReaderWriter
        {
            serializer.SerializeValue(ref ServerTick);
            serializer.SerializeValue(ref WaveStatus);
            serializer.SerializeValue(ref KilledTargets);
            serializer.SerializeValue(ref RequiredKills);
            serializer.SerializeValue(ref EnemyPoolCapacity);
            serializer.SerializeValue(ref ActiveEnemyCount);
            serializer.SerializeValue(ref PendingEnemyCount);
            serializer.SerializeValue(ref RemainingEnemyCount);
            serializer.SerializeValue(ref LastEventSequence);
        }

        public bool Equals(NetcodeWorldState other)
        {
            return ServerTick == other.ServerTick &&
                WaveStatus == other.WaveStatus &&
                KilledTargets == other.KilledTargets &&
                RequiredKills == other.RequiredKills &&
                EnemyPoolCapacity == other.EnemyPoolCapacity &&
                ActiveEnemyCount == other.ActiveEnemyCount &&
                PendingEnemyCount == other.PendingEnemyCount &&
                RemainingEnemyCount == other.RemainingEnemyCount &&
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

    public struct NetcodeShotFeedbackEvent : INetworkSerializable,
        IEquatable<NetcodeShotFeedbackEvent>
    {
        public long ServerTick;
        public long Sequence;
        public int ShooterPlayerId;
        public uint ShotCommandSequence;
        public FixedString64Bytes WeaponId;
        public ShotResolutionKind Kind;
        public int TargetId;
        public Vector3 Origin;
        public Vector3 EndPoint;
        public Vector3 Normal;
        public AuthoritativeHitRegion HitRegion;
        public AuthoritativeSurface Surface;
        public float AppliedDamage;

        public bool DidHit => Kind == ShotResolutionKind.Hit ||
            Kind == ShotResolutionKind.Killed;
        public bool DidImpact => DidHit ||
            Kind == ShotResolutionKind.Blocked;

        public static NetcodeShotFeedbackEvent FromDomain(
            long serverTick,
            long sequence,
            int shooterPlayerId,
            ShotResolution value)
        {
            return new NetcodeShotFeedbackEvent
            {
                ServerTick = serverTick,
                Sequence = sequence,
                ShooterPlayerId = shooterPlayerId,
                ShotCommandSequence = value.ShotSequence,
                WeaponId = value.WeaponId,
                Kind = value.Kind,
                TargetId = value.TargetId,
                Origin = NetcodeConversions.ToUnity(value.Origin),
                EndPoint = NetcodeConversions.ToUnity(value.EndPoint),
                Normal = NetcodeConversions.ToUnity(value.Normal),
                HitRegion = value.HitRegion,
                Surface = value.Surface,
                AppliedDamage = (float)value.AppliedDamage
            };
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer)
            where T : IReaderWriter
        {
            serializer.SerializeValue(ref ServerTick);
            serializer.SerializeValue(ref Sequence);
            serializer.SerializeValue(ref ShooterPlayerId);
            serializer.SerializeValue(ref ShotCommandSequence);
            serializer.SerializeValue(ref WeaponId);
            serializer.SerializeValue(ref Kind);
            serializer.SerializeValue(ref TargetId);
            serializer.SerializeValue(ref Origin);
            serializer.SerializeValue(ref EndPoint);
            serializer.SerializeValue(ref Normal);
            serializer.SerializeValue(ref HitRegion);
            serializer.SerializeValue(ref Surface);
            serializer.SerializeValue(ref AppliedDamage);
        }

        public bool Equals(NetcodeShotFeedbackEvent other)
        {
            return ServerTick == other.ServerTick &&
                Sequence == other.Sequence &&
                ShooterPlayerId == other.ShooterPlayerId &&
                ShotCommandSequence == other.ShotCommandSequence &&
                WeaponId.Equals(other.WeaponId) && Kind == other.Kind &&
                TargetId == other.TargetId && Origin.Equals(other.Origin) &&
                EndPoint.Equals(other.EndPoint) &&
                Normal.Equals(other.Normal) &&
                HitRegion == other.HitRegion && Surface == other.Surface &&
                AppliedDamage.Equals(other.AppliedDamage);
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
