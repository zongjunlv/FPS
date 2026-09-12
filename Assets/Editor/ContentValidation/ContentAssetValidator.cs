using System;
using System.Collections.Generic;
using System.Linq;
using FPS.GameplayEffects;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;
using Object = UnityEngine.Object;

public enum ContentValidationSeverity { Error, Warning }

public sealed class ContentValidationIssue
{
    public string Code { get; }
    public string Message { get; }
    public Object Asset { get; }
    public string AssetPath { get; }
    public ContentValidationSeverity Severity { get; }
    public ContentValidationIssue(string code, string message, Object asset,
        string assetPath, ContentValidationSeverity severity = ContentValidationSeverity.Error)
    { Code = code; Message = message; Asset = asset; AssetPath = assetPath; Severity = severity; }
}

public sealed class ContentValidationReport
{
    private readonly List<ContentValidationIssue> issues = new();
    public IReadOnlyList<ContentValidationIssue> Issues => issues;
    public int ErrorCount => issues.Count(issue => issue.Severity == ContentValidationSeverity.Error);
    public int WarningCount => issues.Count(issue => issue.Severity == ContentValidationSeverity.Warning);
    public bool IsValid => ErrorCount == 0;
    internal void Add(string code, string message, Object asset, string path = null,
        ContentValidationSeverity severity = ContentValidationSeverity.Error) =>
        issues.Add(new ContentValidationIssue(code, message, asset,
            path ?? AssetDatabase.GetAssetPath(asset), severity));
}

/// <summary>只读正式内容及其领域依赖，不调用会生成临时资源的属性或 Configure。</summary>
public static class ContentAssetValidator
{
    public static ContentValidationReport Validate(string[] roots = null,
        AddressableAssetSettings settingsOverride = null)
    {
        var report = new ContentValidationReport();
        roots ??= new[] { "Assets/Resources/Content" };
        var assets = new HashSet<ScriptableObject>();
        foreach (string root in roots)
        {
            if (string.IsNullOrWhiteSpace(root) || !AssetDatabase.IsValidFolder(root))
            { report.Add("SCAN_ROOT_INVALID", "扫描目录不存在。", null, root ?? ""); continue; }
            foreach (string guid in AssetDatabase.FindAssets("t:ScriptableObject", new[] { root }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                try
                {
                    foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
                        if (asset is ScriptableObject content && IsContent(content)) assets.Add(content);
                    foreach (string dependency in AssetDatabase.GetDependencies(path, true))
                        if (AssetDatabase.LoadAssetAtPath<ScriptableObject>(dependency) is ScriptableObject content && IsContent(content))
                            assets.Add(content);
                }
                catch (Exception exception) { report.Add("ASSET_READ_FAILED", exception.Message, null, path); }
            }
        }
        if (assets.Count == 0) report.Add("SCAN_EMPTY", "未找到可校验的正式内容资产。", null, string.Join(", ", roots));
        var ids = new Dictionary<string, Object>(StringComparer.Ordinal);
        var knownTags = new HashSet<string>(StringComparer.Ordinal) { "*", "enemy", "enemy.alive" };
        foreach (var archetype in assets.OfType<EnemyArchetypeDefinition>())
        { knownTags.Add(archetype.EnemyTypeId); knownTags.Add("enemy.type." + archetype.EnemyTypeId); }
        foreach (var set in assets.OfType<EnemyAbilitySetDefinition>()) knownTags.Add(set.StableId ?? "");
        var settings = settingsOverride != null ? settingsOverride : AddressableAssetSettingsDefaultObject.Settings;
        foreach (ScriptableObject asset in assets.OrderBy(AssetDatabase.GetAssetPath, StringComparer.Ordinal))
        {
            try
            {
                using var serialized = new SerializedObject(asset);
                string id = serialized.FindProperty("stableId")?.stringValue;
                if (string.IsNullOrWhiteSpace(id)) report.Add("ID_EMPTY", "stableId 不能为空。", asset);
                else if (ids.TryGetValue(id.Trim(), out Object previous))
                {
                    report.Add("ID_DUPLICATE", $"stableId '{id}' 与 {AssetDatabase.GetAssetPath(previous)} 重复。", asset);
                    if (!report.Issues.Any(issue => issue.Code == "ID_DUPLICATE" && issue.Asset == previous))
                        report.Add("ID_DUPLICATE", $"stableId '{id}' 与 {AssetDatabase.GetAssetPath(asset)} 重复。", previous);
                }
                else ids.Add(id.Trim(), asset);
                if (id != null && id != id.Trim()) report.Add("ID_INVALID", "stableId 不能包含首尾空白。", asset);
                ValidateAsset(asset, serialized, report, settings, knownTags);
            }
            catch (Exception exception) { report.Add("ASSET_READ_FAILED", exception.Message, asset); }
        }
        return report;
    }

    private static bool IsContent(ScriptableObject asset) => asset is CityNewContentCatalog ||
        asset is EnemyDefinition || asset is EnemyArchetypeDefinition || asset is EnemyAffixDefinition ||
        asset is EnemyAbilityDefinition || asset is EnemyAbilitySetDefinition || asset is WaveDefinition ||
        asset is WaveSequenceDefinition || asset is LootDropTableDefinition || asset is ItemDefinition ||
        asset is UpgradeDefinition || asset is GameplayEffectDefinition ||
        asset is CombatBuildDefinition || asset is CombatRuleDefinition ||
        asset is CombatRuleEffectDefinition;

    private static void ValidateAsset(ScriptableObject asset, SerializedObject data,
        ContentValidationReport report, AddressableAssetSettings settings, HashSet<string> knownTags)
    {
        // Inspector attributes describe raw data constraints; getters often silently clamp these.
        ValidateNumericFields(asset, data, report);
        if (asset is CityNewContentCatalog catalog && !catalog.TryValidate(out string error))
            report.Add("CATALOG_INVALID", error, asset);
        if (asset is ItemDefinition item)
        {
            Require(data, "icon", report, "ICON_MISSING");
            if (item.EffectType == ItemEffectType.RestoreHealth || item.EffectType == ItemEffectType.RestoreArmor)
                Require(data, "gameplayEffect", report);
        }
        if (asset is UpgradeDefinition upgrade)
        {
            Require(data, "icon", report, "ICON_MISSING");
            if (upgrade.EffectType == UpgradeEffectType.MaximumHealth) Require(data, "gameplayEffect", report);
        }
        if (asset is EnemyAffixDefinition affix)
        {
            Require(data, "gameplayEffect", report);
            if (affix.GameplayEffect != null)
            {
                // EnemyAffixController only evaluates these four attributes.
                foreach (var modifier in affix.GameplayEffect.Modifiers)
                    if (modifier.Attribute != GameplayAttributeId.EnemyMaximumArmor &&
                        modifier.Attribute != GameplayAttributeId.EnemyAttackDamage &&
                        modifier.Attribute != GameplayAttributeId.EnemyExperienceReward &&
                        modifier.Attribute != GameplayAttributeId.EnemyLootQuantity)
                        report.Add("EFFECT_INVALID", $"词缀属性 {modifier.Attribute} 不被 EnemyAffixController 消费。", asset);
                if (affix.GameplayEffect.DurationPolicy != GameplayEffectDurationPolicy.Persistent)
                    report.Add("EFFECT_INVALID", "词缀必须为持续效果；词缀控制器不执行瞬时或定时效果。", asset);
            }
        }
        if (asset is EnemyArchetypeDefinition archetype)
        {
            if (string.IsNullOrWhiteSpace(data.FindProperty("roleTag").stringValue))
                report.Add("TAG_INVALID", "敌人 roleTag 不能为空。", asset);
            ValidateAddress(archetype, settings, report);
        }
        if (asset is EnemySupportAuraAbilityDefinition)
        {
            Require(data, "buffEffect", report);
            string tag = data.FindProperty("requiredTargetTag").stringValue;
            if (string.IsNullOrWhiteSpace(tag) || !knownTags.Contains(tag))
                report.Add("TAG_INVALID", $"requiredTargetTag '{tag}' 无法匹配当前敌人内容。", asset);
        }
        if (asset is EnemyAbilitySetDefinition set)
        {
            CheckReferences(data.FindProperty("abilities"), asset, report);
            var types = new HashSet<Type>();
            foreach (var ability in set.Abilities)
                if (ability != null && !types.Add(ability.GetType()))
                    report.Add("ABILITY_CONFLICT", "同类能力重复，运行时只会使用第一项。", asset);
            if (types.Contains(typeof(RaiderApproachAbilityDefinition)) && types.Contains(typeof(SuppressorRangedAbilityDefinition)))
                report.Add("ABILITY_CONFLICT", "突袭和压制不能同时拥有战术移动控制权。", asset);
        }
        if (asset is WaveSequenceDefinition)
        {
            SerializedProperty stages = data.FindProperty("stages");
            if (stages.arraySize == 0) report.Add("REFERENCE_MISSING", "波次序列不能为空。", asset);
            for (int i = 0; i < stages.arraySize; i++)
                Require(data, $"stages.Array.data[{i}].wave", report);
        }
        if (asset is WaveDefinition wave) ValidateWave(wave, data, report);
        if (asset is LootDropTableDefinition)
        {
            SerializedProperty rules = data.FindProperty("rules");
            if (rules.arraySize == 0) report.Add("REFERENCE_MISSING", "掉落表没有规则。", asset);
            for (int r = 0; r < rules.arraySize; r++)
            {
                var rule = rules.GetArrayElementAtIndex(r);
                OrderedRange(rule, "minimumWave", "maximumWave", asset, report);
                OrderedRange(rule, "minimumRolls", "maximumRolls", asset, report);
                var entries = rule.FindPropertyRelative("entries");
                if (entries.arraySize == 0) report.Add("REFERENCE_MISSING", $"rules[{r}] 没有掉落项。", asset);
                for (int e = 0; e < entries.arraySize; e++)
                {
                    var entry = entries.GetArrayElementAtIndex(e);
                    OrderedRange(entry, "minimumQuantity", "maximumQuantity", asset, report);
                    if (string.IsNullOrWhiteSpace(entry.FindPropertyRelative("itemStableId").stringValue))
                        report.Add("REFERENCE_MISSING", $"rules[{r}].entries[{e}] 缺少物品ID。", asset);
                }
            }
        }
        if (asset is GameplayEffectDefinition effect)
        {
            SerializedProperty tags = data.FindProperty("gameplayTags");
            var unique = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < tags.arraySize; i++)
            {
                string tag = tags.GetArrayElementAtIndex(i).stringValue;
                if (string.IsNullOrWhiteSpace(tag) || tag != tag.Trim() || !unique.Add(tag))
                    report.Add("TAG_INVALID", $"gameplayTags[{i}] 为空、未规范化或重复。", asset);
            }
            if (effect.Modifiers.Count == 0 && effect.PeriodicMagnitude == 0)
                report.Add("EFFECT_INVALID", "效果没有属性修改或周期数值。", asset);
            if (effect.DurationPolicy == GameplayEffectDurationPolicy.Timed &&
                (data.FindProperty("duration").floatValue <= 0 || data.FindProperty("tickInterval").floatValue <= 0))
                report.Add("RANGE_INVALID", "定时效果的持续时间和tick间隔必须大于零。", asset);
        }
        if (asset is CombatBuildDefinition build)
        {
            IReadOnlyList<CombatRuleValidationIssue> issues =
                CombatRuleContentValidator.Validate(new[] { build });
            for (int index = 0; index < issues.Count; index++)
            {
                CombatRuleValidationIssue issue = issues[index];
                report.Add(
                    "COMBAT_RULE_" + issue.Code.ToString().ToUpperInvariant(),
                    issue.Message,
                    asset);
            }
        }
    }

    private static void ValidateAddress(EnemyArchetypeDefinition archetype,
        AddressableAssetSettings settings, ContentValidationReport report)
    {
        string address = archetype.TemplateAddress;
        if (string.IsNullOrWhiteSpace(address))
        { report.Add("TEMPLATE_MISSING", "敌人缺少异步模板地址。", archetype); return; }
        var matches = settings == null ? new List<AddressableAssetEntry>() :
            settings.groups.Where(group => group != null).SelectMany(group => group.entries)
                .Where(entry => entry.address == address).ToList();
        if (matches.Count != 1)
        { report.Add("ADDRESSABLE_MISSING", $"模板地址 '{address}' 应唯一注册，当前 {matches.Count} 项。", archetype); return; }
        var entry = matches[0];
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(entry.AssetPath);
        if (prefab == null || prefab.GetComponent<EnemyController>() == null)
            report.Add("TEMPLATE_MISSING", $"地址 '{address}' 必须引用带 EnemyController 的预制体。", archetype);
        if (entry.parentGroup.Name != "Local Enemies")
            report.Add("ADDRESSABLE_GROUP", $"地址 '{address}' 必须放入 Local Enemies 分组。", archetype);
        var schema = entry.parentGroup.GetSchema<BundledAssetGroupSchema>();
        if (schema == null || !schema.IncludeInBuild)
            report.Add("ADDRESSABLE_BUNDLE", $"地址 '{address}' 的分组未启用 Bundle 构建。", archetype);
        else if (schema.BuildPath.Id != settings.profileSettings.GetProfileDataByName(AddressableAssetSettings.kLocalBuildPath)?.Id ||
                 schema.LoadPath.Id != settings.profileSettings.GetProfileDataByName(AddressableAssetSettings.kLocalLoadPath)?.Id)
            report.Add("ADDRESSABLE_GROUP", $"地址 '{address}' 必须使用本地 BuildPath 和 LoadPath。", archetype);
    }

    private static void ValidateWave(WaveDefinition wave, SerializedObject data, ContentValidationReport report)
    {
        SerializedProperty entries = data.FindProperty("enemyEntries");
        if (entries.arraySize == 0) { report.Add("REFERENCE_MISSING", "波次没有敌人候选项。", wave); return; }
        for (int i = 0; i < entries.arraySize; i++) Require(data, $"enemyEntries.Array.data[{i}].archetype", report);
        if (wave.MinimumSpawnRadius < wave.PlayerSafetyDistance || wave.MaximumSpawnRadius < wave.MinimumSpawnRadius)
            report.Add("RANGE_INVALID", "生成半径必须满足 安全距离 ≤ 最小半径 ≤ 最大半径。", wave);
        if (wave.CompositionMode != WaveCompositionMode.ThreatBudget) return;
        var constraints = new List<ThreatRoleConstraint>();
        var roles = new HashSet<string>(StringComparer.Ordinal);
        SerializedProperty raw = data.FindProperty("roleConstraints");
        for (int i = 0; i < raw.arraySize; i++)
        {
            var constraint = raw.GetArrayElementAtIndex(i);
            string role = constraint.FindPropertyRelative("roleTag").stringValue;
            int min = constraint.FindPropertyRelative("minimumCount").intValue;
            int max = constraint.FindPropertyRelative("maximumCount").intValue;
            if (string.IsNullOrWhiteSpace(role) || role != role.Trim() || !roles.Add(role))
                report.Add("TAG_INVALID", $"roleConstraints[{i}] 标签为空、不规范或重复。", wave);
            if (min < 0 || max < min) report.Add("BUDGET_INVALID", $"角色 '{role}' 数量上下限非法。", wave);
            constraints.Add(new ThreatRoleConstraint(role, min, max));
        }
        int budget = data.FindProperty("threatBudget").intValue;
        if (budget < 1 || budget > 100000)
        { report.Add("BUDGET_INVALID", "威胁预算必须为正且不能超过校验安全上限 100000。", wave); return; }
        ThreatBudgetWavePlan plan = ThreatBudgetWaveComposer.Compose(wave.EnemyEntries, budget,
            wave.CompositionSeed, constraints, data.FindProperty("maximumEliteThreatRatio").floatValue);
        if (plan.UsedFallback || plan.TotalThreat > budget)
            report.Add("BUDGET_INVALID", "当前种子与预算/角色/精英比例约束无法满足，运行时会降级。", wave);
    }

    private static void Require(SerializedObject data, string path, ContentValidationReport report,
        string code = "REFERENCE_MISSING")
    {
        var property = data.FindProperty(path);
        if (property == null || property.objectReferenceValue == null)
            report.Add(code, $"{path} 缺少必需引用。", data.targetObject);
    }

    private static void OrderedRange(SerializedProperty parent, string minimum, string maximum,
        Object asset, ContentValidationReport report)
    {
        if (parent.FindPropertyRelative(minimum).intValue > parent.FindPropertyRelative(maximum).intValue)
            report.Add("RANGE_INVALID", $"{parent.propertyPath}.{maximum} 不能小于 {minimum}。", asset);
    }

    private static void CheckReferences(SerializedProperty array, Object asset, ContentValidationReport report)
    {
        if (array.arraySize == 0) report.Add("REFERENCE_MISSING", $"{array.propertyPath} 不能为空。", asset);
        for (int i = 0; i < array.arraySize; i++)
            if (array.GetArrayElementAtIndex(i).objectReferenceValue == null)
                report.Add("REFERENCE_MISSING", $"{array.propertyPath}[{i}] 缺少引用。", asset);
    }

    private static void ValidateNumericFields(Object asset, SerializedObject data, ContentValidationReport report)
    {
        var iterator = data.GetIterator();
        while (iterator.Next(true))
        {
            if (iterator.propertyType == SerializedPropertyType.Enum &&
                (iterator.enumValueIndex < 0 || iterator.enumValueIndex >= iterator.enumNames.Length))
                report.Add("RANGE_INVALID", $"{iterator.propertyPath} 枚举值未定义。", asset);
            if (iterator.propertyType == SerializedPropertyType.Float &&
                (float.IsNaN(iterator.floatValue) || float.IsInfinity(iterator.floatValue)))
                report.Add("RANGE_INVALID", $"{iterator.propertyPath} 不是有限数值。", asset);
            if (iterator.propertyType != SerializedPropertyType.Float && iterator.propertyType != SerializedPropertyType.Integer) continue;
            string name = iterator.name;
            double value = iterator.propertyType == SerializedPropertyType.Float ? iterator.floatValue : iterator.intValue;
            bool positive = name == "weight" || name == "threatCost" || name == "maximumStack" || name == "maximumLevel" ||
                name == "maximumStacks" || name == "maximumAliveCount" || name == "totalEnemyCount" ||
                name == "minimumQuantity" || name == "maximumQuantity" || name == "minimumWave" || name == "maximumWave" ||
                name == "minimumRolls" || name == "maximumRolls" || name == "retryInterval";
            bool nonnegative = name == "effectAmount" || name == "spawnInterval" || name == "intermissionAfterSeconds" || name == "rewardExperience";
            if (positive && value <= 0 || nonnegative && value < 0 ||
                (name == "dropChance" || name == "maximumEliteThreatRatio") && (value < 0 || value > 1))
                report.Add("RANGE_INVALID", $"{iterator.propertyPath} 的原始值 {value} 不合法。", asset);
        }
    }
}
