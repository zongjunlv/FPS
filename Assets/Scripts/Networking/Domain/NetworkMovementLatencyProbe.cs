using System;
using System.Collections.Generic;
using System.Linq;

namespace FPS.Networking.Domain
{
    public readonly struct MovementLatencyReport
    {
        public MovementLatencyReport(
            int latencyMilliseconds,
            int submittedCommands,
            int correctionCount,
            double maximumError,
            double meanError)
        {
            LatencyMilliseconds = latencyMilliseconds;
            SubmittedCommands = submittedCommands;
            CorrectionCount = correctionCount;
            MaximumError = maximumError;
            MeanError = meanError;
        }

        public int LatencyMilliseconds { get; }
        public int SubmittedCommands { get; }
        public int CorrectionCount { get; }
        public double MaximumError { get; }
        public double MeanError { get; }
    }

    /// <summary>
    /// Deterministic RTT probe used by automated validation and diagnostics.
    /// It delays both input delivery and snapshots while the client predicts
    /// immediately with the same movement kernel as the server.
    /// </summary>
    public static class NetworkMovementLatencyProbe
    {
        public static MovementLatencyReport Run(
            int latencyMilliseconds,
            int simulatedTicks = 180,
            int tickRate = 60)
        {
            if (latencyMilliseconds < 0)
                throw new ArgumentOutOfRangeException(
                    nameof(latencyMilliseconds));
            if (simulatedTicks < 1)
                throw new ArgumentOutOfRangeException(nameof(simulatedTicks));
            if (tickRate < 1)
                throw new ArgumentOutOfRangeException(nameof(tickRate));

            var rules = new CoopServerRules(
                tickRate: tickRate,
                maximumPastCommandTicks: Math.Max(12, tickRate),
                historyCapacity: Math.Max(64, tickRate + 2),
                maximumMoveSpeed: 5d,
                walkSpeed: 2d,
                sprintSpeed: 5d,
                crouchSpeed: 1.5d,
                maximumAcceleration: 30d,
                gravity: 20d,
                jumpSpeed: 7.75d,
                minimumJumpIntervalTicks: 12);
            var simulation = new AuthoritativeCoopSimulation(
                rules,
                new[] { new CoopPlayerSpawn(1, default) },
                new[]
                {
                    new CoopTargetSpawn(1,
                        new NetVector3(0d, 0d, 100d), 1d, 100d)
                });
            var prediction = new LocalPredictionBuffer(rules, 1, default);
            int oneWayTicks = (int)Math.Round(
                latencyMilliseconds * tickRate / 2000d,
                MidpointRounding.AwayFromZero);
            var commands = new List<DelayedCommand>();
            var snapshots = new List<DelayedSnapshot>();
            uint sequence = 0;
            int corrections = 0;
            int samples = 0;
            double maximumError = 0d;
            double errorSum = 0d;
            int totalTicks = simulatedTicks + oneWayTicks * 2 + 2;

            for (int tick = 1; tick <= totalTicks; tick++)
            {
                if (tick <= simulatedTicks)
                {
                    double moveX = tick % 120 >= 90 ? 0.45d : 0d;
                    double moveZ = tick % 120 < 90 ? 1d : 0.65d;
                    bool sprint = tick % 120 < 55;
                    bool crouch = tick % 120 >= 90;
                    bool jump = tick == 30 || tick == 150;
                    var provisional = new PlayerInputCommand(
                        1, ++sequence, sequence + 1000UL, tick,
                        moveX, moveZ, 25d, 0d, false,
                        prediction.PredictedPosition,
                        jump, sprint, crouch);
                    NetVector3 claimed = prediction.Predict(provisional);
                    var command = new PlayerInputCommand(
                        1, sequence, sequence + 1000UL, tick,
                        moveX, moveZ, 25d, 0d, false, claimed,
                        jump, sprint, crouch);
                    commands.Add(new DelayedCommand(
                        tick + oneWayTicks, command));
                }

                PlayerInputCommand[] delivered = commands
                    .Where(value => value.DueTick == tick)
                    .Select(value => value.Command)
                    .ToArray();
                AuthoritativeTickResult result = simulation.Step(delivered);
                snapshots.Add(new DelayedSnapshot(
                    tick + oneWayTicks,
                    result.Snapshot.Player(1)));

                foreach (DelayedSnapshot snapshot in snapshots.Where(value =>
                             value.DueTick == tick))
                {
                    PredictionCorrection correction = prediction.Reconcile(
                        snapshot.State);
                    samples++;
                    errorSum += correction.ErrorDistance;
                    maximumError = Math.Max(
                        maximumError, correction.ErrorDistance);
                    if (correction.WasCorrected) corrections++;
                }
            }

            return new MovementLatencyReport(
                latencyMilliseconds,
                simulatedTicks,
                corrections,
                maximumError,
                samples == 0 ? 0d : errorSum / samples);
        }

        private readonly struct DelayedCommand
        {
            public DelayedCommand(int dueTick, PlayerInputCommand command)
            {
                DueTick = dueTick;
                Command = command;
            }

            public int DueTick { get; }
            public PlayerInputCommand Command { get; }
        }

        private readonly struct DelayedSnapshot
        {
            public DelayedSnapshot(
                int dueTick,
                AuthoritativePlayerState state)
            {
                DueTick = dueTick;
                State = state;
            }

            public int DueTick { get; }
            public AuthoritativePlayerState State { get; }
        }
    }
}
