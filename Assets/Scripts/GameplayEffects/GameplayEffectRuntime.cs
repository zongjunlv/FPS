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

    public readonly struct GameplayEffectApplicationResult
    {
        public GameplayEffectApplicationResult(
            GameplayEffectInstance instance,
            bool stackAdded,
            bool durationRefreshed)
        {
            Instance = instance;
            StackAdded = stackAdded;
            DurationRefreshed = durationRefreshed;
        }

        public GameplayEffectInstance Instance { get; }
        public bool Succeeded => Instance != null;
        public bool StackAdded { get; }
        public bool DurationRefreshed { get; }
        public int StackCount => Instance?.StackCount ?? 0;
    }

    public readonly struct GameplayEffectTick
    {
        internal GameplayEffectTick(
            GameplayEffectInstance instance,
            GameplayEffectContext context)
        {
            Instance = instance;
            Context = context;
        }

        public GameplayEffectInstance Instance { get; }
        public GameplayEffectDefinition Definition => Instance?.Definition;
        public GameplayEffectContext Context { get; }
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

    public sealed class GameplayEffectContextSnapshot
    {
        public GameplayEffectContextSnapshot(
            string sourceId,
            string sourceKey)
        {
            SourceId = sourceId ?? string.Empty;
            SourceKey = sourceKey ?? string.Empty;
        }

        public string SourceId { get; }
        public string SourceKey { get; }
    }

    public sealed class GameplayEffectTimedStackSnapshot
    {
        public GameplayEffectTimedStackSnapshot(
            GameplayEffectContextSnapshot context,
            float remainingDuration,
            long order)
        {
            Context = context;
            RemainingDuration = remainingDuration;
            Order = order;
        }

        public GameplayEffectContextSnapshot Context { get; }
        public float RemainingDuration { get; }
        public long Order { get; }
    }

    public sealed class GameplayEffectInstanceSnapshot
    {
        private readonly IReadOnlyList<GameplayEffectTimedStackSnapshot>
            timedStacks;

        public GameplayEffectInstanceSnapshot(
            string definitionId,
            GameplayEffectContextSnapshot context,
            float tickRemaining,
            IReadOnlyList<GameplayEffectTimedStackSnapshot> timedStacks)
        {
            DefinitionId = definitionId;
            Context = context;
            TickRemaining = tickRemaining;
            this.timedStacks = Copy(timedStacks);
        }

        public string DefinitionId { get; }
        public GameplayEffectContextSnapshot Context { get; }
        public float TickRemaining { get; }
        public IReadOnlyList<GameplayEffectTimedStackSnapshot> TimedStacks =>
            timedStacks;

        private static IReadOnlyList<GameplayEffectTimedStackSnapshot> Copy(
            IReadOnlyList<GameplayEffectTimedStackSnapshot> source)
        {
            if (source == null || source.Count == 0)
            {
                return Array.Empty<GameplayEffectTimedStackSnapshot>();
            }

            var copy = new GameplayEffectTimedStackSnapshot[source.Count];

            for (int index = 0; index < source.Count; index++)
            {
                copy[index] = source[index];
            }

            return Array.AsReadOnly(copy);
        }
    }

    public sealed class GameplayEffectRuntimeSnapshot
    {
        private readonly IReadOnlyList<GameplayEffectInstanceSnapshot>
            instances;

        public GameplayEffectRuntimeSnapshot(
            IReadOnlyList<GameplayEffectInstanceSnapshot> instances)
        {
            if (instances == null || instances.Count == 0)
            {
                this.instances = Array.Empty<GameplayEffectInstanceSnapshot>();
                return;
            }

            var copy = new GameplayEffectInstanceSnapshot[instances.Count];

            for (int index = 0; index < instances.Count; index++)
            {
                copy[index] = instances[index];
            }

            this.instances = Array.AsReadOnly(copy);
        }

        public IReadOnlyList<GameplayEffectInstanceSnapshot> Instances =>
            instances;
    }

    public sealed class GameplayEffectInstance
    {
        private sealed class TimedStack
        {
            public TimedStack(
                GameplayEffectContext context,
                float remainingDuration,
                long order)
            {
                Context = context;
                RemainingDuration = remainingDuration;
                Order = order;
            }

            public GameplayEffectContext Context;
            public float RemainingDuration;
            public long Order;
        }

        private readonly List<TimedStack> timedStacks = new();

        internal GameplayEffectInstance(
            long instanceId,
            GameplayEffectDefinition definition,
            GameplayEffectContext context)
        {
            InstanceId = instanceId;
            Definition = definition;
            Context = context;
            TickRemaining = definition != null
                ? definition.TickInterval
                : 0f;
        }

        public long InstanceId { get; }
        public GameplayEffectDefinition Definition { get; }
        public GameplayEffectContext Context { get; }
        public int StackCount => timedStacks.Count;
        public int EffectiveStackCount => Definition != null &&
                                          Definition.DurationPolicy ==
                                          GameplayEffectDurationPolicy.Timed
            ? StackCount
            : 1;
        public float RemainingDuration
        {
            get
            {
                if (Definition == null)
                {
                    return 0f;
                }

                if (Definition.DurationPolicy ==
                    GameplayEffectDurationPolicy.Persistent)
                {
                    return float.PositiveInfinity;
                }

                float remaining = 0f;

                for (int index = 0; index < timedStacks.Count; index++)
                {
                    remaining = Mathf.Max(
                        remaining,
                        timedStacks[index].RemainingDuration);
                }

                return remaining;
            }
        }
        internal float TickRemaining { get; set; }

        internal void AddTimedStack(
            GameplayEffectContext context,
            long order)
        {
            timedStacks.Add(new TimedStack(
                context,
                Definition.Duration,
                order));
        }

        internal void RestoreTimedStack(
            GameplayEffectContext context,
            float remainingDuration,
            long order)
        {
            timedStacks.Add(new TimedStack(
                context,
                remainingDuration,
                order));
        }

        internal GameplayEffectTimedStackSnapshot[] CaptureTimedStacks(
            Func<GameplayEffectContext, GameplayEffectContextSnapshot>
                contextEncoder)
        {
            var snapshots =
                new GameplayEffectTimedStackSnapshot[timedStacks.Count];

            for (int index = 0; index < timedStacks.Count; index++)
            {
                TimedStack stack = timedStacks[index];
                snapshots[index] = new GameplayEffectTimedStackSnapshot(
                    contextEncoder(stack.Context),
                    stack.RemainingDuration,
                    stack.Order);
            }

            return snapshots;
        }

        internal bool RefreshAllTimedStacks()
        {
            if (timedStacks.Count == 0)
            {
                return false;
            }

            for (int index = 0; index < timedStacks.Count; index++)
            {
                timedStacks[index].RemainingDuration = Definition.Duration;
            }

            return true;
        }

        internal bool ReplaceOldestTimedStack(
            GameplayEffectContext context,
            long order)
        {
            if (timedStacks.Count == 0)
            {
                return false;
            }

            int oldestIndex = 0;

            for (int index = 1; index < timedStacks.Count; index++)
            {
                if (timedStacks[index].Order < timedStacks[oldestIndex].Order)
                {
                    oldestIndex = index;
                }
            }

            timedStacks[oldestIndex].Context = context;
            timedStacks[oldestIndex].RemainingDuration = Definition.Duration;
            timedStacks[oldestIndex].Order = order;
            return true;
        }

        internal float GetNextExpiry()
        {
            float next = float.PositiveInfinity;

            for (int index = 0; index < timedStacks.Count; index++)
            {
                next = Mathf.Min(next, timedStacks[index].RemainingDuration);
            }

            return next;
        }

        internal void DecrementTimedStacks(float deltaTime)
        {
            for (int index = 0; index < timedStacks.Count; index++)
            {
                timedStacks[index].RemainingDuration -= deltaTime;
            }
        }

        internal GameplayEffectContext[] SnapshotTickContexts()
        {
            var contexts = new GameplayEffectContext[timedStacks.Count];

            for (int index = 0; index < timedStacks.Count; index++)
            {
                contexts[index] = timedStacks[index].Context;
            }

            return contexts;
        }

        internal void RemoveExpiredTimedStacks()
        {
            for (int index = timedStacks.Count - 1; index >= 0; index--)
            {
                if (timedStacks[index].RemainingDuration <= Mathf.Epsilon)
                {
                    timedStacks.RemoveAt(index);
                }
            }
        }
    }

    public sealed class GameplayEffectRuntime
    {
        private static long nextInstanceId;
        private static long nextStackOrder;
        private readonly List<GameplayEffectInstance> active = new();
        private readonly UnityEngine.Object target;
        private readonly Dictionary<GameplayAttributeId, float>
            debugBaseValues = new();

        public GameplayEffectRuntime(
            UnityEngine.Object effectTarget,
            string debugChannel = null)
        {
            target = effectTarget != null
                ? effectTarget
                : throw new ArgumentNullException(nameof(effectTarget));
            DebugChannel = string.IsNullOrWhiteSpace(debugChannel)
                ? "Gameplay Effects"
                : debugChannel.Trim();
            GameplayEffectDebugRegistry.Register(this);
        }

        public IReadOnlyList<GameplayEffectInstance> ActiveInstances => active;
        public UnityEngine.Object Target => target;
        public string DebugChannel { get; }
        public IReadOnlyDictionary<GameplayAttributeId, float>
            DebugBaseValues => debugBaseValues;

        public bool TryCaptureSnapshot(
            Func<UnityEngine.Object, string> sourceEncoder,
            out GameplayEffectRuntimeSnapshot snapshot,
            out string error)
        {
            snapshot = null;
            error = string.Empty;

            try
            {
                var instances =
                    new GameplayEffectInstanceSnapshot[active.Count];

                for (int index = 0; index < active.Count; index++)
                {
                    GameplayEffectInstance instance = active[index];
                    GameplayEffectDefinition definition = instance?.Definition;

                    if (definition == null ||
                        string.IsNullOrWhiteSpace(definition.StableId))
                    {
                        error = $"Active gameplay effect {index} has no stable definition ID.";
                        return false;
                    }

                    GameplayEffectContextSnapshot context = EncodeContext(
                        instance.Context,
                        sourceEncoder);
                    GameplayEffectTimedStackSnapshot[] stacks =
                        instance.CaptureTimedStacks(value => EncodeContext(
                            value,
                            sourceEncoder));
                    instances[index] = new GameplayEffectInstanceSnapshot(
                        definition.StableId,
                        context,
                        definition.DurationPolicy ==
                            GameplayEffectDurationPolicy.Timed
                            ? instance.TickRemaining
                            : 0f,
                        stacks);
                }

                snapshot = new GameplayEffectRuntimeSnapshot(instances);
                return true;
            }
            catch (Exception exception)
            {
                error = $"Gameplay effect snapshot capture failed: {exception.Message}";
                snapshot = null;
                return false;
            }
        }

        public bool TryRestoreSnapshot(
            GameplayEffectRuntimeSnapshot snapshot,
            Func<string, GameplayEffectDefinition> definitionResolver,
            Func<string, UnityEngine.Object> sourceResolver,
            out string error)
        {
            error = string.Empty;

            if (snapshot == null)
            {
                error = "Gameplay effect snapshot is null.";
                return false;
            }

            if (definitionResolver == null)
            {
                error = "A gameplay effect definition resolver is required.";
                return false;
            }

            IReadOnlyList<GameplayEffectInstanceSnapshot> savedInstances =
                snapshot.Instances;
            var prepared = new PreparedInstance[savedInstances.Count];
            var definitions =
                new Dictionary<string, GameplayEffectDefinition>(
                    StringComparer.Ordinal);
            var sources = new Dictionary<string, UnityEngine.Object>(
                StringComparer.Ordinal);
            var timedDefinitions = new HashSet<string>(StringComparer.Ordinal);
            var stackOrders = new HashSet<long>();
            long maximumOrder = 0;

            try
            {
                for (int index = 0; index < savedInstances.Count; index++)
                {
                    GameplayEffectInstanceSnapshot saved = savedInstances[index];

                    if (saved == null ||
                        string.IsNullOrWhiteSpace(saved.DefinitionId))
                    {
                        error = $"Gameplay effect snapshot entry {index} is invalid.";
                        return false;
                    }

                    string definitionId = saved.DefinitionId.Trim();

                    if (!definitions.TryGetValue(
                            definitionId,
                            out GameplayEffectDefinition definition))
                    {
                        definition = definitionResolver(definitionId);

                        if (definition == null ||
                            !string.Equals(
                                definition.StableId,
                                definitionId,
                                StringComparison.Ordinal))
                        {
                            error = $"Gameplay effect definition '{definitionId}' could not be resolved.";
                            return false;
                        }

                        definitions.Add(definitionId, definition);
                    }

                    if (definition.DurationPolicy !=
                            GameplayEffectDurationPolicy.Persistent &&
                        definition.DurationPolicy !=
                            GameplayEffectDurationPolicy.Timed)
                    {
                        error = $"Gameplay effect '{definitionId}' does not have a restorable duration policy.";
                        return false;
                    }

                    if (!TryResolveContext(
                            saved.Context,
                            sourceResolver,
                            sources,
                            out GameplayEffectContext context,
                            out error))
                    {
                        return false;
                    }

                    IReadOnlyList<GameplayEffectTimedStackSnapshot> savedStacks =
                        saved.TimedStacks;

                    if (definition.DurationPolicy ==
                        GameplayEffectDurationPolicy.Persistent)
                    {
                        if (savedStacks.Count != 0 ||
                            !IsFinite(saved.TickRemaining) ||
                            Mathf.Abs(saved.TickRemaining) > Mathf.Epsilon)
                        {
                            error = $"Persistent gameplay effect '{definitionId}' has timed state.";
                            return false;
                        }

                        prepared[index] = new PreparedInstance(
                            definition,
                            context,
                            0f,
                            Array.Empty<PreparedTimedStack>());
                        continue;
                    }

                    if (!timedDefinitions.Add(definitionId))
                    {
                        error = $"Timed gameplay effect '{definitionId}' appears more than once.";
                        return false;
                    }

                    if (savedStacks.Count == 0 ||
                        savedStacks.Count > definition.MaximumStacks ||
                        !IsFinite(saved.TickRemaining) ||
                        saved.TickRemaining <= Mathf.Epsilon ||
                        saved.TickRemaining >
                            definition.TickInterval + Mathf.Epsilon)
                    {
                        error = $"Timed gameplay effect '{definitionId}' has invalid tick or stack state.";
                        return false;
                    }

                    var preparedStacks =
                        new PreparedTimedStack[savedStacks.Count];

                    for (int stackIndex = 0;
                         stackIndex < savedStacks.Count;
                         stackIndex++)
                    {
                        GameplayEffectTimedStackSnapshot savedStack =
                            savedStacks[stackIndex];

                        if (savedStack == null ||
                            !IsFinite(savedStack.RemainingDuration) ||
                            savedStack.RemainingDuration <= Mathf.Epsilon ||
                            savedStack.RemainingDuration >
                                definition.Duration + Mathf.Epsilon ||
                            savedStack.Order <= 0 ||
                            !stackOrders.Add(savedStack.Order) ||
                            !TryResolveContext(
                                savedStack.Context,
                                sourceResolver,
                                sources,
                                out GameplayEffectContext stackContext,
                                out error))
                        {
                            if (string.IsNullOrEmpty(error))
                            {
                                error = $"Timed gameplay effect '{definitionId}' stack {stackIndex} is invalid.";
                            }

                            return false;
                        }

                        preparedStacks[stackIndex] = new PreparedTimedStack(
                            stackContext,
                            savedStack.RemainingDuration,
                            savedStack.Order);
                        maximumOrder = Math.Max(maximumOrder, savedStack.Order);
                    }

                    prepared[index] = new PreparedInstance(
                        definition,
                        context,
                        saved.TickRemaining,
                        preparedStacks);
                }
            }
            catch (Exception exception)
            {
                error = $"Gameplay effect snapshot validation failed: {exception.Message}";
                return false;
            }

            var restored = new GameplayEffectInstance[prepared.Length];

            for (int index = 0; index < prepared.Length; index++)
            {
                PreparedInstance value = prepared[index];
                var instance = new GameplayEffectInstance(
                    Interlocked.Increment(ref nextInstanceId),
                    value.Definition,
                    value.Context);

                for (int stackIndex = 0;
                     stackIndex < value.Stacks.Length;
                     stackIndex++)
                {
                    PreparedTimedStack stack = value.Stacks[stackIndex];
                    instance.RestoreTimedStack(
                        stack.Context,
                        stack.RemainingDuration,
                        stack.Order);
                }

                if (value.Definition.DurationPolicy ==
                    GameplayEffectDurationPolicy.Timed)
                {
                    instance.TickRemaining = value.TickRemaining;
                }

                restored[index] = instance;
            }

            ObserveStackOrder(maximumOrder);
            active.Clear();
            active.AddRange(restored);
            return true;
        }

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
                    "Only persistent gameplay effects can use Apply.");
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

        public GameplayEffectApplicationResult ApplyTimed(
            GameplayEffectDefinition definition,
            GameplayEffectContext context)
        {
            if (definition == null ||
                definition.DurationPolicy != GameplayEffectDurationPolicy.Timed)
            {
                return default;
            }

            if (context.Target != target)
            {
                return default;
            }

            GameplayEffectInstance instance = FindTimedInstance(
                definition.StableId);

            if (instance == null)
            {
                instance = new GameplayEffectInstance(
                    Interlocked.Increment(ref nextInstanceId),
                    definition,
                    context);
                instance.AddTimedStack(
                    context,
                    Interlocked.Increment(ref nextStackOrder));
                active.Add(instance);
                return new GameplayEffectApplicationResult(
                    instance,
                    true,
                    false);
            }

            bool stackAdded = false;
            bool refreshed = false;
            GameplayEffectDefinition activeDefinition = instance.Definition;

            if (instance.StackCount < activeDefinition.MaximumStacks)
            {
                instance.AddTimedStack(
                    context,
                    Interlocked.Increment(ref nextStackOrder));
                stackAdded = true;

                if (activeDefinition.StackRefreshPolicy ==
                    GameplayEffectStackRefreshPolicy.RefreshAllDurations)
                {
                    refreshed = instance.RefreshAllTimedStacks();
                }
            }
            else
            {
                switch (activeDefinition.StackRefreshPolicy)
                {
                    case GameplayEffectStackRefreshPolicy.RefreshAllDurations:
                        refreshed = instance.RefreshAllTimedStacks();
                        break;
                    case GameplayEffectStackRefreshPolicy.ReplaceOldestStack:
                        refreshed = instance.ReplaceOldestTimedStack(
                            context,
                            Interlocked.Increment(ref nextStackOrder));
                        break;
                }
            }

            return new GameplayEffectApplicationResult(
                instance,
                stackAdded,
                refreshed);
        }

        public void AdvanceTimed(
            float deltaTime,
            Action<GameplayEffectTick> onTick)
        {
            if (deltaTime <= 0f || active.Count == 0)
            {
                return;
            }

            GameplayEffectInstance[] snapshot = active.ToArray();

            for (int index = 0; index < snapshot.Length; index++)
            {
                GameplayEffectInstance instance = snapshot[index];

                if (instance.Definition.DurationPolicy !=
                        GameplayEffectDurationPolicy.Timed ||
                    !active.Contains(instance))
                {
                    continue;
                }

                AdvanceTimedInstance(instance, deltaTime, onTick);

                if (active.Contains(instance) && instance.StackCount == 0)
                {
                    active.Remove(instance);
                }
            }
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
            SetDebugBaseValue(attribute, baseValue);
            return GameplayAttributeAggregator.Evaluate(
                attribute,
                baseValue,
                active);
        }

        public void SetDebugBaseValue(
            GameplayAttributeId attribute,
            float baseValue)
        {
            if (!float.IsNaN(baseValue) && !float.IsInfinity(baseValue))
            {
                debugBaseValues[attribute] = baseValue;
            }
        }

        public IReadOnlyList<GameplayAttributeEvaluationTrace>
            CaptureAttributeTraces()
        {
            var attributes = new HashSet<GameplayAttributeId>(
                debugBaseValues.Keys);

            for (int instanceIndex = 0;
                 instanceIndex < active.Count;
                 instanceIndex++)
            {
                IReadOnlyList<GameplayEffectModifier> modifiers =
                    active[instanceIndex].Definition?.Modifiers;

                if (modifiers == null)
                {
                    continue;
                }

                for (int modifierIndex = 0;
                     modifierIndex < modifiers.Count;
                     modifierIndex++)
                {
                    attributes.Add(modifiers[modifierIndex].Attribute);
                }
            }

            var ordered = new List<GameplayAttributeId>(attributes);
            ordered.Sort();
            var traces = new List<GameplayAttributeEvaluationTrace>(
                ordered.Count);

            for (int index = 0; index < ordered.Count; index++)
            {
                GameplayAttributeId attribute = ordered[index];
                bool hasBase = debugBaseValues.TryGetValue(
                    attribute,
                    out float baseValue);
                traces.Add(GameplayAttributeAggregator.Trace(
                    attribute,
                    baseValue,
                    active,
                    hasBase));
            }

            return traces;
        }

        private GameplayEffectInstance FindTimedInstance(string stableId)
        {
            for (int index = 0; index < active.Count; index++)
            {
                GameplayEffectInstance instance = active[index];

                if (instance.Definition.DurationPolicy ==
                        GameplayEffectDurationPolicy.Timed &&
                    string.Equals(
                        instance.Definition.StableId,
                        stableId,
                        StringComparison.Ordinal))
                {
                    return instance;
                }
            }

            return null;
        }

        private static GameplayEffectContextSnapshot EncodeContext(
            GameplayEffectContext context,
            Func<UnityEngine.Object, string> sourceEncoder)
        {
            if (context.Source == null)
            {
                return new GameplayEffectContextSnapshot(
                    context.SourceId,
                    string.Empty);
            }

            if (sourceEncoder == null)
            {
                throw new InvalidOperationException(
                    $"Source '{context.SourceId}' requires a stable source encoder.");
            }

            string sourceKey = sourceEncoder(context.Source)?.Trim();

            if (string.IsNullOrEmpty(sourceKey))
            {
                throw new InvalidOperationException(
                    $"Source '{context.SourceId}' could not be encoded to a stable key.");
            }

            return new GameplayEffectContextSnapshot(
                context.SourceId,
                sourceKey);
        }

        private bool TryResolveContext(
            GameplayEffectContextSnapshot saved,
            Func<string, UnityEngine.Object> sourceResolver,
            IDictionary<string, UnityEngine.Object> sourceCache,
            out GameplayEffectContext context,
            out string error)
        {
            context = default;
            error = string.Empty;

            if (saved == null)
            {
                error = "Gameplay effect context snapshot is null.";
                return false;
            }

            UnityEngine.Object source = null;
            string sourceKey = saved.SourceKey?.Trim() ?? string.Empty;

            if (!string.IsNullOrEmpty(sourceKey))
            {
                if (sourceResolver == null)
                {
                    error = $"Source '{sourceKey}' requires a source resolver.";
                    return false;
                }

                if (!sourceCache.TryGetValue(sourceKey, out source))
                {
                    source = sourceResolver(sourceKey);

                    if (source == null)
                    {
                        error = $"Gameplay effect source '{sourceKey}' could not be resolved.";
                        return false;
                    }

                    sourceCache.Add(sourceKey, source);
                }
            }

            context = new GameplayEffectContext(
                saved.SourceId,
                source,
                target);
            return true;
        }

        private static void ObserveStackOrder(long observedOrder)
        {
            long current = Interlocked.Read(ref nextStackOrder);

            while (current < observedOrder)
            {
                long previous = Interlocked.CompareExchange(
                    ref nextStackOrder,
                    observedOrder,
                    current);

                if (previous == current)
                {
                    return;
                }

                current = previous;
            }
        }

        private static bool IsFinite(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value);

        private void AdvanceTimedInstance(
            GameplayEffectInstance instance,
            float deltaTime,
            Action<GameplayEffectTick> onTick)
        {
            float remaining = deltaTime;

            while (remaining > Mathf.Epsilon &&
                   instance.StackCount > 0 &&
                   active.Contains(instance))
            {
                float step = Mathf.Min(
                    remaining,
                    instance.TickRemaining,
                    instance.GetNextExpiry());

                if (step > Mathf.Epsilon)
                {
                    instance.DecrementTimedStacks(step);
                    instance.TickRemaining -= step;
                    remaining -= step;
                }

                bool tickDue = instance.TickRemaining <= Mathf.Epsilon;

                if (tickDue)
                {
                    GameplayEffectContext[] contexts =
                        instance.SnapshotTickContexts();
                    instance.TickRemaining = instance.Definition.TickInterval;

                    for (int index = 0; index < contexts.Length; index++)
                    {
                        onTick?.Invoke(new GameplayEffectTick(
                            instance,
                            contexts[index]));

                        if (!active.Contains(instance))
                        {
                            return;
                        }
                    }
                }

                instance.RemoveExpiredTimedStacks();

                if (step <= Mathf.Epsilon && !tickDue)
                {
                    break;
                }
            }
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

        private readonly struct PreparedTimedStack
        {
            public PreparedTimedStack(
                GameplayEffectContext context,
                float remainingDuration,
                long order)
            {
                Context = context;
                RemainingDuration = remainingDuration;
                Order = order;
            }

            public GameplayEffectContext Context { get; }
            public float RemainingDuration { get; }
            public long Order { get; }
        }

        private readonly struct PreparedInstance
        {
            public PreparedInstance(
                GameplayEffectDefinition definition,
                GameplayEffectContext context,
                float tickRemaining,
                PreparedTimedStack[] stacks)
            {
                Definition = definition;
                Context = context;
                TickRemaining = tickRemaining;
                Stacks = stacks;
            }

            public GameplayEffectDefinition Definition { get; }
            public GameplayEffectContext Context { get; }
            public float TickRemaining { get; }
            public PreparedTimedStack[] Stacks { get; }
        }
    }

    public static class GameplayAttributeAggregator
    {
        private readonly struct ModifierSource
        {
            public ModifierSource(
                GameplayEffectInstance instance,
                GameplayEffectModifier modifier)
            {
                Instance = instance;
                Modifier = modifier;
            }

            public GameplayEffectInstance Instance { get; }
            public GameplayEffectModifier Modifier { get; }
        }

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

        public static GameplayAttributeEvaluationTrace Trace(
            GameplayAttributeId attribute,
            float baseValue,
            IReadOnlyList<GameplayEffectInstance> instances,
            bool hasKnownBaseValue = true)
        {
            var matching = new List<ModifierSource>();
            GameplayEffectInstance selectedOverride = null;
            GameplayEffectModifier selectedOverrideModifier = default;
            int selectedOverrideIndex = -1;

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

                        matching.Add(new ModifierSource(instance, modifier));

                        if (modifier.Operation ==
                                GameplayModifierOperation.Override &&
                            IsPreferredOverride(
                                instance,
                                modifier,
                                selectedOverride,
                                selectedOverrideModifier))
                        {
                            selectedOverride = instance;
                            selectedOverrideModifier = modifier;
                            selectedOverrideIndex = matching.Count - 1;
                        }
                    }
                }
            }

            var steps = new List<GameplayModifierEvaluationStep>(
                matching.Count);

            if (selectedOverride != null)
            {
                for (int index = 0; index < matching.Count; index++)
                {
                    ModifierSource source = matching[index];
                    bool applied = index == selectedOverrideIndex;
                    steps.Add(CreateStep(
                        index + 1,
                        source,
                        baseValue,
                        applied
                            ? selectedOverrideModifier.Magnitude
                            : baseValue,
                        applied));
                }

                return new GameplayAttributeEvaluationTrace(
                    attribute,
                    baseValue,
                    hasKnownBaseValue,
                    selectedOverrideModifier.Magnitude,
                    steps);
            }

            float current = baseValue;
            int sequence = 1;

            for (int index = 0; index < matching.Count; index++)
            {
                ModifierSource source = matching[index];

                if (source.Modifier.Operation != GameplayModifierOperation.Add)
                {
                    continue;
                }

                float next = current + source.Modifier.Magnitude;
                steps.Add(CreateStep(
                    sequence++, source, current, next, true));
                current = next;
            }

            float multiplierBase = current;
            float multiplicativeBonus = 0f;

            for (int index = 0; index < matching.Count; index++)
            {
                ModifierSource source = matching[index];

                if (source.Modifier.Operation !=
                    GameplayModifierOperation.Multiply)
                {
                    continue;
                }

                float before = current;
                multiplicativeBonus += source.Modifier.Magnitude;
                current = multiplierBase *
                          Mathf.Max(0f, 1f + multiplicativeBonus);
                steps.Add(CreateStep(
                    sequence++, source, before, current, true));
            }

            return new GameplayAttributeEvaluationTrace(
                attribute,
                baseValue,
                hasKnownBaseValue,
                current,
                steps);
        }

        private static GameplayModifierEvaluationStep CreateStep(
            int sequence,
            ModifierSource source,
            float input,
            float output,
            bool applied)
        {
            return new GameplayModifierEvaluationStep(
                sequence,
                source.Instance.InstanceId,
                source.Instance.Definition.StableId,
                source.Instance.Context.SourceId,
                source.Modifier.Operation,
                source.Modifier.Magnitude,
                source.Modifier.Priority,
                input,
                output,
                applied);
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
