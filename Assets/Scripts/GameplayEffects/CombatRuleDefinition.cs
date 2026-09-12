using System;
using System.Collections.Generic;
using UnityEngine;

namespace FPS.GameplayEffects
{
    public enum CombatTriggerType
    {
        Hit,
        Kill,
        ReloadCompleted,
        DamageTaken,
        ArmorBroken,
        LowHealthEntered
    }

    public enum CombatRuleTarget
    {
        EventSource,
        EventTarget,
        NearbyEnemies
    }

    public enum CombatRuleEffectKind
    {
        ApplyStatus,
        SpreadStatus,
        ShowHudMessage
    }

    [CreateAssetMenu(
        fileName = "CombatRuleDefinition",
        menuName = "FPS/Gameplay Effects/Combat Rule")]
    public sealed class CombatRuleDefinition : ScriptableObject
    {
        [SerializeField] private string stableId;
        [SerializeField] private CombatTriggerType trigger;
        [SerializeField] private string[] requiredSourceTags = Array.Empty<string>();
        [SerializeField] private string[] requiredTargetTags = Array.Empty<string>();
        [SerializeField] private string[] excludedTargetTags = Array.Empty<string>();
        [SerializeField, Range(0f, 1f)] private float minimumHealthNormalized;
        [SerializeField, Range(0f, 1f)] private float maximumHealthNormalized = 1f;
        [SerializeField, Min(0)] private int cooldownTicks;
        [SerializeField, Range(0, 10000)] private int probabilityBasisPoints = 10000;
        [SerializeField] private CombatRuleEffectDefinition[] effects =
            Array.Empty<CombatRuleEffectDefinition>();

        public string StableId => stableId;
        public CombatTriggerType Trigger => trigger;
        public IReadOnlyList<string> RequiredSourceTags =>
            requiredSourceTags ?? Array.Empty<string>();
        public IReadOnlyList<string> RequiredTargetTags =>
            requiredTargetTags ?? Array.Empty<string>();
        public IReadOnlyList<string> ExcludedTargetTags =>
            excludedTargetTags ?? Array.Empty<string>();
        public float MinimumHealthNormalized => Mathf.Clamp01(minimumHealthNormalized);
        public float MaximumHealthNormalized => Mathf.Clamp01(maximumHealthNormalized);
        public int CooldownTicks => Mathf.Max(0, cooldownTicks);
        public int ProbabilityBasisPoints => Mathf.Clamp(probabilityBasisPoints, 0, 10000);
        public IReadOnlyList<CombatRuleEffectDefinition> Effects =>
            effects ?? Array.Empty<CombatRuleEffectDefinition>();

        public void Configure(
            string id,
            CombatTriggerType triggerType,
            IEnumerable<string> sourceTags,
            IEnumerable<string> targetTags,
            IEnumerable<string> blockedTargetTags,
            float minimumHealth,
            float maximumHealth,
            int cooldown,
            int probability,
            params CombatRuleEffectDefinition[] configuredEffects)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException(
                    "A combat rule requires a stable ID.", nameof(id));
            }

            stableId = id.Trim();
            trigger = triggerType;
            requiredSourceTags = CopyTags(sourceTags);
            requiredTargetTags = CopyTags(targetTags);
            excludedTargetTags = CopyTags(blockedTargetTags);
            minimumHealthNormalized = Mathf.Clamp01(minimumHealth);
            maximumHealthNormalized = Mathf.Clamp(
                maximumHealth, minimumHealthNormalized, 1f);
            cooldownTicks = Mathf.Max(0, cooldown);
            probabilityBasisPoints = Mathf.Clamp(probability, 0, 10000);
            effects = configuredEffects != null
                ? (CombatRuleEffectDefinition[])configuredEffects.Clone()
                : Array.Empty<CombatRuleEffectDefinition>();
        }

        private static string[] CopyTags(IEnumerable<string> source)
        {
            if (source == null)
            {
                return Array.Empty<string>();
            }

            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string value in source)
            {
                string tag = value?.Trim();
                if (!string.IsNullOrWhiteSpace(tag) && seen.Add(tag))
                {
                    result.Add(tag);
                }
            }
            return result.ToArray();
        }
    }
}
