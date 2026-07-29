using System;
using UnityEngine;

public sealed class Health : MonoBehaviour, IDamageable
{
    [SerializeField, Min(0.01f)] private float maxHealth = 100f;

    public event Action<DamageInfo> Damaged;
    public event Action Died;

    public float MaxHealth { get; private set; }
    public float CurrentHealth { get; private set; }
    public bool IsDead { get; private set; }

    private void Awake()
    {
        Initialize(maxHealth);
    }

    public void Initialize(float newMaxHealth)
    {
        MaxHealth = Mathf.Max(0.01f, newMaxHealth);
        CurrentHealth = MaxHealth;
        IsDead = false;
    }

    public bool ApplyDamage(DamageInfo damage)
    {
        if (IsDead || damage.Amount <= 0f)
        {
            return false;
        }

        CurrentHealth = Mathf.Max(
            0f,
            CurrentHealth - damage.Amount);
        Damaged?.Invoke(damage);

        if (CurrentHealth > 0f)
        {
            return true;
        }

        IsDead = true;
        Died?.Invoke();
        return true;
    }
}
