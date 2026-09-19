using System.Collections;
using FPS.Networking.Netcode;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

[RequireComponent(
    typeof(EnemyPerceptionController),
    typeof(EnemyNavigationController))]
public sealed class EnemyCombatController : MonoBehaviour
{
    private const int HitscanCapacity = 8;
    private static readonly Color EnemyTracerStart =
        new(1f, 0.12f, 0.05f, 1f);
    private static readonly Color EnemyTracerEnd =
        new(1f, 0.7f, 0.08f, 0.2f);

    [SerializeField, Min(0.5f)] private float attackRange = 2.8f;
    [SerializeField, Min(0f)] private float aimDuration = 0.55f;
    [SerializeField, Min(0.05f)] private float attackCooldown = 1.35f;
    [SerializeField, Min(1f)] private float turnSpeed = 540f;

    private EnemyController enemy;
    private EnemyPerceptionController perception;
    private EnemyNavigationController navigation;
    private EnemyAttackStateMachine attackState;
    private EnemyCombatPresentationProfile presentation;
    private Animator animator;
    private AudioSource audioSource;
    private PlayableGraph attackGraph;
    private Coroutine attackAnimationRoutine;
    private EnemyAbilityController abilities;
    private EnemyAiLodController lod;
    private EnemyVisualAnimator visualAnimator;
    private Transform cachedHealthTarget;
    private Health cachedTargetHealth;
    private ShotTracerPool rangedTracerPool;
    private readonly RaycastHit[] hitscanResults =
        new RaycastHit[HitscanCapacity];

    public EnemyAttackDecision Decision { get; private set; } =
        EnemyAttackDecision.Chase;
    public int SuccessfulAttackCount { get; private set; }

    private void Awake()
    {
        enemy = GetComponent<EnemyController>();
        perception = GetComponent<EnemyPerceptionController>();
        navigation = GetComponent<EnemyNavigationController>();
        abilities = GetComponent<EnemyAbilityController>();
        lod = GetComponent<EnemyAiLodController>();

        if (!DedicatedServerRuntime.IsActive)
        {
            animator = GetComponentInChildren<Animator>(true);
            audioSource = GetComponent<AudioSource>();
            visualAnimator = GetComponent<EnemyVisualAnimator>();

            if (audioSource == null)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
            }
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 1f;
            audioSource.minDistance = 1f;
            audioSource.maxDistance = 25f;
            presentation =
                Resources.Load<EnemyCombatPresentationProfile>(
                    "EnemyCombatPresentation");
        }
        attackState = new EnemyAttackStateMachine();
        RefreshAbilityProfile();
    }

    private void Update()
    {
        if (Time.timeScale <= 0f)
        {
            return;
        }

        Transform target = perception.Target;

        if (target == null ||
            perception.State != EnemyAwarenessState.Alert)
        {
            attackState.CancelAim();
            abilities?.EndEngagement();
            return;
        }

        if (cachedHealthTarget != target)
        {
            cachedHealthTarget = target;
            cachedTargetHealth = target.GetComponent<Health>();
        }

        Health targetHealth = cachedTargetHealth;

        if (targetHealth != null && targetHealth.IsDead)
        {
            attackState.CancelAim();
            abilities?.EndEngagement();
            navigation.Stop();
            return;
        }

        lod ??= GetComponent<EnemyAiLodController>();
        float elapsedTime = Time.deltaTime;

        if (lod != null && !lod.TryAcquireTick(
                EnemyAiLodChannel.Combat,
                out elapsedTime))
        {
            return;
        }

        Vector3 toTarget = target.position - transform.position;
        float distance = toTarget.magnitude;
        Vector3 knownTargetPosition = perception.HasVisualContact
            ? target.position
            : perception.HasSquadSearchAssignment
                ? perception.SquadSearchDestination
                : perception.LastKnownPosition;
        EnemyMovementDirective movement = abilities != null
            ? abilities.ResolveMovement(
                target,
                knownTargetPosition,
                perception.HasVisualContact,
                distance,
                elapsedTime)
            : EnemyMovementDirective.None;

        if (movement.Kind != EnemyMovementDirectiveKind.None)
        {
            attackState.CancelAim();
            Decision = EnemyAttackDecision.Chase;

            if (movement.Kind == EnemyMovementDirectiveKind.Move)
            {
                navigation.SetDestination(movement.Destination);
            }
            else
            {
                navigation.Stop();
            }

            return;
        }

        Decision = attackState.Evaluate(
            distance,
            perception.HasVisualContact,
            elapsedTime);
        abilities?.NotifyAttackDecision(Decision);

        if (Decision == EnemyAttackDecision.Chase)
        {
            navigation.SetDestination(knownTargetPosition);
            return;
        }

        navigation.Stop();
        FaceTarget(toTarget, elapsedTime);

        if (Decision == EnemyAttackDecision.Attack)
        {
            ApplyAttack(target);
        }
    }

    private void OnDisable()
    {
        PrepareForPool();
    }

    public void ResetForSpawn()
    {
        PrepareForPool();
        attackState ??= new EnemyAttackStateMachine();
        abilities = GetComponent<EnemyAbilityController>();
        RefreshAbilityProfile();
        Decision = EnemyAttackDecision.Chase;
        SuccessfulAttackCount = 0;
        cachedHealthTarget = null;
        cachedTargetHealth = null;
    }

    public void RefreshAbilityProfile()
    {
        abilities = GetComponent<EnemyAbilityController>();
        attackState ??= new EnemyAttackStateMachine();
        attackState.Configure(
            abilities != null && abilities.IsActive
                ? abilities.AttackRange
                : attackRange,
            abilities != null && abilities.IsActive
                ? abilities.WindupDuration
                : aimDuration,
            abilities != null && abilities.IsActive
                ? abilities.AttackCooldown
                : attackCooldown);
    }

    public void PrepareForPool()
    {
        if (attackAnimationRoutine != null)
        {
            StopCoroutine(attackAnimationRoutine);
            attackAnimationRoutine = null;
        }

        attackState?.CancelAim();
        abilities?.EndEngagement();
        navigation?.Stop();
        audioSource?.Stop();
        DestroyAttackGraph();
        Decision = EnemyAttackDecision.Chase;
    }

    private void FaceTarget(Vector3 direction, float elapsedTime)
    {
        direction.y = 0f;

        if (direction.sqrMagnitude <= 0.001f)
        {
            return;
        }

        transform.rotation = Quaternion.RotateTowards(
            transform.rotation,
            Quaternion.LookRotation(direction.normalized),
            turnSpeed * elapsedTime);
    }

    private void ApplyAttack(Transform target)
    {
        if (abilities != null &&
            abilities.AttackMode == EnemyAttackMode.Hitscan)
        {
            ApplyHitscanAttack(target);
            return;
        }

        Health targetHealth = target.GetComponent<Health>();

        if (targetHealth == null || targetHealth.IsDead)
        {
            return;
        }

        Vector3 direction =
            (target.position - transform.position).normalized;
        targetHealth.ApplyDamage(
            new DamageInfo(
                (enemy != null ? enemy.AttackDamage : 20f) *
                (abilities != null ? abilities.DamageMultiplier : 1f),
                target.position,
                direction,
                gameObject,
                DamageType.Melee));
        SuccessfulAttackCount++;
        PlayAttackPresentation();
    }

    private void ApplyHitscanAttack(Transform target)
    {
        Health targetHealth = target.GetComponent<Health>();

        if (targetHealth == null || targetHealth.IsDead)
        {
            return;
        }

        Vector3 targetPoint = target.position + Vector3.up * 0.7f;
        Vector3 rawDirection = targetPoint - transform.position;
        Vector3 direction = rawDirection.sqrMagnitude > 0.001f
            ? rawDirection.normalized
            : transform.forward;
        Vector3 origin = transform.position +
            Vector3.up * 0.7f + direction * 0.65f;
        Vector3 offset = targetPoint - origin;
        float castDistance = Mathf.Max(
            0.01f,
            Mathf.Min(offset.magnitude, abilities.AttackRange + 1f));
        direction = offset.sqrMagnitude > 0.001f
            ? offset.normalized
            : direction;
        int hitCount = Physics.RaycastNonAlloc(
            origin,
            direction,
            hitscanResults,
            castDistance,
            Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore);
        int nearestIndex = -1;
        float nearestDistance = float.PositiveInfinity;

        for (int index = 0; index < hitCount; index++)
        {
            Transform hitTransform = hitscanResults[index].transform;

            if (hitTransform == null ||
                hitTransform == transform ||
                hitTransform.IsChildOf(transform))
            {
                continue;
            }

            if (hitscanResults[index].distance < nearestDistance)
            {
                nearestIndex = index;
                nearestDistance = hitscanResults[index].distance;
            }
        }

        Vector3 endPoint = targetPoint;
        bool hitTarget = nearestIndex < 0;
        IDamageable hitDamageable = targetHealth;

        if (nearestIndex >= 0)
        {
            RaycastHit hit = hitscanResults[nearestIndex];
            endPoint = hit.point;
            hitDamageable = DamageableResolver.Find(hit.transform);
            Transform hitTransform = hit.transform;
            hitTarget = hitTransform == target ||
                hitTransform.IsChildOf(target) ||
                target.IsChildOf(hitTransform);
        }

        if (hitTarget)
        {
            DamageResult result = hitDamageable.ApplyDamage(
                new DamageInfo(
                    (enemy != null ? enemy.AttackDamage : 20f) *
                    abilities.DamageMultiplier,
                    endPoint,
                    direction,
                    gameObject,
                    DamageType.Hitscan));

            if (result.WasApplied)
            {
                SuccessfulAttackCount++;
            }
        }

        if (!DedicatedServerRuntime.IsActive)
        {
            EnsureRangedTracerPool();
            rangedTracerPool?.Play(
                origin,
                endPoint,
                abilities.TracerSpeed,
                EnemyTracerStart,
                EnemyTracerEnd);
        }
        PlayAttackPresentation();
    }

    private void EnsureRangedTracerPool()
    {
        if (rangedTracerPool != null)
        {
            return;
        }

        rangedTracerPool = FindAnyObjectByType<ShotTracerPool>();

        if (rangedTracerPool != null)
        {
            return;
        }

        var poolObject = new GameObject("Shared Shot Tracer Pool");
        rangedTracerPool = poolObject.AddComponent<ShotTracerPool>();
    }

    private void PlayAttackPresentation()
    {
        if (DedicatedServerRuntime.IsActive)
        {
            return;
        }

        if (presentation != null &&
            presentation.AttackImpact != null)
        {
            audioSource.PlayOneShot(presentation.AttackImpact);
        }

        visualAnimator ??= GetComponent<EnemyVisualAnimator>();

        if (visualAnimator != null)
        {
            visualAnimator.PlayAttack();
            return;
        }

        if (animator == null ||
            presentation == null ||
            presentation.AttackClip == null)
        {
            return;
        }

        if (attackAnimationRoutine != null)
        {
            StopCoroutine(attackAnimationRoutine);
        }

        DestroyAttackGraph();
        attackGraph = PlayableGraph.Create("Spider Attack");
        AnimationPlayableOutput output =
            AnimationPlayableOutput.Create(
                attackGraph,
                "Spider Attack Output",
                animator);
        AnimationClipPlayable playable =
            AnimationClipPlayable.Create(
                attackGraph,
                presentation.AttackClip);
        output.SetSourcePlayable(playable);
        attackGraph.Play();
        attackAnimationRoutine = StartCoroutine(
            StopAttackAnimation(
                presentation.AttackClip.length));
    }

    private IEnumerator StopAttackAnimation(float duration)
    {
        yield return new WaitForSeconds(duration);
        DestroyAttackGraph();
        attackAnimationRoutine = null;
    }

    private void DestroyAttackGraph()
    {
        if (attackGraph.IsValid())
        {
            attackGraph.Destroy();
        }
    }
}
