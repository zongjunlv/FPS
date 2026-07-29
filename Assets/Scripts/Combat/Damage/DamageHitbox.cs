using UnityEngine;

public sealed class DamageHitbox : MonoBehaviour, IDamageable
{
    [SerializeField] private Health health;
    [SerializeField, Min(0f)] private float damageMultiplier = 1f;

    public float DamageMultiplier => damageMultiplier;

    private void Awake()
    {
        if (health == null)
        {
            health = GetComponentInParent<Health>();
        }
    }

    public void Configure(
        Health targetHealth,
        float multiplier)
    {
        health = targetHealth;
        damageMultiplier = Mathf.Max(0f, multiplier);
    }

    public bool ApplyDamage(DamageInfo damage)
    {
        if (health == null)
        {
            return false;
        }

        return health.ApplyDamage(
            damage.WithAmount(
                damage.Amount * damageMultiplier));
    }
}
