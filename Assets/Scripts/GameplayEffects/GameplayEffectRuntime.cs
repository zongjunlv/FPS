using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace FPS.GameplayEffects
{
    public enum GameplayEffectExecutionFailure
    {
        None,
        InvalidDefinition,
        WrongDuration,
        TargetMismatch,
        UnsupportedAttribute,
        NoChange,
        CommitRejected
    }

    public readonly struct GameplayEffectExecutionResult
    {
        public GameplayEffectExecutionResult(
            bool succeeded,
            GameplayEffectExecutionFailure failure,
            float appliedAmount)
        {
            Succeeded = succeeded;
            Failure = failure;
            AppliedAmount = Mathf.Max(0f, appliedAmount);
        }

        public bool Succeeded { get; }
        public GameplayEffectExecutionFailure Failure { get; }
        public float AppliedAmount { get; }

        public static GameplayEffectExecutionResult Success(float amount) =>
            new(true, GameplayEffectExecutionFailure.None, amount);

        public static GameplayEffectExecutionResult Failed(
            GameplayEffectExecutionFailure failure) => new(false, failure, 0f);
    }

    public interface IGameplayEffectAttributeTarget
    {
        bool TryGetGameplayAttribute(
            GameplayAttributeId attribute,
            out float currentValue,
            out float minimumValue,
            out float maximumValue);

        bool TrySetGameplayAttribute(
            GameplayAttributeId attribute,
            float value,
            out float appliedAmount);
    }

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

            if (definition.DurationPolicy !=
                GameplayEffectDurationPolicy.Persistent)
            {
                throw new InvalidOperationException(
                    "Instant gameplay effects cannot become active instances.");
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

        public GameplayEffectExecutionResult PreviewInstant(
            GameplayEffectDefinition definition,
            GameplayEffectContext context,
            IGameplayEffectAttributeTarget attributeTarget)
        {
            return PrepareInstant(
                definition,
                context,
                attributeTarget,
                out _);
        }

        public GameplayEffectExecutionResult ExecuteInstant(
            GameplayEffectDefinition definition,
            GameplayEffectContext context,
            IGameplayEffectAttributeTarget attributeTarget)
        {
            GameplayEffectExecutionResult preview = PrepareInstant(
                definition,
                context,
                attributeTarget,
                out List<PendingAttributeChange> changes);

            if (!preview.Succeeded)
            {
                return preview;
            }

            var committed = new List<PendingAttributeChange>(changes.Count);
            float appliedAmount = 0f;

            for (int index = 0; index < changes.Count; index++)
            {
                PendingAttributeChange change = changes[index];

                if (!attributeTarget.TrySetGameplayAttribute(
                        change.Attribute,
                        change.NextValue,
                        out float applied))
                {
                    Rollback(attributeTarget, committed);
                    return GameplayEffectExecutionResult.Failed(
                        GameplayEffectExecutionFailure.CommitRejected);
                }

                committed.Add(change);
                appliedAmount += Mathf.Abs(applied);
            }

            return appliedAmount > Mathf.Epsilon
                ? GameplayEffectExecutionResult.Success(appliedAmount)
                : GameplayEffectExecutionResult.Failed(
                    GameplayEffectExecutionFailure.NoChange);
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

        private GameplayEffectExecutionResult PrepareInstant(
            GameplayEffectDefinition definition,
            GameplayEffectContext context,
            IGameplayEffectAttributeTarget attributeTarget,
            out List<PendingAttributeChange> changes)
        {
            changes = new List<PendingAttributeChange>();

            if (definition == null || attributeTarget == null)
            {
                return GameplayEffectExecutionResult.Failed(
                    GameplayEffectExecutionFailure.InvalidDefinition);
            }

            if (definition.DurationPolicy != GameplayEffectDurationPolicy.Instant)
            {
                return GameplayEffectExecutionResult.Failed(
                    GameplayEffectExecutionFailure.WrongDuration);
            }

            if (context.Target != target)
            {
                return GameplayEffectExecutionResult.Failed(
                    GameplayEffectExecutionFailure.TargetMismatch);
            }

            var values = new Dictionary<GameplayAttributeId, PendingAttributeChange>();
            IReadOnlyList<GameplayEffectModifier> modifiers = definition.Modifiers;

            for (int index = 0; index < modifiers.Count; index++)
            {
                GameplayEffectModifier modifier = modifiers[index];

                if (!values.TryGetValue(
                        modifier.Attribute,
                        out PendingAttributeChange change))
                {
                    if (!attributeTarget.TryGetGameplayAttribute(
                            modifier.Attribute,
                            out float current,
                            out float minimum,
                            out float maximum))
                    {
                        return GameplayEffectExecutionResult.Failed(
                            GameplayEffectExecutionFailure.UnsupportedAttribute);
                    }

                    change = new PendingAttributeChange(
                        modifier.Attribute,
                        current,
                        current,
                        minimum,
                        maximum);
                }

                float next = modifier.Operation switch
                {
                    GameplayModifierOperation.Add =>
                        change.NextValue + modifier.Magnitude,
                    GameplayModifierOperation.Multiply =>
                        change.NextValue * (1f + modifier.Magnitude),
                    GameplayModifierOperation.Override => modifier.Magnitude,
                    _ => change.NextValue
                };
                change = change.WithNextValue(Mathf.Clamp(
                    next,
                    change.MinimumValue,
                    change.MaximumValue));
                values[modifier.Attribute] = change;
            }

            float totalChange = 0f;

            foreach (PendingAttributeChange change in values.Values)
            {
                changes.Add(change);
                totalChange += Mathf.Abs(change.NextValue - change.OriginalValue);
            }

            changes.Sort((left, right) =>
                left.Attribute.CompareTo(right.Attribute));

            return totalChange > Mathf.Epsilon
                ? GameplayEffectExecutionResult.Success(totalChange)
                : GameplayEffectExecutionResult.Failed(
                    GameplayEffectExecutionFailure.NoChange);
        }

        private static void Rollback(
            IGameplayEffectAttributeTarget target,
            IReadOnlyList<PendingAttributeChange> committed)
        {
            for (int index = committed.Count - 1; index >= 0; index--)
            {
                PendingAttributeChange change = committed[index];
                target.TrySetGameplayAttribute(
                    change.Attribute,
                    change.OriginalValue,
                    out _);
            }
        }

        private readonly struct PendingAttributeChange
        {
            public PendingAttributeChange(
                GameplayAttributeId attribute,
                float originalValue,
                float nextValue,
                float minimumValue,
                float maximumValue)
            {
                Attribute = attribute;
                OriginalValue = originalValue;
                NextValue = nextValue;
                MinimumValue = minimumValue;
                MaximumValue = maximumValue;
            }

            public GameplayAttributeId Attribute { get; }
            public float OriginalValue { get; }
            public float NextValue { get; }
            public float MinimumValue { get; }
            public float MaximumValue { get; }

            public PendingAttributeChange WithNextValue(float value) => new(
                Attribute,
                OriginalValue,
                value,
                MinimumValue,
                MaximumValue);
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
