using FPS.Simulation;
using FPS.Determinism;
using NUnit.Framework;

namespace FPS.Tests.Architecture
{
    public sealed class Issue60DynamicCombatDirectorTests
    {
        [Test]
        public void SameSeedAndFactsStartTheSameRearFlankWarning()
        {
            DynamicCombatDirector first = CreateDirector(60060);
            DynamicCombatDirector second = CreateDirector(60060);
            CombatDirectorInput input = StrongPlayerInput(0);

            CombatDirectorOutput firstOutput = first.Advance(input);
            CombatDirectorOutput secondOutput = second.Advance(input);

            Assert.That(firstOutput.Signal,
                Is.EqualTo(CombatDirectorSignal.WarningStarted));
            Assert.That(secondOutput.Signal, Is.EqualTo(firstOutput.Signal));
            Assert.That(secondOutput.CurrentEvent.EnemyTypeId,
                Is.EqualTo(firstOutput.CurrentEvent.EnemyTypeId));
            Assert.That(secondOutput.CurrentEvent.RequestedCount,
                Is.EqualTo(firstOutput.CurrentEvent.RequestedCount));
            Assert.That(secondOutput.CurrentEvent.SignedDirectionDegrees,
                Is.EqualTo(firstOutput.CurrentEvent.SignedDirectionDegrees));
            Assert.That(
                System.Math.Abs(firstOutput.CurrentEvent.SignedDirectionDegrees),
                Is.InRange(120, 165));
        }

        [Test]
        public void WarningMustFinishBeforeBoundedSpawnsThenCooldownBegins()
        {
            DynamicCombatDirector director = CreateDirector(60060);

            CombatDirectorOutput warning = director.Advance(
                StrongPlayerInput(0));
            Assert.That(warning.CurrentEvent.RequestedCount, Is.EqualTo(2));
            Assert.That(
                director.Advance(StrongPlayerInput(44)).Signal,
                Is.EqualTo(CombatDirectorSignal.None));

            CombatDirectorOutput firstRequest = director.Advance(
                StrongPlayerInput(45));
            Assert.That(firstRequest.Signal,
                Is.EqualTo(CombatDirectorSignal.SpawnRequested));
            director.ReportSpawnOutcome(45, true);
            Assert.That(director.Phase,
                Is.EqualTo(CombatDirectorPhase.Deploying));

            Assert.That(
                director.Advance(StrongPlayerInput(46)).Signal,
                Is.EqualTo(CombatDirectorSignal.SpawnRequested));
            director.ReportSpawnOutcome(46, true);

            Assert.That(director.Phase,
                Is.EqualTo(CombatDirectorPhase.Cooldown));
            Assert.That(director.CurrentEvent.SpawnedCount, Is.EqualTo(2));
            Assert.That(
                director.Advance(StrongPlayerInput(405)).Phase,
                Is.EqualTo(CombatDirectorPhase.Cooldown));
            Assert.That(
                director.Advance(StrongPlayerInput(406)).Phase,
                Is.EqualTo(CombatDirectorPhase.Observing));
        }

        [Test]
        public void WarningSnapshotRestoresPhaseAndRandomSequenceExactly()
        {
            DynamicCombatDirector source = CreateDirector(8118);
            CombatDirectorOutput warning = source.Advance(
                StrongPlayerInput(0));
            CombatDirectorRuntimeSnapshot saved = source.CaptureState();

            DynamicCombatDirector restored = CreateDirector(8118);
            Assert.That(restored.TryRestore(saved, out string error),
                Is.True,
                error);
            Assert.That(restored.Phase,
                Is.EqualTo(CombatDirectorPhase.Warning));
            Assert.That(restored.CurrentEvent.EventId,
                Is.EqualTo(warning.CurrentEvent.EventId));
            Assert.That(restored.CurrentEvent.EnemyTypeId,
                Is.EqualTo(warning.CurrentEvent.EnemyTypeId));

            CombatDirectorOutput expected = source.Advance(
                StrongPlayerInput(45));
            CombatDirectorOutput actual = restored.Advance(
                StrongPlayerInput(45));
            Assert.That(actual.Signal, Is.EqualTo(expected.Signal));
            Assert.That(actual.CurrentEvent.SignedDirectionDegrees,
                Is.EqualTo(expected.CurrentEvent.SignedDirectionDegrees));
            Assert.That(restored.CaptureState().RandomState,
                Is.EqualTo(source.CaptureState().RandomState));
        }

        [Test]
        public void ExtremePressureAndResourceShortageSuppressInterventions()
        {
            DynamicCombatDirector pressured = CreateDirector(60060);
            var extremePressure = new CombatPressureSnapshot(
                0.1f, 0f, 0.05f, 0.9f, 0f, 1f, 0.1f, 0, 0);
            CombatDirectorResources available = StrongPlayerInput(0).Resources;
            Assert.That(pressured.Advance(new CombatDirectorInput(
                    0, extremePressure, available)).Signal,
                Is.EqualTo(CombatDirectorSignal.None));

            DynamicCombatDirector starved = CreateDirector(60060);
            var noCapacity = new CombatDirectorResources(
                4, 0, 6, true, available.RoleCandidates);
            Assert.That(starved.Advance(new CombatDirectorInput(
                    0, StrongPlayerInput(0).Pressure, noCapacity)).Signal,
                Is.EqualTo(CombatDirectorSignal.None));

            DynamicCombatDirector unreachable = CreateDirector(60060);
            var noPath = new CombatDirectorResources(
                4, 2, 6, false, available.RoleCandidates);
            Assert.That(unreachable.Advance(new CombatDirectorInput(
                    0, StrongPlayerInput(0).Pressure, noPath)).Signal,
                Is.EqualTo(CombatDirectorSignal.None));
        }

        [Test]
        public void DirectorRandomConsumptionDoesNotChangeOtherNamedStreams()
        {
            var baseline = new NamedRandomStreams(60060);
            var exercised = new NamedRandomStreams(60060);
            for (int index = 0; index < 32; index++)
                exercised.Get(RunRandomStream.Director).NextUInt32();

            RunRandomStream[] unaffected =
            {
                RunRandomStream.Upgrade,
                RunRandomStream.Loot,
                RunRandomStream.Wave,
                RunRandomStream.Elite
            };
            foreach (RunRandomStream stream in unaffected)
                Assert.That(exercised.Get(stream).NextUInt32(),
                    Is.EqualTo(baseline.Get(stream).NextUInt32()),
                    stream.ToString());
        }

        [Test]
        public void DirectorDecisionHasDedicatedReplayTimelineFilter()
        {
            var run = new DeterministicRun(60060);
            run.RecordEvent(
                RunEventType.CombatDirectorDecision,
                StableEventPayload.Create(
                    RunPayloadField.Number("eventId", 1),
                    RunPayloadField.Text("result", "warning")));

            var timeline = new ReplayTimeline(run.Record);
            ReplayTimelinePage page = timeline.Query(
                ReplayTimelineFilter.Only(
                    ReplayTimelineEventKind.CombatDirector),
                0,
                10);
            Assert.That(page.TotalItems, Is.EqualTo(1));
            Assert.That(page.Items[0].Title, Is.EqualTo("战斗导演"));
        }

        private static DynamicCombatDirector CreateDirector(long seed)
        {
            return new DynamicCombatDirector(
                seed,
                new CombatDirectorConfiguration(
                    0,
                    15,
                    45,
                    360,
                    2,
                    2,
                    0.55f));
        }

        private static CombatDirectorInput StrongPlayerInput(long tick)
        {
            return new CombatDirectorInput(
                tick,
                new CombatPressureSnapshot(
                    1f,
                    0.5f,
                    0.9f,
                    0f,
                    0.8f,
                    0.2f,
                    0.65f,
                    3,
                    -2),
                new CombatDirectorResources(
                    4,
                    2,
                    6,
                    true,
                    new[]
                    {
                        new CombatDirectorRoleCandidate(
                            "spider_raider",
                            "raider",
                            3),
                        new CombatDirectorRoleCandidate(
                            "spider_suppressor",
                            "suppressor",
                            3)
                    }));
        }
    }
}
