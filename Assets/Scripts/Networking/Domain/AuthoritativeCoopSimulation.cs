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
    }

    /// <summary>
    /// Deterministic two-player/PVE authoritative kernel. Callers provide all
    /// commands received for one server tick; the kernel validates, sorts and
    /// resolves them without reading wall-clock time.
    /// </summary>
    public sealed class AuthoritativeCoopSimulation
    {
        private readonly CoopServerRules rules;
        private readonly Dictionary<int, MutablePlayer> players;
        private readonly Dictionary<int, MutableTarget> targets;
        private readonly LinkedList<HistoryFrame> history = new();
        private readonly int requiredKills;
        private long currentTick;
        private long nextEventSequence;
        private int killedTargets;
        private AuthoritativeWaveStatus waveStatus =
            AuthoritativeWaveStatus.Fighting;

        public AuthoritativeCoopSimulation(
            CoopServerRules rules,
            IEnumerable<CoopPlayerSpawn> configuredPlayers,
            IEnumerable<CoopTargetSpawn> configuredTargets,
            int requiredKills = 0)
        {
            this.rules = rules ?? throw new ArgumentNullException(nameof(rules));
            players = (configuredPlayers ?? throw new ArgumentNullException(
                    nameof(configuredPlayers)))
                .Select(value => new MutablePlayer(value))
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
            if (requiredKills < 0 || requiredKills > targets.Count)
                throw new ArgumentOutOfRangeException(nameof(requiredKills));
            this.requiredKills = requiredKills == 0
                ? targets.Count
                : requiredKills;
            CaptureHistory(0);
        }

        public CoopServerRules Rules => rules;
        public long CurrentTick => currentTick;
        public int HistoryCount => history.Count;
        public AuthoritativeWaveStatus WaveStatus => waveStatus;

        public AuthoritativeTickResult Step(
            IReadOnlyList<PlayerInputCommand> receivedCommands)
        {
            currentTick++;
            var events = new List<AuthoritativeEvent>();
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

            CaptureHistory(currentTick);
            return new AuthoritativeTickResult(
                currentTick,
                resolutions,
                events,
                CaptureSnapshot());
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
            if (!player.IsAlive || damage <= 0d)
                return events;
            double before = player.Health;
            player.Health = CoopGameplayRules.ApplyDamage(before, damage);
            double applied = before - player.Health;
            events.Add(Emit(
                AuthoritativeEventKind.PlayerDamaged,
                0,
                playerId,
                applied));
            if (!player.IsAlive)
            {
                events.Add(Emit(
                    AuthoritativeEventKind.PlayerKilled,
                    0,
                    playerId,
                    0d));
                if (players.Values.All(value => !value.IsAlive) &&
                    waveStatus == AuthoritativeWaveStatus.Fighting)
                {
                    waveStatus = AuthoritativeWaveStatus.Failed;
                    events.Add(Emit(
                        AuthoritativeEventKind.WaveFailed,
                        0,
                        0,
                        0d));
                }
            }

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
                killedTargets);
        }

        private CommandResolution ResolveCommand(
            PlayerInputCommand command,
            ICollection<AuthoritativeEvent> events)
        {
            if (!players.TryGetValue(command.PlayerId, out MutablePlayer player))
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
                out NetVector3 nextPosition);
            if (error != CommandRejectionReason.None)
                return Rejected(command, error);

            player.Position = nextPosition;
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
                player.LastFireClientTick = command.ClientTick;
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
            out NetVector3 nextPosition)
        {
            elapsedTicks = player.LastClientTick == long.MinValue
                ? 1
                : (int)Math.Max(1L, command.ClientTick - player.LastClientTick);
            nextPosition = player.Position;
            if (!player.IsAlive)
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

            nextPosition = CoopGameplayRules.IntegrateMovement(
                player.Position,
                command.MoveX,
                command.MoveZ,
                elapsedTicks,
                rules);
            if (NetVector3.Distance(
                    nextPosition,
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

            if (command.Fire && player.LastFireClientTick != long.MinValue &&
                command.ClientTick - player.LastFireClientTick <
                    rules.FireCooldownTicks)
                return CommandRejectionReason.FireRateExceeded;
            return CommandRejectionReason.None;
        }

        private ShotResolution ResolveShot(
            MutablePlayer shooter,
            PlayerInputCommand command,
            ICollection<AuthoritativeEvent> events)
        {
            HistoryFrame frame = FindHistory(command.ClientTick);
            NetVector3 origin = frame.TryGetPlayerPosition(
                shooter.Id,
                out NetVector3 historicalPosition)
                ? historicalPosition
                : shooter.Position;
            NetVector3 direction = CoopGameplayRules.AimDirection(
                command.AimYawDegrees,
                command.AimPitchDegrees);
            MutableTarget selected = null;
            double selectedDistance = double.PositiveInfinity;

            foreach (MutableTarget target in targets.Values
                         .OrderBy(value => value.Id))
            {
                if (!target.IsAlive ||
                    !frame.TryGetTargetPosition(target.Id, out NetVector3 center))
                    continue;
                if (TryRaySphere(
                        origin,
                        direction,
                        center,
                        target.Radius,
                        rules.HitscanRange,
                        out double distance) &&
                    distance < selectedDistance)
                {
                    selected = target;
                    selectedDistance = distance;
                }
            }

            if (selected == null)
            {
                events.Add(Emit(
                    AuthoritativeEventKind.ShotMissed,
                    shooter.Id,
                    0,
                    frame.Tick));
                return new ShotResolution(
                    ShotResolutionKind.Miss,
                    frame.Tick,
                    0,
                    0d);
            }

            double before = selected.Health;
            selected.Health = CoopGameplayRules.ApplyDamage(
                selected.Health,
                rules.ShotDamage);
            double applied = before - selected.Health;
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
                    applied);
            }

            killedTargets++;
            events.Add(Emit(
                AuthoritativeEventKind.TargetKilled,
                shooter.Id,
                selected.Id,
                0d));
            if (!string.IsNullOrEmpty(selected.DropDefinitionId))
            {
                events.Add(Emit(
                    AuthoritativeEventKind.LootDropped,
                    shooter.Id,
                    selected.Id,
                    1d,
                    selected.DropDefinitionId));
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
                applied);
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
            public MutablePlayer(CoopPlayerSpawn spawn)
            {
                Id = spawn.PlayerId;
                Position = spawn.Position;
                LastClaimedPosition = spawn.Position;
                Health = spawn.Health;
            }

            public int Id;
            public NetVector3 Position;
            public NetVector3 LastClaimedPosition;
            public double Health;
            public bool HasSequence;
            public uint LastSequence;
            public uint AcknowledgedSequence;
            public long LastClientTick = long.MinValue;
            public long LastFireClientTick = long.MinValue;
            public double AimYaw;
            public double AimPitch;
            public readonly HashSet<ulong> Nonces = new();
            public readonly Queue<ulong> NonceOrder = new();
            public bool IsAlive => Health > 0d;

            public AuthoritativePlayerState Snapshot() => new(
                Id,
                Position,
                Health,
                AcknowledgedSequence,
                AimYaw,
                AimPitch);
        }

        private sealed class MutableTarget
        {
            public MutableTarget(CoopTargetSpawn spawn)
            {
                Id = spawn.TargetId;
                Position = spawn.Position;
                Radius = spawn.Radius;
                Health = spawn.Health;
                DropDefinitionId = spawn.DropDefinitionId;
            }

            public int Id;
            public NetVector3 Position;
            public double Radius;
            public double Health;
            public string DropDefinitionId;
            public bool IsAlive => Health > 0d;

            public AuthoritativeTargetState Snapshot() => new(
                Id,
                Position,
                Radius,
                Health,
                DropDefinitionId);
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
        }
    }
}
