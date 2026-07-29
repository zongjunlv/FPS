using UnityEngine;

public class EnemyController : MonoBehaviour
{
    [SerializeField] private EnemyDefinition currentEnemy;
    [SerializeField] private GameObject bombEffect;

    [SerializeField, Min(1f)] private float headDamageMultiplier = 2f;

    private Health health;
    private bool deathPresentationTriggered;

    private void Awake()
    {
        health = GetComponent<Health>();

        if (health == null)
        {
            health = gameObject.AddComponent<Health>();
        }

        float configuredHealth =
            currentEnemy != null ? currentEnemy.HP : health.MaxHealth;
        health.Initialize(configuredHealth);
        health.Died += HandleDeath;
        EnsureHitboxes();
    }

    private void OnDestroy()
    {
        if (health != null)
        {
            health.Died -= HandleDeath;
        }
    }

    private void HandleDeath()
    {
        if (deathPresentationTriggered)
        {
            return;
        }

        deathPresentationTriggered = true;

        if (bombEffect != null)
        {
            Instantiate(bombEffect, transform.position, transform.rotation);
        }

        Destroy(gameObject);
    }

    private void EnsureHitboxes()
    {
        if (GetComponentInChildren<DamageHitbox>(true) != null)
        {
            return;
        }

        BoxCollider legacyCollider = GetComponent<BoxCollider>();

        if (legacyCollider == null)
        {
            return;
        }

        Vector3 originalSize = legacyCollider.size;
        Vector3 originalCenter = legacyCollider.center;
        legacyCollider.enabled = false;

        Vector3 bodySize = originalSize;
        bodySize.y *= 0.68f;
        Vector3 bodyCenter = originalCenter;
        bodyCenter.y -= originalSize.y * 0.16f;
        CreateHitbox(
            "Body Hitbox",
            bodyCenter,
            bodySize,
            1f);

        Vector3 headSize = originalSize;
        headSize.x *= 0.75f;
        headSize.y *= 0.32f;
        headSize.z *= 0.75f;
        Vector3 headCenter = originalCenter;
        headCenter.y += originalSize.y * 0.34f;
        CreateHitbox(
            "Head Hitbox",
            headCenter,
            headSize,
            headDamageMultiplier);
    }

    private void CreateHitbox(
        string hitboxName,
        Vector3 center,
        Vector3 size,
        float multiplier)
    {
        var hitboxObject = new GameObject(hitboxName);
        hitboxObject.layer = gameObject.layer;
        hitboxObject.transform.SetParent(transform, false);

        BoxCollider collider =
            hitboxObject.AddComponent<BoxCollider>();
        collider.center = center;
        collider.size = size;

        DamageHitbox hitbox =
            hitboxObject.AddComponent<DamageHitbox>();
        hitbox.Configure(health, multiplier);
    }
}
