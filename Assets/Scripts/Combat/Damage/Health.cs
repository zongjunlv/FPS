using System;
using UnityEngine;

public sealed class Health : MonoBehaviour, IDamageable
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

        float armorBeforeDamage = CurrentArmor;
        float absorbedDamage = Mathf.Min(
            CurrentArmor,
            damage.Amount);
        CurrentArmor -= absorbedDamage;
        float healthDamage = damage.Amount - absorbedDamage;
        float healthBeforeDamage = CurrentHealth;
        CurrentHealth = Mathf.Max(
            0f,
            CurrentHealth - healthDamage);
        float appliedAmount =
            armorBeforeDamage - CurrentArmor +
            healthBeforeDamage - CurrentHealth;
        bool wasKilled = CurrentHealth <= 0f;
        var result = new DamageResult(
            true,
            wasKilled,
            appliedAmount,
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
