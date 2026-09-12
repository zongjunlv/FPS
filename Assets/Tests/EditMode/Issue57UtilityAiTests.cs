using System.Linq;
using FPS.Determinism;
using NUnit.Framework;
using UnityEngine;

namespace FPS.Tests.Architecture
{
    public sealed class Issue57UtilityAiTests
    {
        private EnemyUtilityProfileDefinition profile;

        [TearDown]
        public void TearDown()
        {
            if (profile != null)
            {
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void ScoresAllConfiguredActionsWithoutChangingDecisionLoop()
        {
            profile = CreateProfile();
            var engine = new EnemyUtilityDecisionEngine();
            engine.Reset(profile);
            EnemyUtilityWorldFacts facts = Facts(12f, true, 0.9f);

            EnemyUtilityDecisionResult result = engine.Evaluate(
                facts,
                0f,
                5701L);

            Assert.That(result.Candidates.Count, Is.EqualTo(3));
            Assert.That(result.Candidates.Select(value => value.Action.StableId),
                Is.EquivalentTo(new[]
                {
                    "raider.chase",
                    "raider.flank",
                    "raider.retreat"
                }));
            Assert.That(result.SelectedAction.StableId,
                Is.EqualTo("raider.flank"));
        }

        [Test]
        public void LowHealthWithoutSupportSelectsRetreat()
        {
            profile = CreateProfile();
            var engine = new EnemyUtilityDecisionEngine();
            engine.Reset(profile);
            var facts = new EnemyUtilityWorldFacts(
                3f,
                true,
                false,
                0.15f,
                0,
                0,
                0,
                false);

            EnemyUtilityDecisionResult result = engine.Evaluate(
                facts,
                0f,
                5702L);

            Assert.That(result.SelectedAction.StableId,
                Is.EqualTo("raider.retreat"));
        }

        [Test]
        public void WorldFactsExposeEveryReadOnlyDecisionInput()
        {
            var facts = new EnemyUtilityWorldFacts(
                9.5f,
                true,
                false,
                0.4f,
                2,
                3,
                1,
                true,
                1.25f,
                0.75f);

            Assert.That(facts.Read(EnemyUtilityFactId.TargetDistance),
                Is.EqualTo(9.5f));
            Assert.That(facts.Read(EnemyUtilityFactId.HasLineOfSight),
                Is.EqualTo(1f));
            Assert.That(facts.Read(EnemyUtilityFactId.TargetInCover),
                Is.Zero);
            Assert.That(facts.Read(EnemyUtilityFactId.HealthRatio),
                Is.EqualTo(0.4f));
            Assert.That(facts.Read(EnemyUtilityFactId.FriendlyRaiderCount),
                Is.EqualTo(2f));
            Assert.That(facts.Read(
                    EnemyUtilityFactId.FriendlySuppressorCount),
                Is.EqualTo(3f));
            Assert.That(facts.Read(EnemyUtilityFactId.FriendlySupportCount),
                Is.EqualTo(1f));
            Assert.That(facts.Read(EnemyUtilityFactId.HasSupportCoverage),
                Is.EqualTo(1f));
            Assert.That(facts.Read(
                    EnemyUtilityFactId.ActionCooldownRemaining),
                Is.EqualTo(1.25f));
            Assert.That(facts.Read(
                    EnemyUtilityFactId.ActionCommitmentRemaining),
                Is.EqualTo(0.75f));
        }

        [Test]
        public void SameFactsAndSeedAlwaysChooseSameAction()
        {
            profile = CreateTieProfile();
            EnemyUtilityWorldFacts facts = Facts(8f, true, 1f);
            var first = new EnemyUtilityDecisionEngine();
            var second = new EnemyUtilityDecisionEngine();
            first.Reset(profile);
            second.Reset(profile);

            string firstChoice = first.Evaluate(facts, 0f, 5719L)
                .SelectedAction.StableId;
            string secondChoice = second.Evaluate(facts, 0f, 5719L)
                .SelectedAction.StableId;

            Assert.That(secondChoice, Is.EqualTo(firstChoice));
        }

        [Test]
        public void CommitmentAndHysteresisPreventDecisionThrashing()
        {
            profile = CreateProfile();
            var engine = new EnemyUtilityDecisionEngine();
            engine.Reset(profile);

            EnemyUtilityDecisionResult initial = engine.Evaluate(
                Facts(12f, true, 0.9f),
                0f,
                57L);
            EnemyUtilityDecisionResult committed = engine.Evaluate(
                Facts(4.2f, true, 0.9f),
                0.5f,
                57L);

            Assert.That(initial.SelectedAction.StableId,
                Is.EqualTo("raider.flank"));
            Assert.That(committed.SelectedAction.StableId,
                Is.EqualTo("raider.flank"));
            Assert.That(committed.ReasonCode, Is.EqualTo("commitment"));
        }

        [Test]
        public void FailedActionEntersCooldownAndFallsBackToExecutableAction()
        {
            profile = CreateProfile();
            var engine = new EnemyUtilityDecisionEngine();
            engine.Reset(profile);
            EnemyUtilityWorldFacts facts = Facts(12f, true, 0.9f);
            EnemyUtilityDecisionResult initial = engine.Evaluate(
                facts,
                0f,
                57L);

            engine.ReportFailure(initial.SelectedAction.StableId);
            EnemyUtilityDecisionResult fallback = engine.Evaluate(
                facts,
                0f,
                57L,
                action => action.Kind != EnemyUtilityActionKind.Retreat);

            Assert.That(fallback.SelectedAction.StableId,
                Is.EqualTo("raider.chase"));
            Assert.That(fallback.ReasonCode, Is.EqualTo("fallback"));
            Assert.That(engine.GetCooldownRemaining("raider.flank"),
                Is.GreaterThan(0f));
        }

        [Test]
        public void ResetClearsSelectionCommitmentAndCooldownsForPooling()
        {
            profile = CreateProfile();
            var engine = new EnemyUtilityDecisionEngine();
            engine.Reset(profile);
            EnemyUtilityDecisionResult selected = engine.Evaluate(
                Facts(12f, true, 0.9f),
                0f,
                57L);
            engine.ReportFailure(selected.SelectedAction.StableId);

            engine.Reset(profile);

            Assert.That(engine.SelectedActionId, Is.Empty);
            Assert.That(engine.CommitmentRemaining, Is.Zero);
            Assert.That(engine.GetCooldownRemaining("raider.flank"), Is.Zero);
        }

        [Test]
        public void RuntimeProfileStoresWeightsThresholdsCooldownsAndCommitments()
        {
            EnemyUtilityProfileDefinition runtimeProfile =
                Resources.Load<EnemyUtilityProfileDefinition>(
                    "Content/CityNew/Enemies/Utility/RaiderUtility");
            RaiderApproachAbilityDefinition raider =
                Resources.Load<RaiderApproachAbilityDefinition>(
                    "Content/CityNew/Enemies/Abilities/RaiderFlank");

            Assert.That(runtimeProfile, Is.Not.Null);
            Assert.That(runtimeProfile.Actions.Count, Is.EqualTo(3));
            Assert.That(runtimeProfile.Actions.All(action =>
                action.MinimumScore > 0f &&
                action.CommitmentDuration > 0f), Is.True);
            Assert.That(runtimeProfile.FindAction("raider.flank").Cooldown,
                Is.GreaterThan(0f));
            Assert.That(raider.UtilityProfile, Is.SameAs(runtimeProfile));
        }

        [Test]
        public void AiDecisionIsAStableReplayTimelineEvent()
        {
            var run = new DeterministicRun(57L);
            run.RecordEvent(RunEventType.AiDecision, StableEventPayload.Create(
                RunPayloadField.Text("action", "raider.flank"),
                RunPayloadField.Text("previous", "raider.chase"),
                RunPayloadField.Text("reason", "highest-score"),
                RunPayloadField.Number("score", 875),
                RunPayloadField.Number("spawnId", 7)));

            ReplayTimelineItem item = new ReplayTimeline(run.Record)
                .Query(
                    ReplayTimelineFilter.Only(
                        ReplayTimelineEventKind.AiDecision),
                    0,
                    20)
                .Items.Single();

            Assert.That(item.Title, Is.EqualTo("AI 决策"));
            Assert.That(item.EntityId, Is.EqualTo("7"));
            Assert.That(StableEventPayload.Parse(item.Payload)["action"],
                Is.EqualTo("raider.flank"));
        }

        private EnemyUtilityProfileDefinition CreateProfile()
        {
            profile = ScriptableObject.CreateInstance<
                EnemyUtilityProfileDefinition>();
            profile.Configure(
                "test.utility",
                "raider.chase",
                14f,
                new[]
                {
                    Action(
                        "raider.chase",
                        EnemyUtilityActionKind.Chase,
                        0.6f,
                        0.5f,
                        new EnemyUtilityConsiderationDefinition(
                            EnemyUtilityFactId.TargetDistance,
                            EnemyUtilityResponseCurve.Rising,
                            1f,
                            10f,
                            1f,
                            0.5f)),
                    Action(
                        "raider.flank",
                        EnemyUtilityActionKind.Flank,
                        1f,
                        2.5f,
                        new EnemyUtilityConsiderationDefinition(
                            EnemyUtilityFactId.HasLineOfSight,
                            EnemyUtilityResponseCurve.BooleanTrue),
                        new EnemyUtilityConsiderationDefinition(
                            EnemyUtilityFactId.TargetDistance,
                            EnemyUtilityResponseCurve.Rising,
                            3f,
                            12f)),
                    Action(
                        "raider.retreat",
                        EnemyUtilityActionKind.Retreat,
                        1.2f,
                        1.5f,
                        new EnemyUtilityConsiderationDefinition(
                            EnemyUtilityFactId.HealthRatio,
                            EnemyUtilityResponseCurve.Falling,
                            0.2f,
                            0.6f))
                });
            return profile;
        }

        private EnemyUtilityProfileDefinition CreateTieProfile()
        {
            profile = ScriptableObject.CreateInstance<
                EnemyUtilityProfileDefinition>();
            profile.Configure(
                "test.tie",
                "alpha",
                10f,
                new[]
                {
                    Action("alpha", EnemyUtilityActionKind.Chase, 1f, 0f),
                    Action("beta", EnemyUtilityActionKind.Flank, 1f, 0f)
                });
            return profile;
        }

        private static EnemyUtilityActionDefinition Action(
            string id,
            EnemyUtilityActionKind kind,
            float weight,
            float commitment,
            params EnemyUtilityConsiderationDefinition[] considerations)
        {
            return new EnemyUtilityActionDefinition(
                id,
                id,
                kind,
                weight,
                0.01f,
                kind == EnemyUtilityActionKind.Flank ? 3f : 0f,
                commitment,
                0.1f,
                6f,
                1f,
                "raider.chase",
                considerations);
        }

        private static EnemyUtilityWorldFacts Facts(
            float distance,
            bool lineOfSight,
            float health)
        {
            return new EnemyUtilityWorldFacts(
                distance,
                lineOfSight,
                !lineOfSight,
                health,
                1,
                1,
                1,
                false);
        }
    }
}
