using System;
using System.Linq;
using FPS.Determinism;
using NUnit.Framework;

namespace FPS.Tests.Architecture
{
    public sealed class Issue55ReplayTimelineTests
    {
        [Test]
        public void TimelineFiltersMajorEventsAndOrdersThemByTickAndSequence()
        {
            DeterministicRun run = CreateRepresentativeRun();
            var timeline = new ReplayTimeline(run.Record);

            ReplayTimelinePage page = timeline.Query(
                ReplayTimelineFilter.Only(ReplayTimelineEventKind.Shot, ReplayTimelineEventKind.EnemyKilled),
                0,
                20);

            Assert.That(page.TotalItems, Is.EqualTo(2));
            Assert.That(page.Items.Select(item => item.Kind), Is.EqualTo(new[]
            {
                ReplayTimelineEventKind.Shot,
                ReplayTimelineEventKind.EnemyKilled
            }));
            Assert.That(page.Items.Select(item => item.Tick), Is.Ordered);
        }

        [Test]
        public void QueryReturnsOneBoundedPageForLargeRecords()
        {
            var run = new DeterministicRun(55);
            for (int index = 0; index < 10000; index++)
            {
                run.RecordEvent(RunEventType.InputSampled,
                    StableEventPayload.Create(RunPayloadField.Number("flags", index)));
                run.AdvanceTick();
            }

            ReplayTimelinePage page = new ReplayTimeline(run.Record).Query(
                ReplayTimelineFilter.All,
                37,
                100);

            Assert.That(page.Items.Count, Is.EqualTo(100));
            Assert.That(page.PageIndex, Is.EqualTo(37));
            Assert.That(page.TotalItems, Is.EqualTo(10000));
            Assert.That(page.TotalPages, Is.EqualTo(100));
            Assert.That(page.Items[0].Sequence, Is.EqualTo(3700));
        }

        [Test]
        public void SelectedEventShowsEntityPayloadAndAdjacentStateSummaries()
        {
            var run = new DeterministicRun(55);
            run.RecordCheckpoint(ReplayStateSnapshot.Create(
                ReplayStateField.Number("enemy/7/health", 100),
                ReplayStateField.Number("wave/current", 1)));
            run.AdvanceTick(5);
            run.RecordEvent(RunEventType.EnemyKilled, StableEventPayload.Create(
                RunPayloadField.Text("enemyType", "spider"),
                RunPayloadField.Number("spawnId", 7),
                RunPayloadField.Number("wave", 1)));
            run.AdvanceTick(5);
            run.RecordCheckpoint(ReplayStateSnapshot.Create(
                ReplayStateField.Number("enemy/7/health", 0),
                ReplayStateField.Number("wave/current", 1)));

            var timeline = new ReplayTimeline(run.Record);
            ReplayTimelineItem item = timeline.Query(ReplayTimelineFilter.All, 0, 20).Items.Single();
            ReplayTimelineDetails details = timeline.Describe(item);

            Assert.That(details.EntityId, Is.EqualTo("7"));
            Assert.That(details.Payload["enemyType"], Is.EqualTo("spider"));
            Assert.That(details.Before.Tick, Is.EqualTo(0));
            Assert.That(details.After.Tick, Is.EqualTo(10));
            Assert.That(details.ChangedFields, Does.Contain("enemy/7/health: 100 → 0"));
        }

        [Test]
        public void TimelineJumpsToFirstChecksumDivergence()
        {
            DeterministicRun run = CreateRepresentativeRun();
            var divergence = ReplayDivergence.CreateForDiagnostics(
                18, "enemy/7/health", "20", "19");
            var timeline = new ReplayTimeline(run.Record, divergence);

            ReplayTimelineLocation location = timeline.FindFirstDivergence(10);

            Assert.That(location.Found, Is.True);
            Assert.That(location.Item.Kind, Is.EqualTo(ReplayTimelineEventKind.ChecksumDivergence));
            Assert.That(location.Item.Tick, Is.EqualTo(18));
            Assert.That(location.PageIndex, Is.GreaterThanOrEqualTo(0));
        }

        [Test]
        public void JsonLoaderExplainsUnsupportedVersionAndMigration()
        {
            string json = RunRecordCodec.Export(CreateRepresentativeRun().Record)
                .Replace("\"schemaVersion\":1", "\"schemaVersion\":9");

            ReplayTimelineLoadResult result = ReplayTimeline.LoadJson(json);

            Assert.That(result.Success, Is.False);
            Assert.That(result.DetectedSchemaVersion, Is.EqualTo(9));
            Assert.That(result.Message, Does.Contain("版本 9"));
            Assert.That(result.Message, Does.Contain("迁移"));
        }

        [Test]
        public void PageSizeIsCappedToPreventUnboundedUiAllocation()
        {
            var timeline = new ReplayTimeline(CreateRepresentativeRun().Record);

            Assert.That(
                () => timeline.Query(
                    ReplayTimelineFilter.All,
                    0,
                    ReplayTimeline.MaximumPageSize + 1),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        private static DeterministicRun CreateRepresentativeRun()
        {
            var run = new DeterministicRun(55);
            run.RecordEvent(RunEventType.WaveGenerated,
                StableEventPayload.Create(RunPayloadField.Number("wave", 1)));
            run.AdvanceTick(3);
            run.RecordEvent(RunEventType.WaveTransition,
                StableEventPayload.Create(
                    RunPayloadField.Text("phase", "started"),
                    RunPayloadField.Number("wave", 1)));
            run.RecordEvent(RunEventType.ShotFired,
                StableEventPayload.Create(RunPayloadField.Text("weapon", "rifle")));
            run.AdvanceTick(2);
            run.RecordEvent(RunEventType.EnemyKilled,
                StableEventPayload.Create(RunPayloadField.Number("spawnId", 7)));
            return run;
        }
    }
}
