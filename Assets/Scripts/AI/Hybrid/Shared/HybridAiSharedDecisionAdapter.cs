using System;

namespace FPS.AI.Hybrid.Shared
{
    /// <summary>
    /// The one shared gameplay-rule adapter used by GO and ECS orchestration.
    /// It delegates scoring, cooldown, commitment, hysteresis and fallback to
    /// the existing Issue 57 EnemyUtilityDecisionEngine.
    /// </summary>
    public sealed class HybridAiSharedDecisionAdapter :
        IHybridAiDecisionAdapter
    {
        private readonly EnemyUtilityDecisionEngine engine = new();

        public EnemyUtilityDecisionResult LastDecision { get; private set; }

        public HybridAiActionIntent Evaluate(
            in HybridAiWorldSnapshot world,
            HybridAiExecutionState execution,
            float deltaTime,
            long seed,
            Func<EnemyUtilityActionDefinition, bool> isExecutable = null)
        {
            if (!world.IsAlive)
            {
                engine.CancelActive();
                LastDecision = null;
                return HybridAiActionIntent.None(
                    world,
                    execution,
                    "agent-inactive");
            }

            LastDecision = engine.Evaluate(
                world.UtilityFacts,
                deltaTime,
                seed,
                isExecutable);
            EnemyUtilityActionDefinition action =
                LastDecision.SelectedAction;

            if (action == null)
            {
                return HybridAiActionIntent.None(
                    world,
                    execution,
                    LastDecision.ReasonCode);
            }

            return new HybridAiActionIntent(
                world.AgentStableId,
                world.Generation,
                action.StableId,
                action.Kind,
                world.TargetPosition,
                action.MovementSpeedMultiplier,
                LastDecision.ReasonCode,
                LastDecision.Changed,
                execution.RangeBand,
                execution.Owner,
                execution.HandoffSignal);
        }

        public void CompleteActive()
        {
            engine.CompleteActive();
        }

        public void ReportFailure(string actionId)
        {
            engine.ReportFailure(actionId);
        }

        public void CancelActive()
        {
            engine.CancelActive();
            LastDecision = null;
        }

        public void Reset(EnemyUtilityProfileDefinition profile)
        {
            engine.Reset(profile);
            LastDecision = null;
        }
    }
}
