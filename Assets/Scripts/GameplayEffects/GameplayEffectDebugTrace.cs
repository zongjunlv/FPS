using System;
using System.Collections.Generic;
using UnityEngine;

namespace FPS.GameplayEffects
{
    public readonly struct GameplayModifierEvaluationStep
    {
        public GameplayModifierEvaluationStep(
            int sequence,
            long instanceId,
            string effectId,
            string sourceId,
            GameplayModifierOperation operation,
            float magnitude,
            int priority,
            float inputValue,
            float outputValue,
            bool applied)
        {
            Sequence = sequence;
            InstanceId = instanceId;
            EffectId = effectId ?? string.Empty;
            SourceId = sourceId ?? string.Empty;
            Operation = operation;
            Magnitude = magnitude;
            Priority = priority;
            InputValue = inputValue;
            OutputValue = outputValue;
            Applied = applied;
        }

        public int Sequence { get; }
        public long InstanceId { get; }
        public string EffectId { get; }
        public string SourceId { get; }
        public GameplayModifierOperation Operation { get; }
        public float Magnitude { get; }
        public int Priority { get; }
        public float InputValue { get; }
        public float OutputValue { get; }
        public bool Applied { get; }
    }

    public sealed class GameplayAttributeEvaluationTrace
    {
        public GameplayAttributeEvaluationTrace(
            GameplayAttributeId attribute,
            float baseValue,
            bool hasKnownBaseValue,
            float finalValue,
            IReadOnlyList<GameplayModifierEvaluationStep> steps)
        {
            Attribute = attribute;
            BaseValue = baseValue;
            HasKnownBaseValue = hasKnownBaseValue;
            FinalValue = finalValue;
            Steps = steps ?? Array.Empty<GameplayModifierEvaluationStep>();
        }

        public GameplayAttributeId Attribute { get; }
        public float BaseValue { get; }
        public bool HasKnownBaseValue { get; }
        public float FinalValue { get; }
        public IReadOnlyList<GameplayModifierEvaluationStep> Steps { get; }
    }

    public sealed class GameplayEffectDebugTargetSnapshot
    {
        internal GameplayEffectDebugTargetSnapshot(
            GameObject target,
            IReadOnlyList<GameplayEffectRuntime> runtimes)
        {
            Target = target;
            Runtimes = runtimes ?? Array.Empty<GameplayEffectRuntime>();
        }

        public GameObject Target { get; }
        public string DisplayName => Target != null
            ? Target.name
            : "<destroyed>";
        public IReadOnlyList<GameplayEffectRuntime> Runtimes { get; }
    }
}
