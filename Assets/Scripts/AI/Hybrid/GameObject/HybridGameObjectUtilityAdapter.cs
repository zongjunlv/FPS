using System;
using FPS.AI.Hybrid.Shared;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class HybridGameObjectUtilityAdapter : MonoBehaviour
{
    [SerializeField] private EnemyUtilityProfileDefinition utilityProfile;
    [SerializeField] private bool hybridBatchEnabled;
    [SerializeField, Min(0f)] private float nearMaximumDistance = 12f;
    [SerializeField, Min(0f)] private float midMaximumDistance = 28f;
    [SerializeField, Min(0f)] private float handoffHysteresis = 2f;

    private readonly EnemyUtilityWorldFactCollector factCollector = new();
    private readonly HybridAiSharedDecisionAdapter decisionAdapter = new();
    private readonly HybridAiHandoffStateMachine handoffState = new();
    private EnemyUtilityProfileDefinition configuredProfile;
    private bool preparedForPool;

    public HybridAiWorldSnapshot LastWorldSnapshot { get; private set; }
    public HybridAiActionIntent LastIntent { get; private set; }
    public EnemyUtilityDecisionResult LastDecision =>
        decisionAdapter.LastDecision;
    public HybridAiExecutionOwner ExecutionOwner => handoffState.Owner;
    public bool HybridBatchEnabled => hybridBatchEnabled;

    public HybridAiActionIntent Evaluate(
        EnemyController owner,
        Transform target,
        bool hasLineOfSight,
        IEnemyNeighborQuery neighborQuery,
        float deltaTime,
        long runSeed,
        Func<EnemyUtilityActionDefinition, bool> isExecutable = null)
    {
        EnsureProfileConfigured();
        preparedForPool = false;
        EnemyUtilityWorldFacts facts = factCollector.Capture(
            owner,
            target,
            hasLineOfSight,
            utilityProfile,
            neighborQuery);
        HybridAiExecutionPolicy policy = BuildPolicy();
        HybridAiExecutionState execution = handoffState.Evaluate(
            facts.TargetDistance,
            policy);
        WaveEnemyLifecycle lifecycle = owner != null
            ? owner.GetComponent<WaveEnemyLifecycle>()
            : null;
        int generation = lifecycle != null ? lifecycle.Generation : 0;
        string stableId = lifecycle != null && lifecycle.IsArmed
            ? $"enemy:{lifecycle.SpawnId}:{generation}"
            : owner != null
                ? $"go:{owner.gameObject.GetEntityId()}"
                : string.Empty;
        Health health = owner != null ? owner.GetComponent<Health>() : null;
        bool isAlive = owner != null && owner.isActiveAndEnabled &&
            (health == null || !health.IsDead);
        Vector3 ownerPosition = owner != null
            ? owner.transform.position
            : Vector3.zero;
        Vector3 targetPosition = target != null
            ? target.position
            : ownerPosition;
        LastWorldSnapshot = new HybridAiWorldSnapshot(
            stableId,
            generation,
            ownerPosition,
            targetPosition,
            facts,
            execution.RangeBand,
            isAlive);
        return EvaluatePreparedSnapshot(
            LastWorldSnapshot,
            execution,
            deltaTime,
            runSeed,
            isExecutable);
    }

    /// <summary>
    /// Adapter entry point used by A/B tests and by coordinators that already
    /// captured the immutable shared snapshot. It deliberately performs no
    /// extra gameplay-fact derivation.
    /// </summary>
    public HybridAiActionIntent Evaluate(
        in HybridAiWorldSnapshot world,
        float deltaTime,
        long runSeed,
        Func<EnemyUtilityActionDefinition, bool> isExecutable = null)
    {
        EnsureProfileConfigured();
        preparedForPool = false;
        HybridAiExecutionState execution = handoffState.Evaluate(
            world.UtilityFacts.TargetDistance,
            BuildPolicy());
        LastWorldSnapshot = new HybridAiWorldSnapshot(
            world.AgentStableId,
            world.Generation,
            world.Position,
            world.TargetPosition,
            world.UtilityFacts,
            execution.RangeBand,
            world.IsAlive);
        return EvaluatePreparedSnapshot(
            LastWorldSnapshot,
            execution,
            deltaTime,
            runSeed,
            isExecutable);
    }

    public void Configure(
        EnemyUtilityProfileDefinition profile,
        HybridAiExecutionPolicy policy)
    {
        utilityProfile = profile;
        hybridBatchEnabled = policy.BatchEnabled;
        nearMaximumDistance = policy.NearMaximumDistance;
        midMaximumDistance = policy.MidMaximumDistance;
        handoffHysteresis = policy.HandoffHysteresis;
        ResetForSpawn(profile);
    }

    public void ResetForSpawn(EnemyUtilityProfileDefinition profile = null)
    {
        if (profile != null)
        {
            utilityProfile = profile;
        }

        configuredProfile = utilityProfile;
        decisionAdapter.Reset(configuredProfile);
        handoffState.Reset(0f, BuildPolicy());
        LastWorldSnapshot = default;
        LastIntent = default;
        preparedForPool = false;
    }

    public void PrepareForPool()
    {
        if (preparedForPool)
        {
            return;
        }

        decisionAdapter.Reset(null);
        handoffState.Reset(0f, HybridAiExecutionPolicy.GameObjectOnly);
        configuredProfile = null;
        LastWorldSnapshot = default;
        LastIntent = default;
        preparedForPool = true;
    }

    public void CompleteActive()
    {
        decisionAdapter.CompleteActive();
    }

    public void ReportFailure(string actionId)
    {
        decisionAdapter.ReportFailure(actionId);
    }

    public void CancelActive()
    {
        decisionAdapter.CancelActive();
    }

    private void OnDisable()
    {
        PrepareForPool();
    }

    private HybridAiExecutionPolicy BuildPolicy()
    {
        return new HybridAiExecutionPolicy(
            hybridBatchEnabled,
            nearMaximumDistance,
            midMaximumDistance,
            handoffHysteresis);
    }

    private HybridAiActionIntent EvaluatePreparedSnapshot(
        in HybridAiWorldSnapshot world,
        HybridAiExecutionState execution,
        float deltaTime,
        long runSeed,
        Func<EnemyUtilityActionDefinition, bool> isExecutable)
    {
        LastIntent = decisionAdapter.Evaluate(
            world,
            execution,
            deltaTime,
            runSeed,
            isExecutable);
        return LastIntent;
    }

    private void EnsureProfileConfigured()
    {
        if (ReferenceEquals(configuredProfile, utilityProfile))
        {
            return;
        }

        configuredProfile = utilityProfile;
        decisionAdapter.Reset(configuredProfile);
    }
}
