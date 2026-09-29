using System;
using System.Linq;
using FPS.Networking.Domain;
using NUnit.Framework;

namespace FPS.Tests.Architecture
{
    public sealed class NetworkTimingRegressionTests
    {
        [Test]
        public void MissingAcknowledgementsCannotGrowPredictionHistoryWithoutBound()
        {
            var rules = new CoopServerRules();
            var prediction = new LocalPredictionBuffer(rules, 1, default);
            for (uint sequence = 1; sequence <= 600; sequence++)
            {
                var command = new PlayerInputCommand(
                    1, sequence, sequence, sequence,
                    0d, 1d, 0d, 0d, false,
                    prediction.PredictedPosition);
                try
                {
                    prediction.Predict(command);
                }
                catch (InvalidOperationException)
                {
                    break;
                }
            }

            Assert.That(prediction.PendingCommands.Count,
                Is.LessThanOrEqualTo(rules.TickRate * 2),
                "服务器停止确认输入时，不能无限保留并回放预测命令。");
        }

        [Test]
        public void HealthyClockAdvancesInputWithoutWaitingForAnotherSnapshot()
        {
            var clock = new ClientServerClock(60);
            clock.Observe(100, 1, 0d, 0.1d);
            Assert.That(clock.TryGetNextInputTick(0.1d, 101,
                out long tick), Is.True);
            Assert.That(tick, Is.GreaterThan(101));
            Assert.That(clock.LastObservedTick, Is.EqualTo(100));
        }

        [Test]
        public void InputStampDoesNotMistakeHalfRttForExactOneWayLatency()
        {
            var clock = new ClientServerClock(60);
            clock.Observe(100, 1, 0d, 0.4d);
            Assert.That(clock.Estimate(0d), Is.EqualTo(112d));
            Assert.That(clock.TryGetNextInputTick(0d, 100,
                out long tick), Is.True);
            Assert.That(tick, Is.EqualTo(101),
                "输入时刻不应把 RTT/2 的估算当作可验证的服务器当前时刻。");
            Assert.That(clock.TryGetNextInputTick(0.1d, tick,
                out long following), Is.True);
            Assert.That(following, Is.GreaterThan(tick));
            Assert.That(following, Is.LessThanOrEqualTo(107));
        }

        [Test]
        public void DuplicateSnapshotCannotKeepAStalledClockHealthy()
        {
            var clock = new ClientServerClock(60);
            clock.Observe(100, 1, 0d, 0d);
            Assert.That(clock.Observe(100, 1, 1.9d, 0d), Is.False);
            Assert.That(clock.TryGetNextInputTick(2.01d, 100,
                out _), Is.False);
            Assert.That(clock.Estimate(30d), Is.LessThanOrEqualTo(220d));
        }

        [Test]
        public void LongPauseAndNewRunExplicitlyResynchronizeClock()
        {
            var clock = new ClientServerClock(60);
            clock.Observe(100, 1, 0d, 0d);
            int revision = clock.Revision;
            clock.Estimate(30d);
            clock.Observe(1900, 1, 30d, 0.1d);
            Assert.That(clock.Revision, Is.EqualTo(revision + 1));
            Assert.That(clock.Estimate(30d), Is.EqualTo(1903d));
            Assert.That(clock.TryGetNextInputTick(30d, 100,
                out long tick), Is.True);
            Assert.That(tick, Is.GreaterThanOrEqualTo(1901));
            clock.Observe(0, 2, 30.1d, 0d);
            Assert.That(clock.Estimate(30.1d), Is.EqualTo(0d));
            Assert.That(clock.Revision, Is.EqualTo(revision + 2));
        }

        [Test]
        public void SlewedClockAndPresentationRemainMonotonicWithinRevision()
        {
            var clock = new ClientServerClock(60);
            clock.Observe(100, 1, 0d, 0.2d);
            double estimate = clock.Estimate(0.1d);
            double render = clock.PresentationTick(0.1d);
            int revision = clock.Revision;
            clock.Observe(101, 1, 0.11d, 0.01d);
            Assert.That(clock.Revision, Is.EqualTo(revision));
            Assert.That(clock.Estimate(0.11d), Is.GreaterThanOrEqualTo(estimate));
            Assert.That(clock.PresentationTick(0.11d),
                Is.GreaterThanOrEqualTo(render));
        }

        [Test]
        public void TwentyHzSnapshotClockAndInterpolatorMoveEveryRenderFrame()
        {
            var clock = new ClientServerClock(60);
            var interpolation = new RemoteSnapshotInterpolator(
                interpolationDelayTicks: 0);
            clock.Observe(0, 1, 0d, 0d);
            interpolation.Push(new RemotePlayerSnapshot(0, 2,
                default, 0d, 0d));
            double previousX = 0d;
            for (int renderFrame = 1; renderFrame <= 120; renderFrame++)
            {
                // Real Domain interpolator: 20Hz snapshots, 120Hz renderer,
                // no latency and no loss. A 2-tick delay reaches the newest
                // sample before the next 3-tick snapshot even on this link.
                double now = renderFrame / 120d;
                if (renderFrame % 6 == 0)
                {
                    long serverTick = renderFrame / 2;
                    clock.Observe(serverTick, 1, now, 0d);
                    interpolation.Push(new RemotePlayerSnapshot(serverTick, 2,
                        new NetVector3(serverTick, 0d, 0d), 0d, 0d));
                }
                RemoteInterpolationSample sample = interpolation.Sample(
                    clock.PresentationTick(now));
                Assert.That(sample.Available, Is.True);
                if (renderFrame > 24)
                    Assert.That(sample.Position.X - previousX,
                        Is.EqualTo(0.5d).Within(0.000001d),
                        "20Hz快照之间应连续插值，不能周期触顶hold再跳进。");
                previousX = sample.Position.X;
            }
        }

        [Test]
        public void PresentationDelayIncludesSnapshotIntervalAndBoundedJitterMargin()
        {
            var clock = new ClientServerClock(60);
            clock.Observe(100, 1, 0d, 0d);
            Assert.That(clock.InterpolationDelayTicks,
                Is.GreaterThanOrEqualTo(5d),
                "60Hz/20Hz需保留3tick发布间隔加2tick抖动余量。");
            clock.Observe(103, 1, 0.05d, 0.5d);
            Assert.That(clock.InterpolationDelayTicks,
                Is.LessThanOrEqualTo(12d), "表现缓冲仍应限制在200ms以内。");
        }

        [Test]
        public void BufferedSnapshotBurstResynchronizesInsteadOfLongClockDrift()
        {
            var clock = new ClientServerClock(60);
            clock.Observe(100, 1, 0d, 0d);
            int revision = clock.Revision;
            clock.Observe(900, 1, 0.1d, 0d);
            Assert.That(clock.Revision, Is.EqualTo(revision + 1));
            Assert.That(clock.Estimate(0.1d), Is.EqualTo(900d));
        }

        [Test]
        public void PredictionCannotIntegrateThirtySecondPauseInOneCommand()
        {
            var rules = new CoopServerRules();
            var prediction = new LocalPredictionBuffer(rules, 1, default);
            prediction.Predict(Input(1, 1, 1));
            NetVector3 before = prediction.PredictedPosition;
            prediction.Predict(Input(1, 2, 1801));
            Assert.That(NetVector3.Distance(before, prediction.PredictedPosition),
                Is.LessThanOrEqualTo(rules.MaximumMoveSpeed *
                    rules.MaximumInputGapTicks * rules.FixedDeltaSeconds));
        }

        [Test]
        public void ShortInputQueueBoundsEachPlayerAndDrainsWithoutStarvation()
        {
            var buffer = new BoundedInputCommandBuffer(4, 2);
            for (uint sequence = 1; sequence <= 4; sequence++)
                Assert.That(buffer.TryEnqueue(Input(1, sequence, sequence)),
                    Is.True);
            Assert.That(buffer.TryEnqueue(Input(1, 5, 5)), Is.False);
            Assert.That(buffer.TryEnqueue(Input(2, 1, 1)), Is.True);
            PlayerInputCommand[] first = buffer.DrainTick();
            Assert.That(first.Where(value => value.PlayerId == 1)
                .Select(value => value.Sequence), Is.EqualTo(new uint[] { 1, 2 }));
            Assert.That(first.Count(value => value.PlayerId == 2), Is.EqualTo(1));
            Assert.That(buffer.DrainTick().Select(value => value.Sequence),
                Is.EqualTo(new uint[] { 3, 4 }));
            Assert.That(buffer.Count, Is.Zero);
        }

        [Test]
        public void RejectedFireAckDoesNotPretendItsMovementTickWasExecuted()
        {
            var rules = new CoopServerRules();
            var prediction = new LocalPredictionBuffer(rules, 1, default);
            var simulation = new AuthoritativeCoopSimulation(rules,
                new[] { new CoopPlayerSpawn(1, default) },
                new[] { new CoopTargetSpawn(1,
                    new NetVector3(0d, 0d, 10d), 0.5d, 100d) });
            var first = new PlayerInputCommand(1, 1, 1, 1,
                0d, 1d, 0d, 0d, true, new NetVector3(0d, 0d, 0.1d));
            prediction.Predict(first);
            Assert.That(simulation.Step(new[] { first }).Commands[0].Accepted,
                Is.True);
            var rejected = new PlayerInputCommand(1, 2, 2, 2,
                0d, 1d, 0d, 0d, true, new NetVector3(0d, 0d, 0.2d));
            prediction.Predict(rejected);
            AuthoritativeTickResult second = simulation.Step(new[] { rejected });
            Assert.That(second.Commands[0].RejectionReason,
                Is.EqualTo(CommandRejectionReason.FireRateExceeded));
            prediction.Predict(Input(1, 3, 3));
            PredictionCorrection correction = prediction.Reconcile(
                second.Snapshot.Player(1));
            Assert.That(correction.ReplayTargetPosition.Z,
                Is.EqualTo(0.3d).Within(0.00001d),
                "输入序号已确认不等于该条移动已经执行；回放需基于实际执行的 Tick。");
        }

        [Test]
        public void AcknowledgementAndResynchronizationReleasePredictionCapacity()
        {
            var rules = new CoopServerRules(tickRate: 30);
            var prediction = new LocalPredictionBuffer(rules, 1, default);
            for (uint sequence = 1;
                 sequence <= prediction.MaximumPendingCommands; sequence++)
                prediction.Predict(Input(1, sequence, sequence));
            Assert.That(prediction.HasCapacity, Is.False);
            Assert.Throws<InvalidOperationException>(() =>
                prediction.Predict(Input(1, 61, 61)));
            prediction.Reconcile(new AuthoritativePlayerState(1,
                prediction.PredictedPosition, 100d, 30, 0d, 0d));
            Assert.That(prediction.HasCapacity, Is.True);
            prediction.ResetToAuthoritative(new AuthoritativePlayerState(1,
                new NetVector3(3d, 0d, 4d), 100d, 60, 0d, 0d));
            Assert.That(prediction.PendingCommands, Is.Empty);
            Assert.That(prediction.PredictedPosition,
                Is.EqualTo(new NetVector3(3d, 0d, 4d)));
        }

        [Test]
        public void FutureInputWaitsInSequenceAndDoesNotBlockOtherPlayers()
        {
            var buffer = new BoundedInputCommandBuffer();
            buffer.TryEnqueue(Input(1, 1, 5));
            buffer.TryEnqueue(Input(1, 2, 1));
            buffer.TryEnqueue(Input(2, 1, 1));
            PlayerInputCommand[] ready = buffer.DrainTick(2);
            Assert.That(ready.Length, Is.EqualTo(1));
            Assert.That(ready[0].PlayerId, Is.EqualTo(2));
            Assert.That(buffer.CountForPlayer(1), Is.EqualTo(2));
            Assert.That(buffer.DrainTick(5).Select(value => value.Sequence),
                Is.EqualTo(new uint[] { 1, 2 }));
        }

        private static PlayerInputCommand Input(int playerId, uint sequence,
            long tick) => new(playerId, sequence, sequence, tick,
                0d, 1d, 0d, 0d, false, default);
    }
}
