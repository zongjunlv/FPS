using System;
using System.Collections.Generic;
using System.Linq;

namespace FPS.Networking.Domain
{
    /// <summary>
    /// The sole gameplay rule implementation shared by authoritative movement
    /// and local prediction. Transport and presentation must not reproduce it.
    /// </summary>
    public static class CoopGameplayRules
    {
        public static NetVector3 IntegrateMovement(
            NetVector3 position,
            double moveX,
            double moveZ,
            int elapsedTicks,
            CoopServerRules rules)
        {
            if (rules == null) throw new ArgumentNullException(nameof(rules));
            double magnitude = Math.Sqrt(moveX * moveX + moveZ * moveZ);
            double scale = magnitude > 1d ? 1d / magnitude : 1d;
            return position + new NetVector3(
                moveX * scale,
                0d,
                moveZ * scale) * (rules.MaximumMoveSpeed *
                    rules.FixedDeltaSeconds * Math.Max(1, elapsedTicks));
        }

        public static PlayerMovementState IntegrateMovement(
            PlayerMovementState state,
            PlayerInputCommand command,
            int elapsedTicks,
            CoopServerRules rules,
            bool standingClearance = true)
        {
            if (rules == null) throw new ArgumentNullException(nameof(rules));
            int ticks = Math.Max(1, elapsedTicks);
            PlayerStance stance = command.CrouchRequested ||
                                   state.IsCrouching && !standingClearance
                ? PlayerStance.Crouching
                : PlayerStance.Standing;
            NetVector3 position = state.Position;
            NetVector3 velocity = state.Velocity;
            bool grounded = state.Grounded;
            long lastJumpTick = state.LastJumpTick;
            double inputMagnitude = Math.Sqrt(
                command.MoveX * command.MoveX +
                command.MoveZ * command.MoveZ);
            double inputScale = inputMagnitude > 1d
                ? 1d / inputMagnitude
                : 1d;
            double yaw = command.AimYawDegrees * Math.PI / 180d;
            double localX = command.MoveX * inputScale;
            double localZ = command.MoveZ * inputScale;
            double worldX = Math.Cos(yaw) * localX + Math.Sin(yaw) * localZ;
            double worldZ = -Math.Sin(yaw) * localX + Math.Cos(yaw) * localZ;
            bool sprinting = command.SprintHeld &&
                             stance == PlayerStance.Standing &&
                             command.MoveZ > 0.1d;
            double targetSpeed = stance == PlayerStance.Crouching
                ? rules.CrouchSpeed
                : sprinting
                    ? rules.SprintSpeed
                    : rules.WalkSpeed;
            NetVector3 targetHorizontal = new(
                worldX * targetSpeed,
                0d,
                worldZ * targetSpeed);

            for (int index = 0; index < ticks; index++)
            {
                double maxVelocityChange = rules.MaximumAcceleration *
                                           rules.FixedDeltaSeconds;
                double horizontalX = MoveTowards(
                    velocity.X, targetHorizontal.X, maxVelocityChange);
                double horizontalZ = MoveTowards(
                    velocity.Z, targetHorizontal.Z, maxVelocityChange);
                double vertical = velocity.Y;
                if (index == 0 && command.JumpPressed && grounded)
                {
                    vertical = rules.JumpSpeed;
                    grounded = false;
                    lastJumpTick = command.ClientTick;
                }
                if (!grounded)
                    vertical -= rules.Gravity * rules.FixedDeltaSeconds;

                velocity = new NetVector3(horizontalX, vertical, horizontalZ);
                position += velocity * rules.FixedDeltaSeconds;
                if (position.Y <= state.GroundHeight)
                {
                    position = new NetVector3(
                        position.X, state.GroundHeight, position.Z);
                    velocity = new NetVector3(velocity.X, 0d, velocity.Z);
                    grounded = true;
                }
            }

            return new PlayerMovementState(
                position,
                velocity,
                command.AimYawDegrees,
                command.AimPitchDegrees,
                stance,
                grounded,
                lastJumpTick,
                state.GroundHeight);
        }

        public static NetVector3 AimDirection(
            double yawDegrees,
            double pitchDegrees)
        {
            double yaw = yawDegrees * Math.PI / 180d;
            double pitch = pitchDegrees * Math.PI / 180d;
            double horizontal = Math.Cos(pitch);
            return new NetVector3(
                Math.Sin(yaw) * horizontal,
                -Math.Sin(pitch),
                Math.Cos(yaw) * horizontal).Normalized;
        }

        public static double ShortestAngleDelta(
            double fromDegrees,
            double toDegrees)
        {
            double delta = (toDegrees - fromDegrees) % 360d;
            if (delta > 180d) delta -= 360d;
            if (delta < -180d) delta += 360d;
            return delta;
        }

        public static double ApplyDamage(double health, double damage)
        {
            if (!Finite(health) || health < 0d)
                throw new ArgumentOutOfRangeException(nameof(health));
            if (!Finite(damage))
                throw new ArgumentOutOfRangeException(nameof(damage));
            return Math.Max(0d, health - Math.Max(0d, damage));
        }

        internal static bool Finite(double value) =>
            !double.IsNaN(value) && !double.IsInfinity(value);

        private static double MoveTowards(
            double current,
            double target,
            double maximumDelta)
        {
            double delta = target - current;
            if (Math.Abs(delta) <= maximumDelta) return target;
            return current + Math.Sign(delta) * maximumDelta;
        }
    }

    /// <summary>
    /// Deterministic two-player/PVE authoritative kernel. Callers provide all
    /// commands received for one server tick; the kernel validates, sorts and
    /// resolves them without reading wall-clock time.
    /// </summary>
    public sealed class AuthoritativeCoopSimulation
    {
        private const int EnemyRecycleDelayTicks = 15;
        private const int InteractionHeartbeatGraceTicks = 6;
        private readonly CoopServerRules rules;
        private readonly Dictionary<int, MutablePlayer> players;
        private readonly Dictionary<int, MutableTarget> targets;
        private readonly Dictionary<string, AuthoritativeWeaponDefinition>
            weaponDefinitions;
        private readonly AuthoritativeCoopEconomy economy;
        private readonly AuthoritativeMissionDefinition missionDefinition;
        private readonly Dictionary<int, MutableMissionStats> missionStats;
        private readonly LinkedList<HistoryFrame> history = new();
        private readonly int requiredKills;
        private long currentTick;
        private long nextEventSequence;
        private int killedTargets;
        private Func<int, NetVector3, bool> standingClearanceValidator =
            (_, _) => true;
        private Func<int, NetVector3, NetVector3,
            AuthoritativeShotObstruction> shotObstructionResolver =
            (_, _, _) => AuthoritativeShotObstruction.Clear;
        private Func<int, NetVector3, NetVector3, NetVector3>
            enemyMovementResolver = (_, _, desired) => desired;
        private AuthoritativeWaveStatus waveStatus =
            AuthoritativeWaveStatus.Fighting;
        private AuthoritativeMissionPhase missionPhase =
            AuthoritativeMissionPhase.ClearEnemies;
        private AuthoritativeMissionOutcomeReason missionOutcomeReason;
        private int missionRevision = 1;
        private int terminalProgressTicks;
        private int extractionProgressTicks;
        private int reviveProgressTicks;
        private int terminalPlayerId;
        private int revivePlayerId;
        private int downedPlayerId;
        private long terminalHeartbeatTick = long.MinValue;
        private long reviveHeartbeatTick = long.MinValue;
        private bool extractionStarted;

        public AuthoritativeCoopSimulation(
            CoopServerRules rules,
            IEnumerable<CoopPlayerSpawn> configuredPlayers,
            IEnumerable<CoopTargetSpawn> configuredTargets,
            int requiredKills = 0,
            IEnumerable<AuthoritativeWeaponDefinition> configuredWeapons = null,
            IEnumerable<AuthoritativeItemDefinition> configuredItems = null,
            IEnumerable<AuthoritativeUpgradeDefinition> configuredUpgrades = null,
            int runSeed = 18018,
            int inventoryCapacity = 12,
            AuthoritativeMissionDefinition configuredMission = null)
        {
            this.rules = rules ?? throw new ArgumentNullException(nameof(rules));
            weaponDefinitions = (configuredWeapons ??
                    AuthoritativeWeaponDefinition.CreateProjectDefaults(rules))
                .ToDictionary(value => value.WeaponId,
                    StringComparer.Ordinal);
            if (weaponDefinitions.Count == 0 ||
                !weaponDefinitions.ContainsKey("weapon.rifle"))
                throw new ArgumentException(
                    "The authoritative loadout requires weapon.rifle.",
                    nameof(configuredWeapons));
            players = (configuredPlayers ?? throw new ArgumentNullException(
                    nameof(configuredPlayers)))
                .Select(value => new MutablePlayer(value, weaponDefinitions))
                .ToDictionary(value => value.Id);
            targets = (configuredTargets ?? throw new ArgumentNullException(
                    nameof(configuredTargets)))
                .Select(value => new MutableTarget(value))
                .ToDictionary(value => value.Id);
            if (players.Count < 1 || players.Count > 2)
                throw new ArgumentException(
                    "The vertical slice supports one or two players.",
                    nameof(configuredPlayers));
            if (targets.Count == 0)
                throw new ArgumentException(
                    "At least one target is required.",
                    nameof(configuredTargets));
            economy = new AuthoritativeCoopEconomy(
                players.Keys,
                configuredItems,
                configuredUpgrades,
                runSeed,
                inventoryCapacity);
            missionDefinition = configuredMission ??
                AuthoritativeMissionDefinition.Default;
            missionStats = players.Keys.ToDictionary(
                value => value,
                value => new MutableMissionStats(value));
            if (requiredKills < 0 || requiredKills > targets.Count)
                throw new ArgumentOutOfRangeException(nameof(requiredKills));
            this.requiredKills = requiredKills == 0
                ? targets.Count
                : requiredKills;
            waveStatus = targets.Values.Any(value => value.IsPending)
                ? AuthoritativeWaveStatus.Spawning
                : AuthoritativeWaveStatus.Fighting;
            CaptureHistory(0);
        }

        public CoopServerRules Rules => rules;
        public long CurrentTick => currentTick;
        public int HistoryCount => history.Count;
        public AuthoritativeWaveStatus WaveStatus => waveStatus;
        public int EnemyPoolCapacity => targets.Count;
        public int ActiveEnemyCount =>
            targets.Values.Count(value => value.IsAlive);
        public int PendingEnemyCount =>
            targets.Values.Count(value => value.IsPending);
        public int AvailableEnemySlots =>
            targets.Count - ActiveEnemyCount;
        private bool IsMissionOutcome =>
            missionPhase == AuthoritativeMissionPhase.Victory ||
            missionPhase == AuthoritativeMissionPhase.Defeat;

        public void SetStandingClearanceValidator(
            Func<int, NetVector3, bool> validator)
        {
            standingClearanceValidator = validator ?? ((_, _) => true);
        }

        public void SetShotLineOfSightValidator(
            Func<int, NetVector3, NetVector3, bool> validator)
        {
            shotObstructionResolver = validator == null
                ? (_, _, _) => AuthoritativeShotObstruction.Clear
                : (playerId, origin, endPoint) =>
                    validator(playerId, origin, endPoint)
                        ? AuthoritativeShotObstruction.Clear
                        : AuthoritativeShotObstruction.At(
                            endPoint,
                            default,
                            AuthoritativeSurface.Concrete);
        }

        public void SetShotObstructionResolver(
            Func<int, NetVector3, NetVector3,
                AuthoritativeShotObstruction> resolver)
        {
            shotObstructionResolver = resolver ??
                ((_, _, _) => AuthoritativeShotObstruction.Clear);
        }

        public void SetEnemyMovementResolver(
            Func<int, NetVector3, NetVector3, NetVector3> resolver)
        {
            enemyMovementResolver = resolver ??
                ((_, _, desired) => desired);
        }

        public WeaponActionResolution ApplyWeaponAction(
            int playerId,
            AuthoritativeWeaponAction action,
            string requestedWeaponId,
            ICollection<AuthoritativeEvent> events = null)
        {
            if (!players.TryGetValue(playerId, out MutablePlayer player))
                return new WeaponActionResolution(false,
                    CommandRejectionReason.UnknownPlayer, action,
                    requestedWeaponId);
            if (IsMissionOutcome)
                return new WeaponActionResolution(false,
                    CommandRejectionReason.InvalidMovement, action,
                    requestedWeaponId);
            if (!player.IsCombatActive)
                return new WeaponActionResolution(false,
                    CommandRejectionReason.InvalidMovement, action,
                    requestedWeaponId);

            if (action == AuthoritativeWeaponAction.Reload)
            {
                MutableWeapon weapon = player.EquippedWeapon;
                if (player.IsSwitching)
                    return RejectWeaponAction(action, weapon.Definition.WeaponId,
                        CommandRejectionReason.Switching);
                if (weapon.IsReloading)
                    return RejectWeaponAction(action, weapon.Definition.WeaponId,
                        CommandRejectionReason.Reloading);
                int magazineCapacity = Math.Max(1, (int)Math.Round(
                    weapon.Definition.MagazineCapacity *
                    economy.Modifier(playerId,
                        AuthoritativeUpgradeEffect.MagazineCapacity)));
                if (weapon.MagazineAmmo >= magazineCapacity ||
                    weapon.ReserveAmmo <= 0)
                    return RejectWeaponAction(action, weapon.Definition.WeaponId,
                        CommandRejectionReason.CannotReload);
                weapon.ReloadEndTick = currentTick +
                    Math.Max(1, (int)Math.Ceiling(
                        weapon.Definition.ReloadDurationTicks /
                        economy.Modifier(playerId,
                            AuthoritativeUpgradeEffect.ReloadSpeed)));
                events?.Add(Emit(AuthoritativeEventKind.ReloadStarted,
                    playerId, 0, weapon.ReloadEndTick,
                    weapon.Definition.WeaponId));
                return new WeaponActionResolution(true,
                    CommandRejectionReason.None, action,
                    weapon.Definition.WeaponId);
            }

            string normalized = requestedWeaponId?.Trim() ?? string.Empty;
            if (!weaponDefinitions.ContainsKey(normalized) ||
                !player.Weapons.ContainsKey(normalized))
                return RejectWeaponAction(action, normalized,
                    CommandRejectionReason.UnknownWeapon);
            if (player.IsSwitching)
                return RejectWeaponAction(action, normalized,
                    CommandRejectionReason.Switching);
            if (string.Equals(player.EquippedWeaponId, normalized,
                    StringComparison.Ordinal))
                return RejectWeaponAction(action, normalized,
                    CommandRejectionReason.WeaponMismatch);

            player.EquippedWeapon.CancelReload();
            player.PendingWeaponId = normalized;
            player.SwitchEndTick = currentTick +
                weaponDefinitions[normalized].SwitchDurationTicks;
            events?.Add(Emit(AuthoritativeEventKind.WeaponSwitchStarted,
                playerId, 0, player.SwitchEndTick, normalized));
            return new WeaponActionResolution(true,
                CommandRejectionReason.None, action, normalized);
        }

        public AuthoritativeTickResult Step(
            IReadOnlyList<PlayerInputCommand> receivedCommands)
        {
            return Step(receivedCommands,
                Array.Empty<AuthoritativeEconomyCommand>());
        }

        public AuthoritativeTickResult Step(
            IReadOnlyList<PlayerInputCommand> receivedCommands,
            IReadOnlyList<AuthoritativeEconomyCommand> economyCommands)
        {
            return Step(receivedCommands, economyCommands,
                Array.Empty<AuthoritativeMissionCommand>());
        }

        public AuthoritativeTickResult Step(
            IReadOnlyList<PlayerInputCommand> receivedCommands,
            IReadOnlyList<AuthoritativeEconomyCommand> economyCommands,
            IReadOnlyList<AuthoritativeMissionCommand> missionCommands)
        {
            currentTick++;
            var events = new List<AuthoritativeEvent>();
            AdvanceCombatState(events);
            AdvanceEnemyLifecycle(events);
            AdvanceEnemySpawns(events);
            AdvanceEnemyAi(events);
            var resolutions = new List<CommandResolution>();
            PlayerInputCommand[] commands = (receivedCommands ??
                    Array.Empty<PlayerInputCommand>())
                .OrderBy(value => value.PlayerId)
                .ThenBy(value => value.Sequence)
                .ToArray();

            foreach (PlayerInputCommand command in commands)
            {
                CommandResolution resolution = ResolveCommand(
                    command,
                    events);
                resolutions.Add(resolution);
                if (!resolution.Accepted)
                {
                    events.Add(Emit(
                        AuthoritativeEventKind.CommandRejected,
                        command.PlayerId,
                        0,
                        command.Sequence,
                        string.Empty,
                        resolution.RejectionReason));
                }
            }

            var economyResolutions = new List<AuthoritativeEconomyResolution>();
            AuthoritativeEconomyCommand[] orderedEconomy =
                (economyCommands ?? Array.Empty<AuthoritativeEconomyCommand>())
                .OrderBy(value => value.Kind ==
                    AuthoritativeEconomyCommandKind.Pickup ? 0 : 1)
                .ThenBy(value => value.EntityId)
                .ThenBy(value => value.PlayerId)
                .ThenBy(value => value.Sequence)
                .ToArray();
            foreach (AuthoritativeEconomyCommand command in orderedEconomy)
            {
                AuthoritativeEconomyResolution resolution = IsMissionOutcome
                    ? new AuthoritativeEconomyResolution(
                        command, false,
                        AuthoritativeEconomyRejection.MatchEnded)
                    : economy.Apply(
                        command,
                        currentTick,
                        PlayerPosition,
                        ApplyAuthoritativeItem,
                        ApplyAuthoritativeUpgrade);
                economyResolutions.Add(resolution);
                EmitEconomyResult(resolution, events);
                if (resolution.Accepted && command.Kind ==
                    AuthoritativeEconomyCommandKind.SelectUpgrade &&
                    missionStats.TryGetValue(command.PlayerId,
                        out MutableMissionStats stats))
                    stats.UpgradesSelected++;
            }

            var missionResolutions = new List<AuthoritativeMissionResolution>();
            AuthoritativeMissionCommand[] orderedMission =
                (missionCommands ?? Array.Empty<AuthoritativeMissionCommand>())
                .OrderBy(value => value.PlayerId)
                .ThenBy(value => value.Sequence)
                .ToArray();
            foreach (AuthoritativeMissionCommand command in orderedMission)
            {
                AuthoritativeMissionResolution resolution =
                    ResolveMissionCommand(command, events);
                missionResolutions.Add(resolution);
                if (!resolution.Accepted)
                    events.Add(Emit(
                        AuthoritativeEventKind.MissionCommandRejected,
                        command.PlayerId,
                        command.TargetPlayerId,
                        (double)resolution.Rejection));
            }
            AdvanceMission(events);

            CaptureHistory(currentTick);
            return new AuthoritativeTickResult(
                currentTick,
                resolutions,
                events,
                CaptureSnapshot(),
                economyResolutions,
                missionResolutions);
        }

        public int SpawnServerWorldDrop(
            string itemId,
            int quantity,
            NetVector3 position,
            int ownerPlayerId = 0)
        {
            return economy.SpawnDrop(itemId, quantity, position,
                ownerPlayerId);
        }

        public int GrantServerExperience(int playerId, int amount)
        {
            return economy.GrantExperience(playerId, amount);
        }

        public IReadOnlyList<AuthoritativeEvent> ApplyServerDamageToPlayer(
            int playerId,
            double damage)
        {
            if (!players.TryGetValue(playerId, out MutablePlayer player))
                throw new ArgumentOutOfRangeException(nameof(playerId));
            if (!CoopGameplayRules.Finite(damage) || damage < 0d)
                throw new ArgumentOutOfRangeException(nameof(damage));

            var events = new List<AuthoritativeEvent>();
            ApplyDamageToPlayer(player, damage, 0, events);

            ReplaceCurrentHistory();
            return events;
        }

        public void SetAuthoritativeTargetPosition(
            int targetId,
            NetVector3 position)
        {
            if (!position.IsFinite)
                throw new ArgumentOutOfRangeException(nameof(position));
            if (!targets.TryGetValue(targetId, out MutableTarget target))
                throw new ArgumentOutOfRangeException(nameof(targetId));
            target.Position = position;
        }

        public AuthoritativeWorldSnapshot CaptureSnapshot()
        {
            return new AuthoritativeWorldSnapshot(
                currentTick,
                players.Values.Select(value => value.Snapshot()),
                targets.Values.Select(value => value.Snapshot()),
                waveStatus,
                killedTargets,
                requiredKills,
                economy.Snapshot(),
                CaptureMissionState());
        }

        public IReadOnlyList<AuthoritativeEvent> SetPlayerConnected(
            int playerId,
            bool connected)
        {
            if (!players.TryGetValue(playerId, out MutablePlayer player))
                throw new ArgumentOutOfRangeException(nameof(playerId));
            if (player.Connected == connected)
                return Array.Empty<AuthoritativeEvent>();
            var events = new List<AuthoritativeEvent>();
            player.Connected = connected;
            if (!connected)
            {
                events.Add(Emit(AuthoritativeEventKind.PlayerDisconnected,
                    playerId, 0, 0d));
                if (terminalPlayerId == playerId)
                    ResetTerminalProgress();
                if (revivePlayerId == playerId || downedPlayerId == playerId)
                    ResetReviveProgress();
                if (players.Values.All(value => !value.Connected))
                    FailMission(AuthoritativeMissionOutcomeReason.AllPlayersLeft,
                        playerId, events);
            }
            ReplaceCurrentHistory();
            return events;
        }

        public void InitializePlayerConnections(
            IEnumerable<int> connectedPlayerIds)
        {
            var connected = new HashSet<int>(connectedPlayerIds ??
                Array.Empty<int>());
            foreach (MutablePlayer player in players.Values)
                player.Connected = connected.Contains(player.Id);
            ResetTerminalProgress();
            ResetReviveProgress();
            ReplaceCurrentHistory();
        }

        private AuthoritativeMissionState CaptureMissionState() => new(
            missionPhase,
            missionOutcomeReason,
            missionRevision,
            terminalProgressTicks,
            extractionProgressTicks,
            reviveProgressTicks,
            terminalPlayerId,
            revivePlayerId,
            downedPlayerId,
            missionDefinition,
            missionStats.Values.Select(value => value.Snapshot()));

        private AuthoritativeMissionResolution ResolveMissionCommand(
            AuthoritativeMissionCommand command,
            ICollection<AuthoritativeEvent> events)
        {
            if (!players.TryGetValue(command.PlayerId, out MutablePlayer player))
                return RejectMission(command,
                    AuthoritativeMissionRejection.UnknownPlayer);
            if (command.Sequence == 0 || player.HasMissionSequence &&
                command.Sequence <= player.LastMissionSequence)
                return RejectMission(command,
                    AuthoritativeMissionRejection.InvalidSequence);
            if (command.Nonce == 0 || player.MissionNonces.Contains(command.Nonce))
                return RejectMission(command,
                    AuthoritativeMissionRejection.DuplicateNonce);

            player.HasMissionSequence = true;
            player.LastMissionSequence = command.Sequence;
            player.MissionNonces.Add(command.Nonce);
            player.MissionNonceOrder.Enqueue(command.Nonce);
            while (player.MissionNonceOrder.Count > rules.NonceHistoryCapacity)
                player.MissionNonces.Remove(
                    player.MissionNonceOrder.Dequeue());

            if (missionPhase == AuthoritativeMissionPhase.Victory ||
                missionPhase == AuthoritativeMissionPhase.Defeat)
                return RejectMission(command,
                    AuthoritativeMissionRejection.MatchEnded);
            if (!player.IsCombatActive)
                return RejectMission(command,
                    AuthoritativeMissionRejection.PlayerUnavailable);

            switch (command.Kind)
            {
                case AuthoritativeMissionCommandKind.HoldTerminal:
                    if (missionPhase !=
                        AuthoritativeMissionPhase.ActivateTerminal)
                        return RejectMission(command,
                            AuthoritativeMissionRejection.WrongPhase);
                    if (!Within(player.Position,
                            missionDefinition.TerminalPosition,
                            missionDefinition.TerminalRadius))
                        return RejectMission(command,
                            AuthoritativeMissionRejection.OutOfRange);
                    if (terminalPlayerId != command.PlayerId)
                    {
                        terminalPlayerId = command.PlayerId;
                        terminalProgressTicks = 0;
                        events.Add(Emit(
                            AuthoritativeEventKind.TerminalInteractionStarted,
                            command.PlayerId, 0, 0d));
                    }
                    terminalHeartbeatTick = currentTick;
                    break;
                case AuthoritativeMissionCommandKind.HoldRevive:
                    if (!players.TryGetValue(command.TargetPlayerId,
                            out MutablePlayer downed) ||
                        command.TargetPlayerId == command.PlayerId ||
                        !downed.Connected || downed.Health > 0d)
                        return RejectMission(command,
                            AuthoritativeMissionRejection.InvalidTarget);
                    if (!Within(player.Position, downed.Position,
                            missionDefinition.ReviveRadius))
                        return RejectMission(command,
                            AuthoritativeMissionRejection.OutOfRange);
                    if (revivePlayerId != command.PlayerId ||
                        downedPlayerId != command.TargetPlayerId)
                    {
                        revivePlayerId = command.PlayerId;
                        downedPlayerId = command.TargetPlayerId;
                        reviveProgressTicks = 0;
                        events.Add(Emit(AuthoritativeEventKind.ReviveStarted,
                            command.PlayerId, command.TargetPlayerId, 0d));
                    }
                    reviveHeartbeatTick = currentTick;
                    break;
                case AuthoritativeMissionCommandKind.StartExtraction:
                    if (missionPhase != AuthoritativeMissionPhase.Extraction)
                        return RejectMission(command,
                            AuthoritativeMissionRejection.WrongPhase);
                    if (!Within(player.Position,
                            missionDefinition.ExtractionPosition,
                            missionDefinition.ExtractionRadius))
                        return RejectMission(command,
                            AuthoritativeMissionRejection.OutOfRange);
                    if (!extractionStarted)
                    {
                        extractionStarted = true;
                        events.Add(Emit(
                            AuthoritativeEventKind.ExtractionStarted,
                            command.PlayerId, 0, 0d));
                    }
                    break;
                default:
                    return RejectMission(command,
                        AuthoritativeMissionRejection.WrongPhase);
            }

            return new AuthoritativeMissionResolution(
                command, true, AuthoritativeMissionRejection.None);
        }

        private void AdvanceMission(ICollection<AuthoritativeEvent> events)
        {
            if (missionPhase == AuthoritativeMissionPhase.Victory ||
                missionPhase == AuthoritativeMissionPhase.Defeat)
                return;

            AdvanceRevive(events);
            if (missionPhase == AuthoritativeMissionPhase.ClearEnemies &&
                waveStatus == AuthoritativeWaveStatus.Completed)
            {
                SetMissionPhase(AuthoritativeMissionPhase.ActivateTerminal,
                    events);
            }

            if (missionPhase == AuthoritativeMissionPhase.ActivateTerminal)
            {
                if (currentTick - terminalHeartbeatTick <=
                        InteractionHeartbeatGraceTicks &&
                    players.TryGetValue(terminalPlayerId,
                        out MutablePlayer operatorPlayer) &&
                    operatorPlayer.IsCombatActive &&
                    Within(operatorPlayer.Position,
                        missionDefinition.TerminalPosition,
                        missionDefinition.TerminalRadius))
                {
                    terminalProgressTicks++;
                    if (terminalProgressTicks >=
                        missionDefinition.TerminalHoldTicks)
                    {
                        events.Add(Emit(
                            AuthoritativeEventKind.TerminalInteractionCompleted,
                            terminalPlayerId, 0, terminalProgressTicks));
                        SetMissionPhase(AuthoritativeMissionPhase.Extraction,
                            events);
                    }
                }
                else if (terminalProgressTicks > 0 || terminalPlayerId > 0)
                {
                    ResetTerminalProgress();
                }
            }

            if (missionPhase != AuthoritativeMissionPhase.Extraction ||
                !extractionStarted) return;
            MutablePlayer[] connected = players.Values
                .Where(value => value.Connected).ToArray();
            bool squadReady = connected.Length > 0 && connected.All(value =>
                value.IsCombatActive && Within(value.Position,
                    missionDefinition.ExtractionPosition,
                    missionDefinition.ExtractionRadius));
            if (!squadReady)
            {
                extractionProgressTicks = 0;
                return;
            }
            extractionProgressTicks++;
            if (extractionProgressTicks <
                missionDefinition.ExtractionHoldTicks) return;
            missionOutcomeReason =
                AuthoritativeMissionOutcomeReason.Extracted;
            SetMissionPhase(AuthoritativeMissionPhase.Victory, events);
            events.Add(Emit(AuthoritativeEventKind.MissionSucceeded,
                0, 0, currentTick));
        }

        private void AdvanceRevive(ICollection<AuthoritativeEvent> events)
        {
            if (revivePlayerId <= 0 || downedPlayerId <= 0) return;
            if (currentTick - reviveHeartbeatTick >
                    InteractionHeartbeatGraceTicks ||
                !players.TryGetValue(revivePlayerId, out MutablePlayer helper) ||
                !players.TryGetValue(downedPlayerId, out MutablePlayer downed) ||
                !helper.IsCombatActive || !downed.Connected ||
                downed.Health > 0d ||
                !Within(helper.Position, downed.Position,
                    missionDefinition.ReviveRadius))
            {
                ResetReviveProgress();
                return;
            }
            reviveProgressTicks++;
            if (reviveProgressTicks < missionDefinition.ReviveHoldTicks) return;
            int revived = downedPlayerId;
            int helperId = revivePlayerId;
            downed.Health = Math.Min(downed.MaximumHealth,
                missionDefinition.RevivedHealth);
            ResetReviveProgress();
            events.Add(Emit(AuthoritativeEventKind.PlayerRevived,
                helperId, revived, downed.Health));
        }

        private void FailMission(
            AuthoritativeMissionOutcomeReason reason,
            int sourceId,
            ICollection<AuthoritativeEvent> events)
        {
            if (missionPhase == AuthoritativeMissionPhase.Victory ||
                missionPhase == AuthoritativeMissionPhase.Defeat)
                return;
            missionOutcomeReason = reason;
            SetMissionPhase(AuthoritativeMissionPhase.Defeat, events);
            events.Add(Emit(AuthoritativeEventKind.MissionFailed,
                sourceId, 0, (double)reason));
        }

        private void SetMissionPhase(
            AuthoritativeMissionPhase phase,
            ICollection<AuthoritativeEvent> events)
        {
            if (missionPhase == phase) return;
            missionPhase = phase;
            missionRevision++;
            if (phase != AuthoritativeMissionPhase.ActivateTerminal)
                ResetTerminalProgress();
            if (phase != AuthoritativeMissionPhase.Extraction)
            {
                extractionProgressTicks = 0;
                extractionStarted = false;
            }
            events.Add(Emit(AuthoritativeEventKind.MissionPhaseChanged,
                0, 0, (double)phase));
        }

        private void ResetTerminalProgress()
        {
            terminalProgressTicks = 0;
            terminalPlayerId = 0;
            terminalHeartbeatTick = long.MinValue;
        }

        private void ResetReviveProgress()
        {
            reviveProgressTicks = 0;
            revivePlayerId = 0;
            downedPlayerId = 0;
            reviveHeartbeatTick = long.MinValue;
        }

        private static AuthoritativeMissionResolution RejectMission(
            AuthoritativeMissionCommand command,
            AuthoritativeMissionRejection rejection) => new(
                command, false, rejection);

        private static bool Within(
            NetVector3 left,
            NetVector3 right,
            double radius) => PlanarDistance(left, right) <= radius;

        private NetVector3 PlayerPosition(int playerId) =>
            players.TryGetValue(playerId, out MutablePlayer player)
                ? player.Position
                : default;

        private bool ApplyAuthoritativeItem(
            int playerId,
            AuthoritativeItemDefinition item)
        {
            if (!players.TryGetValue(playerId, out MutablePlayer player) ||
                !player.IsCombatActive)
                return false;
            switch (item.Effect)
            {
                case AuthoritativeItemEffect.RestoreHealth:
                    if (player.Health >= player.MaximumHealth) return false;
                    player.Health = Math.Min(player.MaximumHealth,
                        player.Health + item.EffectAmount);
                    return true;
                case AuthoritativeItemEffect.RestoreArmor:
                    if (player.Armor >= player.MaximumArmor) return false;
                    player.Armor = Math.Min(player.MaximumArmor,
                        player.Armor + item.EffectAmount);
                    return true;
                case AuthoritativeItemEffect.AddRifleAmmo:
                    return AddReserveAmmo(player, "weapon.rifle",
                        item.EffectAmount);
                case AuthoritativeItemEffect.AddHandgunAmmo:
                    return AddReserveAmmo(player, "weapon.pistol",
                        item.EffectAmount);
                default:
                    return false;
            }
        }

        private bool ApplyAuthoritativeUpgrade(
            int playerId,
            AuthoritativeUpgradeDefinition upgrade)
        {
            if (!players.TryGetValue(playerId, out MutablePlayer player) ||
                !player.IsCombatActive)
                return false;
            switch (upgrade.Effect)
            {
                case AuthoritativeUpgradeEffect.MaximumHealth:
                {
                    double amount = player.BaseMaximumHealth *
                        upgrade.EffectAmount;
                    player.MaximumHealth += amount;
                    player.Health = Math.Min(player.MaximumHealth,
                        player.Health + amount);
                    return true;
                }
                case AuthoritativeUpgradeEffect.MaximumArmor:
                {
                    double amount = player.BaseMaximumArmor *
                        upgrade.EffectAmount;
                    player.MaximumArmor += amount;
                    player.Armor = Math.Min(player.MaximumArmor,
                        player.Armor + amount);
                    return true;
                }
                case AuthoritativeUpgradeEffect.HealthRestore:
                    if (player.Health >= player.MaximumHealth) return false;
                    player.Health = Math.Min(player.MaximumHealth,
                        player.Health + upgrade.EffectAmount);
                    return true;
                case AuthoritativeUpgradeEffect.ArmorRestore:
                    if (player.Armor >= player.MaximumArmor) return false;
                    player.Armor = Math.Min(player.MaximumArmor,
                        player.Armor + upgrade.EffectAmount);
                    return true;
                default:
                    return true;
            }
        }

        private static bool AddReserveAmmo(
            MutablePlayer player,
            string weaponId,
            double amount)
        {
            if (!player.Weapons.TryGetValue(weaponId,
                    out MutableWeapon weapon))
                return false;
            int increase = Math.Max(0, (int)Math.Round(amount));
            if (increase == 0) return false;
            weapon.ReserveAmmo += increase;
            return true;
        }

        private void EmitEconomyResult(
            AuthoritativeEconomyResolution resolution,
            ICollection<AuthoritativeEvent> events)
        {
            AuthoritativeEconomyCommand command = resolution.Command;
            if (!resolution.Accepted)
            {
                events.Add(Emit(
                    AuthoritativeEventKind.EconomyCommandRejected,
                    command.PlayerId,
                    command.EntityId,
                    (double)resolution.Rejection,
                    resolution.DefinitionId));
                return;
            }

            AuthoritativeEventKind kind = command.Kind switch
            {
                AuthoritativeEconomyCommandKind.Pickup =>
                    AuthoritativeEventKind.WorldDropClaimed,
                AuthoritativeEconomyCommandKind.Use =>
                    AuthoritativeEventKind.ConsumableUsed,
                AuthoritativeEconomyCommandKind.SelectUpgrade =>
                    AuthoritativeEventKind.UpgradeApplied,
                _ => AuthoritativeEventKind.InventoryChanged
            };
            events.Add(Emit(kind, command.PlayerId, command.EntityId,
                resolution.AffectedQuantity, resolution.DefinitionId));
        }

        private void AdvanceEnemySpawns(ICollection<AuthoritativeEvent> events)
        {
            if (waveStatus == AuthoritativeWaveStatus.Completed ||
                waveStatus == AuthoritativeWaveStatus.Failed)
                return;
            bool spawnedAny = false;
            foreach (MutableTarget target in targets.Values
                         .Where(value => value.IsPending &&
                                         value.SpawnTick <= currentTick)
                         .OrderBy(value => value.SpawnTick)
                         .ThenBy(value => value.Id))
            {
                target.Spawn();
                spawnedAny = true;
                events.Add(Emit(
                    AuthoritativeEventKind.TargetSpawned,
                    target.Id,
                    0,
                    target.SpawnGeneration,
                    target.Role.ToString()));
            }
            if (spawnedAny || targets.Values.Any(value => value.IsAlive))
                waveStatus = AuthoritativeWaveStatus.Fighting;
        }

        private void AdvanceEnemyLifecycle(
            ICollection<AuthoritativeEvent> events)
        {
            foreach (MutableTarget target in targets.Values
                         .Where(value => value.Spawned && !value.Active &&
                                         value.Behavior ==
                                         AuthoritativeEnemyBehavior.Dead &&
                                         value.RecycleTick <= currentTick)
                         .OrderBy(value => value.Id))
            {
                target.Behavior = AuthoritativeEnemyBehavior.Pooled;
                events.Add(Emit(
                    AuthoritativeEventKind.TargetBehaviorChanged,
                    target.Id,
                    0,
                    (double)AuthoritativeEnemyBehavior.Pooled,
                    target.Role.ToString()));
            }
        }

        private void AdvanceEnemyAi(ICollection<AuthoritativeEvent> events)
        {
            if (waveStatus == AuthoritativeWaveStatus.Completed ||
                waveStatus == AuthoritativeWaveStatus.Failed)
                return;
            MutablePlayer[] livingPlayers = players.Values
                .Where(value => value.IsCombatActive)
                .OrderBy(value => value.Id)
                .ToArray();
            if (livingPlayers.Length == 0) return;

            foreach (MutableTarget target in targets.Values
                         .Where(value => value.IsAlive)
                         .OrderBy(value => value.Id))
            {
                MutablePlayer selected = null;
                double selectedDistance = double.PositiveInfinity;
                foreach (MutablePlayer candidate in livingPlayers)
                {
                    double distance = PlanarDistance(
                        target.Position, candidate.Position);
                    if (distance < selectedDistance - 0.000001d ||
                        Math.Abs(distance - selectedDistance) <= 0.000001d &&
                        (selected == null || candidate.Id < selected.Id))
                    {
                        selected = candidate;
                        selectedDistance = distance;
                    }
                }

                AuthoritativeEnemyDecision decision =
                    AuthoritativeEnemyUtility.Decide(
                        target.Role,
                        selected?.Id ?? 0,
                        selectedDistance,
                        target.AttackRange,
                        target.MoveSpeed > 0d);
                if (decision.Behavior != target.Behavior ||
                    decision.TargetPlayerId != target.TargetPlayerId)
                {
                    target.Behavior = decision.Behavior;
                    target.TargetPlayerId = decision.TargetPlayerId;
                    events.Add(Emit(
                        AuthoritativeEventKind.TargetBehaviorChanged,
                        target.Id,
                        decision.TargetPlayerId,
                        (double)decision.Behavior,
                        target.Role.ToString()));
                }
                if (selected == null) continue;

                NetVector3 planarDelta = new(
                    selected.Position.X - target.Position.X,
                    0d,
                    selected.Position.Z - target.Position.Z);
                if (planarDelta.SqrMagnitude > 0.000001d)
                {
                    target.YawDegrees = Math.Atan2(
                        planarDelta.X, planarDelta.Z) * 180d / Math.PI;
                }

                if (decision.Behavior ==
                    AuthoritativeEnemyBehavior.Pursue)
                {
                    double speed = target.MoveSpeed *
                        AuthoritativeEnemyUtility.MoveSpeedMultiplier(
                            target.Role);
                    double travel = Math.Min(
                        speed * rules.FixedDeltaSeconds,
                        Math.Max(0d,
                            selectedDistance - target.AttackRange * 0.9d));
                    NetVector3 desired = target.Position +
                        planarDelta.Normalized * travel;
                    NetVector3 resolved = enemyMovementResolver(
                        target.Id, target.Position, desired);
                    if (resolved.IsFinite) target.Position = resolved;
                }

                if (decision.Behavior !=
                        AuthoritativeEnemyBehavior.Attack ||
                    target.AttackDamage <= 0d ||
                    currentTick < target.NextAttackTick)
                    continue;
                target.NextAttackTick = currentTick +
                    target.AttackIntervalTicks;
                double damage = target.AttackDamage *
                    AuthoritativeEnemyUtility.DamageMultiplier(target.Role);
                events.Add(Emit(
                    AuthoritativeEventKind.TargetAttacked,
                    target.Id,
                    selected.Id,
                    damage,
                    target.Role.ToString()));
                ApplyDamageToPlayer(selected, damage, target.Id, events);
            }
        }

        private void ApplyDamageToPlayer(
            MutablePlayer target,
            double damage,
            int sourceTargetId,
            ICollection<AuthoritativeEvent> events)
        {
            if (IsMissionOutcome || !target.IsCombatActive || damage <= 0d)
                return;
            double armorDamage = Math.Min(target.Armor, damage);
            target.Armor -= armorDamage;
            damage -= armorDamage;
            double before = target.Health;
            target.Health = CoopGameplayRules.ApplyDamage(before, damage);
            double applied = armorDamage + before - target.Health;
            missionStats[target.Id].DamageTaken += applied;
            events.Add(Emit(
                AuthoritativeEventKind.PlayerDamaged,
                sourceTargetId,
                target.Id,
                applied));
            if (target.IsAlive) return;
            events.Add(Emit(
                AuthoritativeEventKind.PlayerDowned,
                sourceTargetId,
                target.Id,
                0d));
            events.Add(Emit(
                AuthoritativeEventKind.PlayerKilled,
                sourceTargetId,
                target.Id,
                0d));
            if (players.Values.Any(value => value.IsCombatActive)) return;
            if (waveStatus != AuthoritativeWaveStatus.Failed)
            {
                waveStatus = AuthoritativeWaveStatus.Failed;
                events.Add(Emit(
                    AuthoritativeEventKind.WaveFailed,
                    sourceTargetId,
                    0,
                    0d));
            }
            FailMission(AuthoritativeMissionOutcomeReason.SquadWiped,
                sourceTargetId, events);
        }

        private static double PlanarDistance(
            NetVector3 left,
            NetVector3 right)
        {
            double x = left.X - right.X;
            double z = left.Z - right.Z;
            return Math.Sqrt(x * x + z * z);
        }

        private CommandResolution ResolveCommand(
            PlayerInputCommand command,
            ICollection<AuthoritativeEvent> events)
        {
            if (!players.TryGetValue(command.PlayerId, out MutablePlayer player))
                return Rejected(command, CommandRejectionReason.UnknownPlayer);
            if (!player.Connected)
                return Rejected(command, CommandRejectionReason.UnknownPlayer);
            CommandRejectionReason identityError = ValidateAndReserveIdentity(
                player,
                command);
            if (identityError != CommandRejectionReason.None)
                return Rejected(command, identityError);

            CommandRejectionReason error = ValidateGameplay(
                player,
                command,
                out int elapsedTicks,
                out PlayerMovementState nextMovement);
            if (error != CommandRejectionReason.None)
            {
                player.AcknowledgedSequence = command.Sequence;
                return Rejected(command, error);
            }

            player.ApplyMovement(nextMovement);
            player.LastClaimedPosition = command.ClaimedPosition;
            player.LastClientTick = command.ClientTick;
            player.AimYaw = command.AimYawDegrees;
            player.AimPitch = command.AimPitchDegrees;
            player.AcknowledgedSequence = command.Sequence;
            events.Add(Emit(
                AuthoritativeEventKind.PlayerMoved,
                command.PlayerId,
                0,
                elapsedTicks));

            ShotResolution shot = new(
                ShotResolutionKind.NotRequested,
                command.ClientTick,
                0,
                0d);
            if (command.Fire)
            {
                MutableWeapon weapon = player.EquippedWeapon;
                weapon.LastFireTick = command.ClientTick;
                weapon.MagazineAmmo--;
                shot = ResolveShot(player, command, events);
            }

            return new CommandResolution(
                command,
                true,
                CommandRejectionReason.None,
                shot);
        }

        private CommandRejectionReason ValidateAndReserveIdentity(
            MutablePlayer player,
            PlayerInputCommand command)
        {
            if (command.Sequence == 0 ||
                (player.HasSequence && command.Sequence <= player.LastSequence))
                return CommandRejectionReason.InvalidSequence;
            if (command.Nonce == 0 || player.Nonces.Contains(command.Nonce))
                return CommandRejectionReason.DuplicateNonce;
            if (command.ClientTick < currentTick - rules.MaximumPastCommandTicks)
                return CommandRejectionReason.TimestampTooOld;
            if (command.ClientTick > currentTick + rules.MaximumFutureCommandTicks)
                return CommandRejectionReason.TimestampInFuture;

            player.HasSequence = true;
            player.LastSequence = command.Sequence;
            player.Nonces.Add(command.Nonce);
            player.NonceOrder.Enqueue(command.Nonce);
            while (player.NonceOrder.Count > rules.NonceHistoryCapacity)
                player.Nonces.Remove(player.NonceOrder.Dequeue());
            return CommandRejectionReason.None;
        }

        private CommandRejectionReason ValidateGameplay(
            MutablePlayer player,
            PlayerInputCommand command,
            out int elapsedTicks,
            out PlayerMovementState nextMovement)
        {
            elapsedTicks = player.LastClientTick == long.MinValue
                ? 1
                : (int)Math.Max(1L, command.ClientTick - player.LastClientTick);
            nextMovement = player.Movement;
            if (IsMissionOutcome)
                return CommandRejectionReason.InvalidMovement;
            if (!player.IsCombatActive)
                return CommandRejectionReason.InvalidMovement;
            if (player.LastClientTick != long.MinValue &&
                command.ClientTick <= player.LastClientTick)
                return CommandRejectionReason.DuplicateClientTick;
            if (!CoopGameplayRules.Finite(command.MoveX) ||
                !CoopGameplayRules.Finite(command.MoveZ) ||
                command.MoveX * command.MoveX +
                    command.MoveZ * command.MoveZ > 1.000001d)
                return CommandRejectionReason.InvalidMovement;
            if (!command.ClaimedPosition.IsFinite)
                return CommandRejectionReason.ImpossibleDisplacement;
            if (!CoopGameplayRules.Finite(command.AimYawDegrees) ||
                !CoopGameplayRules.Finite(command.AimPitchDegrees) ||
                command.AimPitchDegrees < -89d ||
                command.AimPitchDegrees > 89d)
                return CommandRejectionReason.InvalidAim;

            if (command.JumpPressed &&
                (!player.Grounded || player.LastJumpTick != long.MinValue &&
                    command.ClientTick - player.LastJumpTick <
                    rules.MinimumJumpIntervalTicks))
                return CommandRejectionReason.JumpRateExceeded;

            bool requestsStanding = player.Stance == PlayerStance.Crouching &&
                                    !command.CrouchRequested;
            bool standingClearance = !requestsStanding ||
                standingClearanceValidator(player.Id, player.Position);
            if (requestsStanding && !standingClearance)
                return CommandRejectionReason.StanceBlocked;

            nextMovement = CoopGameplayRules.IntegrateMovement(
                player.Movement,
                command,
                elapsedTicks,
                rules,
                standingClearance);
            if (NetVector3.Distance(
                    nextMovement.Position,
                    command.ClaimedPosition) >
                rules.ClaimedPositionTolerance)
                return CommandRejectionReason.ImpossibleDisplacement;

            if (player.LastClientTick != long.MinValue)
            {
                double aimDelta = Math.Max(
                    Math.Abs(CoopGameplayRules.ShortestAngleDelta(
                        player.AimYaw,
                        command.AimYawDegrees)),
                    Math.Abs(command.AimPitchDegrees - player.AimPitch));
                double maximum = rules.MaximumAimDegreesPerSecond *
                    elapsedTicks * rules.FixedDeltaSeconds;
                if (aimDelta > maximum + 0.000001d)
                    return CommandRejectionReason.AimRateExceeded;
            }

            if (command.Fire)
            {
                if (!weaponDefinitions.TryGetValue(command.WeaponId,
                        out AuthoritativeWeaponDefinition definition) ||
                    !player.Weapons.TryGetValue(command.WeaponId,
                        out MutableWeapon weapon))
                    return CommandRejectionReason.UnknownWeapon;
                if (!string.Equals(player.EquippedWeaponId,
                        command.WeaponId, StringComparison.Ordinal))
                    return CommandRejectionReason.WeaponMismatch;
                if (player.IsSwitching)
                    return CommandRejectionReason.Switching;
                if (weapon.IsReloading)
                    return CommandRejectionReason.Reloading;
                if (weapon.MagazineAmmo <= 0)
                    return CommandRejectionReason.OutOfAmmo;
                if (!command.ShotOrigin.IsFinite ||
                    NetVector3.Distance(command.ShotOrigin,
                        command.ClaimedPosition) > 2.5d)
                    return CommandRejectionReason.InvalidShotOrigin;
                int fireInterval = Math.Max(1, (int)Math.Ceiling(
                    definition.FireIntervalTicks /
                    economy.Modifier(player.Id,
                        AuthoritativeUpgradeEffect.WeaponFireRate)));
                if (weapon.LastFireTick != long.MinValue &&
                    command.ClientTick - weapon.LastFireTick < fireInterval)
                    return CommandRejectionReason.FireRateExceeded;
            }
            return CommandRejectionReason.None;
        }

        private ShotResolution ResolveShot(
            MutablePlayer shooter,
            PlayerInputCommand command,
            ICollection<AuthoritativeEvent> events)
        {
            HistoryFrame frame = FindHistory(command.ClientTick);
            AuthoritativeWeaponDefinition weapon =
                weaponDefinitions[command.WeaponId];
            NetVector3 origin = command.ShotOrigin;
            NetVector3 direction = CoopGameplayRules.AimDirection(
                command.AimYawDegrees,
                command.AimPitchDegrees);
            MutableTarget selected = null;
            double selectedDistance = double.PositiveInfinity;
            AuthoritativeHitRegion selectedRegion =
                AuthoritativeHitRegion.None;

            foreach (MutableTarget target in targets.Values
                         .OrderBy(value => value.Id))
            {
                if (!target.IsAlive || command.ClientTick < target.SpawnTick ||
                    !frame.TryGetTargetPosition(target.Id, out NetVector3 center))
                    continue;
                double headDistance = 0d;
                bool hitHead = target.HeadRadius > 0d && TryRaySphere(
                        origin,
                        direction,
                        center + target.HeadOffset,
                        target.HeadRadius,
                        weapon.HitscanRange,
                        out headDistance);
                if (hitHead && headDistance < selectedDistance)
                {
                    selected = target;
                    selectedDistance = headDistance;
                    selectedRegion = AuthoritativeHitRegion.Head;
                }
                if (!hitHead && TryRaySphere(
                        origin,
                        direction,
                        center,
                        target.Radius,
                        weapon.HitscanRange,
                        out double distance) &&
                    distance < selectedDistance)
                {
                    selected = target;
                    selectedDistance = distance;
                    selectedRegion = AuthoritativeHitRegion.Body;
                }
            }

            if (selected == null)
            {
                NetVector3 endPoint = origin + direction *
                    weapon.HitscanRange;
                events.Add(Emit(
                    AuthoritativeEventKind.ShotMissed,
                    shooter.Id,
                    0,
                    frame.Tick));
                return new ShotResolution(
                    ShotResolutionKind.Miss,
                    frame.Tick,
                    0,
                    0d,
                    command.Sequence,
                    weapon.WeaponId,
                    origin,
                    endPoint,
                    direction * -1d,
                    AuthoritativeHitRegion.None,
                    AuthoritativeSurface.None);
            }

            NetVector3 hitPoint = origin + direction * selectedDistance;
            NetVector3 hitCenter = selectedRegion ==
                AuthoritativeHitRegion.Head
                    ? frame.TargetPosition(selected.Id) + selected.HeadOffset
                    : frame.TargetPosition(selected.Id);
            NetVector3 normal = (hitPoint - hitCenter).Normalized;
            AuthoritativeShotObstruction obstruction =
                shotObstructionResolver(shooter.Id, origin, hitPoint);
            if (obstruction.Blocked)
            {
                events.Add(Emit(AuthoritativeEventKind.ShotMissed,
                    shooter.Id, 0, frame.Tick));
                return new ShotResolution(
                    ShotResolutionKind.Blocked,
                    frame.Tick,
                    0,
                    0d,
                    command.Sequence,
                    weapon.WeaponId,
                    origin,
                    obstruction.Point,
                    obstruction.Normal,
                    AuthoritativeHitRegion.None,
                    obstruction.Surface == AuthoritativeSurface.None
                        ? AuthoritativeSurface.Concrete
                        : obstruction.Surface);
            }

            double before = selected.Health;
            double damage = weapon.BaseDamage *
                economy.Modifier(shooter.Id,
                    AuthoritativeUpgradeEffect.WeaponDamage) *
                (selectedRegion == AuthoritativeHitRegion.Head
                    ? weapon.HeadDamageMultiplier
                    : 1d);
            selected.Health = CoopGameplayRules.ApplyDamage(
                selected.Health,
                damage);
            double applied = before - selected.Health;
            missionStats[shooter.Id].DamageDealt += applied;
            events.Add(Emit(
                AuthoritativeEventKind.TargetDamaged,
                shooter.Id,
                selected.Id,
                applied));
            if (selected.IsAlive)
            {
                return new ShotResolution(
                    ShotResolutionKind.Hit,
                    frame.Tick,
                    selected.Id,
                    applied,
                    command.Sequence,
                    weapon.WeaponId,
                    origin,
                    hitPoint,
                    normal,
                    selectedRegion,
                    AuthoritativeSurface.Flesh);
            }

            selected.Active = false;
            selected.Behavior = AuthoritativeEnemyBehavior.Dead;
            selected.TargetPlayerId = 0;
            selected.RecycleTick = currentTick + EnemyRecycleDelayTicks;
            killedTargets++;
            missionStats[shooter.Id].Kills++;
            events.Add(Emit(
                AuthoritativeEventKind.TargetKilled,
                shooter.Id,
                selected.Id,
                0d));
            if (!string.IsNullOrEmpty(selected.DropDefinitionId))
            {
                int dropId = economy.SpawnDrop(
                    selected.DropDefinitionId,
                    selected.DropQuantity,
                    selected.Position);
                events.Add(Emit(
                    AuthoritativeEventKind.LootDropped,
                    shooter.Id,
                    selected.Id,
                    1d,
                    selected.DropDefinitionId));
                if (dropId > 0)
                    events.Add(Emit(
                        AuthoritativeEventKind.WorldDropSpawned,
                        shooter.Id,
                        dropId,
                        selected.DropQuantity,
                        selected.DropDefinitionId));
            }
            int levelsGained = economy.GrantExperience(
                shooter.Id, selected.RewardExperience);
            if (selected.RewardExperience > 0)
                events.Add(Emit(
                    AuthoritativeEventKind.ExperienceGranted,
                    shooter.Id,
                    selected.Id,
                    selected.RewardExperience));
            if (levelsGained > 0)
            {
                events.Add(Emit(
                    AuthoritativeEventKind.PlayerLevelGained,
                    shooter.Id,
                    selected.Id,
                    levelsGained));
                events.Add(Emit(
                    AuthoritativeEventKind.UpgradeChoicesOffered,
                    shooter.Id,
                    0,
                    levelsGained));
            }
            if (killedTargets >= requiredKills &&
                waveStatus == AuthoritativeWaveStatus.Fighting)
            {
                waveStatus = AuthoritativeWaveStatus.Completed;
                events.Add(Emit(
                    AuthoritativeEventKind.WaveCompleted,
                    shooter.Id,
                    0,
                    killedTargets));
            }

            return new ShotResolution(
                ShotResolutionKind.Killed,
                frame.Tick,
                selected.Id,
                applied,
                command.Sequence,
                weapon.WeaponId,
                origin,
                hitPoint,
                normal,
                selectedRegion,
                AuthoritativeSurface.Flesh);
        }

        private HistoryFrame FindHistory(long requestedTick)
        {
            HistoryFrame candidate = history.First.Value;
            foreach (HistoryFrame frame in history)
            {
                if (frame.Tick > requestedTick) break;
                candidate = frame;
            }
            return candidate;
        }

        private void AdvanceCombatState(ICollection<AuthoritativeEvent> events)
        {
            foreach (MutablePlayer player in players.Values)
            {
                foreach (MutableWeapon weapon in player.Weapons.Values)
                {
                    if (!weapon.IsReloading ||
                        currentTick < weapon.ReloadEndTick) continue;
                    int capacity = Math.Max(1, (int)Math.Round(
                        weapon.Definition.MagazineCapacity *
                        economy.Modifier(player.Id,
                            AuthoritativeUpgradeEffect.MagazineCapacity)));
                    int needed = capacity -
                        weapon.MagazineAmmo;
                    int transferred = Math.Min(needed, weapon.ReserveAmmo);
                    weapon.MagazineAmmo += transferred;
                    weapon.ReserveAmmo -= transferred;
                    weapon.ReloadEndTick = 0;
                    events.Add(Emit(AuthoritativeEventKind.ReloadCompleted,
                        player.Id, 0, transferred,
                        weapon.Definition.WeaponId));
                }

                if (!player.IsSwitching ||
                    currentTick < player.SwitchEndTick) continue;
                player.EquippedWeaponId = player.PendingWeaponId;
                player.PendingWeaponId = string.Empty;
                player.SwitchEndTick = 0;
                events.Add(Emit(
                    AuthoritativeEventKind.WeaponSwitchCompleted,
                    player.Id, 0, 0d, player.EquippedWeaponId));
            }
        }

        private static WeaponActionResolution RejectWeaponAction(
            AuthoritativeWeaponAction action,
            string weaponId,
            CommandRejectionReason reason)
        {
            return new WeaponActionResolution(false, reason, action,
                weaponId);
        }

        private static bool TryRaySphere(
            NetVector3 origin,
            NetVector3 direction,
            NetVector3 center,
            double radius,
            double maximumDistance,
            out double distance)
        {
            NetVector3 toCenter = center - origin;
            double projection = NetVector3.Dot(toCenter, direction);
            double centerDistanceSquared = toCenter.SqrMagnitude;
            double perpendicularSquared = centerDistanceSquared -
                projection * projection;
            double radiusSquared = radius * radius;
            if (perpendicularSquared > radiusSquared)
            {
                distance = 0d;
                return false;
            }
            double offset = Math.Sqrt(Math.Max(0d,
                radiusSquared - perpendicularSquared));
            double near = projection - offset;
            double far = projection + offset;
            distance = near >= 0d ? near : far;
            return distance >= 0d && distance <= maximumDistance;
        }

        private void CaptureHistory(long tick)
        {
            history.AddLast(new HistoryFrame(
                tick,
                players.Values.ToDictionary(value => value.Id,
                    value => value.Position),
                targets.Values.ToDictionary(value => value.Id,
                    value => value.Position)));
            while (history.Count > rules.HistoryCapacity)
                history.RemoveFirst();
        }

        private void ReplaceCurrentHistory()
        {
            if (history.Last != null && history.Last.Value.Tick == currentTick)
                history.RemoveLast();
            CaptureHistory(currentTick);
        }

        private AuthoritativeEvent Emit(
            AuthoritativeEventKind kind,
            int subjectId,
            int targetId,
            double value,
            string definitionId = "",
            CommandRejectionReason rejection = CommandRejectionReason.None)
        {
            return new AuthoritativeEvent(
                currentTick,
                nextEventSequence++,
                kind,
                subjectId,
                targetId,
                value,
                definitionId,
                rejection);
        }

        private static CommandResolution Rejected(
            PlayerInputCommand command,
            CommandRejectionReason reason)
        {
            return new CommandResolution(
                command,
                false,
                reason,
                new ShotResolution(
                    ShotResolutionKind.NotRequested,
                    command.ClientTick,
                    0,
                    0d));
        }

        private sealed class MutablePlayer
        {
            public MutablePlayer(
                CoopPlayerSpawn spawn,
                IReadOnlyDictionary<string, AuthoritativeWeaponDefinition>
                    definitions)
            {
                Id = spawn.PlayerId;
                Position = spawn.Position;
                Velocity = new NetVector3(0d, 0d, 0d);
                GroundHeight = spawn.Position.Y;
                Grounded = true;
                Stance = PlayerStance.Standing;
                LastJumpTick = long.MinValue;
                LastClaimedPosition = spawn.Position;
                Health = spawn.Health;
                BaseMaximumHealth = spawn.Health;
                MaximumHealth = spawn.Health;
                Armor = spawn.Armor;
                BaseMaximumArmor = spawn.MaximumArmor;
                MaximumArmor = spawn.MaximumArmor;
                foreach (KeyValuePair<string, AuthoritativeWeaponDefinition>
                         pair in definitions)
                    Weapons[pair.Key] = new MutableWeapon(pair.Value);
            }

            public int Id;
            public NetVector3 Position;
            public NetVector3 Velocity;
            public double GroundHeight;
            public bool Grounded;
            public PlayerStance Stance;
            public long LastJumpTick;
            public NetVector3 LastClaimedPosition;
            public double Health;
            public double BaseMaximumHealth;
            public double MaximumHealth;
            public double Armor;
            public double BaseMaximumArmor;
            public double MaximumArmor;
            public bool HasSequence;
            public uint LastSequence;
            public uint AcknowledgedSequence;
            public long LastClientTick = long.MinValue;
            public double AimYaw;
            public double AimPitch;
            public string EquippedWeaponId = "weapon.rifle";
            public string PendingWeaponId = string.Empty;
            public long SwitchEndTick;
            public readonly Dictionary<string, MutableWeapon> Weapons =
                new(StringComparer.Ordinal);
            public readonly HashSet<ulong> Nonces = new();
            public readonly Queue<ulong> NonceOrder = new();
            public bool Connected = true;
            public bool HasMissionSequence;
            public uint LastMissionSequence;
            public readonly HashSet<ulong> MissionNonces = new();
            public readonly Queue<ulong> MissionNonceOrder = new();
            public bool IsAlive => Health > 0d;
            public bool IsCombatActive => Connected && IsAlive;
            public bool IsSwitching => SwitchEndTick > 0;
            public MutableWeapon EquippedWeapon =>
                Weapons[EquippedWeaponId];

            public PlayerMovementState Movement => new(
                Position,
                Velocity,
                AimYaw,
                AimPitch,
                Stance,
                Grounded,
                LastJumpTick,
                GroundHeight);

            public void ApplyMovement(PlayerMovementState movement)
            {
                Position = movement.Position;
                Velocity = movement.Velocity;
                AimYaw = movement.AimYawDegrees;
                AimPitch = movement.AimPitchDegrees;
                Stance = movement.Stance;
                Grounded = movement.Grounded;
                LastJumpTick = movement.LastJumpTick;
            }

            public AuthoritativePlayerState Snapshot()
            {
                MutableWeapon equipped = EquippedWeapon;
                return new AuthoritativePlayerState(
                    Id,
                    Position,
                    Health,
                    AcknowledgedSequence,
                    AimYaw,
                    AimPitch,
                    Velocity,
                    Stance,
                    Grounded,
                    LastJumpTick,
                    GroundHeight,
                    EquippedWeaponId,
                    equipped.MagazineAmmo,
                    equipped.ReserveAmmo,
                    equipped.IsReloading,
                    equipped.ReloadEndTick,
                    IsSwitching,
                    PendingWeaponId,
                    SwitchEndTick,
                    Weapons.Values
                        .OrderBy(value => value.Definition.WeaponId)
                        .Select(value => value.Snapshot())
                        .ToArray(),
                    MaximumHealth,
                    Armor,
                    MaximumArmor,
                    !Connected
                        ? AuthoritativePlayerLifeState.Disconnected
                        : IsAlive
                            ? AuthoritativePlayerLifeState.Alive
                            : AuthoritativePlayerLifeState.Downed);
            }
        }

        private sealed class MutableMissionStats
        {
            public MutableMissionStats(int playerId)
            {
                PlayerId = playerId;
            }

            public readonly int PlayerId;
            public int Kills;
            public double DamageDealt;
            public double DamageTaken;
            public int UpgradesSelected;

            public AuthoritativePlayerMissionStats Snapshot() => new(
                PlayerId, Kills, DamageDealt, DamageTaken,
                UpgradesSelected);
        }

        private sealed class MutableWeapon
        {
            public MutableWeapon(AuthoritativeWeaponDefinition definition)
            {
                Definition = definition;
                MagazineAmmo = definition.MagazineCapacity;
                ReserveAmmo = definition.InitialReserveAmmo;
            }

            public AuthoritativeWeaponDefinition Definition;
            public int MagazineAmmo;
            public int ReserveAmmo;
            public long ReloadEndTick;
            public long LastFireTick = long.MinValue;
            public bool IsReloading => ReloadEndTick > 0;

            public void CancelReload()
            {
                ReloadEndTick = 0;
            }

            public AuthoritativeWeaponState Snapshot() => new(
                Definition.WeaponId,
                MagazineAmmo,
                ReserveAmmo,
                IsReloading,
                ReloadEndTick,
                LastFireTick);
        }

        private sealed class MutableTarget
        {
            public MutableTarget(CoopTargetSpawn spawn)
            {
                Id = spawn.TargetId;
                Position = spawn.Position;
                Radius = spawn.Radius;
                MaximumHealth = spawn.Health;
                Health = spawn.Health;
                DropDefinitionId = spawn.DropDefinitionId;
                HeadOffset = spawn.HeadOffset;
                HeadRadius = spawn.HeadRadius;
                Role = spawn.Role;
                SpawnTick = spawn.SpawnTick;
                MoveSpeed = spawn.MoveSpeed;
                AttackRange = spawn.AttackRange;
                AttackDamage = spawn.AttackDamage;
                AttackIntervalTicks = spawn.AttackIntervalTicks;
                RewardExperience = spawn.RewardExperience;
                DropQuantity = spawn.DropQuantity;
                Spawned = spawn.SpawnTick == 0;
                Active = Spawned;
                SpawnGeneration = Spawned ? 1 : 0;
                Behavior = Spawned
                    ? AuthoritativeEnemyBehavior.Patrol
                    : AuthoritativeEnemyBehavior.Pooled;
            }

            public int Id;
            public NetVector3 Position;
            public double Radius;
            public double MaximumHealth;
            public double Health;
            public string DropDefinitionId;
            public NetVector3 HeadOffset;
            public double HeadRadius;
            public AuthoritativeEnemyRole Role;
            public long SpawnTick;
            public double MoveSpeed;
            public double AttackRange;
            public double AttackDamage;
            public int AttackIntervalTicks;
            public int RewardExperience;
            public int DropQuantity;
            public long NextAttackTick;
            public long RecycleTick;
            public bool Spawned;
            public bool Active;
            public double YawDegrees;
            public AuthoritativeEnemyBehavior Behavior;
            public int TargetPlayerId;
            public int SpawnGeneration;
            public bool IsAlive => Active && Health > 0d;
            public bool IsPending => !Spawned;

            public void Spawn()
            {
                if (Spawned) return;
                Spawned = true;
                Active = true;
                Health = MaximumHealth;
                Behavior = AuthoritativeEnemyBehavior.Patrol;
                TargetPlayerId = 0;
                NextAttackTick = 0;
                RecycleTick = 0;
                SpawnGeneration++;
            }

            public AuthoritativeTargetState Snapshot() => new(
                Id,
                Position,
                Radius,
                Health,
                DropDefinitionId,
                HeadOffset,
                HeadRadius,
                Active,
                YawDegrees,
                Role,
                Behavior,
                TargetPlayerId,
                SpawnGeneration);
        }

        private sealed class HistoryFrame
        {
            private readonly IReadOnlyDictionary<int, NetVector3> players;
            private readonly IReadOnlyDictionary<int, NetVector3> targets;

            public HistoryFrame(
                long tick,
                IReadOnlyDictionary<int, NetVector3> players,
                IReadOnlyDictionary<int, NetVector3> targets)
            {
                Tick = tick;
                this.players = players;
                this.targets = targets;
            }

            public long Tick { get; }
            public bool TryGetPlayerPosition(int id, out NetVector3 value) =>
                players.TryGetValue(id, out value);
            public bool TryGetTargetPosition(int id, out NetVector3 value) =>
                targets.TryGetValue(id, out value);
            public NetVector3 TargetPosition(int id) => targets[id];
        }
    }
}
