using UnityEngine;

namespace FPS.AI.Hybrid.Shared
{
    /// <summary>
    /// Defines the distance bands and their ownership. GameObject-only is the
    /// safe default, so removing or disabling the experiment changes no gameplay.
    /// </summary>
    public readonly struct HybridAiExecutionPolicy
    {
        public HybridAiExecutionPolicy(
            bool batchEnabled,
            float nearMaximumDistance,
            float midMaximumDistance,
            float handoffHysteresis)
        {
            BatchEnabled = batchEnabled;
            NearMaximumDistance = Mathf.Max(0f, nearMaximumDistance);
            MidMaximumDistance = Mathf.Max(
                NearMaximumDistance,
                midMaximumDistance);
            HandoffHysteresis = Mathf.Max(0f, handoffHysteresis);
        }

        public bool BatchEnabled { get; }
        public float NearMaximumDistance { get; }
        public float MidMaximumDistance { get; }
        public float HandoffHysteresis { get; }

        public static HybridAiExecutionPolicy GameObjectOnly =>
            new(false, 12f, 28f, 2f);

        public static HybridAiExecutionPolicy HybridDefault =>
            new(true, 12f, 28f, 2f);

        public HybridAiRangeBand Classify(float distance)
        {
            float safeDistance = Mathf.Max(0f, distance);

            if (safeDistance <= NearMaximumDistance)
            {
                return HybridAiRangeBand.Near;
            }

            return safeDistance <= MidMaximumDistance
                ? HybridAiRangeBand.Mid
                : HybridAiRangeBand.Far;
        }
    }

    /// <summary>
    /// Pure ownership state machine shared by both adapters. Hysteresis prevents
    /// an agent on a range boundary from creating duplicate handoffs every tick.
    /// </summary>
    public sealed class HybridAiHandoffStateMachine
    {
        private HybridAiExecutionOwner owner =
            HybridAiExecutionOwner.GameObject;

        public HybridAiExecutionOwner Owner => owner;

        public HybridAiExecutionState Evaluate(
            float distance,
            HybridAiExecutionPolicy policy)
        {
            HybridAiRangeBand band = policy.Classify(distance);

            if (!policy.BatchEnabled)
            {
                HybridAiHandoffSignal fallbackSignal = owner ==
                    HybridAiExecutionOwner.GameObject
                    ? HybridAiHandoffSignal.None
                    : HybridAiHandoffSignal.ActivateGameObject;
                owner = HybridAiExecutionOwner.GameObject;
                return new HybridAiExecutionState(
                    band,
                    owner,
                    fallbackSignal);
            }

            HybridAiHandoffSignal signal = HybridAiHandoffSignal.None;
            float safeDistance = Mathf.Max(0f, distance);
            float batchThreshold = policy.NearMaximumDistance +
                policy.HandoffHysteresis;

            if (owner == HybridAiExecutionOwner.GameObject &&
                safeDistance > batchThreshold)
            {
                owner = HybridAiExecutionOwner.Batch;
                signal = HybridAiHandoffSignal.ActivateBatch;
            }
            else if (owner == HybridAiExecutionOwner.Batch &&
                     safeDistance <= policy.NearMaximumDistance)
            {
                owner = HybridAiExecutionOwner.GameObject;
                signal = HybridAiHandoffSignal.ActivateGameObject;
            }

            return new HybridAiExecutionState(band, owner, signal);
        }

        public HybridAiExecutionState Reset(
            float distance,
            HybridAiExecutionPolicy policy)
        {
            owner = HybridAiExecutionOwner.GameObject;
            return new HybridAiExecutionState(
                policy.Classify(distance),
                owner,
                HybridAiHandoffSignal.None);
        }
    }
}
