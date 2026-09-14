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
    [SerializeField] private bool useProceduralHoverLocomotion;
    [SerializeField, Min(0f)] private float hoverBobAmplitude = 0.035f;
    [SerializeField, Min(0.1f)] private float hoverBobFrequency = 4.5f;
    [SerializeField, Range(0f, 12f)] private float hoverMoveTiltDegrees = 4f;

    private Animator animator;
    private NavMeshAgent agent;
    private Transform visualRoot;
    private Vector3 visualBaseLocalPosition;
    private Quaternion visualBaseLocalRotation;
    private int activeState;
    private float attackUntil;
    private float hoverPhase;
    private bool resetWhenEnabled;
    private bool visualBaselineCaptured;
    private bool runtimeUsesProceduralLocomotion;
    private bool proceduralMovementActive;

    public bool IsAttacking => Time.time < attackUntil;
    public bool UsesProceduralLocomotion =>
        runtimeUsesProceduralLocomotion;
    public bool IsProceduralMovementActive => proceduralMovementActive;
    public float HoverBobAmplitude => hoverBobAmplitude;
    public float HoverBobFrequency => hoverBobFrequency;
    public float HoverMoveTiltDegrees => hoverMoveTiltDegrees;

    private void Awake()
    {
        animator = GetComponentInChildren<Animator>(true);
        agent = GetComponent<NavMeshAgent>();
        ResolveVisualRoot();
        runtimeUsesProceduralLocomotion =
            useProceduralHoverLocomotion ||
            AnimatorHasNoDistinctLocomotionClip();
        ApplyState(IdleState, 0f, true);
    }

    private void OnEnable()
    {
        if (!resetWhenEnabled)
        {
            return;
        }

        resetWhenEnabled = false;
        ResetProceduralLocomotion();
        ApplyState(IdleState, 0f, true);
    }

    private void Update()
    {
        if (Time.timeScale <= 0f)
        {
            return;
        }

        if (IsAttacking)
        {
            ApplyProceduralLocomotion(
                false,
                Vector3.zero,
                Time.deltaTime);
            return;
        }

        Vector3 velocity = agent != null && agent.enabled &&
            agent.isOnNavMesh
            ? agent.velocity
            : Vector3.zero;
        UpdateLocomotionFeedback(velocity, Time.deltaTime);
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
        ResetProceduralLocomotion();

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
        ResetProceduralLocomotion();
    }

    public void Configure(float configuredAttackDuration)
    {
        attackDuration = Mathf.Max(0.05f, configuredAttackDuration);
    }

    public void ConfigureProceduralLocomotion(
        bool enabled,
        float bobAmplitude = 0.035f,
        float bobFrequency = 4.5f,
        float moveTiltDegrees = 4f)
    {
        useProceduralHoverLocomotion = enabled;
        hoverBobAmplitude = Mathf.Max(0f, bobAmplitude);
        hoverBobFrequency = Mathf.Max(0.1f, bobFrequency);
        hoverMoveTiltDegrees = Mathf.Clamp(moveTiltDegrees, 0f, 12f);
        runtimeUsesProceduralLocomotion = enabled ||
            AnimatorHasNoDistinctLocomotionClip();
        ResetProceduralLocomotion();
    }

    public void UpdateLocomotionFeedback(
        Vector3 worldVelocity,
        float deltaTime)
    {
        bool moving = worldVelocity.sqrMagnitude >
            movingThreshold * movingThreshold;
        ApplyState(moving ? RunState : IdleState, locomotionBlendDuration);
        ApplyProceduralLocomotion(
            moving,
            worldVelocity,
            Mathf.Max(0f, deltaTime));
    }

    private void ResolveVisualRoot()
    {
        visualRoot = transform.Find("Visual");

        if (visualRoot == null && animator != null)
        {
            visualRoot = animator.transform;
        }

        if (visualRoot == null || visualBaselineCaptured)
        {
            return;
        }

        visualBaseLocalPosition = visualRoot.localPosition;
        visualBaseLocalRotation = visualRoot.localRotation;
        visualBaselineCaptured = true;
    }

    private bool AnimatorHasNoDistinctLocomotionClip()
    {
        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>(true);
        }

        AnimationClip[] clips = animator != null &&
            animator.runtimeAnimatorController != null
            ? animator.runtimeAnimatorController.animationClips
            : null;

        if (clips == null || clips.Length == 0)
        {
            return false;
        }

        // Controllers built from a model with no Run/Walk contain only the
        // idle and attack motions. Procedural hover provides locomotion
        // feedback without borrowing an attack or charge animation.
        for (int index = 0; index < clips.Length; index++)
        {
            string clipName = clips[index] != null
                ? clips[index].name
                : string.Empty;

            if (clipName.IndexOf(
                    "run",
                    System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                clipName.IndexOf(
                    "walk",
                    System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return false;
            }
        }

        return true;
    }

    private void ApplyProceduralLocomotion(
        bool moving,
        Vector3 worldVelocity,
        float deltaTime)
    {
        if (!runtimeUsesProceduralLocomotion)
        {
            proceduralMovementActive = false;
            return;
        }

        ResolveVisualRoot();

        if (!visualBaselineCaptured || visualRoot == null)
        {
            proceduralMovementActive = false;
            return;
        }

        proceduralMovementActive = moving;
        float safeDeltaTime = Mathf.Max(0f, deltaTime);

        if (moving)
        {
            hoverPhase = Mathf.Repeat(
                hoverPhase + safeDeltaTime * hoverBobFrequency *
                Mathf.PI * 2f,
                Mathf.PI * 2f);
            Vector3 localDirection = transform.InverseTransformDirection(
                worldVelocity.normalized);
            float bob = Mathf.Sin(hoverPhase) * hoverBobAmplitude;
            Quaternion tilt = Quaternion.Euler(
                -localDirection.z * hoverMoveTiltDegrees * 0.35f,
                0f,
                -localDirection.x * hoverMoveTiltDegrees);
            visualRoot.localPosition = visualBaseLocalPosition +
                Vector3.up * bob;
            visualRoot.localRotation = visualBaseLocalRotation * tilt;
            return;
        }

        float positionSpeed = safeDeltaTime * 10f;
        float rotationBlend = 1f - Mathf.Exp(-safeDeltaTime * 12f);
        visualRoot.localPosition = Vector3.MoveTowards(
            visualRoot.localPosition,
            visualBaseLocalPosition,
            positionSpeed);
        visualRoot.localRotation = Quaternion.Slerp(
            visualRoot.localRotation,
            visualBaseLocalRotation,
            rotationBlend);
    }

    private void ResetProceduralLocomotion()
    {
        hoverPhase = 0f;
        proceduralMovementActive = false;
        ResolveVisualRoot();

        if (!visualBaselineCaptured || visualRoot == null)
        {
            return;
        }

        visualRoot.localPosition = visualBaseLocalPosition;
        visualRoot.localRotation = visualBaseLocalRotation;
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
