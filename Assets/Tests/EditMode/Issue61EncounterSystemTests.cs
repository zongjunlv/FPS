using System.Linq;
using FPS.Determinism;
using FPS.SaveGame;
using FPS.Simulation;
using NUnit.Framework;
using UnityEditor;

namespace FPS.Tests.Architecture
{
    public sealed class Issue61EncounterSystemTests
    {
        [Test]
        public void SameDefinitionsAndFactsProduceTheSameEncounterStart()
        {
            EncounterDefinitionSpec[] definitions =
            {
                new EncounterDefinitionSpec(
                    "city_new.encounter.ambush",
                    1,
                    EncounterKind.Ambush,
                    "侧后伏击",
                    "击退伏击单位",
                    EncounterTrigger.Wave(1),
                    EncounterObjective.Eliminate(2),
                    EncounterMainFlowPolicy.Parallel,
                    45,
                    600)
            };
            var first = new EncounterSequence(definitions);
            var second = new EncounterSequence(definitions);
            var facts = new EncounterFacts(
                12,
                1,
                WaveRunPhase.Fighting,
                MissionFlowState.EliminateTargets,
                true);

            EncounterTransition expected = first.Advance(facts);
            EncounterTransition actual = second.Advance(facts);

            Assert.That(actual.Signal,
                Is.EqualTo(EncounterSignal.Started));
            Assert.That(actual.Signal, Is.EqualTo(expected.Signal));
            Assert.That(actual.EncounterId,
                Is.EqualTo(expected.EncounterId));
            Assert.That(actual.Phase, Is.EqualTo(expected.Phase));
            Assert.That(actual.DeadlineTick,
                Is.EqualTo(expected.DeadlineTick));
        }

        [Test]
        public void FourObjectivesProgressDifferentlyWithoutBlockingMainFlow()
        {
            EncounterDefinitionSpec[] definitions = CreateAllDefinitions();
            var sequence = new EncounterSequence(definitions);

            Assert.That(sequence.Advance(Facts(0, 1)).Signal,
                Is.EqualTo(EncounterSignal.Started));
            Assert.That(sequence.Advance(Facts(2, 1, kills: 1)).Signal,
                Is.EqualTo(EncounterSignal.Progressed));
            Assert.That(sequence.Advance(Facts(3, 1, kills: 1)).Signal,
                Is.EqualTo(EncounterSignal.Succeeded));

            Assert.That(sequence.Advance(Facts(4, 2)).Signal,
                Is.EqualTo(EncounterSignal.Started));
            Assert.That(sequence.Advance(Facts(5, 2, eliteKills: 1)).Signal,
                Is.EqualTo(EncounterSignal.Succeeded));

            Assert.That(sequence.Advance(Facts(
                    6, 3,
                    mission: MissionFlowState.EliminateTargets)).Signal,
                Is.EqualTo(EncounterSignal.None),
                "仍在清怪阶段时不得提前开始终端守点。" );

            Assert.That(sequence.Advance(Facts(
                    7, 3, mission: MissionFlowState.ActivateTerminal)).Signal,
                Is.EqualTo(EncounterSignal.Started));
            Assert.That(sequence.Advance(Facts(
                    8, 3, mission: MissionFlowState.ActivateTerminal,
                    inArea: true)).Progress,
                Is.EqualTo(1));
            Assert.That(sequence.Advance(Facts(
                    9, 3, mission: MissionFlowState.ActivateTerminal,
                    inArea: false)).Progress,
                Is.EqualTo(0));
            sequence.Advance(Facts(10, 3,
                mission: MissionFlowState.ActivateTerminal, inArea: true));
            sequence.Advance(Facts(11, 3,
                mission: MissionFlowState.ActivateTerminal, inArea: true));
            Assert.That(sequence.Advance(Facts(12, 3,
                    mission: MissionFlowState.ActivateTerminal,
                    inArea: true)).Signal,
                Is.EqualTo(EncounterSignal.Succeeded));

            Assert.That(sequence.Advance(Facts(
                    13, 3,
                    mission: MissionFlowState.ExtractionAvailable)).Signal,
                Is.EqualTo(EncounterSignal.Started));
            Assert.That(sequence.Advance(Facts(
                    14, 3,
                    mission: MissionFlowState.ExtractionAvailable,
                    extracted: true)).Signal,
                Is.EqualTo(EncounterSignal.Succeeded));
            Assert.That(sequence.IsComplete, Is.True);
        }

        [Test]
        public void SnapshotRestoresMidEncounterAndRewardIsIdempotent()
        {
            EncounterDefinitionSpec definition = CreateAllDefinitions()[0];
            var source = new EncounterSequence(new[] { definition });
            source.Advance(Facts(100, 1));
            source.Advance(Facts(101, 1, kills: 1));
            EncounterRuntimeSnapshot saved = source.CaptureState();

            var restored = new EncounterSequence(new[] { definition });
            Assert.That(restored.TryRestore(saved, out string error),
                Is.True,
                error);
            EncounterTransition completed = restored.Advance(
                Facts(102, 1, kills: 1));
            Assert.That(completed.Signal, Is.EqualTo(EncounterSignal.Succeeded));
            Assert.That(restored.AcknowledgeReward(completed.EncounterId).Signal,
                Is.EqualTo(EncounterSignal.RewardAcknowledged));
            Assert.That(restored.AcknowledgeReward(completed.EncounterId).Signal,
                Is.EqualTo(EncounterSignal.None));
        }

        [Test]
        public void MissingResourcesSkipEncounterSoMissionCanContinue()
        {
            var sequence = new EncounterSequence(CreateAllDefinitions());
            EncounterTransition transition = sequence.Advance(new EncounterFacts(
                0,
                1,
                WaveRunPhase.Fighting,
                MissionFlowState.EliminateTargets,
                false));

            Assert.That(transition.Signal, Is.EqualTo(EncounterSignal.Skipped));
            Assert.That(sequence.HasActiveEncounter, Is.False);
        }

        [Test]
        public void CityNewCatalogContainsFourVersionedEncounterAssets()
        {
            CityNewContentCatalog catalog =
                AssetDatabase.LoadAssetAtPath<CityNewContentCatalog>(
                    "Assets/Resources/Content/CityNew/CityNewContentCatalog.asset");

            Assert.That(catalog, Is.Not.Null);
            Assert.That(catalog.TryValidate(out string error), Is.True, error);
            Assert.That(catalog.EncounterSequence.ContentVersion, Is.EqualTo(1));
            Assert.That(catalog.EncounterSequence.Encounters
                    .Select(definition => definition.Kind),
                Is.EquivalentTo(new[]
                {
                    EncounterKind.Ambush,
                    EncounterKind.EliteEscort,
                    EncounterKind.TimedHold,
                    EncounterKind.ExtractionPursuit
                }));
            Assert.That(catalog.EncounterSequence.Encounters,
                Has.All.Matches<EncounterDefinition>(definition =>
                    definition.ContentVersion >= 1 &&
                    definition.MainFlowPolicy ==
                    EncounterMainFlowPolicy.Parallel &&
                    definition.RewardItem != null &&
                    definition.Roster.Count > 0));
        }

        [Test]
        public void EncounterTransitionsAreVisibleInReplayTimeline()
        {
            var run = new DeterministicRun(61061);
            run.RecordEvent(
                RunEventType.EncounterTransition,
                StableEventPayload.Create(
                    RunPayloadField.Text("id", "encounter.ambush"),
                    RunPayloadField.Number("signal", 1)));

            ReplayTimelinePage page = new ReplayTimeline(run.Record).Query(
                ReplayTimelineFilter.Only(
                    ReplayTimelineEventKind.Encounter),
                0,
                10);

            Assert.That(page.TotalItems, Is.EqualTo(1));
            Assert.That(page.Items[0].Kind,
                Is.EqualTo(ReplayTimelineEventKind.Encounter));
            Assert.That(page.Items[0].Title, Is.EqualTo("遭遇事件"));
        }

        [Test]
        public void EncounterClockIsProtectedBySaveChecksum()
        {
            var snapshot = new RunSnapshot
            {
                Encounter = new EncounterSaveSnapshot
                {
                    SequenceContentVersion = 1,
                    NextIndex = 1,
                    ActiveEncounterId = "encounter.hold",
                    ActiveDefinitionVersion = 1,
                    Phase = (int)EncounterPhase.Active,
                    StartedTick = 120,
                    ActiveTick = 20,
                    DeadlineTick = 1020,
                    CurrentTick = 140,
                    Progress = 15
                }
            };
            string checksum = SnapshotChecksum.Compute(snapshot);

            snapshot.Encounter.CurrentTick++;

            Assert.That(SnapshotChecksum.Compute(snapshot),
                Is.Not.EqualTo(checksum));
        }

        private static EncounterFacts Facts(
            long tick,
            int wave,
            int kills = 0,
            int eliteKills = 0,
            MissionFlowState mission = MissionFlowState.EliminateTargets,
            bool inArea = false,
            bool extracted = false)
        {
            return new EncounterFacts(
                tick,
                wave,
                WaveRunPhase.Fighting,
                mission,
                true,
                kills,
                eliteKills,
                inArea,
                extracted);
        }

        private static EncounterDefinitionSpec[] CreateAllDefinitions()
        {
            return new[]
            {
                new EncounterDefinitionSpec(
                    "encounter.ambush", 1, EncounterKind.Ambush,
                    "侧后伏击", "击退伏击单位",
                    EncounterTrigger.Wave(1),
                    EncounterObjective.Eliminate(2),
                    EncounterMainFlowPolicy.Parallel, 0, 90),
                new EncounterDefinitionSpec(
                    "encounter.elite", 1, EncounterKind.EliteEscort,
                    "精英护送", "击杀精英单位",
                    EncounterTrigger.Wave(2),
                    EncounterObjective.EliminateElite(),
                    EncounterMainFlowPolicy.Parallel, 0, 90),
                new EncounterDefinitionSpec(
                    "encounter.hold", 1, EncounterKind.TimedHold,
                    "限时守点", "守住终端",
                    EncounterTrigger.Mission(MissionFlowState.ActivateTerminal),
                    EncounterObjective.HoldTicks(3),
                    EncounterMainFlowPolicy.Parallel, 0, 90),
                new EncounterDefinitionSpec(
                    "encounter.pursuit", 1,
                    EncounterKind.ExtractionPursuit,
                    "撤离追击", "抵达撤离点",
                    EncounterTrigger.Mission(
                        MissionFlowState.ExtractionAvailable),
                    EncounterObjective.ReachExtraction(),
                    EncounterMainFlowPolicy.Parallel, 0, 90)
            };
        }
    }
}
