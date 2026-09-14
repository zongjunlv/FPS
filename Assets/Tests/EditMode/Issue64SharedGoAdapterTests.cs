using FPS.AI.Hybrid.Shared;
using FPS.Performance.HybridAi;
using NUnit.Framework;
using UnityEngine;

public sealed class Issue64SharedGoAdapterTests
{
    private EnemyUtilityProfileDefinition profile;
    private GameObject host;

    [TearDown]
    public void TearDown()
    {
        if (host != null)
        {
            Object.DestroyImmediate(host);
        }

        if (profile != null)
        {
            Object.DestroyImmediate(profile);
        }
    }

    [Test]
    public void SharedAdapterDelegatesSelectionToIssue57DecisionEngine()
    {
        profile = CreateProfile();
        EnemyUtilityWorldFacts facts = Facts(18f, true, 0.9f);
        var directEngine = new EnemyUtilityDecisionEngine();
        directEngine.Reset(profile);
        EnemyUtilityDecisionResult direct = directEngine.Evaluate(
            facts,
            0f,
            6401L);
        var shared = new HybridAiSharedDecisionAdapter();
        shared.Reset(profile);
        HybridAiWorldSnapshot world = World(facts);

        HybridAiActionIntent intent = shared.Evaluate(
            world,
            new HybridAiExecutionState(
                HybridAiRangeBand.Mid,
                HybridAiExecutionOwner.Batch,
                HybridAiHandoffSignal.None),
            0f,
            6401L);

        Assert.That(intent.ActionId,
            Is.EqualTo(direct.SelectedAction.StableId));
        Assert.That(intent.ActionKind,
            Is.EqualTo(direct.SelectedAction.Kind));
        Assert.That(intent.DecisionReason,
            Is.EqualTo(direct.ReasonCode));
        Assert.That(shared.LastDecision.Candidates.Count,
            Is.EqualTo(direct.Candidates.Count));
    }

    [Test]
    public void SharedSnapshotProducesSameIntentThroughGoAndSharedAdapters()
    {
        profile = CreateProfile();
        host = new GameObject("Issue64 GO Adapter");
        HybridGameObjectUtilityAdapter gameObjectAdapter =
            host.AddComponent<HybridGameObjectUtilityAdapter>();
        gameObjectAdapter.Configure(
            profile,
            HybridAiExecutionPolicy.GameObjectOnly);
        var sharedAdapter = new HybridAiSharedDecisionAdapter();
        sharedAdapter.Reset(profile);
        HybridAiWorldSnapshot world = World(Facts(18f, true, 0.9f));

        HybridAiActionIntent goIntent = gameObjectAdapter.Evaluate(
            world,
            0f,
            6402L);
        HybridAiActionIntent sharedIntent = sharedAdapter.Evaluate(
            world,
            new HybridAiExecutionState(
                HybridAiRangeBand.Mid,
                HybridAiExecutionOwner.GameObject,
                HybridAiHandoffSignal.None),
            0f,
            6402L);

        Assert.That(goIntent.ActionId, Is.EqualTo(sharedIntent.ActionId));
        Assert.That(goIntent.ActionKind, Is.EqualTo(sharedIntent.ActionKind));
        Assert.That(goIntent.AnchorPosition,
            Is.EqualTo(sharedIntent.AnchorPosition));
        Assert.That(goIntent.MovementSpeedMultiplier,
            Is.EqualTo(sharedIntent.MovementSpeedMultiplier));
        Assert.That(goIntent.DecisionReason,
            Is.EqualTo(sharedIntent.DecisionReason));
    }

    [Test]
    public void DefaultPolicyNeverHandsOwnershipAwayFromGameObject()
    {
        var stateMachine = new HybridAiHandoffStateMachine();

        HybridAiExecutionState far = stateMachine.Evaluate(
            200f,
            HybridAiExecutionPolicy.GameObjectOnly);

        Assert.That(far.RangeBand, Is.EqualTo(HybridAiRangeBand.Far));
        Assert.That(far.Owner,
            Is.EqualTo(HybridAiExecutionOwner.GameObject));
        Assert.That(far.HandoffSignal,
            Is.EqualTo(HybridAiHandoffSignal.None));
    }

    [Test]
    public void HybridPolicyHandsMidAndFarToBatchAndNearBackToGameObject()
    {
        var stateMachine = new HybridAiHandoffStateMachine();
        HybridAiExecutionPolicy policy =
            HybridAiExecutionPolicy.HybridDefault;

        HybridAiExecutionState far = stateMachine.Evaluate(40f, policy);
        HybridAiExecutionState midBoundary = stateMachine.Evaluate(
            13f,
            policy);
        HybridAiExecutionState near = stateMachine.Evaluate(12f, policy);

        Assert.That(far.RangeBand, Is.EqualTo(HybridAiRangeBand.Far));
        Assert.That(far.Owner, Is.EqualTo(HybridAiExecutionOwner.Batch));
        Assert.That(far.HandoffSignal,
            Is.EqualTo(HybridAiHandoffSignal.ActivateBatch));
        Assert.That(midBoundary.RangeBand,
            Is.EqualTo(HybridAiRangeBand.Mid));
        Assert.That(midBoundary.Owner,
            Is.EqualTo(HybridAiExecutionOwner.Batch),
            "滞回区间内不应反复交接");
        Assert.That(midBoundary.HandoffSignal,
            Is.EqualTo(HybridAiHandoffSignal.None));
        Assert.That(near.RangeBand, Is.EqualTo(HybridAiRangeBand.Near));
        Assert.That(near.Owner,
            Is.EqualTo(HybridAiExecutionOwner.GameObject));
        Assert.That(near.HandoffSignal,
            Is.EqualTo(HybridAiHandoffSignal.ActivateGameObject));
    }

    [Test]
    public void GoAdapterPoolResetClearsDecisionAndRestoresSafeOwnership()
    {
        profile = CreateProfile();
        host = new GameObject("Issue64 Pool Reset");
        HybridGameObjectUtilityAdapter adapter =
            host.AddComponent<HybridGameObjectUtilityAdapter>();
        adapter.Configure(profile, HybridAiExecutionPolicy.HybridDefault);

        HybridAiActionIntent beforeReset = adapter.Evaluate(
            World(Facts(40f, true, 0.9f)),
            0f,
            6403L);
        adapter.PrepareForPool();

        Assert.That(beforeReset.ExecutionOwner,
            Is.EqualTo(HybridAiExecutionOwner.Batch));
        Assert.That(adapter.ExecutionOwner,
            Is.EqualTo(HybridAiExecutionOwner.GameObject));
        Assert.That(adapter.LastDecision, Is.Null);
        Assert.That(adapter.LastIntent.HasSelection, Is.False);
        Assert.That(adapter.LastWorldSnapshot.AgentStableId,
            Is.Null.Or.Empty);
    }

    [Test]
    public void InactiveAgentCannotLeaveAnActionIntentBehind()
    {
        profile = CreateProfile();
        var shared = new HybridAiSharedDecisionAdapter();
        shared.Reset(profile);
        HybridAiWorldSnapshot active = World(Facts(18f, true, 1f));
        HybridAiWorldSnapshot inactive = new(
            active.AgentStableId,
            active.Generation,
            active.Position,
            active.TargetPosition,
            active.UtilityFacts,
            active.RangeBand,
            false);
        HybridAiExecutionState execution = new(
            HybridAiRangeBand.Mid,
            HybridAiExecutionOwner.Batch,
            HybridAiHandoffSignal.None);

        Assert.That(shared.Evaluate(active, execution, 0f, 6404L)
            .HasSelection, Is.True);
        HybridAiActionIntent stopped = shared.Evaluate(
            inactive,
            execution,
            0.1f,
            6404L);

        Assert.That(stopped.HasSelection, Is.False);
        Assert.That(stopped.ActionKind,
            Is.EqualTo(EnemyUtilityActionKind.None));
        Assert.That(stopped.DecisionReason,
            Is.EqualTo("agent-inactive"));
        Assert.That(shared.LastDecision, Is.Null);
    }

    [Test]
    public void BenchmarkScenarioIsDeterministicAndRoleStriped()
    {
        HybridAiBenchmarkAgentState[] first =
            HybridAiBenchmarkScenarioKernel.CreateAgents(6, 6410);
        HybridAiBenchmarkAgentState[] second =
            HybridAiBenchmarkScenarioKernel.CreateAgents(6, 6410);

        Assert.That(first[0].StableId,
            Is.EqualTo("benchmark.enemy.0000"));
        Assert.That(first[0].Generation, Is.EqualTo(1));
        Assert.That(first[0].Role,
            Is.EqualTo(HybridAiBenchmarkRole.Raider));
        Assert.That(first[1].Role,
            Is.EqualTo(HybridAiBenchmarkRole.Suppressor));
        Assert.That(first[2].Role,
            Is.EqualTo(HybridAiBenchmarkRole.Support));
        Assert.That(first[0].Position.magnitude,
            Is.EqualTo(14f).Within(0.001f));
        Assert.That(second[5].Position, Is.EqualTo(first[5].Position));
        Assert.That(second[5].HealthRatio,
            Is.EqualTo(first[5].HealthRatio));
        Assert.That(
            HybridAiBenchmarkScenarioKernel.SamplePerception(3, 6410, 8)
                .TargetPosition,
            Is.EqualTo(
                HybridAiBenchmarkScenarioKernel.SamplePerception(
                    3,
                    6410,
                    8).TargetPosition));
    }

    [Test]
    public void IntentDigestIsIndependentOfAdapterIterationOrder()
    {
        HybridAiWorldSnapshot firstWorld = World(Facts(18f, true, 1f));
        HybridAiWorldSnapshot secondWorld = new(
            "benchmark.enemy.0002",
            1,
            Vector3.right,
            Vector3.zero,
            Facts(8f, true, 1f),
            HybridAiRangeBand.Near,
            true);
        HybridAiActionIntent first = new(
            firstWorld.AgentStableId,
            firstWorld.Generation,
            "raider.flank",
            EnemyUtilityActionKind.Flank,
            firstWorld.TargetPosition,
            1.2f,
            "ignored-reason",
            true,
            HybridAiRangeBand.Mid,
            HybridAiExecutionOwner.Batch,
            HybridAiHandoffSignal.ActivateBatch);
        HybridAiActionIntent second = new(
            secondWorld.AgentStableId,
            secondWorld.Generation,
            "raider.chase",
            EnemyUtilityActionKind.Chase,
            secondWorld.TargetPosition,
            1f,
            "another-ignored-reason",
            false,
            HybridAiRangeBand.Near,
            HybridAiExecutionOwner.GameObject,
            HybridAiHandoffSignal.None);

        string forward = HybridAiIntentDigestKernel.Compute(
            new[] { first, second });
        string reverse = HybridAiIntentDigestKernel.Compute(
            new[] { second, first });

        Assert.That(reverse, Is.EqualTo(forward));
        Assert.That(forward, Has.Length.EqualTo(64));
    }

    [Test]
    public void GoBenchmarkConcreteUsesSharedScenarioAndProducesMetrics()
    {
        using var adapter = new Issue64GameObjectBenchmarkAdapter();

        Assert.That(adapter.IsAvailable, Is.True);
        adapter.Configure(6, 6411);
        adapter.Step(1f / 60f);
        Issue64AdapterMetricsSnapshot metrics = adapter.CaptureMetrics();

        Assert.That(metrics.ActiveCount, Is.EqualTo(6));
        Assert.That(metrics.GhostCount, Is.Zero);
        Assert.That(metrics.IntentDigest, Has.Length.EqualTo(64));
        Assert.That(metrics.MeanDecisionLatencyMilliseconds,
            Is.GreaterThanOrEqualTo(0d));
        Assert.That(metrics.MemoryBytes, Is.GreaterThan(0L));
    }

    private EnemyUtilityProfileDefinition CreateProfile()
    {
        EnemyUtilityProfileDefinition created = ScriptableObject
            .CreateInstance<EnemyUtilityProfileDefinition>();
        created.Configure(
            "issue64.shared",
            "raider.chase",
            30f,
            new[]
            {
                new EnemyUtilityActionDefinition(
                    "raider.chase",
                    "追击",
                    EnemyUtilityActionKind.Chase,
                    0.55f,
                    0.05f,
                    0.2f,
                    0.2f,
                    0.05f,
                    0f,
                    1f,
                    "raider.chase",
                    new[]
                    {
                        new EnemyUtilityConsiderationDefinition(
                            EnemyUtilityFactId.TargetDistance,
                            EnemyUtilityResponseCurve.Falling,
                            0f,
                            30f,
                            1f,
                            0.35f)
                    }),
                new EnemyUtilityActionDefinition(
                    "raider.flank",
                    "侧翼",
                    EnemyUtilityActionKind.Flank,
                    1f,
                    0.05f,
                    1.5f,
                    0.5f,
                    0.1f,
                    8f,
                    1.35f,
                    "raider.chase",
                    new[]
                    {
                        new EnemyUtilityConsiderationDefinition(
                            EnemyUtilityFactId.HasLineOfSight,
                            EnemyUtilityResponseCurve.BooleanTrue),
                        new EnemyUtilityConsiderationDefinition(
                            EnemyUtilityFactId.TargetDistance,
                            EnemyUtilityResponseCurve.Rising,
                            4f,
                            24f)
                    }),
                new EnemyUtilityActionDefinition(
                    "raider.retreat",
                    "撤退",
                    EnemyUtilityActionKind.Retreat,
                    1.2f,
                    0.05f,
                    2f,
                    0.4f,
                    0.1f,
                    6f,
                    1.1f,
                    "raider.chase",
                    new[]
                    {
                        new EnemyUtilityConsiderationDefinition(
                            EnemyUtilityFactId.HealthRatio,
                            EnemyUtilityResponseCurve.Falling,
                            0.2f,
                            0.6f)
                    })
            });
        return created;
    }

    private static EnemyUtilityWorldFacts Facts(
        float distance,
        bool lineOfSight,
        float healthRatio)
    {
        return new EnemyUtilityWorldFacts(
            distance,
            lineOfSight,
            !lineOfSight,
            healthRatio,
            1,
            1,
            1,
            true);
    }

    private static HybridAiWorldSnapshot World(
        EnemyUtilityWorldFacts facts)
    {
        return new HybridAiWorldSnapshot(
            "enemy:64:2",
            2,
            new Vector3(1f, 0f, 2f),
            new Vector3(16f, 0f, 2f),
            facts,
            HybridAiExecutionPolicy.HybridDefault.Classify(
                facts.TargetDistance),
            true);
    }
}
