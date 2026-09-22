using System.Linq;
using FPS.Networking.Domain;
using FPS.Networking.Netcode;
using NUnit.Framework;

namespace FPS.Tests.Architecture
{
    public sealed class Issue89NetworkMovementPredictionTests
    {
        [Test]
        public void SharedKernelHandlesYawSprintCrouchJumpAndLanding()
        {
            CoopServerRules rules = Rules();
            PlayerMovementState movement = InitialMovement();
            PlayerInputCommand sprint = Command(1, movement.Position,
                moveZ: 1d, yaw: 90d, sprint: true);

            movement = CoopGameplayRules.IntegrateMovement(
                movement, sprint, 1, rules);
            Assert.That(movement.Position.X, Is.EqualTo(0.5d).Within(0.0001d));
            Assert.That(movement.Velocity.X, Is.EqualTo(5d).Within(0.0001d));

            PlayerInputCommand crouch = Command(2, movement.Position,
                moveZ: 1d, crouch: true);
            movement = CoopGameplayRules.IntegrateMovement(
                movement, crouch, 1, rules);
            Assert.That(movement.Stance, Is.EqualTo(PlayerStance.Crouching));
            Assert.That(movement.Velocity.Z,
                Is.EqualTo(1.5d).Within(0.0001d));

            PlayerInputCommand jump = Command(3, movement.Position,
                jump: true, crouch: false);
            movement = CoopGameplayRules.IntegrateMovement(
                movement, jump, 1, rules);
            Assert.That(movement.Stance, Is.EqualTo(PlayerStance.Standing));
            Assert.That(movement.Grounded, Is.False);
            Assert.That(movement.Position.Y, Is.GreaterThan(0d));

            for (int tick = 4; tick < 30 && !movement.Grounded; tick++)
            {
                movement = CoopGameplayRules.IntegrateMovement(
                    movement, Command(tick, movement.Position), 1, rules);
            }
            Assert.That(movement.Grounded, Is.True);
            Assert.That(movement.Position.Y, Is.EqualTo(0d));
        }

        [Test]
        public void ServerKeepsCrouchWithoutHeadroomAndRejectsJumpSpam()
        {
            CoopServerRules rules = Rules();
            AuthoritativeCoopSimulation simulation = Simulation(rules);
            PlayerMovementState state = simulation.CaptureSnapshot()
                .Player(1).Movement;

            PlayerInputCommand crouch = PredictedCommand(
                rules, state, 1, crouch: true);
            Assert.That(simulation.Step(new[] { crouch })
                .Commands.Single().Accepted, Is.True);
            simulation.SetStandingClearanceValidator((_, _) => false);
            state = simulation.CaptureSnapshot().Player(1).Movement;
            PlayerInputCommand blockedStand = PredictedCommand(
                rules, state, 2, crouch: false);
            Assert.That(simulation.Step(new[] { blockedStand })
                    .Commands.Single().Accepted,
                Is.True,
                "空间不足时应接受移动/瞄准输入，只保持下蹲姿态。");
            Assert.That(simulation.CaptureSnapshot().Player(1).IsCrouching,
                Is.True);

            simulation.SetStandingClearanceValidator((_, _) => true);
            state = simulation.CaptureSnapshot().Player(1).Movement;
            PlayerInputCommand jump = PredictedCommand(
                rules, state, 3, jump: true, crouch: true);
            Assert.That(simulation.Step(new[] { jump })
                .Commands.Single().Accepted, Is.True);
            state = simulation.CaptureSnapshot().Player(1).Movement;
            PlayerInputCommand spam = PredictedCommand(
                rules, state, 4, jump: true, crouch: true);
            Assert.That(simulation.Step(new[] { spam })
                    .Commands.Single().RejectionReason,
                Is.EqualTo(CommandRejectionReason.JumpRateExceeded));
        }

        [Test]
        public void AccelerationIsBoundedByServerRule()
        {
            var rules = new CoopServerRules(
                tickRate: 10,
                maximumMoveSpeed: 5d,
                claimedPositionTolerance: 0.005d,
                walkSpeed: 5d,
                maximumAcceleration: 2d);
            PlayerMovementState movement = CoopGameplayRules.IntegrateMovement(
                InitialMovement(), Command(1, default, moveZ: 1d), 1, rules);

            Assert.That(movement.Velocity.Z,
                Is.EqualTo(0.2d).Within(0.000001d));
            Assert.That(movement.Position.Z,
                Is.EqualTo(0.02d).Within(0.000001d));

            AuthoritativeCoopSimulation simulation = Simulation(rules);
            PlayerInputCommand acceleratedClaim = Command(
                1, new NetVector3(0d, 0d, 0.5d), moveZ: 1d);
            Assert.That(simulation.Step(new[] { acceleratedClaim })
                    .Commands.Single().RejectionReason,
                Is.EqualTo(CommandRejectionReason.ImpossibleDisplacement));
        }

        [Test]
        public void ServerCollisionResolverOwnsTheAcceptedPlayerPosition()
        {
            CoopServerRules rules = Rules();
            AuthoritativeCoopSimulation simulation = Simulation(rules);
            simulation.SetPlayerMovementResolver((_, current, desired) =>
                new PlayerMovementState(
                    current.Position,
                    default,
                    desired.AimYawDegrees,
                    desired.AimPitchDegrees,
                    desired.Stance,
                    desired.Grounded,
                    desired.LastJumpTick,
                    desired.GroundHeight));

            PlayerInputCommand intoWall = Command(
                1,
                default,
                moveZ: 1d);
            CommandResolution resolution = simulation.Step(
                new[] { intoWall }).Commands.Single();

            Assert.That(resolution.Accepted, Is.True);
            Assert.That(simulation.CaptureSnapshot().Player(1).Position,
                Is.EqualTo(default(NetVector3)));
        }

        [Test]
        public void RemoteInterpolationIncludesFacingStanceAndGroundState()
        {
            var interpolation = new RemoteSnapshotInterpolator(
                interpolationDelayTicks: 0);
            interpolation.Push(new RemotePlayerSnapshot(
                10, 2, default, 350d, 0d, default,
                PlayerStance.Standing, true));
            interpolation.Push(new RemotePlayerSnapshot(
                20, 2, new NetVector3(10d, 2d, 0d), 10d, 20d,
                new NetVector3(2d, 1d, 0d),
                PlayerStance.Crouching, false));

            RemoteInterpolationSample sample = interpolation.Sample(16d);

            Assert.That(sample.Position.X, Is.EqualTo(6d));
            Assert.That(sample.AimYawDegrees, Is.EqualTo(362d));
            Assert.That(sample.Stance, Is.EqualTo(PlayerStance.Crouching));
            Assert.That(sample.Grounded, Is.False);
        }

        [Test]
        public void NetcodeSnapshotRoundTripsVelocityStanceAndGroundedState()
        {
            var source = new AuthoritativePlayerState(
                2,
                new NetVector3(1d, 2d, 3d),
                90d,
                17,
                45d,
                -12d,
                new NetVector3(4d, -2d, 1d),
                PlayerStance.Crouching,
                grounded: false,
                lastJumpTick: 14,
                groundHeight: 0.5d);

            AuthoritativePlayerState restored =
                NetcodePlayerState.FromDomain(30, source).ToDomain();

            Assert.That(restored.Velocity,
                Is.EqualTo(source.Velocity));
            Assert.That(restored.Stance, Is.EqualTo(PlayerStance.Crouching));
            Assert.That(restored.Grounded, Is.False);
            Assert.That(restored.LastJumpTick, Is.EqualTo(14));
            Assert.That(restored.GroundHeight, Is.EqualTo(0.5d));
        }

        [Test]
        public void ReconciliationPreservesTickGapAfterAcknowledgedInput()
        {
            CoopServerRules rules = Rules();
            var prediction = new LocalPredictionBuffer(rules, 1, default);
            PlayerInputCommand first = Command(
                10, default, moveZ: 1d);
            prediction.Predict(first);
            PlayerMovementState authoritativeMovement =
                CoopGameplayRules.IntegrateMovement(
                    InitialMovement(), first, 1, rules);
            PlayerInputCommand delayed = Command(
                13, prediction.PredictedPosition, moveZ: 1d);
            NetVector3 expected = prediction.Predict(delayed);
            var authoritative = new AuthoritativePlayerState(
                1,
                authoritativeMovement.Position,
                100d,
                first.Sequence,
                0d,
                0d,
                authoritativeMovement.Velocity,
                authoritativeMovement.Stance,
                authoritativeMovement.Grounded,
                authoritativeMovement.LastJumpTick,
                authoritativeMovement.GroundHeight);

            PredictionCorrection correction =
                prediction.Reconcile(authoritative);

            Assert.That(correction.ErrorDistance,
                Is.EqualTo(0d).Within(0.000001d));
            Assert.That(prediction.PredictedPosition,
                Is.EqualTo(expected));
        }

        [TestCase(0)]
        [TestCase(80)]
        [TestCase(150)]
        public void LatencyProbeRecordsCorrectionsAndError(int latencyMs)
        {
            MovementLatencyReport report =
                NetworkMovementLatencyProbe.Run(latencyMs);

            Assert.That(report.LatencyMilliseconds, Is.EqualTo(latencyMs));
            Assert.That(report.SubmittedCommands, Is.EqualTo(180));
            Assert.That(report.CorrectionCount, Is.GreaterThanOrEqualTo(0));
            Assert.That(report.MaximumError, Is.LessThan(1.5d));
            TestContext.Out.WriteLine(
                $"RTT {latencyMs}ms: corrections={report.CorrectionCount}, " +
                $"max={report.MaximumError:F4}m, mean={report.MeanError:F4}m");
        }

        private static PlayerInputCommand PredictedCommand(
            CoopServerRules rules,
            PlayerMovementState state,
            int tick,
            bool jump = false,
            bool crouch = false)
        {
            PlayerInputCommand provisional = Command(
                tick, state.Position, jump: jump, crouch: crouch);
            PlayerMovementState predicted = CoopGameplayRules.IntegrateMovement(
                state, provisional, 1, rules);
            return Command(tick, predicted.Position,
                jump: jump, crouch: crouch);
        }

        private static PlayerInputCommand Command(
            int tick,
            NetVector3 claimed,
            double moveX = 0d,
            double moveZ = 0d,
            double yaw = 0d,
            bool sprint = false,
            bool jump = false,
            bool crouch = false)
        {
            return new PlayerInputCommand(
                1, (uint)tick, (ulong)(1000 + tick), tick,
                moveX, moveZ, yaw, 0d, false, claimed,
                jump, sprint, crouch);
        }

        private static PlayerMovementState InitialMovement() => new(
            default, default, 0d, 0d, PlayerStance.Standing,
            true, long.MinValue, 0d);

        private static CoopServerRules Rules() => new(
            tickRate: 10,
            maximumPastCommandTicks: 8,
            historyCapacity: 16,
            maximumMoveSpeed: 5d,
            walkSpeed: 2d,
            sprintSpeed: 5d,
            crouchSpeed: 1.5d,
            maximumAcceleration: 1000d,
            gravity: 20d,
            jumpSpeed: 4d,
            minimumJumpIntervalTicks: 3);

        private static AuthoritativeCoopSimulation Simulation(
            CoopServerRules rules) => new(
            rules,
            new[] { new CoopPlayerSpawn(1, default) },
            new[]
            {
                new CoopTargetSpawn(1,
                    new NetVector3(0d, 0d, 50d), 1d, 100d)
            });
    }
}
