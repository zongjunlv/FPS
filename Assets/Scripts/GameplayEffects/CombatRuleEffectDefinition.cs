using System;
using System.Collections.Generic;
using UnityEngine;

namespace FPS.GameplayEffects
{
    [CreateAssetMenu(
        fileName = "CombatRuleEffectDefinition",
        menuName = "FPS/Gameplay Effects/Combat Rule Effect")]
    public sealed class CombatRuleEffectDefinition : ScriptableObject
    {
        [SerializeField] private string stableId;
        [SerializeField] private CombatRuleEffectKind effectKind;
        [SerializeField] private CombatRuleTarget target = CombatRuleTarget.EventTarget;
        [SerializeField] private GameplayEffectDefinition gameplayEffect;
        [SerializeField] private string grantedTag;
        [SerializeField, Min(0f)] private float radius;
        [SerializeField, Min(1)] private int maximumTargets = 1;
        [SerializeField] private string hudMessage;
        [SerializeField] private CombatRuleEffectDefinition[] followUpEffects =
            Array.Empty<CombatRuleEffectDefinition>();

        public string StableId => stableId;
        public CombatRuleEffectKind EffectKind => effectKind;
        public CombatRuleTarget Target => target;
        public GameplayEffectDefinition GameplayEffect => gameplayEffect;
        public string GrantedTag => grantedTag;
        public float Radius => Mathf.Max(0f, radius);
        public int MaximumTargets => Mathf.Max(1, maximumTargets);
        public string HudMessage => hudMessage ?? string.Empty;
        public IReadOnlyList<CombatRuleEffectDefinition> FollowUpEffects =>
            followUpEffects ?? Array.Empty<CombatRuleEffectDefinition>();

        public void Configure(
            string id,
            CombatRuleEffectKind kind,
            CombatRuleTarget effectTarget,
            GameplayEffectDefinition statusEffect,
            string tag,
            float effectRadius,
            int targetLimit,
            string message,
            params CombatRuleEffectDefinition[] followUps)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException(
                    "A combat rule effect requires a stable ID.", nameof(id));
            }

            stableId = id.Trim();
            effectKind = kind;
            target = effectTarget;
            gameplayEffect = statusEffect;
            grantedTag = tag?.Trim() ?? string.Empty;
            radius = Mathf.Max(0f, effectRadius);
            maximumTargets = Mathf.Max(1, targetLimit);
            hudMessage = message ?? string.Empty;
            followUpEffects = followUps != null
                ? (CombatRuleEffectDefinition[])followUps.Clone()
                : Array.Empty<CombatRuleEffectDefinition>();
        }
    }
}
