using System;
using System.Collections.Generic;
using UnityEngine;

namespace FPS.GameplayEffects
{
    public enum GameplayAttributeId
    {
        MaximumHealth,
        CurrentHealth,
        CurrentArmor,
        WeaponFireRate,
        EnemyMaximumArmor,
        EnemyAttackDamage,
        EnemyExperienceReward,
        EnemyLootQuantity
    }

    public enum GameplayEffectDurationPolicy
    {
        Persistent,
        Instant,
        Timed
    }

    public enum GameplayEffectStackRefreshPolicy
    {
        None,
        RefreshAllDurations,
        ReplaceOldestStack
    }

    public enum GameplayModifierOperation
    {
        Add,
        Multiply,
        Override
    }

    [Serializable]
    public struct GameplayEffectModifier
    {
        public GameplayEffectModifier(
            GameplayAttributeId attribute,
            GameplayModifierOperation operation,
            float magnitude,
            int priority = 0)
        {
            this.attribute = attribute;
            this.operation = operation;
            this.magnitude = magnitude;
            this.priority = priority;
        }

        [SerializeField] private GameplayAttributeId attribute;
        [SerializeField] private GameplayModifierOperation operation;
        [SerializeField] private float magnitude;
        [SerializeField] private int priority;

        public GameplayAttributeId Attribute => attribute;
        public GameplayModifierOperation Operation => operation;
        public float Magnitude => magnitude;
        public int Priority => priority;
    }

    [CreateAssetMenu(
        fileName = "GameplayEffectDefinition",
        menuName = "FPS/Gameplay Effects/Effect Definition")]
    public sealed class GameplayEffectDefinition : ScriptableObject
    {
        [SerializeField] private string stableId;
        [SerializeField] private GameplayEffectDurationPolicy durationPolicy;
        [SerializeField, Min(0.01f)] private float duration = 1f;
        [SerializeField, Min(0.01f)] private float tickInterval = 1f;
        [SerializeField, Min(1)] private int maximumStacks = 1;
        [SerializeField] private GameplayEffectStackRefreshPolicy
            stackRefreshPolicy;
        [SerializeField] private float periodicMagnitude;
        [SerializeField] private GameplayEffectModifier[] modifiers =
            Array.Empty<GameplayEffectModifier>();

        public string StableId => stableId;
        public GameplayEffectDurationPolicy DurationPolicy => durationPolicy;
        public float Duration => Mathf.Max(0.01f, duration);
        public float TickInterval => Mathf.Max(0.01f, tickInterval);
        public int MaximumStacks => Mathf.Max(1, maximumStacks);
        public GameplayEffectStackRefreshPolicy StackRefreshPolicy =>
            stackRefreshPolicy;
        public float PeriodicMagnitude => periodicMagnitude;
        public IReadOnlyList<GameplayEffectModifier> Modifiers => modifiers;

        public void Configure(
            string id,
            params GameplayEffectModifier[] configuredModifiers)
        {
            Configure(
                id,
                GameplayEffectDurationPolicy.Persistent,
                configuredModifiers);
        }

        public void ConfigureInstant(
            string id,
            params GameplayEffectModifier[] configuredModifiers)
        {
            Configure(
                id,
                GameplayEffectDurationPolicy.Instant,
                configuredModifiers);
        }

        public void ConfigureTimed(
            string id,
            float configuredPeriodicMagnitude,
            float configuredTickInterval,
            float configuredDuration,
            int configuredMaximumStacks,
            GameplayEffectStackRefreshPolicy configuredRefreshPolicy,
            params GameplayEffectModifier[] configuredModifiers)
        {
            Configure(
                id,
                GameplayEffectDurationPolicy.Timed,
                configuredModifiers);
            periodicMagnitude = configuredPeriodicMagnitude;
            tickInterval = Mathf.Max(0.01f, configuredTickInterval);
            duration = Mathf.Max(0.01f, configuredDuration);
            maximumStacks = Mathf.Max(1, configuredMaximumStacks);
            stackRefreshPolicy = configuredRefreshPolicy;
        }

        private void Configure(
            string id,
            GameplayEffectDurationPolicy configuredDuration,
            GameplayEffectModifier[] configuredModifiers)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException(
                    "A gameplay effect requires a stable ID.",
                    nameof(id));
            }

            stableId = id.Trim();
            durationPolicy = configuredDuration;
            duration = 1f;
            tickInterval = 1f;
            maximumStacks = 1;
            stackRefreshPolicy = GameplayEffectStackRefreshPolicy.None;
            periodicMagnitude = 0f;
            modifiers = configuredModifiers != null
                ? (GameplayEffectModifier[])configuredModifiers.Clone()
                : Array.Empty<GameplayEffectModifier>();
        }
    }
}
