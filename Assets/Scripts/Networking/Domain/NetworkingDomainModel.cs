using System;
using System.Collections.Generic;
using System.Linq;

namespace FPS.Networking.Domain
{
    public readonly struct NetVector3 : IEquatable<NetVector3>
    {
        public NetVector3(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public double X { get; }
        public double Y { get; }
        public double Z { get; }
        public double SqrMagnitude => X * X + Y * Y + Z * Z;
        public double Magnitude => Math.Sqrt(SqrMagnitude);
        public bool IsFinite => IsFiniteNumber(X) && IsFiniteNumber(Y) &&
            IsFiniteNumber(Z);

        public NetVector3 Normalized
        {
            get
            {
                double magnitude = Magnitude;
                return magnitude > 0.0000001d
                    ? this / magnitude
                    : new NetVector3(0d, 0d, 0d);
            }
        }

        public static NetVector3 operator +(NetVector3 left, NetVector3 right) =>
            new(left.X + right.X, left.Y + right.Y, left.Z + right.Z);
        public static NetVector3 operator -(NetVector3 left, NetVector3 right) =>
            new(left.X - right.X, left.Y - right.Y, left.Z - right.Z);
        public static NetVector3 operator *(NetVector3 value, double scale) =>
            new(value.X * scale, value.Y * scale, value.Z * scale);
        public static NetVector3 operator /(NetVector3 value, double scale) =>
            new(value.X / scale, value.Y / scale, value.Z / scale);

        public static double Dot(NetVector3 left, NetVector3 right) =>
            left.X * right.X + left.Y * right.Y + left.Z * right.Z;
        public static double Distance(NetVector3 left, NetVector3 right) =>
            (left - right).Magnitude;
        public static NetVector3 Lerp(
            NetVector3 from,
            NetVector3 to,
            double ratio)
        {
            double clamped = Math.Max(0d, Math.Min(1d, ratio));
            return from + (to - from) * clamped;
        }

        public bool Equals(NetVector3 other) =>
            X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z);
        public override bool Equals(object obj) =>
            obj is NetVector3 other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(X, Y, Z);
        public override string ToString() => $"({X:R},{Y:R},{Z:R})";

        private static bool IsFiniteNumber(double value) =>
            !double.IsNaN(value) && !double.IsInfinity(value);
    }

    public sealed class CoopServerRules
    {
        public CoopServerRules(
            int tickRate = 60,
            int maximumPastCommandTicks = 12,
            int maximumFutureCommandTicks = 1,
            int historyCapacity = 32,
            double maximumMoveSpeed = 6d,
            double claimedPositionTolerance = 0.35d,
            double maximumAimDegreesPerSecond = 1080d,
            int fireCooldownTicks = 6,
            double hitscanRange = 120d,
            double shotDamage = 34d,
            double predictionCorrectionThreshold = 0.15d,
            double predictionSnapThreshold = 2d,
            int nonceHistoryCapacity = 256,
            double? walkSpeed = null,
            double? sprintSpeed = null,
            double? crouchSpeed = null,
            double maximumAcceleration = 360d,
            double gravity = 20d,
            double jumpSpeed = 7.75d,
            int minimumJumpIntervalTicks = 12)
        {
            if (tickRate < 1 || tickRate > 1000)
                throw new ArgumentOutOfRangeException(nameof(tickRate));
            if (maximumPastCommandTicks < 0)
                throw new ArgumentOutOfRangeException(
                    nameof(maximumPastCommandTicks));
            if (maximumFutureCommandTicks < 0)
                throw new ArgumentOutOfRangeException(
                    nameof(maximumFutureCommandTicks));
            if (historyCapacity < maximumPastCommandTicks + 1)
                throw new ArgumentOutOfRangeException(nameof(historyCapacity));
            if (!PositiveFinite(maximumMoveSpeed))
                throw new ArgumentOutOfRangeException(nameof(maximumMoveSpeed));
            if (!NonNegativeFinite(claimedPositionTolerance))
                throw new ArgumentOutOfRangeException(
                    nameof(claimedPositionTolerance));
            if (!PositiveFinite(maximumAimDegreesPerSecond))
                throw new ArgumentOutOfRangeException(
                    nameof(maximumAimDegreesPerSecond));
            if (fireCooldownTicks < 1)
                throw new ArgumentOutOfRangeException(nameof(fireCooldownTicks));
            if (!PositiveFinite(hitscanRange))
                throw new ArgumentOutOfRangeException(nameof(hitscanRange));
            if (!PositiveFinite(shotDamage))
                throw new ArgumentOutOfRangeException(nameof(shotDamage));
            if (!NonNegativeFinite(predictionCorrectionThreshold))
                throw new ArgumentOutOfRangeException(
                    nameof(predictionCorrectionThreshold));
            if (!PositiveFinite(predictionSnapThreshold) ||
                predictionSnapThreshold < predictionCorrectionThreshold)
                throw new ArgumentOutOfRangeException(
                    nameof(predictionSnapThreshold));
            if (nonceHistoryCapacity < 8)
                throw new ArgumentOutOfRangeException(
                    nameof(nonceHistoryCapacity));
            if (!PositiveFinite(walkSpeed ?? maximumMoveSpeed))
                throw new ArgumentOutOfRangeException(nameof(walkSpeed));
            if (!PositiveFinite(sprintSpeed ?? maximumMoveSpeed))
                throw new ArgumentOutOfRangeException(nameof(sprintSpeed));
            if (!PositiveFinite(crouchSpeed ?? maximumMoveSpeed))
                throw new ArgumentOutOfRangeException(nameof(crouchSpeed));
            if (!PositiveFinite(maximumAcceleration))
                throw new ArgumentOutOfRangeException(
                    nameof(maximumAcceleration));
            if (!PositiveFinite(gravity))
                throw new ArgumentOutOfRangeException(nameof(gravity));
            if (!PositiveFinite(jumpSpeed))
                throw new ArgumentOutOfRangeException(nameof(jumpSpeed));
            if (minimumJumpIntervalTicks < 1)
                throw new ArgumentOutOfRangeException(
                    nameof(minimumJumpIntervalTicks));

            TickRate = tickRate;
            MaximumPastCommandTicks = maximumPastCommandTicks;
            MaximumFutureCommandTicks = maximumFutureCommandTicks;
            HistoryCapacity = historyCapacity;
            MaximumMoveSpeed = maximumMoveSpeed;
            ClaimedPositionTolerance = claimedPositionTolerance;
            MaximumAimDegreesPerSecond = maximumAimDegreesPerSecond;
            FireCooldownTicks = fireCooldownTicks;
            HitscanRange = hitscanRange;
            ShotDamage = shotDamage;
            PredictionCorrectionThreshold = predictionCorrectionThreshold;
            PredictionSnapThreshold = predictionSnapThreshold;
            NonceHistoryCapacity = nonceHistoryCapacity;
            WalkSpeed = walkSpeed ?? maximumMoveSpeed;
            SprintSpeed = sprintSpeed ?? maximumMoveSpeed;
            CrouchSpeed = crouchSpeed ?? maximumMoveSpeed;
            MaximumAcceleration = maximumAcceleration;
            Gravity = gravity;
            JumpSpeed = jumpSpeed;
            MinimumJumpIntervalTicks = minimumJumpIntervalTicks;
        }

        public int TickRate { get; }
        public double FixedDeltaSeconds => 1d / TickRate;
        public int MaximumPastCommandTicks { get; }
        public int MaximumFutureCommandTicks { get; }
        public int HistoryCapacity { get; }
        public double MaximumMoveSpeed { get; }
        public double ClaimedPositionTolerance { get; }
        public double MaximumAimDegreesPerSecond { get; }
        public int FireCooldownTicks { get; }
        public double HitscanRange { get; }
        public double ShotDamage { get; }
        public double PredictionCorrectionThreshold { get; }
        public double PredictionSnapThreshold { get; }
        public int NonceHistoryCapacity { get; }
        public double WalkSpeed { get; }
        public double SprintSpeed { get; }
        public double CrouchSpeed { get; }
        public double MaximumAcceleration { get; }
        public double Gravity { get; }
        public double JumpSpeed { get; }
        public int MinimumJumpIntervalTicks { get; }

        private static bool PositiveFinite(double value) =>
            value > 0d && !double.IsNaN(value) && !double.IsInfinity(value);
        private static bool NonNegativeFinite(double value) =>
            value >= 0d && !double.IsNaN(value) && !double.IsInfinity(value);
    }

    public enum PlayerStance : byte
    {
        Standing = 0,
        Crouching = 1
    }

    public readonly struct PlayerMovementState
    {
        public PlayerMovementState(
            NetVector3 position,
            NetVector3 velocity,
            double aimYawDegrees,
            double aimPitchDegrees,
            PlayerStance stance,
            bool grounded,
            long lastJumpTick,
            double groundHeight)
        {
            Position = position;
            Velocity = velocity;
            AimYawDegrees = aimYawDegrees;
            AimPitchDegrees = aimPitchDegrees;
            Stance = stance;
            Grounded = grounded;
            LastJumpTick = lastJumpTick;
            GroundHeight = groundHeight;
        }

        public NetVector3 Position { get; }
        public NetVector3 Velocity { get; }
        public double AimYawDegrees { get; }
        public double AimPitchDegrees { get; }
        public PlayerStance Stance { get; }
        public bool Grounded { get; }
        public long LastJumpTick { get; }
        public double GroundHeight { get; }
        public bool IsCrouching => Stance == PlayerStance.Crouching;
    }

    public readonly struct CoopPlayerSpawn
    {
        public CoopPlayerSpawn(int playerId, NetVector3 position,
            double health = 100d, double armor = 0d,
            double maximumArmor = 100d)
        {
            if (playerId <= 0) throw new ArgumentOutOfRangeException(nameof(playerId));
            if (!position.IsFinite) throw new ArgumentOutOfRangeException(nameof(position));
            if (health <= 0d || double.IsNaN(health) || double.IsInfinity(health))
                throw new ArgumentOutOfRangeException(nameof(health));
            if (armor < 0d || double.IsNaN(armor) || double.IsInfinity(armor))
                throw new ArgumentOutOfRangeException(nameof(armor));
            if (maximumArmor <= 0d || double.IsNaN(maximumArmor) ||
                double.IsInfinity(maximumArmor))
                throw new ArgumentOutOfRangeException(nameof(maximumArmor));
            PlayerId = playerId;
            Position = position;
            Health = health;
            Armor = Math.Min(armor, maximumArmor);
            MaximumArmor = maximumArmor;
        }

        public int PlayerId { get; }
        public NetVector3 Position { get; }
        public double Health { get; }
        public double Armor { get; }
        public double MaximumArmor { get; }
    }

    public readonly struct CoopTargetSpawn
    {
        public CoopTargetSpawn(
            int targetId,
            NetVector3 position,
            double radius,
            double health,
            string dropDefinitionId = "",
            NetVector3 headOffset = default,
            double headRadius = 0d,
            AuthoritativeEnemyRole role = AuthoritativeEnemyRole.Assault,
            long spawnTick = 0,
            double moveSpeed = 0d,
            double attackRange = 1.8d,
            double attackDamage = 0d,
            int attackIntervalTicks = 60,
            int rewardExperience = 0,
            int dropQuantity = 1)
        {
            if (targetId <= 0) throw new ArgumentOutOfRangeException(nameof(targetId));
            if (!position.IsFinite) throw new ArgumentOutOfRangeException(nameof(position));
            if (radius <= 0d || double.IsNaN(radius) || double.IsInfinity(radius))
                throw new ArgumentOutOfRangeException(nameof(radius));
            if (health <= 0d || double.IsNaN(health) || double.IsInfinity(health))
                throw new ArgumentOutOfRangeException(nameof(health));
            if (!headOffset.IsFinite)
                throw new ArgumentOutOfRangeException(nameof(headOffset));
            if (headRadius < 0d || double.IsNaN(headRadius) ||
                double.IsInfinity(headRadius))
                throw new ArgumentOutOfRangeException(nameof(headRadius));
            if (spawnTick < 0)
                throw new ArgumentOutOfRangeException(nameof(spawnTick));
            if (!NonNegativeFinite(moveSpeed))
                throw new ArgumentOutOfRangeException(nameof(moveSpeed));
            if (!PositiveFinite(attackRange))
                throw new ArgumentOutOfRangeException(nameof(attackRange));
            if (!NonNegativeFinite(attackDamage))
                throw new ArgumentOutOfRangeException(nameof(attackDamage));
            if (attackIntervalTicks < 1)
                throw new ArgumentOutOfRangeException(
                    nameof(attackIntervalTicks));
            if (rewardExperience < 0)
                throw new ArgumentOutOfRangeException(
                    nameof(rewardExperience));
            if (dropQuantity < 1)
                throw new ArgumentOutOfRangeException(nameof(dropQuantity));
            TargetId = targetId;
            Position = position;
            Radius = radius;
            Health = health;
            DropDefinitionId = dropDefinitionId ?? string.Empty;
            HeadOffset = headOffset;
            HeadRadius = headRadius;
            Role = role;
            SpawnTick = spawnTick;
            MoveSpeed = moveSpeed;
            AttackRange = attackRange;
            AttackDamage = attackDamage;
            AttackIntervalTicks = attackIntervalTicks;
            RewardExperience = rewardExperience;
            DropQuantity = dropQuantity;
        }

        public int TargetId { get; }
        public NetVector3 Position { get; }
        public double Radius { get; }
        public double Health { get; }
        public string DropDefinitionId { get; }
        public NetVector3 HeadOffset { get; }
        public double HeadRadius { get; }
        public AuthoritativeEnemyRole Role { get; }
        public long SpawnTick { get; }
        public double MoveSpeed { get; }
        public double AttackRange { get; }
        public double AttackDamage { get; }
        public int AttackIntervalTicks { get; }
        public int RewardExperience { get; }
        public int DropQuantity { get; }

        private static bool PositiveFinite(double value) =>
            value > 0d && !double.IsNaN(value) && !double.IsInfinity(value);
        private static bool NonNegativeFinite(double value) =>
            value >= 0d && !double.IsNaN(value) && !double.IsInfinity(value);
    }

    public readonly struct PlayerInputCommand
    {
        public PlayerInputCommand(
            int playerId,
            uint sequence,
            ulong nonce,
            long clientTick,
            double moveX,
            double moveZ,
            double aimYawDegrees,
            double aimPitchDegrees,
            bool fire,
            NetVector3 claimedPosition)
            : this(playerId, sequence, nonce, clientTick, moveX, moveZ,
                aimYawDegrees, aimPitchDegrees, fire, claimedPosition,
                jumpPressed: false, sprintHeld: false,
                crouchRequested: false)
        {
        }

        public PlayerInputCommand(
            int playerId,
            uint sequence,
            ulong nonce,
            long clientTick,
            double moveX,
            double moveZ,
            double aimYawDegrees,
            double aimPitchDegrees,
            bool fire,
            NetVector3 claimedPosition,
            bool jumpPressed,
            bool sprintHeld,
            bool crouchRequested)
            : this(playerId, sequence, nonce, clientTick, moveX, moveZ,
                aimYawDegrees, aimPitchDegrees, fire, claimedPosition,
                jumpPressed, sprintHeld, crouchRequested,
                "weapon.rifle", claimedPosition)
        {
        }

        public PlayerInputCommand(
            int playerId,
            uint sequence,
            ulong nonce,
            long clientTick,
            double moveX,
            double moveZ,
            double aimYawDegrees,
            double aimPitchDegrees,
            bool fire,
            NetVector3 claimedPosition,
            bool jumpPressed,
            bool sprintHeld,
            bool crouchRequested,
            string weaponId,
            NetVector3 shotOrigin)
        {
            PlayerId = playerId;
            Sequence = sequence;
            Nonce = nonce;
            ClientTick = clientTick;
            MoveX = moveX;
            MoveZ = moveZ;
            AimYawDegrees = aimYawDegrees;
            AimPitchDegrees = aimPitchDegrees;
            Fire = fire;
            ClaimedPosition = claimedPosition;
            JumpPressed = jumpPressed;
            SprintHeld = sprintHeld;
            CrouchRequested = crouchRequested;
            WeaponId = string.IsNullOrWhiteSpace(weaponId)
                ? "weapon.rifle"
                : weaponId.Trim();
            ShotOrigin = shotOrigin;
        }

        public int PlayerId { get; }
        public uint Sequence { get; }
        public ulong Nonce { get; }
        public long ClientTick { get; }
        public double MoveX { get; }
        public double MoveZ { get; }
        public double AimYawDegrees { get; }
        public double AimPitchDegrees { get; }
        public bool Fire { get; }
        public NetVector3 ClaimedPosition { get; }
        public bool JumpPressed { get; }
        public bool SprintHeld { get; }
        public bool CrouchRequested { get; }
        public string WeaponId { get; }
        public NetVector3 ShotOrigin { get; }
    }

    public enum CommandRejectionReason
    {
        None,
        UnknownPlayer,
        InvalidSequence,
        DuplicateNonce,
        TimestampTooOld,
        TimestampInFuture,
        DuplicateClientTick,
        InvalidMovement,
        ImpossibleDisplacement,
        InvalidAim,
        AimRateExceeded,
        FireRateExceeded,
        UnknownWeapon,
        WeaponMismatch,
        OutOfAmmo,
        Reloading,
        Switching,
        CannotReload,
        InvalidShotOrigin,
        LineOfSightBlocked,
        JumpRateExceeded,
        StanceBlocked
    }

    public enum ShotResolutionKind
    {
        NotRequested,
        Miss,
        Hit,
        Killed,
        Blocked
    }

    public readonly struct ShotResolution
    {
        public ShotResolution(
            ShotResolutionKind kind,
            long rewoundTick,
            int targetId,
            double appliedDamage)
            : this(kind, rewoundTick, targetId, appliedDamage,
                0, string.Empty, default, default, default,
                AuthoritativeHitRegion.None, AuthoritativeSurface.None)
        {
        }

        public ShotResolution(
            ShotResolutionKind kind,
            long rewoundTick,
            int targetId,
            double appliedDamage,
            uint shotSequence,
            string weaponId,
            NetVector3 origin,
            NetVector3 endPoint,
            NetVector3 normal,
            AuthoritativeHitRegion hitRegion,
            AuthoritativeSurface surface)
        {
            Kind = kind;
            RewoundTick = rewoundTick;
            TargetId = targetId;
            AppliedDamage = appliedDamage;
            ShotSequence = shotSequence;
            WeaponId = weaponId ?? string.Empty;
            Origin = origin;
            EndPoint = endPoint;
            Normal = normal;
            HitRegion = hitRegion;
            Surface = surface;
        }

        public ShotResolutionKind Kind { get; }
        public long RewoundTick { get; }
        public int TargetId { get; }
        public double AppliedDamage { get; }
        public uint ShotSequence { get; }
        public string WeaponId { get; }
        public NetVector3 Origin { get; }
        public NetVector3 EndPoint { get; }
        public NetVector3 Normal { get; }
        public AuthoritativeHitRegion HitRegion { get; }
        public AuthoritativeSurface Surface { get; }
        public bool DidHit => Kind == ShotResolutionKind.Hit ||
            Kind == ShotResolutionKind.Killed;
    }

    public sealed class CommandResolution
    {
        public CommandResolution(
            PlayerInputCommand command,
            bool accepted,
            CommandRejectionReason rejectionReason,
            ShotResolution shot)
        {
            Command = command;
            Accepted = accepted;
            RejectionReason = rejectionReason;
            Shot = shot;
        }

        public PlayerInputCommand Command { get; }
        public bool Accepted { get; }
        public CommandRejectionReason RejectionReason { get; }
        public ShotResolution Shot { get; }
    }

    public enum AuthoritativeEventKind
    {
        PlayerMoved,
        ShotMissed,
        TargetDamaged,
        TargetKilled,
        LootDropped,
        PlayerDamaged,
        PlayerKilled,
        WaveCompleted,
        WaveFailed,
        ReloadStarted,
        ReloadCompleted,
        WeaponSwitchStarted,
        WeaponSwitchCompleted,
        CommandRejected,
        TargetSpawned,
        TargetBehaviorChanged,
        TargetAttacked,
        WorldDropSpawned,
        WorldDropClaimed,
        InventoryChanged,
        ConsumableUsed,
        ExperienceGranted,
        PlayerLevelGained,
        UpgradeChoicesOffered,
        UpgradeApplied,
        EconomyCommandRejected,
        MissionPhaseChanged,
        MissionCommandRejected,
        TerminalInteractionStarted,
        TerminalInteractionCompleted,
        PlayerDowned,
        ReviveStarted,
        PlayerRevived,
        ExtractionStarted,
        MissionSucceeded,
        MissionFailed,
        PlayerDisconnected
    }

    public readonly struct AuthoritativeEvent
    {
        public AuthoritativeEvent(
            long tick,
            long sequence,
            AuthoritativeEventKind kind,
            int subjectId,
            int targetId,
            double value,
            string definitionId,
            CommandRejectionReason rejectionReason)
        {
            Tick = tick;
            Sequence = sequence;
            Kind = kind;
            SubjectId = subjectId;
            TargetId = targetId;
            Value = value;
            DefinitionId = definitionId ?? string.Empty;
            RejectionReason = rejectionReason;
        }

        public long Tick { get; }
        public long Sequence { get; }
        public AuthoritativeEventKind Kind { get; }
        public int SubjectId { get; }
        public int TargetId { get; }
        public double Value { get; }
        public string DefinitionId { get; }
        public CommandRejectionReason RejectionReason { get; }
    }

    public readonly struct AuthoritativePlayerState
    {
        public AuthoritativePlayerState(
            int playerId,
            NetVector3 position,
            double health,
            uint acknowledgedSequence,
            double aimYawDegrees,
            double aimPitchDegrees)
            : this(playerId, position, health, acknowledgedSequence,
                aimYawDegrees, aimPitchDegrees,
                new NetVector3(0d, 0d, 0d),
                PlayerStance.Standing, grounded: true,
                lastJumpTick: long.MinValue,
                groundHeight: position.Y)
        {
        }

        public AuthoritativePlayerState(
            int playerId,
            NetVector3 position,
            double health,
            uint acknowledgedSequence,
            double aimYawDegrees,
            double aimPitchDegrees,
            NetVector3 velocity,
            PlayerStance stance,
            bool grounded,
            long lastJumpTick,
            double groundHeight)
            : this(playerId, position, health, acknowledgedSequence,
                aimYawDegrees, aimPitchDegrees, velocity, stance, grounded,
                lastJumpTick, groundHeight, "weapon.rifle", 0, 0,
                false, 0, false, string.Empty, 0,
                Array.Empty<AuthoritativeWeaponState>())
        {
        }

        public AuthoritativePlayerState(
            int playerId,
            NetVector3 position,
            double health,
            uint acknowledgedSequence,
            double aimYawDegrees,
            double aimPitchDegrees,
            NetVector3 velocity,
            PlayerStance stance,
            bool grounded,
            long lastJumpTick,
            double groundHeight,
            string equippedWeaponId,
            int magazineAmmo,
            int reserveAmmo,
            bool reloading,
            long reloadEndTick,
            bool switching,
            string pendingWeaponId,
            long switchEndTick,
            IReadOnlyList<AuthoritativeWeaponState> weapons,
            double maximumHealth = 0d,
            double armor = 0d,
            double maximumArmor = 100d,
            AuthoritativePlayerLifeState lifeState =
                AuthoritativePlayerLifeState.Alive)
        {
            PlayerId = playerId;
            Position = position;
            Health = health;
            AcknowledgedSequence = acknowledgedSequence;
            AimYawDegrees = aimYawDegrees;
            AimPitchDegrees = aimPitchDegrees;
            Velocity = velocity;
            Stance = stance;
            Grounded = grounded;
            LastJumpTick = lastJumpTick;
            GroundHeight = groundHeight;
            EquippedWeaponId = equippedWeaponId ?? string.Empty;
            MagazineAmmo = Math.Max(0, magazineAmmo);
            ReserveAmmo = Math.Max(0, reserveAmmo);
            Reloading = reloading;
            ReloadEndTick = reloadEndTick;
            Switching = switching;
            PendingWeaponId = pendingWeaponId ?? string.Empty;
            SwitchEndTick = switchEndTick;
            Weapons = weapons ?? Array.Empty<AuthoritativeWeaponState>();
            MaximumHealth = maximumHealth > 0d ? maximumHealth : health;
            Armor = Math.Max(0d, Math.Min(armor, maximumArmor));
            MaximumArmor = Math.Max(0d, maximumArmor);
            LifeState = lifeState;
        }

        public int PlayerId { get; }
        public NetVector3 Position { get; }
        public double Health { get; }
        public uint AcknowledgedSequence { get; }
        public double AimYawDegrees { get; }
        public double AimPitchDegrees { get; }
        public NetVector3 Velocity { get; }
        public PlayerStance Stance { get; }
        public bool Grounded { get; }
        public long LastJumpTick { get; }
        public double GroundHeight { get; }
        public string EquippedWeaponId { get; }
        public int MagazineAmmo { get; }
        public int ReserveAmmo { get; }
        public bool Reloading { get; }
        public long ReloadEndTick { get; }
        public bool Switching { get; }
        public string PendingWeaponId { get; }
        public long SwitchEndTick { get; }
        public IReadOnlyList<AuthoritativeWeaponState> Weapons { get; }
        public double MaximumHealth { get; }
        public double Armor { get; }
        public double MaximumArmor { get; }
        public AuthoritativePlayerLifeState LifeState { get; }
        public bool IsCrouching => Stance == PlayerStance.Crouching;
        public bool IsAlive => LifeState == AuthoritativePlayerLifeState.Alive &&
            Health > 0d;
        public bool IsConnected =>
            LifeState != AuthoritativePlayerLifeState.Disconnected;
        public bool IsDowned =>
            LifeState == AuthoritativePlayerLifeState.Downed;

        public PlayerMovementState Movement => new(
            Position,
            Velocity,
            AimYawDegrees,
            AimPitchDegrees,
            Stance,
            Grounded,
            LastJumpTick,
            GroundHeight);
    }

    public readonly struct AuthoritativeTargetState
    {
        public AuthoritativeTargetState(
            int targetId,
            NetVector3 position,
            double radius,
            double health,
            string dropDefinitionId,
            NetVector3 headOffset = default,
            double headRadius = 0d,
            bool active = true,
            double yawDegrees = 0d,
            AuthoritativeEnemyRole role = AuthoritativeEnemyRole.Assault,
            AuthoritativeEnemyBehavior behavior =
                AuthoritativeEnemyBehavior.Patrol,
            int targetPlayerId = 0,
            int spawnGeneration = 1)
        {
            TargetId = targetId;
            Position = position;
            Radius = radius;
            Health = health;
            DropDefinitionId = dropDefinitionId ?? string.Empty;
            HeadOffset = headOffset;
            HeadRadius = headRadius;
            Active = active;
            YawDegrees = yawDegrees;
            Role = role;
            Behavior = behavior;
            TargetPlayerId = targetPlayerId;
            SpawnGeneration = Math.Max(0, spawnGeneration);
        }

        public int TargetId { get; }
        public NetVector3 Position { get; }
        public double Radius { get; }
        public double Health { get; }
        public string DropDefinitionId { get; }
        public NetVector3 HeadOffset { get; }
        public double HeadRadius { get; }
        public bool Active { get; }
        public double YawDegrees { get; }
        public AuthoritativeEnemyRole Role { get; }
        public AuthoritativeEnemyBehavior Behavior { get; }
        public int TargetPlayerId { get; }
        public int SpawnGeneration { get; }
        public bool IsAlive => Active && Health > 0d;
    }

    public enum AuthoritativeWaveStatus
    {
        Fighting = 0,
        Completed = 1,
        Failed = 2,
        Spawning = 3
    }

    public sealed class AuthoritativeWorldSnapshot
    {
        private readonly AuthoritativePlayerState[] players;
        private readonly AuthoritativeTargetState[] targets;

        public AuthoritativeWorldSnapshot(
            long tick,
            IEnumerable<AuthoritativePlayerState> players,
            IEnumerable<AuthoritativeTargetState> targets,
            AuthoritativeWaveStatus waveStatus,
            int killedTargets,
            int requiredKills = 0,
            AuthoritativeEconomySnapshot economy = null,
            AuthoritativeMissionState mission = null)
        {
            Tick = tick;
            this.players = (players ?? throw new ArgumentNullException(nameof(players)))
                .OrderBy(value => value.PlayerId).ToArray();
            this.targets = (targets ?? throw new ArgumentNullException(nameof(targets)))
                .OrderBy(value => value.TargetId).ToArray();
            WaveStatus = waveStatus;
            KilledTargets = Math.Max(0, killedTargets);
            RequiredKills = requiredKills <= 0
                ? this.targets.Length
                : Math.Min(requiredKills, this.targets.Length);
            Economy = economy ?? new AuthoritativeEconomySnapshot(
                Array.Empty<AuthoritativeInventorySlotState>(),
                Array.Empty<AuthoritativeWorldDropState>(),
                Array.Empty<AuthoritativeProgressionState>(),
                Array.Empty<AuthoritativeUpgradeStackState>());
            Mission = mission ?? new AuthoritativeMissionState(
                waveStatus == AuthoritativeWaveStatus.Failed
                    ? AuthoritativeMissionPhase.Defeat
                    : AuthoritativeMissionPhase.ClearEnemies,
                waveStatus == AuthoritativeWaveStatus.Failed
                    ? AuthoritativeMissionOutcomeReason.SquadWiped
                    : AuthoritativeMissionOutcomeReason.None,
                1, 0, 0, 0, 0, 0, 0,
                AuthoritativeMissionDefinition.Default,
                this.players.Select(value =>
                    new AuthoritativePlayerMissionStats(
                        value.PlayerId, 0, 0d, 0d, 0)));
        }

        public long Tick { get; }
        public IReadOnlyList<AuthoritativePlayerState> Players => players;
        public IReadOnlyList<AuthoritativeTargetState> Targets => targets;
        public AuthoritativeWaveStatus WaveStatus { get; }
        public int KilledTargets { get; }
        public int RequiredKills { get; }
        public AuthoritativeEconomySnapshot Economy { get; }
        public AuthoritativeMissionState Mission { get; }
        public int EnemyPoolCapacity => targets.Length;
        public int SpawnedTargets => targets.Count(value =>
            value.SpawnGeneration > 0);
        public int ActiveTargets => targets.Count(value => value.IsAlive);
        public int PendingTargets => targets.Count(value =>
            value.SpawnGeneration == 0);
        public int RemainingTargets => Math.Max(0,
            RequiredKills - KilledTargets);
        public AuthoritativePlayerState Player(int playerId) =>
            players.Single(value => value.PlayerId == playerId);
        public AuthoritativeTargetState Target(int targetId) =>
            targets.Single(value => value.TargetId == targetId);
    }

    public sealed class AuthoritativeTickResult
    {
        private readonly CommandResolution[] commands;
        private readonly AuthoritativeEvent[] events;
        private readonly AuthoritativeEconomyResolution[] economyCommands;
        private readonly AuthoritativeMissionResolution[] missionCommands;

        public AuthoritativeTickResult(
            long tick,
            IEnumerable<CommandResolution> commands,
            IEnumerable<AuthoritativeEvent> events,
            AuthoritativeWorldSnapshot snapshot,
            IEnumerable<AuthoritativeEconomyResolution> economyCommands = null,
            IEnumerable<AuthoritativeMissionResolution> missionCommands = null)
        {
            Tick = tick;
            this.commands = (commands ?? Array.Empty<CommandResolution>()).ToArray();
            this.events = (events ?? Array.Empty<AuthoritativeEvent>()).ToArray();
            this.economyCommands = (economyCommands ??
                Array.Empty<AuthoritativeEconomyResolution>()).ToArray();
            this.missionCommands = (missionCommands ??
                Array.Empty<AuthoritativeMissionResolution>()).ToArray();
            Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        }

        public long Tick { get; }
        public IReadOnlyList<CommandResolution> Commands => commands;
        public IReadOnlyList<AuthoritativeEvent> Events => events;
        public IReadOnlyList<AuthoritativeEconomyResolution> EconomyCommands =>
            economyCommands;
        public IReadOnlyList<AuthoritativeMissionResolution> MissionCommands =>
            missionCommands;
        public AuthoritativeWorldSnapshot Snapshot { get; }
    }
}
