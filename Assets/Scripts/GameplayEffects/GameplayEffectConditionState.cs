using UnityEngine;

namespace FPS.GameplayEffects
{
    public enum GameplayEffectConditionTransition
    {
        None,
        Activated,
        Deactivated
    }

    public sealed class GameplayEffectConditionState
    {
        public GameplayEffectConditionState(float thresholdNormalized)
        {
            ThresholdNormalized = Mathf.Clamp01(thresholdNormalized);
        }

        public float ThresholdNormalized { get; }
        public bool IsActive { get; private set; }

        public GameplayEffectConditionTransition Evaluate(
            float currentValue,
            float maximumValue,
            bool isUnavailable = false)
        {
            float normalized = maximumValue > Mathf.Epsilon
                ? Mathf.Clamp01(currentValue / maximumValue)
                : 0f;
            bool shouldBeActive = !isUnavailable && currentValue > 0f &&
                                  normalized < ThresholdNormalized;

            if (shouldBeActive == IsActive)
            {
                return GameplayEffectConditionTransition.None;
            }

            IsActive = shouldBeActive;
            return IsActive
                ? GameplayEffectConditionTransition.Activated
                : GameplayEffectConditionTransition.Deactivated;
        }

        public GameplayEffectConditionTransition Reset()
        {
            if (!IsActive)
            {
                return GameplayEffectConditionTransition.None;
            }

            IsActive = false;
            return GameplayEffectConditionTransition.Deactivated;
        }
    }
}
