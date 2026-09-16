using System;
using FPS.GameplayEffects;
using FPS.Simulation;
using UnityEngine;

public sealed class Health : MonoBehaviour, IDamageable,
    IGameplayEffectAttributeTarget
{
    [SerializeField, Min(0.01f)] private float maxHealth = 100f;
    [SerializeField, Min(0f)] private float maxArmor;

    public event Action<DamageInfo> Damaged;
    public event Action<DamageResult> DamageApplied;
    public event Action<DamageInfo> Killed;
    public event Action Died;
    public event Action VitalsChanged;

    public float MaxHealth { get; private set; }
    public float CurrentHealth { get; private set; }
    public float MaxArmor { get; private set; }
    public float CurrentArmor { get; private set; }
    public bool IsDead { get; private set; }
    public bool HasLastAppliedDamage { get; private set; }
    public DamageInfo LastAppliedDamage { get; private set; }

    private void Awake()
    {
        Initialize(maxHealth);
    }

    public void Initialize(float newMaxHealth)
    {
        Initialize(newMaxHealth, 0f);
    }

    public void Initialize(
        float newMaxHealth,
        float newMaxArmor)
    {
        MaxHealth = Mathf.Max(0.01f, newMaxHealth);
        CurrentHealth = MaxHealth;
        MaxArmor = Mathf.Max(0f, newMaxArmor);
        CurrentArmor = MaxArmor;
        IsDead = false;
        HasLastAppliedDamage = false;
        LastAppliedDamage = default;
        VitalsChanged?.Invoke();
    }

    public DamageResult ApplyDamage(DamageInfo damage)
    {
        if (IsDead || damage.Amount <= 0f)
        {
            return DamageResult.None;
        }

        CombatDamageResolution resolution = CombatDamageRules.Apply(
            CurrentHealth,
            CurrentArmor,
            damage.Amount);
        CurrentHealth = resolution.RemainingHealth;
        CurrentArmor = resolution.RemainingArmor;
        bool wasKilled = resolution.WasKilled;
        var result = new DamageResult(
            true,
            wasKilled,
            resolution.AppliedDamage,
            HitRegion.Generic);
        LastAppliedDamage = damage;
        HasLastAppliedDamage = true;
        Damaged?.Invoke(damage);
        DamageApplied?.Invoke(result);
        VitalsChanged?.Invoke();

        if (!wasKilled)
        {
            return result;
        }

        IsDead = true;
        Killed?.Invoke(damage);
        Died?.Invoke();
        return result;
    }

    public float RestoreHealth(float amount)
    {
        return RestoreValue(
            amount,
            CurrentHealth,
            MaxHealth,
            value => CurrentHealth = value);
    }

    public bool TryRestoreSnapshotVitals(float health, float armor)
    {
        if (!IsFinite(health) || !IsFinite(armor) || health <= 0f ||
            health > MaxHealth || armor < 0f || armor > MaxArmor) return false;
        CurrentHealth = health;
        CurrentArmor = armor;
        IsDead = false;
        HasLastAppliedDamage = false;
        LastAppliedDamage = default;
        VitalsChanged?.Invoke();
        return true;
    }

    public bool ReconcileAuthoritativeVitals(
        float maximumHealth,
        float health,
        float maximumArmor,
        float armor,
        bool alive)
    {
        if (!IsFinite(maximumHealth) || maximumHealth <= 0f ||
            !IsFinite(health) || health < 0f || health > maximumHealth ||
            !IsFinite(maximumArmor) || maximumArmor < 0f ||
            !IsFinite(armor) || armor < 0f || armor > maximumArmor)
            return false;
        MaxHealth = maximumHealth;
        CurrentHealth = health;
        MaxArmor = maximumArmor;
        CurrentArmor = armor;
        IsDead = !alive || health <= 0f;
        HasLastAppliedDamage = false;
        LastAppliedDamage = default;
        VitalsChanged?.Invoke();
        return true;
    }

    public float RestoreArmor(float amount)
    {
        return RestoreValue(
            amount,
            CurrentArmor,
            MaxArmor,
            value => CurrentArmor = value);
    }

    public bool SetMaximumHealth(float value)
    {
        if (IsDead || !IsFinite(value))
        {
            return false;
        }

        float nextMaximum = Mathf.Max(0.01f, value);

        if (Mathf.Approximately(nextMaximum, MaxHealth))
        {
            return false;
        }

        float delta = nextMaximum - MaxHealth;
        MaxHealth = nextMaximum;
        CurrentHealth = Mathf.Clamp(CurrentHealth + delta, 0f, MaxHealth);
        VitalsChanged?.Invoke();
        return true;
    }

    public bool SetMaximumArmor(float value)
    {
        if (IsDead || !IsFinite(value))
        {
            return false;
        }

        float nextMaximum = Mathf.Max(0f, value);

        if (Mathf.Approximately(nextMaximum, MaxArmor))
        {
            return false;
        }

        float delta = nextMaximum - MaxArmor;
        MaxArmor = nextMaximum;
        CurrentArmor = Mathf.Clamp(CurrentArmor + delta, 0f, MaxArmor);
        VitalsChanged?.Invoke();
        return true;
    }

    public bool TryGetGameplayAttribute(
        GameplayAttributeId attribute,
        out float currentValue,
        out float minimumValue,
        out float maximumValue)
    {
        switch (attribute)
        {
            case GameplayAttributeId.CurrentHealth:
                currentValue = CurrentHealth;
                minimumValue = 0f;
                maximumValue = MaxHealth;
                return !IsDead;
            case GameplayAttributeId.CurrentArmor:
                currentValue = CurrentArmor;
                minimumValue = 0f;
                maximumValue = MaxArmor;
                return !IsDead;
            default:
                currentValue = 0f;
                minimumValue = 0f;
                maximumValue = 0f;
                return false;
        }
    }

    public bool TrySetGameplayAttribute(
        GameplayAttributeId attribute,
        float value,
        out float appliedAmount)
    {
        appliedAmount = 0f;

        if (IsDead || !IsFinite(value))
        {
            return false;
        }

        switch (attribute)
        {
            case GameplayAttributeId.CurrentHealth:
                float nextHealth = Mathf.Clamp(value, 0f, MaxHealth);
                appliedAmount = nextHealth - CurrentHealth;
                CurrentHealth = nextHealth;
                break;
            case GameplayAttributeId.CurrentArmor:
                float nextArmor = Mathf.Clamp(value, 0f, MaxArmor);
                appliedAmount = nextArmor - CurrentArmor;
                CurrentArmor = nextArmor;
                break;
            default:
                return false;
        }

        if (!Mathf.Approximately(appliedAmount, 0f))
        {
            VitalsChanged?.Invoke();
        }

        return true;
    }

    private float RestoreValue(
        float amount,
        float current,
        float maximum,
        Action<float> assign)
    {
        if (IsDead || !IsFinite(amount) || amount <= 0f ||
            current >= maximum)
        {
            return 0f;
        }

        float restored = Mathf.Min(amount, maximum - current);
        assign(current + restored);
        VitalsChanged?.Invoke();
        return restored;
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
