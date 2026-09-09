using System.Collections.Generic;
using FPS.Determinism;
using NUnit.Framework;

namespace FPS.Tests.Architecture
{
    public sealed class Issue54ReplayTests
    {
        [Test]
        public void ReplayDispatchesEventsByTickAndCompletesWithoutDivergence()
        {
            var source = new DeterministicRun(5401);
            source.RecordEvent(RunEventType.WaveGenerated,
                StableEventPayload.Create(RunPayloadField.Number("wave", 1)));
            source.AdvanceTick();
            source.RecordEvent(RunEventType.InputSampled,
                StableEventPayload.Create(RunPayloadField.Number("moveX", 1000)));
            source.RecordCheckpoint(ReplayStateSnapshot.Create(
                ReplayStateField.Number("player/x", 1000)));

            var adapter = new RecordingReplayAdapter();
            var replay = new RunReplayPlayer(source.Record, adapter);
            ReplayAdvanceResult result = replay.AdvanceTo(1);

            Assert.That(result.Status, Is.EqualTo(RunReplayStatus.Completed));
            Assert.That(adapter.Seed, Is.EqualTo(5401));
            Assert.That(adapter.Applied.Count, Is.EqualTo(2));
            Assert.That(adapter.Applied[0].Type, Is.EqualTo(RunEventType.WaveGenerated));
            Assert.That(adapter.Applied[1].Tick, Is.EqualTo(1));
            Assert.That(result.Divergence, Is.Null);
        }

        [Test]
        public void CheckpointSurvivesJsonRoundTripAndOlderRecordWithoutItStillLoads()
        {
            var run = new DeterministicRun(54);
            run.AdvanceTick(5);
            run.RecordCheckpoint(ReplayStateSnapshot.Create(
                ReplayStateField.Number("wave/current", 2),
                ReplayStateField.Text("inventory/0/id", "medkit")));

            string json = RunRecordCodec.Export(run.Record);
            RunRecord imported = RunRecordCodec.Import(json);
            Assert.That(imported.Checkpoints.Count, Is.EqualTo(1));
            Assert.That(imported.Checkpoints[0].Tick, Is.EqualTo(5));
            Assert.That(imported.Checkpoints[0].Checksum,
                Is.EqualTo(run.Record.Checkpoints[0].Checksum));

            string legacyJson = json.Substring(0, json.LastIndexOf(",\"checkpoints\"", System.StringComparison.Ordinal)) + "}";
            Assert.That(() => RunRecordCodec.Import(legacyJson), Throws.Nothing);
            Assert.That(RunRecordCodec.Import(legacyJson).Checkpoints, Is.Empty);
        }

        [Test]
        public void TamperedInputStopsAtFirstDifferentPlayerField()
        {
            DeterministicRun run = RecordedSingleEvent(
                RunEventType.InputSampled,
                "player/input/moveX");
            var adapter = new MutatingReplayAdapter("player/input/moveX", 2);

            ReplayAdvanceResult result = new RunReplayPlayer(run.Record, adapter).AdvanceTo(1);

            Assert.That(result.Status, Is.EqualTo(RunReplayStatus.Diverged));
            Assert.That(result.Divergence.Tick, Is.EqualTo(1));
            Assert.That(result.Divergence.System, Is.EqualTo("player"));
            Assert.That(result.Divergence.FieldPath, Is.EqualTo("player/input/moveX"));
            Assert.That(result.Divergence.Expected, Is.EqualTo("1"));
            Assert.That(result.Divergence.Actual, Is.EqualTo("2"));
        }

        [Test]
        public void MissingEventApplicationStopsAndIdentifiesEnemyEntity()
        {
            DeterministicRun run = RecordedSingleEvent(
                RunEventType.EnemySpawned,
                "enemy/7/alive");
            var adapter = new MutatingReplayAdapter("enemy/7/alive", 0);

            ReplayAdvanceResult result = new RunReplayPlayer(run.Record, adapter).AdvanceTo(1);

            Assert.That(result.Status, Is.EqualTo(RunReplayStatus.Diverged));
            Assert.That(result.Divergence.EntityId, Is.EqualTo("7"));
            Assert.That(result.Divergence.Summary, Does.Contain("Tick 1"));
            Assert.That(result.Divergence.Summary, Does.Contain("enemy/7/alive"));
        }

        [Test]
        public void AdvanceDoesNotDispatchFutureTickEvents()
        {
            var run = new DeterministicRun(54);
            run.AdvanceTick(2);
            run.RecordEvent(RunEventType.InputSampled, string.Empty);
            run.AdvanceTick(3);
            run.RecordEvent(RunEventType.LootGenerated, string.Empty);
            var adapter = new MutatingReplayAdapter("events/count", 0);
            var replay = new RunReplayPlayer(run.Record, adapter);

            ReplayAdvanceResult result = replay.AdvanceTo(2);

            Assert.That(result.Status, Is.EqualTo(RunReplayStatus.Running));
            Assert.That(adapter.AppliedCount, Is.EqualTo(1));
            Assert.That(replay.CurrentTick, Is.EqualTo(2));
        }

        [Test]
        public void AdvancingAcrossSeveralCheckpointsValidatesEachTickBeforeFutureEvents()
        {
            var run = new DeterministicRun(54);
            run.AdvanceTick();
            run.RecordEvent(RunEventType.InputSampled, string.Empty);
            run.RecordCheckpoint(ReplayStateSnapshot.Create(
                ReplayStateField.Number("events/count", 1)));
            run.AdvanceTick(2);
            run.RecordEvent(RunEventType.LootGenerated, string.Empty);
            run.RecordCheckpoint(ReplayStateSnapshot.Create(
                ReplayStateField.Number("events/count", 2)));

            ReplayAdvanceResult result = new RunReplayPlayer(
                run.Record, new CountingReplayAdapter()).AdvanceTo(3);

            Assert.That(result.Status, Is.EqualTo(RunReplayStatus.Completed));
            Assert.That(result.Divergence, Is.Null);
        }

        [Test]
        public void RuntimeSplitDispatchesBeforeAndVerifiesAfterSameFixedTick()
        {
            var run = new DeterministicRun(54);
            run.AdvanceTick();
            run.RecordEvent(RunEventType.InputSampled, string.Empty);
            run.RecordCheckpoint(ReplayStateSnapshot.Create(
                ReplayStateField.Number("events/count", 1)));
            var replay = new RunReplayPlayer(run.Record, new CountingReplayAdapter());

            ReplayAdvanceResult beforeSimulation = replay.DispatchTo(1);
            ReplayAdvanceResult afterSimulation = replay.VerifyThrough(1);

            Assert.That(beforeSimulation.Status, Is.EqualTo(RunReplayStatus.Running));
            Assert.That(afterSimulation.Status, Is.EqualTo(RunReplayStatus.Completed));
        }

        private static DeterministicRun RecordedSingleEvent(
            RunEventType type,
            string fieldPath)
        {
            var run = new DeterministicRun(54);
            run.AdvanceTick();
            run.RecordEvent(type, string.Empty);
            run.RecordCheckpoint(ReplayStateSnapshot.Create(
                ReplayStateField.Number(fieldPath, 1)));
            return run;
        }

        private sealed class RecordingReplayAdapter : IRunReplayAdapter
        {
            public readonly List<RunEvent> Applied = new();
            public long Seed { get; private set; }

            public void Begin(long runSeed, IReadOnlyList<RunEvent> configurationEvents)
            {
                Seed = runSeed;
            }

            public void Apply(RunEvent runEvent)
            {
                Applied.Add(runEvent);
            }

            public ReplayStateSnapshot CaptureState()
            {
                return ReplayStateSnapshot.Create(
                    ReplayStateField.Number("player/x", 1000));
            }
        }

        private sealed class MutatingReplayAdapter : IRunReplayAdapter
        {
            private readonly string fieldPath;
            private readonly int capturedValue;

            public MutatingReplayAdapter(string fieldPath, int capturedValue)
            {
                this.fieldPath = fieldPath;
                this.capturedValue = capturedValue;
            }

            public int AppliedCount { get; private set; }

            public void Begin(long runSeed, IReadOnlyList<RunEvent> configurationEvents) { }

            public void Apply(RunEvent runEvent)
            {
                AppliedCount++;
            }

            public ReplayStateSnapshot CaptureState() => ReplayStateSnapshot.Create(
                ReplayStateField.Number(fieldPath, capturedValue));
        }

        private sealed class CountingReplayAdapter : IRunReplayAdapter
        {
            private int count;
            public void Begin(long runSeed, IReadOnlyList<RunEvent> configurationEvents) { }
            public void Apply(RunEvent runEvent) => count++;
            public ReplayStateSnapshot CaptureState() => ReplayStateSnapshot.Create(
                ReplayStateField.Number("events/count", count));
        }
    }
}
