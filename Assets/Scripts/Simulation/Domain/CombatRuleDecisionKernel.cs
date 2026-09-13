using System;
using System.Collections.Generic;

namespace FPS.Simulation
{
    public enum CombatRuleDecisionTrigger
    {
        Hit,
        Kill,
        ReloadCompleted,
        DamageTaken,
        ArmorBroken,
        LowHealthEntered
    }

    public sealed class CombatRuleDecisionSpec
    {
        private readonly string[] requiredSourceTags;
        private readonly string[] requiredTargetTags;
        private readonly string[] excludedTargetTags;

        public CombatRuleDecisionSpec(
            string stableId,
            CombatRuleDecisionTrigger trigger,
            IEnumerable<string> requiredSourceTags,
            IEnumerable<string> requiredTargetTags,
            IEnumerable<string> excludedTargetTags,
            float minimumHealthNormalized,
            float maximumHealthNormalized,
            int cooldownTicks,
            int probabilityBasisPoints,
            bool hasExecutableEffects = true)
        {
            if (string.IsNullOrWhiteSpace(stableId))
                throw new ArgumentException(
                    "Combat rule ID is required.", nameof(stableId));
            if (!Enum.IsDefined(typeof(CombatRuleDecisionTrigger), trigger))
                throw new ArgumentOutOfRangeException(nameof(trigger));
            if (!Finite(minimumHealthNormalized) ||
                !Finite(maximumHealthNormalized))
                throw new ArgumentOutOfRangeException(
                    nameof(minimumHealthNormalized));

            StableId = stableId.Trim();
            Trigger = trigger;
            this.requiredSourceTags = CopyTags(requiredSourceTags);
            this.requiredTargetTags = CopyTags(requiredTargetTags);
            this.excludedTargetTags = CopyTags(excludedTargetTags);
            MinimumHealthNormalized = Clamp01(minimumHealthNormalized);
            MaximumHealthNormalized = Math.Max(
                MinimumHealthNormalized,
                Clamp01(maximumHealthNormalized));
            CooldownTicks = Math.Max(0, cooldownTicks);
            ProbabilityBasisPoints = Math.Max(
                0,
                Math.Min(10000, probabilityBasisPoints));
            HasExecutableEffects = hasExecutableEffects;
        }

        public string StableId { get; }
        public CombatRuleDecisionTrigger Trigger { get; }
        public IReadOnlyList<string> RequiredSourceTags => requiredSourceTags;
        public IReadOnlyList<string> RequiredTargetTags => requiredTargetTags;
        public IReadOnlyList<string> ExcludedTargetTags => excludedTargetTags;
        public float MinimumHealthNormalized { get; }
        public float MaximumHealthNormalized { get; }
        public int CooldownTicks { get; }
        public int ProbabilityBasisPoints { get; }
        public bool HasExecutableEffects { get; }

        private static string[] CopyTags(IEnumerable<string> values)
        {
            if (values == null) return Array.Empty<string>();
            var unique = new HashSet<string>(StringComparer.Ordinal);
            foreach (string value in values)
            {
                string tag = value?.Trim();
                if (!string.IsNullOrWhiteSpace(tag)) unique.Add(tag);
            }
            string[] result = new string[unique.Count];
            unique.CopyTo(result);
            Array.Sort(result, StringComparer.Ordinal);
            return result;
        }

        private static float Clamp01(float value) =>
            Math.Max(0f, Math.Min(1f, value));

        private static bool Finite(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value);
    }

    public readonly struct CombatRuleDecisionContext
    {
        public CombatRuleDecisionContext(
            long eventId,
            long tick,
            CombatRuleDecisionTrigger trigger,
            IEnumerable<string> sourceTags,
            IEnumerable<string> targetTags,
            float targetHealthNormalized)
        {
            EventId = eventId;
            Tick = tick;
            Trigger = trigger;
            SourceTags = CopyTags(sourceTags);
            TargetTags = CopyTags(targetTags);
            TargetHealthNormalized = !Finite(targetHealthNormalized)
                ? 0f
                : Math.Max(0f, Math.Min(1f, targetHealthNormalized));
        }

        public long EventId { get; }
        public long Tick { get; }
        public CombatRuleDecisionTrigger Trigger { get; }
        public IReadOnlyList<string> SourceTags { get; }
        public IReadOnlyList<string> TargetTags { get; }
        public float TargetHealthNormalized { get; }

        private static IReadOnlyList<string> CopyTags(
            IEnumerable<string> values)
        {
            if (values == null) return Array.Empty<string>();
            var unique = new HashSet<string>(StringComparer.Ordinal);
            foreach (string value in values)
            {
                string tag = value?.Trim();
                if (!string.IsNullOrWhiteSpace(tag)) unique.Add(tag);
            }
            string[] result = new string[unique.Count];
            unique.CopyTo(result);
            Array.Sort(result, StringComparer.Ordinal);
            return result;
        }

        private static bool Finite(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value);
    }

    public readonly struct CombatRuleDecision
    {
        internal CombatRuleDecision(string ruleId)
        {
            RuleId = ruleId ?? string.Empty;
        }

        public string RuleId { get; }
    }

    public readonly struct CombatRuleDecisionCooldown
    {
        public CombatRuleDecisionCooldown(string ruleId, long readyTick)
        {
            RuleId = ruleId ?? string.Empty;
            ReadyTick = Math.Max(0L, readyTick);
        }

        public string RuleId { get; }
        public long ReadyTick { get; }
    }

    public sealed class CombatRuleDecisionSnapshot
    {
        private readonly CombatRuleDecisionCooldown[] cooldowns;
        private readonly long[] processedEventIds;

        public CombatRuleDecisionSnapshot(
            IReadOnlyList<CombatRuleDecisionCooldown> cooldowns,
            IReadOnlyList<long> processedEventIds,
            long nextEventId)
        {
            this.cooldowns = Copy(cooldowns);
            this.processedEventIds = Copy(processedEventIds);
            NextEventId = Math.Max(0L, nextEventId);
        }

        public IReadOnlyList<CombatRuleDecisionCooldown> Cooldowns => cooldowns;
        public IReadOnlyList<long> ProcessedEventIds => processedEventIds;
        public long NextEventId { get; }

        private static T[] Copy<T>(IReadOnlyList<T> source)
        {
            if (source == null || source.Count == 0) return Array.Empty<T>();
            var result = new T[source.Count];
            for (int index = 0; index < source.Count; index++)
                result[index] = source[index];
            return result;
        }
    }

    /// <summary>
    /// Engine-independent authority for combat rule dedupe, tag/health
    /// matching, probability and cooldown decisions.
    /// </summary>
    public sealed class CombatRuleDecisionKernel
    {
        public const int MaximumRememberedEvents = 256;

        private readonly int seed;
        private readonly Dictionary<string, CombatRuleDecisionSpec> rules =
            new Dictionary<string, CombatRuleDecisionSpec>(
                StringComparer.Ordinal);
        private readonly Dictionary<string, long> readyTicks =
            new Dictionary<string, long>(StringComparer.Ordinal);
        private readonly HashSet<long> processedEvents = new HashSet<long>();
        private readonly Queue<long> processedOrder = new Queue<long>();

        public CombatRuleDecisionKernel(
            int runSeed,
            IReadOnlyList<CombatRuleDecisionSpec> configuredRules = null)
        {
            seed = runSeed;
            if (configuredRules == null) return;
            for (int index = 0; index < configuredRules.Count; index++)
                AddRule(configuredRules[index]);
        }

        public int RuleCount => rules.Count;

        public bool AddRule(CombatRuleDecisionSpec rule)
        {
            return rule != null && rules.TryAdd(rule.StableId, rule);
        }

        public IReadOnlyList<CombatRuleDecision> Process(
            CombatRuleDecisionContext context)
        {
            if (context.EventId <= 0 || context.Tick < 0 ||
                !RememberEvent(context.EventId))
                return Array.Empty<CombatRuleDecision>();

            string[] ids = new string[rules.Count];
            rules.Keys.CopyTo(ids, 0);
            Array.Sort(ids, StringComparer.Ordinal);
            var decisions = new List<CombatRuleDecision>();
            for (int index = 0; index < ids.Length; index++)
            {
                CombatRuleDecisionSpec rule = rules[ids[index]];
                if (!Matches(rule, context) ||
                    readyTicks.TryGetValue(rule.StableId, out long ready) &&
                    context.Tick < ready ||
                    !Roll(rule, context.EventId) ||
                    !rule.HasExecutableEffects)
                    continue;

                decisions.Add(new CombatRuleDecision(rule.StableId));
                if (rule.CooldownTicks > 0)
                    readyTicks[rule.StableId] = checked(
                        context.Tick + rule.CooldownTicks);
            }
            return decisions.Count == 0
                ? Array.Empty<CombatRuleDecision>()
                : decisions.ToArray();
        }

        public CombatRuleDecisionSnapshot CaptureSnapshot(
            long nextEventId = 0)
        {
            string[] ids = new string[readyTicks.Count];
            readyTicks.Keys.CopyTo(ids, 0);
            Array.Sort(ids, StringComparer.Ordinal);
            var cooldowns = new CombatRuleDecisionCooldown[ids.Length];
            for (int index = 0; index < ids.Length; index++)
                cooldowns[index] = new CombatRuleDecisionCooldown(
                    ids[index], readyTicks[ids[index]]);
            long captured = Math.Max(0L, nextEventId);
            foreach (long eventId in processedOrder)
                captured = Math.Max(captured, eventId);
            return new CombatRuleDecisionSnapshot(
                cooldowns,
                processedOrder.ToArray(),
                captured);
        }

        public bool TryRestore(
            CombatRuleDecisionSnapshot snapshot,
            out string error)
        {
            error = string.Empty;
            if (snapshot == null ||
                snapshot.ProcessedEventIds.Count > MaximumRememberedEvents)
            {
                error = "Combat rule decision snapshot is invalid.";
                return false;
            }
            var stagedCooldowns = new Dictionary<string, long>(
                StringComparer.Ordinal);
            for (int index = 0; index < snapshot.Cooldowns.Count; index++)
            {
                CombatRuleDecisionCooldown value = snapshot.Cooldowns[index];
                if (string.IsNullOrWhiteSpace(value.RuleId) ||
                    value.ReadyTick < 0 ||
                    !rules.ContainsKey(value.RuleId) ||
                    !stagedCooldowns.TryAdd(value.RuleId, value.ReadyTick))
                {
                    error = "Combat rule cooldown snapshot is invalid.";
                    return false;
                }
            }
            var stagedEvents = new HashSet<long>();
            for (int index = 0;
                 index < snapshot.ProcessedEventIds.Count;
                 index++)
            {
                long eventId = snapshot.ProcessedEventIds[index];
                if (eventId <= 0 || eventId > snapshot.NextEventId ||
                    !stagedEvents.Add(eventId))
                {
                    error = "Combat rule event snapshot is invalid.";
                    return false;
                }
            }

            readyTicks.Clear();
            foreach (KeyValuePair<string, long> value in stagedCooldowns)
                readyTicks.Add(value.Key, value.Value);
            processedEvents.Clear();
            processedOrder.Clear();
            for (int index = 0;
                 index < snapshot.ProcessedEventIds.Count;
                 index++)
            {
                long eventId = snapshot.ProcessedEventIds[index];
                processedEvents.Add(eventId);
                processedOrder.Enqueue(eventId);
            }
            return true;
        }

        private bool RememberEvent(long eventId)
        {
            if (!processedEvents.Add(eventId)) return false;
            processedOrder.Enqueue(eventId);
            while (processedOrder.Count > MaximumRememberedEvents)
                processedEvents.Remove(processedOrder.Dequeue());
            return true;
        }

        private static bool Matches(
            CombatRuleDecisionSpec rule,
            CombatRuleDecisionContext context)
        {
            return rule.Trigger == context.Trigger &&
                   context.TargetHealthNormalized >=
                       rule.MinimumHealthNormalized &&
                   context.TargetHealthNormalized <=
                       rule.MaximumHealthNormalized &&
                   ContainsAll(
                       context.SourceTags, rule.RequiredSourceTags) &&
                   ContainsAll(
                       context.TargetTags, rule.RequiredTargetTags) &&
                   ContainsNone(
                       context.TargetTags, rule.ExcludedTargetTags);
        }

        private bool Roll(CombatRuleDecisionSpec rule, long eventId)
        {
            if (rule.ProbabilityBasisPoints <= 0) return false;
            if (rule.ProbabilityBasisPoints >= 10000) return true;
            uint hash = 2166136261u;
            Mix(ref hash, unchecked((uint)seed));
            Mix(ref hash, unchecked((uint)eventId));
            Mix(ref hash, unchecked((uint)(eventId >> 32)));
            for (int index = 0; index < rule.StableId.Length; index++)
                Mix(ref hash, rule.StableId[index]);
            return hash % 10000u < rule.ProbabilityBasisPoints;
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
                if (!Contains(actual, required[index])) return false;
            return true;
        }

        private static bool ContainsNone(
            IReadOnlyList<string> actual,
            IReadOnlyList<string> excluded)
        {
            for (int index = 0; index < excluded.Count; index++)
                if (Contains(actual, excluded[index])) return false;
            return true;
        }

        private static bool Contains(
            IReadOnlyList<string> values,
            string expected)
        {
            for (int index = 0; index < values.Count; index++)
                if (string.Equals(
                        values[index], expected, StringComparison.Ordinal))
                    return true;
            return false;
        }
    }
}
