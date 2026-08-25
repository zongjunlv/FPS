using UnityEngine;

public class EnemyController : MonoBehaviour
{
    [SerializeField] private EnemyDefinition currentEnemy;
    [SerializeField] private GameObject bombEffect;

    [SerializeField, Min(1f)] private float headDamageMultiplier = 2f;

    private Health health;
    private bool deathPresentationTriggered;
    private bool factoryManaged;
    private Collider[] colliders;
    private bool[] colliderBaseline;
    private Rigidbody[] rigidbodies;
    private Animator animator;
    private float configuredHealth;
    private float configuredArmor;
    private EnemyAffixController affixController;
    private EnemyAbilityController abilityController;
    private EnemySupportEffectReceiver supportEffects;

    public int SpawnResetCount { get; private set; }
    public int PoolPreparationCount { get; private set; }

    public float BaseAttackDamage => currentEnemy != null
        ? Mathf.Max(1f, currentEnemy.Attack)
        : 20f;
    public int BaseRewardExperience =>
        currentEnemy != null && currentEnemy.RewardExperience > 0
            ? currentEnemy.RewardExperience
            : 40;
    public float AttackDamage
    {
        get
        {
            float value = affixController != null
                ? affixController.Evaluate(
                    FPS.GameplayEffects.GameplayAttributeId.EnemyAttackDamage,
                    BaseAttackDamage)
                : BaseAttackDamage;
            value = supportEffects != null
                ? supportEffects.Evaluate(
                    FPS.GameplayEffects.GameplayAttributeId.EnemyAttackDamage,
                    value)
                : value;
            return Mathf.Max(1f, value);
        }
    }
    public int RewardExperience => Mathf.Max(0, Mathf.RoundToInt(
        affixController != null
            ? affixController.Evaluate(
                FPS.GameplayEffects.GameplayAttributeId.EnemyExperienceReward,
                BaseRewardExperience)
            : BaseRewardExperience));
    public float LootQuantityMultiplier => affixController != null
        ? Mathf.Max(1f, affixController.LootQuantityMultiplier)
        : 1f;
    public EnemyAffixController AffixController => affixController;
    public EnemyAbilityController AbilityController => abilityController;
    public EnemySupportEffectReceiver SupportEffects => supportEffects;

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

        configuredHealth =
            currentEnemy != null ? currentEnemy.HP : health.MaxHealth;
        configuredArmor = health.MaxArmor;
        health.Initialize(configuredHealth, configuredArmor);
        health.Died += HandleDeath;
        EnsureBurnEffects();
        EnsureSupportEffects();
        EnsureAbilities();
        EnsureAffixes();
        EnsureHitboxes();
        CacheRuntimeBaseline();
    }

    private void EnsureAwareness()
    {
        RuntimeNavMeshBootstrap.EnsureForActiveScene();
        EnemySquadCoordinator.EnsureForActiveScene();

        if (GetComponent<EnemyAiLodController>() == null)
        {
            gameObject.AddComponent<EnemyAiLodController>();
        }

        if (GetComponent<EnemyPerceptionController>() == null)
        {
            gameObject.AddComponent<EnemyPerceptionController>();
        }

        if (GetComponent<EnemyCombatController>() == null)
        {
            gameObject.AddComponent<EnemyCombatController>();
        }
    }

    private void EnsureBurnEffects()
    {
        if (GetComponent<EnemyBurnEffectController>() == null)
        {
            gameObject.AddComponent<EnemyBurnEffectController>();
        }
    }

    private void EnsureAffixes()
    {
        affixController = GetComponent<EnemyAffixController>();
        affixController ??= gameObject.AddComponent<EnemyAffixController>();
    }

    private void EnsureSupportEffects()
    {
        supportEffects = GetComponent<EnemySupportEffectReceiver>();
        supportEffects ??=
            gameObject.AddComponent<EnemySupportEffectReceiver>();
    }

    private void EnsureAbilities()
    {
        abilityController = GetComponent<EnemyAbilityController>();
        abilityController ??= gameObject.AddComponent<EnemyAbilityController>();
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

    public void ResetForSpawn(Transform target)
    {
        deathPresentationTriggered = false;
        abilityController?.ClearForPool();
        supportEffects?.ClearAll();
        affixController?.ClearAffix(false);
        EnemyBurnEffectController burnEffects =
            GetComponent<EnemyBurnEffectController>();
        burnEffects?.ClearBurn();
        burnEffects?.SetOverheadPresentationEnabled(true);
        RestoreColliderBaseline();

        foreach (Rigidbody body in rigidbodies)
        {
            if (body == null)
            {
                continue;
            }

            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }

        if (animator != null)
        {
            animator.Rebind();
            animator.Update(0f);
        }

        health.Initialize(configuredHealth, configuredArmor);
        GetComponent<EnemyAiLodController>()?.ResetForSpawn(target);
        GetComponent<EnemyCombatController>()?.ResetForSpawn();
        GetComponent<EnemyNavigationController>()?.ResetForSpawn();
        GetComponent<EnemyPerceptionController>()?.ResetForSpawn(target);
        EnemySpatialIndexService spatialIndex =
            EnemySpatialIndexService.Instance;

        if (gameObject.activeInHierarchy && spatialIndex != null)
        {
            spatialIndex.Synchronize(
                GetComponent<EnemyPerceptionController>());
        }

        foreach (DamageHitbox hitbox in
                 GetComponentsInChildren<DamageHitbox>(true))
        {
            hitbox.ResetForSpawn();
        }

        SpawnResetCount++;
    }

    public void PrepareForPool()
    {
        EnemySpatialIndexService.Instance?.Unregister(
            GetComponent<EnemyPerceptionController>());
        abilityController?.ClearForPool();
        supportEffects?.ClearAll();
        affixController?.ClearAffix();
        EnemyBurnEffectController burnEffects =
            GetComponent<EnemyBurnEffectController>();
        burnEffects?.ClearBurn();
        burnEffects?.SetOverheadPresentationEnabled(false);
        GetComponent<EnemyAiLodController>()?.PrepareForPool();
        GetComponent<EnemyCombatController>()?.PrepareForPool();
        GetComponent<EnemyNavigationController>()?.PrepareForPool();
        GetComponent<EnemyPerceptionController>()?.PrepareForPool();

        if (colliders != null)
        {
            foreach (Collider collider in colliders)
            {
                if (collider != null)
                {
                    collider.enabled = false;
                }
            }
        }

        PoolPreparationCount++;
    }

    public bool ApplyAffix(EnemyAffixDefinition definition)
    {
        EnsureAffixes();
        return affixController.ApplyAffix(definition, configuredArmor);
    }

    public bool ApplyAbilitySet(
        EnemyAbilitySetDefinition definition,
        Transform target)
    {
        EnsureAbilities();
        bool applied = abilityController.ApplyAbilitySet(
            definition,
            target);
        GetComponent<EnemyCombatController>()?.RefreshAbilityProfile();
        return applied;
    }

    public bool HasGameplayTag(string gameplayTag)
    {
        string tag = string.IsNullOrWhiteSpace(gameplayTag)
            ? "enemy"
            : gameplayTag.Trim();

        if (tag == "*" || tag == "enemy" || tag == "enemy.alive")
        {
            return true;
        }

        WaveEnemyLifecycle lifecycle =
            GetComponent<WaveEnemyLifecycle>();
        string typeId = lifecycle != null && lifecycle.IsArmed
            ? lifecycle.EnemyTypeId
            : "*";

        if (string.Equals(tag, typeId, System.StringComparison.Ordinal) ||
            string.Equals(
                tag,
                $"enemy.type.{typeId}",
                System.StringComparison.Ordinal))
        {
            return true;
        }

        string roleId = abilityController?.ActiveSet?.StableId;
        return !string.IsNullOrWhiteSpace(roleId) &&
            string.Equals(tag, roleId, System.StringComparison.Ordinal);
    }

    private void CacheRuntimeBaseline()
    {
        colliders = GetComponentsInChildren<Collider>(true);
        colliderBaseline = new bool[colliders.Length];

        for (int index = 0; index < colliders.Length; index++)
        {
            colliderBaseline[index] = colliders[index].enabled;
        }

        rigidbodies = GetComponentsInChildren<Rigidbody>(true);
        animator = GetComponent<Animator>();
    }

    private void RestoreColliderBaseline()
    {
        if (colliders == null || colliderBaseline == null)
        {
            CacheRuntimeBaseline();
        }

        for (int index = 0; index < colliders.Length; index++)
        {
            if (colliders[index] != null)
            {
                colliders[index].enabled = colliderBaseline[index];
            }
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
