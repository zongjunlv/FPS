using System.IO;
using FPS.Determinism;
using NUnit.Framework;

namespace FPS.Tests.Architecture
{
    public sealed class Issue53DeterminismTests
    {
        [Test]
        public void SameRunSeedProducesSameNamedSequencesAcrossSessions()
        {
            var first = new NamedRandomStreams(531234);
            var second = new NamedRandomStreams(531234);

            Assert.That(first.Get(RunRandomStream.Upgrade).NextUInt32(),
                Is.EqualTo(second.Get(RunRandomStream.Upgrade).NextUInt32()));
            Assert.That(first.Get(RunRandomStream.Loot).NextInt(17),
                Is.EqualTo(second.Get(RunRandomStream.Loot).NextInt(17)));
        }

        [Test]
        public void AdvancingLootDoesNotChangeWaveSequence()
        {
            var changed = new NamedRandomStreams(53);
            var baseline = new NamedRandomStreams(53);
            for (int index = 0; index < 100; index++) changed.Get(RunRandomStream.Loot).NextUInt32();

            Assert.That(changed.Get(RunRandomStream.Wave).NextUInt32(),
                Is.EqualTo(baseline.Get(RunRandomStream.Wave).NextUInt32()));
        }

        [Test]
        public void StatelessForkDependsOnSeedStreamAndContextButNotSequentialProgress()
        {
            var streams = new NamedRandomStreams(999);
            uint before = streams.Fork(RunRandomStream.Elite, "wave:4/spawn:2").NextUInt32();
            for (int index = 0; index < 30; index++) streams.Get(RunRandomStream.Elite).NextUInt32();
            uint after = streams.Fork(RunRandomStream.Elite, "wave:4/spawn:2").NextUInt32();
            uint otherContext = streams.Fork(RunRandomStream.Elite, "wave:4/spawn:3").NextUInt32();

            Assert.That(after, Is.EqualTo(before));
            Assert.That(otherContext, Is.Not.EqualTo(before));
        }

        [Test]
        public void PayloadHasStableOrdinalFieldOrderAndInvariantValues()
        {
            string first = StableEventPayload.Create(
                RunPayloadField.Text("upgrade", "rapid fire"),
                RunPayloadField.Number("level", 2),
                RunPayloadField.Flag("elite", true));
            string reordered = StableEventPayload.Create(
                RunPayloadField.Flag("elite", true),
                RunPayloadField.Text("upgrade", "rapid fire"),
                RunPayloadField.Number("level", 2));

            Assert.That(first, Is.EqualTo("elite=true&level=2&upgrade=rapid%20fire"));
            Assert.That(reordered, Is.EqualTo(first));
        }

        [Test]
        public void RunUsesLogicalTicksAndAssignsContiguousEventSequence()
        {
            var run = new DeterministicRun(4200);
            run.RecordEvent(RunEventType.LootGenerated,
                StableEventPayload.Create(RunPayloadField.Text("item", "medkit")));
            run.AdvanceTick(3);
            run.RecordEvent(RunEventType.WaveGenerated,
                StableEventPayload.Create(RunPayloadField.Number("wave", 1)));

            Assert.That(run.Tick, Is.EqualTo(3));
            Assert.That(run.Record.Events[0].Sequence, Is.Zero);
            Assert.That(run.Record.Events[0].Tick, Is.Zero);
            Assert.That(run.Record.Events[1].Sequence, Is.EqualTo(1));
            Assert.That(run.Record.Events[1].Tick, Is.EqualTo(3));
        }

        [Test]
        public void ExportThenImportPreservesVersionedRecordExactly()
        {
            DeterministicRun run = ExampleRun();

            string exported = RunRecordCodec.Export(run.Record);
            RunRecord imported = RunRecordCodec.Import(exported);

            Assert.That(imported.SchemaVersion, Is.EqualTo(RunRecord.CurrentSchemaVersion));
            Assert.That(imported.RunSeed, Is.EqualTo(42));
            Assert.That(imported.CurrentTick, Is.EqualTo(2));
            Assert.That(imported.Events.Count, Is.EqualTo(2));
            Assert.That(imported.Events[1].Payload, Is.EqualTo(run.Record.Events[1].Payload));
            Assert.That(RunRecordCodec.Export(imported), Is.EqualTo(exported));
        }

        [Test]
        public void SequenceComparisonReportsFirstDeterministicDivergence()
        {
            DeterministicRun expected = ExampleRun();
            var actual = new DeterministicRun(42);
            actual.RecordEvent(RunEventType.UpgradeSelected,
                StableEventPayload.Create(RunPayloadField.Text("id", "vitality")));
            actual.AdvanceTick(2);
            actual.RecordEvent(RunEventType.LootGenerated,
                StableEventPayload.Create(RunPayloadField.Text("id", "armor")));

            RunRecordValidationResult result = DeterministicRunValidator.Compare(expected.Record, actual.Record);

            Assert.That(result.Success, Is.False);
            Assert.That(result.DivergenceIndex, Is.EqualTo(1));
            Assert.That(result.Error, Does.Contain("payload"));
        }

        [Test]
        public void ImportRejectsUnsupportedSchemaAndNonCanonicalSequence()
        {
            string exported = RunRecordCodec.Export(ExampleRun().Record);

            Assert.That(() => RunRecordCodec.Import(exported.Replace("\"schemaVersion\":1", "\"schemaVersion\":9")),
                Throws.TypeOf<InvalidDataException>());
            Assert.That(() => RunRecordCodec.Import(exported.Replace("\"sequence\":1", "\"sequence\":5")),
                Throws.TypeOf<InvalidDataException>());
        }

        private static DeterministicRun ExampleRun()
        {
            var run = new DeterministicRun(42);
            run.RecordEvent(RunEventType.UpgradeSelected,
                StableEventPayload.Create(RunPayloadField.Text("id", "vitality")));
            run.AdvanceTick(2);
            run.RecordEvent(RunEventType.LootGenerated,
                StableEventPayload.Create(RunPayloadField.Text("id", "medkit")));
            return run;
        }
    }
}
