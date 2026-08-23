using UnityEngine;

public sealed class DamageHitbox : MonoBehaviour, IDamageable
{
    [SerializeField] private Health health;
    [SerializeField, Min(0f)] private float damageMultiplier = 1f;
    [SerializeField] private HitRegion hitRegion = HitRegion.Body;

    public float DamageMultiplier => damageMultiplier;
    public HitRegion Region => hitRegion;
    private Renderer visualRenderer;
    private Color baseColor;
    private float flashRemaining;

    private void Awake()
    {
        if (health == null)
        {
            health = GetComponentInParent<Health>();
        }

        visualRenderer = GetComponent<Renderer>();

        if (visualRenderer != null)
        {
            baseColor = visualRenderer.material.color;
        }
    }

    private void Update()
    {
        if (visualRenderer == null || flashRemaining <= 0f)
        {
            return;
        }

        flashRemaining -= Time.deltaTime;
        float blend = Mathf.Clamp01(flashRemaining / 0.12f);
        Color flashColor = hitRegion == HitRegion.Head
            ? new Color(1f, 0.72f, 0.08f)
            : Color.white;
        visualRenderer.material.color =
            Color.Lerp(baseColor, flashColor, blend);

        if (flashRemaining <= 0f)
        {
            visualRenderer.material.color = baseColor;
        }
    }

    public void Configure(
        Health targetHealth,
        float multiplier)
    {
        health = targetHealth;
        damageMultiplier = Mathf.Max(0f, multiplier);
        hitRegion = HitRegion.Body;
    }

    public void ConfigureRegion(
        Health targetHealth,
        float multiplier,
        HitRegion region)
    {
        health = targetHealth;
        damageMultiplier = Mathf.Max(0f, multiplier);
        hitRegion = region;
    }

    public DamageResult ApplyDamage(DamageInfo damage)
    {
        if (health == null)
        {
            return DamageResult.None;
        }

        DamageResult result = health.ApplyDamage(
            damage.WithAmount(
                damage.Amount * damageMultiplier))
            .WithRegion(hitRegion);

        if (result.WasApplied && visualRenderer != null)
        {
            flashRemaining = 0.12f;
        }

        return result;
    }

    public void ResetForSpawn()
    {
        flashRemaining = 0f;

        if (visualRenderer != null)
        {
            visualRenderer.material.color = baseColor;
        }
    }
}
