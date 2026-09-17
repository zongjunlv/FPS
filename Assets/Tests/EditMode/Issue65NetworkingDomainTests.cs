using System;
using System.Collections.Generic;
using System.Linq;
using FPS.Networking.Domain;
using NUnit.Framework;

namespace FPS.Tests.Architecture
{
    public sealed class Issue65NetworkingDomainTests
    {
        [Test]
        public void FixedTickCommandsAreOrderIndependentAndServerMovesPlayers()
        {
            AuthoritativeCoopSimulation first = CreateSimulation();
            AuthoritativeCoopSimulation second = CreateSimulation();
            PlayerInputCommand playerOne = Command(
                1, 1, 101, 1, 1d, 0d, new NetVector3(0.1d, 0d, 0d));
            PlayerInputCommand playerTwo = Command(
                2, 1, 201, 1, 0d, 1d, new NetVector3(2d, 0d, 0.1d));

            AuthoritativeTickResult ordered = first.Step(
                new[] { playerOne, playerTwo });
            AuthoritativeTickResult reversed = second.Step(
                new[] { playerTwo, playerOne });

            Assert.That(ordered.Tick, Is.EqualTo(1));
            Assert.That(ordered.Commands.All(value => value.Accepted), Is.True);
            Assert.That(ordered.Commands.Select(value => value.Command.PlayerId),
                Is.EqualTo(new[] { 1, 2 }));
            AssertVector(ordered.Snapshot.Player(1).Position,
                reversed.Snapshot.Player(1).Position);
            AssertVector(ordered.Snapshot.Player(2).Position,
                reversed.Snapshot.Player(2).Position);
            Assert.That(ordered.Events.Select(value => value.Kind),
                Is.EqualTo(reversed.Events.Select(value => value.Kind)));
        }

        [Test]
        public void SequenceNonceAndClientTickRejectReplaysAndBadTimestamps()
        {
            var rules = new CoopServerRules(
                tickRate: 10,
                maximumPastCommandTicks: 2,
                maximumFutureCommandTicks: 1,
                historyCapacity: 4);
            AuthoritativeCoopSimulation simulation = CreateSimulation(rules);

            Assert.That(simulation.Step(new[]
            {
                Command(1, 1, 11, 1, claimed: NetVector3Zero)
            }).Commands.Single().Accepted, Is.True);
            AssertRejected(simulation.Step(new[]
            {
                Command(1, 1, 12, 2, claimed: NetVector3Zero)
            }), CommandRejectionReason.InvalidSequence);
            AssertRejected(simulation.Step(new[]
            {
                Command(1, 2, 11, 3, claimed: NetVector3Zero)
            }), CommandRejectionReason.DuplicateNonce);
            AssertRejected(simulation.Step(new[]
            {
                Command(1, 3, 13, 20, claimed: NetVector3Zero)
            }), CommandRejectionReason.TimestampInFuture);
            simulation.Step(Array.Empty<PlayerInputCommand>());
            simulation.Step(Array.Empty<PlayerInputCommand>());
            AssertRejected(simulation.Step(new[]
            {
                Command(1, 4, 14, 1, claimed: NetVector3Zero)
            }), CommandRejectionReason.TimestampTooOld);
        }

        [Test]
        public void MovementDisplacementAimAndFireRateAreServerValidated()
        {
            var rules = new CoopServerRules(
                tickRate: 10,
                maximumAimDegreesPerSecond: 90d,
                fireCooldownTicks: 3,
                claimedPositionTolerance: 0.05d,
                historyCapacity: 16);
            AuthoritativeCoopSimulation simulation = CreateSimulation(rules);

            AssertRejected(simulation.Step(new[]
            {
                Command(1, 1, 1, 1, 1d, 1d, NetVector3Zero)
            }), CommandRejectionReason.InvalidMovement);
            AssertRejected(simulation.Step(new[]
            {
                Command(1, 2, 2, 2, 1d, 0d, new NetVector3(10d, 0d, 0d))
            }), CommandRejectionReason.ImpossibleDisplacement);
            Assert.That(simulation.Step(new[]
            {
                Command(1, 3, 3, 3, claimed: NetVector3Zero, fire: true)
            }).Commands.Single().Accepted, Is.True);
            AssertRejected(simulation.Step(new[]
            {
                Command(1, 4, 4, 4, claimed: NetVector3Zero,
                    yaw: 30d)
            }), CommandRejectionReason.AimRateExceeded);
            AssertRejected(simulation.Step(new[]
            {
                Command(1, 5, 5, 5, claimed: NetVector3Zero, fire: true)
            }), CommandRejectionReason.FireRateExceeded);

            AssertVector(simulation.CaptureSnapshot().Player(1).Position,
                NetVector3Zero);
        }

        [Test]
        public void MissingInputTicksExpandOnlyTheLegalMovementAllowance()
        {
            var rules = new CoopServerRules(
                tickRate: 10,
                maximumFutureCommandTicks: 2,
                historyCapacity: 16,
                maximumMoveSpeed: 5d,
                claimedPositionTolerance: 0.05d,
                walkSpeed: 5d);
            AuthoritativeCoopSimulation simulation = CreateSimulation(rules);
            AuthoritativeTickResult first = simulation.Step(new[]
            {
                Command(1, 1, 1, 1, 0d, 1d,
                    new NetVector3(0d, 0d, 0.5d))
            });
            Assert.That(first.Commands.Single().Accepted, Is.True);
            simulation.Step(Array.Empty<PlayerInputCommand>());
            simulation.Step(Array.Empty<PlayerInputCommand>());

            AuthoritativeTickResult recovered = simulation.Step(new[]
            {
                Command(1, 2, 2, 4, 1d, 0d,
                    new NetVector3(2.4d, 0d, 0.5d))
            });
            Assert.That(recovered.Commands.Single().Accepted, Is.True,
                "丢失 Tick 后应容纳这段时间内最大合法速度产生的位置差。");

            AuthoritativeTickResult cheated = simulation.Step(new[]
            {
                Command(1, 3, 3, 5, 0d, 0d,
                    new NetVector3(20d, 0d, 0d))
            });
            Assert.That(cheated.Commands.Single().RejectionReason,
                Is.EqualTo(CommandRejectionReason.ImpossibleDisplacement));
        }

        [Test]
        public void HitscanRewindsBoundedHistoryAndUsesHistoricalTargetPosition()
        {
            var rules = new CoopServerRules(
                tickRate: 10,
                maximumPastCommandTicks: 5,
                historyCapacity: 6,
                fireCooldownTicks: 1,
                shotDamage: 60d);
            AuthoritativeCoopSimulation simulation = CreateSimulation(rules);
            simulation.Step(Array.Empty<PlayerInputCommand>());
            simulation.SetAuthoritativeTargetPosition(
                101,
                new NetVector3(10d, 0d, 10d));

            AuthoritativeTickResult result = simulation.Step(new[]
            {
                Command(1, 1, 1, 1, claimed: NetVector3Zero, fire: true)
            });

            ShotResolution shot = result.Commands.Single().Shot;
            Assert.That(shot.Kind, Is.EqualTo(ShotResolutionKind.Hit));
            Assert.That(shot.TargetId, Is.EqualTo(101));
            Assert.That(shot.RewoundTick, Is.EqualTo(1));
            Assert.That(result.Snapshot.Target(101).Health, Is.EqualTo(40d));

            for (int index = 0; index < 8; index++)
                simulation.Step(Array.Empty<PlayerInputCommand>());
            Assert.That(simulation.HistoryCount, Is.EqualTo(6));
            AssertRejected(simulation.Step(new[]
            {
                Command(1, 2, 2, 1, claimed: NetVector3Zero, fire: true)
            }), CommandRejectionReason.TimestampTooOld);
        }

        [Test]
        public void ServerAloneProducesDamageKillDropAndWaveOutcome()
        {
            var rules = new CoopServerRules(shotDamage: 100d);
            AuthoritativeCoopSimulation simulation = CreateSimulation(rules);

            AuthoritativeTickResult result = simulation.Step(new[]
            {
                Command(1, 1, 1, 0, claimed: NetVector3Zero, fire: true)
            });

            Assert.That(result.Commands.Single().Shot.Kind,
                Is.EqualTo(ShotResolutionKind.Killed));
            Assert.That(result.Events.Select(value => value.Kind),
                Does.Contain(AuthoritativeEventKind.TargetDamaged));
            Assert.That(result.Events.Select(value => value.Kind),
                Does.Contain(AuthoritativeEventKind.TargetKilled));
            AuthoritativeEvent drop = result.Events.Single(value =>
                value.Kind == AuthoritativeEventKind.LootDropped);
            Assert.That(drop.DefinitionId, Is.EqualTo("medical-kit"));
            Assert.That(result.Events.Select(value => value.Kind),
                Does.Contain(AuthoritativeEventKind.WaveCompleted));
            Assert.That(result.Snapshot.WaveStatus,
                Is.EqualTo(AuthoritativeWaveStatus.Completed));
            Assert.That(result.Snapshot.Target(101).IsAlive, Is.False);
        }

        [Test]
        public void ServerDamageOwnsPlayerDeathAndFailsOnlyAfterBothPlayersDie()
        {
            AuthoritativeCoopSimulation simulation = CreateSimulation();

            IReadOnlyList<AuthoritativeEvent> first =
                simulation.ApplyServerDamageToPlayer(1, 1000d);
            IReadOnlyList<AuthoritativeEvent> second =
                simulation.ApplyServerDamageToPlayer(2, 1000d);

            Assert.That(first.Any(value =>
                value.Kind == AuthoritativeEventKind.WaveFailed), Is.False);
            Assert.That(second.Select(value => value.Kind),
                Does.Contain(AuthoritativeEventKind.WaveFailed));
            Assert.That(simulation.CaptureSnapshot().WaveStatus,
                Is.EqualTo(AuthoritativeWaveStatus.Failed));
        }

        [Test]
        public void PredictionUsesServerRuleReplaysUnackedAndAppliesThresholds()
        {
            var rules = new CoopServerRules(
                tickRate: 10,
                maximumMoveSpeed: 1d,
                predictionCorrectionThreshold: 0.01d,
                predictionSnapThreshold: 1d);
            var prediction = new LocalPredictionBuffer(
                rules,
                1,
                NetVector3Zero);
            prediction.Predict(Command(
                1, 1, 1, 1, 1d, 0d, new NetVector3(0.1d, 0d, 0d)));
            prediction.Predict(Command(
                1, 2, 2, 2, 1d, 0d, new NetVector3(0.2d, 0d, 0d)));

            PredictionCorrection smooth = prediction.Reconcile(
                new AuthoritativePlayerState(
                    1,
                    new NetVector3(0.08d, 0d, 0d),
                    100d,
                    1,
                    0d,
                    0d));

            Assert.That(smooth.Kind,
                Is.EqualTo(PredictionCorrectionKind.Smooth));
            Assert.That(smooth.ReplayedCommandCount, Is.EqualTo(1));
            Assert.That(smooth.ReplayTargetPosition.X,
                Is.EqualTo(0.18d).Within(0.000001d));
            Assert.That(smooth.AppliedPosition.X,
                Is.EqualTo(0.19d).Within(0.000001d));

            PredictionCorrection snap = prediction.Reconcile(
                new AuthoritativePlayerState(
                    1,
                    new NetVector3(-5d, 0d, 0d),
                    100d,
                    2,
                    0d,
                    0d));
            Assert.That(snap.Kind, Is.EqualTo(PredictionCorrectionKind.Snap));
            Assert.That(snap.AppliedPosition.X, Is.EqualTo(-5d));
            Assert.That(prediction.PendingCommands, Is.Empty);
        }

        [Test]
        public void RemoteSnapshotsInterpolateOutOfOrderAndAcrossYawWrap()
        {
            var interpolation = new RemoteSnapshotInterpolator(
                capacity: 3,
                interpolationDelayTicks: 2);
            interpolation.Push(new RemotePlayerSnapshot(
                20, 2, new NetVector3(10d, 0d, 0d), 10d, 20d));
            interpolation.Push(new RemotePlayerSnapshot(
                10, 2, NetVector3Zero, 350d, 0d));

            RemoteInterpolationSample sample = interpolation.Sample(17d);

            Assert.That(sample.Available, Is.True);
            Assert.That(sample.Ratio, Is.EqualTo(0.5d));
            Assert.That(sample.Position.X, Is.EqualTo(5d));
            Assert.That(sample.AimYawDegrees, Is.EqualTo(360d));
            Assert.That(sample.AimPitchDegrees, Is.EqualTo(10d));
            interpolation.Push(new RemotePlayerSnapshot(
                30, 2, new NetVector3(20d, 0d, 0d), 20d, 0d));
            interpolation.Push(new RemotePlayerSnapshot(
                40, 2, new NetVector3(30d, 0d, 0d), 30d, 0d));
            Assert.That(interpolation.Count, Is.EqualTo(3));
        }

        [Test]
        public void ZeroEightyAndOneFiftyMillisecondProfilesAreDeterministic()
        {
            long zero = DeliveryTick(NetworkConditionProfile.Latency0());
            long eighty = DeliveryTick(NetworkConditionProfile.Latency80());
            long oneFifty = DeliveryTick(NetworkConditionProfile.Latency150());

            Assert.That(zero, Is.EqualTo(10));
            Assert.That(eighty, Is.EqualTo(14));
            Assert.That(oneFifty, Is.EqualTo(18));
        }

        [Test]
        public void ControlledLossMetricsBandwidthFeedbackAndDivergenceRepeat()
        {
            NetworkScenarioMetrics first = RunNetworkMetrics(65065);
            NetworkScenarioMetrics second = RunNetworkMetrics(65065);

            Assert.That(first.DroppedPackets, Is.EqualTo(second.DroppedPackets));
            Assert.That(first.DeliveredPackets, Is.EqualTo(second.DeliveredPackets));
            Assert.That(first.DroppedPackets, Is.InRange(200, 300));
            Assert.That(first.SentPackets, Is.EqualTo(1001));
            Assert.That(first.SentBytes, Is.EqualTo(32064L));
            Assert.That(first.DeliveredBytes,
                Is.LessThan(first.SentBytes));
            Assert.That(first.AverageHitFeedbackMilliseconds,
                Is.EqualTo(80d));
            Assert.That(first.CorrectionCount, Is.EqualTo(1));
            Assert.That(first.SnapCorrectionCount, Is.Zero);
            Assert.That(first.MaximumCorrectionDistance,
                Is.EqualTo(0.5d));
            Assert.That(first.AverageStateDivergence,
                Is.EqualTo(0.2d));
            Assert.That(first.MaximumStateDivergence,
                Is.EqualTo(0.3d));
        }

        [Test]
        public void NetworkingDomainHasNoUnityEngineDependency()
        {
            string[] references = typeof(AuthoritativeCoopSimulation).Assembly
                .GetReferencedAssemblies()
                .Select(value => value.Name)
                .ToArray();

            Assert.That(references, Does.Not.Contain("UnityEngine"));
            Assert.That(references.Any(value => value.StartsWith(
                "UnityEngine.", StringComparison.Ordinal)), Is.False);
        }

        private static NetworkScenarioMetrics RunNetworkMetrics(long seed)
        {
            var model = new DeterministicNetworkConditionModel(
                seed,
                100,
                new NetworkConditionProfile("loss", 80, 2500));
            for (int index = 1; index <= 1000; index++)
            {
                model.Transmit(new NetworkPacket(
                    index,
                    NetworkPacketKind.InputCommand,
                    1,
                    0,
                    0,
                    32));
            }
            model.Transmit(new NetworkPacket(
                1001,
                NetworkPacketKind.HitFeedback,
                0,
                1,
                4,
                64,
                correlationTick: 0));
            model.Drain(100);
            model.RecordCorrection(new PredictionCorrection(
                PredictionCorrectionKind.Smooth,
                0.5d,
                NetVector3Zero,
                new NetVector3(0.5d, 0d, 0d),
                new NetVector3(0.25d, 0d, 0d),
                1));
            model.RecordStateDivergence(0.1d);
            model.RecordStateDivergence(0.3d);
            return model.CaptureMetrics();
        }

        private static long DeliveryTick(NetworkConditionProfile profile)
        {
            var model = new DeterministicNetworkConditionModel(65, 100, profile);
            return model.Transmit(new NetworkPacket(
                1,
                NetworkPacketKind.InputCommand,
                1,
                0,
                10,
                32)).DeliveryTick;
        }

        private static void AssertRejected(
            AuthoritativeTickResult result,
            CommandRejectionReason reason)
        {
            CommandResolution resolution = result.Commands.Single();
            Assert.That(resolution.Accepted, Is.False);
            Assert.That(resolution.RejectionReason, Is.EqualTo(reason));
            Assert.That(result.Events.Last().Kind,
                Is.EqualTo(AuthoritativeEventKind.CommandRejected));
            Assert.That(result.Events.Last().RejectionReason,
                Is.EqualTo(reason));
        }

        private static PlayerInputCommand Command(
            int playerId,
            uint sequence,
            ulong nonce,
            long clientTick,
            double moveX = 0d,
            double moveZ = 0d,
            NetVector3 claimed = default,
            bool fire = false,
            double yaw = 0d,
            double pitch = 0d)
        {
            return new PlayerInputCommand(
                playerId,
                sequence,
                nonce,
                clientTick,
                moveX,
                moveZ,
                yaw,
                pitch,
                fire,
                claimed);
        }

        private static AuthoritativeCoopSimulation CreateSimulation(
            CoopServerRules rules = null)
        {
            return new AuthoritativeCoopSimulation(
                rules ?? new CoopServerRules(),
                new[]
                {
                    new CoopPlayerSpawn(1, NetVector3Zero),
                    new CoopPlayerSpawn(2, new NetVector3(2d, 0d, 0d))
                },
                new[]
                {
                    new CoopTargetSpawn(
                        101,
                        new NetVector3(0d, 0d, 10d),
                        0.5d,
                        100d,
                        "medical-kit")
                });
        }

        private static void AssertVector(NetVector3 actual, NetVector3 expected)
        {
            Assert.That(actual.X, Is.EqualTo(expected.X).Within(0.000001d));
            Assert.That(actual.Y, Is.EqualTo(expected.Y).Within(0.000001d));
            Assert.That(actual.Z, Is.EqualTo(expected.Z).Within(0.000001d));
        }

        private static NetVector3 NetVector3Zero => new(0d, 0d, 0d);
    }
}
