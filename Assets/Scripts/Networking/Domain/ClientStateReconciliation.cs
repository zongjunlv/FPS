using System;
using System.Collections.Generic;
using System.Linq;

namespace FPS.Networking.Domain
{
    public enum PredictionCorrectionKind
    {
        None,
        Smooth,
        Snap
    }

    public readonly struct PredictionCorrection
    {
        public PredictionCorrection(
            PredictionCorrectionKind kind,
            double errorDistance,
            NetVector3 positionBeforeCorrection,
            NetVector3 replayTargetPosition,
            NetVector3 appliedPosition,
            int replayedCommandCount)
        {
            Kind = kind;
            ErrorDistance = errorDistance;
            PositionBeforeCorrection = positionBeforeCorrection;
            ReplayTargetPosition = replayTargetPosition;
            AppliedPosition = appliedPosition;
            ReplayedCommandCount = replayedCommandCount;
        }

        public PredictionCorrectionKind Kind { get; }
        public double ErrorDistance { get; }
        public NetVector3 PositionBeforeCorrection { get; }
        public NetVector3 ReplayTargetPosition { get; }
        public NetVector3 AppliedPosition { get; }
        public int ReplayedCommandCount { get; }
        public bool WasCorrected => Kind != PredictionCorrectionKind.None;
    }

    /// <summary>
    /// Predicts with the exact movement rule used by the server, retains only
    /// unacknowledged inputs, then replays them on authoritative snapshots.
    /// </summary>
    public sealed class LocalPredictionBuffer
    {
        private readonly CoopServerRules rules;
        private readonly int playerId;
        private readonly List<PlayerInputCommand> pending = new();
        private NetVector3 predictedPosition;
        private long lastPredictedTick = long.MinValue;

        public LocalPredictionBuffer(
            CoopServerRules rules,
            int playerId,
            NetVector3 initialPosition)
        {
            this.rules = rules ?? throw new ArgumentNullException(nameof(rules));
            if (playerId <= 0)
                throw new ArgumentOutOfRangeException(nameof(playerId));
            if (!initialPosition.IsFinite)
                throw new ArgumentOutOfRangeException(nameof(initialPosition));
            this.playerId = playerId;
            predictedPosition = initialPosition;
        }

        public NetVector3 PredictedPosition => predictedPosition;
        public IReadOnlyList<PlayerInputCommand> PendingCommands => pending;

        public NetVector3 Predict(PlayerInputCommand command)
        {
            if (command.PlayerId != playerId)
                throw new ArgumentException(
                    "Command belongs to a different player.",
                    nameof(command));
            if (pending.Count > 0 &&
                command.Sequence <= pending[pending.Count - 1].Sequence)
                throw new ArgumentException(
                    "Prediction commands must be sequence ordered.",
                    nameof(command));
            if (!CoopGameplayRules.Finite(command.MoveX) ||
                !CoopGameplayRules.Finite(command.MoveZ))
                throw new ArgumentOutOfRangeException(nameof(command));

            int elapsedTicks = lastPredictedTick == long.MinValue
                ? 1
                : (int)Math.Max(1L, command.ClientTick - lastPredictedTick);
            predictedPosition = CoopGameplayRules.IntegrateMovement(
                predictedPosition,
                command.MoveX,
                command.MoveZ,
                elapsedTicks,
                rules);
            lastPredictedTick = command.ClientTick;
            pending.Add(command);
            return predictedPosition;
        }

        public PredictionCorrection Reconcile(
            AuthoritativePlayerState authoritative)
        {
            if (authoritative.PlayerId != playerId)
                throw new ArgumentException(
                    "Snapshot belongs to a different player.",
                    nameof(authoritative));

            NetVector3 before = predictedPosition;
            pending.RemoveAll(command =>
                command.Sequence <= authoritative.AcknowledgedSequence);
            NetVector3 replay = authoritative.Position;
            long replayTick = long.MinValue;
            foreach (PlayerInputCommand command in pending.OrderBy(
                         value => value.Sequence))
            {
                int elapsedTicks = replayTick == long.MinValue
                    ? 1
                    : (int)Math.Max(1L, command.ClientTick - replayTick);
                replay = CoopGameplayRules.IntegrateMovement(
                    replay,
                    command.MoveX,
                    command.MoveZ,
                    elapsedTicks,
                    rules);
                replayTick = command.ClientTick;
            }

            double error = NetVector3.Distance(before, replay);
            PredictionCorrectionKind kind;
            NetVector3 applied;
            if (error <= rules.PredictionCorrectionThreshold)
            {
                kind = PredictionCorrectionKind.None;
                applied = before;
            }
            else if (error < rules.PredictionSnapThreshold)
            {
                kind = PredictionCorrectionKind.Smooth;
                applied = NetVector3.Lerp(before, replay, 0.5d);
            }
            else
            {
                kind = PredictionCorrectionKind.Snap;
                applied = replay;
            }

            predictedPosition = applied;
            if (pending.Count == 0)
                lastPredictedTick = long.MinValue;
            return new PredictionCorrection(
                kind,
                error,
                before,
                replay,
                applied,
                pending.Count);
        }
    }

    public readonly struct RemotePlayerSnapshot
    {
        public RemotePlayerSnapshot(
            long serverTick,
            int playerId,
            NetVector3 position,
            double aimYawDegrees,
            double aimPitchDegrees)
        {
            if (serverTick < 0)
                throw new ArgumentOutOfRangeException(nameof(serverTick));
            if (playerId <= 0)
                throw new ArgumentOutOfRangeException(nameof(playerId));
            if (!position.IsFinite)
                throw new ArgumentOutOfRangeException(nameof(position));
            ServerTick = serverTick;
            PlayerId = playerId;
            Position = position;
            AimYawDegrees = aimYawDegrees;
            AimPitchDegrees = aimPitchDegrees;
        }

        public long ServerTick { get; }
        public int PlayerId { get; }
        public NetVector3 Position { get; }
        public double AimYawDegrees { get; }
        public double AimPitchDegrees { get; }
    }

    public readonly struct RemoteInterpolationSample
    {
        public RemoteInterpolationSample(
            bool available,
            long fromTick,
            long toTick,
            double ratio,
            NetVector3 position,
            double aimYawDegrees,
            double aimPitchDegrees)
        {
            Available = available;
            FromTick = fromTick;
            ToTick = toTick;
            Ratio = ratio;
            Position = position;
            AimYawDegrees = aimYawDegrees;
            AimPitchDegrees = aimPitchDegrees;
        }

        public bool Available { get; }
        public long FromTick { get; }
        public long ToTick { get; }
        public double Ratio { get; }
        public NetVector3 Position { get; }
        public double AimYawDegrees { get; }
        public double AimPitchDegrees { get; }
    }

    public sealed class RemoteSnapshotInterpolator
    {
        private readonly int capacity;
        private readonly int interpolationDelayTicks;
        private readonly List<RemotePlayerSnapshot> snapshots = new();
        private int playerId;

        public RemoteSnapshotInterpolator(
            int capacity = 32,
            int interpolationDelayTicks = 2)
        {
            if (capacity < 2)
                throw new ArgumentOutOfRangeException(nameof(capacity));
            if (interpolationDelayTicks < 0)
                throw new ArgumentOutOfRangeException(
                    nameof(interpolationDelayTicks));
            this.capacity = capacity;
            this.interpolationDelayTicks = interpolationDelayTicks;
        }

        public int Count => snapshots.Count;

        public void Push(RemotePlayerSnapshot snapshot)
        {
            if (snapshots.Count > 0 && snapshot.PlayerId != playerId)
                throw new ArgumentException(
                    "One interpolator tracks one remote player.",
                    nameof(snapshot));
            playerId = snapshot.PlayerId;
            int existing = snapshots.FindIndex(value =>
                value.ServerTick == snapshot.ServerTick);
            if (existing >= 0) snapshots[existing] = snapshot;
            else snapshots.Add(snapshot);
            snapshots.Sort((left, right) =>
                left.ServerTick.CompareTo(right.ServerTick));
            while (snapshots.Count > capacity) snapshots.RemoveAt(0);
        }

        public RemoteInterpolationSample Sample(double estimatedServerTick)
        {
            if (snapshots.Count == 0)
                return default;
            double renderTick = estimatedServerTick - interpolationDelayTicks;
            RemotePlayerSnapshot first = snapshots[0];
            if (renderTick <= first.ServerTick)
                return FromSingle(first);
            RemotePlayerSnapshot last = snapshots[snapshots.Count - 1];
            if (renderTick >= last.ServerTick)
                return FromSingle(last);

            for (int index = 1; index < snapshots.Count; index++)
            {
                RemotePlayerSnapshot to = snapshots[index];
                if (to.ServerTick < renderTick) continue;
                RemotePlayerSnapshot from = snapshots[index - 1];
                double ratio = (renderTick - from.ServerTick) /
                    (to.ServerTick - from.ServerTick);
                double yaw = from.AimYawDegrees +
                    CoopGameplayRules.ShortestAngleDelta(
                        from.AimYawDegrees,
                        to.AimYawDegrees) * ratio;
                return new RemoteInterpolationSample(
                    true,
                    from.ServerTick,
                    to.ServerTick,
                    ratio,
                    NetVector3.Lerp(from.Position, to.Position, ratio),
                    yaw,
                    from.AimPitchDegrees +
                        (to.AimPitchDegrees - from.AimPitchDegrees) * ratio);
            }

            return FromSingle(last);
        }

        private static RemoteInterpolationSample FromSingle(
            RemotePlayerSnapshot snapshot)
        {
            return new RemoteInterpolationSample(
                true,
                snapshot.ServerTick,
                snapshot.ServerTick,
                0d,
                snapshot.Position,
                snapshot.AimYawDegrees,
                snapshot.AimPitchDegrees);
        }
    }
}
