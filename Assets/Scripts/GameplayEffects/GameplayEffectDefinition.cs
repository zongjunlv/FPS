using System;
using System.Collections.Generic;
using UnityEngine;

namespace FPS.GameplayEffects
{
    public enum GameplayAttributeId
    {
        MaximumHealth
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
        [SerializeField] private GameplayEffectModifier[] modifiers =
            Array.Empty<GameplayEffectModifier>();

        public string StableId => stableId;
        public IReadOnlyList<GameplayEffectModifier> Modifiers => modifiers;

        public void Configure(
            string id,
            params GameplayEffectModifier[] configuredModifiers)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException(
                    "A gameplay effect requires a stable ID.",
                    nameof(id));
            }

            stableId = id.Trim();
            modifiers = configuredModifiers != null
                ? (GameplayEffectModifier[])configuredModifiers.Clone()
                : Array.Empty<GameplayEffectModifier>();
        }
    }
}
