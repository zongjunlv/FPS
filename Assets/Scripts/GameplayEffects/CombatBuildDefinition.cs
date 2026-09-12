using System;
using System.Collections.Generic;
using UnityEngine;

namespace FPS.GameplayEffects
{
    [CreateAssetMenu(
        fileName = "CombatBuildDefinition",
        menuName = "FPS/Gameplay Effects/Combat Build")]
    public sealed class CombatBuildDefinition : ScriptableObject
    {
        [SerializeField] private string stableId;
        [SerializeField] private string displayName;
        [SerializeField] private bool installOnRunStart;
        [SerializeField] private CombatRuleDefinition[] rules =
            Array.Empty<CombatRuleDefinition>();

        public string StableId => stableId;
        public string DisplayName => displayName ?? string.Empty;
        public bool InstallOnRunStart => installOnRunStart;
        public IReadOnlyList<CombatRuleDefinition> Rules =>
            rules ?? Array.Empty<CombatRuleDefinition>();

        public void Configure(
            string id,
            string title,
            bool starter,
            params CombatRuleDefinition[] configuredRules)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException(
                    "A combat build requires a stable ID.", nameof(id));
            }

            stableId = id.Trim();
            displayName = title ?? string.Empty;
            installOnRunStart = starter;
            rules = configuredRules != null
                ? (CombatRuleDefinition[])configuredRules.Clone()
                : Array.Empty<CombatRuleDefinition>();
        }
    }
}
