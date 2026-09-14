using System;
using UnityEngine;

namespace FPS.AI.Hybrid.Shared
{
    public enum HybridAiRangeBand
    {
        Near,
        Mid,
        Far
    }

    public enum HybridAiExecutionOwner
    {
        GameObject,
        Batch
    }

    public enum HybridAiHandoffSignal
    {
        None,
        ActivateGameObject,
        ActivateBatch
    }

    public readonly struct HybridAiExecutionState
    {
        public HybridAiExecutionState(
            HybridAiRangeBand rangeBand,
            HybridAiExecutionOwner owner,
            HybridAiHandoffSignal handoffSignal)
        {
            RangeBand = rangeBand;
            Owner = owner;
            HandoffSignal = handoffSignal;
        }

        public HybridAiRangeBand RangeBand { get; }
        public HybridAiExecutionOwner Owner { get; }
        public HybridAiHandoffSignal HandoffSignal { get; }
    }

    /// <summary>
    /// Immutable adapter boundary. Both GameObject and batch/ECS paths consume
    /// this exact snapshot; neither adapter is allowed to derive extra gameplay
    /// facts behind the shared decision layer.
    /// </summary>
    public readonly struct HybridAiWorldSnapshot
    {
        public HybridAiWorldSnapshot(
            string agentStableId,
            int generation,
            Vector3 position,
            Vector3 targetPosition,
            EnemyUtilityWorldFacts utilityFacts,
            HybridAiRangeBand rangeBand,
            bool isAlive)
        {
            AgentStableId = string.IsNullOrWhiteSpace(agentStableId)
                ? string.Empty
                : agentStableId.Trim();
            Generation = Mathf.Max(0, generation);
            Position = position;
            TargetPosition = targetPosition;
            UtilityFacts = utilityFacts;
            RangeBand = rangeBand;
            IsAlive = isAlive;
        }

        public string AgentStableId { get; }
        public int Generation { get; }
        public Vector3 Position { get; }
        public Vector3 TargetPosition { get; }
        public EnemyUtilityWorldFacts UtilityFacts { get; }
        public HybridAiRangeBand RangeBand { get; }
        public bool IsAlive { get; }
    }

    /// <summary>
    /// Presentation-independent result of one Utility AI decision. Navigation,
    /// animation and collision remain adapter responsibilities.
    /// </summary>
    public readonly struct HybridAiActionIntent
    {
        public HybridAiActionIntent(
            string agentStableId,
            int generation,
            string actionId,
            EnemyUtilityActionKind actionKind,
            Vector3 anchorPosition,
            float movementSpeedMultiplier,
            string decisionReason,
            bool changed,
            HybridAiRangeBand rangeBand,
            HybridAiExecutionOwner executionOwner,
            HybridAiHandoffSignal handoffSignal)
        {
            AgentStableId = agentStableId ?? string.Empty;
            Generation = Mathf.Max(0, generation);
            ActionId = actionId ?? string.Empty;
            ActionKind = actionKind;
            AnchorPosition = anchorPosition;
            MovementSpeedMultiplier = Mathf.Max(
                0.1f,
                movementSpeedMultiplier);
            DecisionReason = decisionReason ?? string.Empty;
            Changed = changed;
            RangeBand = rangeBand;
            ExecutionOwner = executionOwner;
            HandoffSignal = handoffSignal;
        }

        public string AgentStableId { get; }
        public int Generation { get; }
        public string ActionId { get; }
        public EnemyUtilityActionKind ActionKind { get; }
        public Vector3 AnchorPosition { get; }
        public float MovementSpeedMultiplier { get; }
        public string DecisionReason { get; }
        public bool Changed { get; }
        public HybridAiRangeBand RangeBand { get; }
        public HybridAiExecutionOwner ExecutionOwner { get; }
        public HybridAiHandoffSignal HandoffSignal { get; }
        public bool HasSelection => ActionKind != EnemyUtilityActionKind.None &&
            !string.IsNullOrWhiteSpace(ActionId);

        public static HybridAiActionIntent None(
            in HybridAiWorldSnapshot world,
            HybridAiExecutionState execution,
            string reason)
        {
            return new HybridAiActionIntent(
                world.AgentStableId,
                world.Generation,
                string.Empty,
                EnemyUtilityActionKind.None,
                world.TargetPosition,
                1f,
                reason,
                false,
                execution.RangeBand,
                execution.Owner,
                execution.HandoffSignal);
        }
    }

    public interface IHybridAiDecisionAdapter
    {
        HybridAiActionIntent Evaluate(
            in HybridAiWorldSnapshot world,
            HybridAiExecutionState execution,
            float deltaTime,
            long seed,
            Func<EnemyUtilityActionDefinition, bool> isExecutable = null);

        void CompleteActive();
        void ReportFailure(string actionId);
        void CancelActive();
        void Reset(EnemyUtilityProfileDefinition profile);
    }
}
