using System.Collections;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

[RequireComponent(
    typeof(EnemyPerceptionController),
    typeof(EnemyNavigationController))]
public sealed class EnemyCombatController : MonoBehaviour
{
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

    public EnemyAttackDecision Decision { get; private set; } =
        EnemyAttackDecision.Chase;
    public int SuccessfulAttackCount { get; private set; }

    private void Awake()
    {
        enemy = GetComponent<EnemyController>();
        perception = GetComponent<EnemyPerceptionController>();
        navigation = GetComponent<EnemyNavigationController>();
        animator = GetComponent<Animator>();
        audioSource = GetComponent<AudioSource>();

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
        attackState = new EnemyAttackStateMachine();
        attackState.Configure(
            attackRange,
            aimDuration,
            attackCooldown);
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
            return;
        }

        Vector3 toTarget = target.position - transform.position;
        float distance = toTarget.magnitude;
        Decision = attackState.Evaluate(
            distance,
            perception.HasVisualContact,
            Time.deltaTime);

        if (Decision == EnemyAttackDecision.Chase)
        {
            Vector3 chaseDestination = perception.HasVisualContact
                ? target.position
                : perception.HasSquadSearchAssignment
                    ? perception.SquadSearchDestination
                    : perception.LastKnownPosition;
            navigation.SetDestination(chaseDestination);
            return;
        }

        navigation.Stop();
        FaceTarget(toTarget);

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
        attackState.Configure(attackRange, aimDuration, attackCooldown);
        Decision = EnemyAttackDecision.Chase;
        SuccessfulAttackCount = 0;
    }

    public void PrepareForPool()
    {
        if (attackAnimationRoutine != null)
        {
            StopCoroutine(attackAnimationRoutine);
            attackAnimationRoutine = null;
        }

        attackState?.CancelAim();
        navigation?.Stop();
        audioSource?.Stop();
        DestroyAttackGraph();
        Decision = EnemyAttackDecision.Chase;
    }

    private void FaceTarget(Vector3 direction)
    {
        direction.y = 0f;

        if (direction.sqrMagnitude <= 0.001f)
        {
            return;
        }

        transform.rotation = Quaternion.RotateTowards(
            transform.rotation,
            Quaternion.LookRotation(direction.normalized),
            turnSpeed * Time.deltaTime);
    }

    private void ApplyAttack(Transform target)
    {
        Health targetHealth = target.GetComponent<Health>();

        if (targetHealth == null || targetHealth.IsDead)
        {
            return;
        }

        Vector3 direction =
            (target.position - transform.position).normalized;
        targetHealth.ApplyDamage(
            new DamageInfo(
                enemy != null ? enemy.AttackDamage : 20f,
                target.position,
                direction,
                gameObject,
                DamageType.Melee));
        SuccessfulAttackCount++;
        PlayAttackPresentation();
    }

    private void PlayAttackPresentation()
    {
        if (presentation != null &&
            presentation.AttackImpact != null)
        {
            audioSource.PlayOneShot(presentation.AttackImpact);
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
