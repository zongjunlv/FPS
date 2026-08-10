using UnityEngine;

public class EnemyController : MonoBehaviour
{
    [SerializeField] private EnemyDefinition currentEnemy;
    [SerializeField] private GameObject bombEffect;

    [SerializeField, Min(1f)] private float headDamageMultiplier = 2f;

    private Health health;
    private bool deathPresentationTriggered;
    private bool factoryManaged;

    public float AttackDamage =>
        currentEnemy != null
            ? Mathf.Max(1f, currentEnemy.Attack)
            : 20f;

    public void SetFactoryManaged(bool managed)
    {
        factoryManaged = managed;
    }

    private void Awake()
    {
        EnsureAwareness();
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

    private void EnsureAwareness()
    {
        RuntimeNavMeshBootstrap.EnsureForActiveScene();
        EnemySquadCoordinator.EnsureForActiveScene();

        if (GetComponent<EnemyPerceptionController>() == null)
        {
            gameObject.AddComponent<EnemyPerceptionController>();
        }

        if (GetComponent<EnemyCombatController>() == null)
        {
            gameObject.AddComponent<EnemyCombatController>();
        }
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
            GameObject deathEffect = Instantiate(
                bombEffect,
                transform.position,
                Quaternion.identity);
            EnemyDeathEffectController controller =
                deathEffect.GetComponent<EnemyDeathEffectController>();

            if (controller == null)
            {
                controller =
                    deathEffect.AddComponent<EnemyDeathEffectController>();
            }

            controller.Configure();
        }

        if (!factoryManaged)
        {
            Destroy(gameObject);
        }
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
            1f,
            HitRegion.Body);

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
            headDamageMultiplier,
            HitRegion.Head);
    }

    private void CreateHitbox(
        string hitboxName,
        Vector3 center,
        Vector3 size,
        float multiplier,
        HitRegion region)
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
        hitbox.ConfigureRegion(health, multiplier, region);
    }
}
