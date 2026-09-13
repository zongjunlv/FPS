using System;
using System.Collections.Generic;
using FPS.Simulation;

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
        private const int MaximumEffectChainDepth = 16;

        private readonly int seed;
        private readonly Dictionary<string, CombatBuildDefinition> catalog =
            new(StringComparer.Ordinal);
        private readonly Dictionary<string, CombatRuleDefinition> activeRules =
            new(StringComparer.Ordinal);
        private readonly SortedSet<string> installedBuildIds =
            new(StringComparer.Ordinal);
        private CombatRuleDecisionKernel decisionKernel;

        public CombatRuleEngine(
            int runSeed,
            IReadOnlyList<CombatBuildDefinition> availableBuilds)
        {
            seed = runSeed;
            decisionKernel = new CombatRuleDecisionKernel(runSeed);
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
                    if (activeRules.TryAdd(rule.StableId, rule))
                    {
                        decisionKernel.AddRule(ToDecisionSpec(rule));
                    }
                }
            }

            return true;
        }

        public IReadOnlyList<CombatRuleExecution> Process(
            CombatTriggerContext context)
        {
            IReadOnlyList<CombatRuleDecision> decisions =
                decisionKernel.Process(ToDecisionContext(context));
            var executions = new List<CombatRuleExecution>();
            for (int index = 0; index < decisions.Count; index++)
            {
                if (!activeRules.TryGetValue(
                        decisions[index].RuleId,
                        out CombatRuleDefinition rule))
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

            }

            return executions.Count > 0
                ? Array.AsReadOnly(executions.ToArray())
                : Array.Empty<CombatRuleExecution>();
        }

        public CombatRuleRuntimeSnapshot CaptureSnapshot(long nextEventId = 0)
        {
            CombatRuleDecisionSnapshot decision =
                decisionKernel.CaptureSnapshot(nextEventId);
            var cooldowns = new List<CombatRuleCooldownSnapshot>(
                decision.Cooldowns.Count);
            for (int index = 0;
                 index < decision.Cooldowns.Count;
                 index++)
            {
                CombatRuleDecisionCooldown value =
                    decision.Cooldowns[index];
                cooldowns.Add(new CombatRuleCooldownSnapshot(
                    value.RuleId,
                    value.ReadyTick));
            }
            return new CombatRuleRuntimeSnapshot(
                new List<string>(installedBuildIds),
                cooldowns,
                decision.ProcessedEventIds,
                decision.NextEventId);
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

            var availableRuleIds = new HashSet<string>(StringComparer.Ordinal);
            var configuredRules = new List<CombatRuleDecisionSpec>();
            for (int buildIndex = 0; buildIndex < builds.Count; buildIndex++)
            {
                IReadOnlyList<CombatRuleDefinition> rules =
                    builds[buildIndex].Rules;
                for (int ruleIndex = 0; ruleIndex < rules.Count; ruleIndex++)
                {
                    if (rules[ruleIndex] != null)
                    {
                        CombatRuleDefinition rule = rules[ruleIndex];
                        if (availableRuleIds.Add(rule.StableId))
                        {
                            configuredRules.Add(ToDecisionSpec(rule));
                        }
                    }
                }
            }
            var cooldowns = new CombatRuleDecisionCooldown[
                snapshot.Cooldowns.Count];
            for (int index = 0; index < snapshot.Cooldowns.Count; index++)
            {
                CombatRuleCooldownSnapshot cooldown =
                    snapshot.Cooldowns[index];
                if (string.IsNullOrWhiteSpace(cooldown.RuleId) ||
                    cooldown.ReadyTick < 0 ||
                    !availableRuleIds.Contains(cooldown.RuleId))
                {
                    error = "构筑冷却快照无效。";
                    return false;
                }
                cooldowns[index] = new CombatRuleDecisionCooldown(
                    cooldown.RuleId,
                    cooldown.ReadyTick);
            }
            var restoredKernel = new CombatRuleDecisionKernel(
                seed,
                configuredRules);
            if (!restoredKernel.TryRestore(
                    new CombatRuleDecisionSnapshot(
                        cooldowns,
                        snapshot.ProcessedEventIds,
                        snapshot.NextEventId),
                    out error))
            {
                return false;
            }

            activeRules.Clear();
            installedBuildIds.Clear();
            for (int index = 0; index < builds.Count; index++)
            {
                CombatBuildDefinition build = builds[index];
                installedBuildIds.Add(build.StableId);
                for (int ruleIndex = 0;
                     ruleIndex < build.Rules.Count;
                     ruleIndex++)
                {
                    CombatRuleDefinition rule = build.Rules[ruleIndex];
                    if (rule != null &&
                        !string.IsNullOrWhiteSpace(rule.StableId))
                    {
                        activeRules.TryAdd(rule.StableId, rule);
                    }
                }
            }
            decisionKernel = restoredKernel;

            error = string.Empty;
            return true;
        }

        private static CombatRuleDecisionSpec ToDecisionSpec(
            CombatRuleDefinition rule)
        {
            bool hasEffect = false;
            for (int index = 0; index < rule.Effects.Count; index++)
                hasEffect |= rule.Effects[index] != null;
            return new CombatRuleDecisionSpec(
                rule.StableId,
                (CombatRuleDecisionTrigger)rule.Trigger,
                rule.RequiredSourceTags,
                rule.RequiredTargetTags,
                rule.ExcludedTargetTags,
                rule.MinimumHealthNormalized,
                rule.MaximumHealthNormalized,
                rule.CooldownTicks,
                rule.ProbabilityBasisPoints,
                hasEffect);
        }

        private static CombatRuleDecisionContext ToDecisionContext(
            CombatTriggerContext context)
        {
            return new CombatRuleDecisionContext(
                context.EventId,
                context.Tick,
                (CombatRuleDecisionTrigger)context.Trigger,
                context.SourceTags,
                context.TargetTags,
                context.TargetHealthNormalized);
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
