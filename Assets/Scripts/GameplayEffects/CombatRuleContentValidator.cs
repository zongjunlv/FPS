using System;
using System.Collections.Generic;

namespace FPS.GameplayEffects
{
    public enum CombatRuleValidationIssueCode
    {
        InvalidId,
        InvalidTag,
        MissingEffect,
        CycleDependency,
        IncompatibleTarget,
        InvalidCondition,
        DuplicateId
    }

    public readonly struct CombatRuleValidationIssue
    {
        public CombatRuleValidationIssue(
            CombatRuleValidationIssueCode code,
            string assetId,
            string message)
        {
            Code = code;
            AssetId = assetId ?? string.Empty;
            Message = message ?? string.Empty;
        }

        public CombatRuleValidationIssueCode Code { get; }
        public string AssetId { get; }
        public string Message { get; }
    }

    public static class CombatRuleContentValidator
    {
        public static IReadOnlyList<CombatRuleValidationIssue> Validate(
            IReadOnlyList<CombatBuildDefinition> builds)
        {
            var issues = new List<CombatRuleValidationIssue>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            if (builds == null)
            {
                return issues;
            }

            for (int buildIndex = 0; buildIndex < builds.Count; buildIndex++)
            {
                CombatBuildDefinition build = builds[buildIndex];
                if (build == null)
                {
                    Add(issues, CombatRuleValidationIssueCode.InvalidId,
                        string.Empty, "构筑定义不能为空。");
                    continue;
                }
                ValidateId(build.StableId, ids, issues);
                for (int ruleIndex = 0; ruleIndex < build.Rules.Count; ruleIndex++)
                {
                    ValidateRule(build.Rules[ruleIndex], ids, issues);
                }
            }
            return issues;
        }

        private static void ValidateRule(
            CombatRuleDefinition rule,
            HashSet<string> ids,
            List<CombatRuleValidationIssue> issues)
        {
            if (rule == null)
            {
                Add(issues, CombatRuleValidationIssueCode.InvalidId,
                    string.Empty, "构筑包含空规则。");
                return;
            }

            ValidateId(rule.StableId, ids, issues);
            ValidateTags(rule.StableId, rule.RequiredSourceTags, issues);
            ValidateTags(rule.StableId, rule.RequiredTargetTags, issues);
            ValidateTags(rule.StableId, rule.ExcludedTargetTags, issues);
            if (rule.MinimumHealthNormalized > rule.MaximumHealthNormalized)
            {
                Add(issues, CombatRuleValidationIssueCode.InvalidCondition,
                    rule.StableId, "生命比例条件下限不能高于上限。");
            }
            if (rule.Effects.Count == 0)
            {
                Add(issues, CombatRuleValidationIssueCode.MissingEffect,
                    rule.StableId, "规则至少需要一个效果。");
                return;
            }

            var visiting = new HashSet<CombatRuleEffectDefinition>();
            var visited = new HashSet<CombatRuleEffectDefinition>();
            for (int index = 0; index < rule.Effects.Count; index++)
            {
                ValidateEffect(rule.Effects[index], ids, visiting, visited, issues);
            }
        }

        private static void ValidateEffect(
            CombatRuleEffectDefinition effect,
            HashSet<string> ids,
            HashSet<CombatRuleEffectDefinition> visiting,
            HashSet<CombatRuleEffectDefinition> visited,
            List<CombatRuleValidationIssue> issues)
        {
            if (effect == null)
            {
                Add(issues, CombatRuleValidationIssueCode.MissingEffect,
                    string.Empty, "效果引用不能为空。");
                return;
            }
            if (visited.Contains(effect))
            {
                return;
            }
            if (!visiting.Add(effect))
            {
                Add(issues, CombatRuleValidationIssueCode.CycleDependency,
                    effect.StableId, "后续效果存在循环依赖。");
                return;
            }

            ValidateId(effect.StableId, ids, issues);
            if (!string.IsNullOrEmpty(effect.GrantedTag) &&
                !IsValidTag(effect.GrantedTag))
            {
                Add(issues, CombatRuleValidationIssueCode.InvalidTag,
                    effect.StableId, $"效果标签 '{effect.GrantedTag}' 无效。");
            }
            if ((effect.EffectKind == CombatRuleEffectKind.ApplyStatus ||
                 effect.EffectKind == CombatRuleEffectKind.SpreadStatus) &&
                effect.GameplayEffect == null)
            {
                Add(issues, CombatRuleValidationIssueCode.MissingEffect,
                    effect.StableId, "状态效果缺少 GameplayEffectDefinition。");
            }
            if (effect.EffectKind == CombatRuleEffectKind.SpreadStatus &&
                effect.Target != CombatRuleTarget.NearbyEnemies)
            {
                Add(issues, CombatRuleValidationIssueCode.IncompatibleTarget,
                    effect.StableId, "范围扩散效果必须以 NearbyEnemies 为目标。");
            }
            for (int index = 0; index < effect.FollowUpEffects.Count; index++)
            {
                ValidateEffect(effect.FollowUpEffects[index], ids,
                    visiting, visited, issues);
            }
            visiting.Remove(effect);
            visited.Add(effect);
        }

        private static void ValidateId(
            string id,
            HashSet<string> ids,
            List<CombatRuleValidationIssue> issues)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                Add(issues, CombatRuleValidationIssueCode.InvalidId,
                    id, "stableId 不能为空。");
            }
            else if (!ids.Add(id))
            {
                Add(issues, CombatRuleValidationIssueCode.DuplicateId,
                    id, "stableId 重复。");
            }
        }

        private static void ValidateTags(
            string owner,
            IReadOnlyList<string> tags,
            List<CombatRuleValidationIssue> issues)
        {
            for (int index = 0; index < tags.Count; index++)
            {
                if (!IsValidTag(tags[index]))
                {
                    Add(issues, CombatRuleValidationIssueCode.InvalidTag,
                        owner, $"标签 '{tags[index]}' 无效。");
                }
            }
        }

        private static bool IsValidTag(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value != value.Trim())
            {
                return false;
            }
            for (int index = 0; index < value.Length; index++)
            {
                char character = value[index];
                if (!char.IsLower(character) && !char.IsDigit(character) &&
                    character != '.' && character != '_' && character != '-')
                {
                    return false;
                }
            }
            return true;
        }

        private static void Add(
            ICollection<CombatRuleValidationIssue> issues,
            CombatRuleValidationIssueCode code,
            string id,
            string message)
        {
            issues.Add(new CombatRuleValidationIssue(code, id, message));
        }
    }
}
