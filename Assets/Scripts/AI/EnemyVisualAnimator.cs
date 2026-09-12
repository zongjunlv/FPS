using UnityEngine;
using UnityEngine.AI;

[DisallowMultipleComponent]
public sealed class EnemyVisualAnimator : MonoBehaviour
{
    private static readonly int IdleState = Animator.StringToHash("Idle");
    private static readonly int RunState = Animator.StringToHash("Run");
    private static readonly int AttackState = Animator.StringToHash("Attack");

    [SerializeField, Min(0.01f)] private float movingThreshold = 0.08f;
    [SerializeField, Min(0f)] private float locomotionBlendDuration = 0.12f;
    [SerializeField, Min(0f)] private float attackBlendDuration = 0.06f;
    [SerializeField, Min(0.05f)] private float attackDuration = 0.6f;

    private Animator animator;
    private NavMeshAgent agent;
    private int activeState;
    private float attackUntil;
    private bool resetWhenEnabled;

    public bool IsAttacking => Time.time < attackUntil;

    private void Awake()
    {
        animator = GetComponentInChildren<Animator>(true);
        agent = GetComponent<NavMeshAgent>();
        ApplyState(IdleState, 0f, true);
    }

    private void OnEnable()
    {
        if (!resetWhenEnabled)
        {
            return;
        }

        resetWhenEnabled = false;
        ApplyState(IdleState, 0f, true);
    }

    private void Update()
    {
        if (Time.timeScale <= 0f || animator == null)
        {
            return;
        }

        if (IsAttacking)
        {
            return;
        }

        bool moving = agent != null && agent.enabled &&
            agent.isOnNavMesh &&
            agent.velocity.sqrMagnitude > movingThreshold * movingThreshold;
        ApplyState(moving ? RunState : IdleState, locomotionBlendDuration);
    }

    public void PlayAttack()
    {
        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>(true);
        }

        attackUntil = Time.time + attackDuration;
        ApplyState(AttackState, attackBlendDuration, true);
    }

    public void ResetForSpawn()
    {
        attackUntil = 0f;
        activeState = 0;

        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>(true);
        }

        if (animator != null && animator.isActiveAndEnabled)
        {
            ApplyState(IdleState, 0f, true);
        }
        else
        {
            resetWhenEnabled = true;
        }
    }

    public void PrepareForPool()
    {
        attackUntil = 0f;
        activeState = 0;
    }

    public void Configure(float configuredAttackDuration)
    {
        attackDuration = Mathf.Max(0.05f, configuredAttackDuration);
    }

    private void ApplyState(int stateHash, float blendDuration, bool force = false)
    {
        if (animator == null || !animator.isActiveAndEnabled ||
            (!force && activeState == stateHash))
        {
            return;
        }

        if (blendDuration <= 0f)
        {
            animator.Play(stateHash, 0, 0f);
        }
        else
        {
            animator.CrossFade(stateHash, blendDuration, 0);
        }

        activeState = stateHash;
    }
}
