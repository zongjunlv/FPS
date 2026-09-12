using System;
using System.Collections.Generic;

namespace FPS.GameplayEffects
{
    public readonly struct CombatTriggerContext
    {
        public CombatTriggerContext(
            long eventId,
            long tick,
            CombatTriggerType trigger,
            string sourceId,
            string targetId,
            IEnumerable<string> sourceTags,
            IEnumerable<string> targetTags,
            float targetHealthNormalized = 1f,
            float primaryValue = 0f,
            float secondaryValue = 0f)
        {
            EventId = eventId;
            Tick = tick;
            Trigger = trigger;
            SourceId = sourceId ?? string.Empty;
            TargetId = targetId ?? string.Empty;
            SourceTags = CopyTags(sourceTags);
            TargetTags = CopyTags(targetTags);
            TargetHealthNormalized = Clamp01(targetHealthNormalized);
            PrimaryValue = IsFinite(primaryValue) ? primaryValue : 0f;
            SecondaryValue = IsFinite(secondaryValue) ? secondaryValue : 0f;
        }

        public long EventId { get; }
        public long Tick { get; }
        public CombatTriggerType Trigger { get; }
        public string SourceId { get; }
        public string TargetId { get; }
        public IReadOnlyList<string> SourceTags { get; }
        public IReadOnlyList<string> TargetTags { get; }
        public float TargetHealthNormalized { get; }
        public float PrimaryValue { get; }
        public float SecondaryValue { get; }

        private static IReadOnlyList<string> CopyTags(
            IEnumerable<string> values)
        {
            if (values == null)
            {
                return Array.Empty<string>();
            }

            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string value in values)
            {
                string tag = value?.Trim();
                if (!string.IsNullOrWhiteSpace(tag) && seen.Add(tag))
                {
                    result.Add(tag);
                }
            }
            result.Sort(StringComparer.Ordinal);
            return Array.AsReadOnly(result.ToArray());
        }

        private static float Clamp01(float value) =>
            !IsFinite(value) ? 0f : Math.Max(0f, Math.Min(1f, value));

        private static bool IsFinite(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value);
    }

    public readonly struct CombatRuleExecution
    {
        public CombatRuleExecution(
            string ruleId,
            CombatRuleEffectDefinition effect,
            int depth)
        {
            RuleId = ruleId ?? string.Empty;
            Effect = effect;
            Depth = Math.Max(0, depth);
        }

        public string RuleId { get; }
        public CombatRuleEffectDefinition Effect { get; }
        public int Depth { get; }
    }

    public readonly struct CombatRuleCooldownSnapshot
    {
        public CombatRuleCooldownSnapshot(string ruleId, long readyTick)
        {
            RuleId = ruleId ?? string.Empty;
            ReadyTick = Math.Max(0L, readyTick);
        }

        public string RuleId { get; }
        public long ReadyTick { get; }
    }

    public sealed class CombatRuleRuntimeSnapshot
    {
        public CombatRuleRuntimeSnapshot(
            IReadOnlyList<string> installedBuildIds,
            IReadOnlyList<CombatRuleCooldownSnapshot> cooldowns,
            IReadOnlyList<long> processedEventIds,
            long nextEventId = 0)
        {
            InstalledBuildIds = Copy(installedBuildIds);
            Cooldowns = Copy(cooldowns);
            ProcessedEventIds = Copy(processedEventIds);
            NextEventId = Math.Max(0L, nextEventId);
        }

        public IReadOnlyList<string> InstalledBuildIds { get; }
        public IReadOnlyList<CombatRuleCooldownSnapshot> Cooldowns { get; }
        public IReadOnlyList<long> ProcessedEventIds { get; }
        public long NextEventId { get; }

        private static IReadOnlyList<T> Copy<T>(IReadOnlyList<T> source)
        {
            if (source == null || source.Count == 0)
            {
                return Array.Empty<T>();
            }

            var result = new T[source.Count];
            for (int index = 0; index < source.Count; index++)
            {
                result[index] = source[index];
            }
            return Array.AsReadOnly(result);
        }
    }

    public sealed class CombatRuleEngine
    {
        private const int MaximumRememberedEvents = 256;
        private const int MaximumEffectChainDepth = 16;

        private readonly int seed;
        private readonly Dictionary<string, CombatBuildDefinition> catalog =
            new(StringComparer.Ordinal);
        private readonly Dictionary<string, CombatRuleDefinition> activeRules =
            new(StringComparer.Ordinal);
        private readonly SortedSet<string> installedBuildIds =
            new(StringComparer.Ordinal);
        private readonly Dictionary<string, long> readyTicks =
            new(StringComparer.Ordinal);
        private readonly HashSet<long> processedEvents = new();
        private readonly Queue<long> processedOrder = new();

        public CombatRuleEngine(
            int runSeed,
            IReadOnlyList<CombatBuildDefinition> availableBuilds)
        {
            seed = runSeed;
            if (availableBuilds == null)
            {
                return;
            }

            for (int index = 0; index < availableBuilds.Count; index++)
            {
                CombatBuildDefinition build = availableBuilds[index];
                if (build != null &&
                    !string.IsNullOrWhiteSpace(build.StableId))
                {
                    catalog.TryAdd(build.StableId, build);
                }
            }
        }

        public IReadOnlyCollection<string> InstalledBuildIds =>
            installedBuildIds;
        public int ActiveRuleCount => activeRules.Count;

        public bool Install(CombatBuildDefinition build)
        {
            if (build == null || string.IsNullOrWhiteSpace(build.StableId) ||
                !catalog.TryGetValue(build.StableId, out CombatBuildDefinition known) ||
                !ReferenceEquals(build, known) ||
                !installedBuildIds.Add(build.StableId))
            {
                return false;
            }

            for (int index = 0; index < build.Rules.Count; index++)
            {
                CombatRuleDefinition rule = build.Rules[index];
                if (rule != null && !string.IsNullOrWhiteSpace(rule.StableId))
                {
                    activeRules.TryAdd(rule.StableId, rule);
                }
            }

            return true;
        }

        public IReadOnlyList<CombatRuleExecution> Process(
            CombatTriggerContext context)
        {
            if (context.EventId <= 0 || context.Tick < 0 ||
                !RememberEvent(context.EventId))
            {
                return Array.Empty<CombatRuleExecution>();
            }

            var ruleIds = new List<string>(activeRules.Keys);
            ruleIds.Sort(StringComparer.Ordinal);
            var executions = new List<CombatRuleExecution>();

            for (int index = 0; index < ruleIds.Count; index++)
            {
                CombatRuleDefinition rule = activeRules[ruleIds[index]];
                if (!Matches(rule, context) ||
                    (readyTicks.TryGetValue(rule.StableId, out long ready) &&
                     context.Tick < ready) ||
                    !Roll(rule, context.EventId))
                {
                    continue;
                }

                var visited = new HashSet<CombatRuleEffectDefinition>();
                for (int effectIndex = 0;
                     effectIndex < rule.Effects.Count;
                     effectIndex++)
                {
                    CollectEffect(
                        rule.StableId,
                        rule.Effects[effectIndex],
                        0,
                        visited,
                        executions);
                }

                if (executions.Count > 0 && rule.CooldownTicks > 0)
                {
                    readyTicks[rule.StableId] =
                        context.Tick + rule.CooldownTicks;
                }
            }

            return executions.Count > 0
                ? Array.AsReadOnly(executions.ToArray())
                : Array.Empty<CombatRuleExecution>();
        }

        public CombatRuleRuntimeSnapshot CaptureSnapshot(long nextEventId = 0)
        {
            var cooldowns = new List<CombatRuleCooldownSnapshot>(
                readyTicks.Count);
            foreach (KeyValuePair<string, long> pair in readyTicks)
            {
                cooldowns.Add(new CombatRuleCooldownSnapshot(
                    pair.Key,
                    pair.Value));
            }
            cooldowns.Sort((left, right) => string.CompareOrdinal(
                left.RuleId,
                right.RuleId));
            long capturedEventId = Math.Max(0L, nextEventId);
            foreach (long eventId in processedOrder)
            {
                capturedEventId = Math.Max(capturedEventId, eventId);
            }
            return new CombatRuleRuntimeSnapshot(
                new List<string>(installedBuildIds),
                cooldowns,
                processedOrder.ToArray(),
                capturedEventId);
        }

        public bool TryRestore(
            CombatRuleRuntimeSnapshot snapshot,
            out string error)
        {
            if (snapshot == null)
            {
                error = "构筑运行时快照为空。";
                return false;
            }

            var builds = new List<CombatBuildDefinition>();
            var buildIds = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0;
                 index < snapshot.InstalledBuildIds.Count;
                 index++)
            {
                string id = snapshot.InstalledBuildIds[index];
                if (string.IsNullOrWhiteSpace(id) || !buildIds.Add(id) ||
                    !catalog.TryGetValue(id, out CombatBuildDefinition build))
                {
                    error = $"构筑快照包含未知或重复构筑：{id}";
                    return false;
                }
                builds.Add(build);
            }

            var cooldowns = new Dictionary<string, long>(
                StringComparer.Ordinal);
            var availableRuleIds = new HashSet<string>(StringComparer.Ordinal);
            for (int buildIndex = 0; buildIndex < builds.Count; buildIndex++)
            {
                IReadOnlyList<CombatRuleDefinition> rules =
                    builds[buildIndex].Rules;
                for (int ruleIndex = 0; ruleIndex < rules.Count; ruleIndex++)
                {
                    if (rules[ruleIndex] != null)
                    {
                        availableRuleIds.Add(rules[ruleIndex].StableId);
                    }
                }
            }
            for (int index = 0; index < snapshot.Cooldowns.Count; index++)
            {
                CombatRuleCooldownSnapshot cooldown =
                    snapshot.Cooldowns[index];
                if (string.IsNullOrWhiteSpace(cooldown.RuleId) ||
                    cooldown.ReadyTick < 0 ||
                    !availableRuleIds.Contains(cooldown.RuleId) ||
                    !cooldowns.TryAdd(cooldown.RuleId, cooldown.ReadyTick))
                {
                    error = "构筑冷却快照无效。";
                    return false;
                }
            }

            var events = new HashSet<long>();
            if (snapshot.ProcessedEventIds.Count > MaximumRememberedEvents)
            {
                error = "构筑事件去重窗口超出上限。";
                return false;
            }
            for (int index = 0;
                 index < snapshot.ProcessedEventIds.Count;
                 index++)
            {
                long eventId = snapshot.ProcessedEventIds[index];
                if (eventId <= 0 || eventId > snapshot.NextEventId ||
                    !events.Add(eventId))
                {
                    error = "构筑事件去重快照无效。";
                    return false;
                }
            }

            activeRules.Clear();
            installedBuildIds.Clear();
            readyTicks.Clear();
            processedEvents.Clear();
            processedOrder.Clear();
            for (int index = 0; index < builds.Count; index++)
            {
                Install(builds[index]);
            }
            foreach (KeyValuePair<string, long> pair in cooldowns)
            {
                readyTicks.Add(pair.Key, pair.Value);
            }
            for (int index = 0;
                 index < snapshot.ProcessedEventIds.Count;
                 index++)
            {
                long eventId = snapshot.ProcessedEventIds[index];
                processedEvents.Add(eventId);
                processedOrder.Enqueue(eventId);
            }

            error = string.Empty;
            return true;
        }

        private bool RememberEvent(long eventId)
        {
            if (!processedEvents.Add(eventId))
            {
                return false;
            }

            processedOrder.Enqueue(eventId);
            while (processedOrder.Count > MaximumRememberedEvents)
            {
                processedEvents.Remove(processedOrder.Dequeue());
            }
            return true;
        }

        private static bool Matches(
            CombatRuleDefinition rule,
            CombatTriggerContext context)
        {
            return rule != null && rule.Trigger == context.Trigger &&
                   context.TargetHealthNormalized >=
                       rule.MinimumHealthNormalized &&
                   context.TargetHealthNormalized <=
                       rule.MaximumHealthNormalized &&
                   ContainsAll(context.SourceTags, rule.RequiredSourceTags) &&
                   ContainsAll(context.TargetTags, rule.RequiredTargetTags) &&
                   ContainsNone(context.TargetTags, rule.ExcludedTargetTags);
        }

        private bool Roll(CombatRuleDefinition rule, long eventId)
        {
            int probability = rule.ProbabilityBasisPoints;
            if (probability <= 0)
            {
                return false;
            }
            if (probability >= 10000)
            {
                return true;
            }

            uint hash = 2166136261u;
            Mix(ref hash, unchecked((uint)seed));
            Mix(ref hash, unchecked((uint)eventId));
            Mix(ref hash, unchecked((uint)(eventId >> 32)));
            string id = rule.StableId;
            for (int index = 0; index < id.Length; index++)
            {
                Mix(ref hash, id[index]);
            }
            return hash % 10000u < probability;
        }

        private static void Mix(ref uint hash, uint value)
        {
            hash ^= value;
            hash *= 16777619u;
        }

        private static bool ContainsAll(
            IReadOnlyList<string> actual,
            IReadOnlyList<string> required)
        {
            for (int index = 0; index < required.Count; index++)
            {
                if (!Contains(actual, required[index]))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool ContainsNone(
            IReadOnlyList<string> actual,
            IReadOnlyList<string> excluded)
        {
            for (int index = 0; index < excluded.Count; index++)
            {
                if (Contains(actual, excluded[index]))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool Contains(
            IReadOnlyList<string> values,
            string expected)
        {
            for (int index = 0; index < values.Count; index++)
            {
                if (string.Equals(
                        values[index],
                        expected,
                        StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        private static void CollectEffect(
            string ruleId,
            CombatRuleEffectDefinition effect,
            int depth,
            HashSet<CombatRuleEffectDefinition> visited,
            List<CombatRuleExecution> output)
        {
            if (effect == null || depth > MaximumEffectChainDepth ||
                !visited.Add(effect))
            {
                return;
            }

            output.Add(new CombatRuleExecution(ruleId, effect, depth));
            for (int index = 0;
                 index < effect.FollowUpEffects.Count;
                 index++)
            {
                CollectEffect(
                    ruleId,
                    effect.FollowUpEffects[index],
                    depth + 1,
                    visited,
                    output);
            }
        }
    }
}
