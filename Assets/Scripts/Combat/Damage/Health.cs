using System;
using UnityEngine;

public sealed class Health : MonoBehaviour, IDamageable
{
    [SerializeField, Min(0.01f)] private float maxHealth = 100f;
    [SerializeField, Min(0f)] private float maxArmor;

    public event Action<DamageInfo> Damaged;
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
        LastAppliedDamage = damage;
        HasLastAppliedDamage = true;
        Damaged?.Invoke(damage);
        VitalsChanged?.Invoke();
        float appliedAmount =
            armorBeforeDamage - CurrentArmor +
            healthBeforeDamage - CurrentHealth;

        if (CurrentHealth > 0f)
        {
            return new DamageResult(
                true,
                false,
                appliedAmount,
                HitRegion.Generic);
        }

        IsDead = true;
        Killed?.Invoke(damage);
        Died?.Invoke();
        return new DamageResult(
            true,
            true,
            appliedAmount,
            HitRegion.Generic);
    }
}
