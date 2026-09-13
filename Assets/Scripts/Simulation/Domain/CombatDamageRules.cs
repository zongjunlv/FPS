using System;

namespace FPS.Simulation
{
    /// <summary>
    /// Authoritative, engine-independent health and armor damage rule shared
    /// by live gameplay and headless balance simulation.
    /// </summary>
    public static class CombatDamageRules
    {
        public static CombatDamageResolution Apply(
            float currentHealth,
            float currentArmor,
            float incomingDamage)
        {
            if (!IsFinite(currentHealth) || currentHealth < 0f)
                throw new ArgumentOutOfRangeException(nameof(currentHealth));
            if (!IsFinite(currentArmor) || currentArmor < 0f)
                throw new ArgumentOutOfRangeException(nameof(currentArmor));
            if (!IsFinite(incomingDamage))
                throw new ArgumentOutOfRangeException(nameof(incomingDamage));

            float damage = Math.Max(0f, incomingDamage);
            float armorDamage = Math.Min(currentArmor, damage);
            float remainingDamage = damage - armorDamage;
            float healthDamage = Math.Min(currentHealth, remainingDamage);

            return new CombatDamageResolution(
                currentHealth - healthDamage,
                currentArmor - armorDamage,
                healthDamage,
                armorDamage);
        }

        private static bool IsFinite(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value);
    }

    public readonly struct CombatDamageResolution
    {
        internal CombatDamageResolution(
            float remainingHealth,
            float remainingArmor,
            float healthDamage,
            float armorDamage)
        {
            RemainingHealth = remainingHealth;
            RemainingArmor = remainingArmor;
            HealthDamage = healthDamage;
            ArmorDamage = armorDamage;
        }

        public float RemainingHealth { get; }
        public float RemainingArmor { get; }
        public float HealthDamage { get; }
        public float ArmorDamage { get; }
        public float AppliedDamage => HealthDamage + ArmorDamage;
        public bool WasKilled => RemainingHealth <= 0f;
    }
}
