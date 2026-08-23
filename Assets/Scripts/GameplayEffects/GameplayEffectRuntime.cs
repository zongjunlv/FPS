using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace FPS.GameplayEffects
{
    public readonly struct GameplayEffectContext
    {
        public GameplayEffectContext(
            string sourceId,
            UnityEngine.Object source,
            UnityEngine.Object target)
        {
            SourceId = sourceId ?? string.Empty;
            Source = source;
            Target = target;
        }

        public string SourceId { get; }
        public UnityEngine.Object Source { get; }
        public UnityEngine.Object Target { get; }
    }

    public sealed class GameplayEffectInstance
    {
        internal GameplayEffectInstance(
            long instanceId,
            GameplayEffectDefinition definition,
            GameplayEffectContext context)
        {
            InstanceId = instanceId;
            Definition = definition;
            Context = context;
        }

        public long InstanceId { get; }
        public GameplayEffectDefinition Definition { get; }
        public GameplayEffectContext Context { get; }
    }

    public sealed class GameplayEffectRuntime
    {
        private static long nextInstanceId;
        private readonly List<GameplayEffectInstance> active = new();
        private readonly UnityEngine.Object target;

        public GameplayEffectRuntime(UnityEngine.Object effectTarget)
        {
            target = effectTarget != null
                ? effectTarget
                : throw new ArgumentNullException(nameof(effectTarget));
        }

        public IReadOnlyList<GameplayEffectInstance> ActiveInstances => active;

        public GameplayEffectInstance Apply(
            GameplayEffectDefinition definition,
            GameplayEffectContext context)
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            if (context.Target != target)
            {
                throw new InvalidOperationException(
                    "Gameplay effect context target does not match runtime target.");
            }

            long id = Interlocked.Increment(ref nextInstanceId);
            var instance = new GameplayEffectInstance(id, definition, context);
            active.Add(instance);
            return instance;
        }

        public bool Remove(long instanceId)
        {
            for (int index = 0; index < active.Count; index++)
            {
                if (active[index].InstanceId != instanceId)
                {
                    continue;
                }

                active.RemoveAt(index);
                return true;
            }

            return false;
        }

        public void Clear()
        {
            active.Clear();
        }

        public float Evaluate(
            GameplayAttributeId attribute,
            float baseValue)
        {
            return GameplayAttributeAggregator.Evaluate(
                attribute,
                baseValue,
                active);
        }
    }

    public static class GameplayAttributeAggregator
    {
        public static float Evaluate(
            GameplayAttributeId attribute,
            float baseValue,
            IReadOnlyList<GameplayEffectInstance> instances)
        {
            float additive = 0f;
            float multiplicativeBonus = 0f;
            GameplayEffectInstance selectedOverride = null;
            GameplayEffectModifier selectedOverrideModifier = default;

            if (instances != null)
            {
                for (int instanceIndex = 0;
                     instanceIndex < instances.Count;
                     instanceIndex++)
                {
                    GameplayEffectInstance instance = instances[instanceIndex];

                    if (instance?.Definition == null)
                    {
                        continue;
                    }

                    IReadOnlyList<GameplayEffectModifier> modifiers =
                        instance.Definition.Modifiers;

                    for (int modifierIndex = 0;
                         modifierIndex < modifiers.Count;
                         modifierIndex++)
                    {
                        GameplayEffectModifier modifier = modifiers[modifierIndex];

                        if (modifier.Attribute != attribute ||
                            !IsFinite(modifier.Magnitude))
                        {
                            continue;
                        }

                        switch (modifier.Operation)
                        {
                            case GameplayModifierOperation.Add:
                                additive += modifier.Magnitude;
                                break;
                            case GameplayModifierOperation.Multiply:
                                multiplicativeBonus += modifier.Magnitude;
                                break;
                            case GameplayModifierOperation.Override:
                                if (IsPreferredOverride(
                                        instance,
                                        modifier,
                                        selectedOverride,
                                        selectedOverrideModifier))
                                {
                                    selectedOverride = instance;
                                    selectedOverrideModifier = modifier;
                                }
                                break;
                        }
                    }
                }
            }

            if (selectedOverride != null)
            {
                return selectedOverrideModifier.Magnitude;
            }

            return (baseValue + additive) *
                   Mathf.Max(0f, 1f + multiplicativeBonus);
        }

        private static bool IsPreferredOverride(
            GameplayEffectInstance candidate,
            GameplayEffectModifier candidateModifier,
            GameplayEffectInstance selected,
            GameplayEffectModifier selectedModifier)
        {
            if (selected == null ||
                candidateModifier.Priority != selectedModifier.Priority)
            {
                return selected == null ||
                       candidateModifier.Priority > selectedModifier.Priority;
            }

            int definitionOrder = string.CompareOrdinal(
                candidate.Definition.StableId,
                selected.Definition.StableId);
            return definitionOrder > 0 ||
                   (definitionOrder == 0 &&
                    candidate.InstanceId > selected.InstanceId);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
