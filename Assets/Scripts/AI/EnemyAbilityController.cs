using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(EnemyNavigationController))]
public sealed class EnemyAbilityController : MonoBehaviour
{
    private const float ProgressThreshold = 0.15f;

    private readonly RaiderTacticsStateMachine tactics = new();
    private EnemyNavigationController navigation;
    private EnemyBurnEffectController overhead;
    private EnemyController enemy;
    private EnemyAbilitySetDefinition activeSet;
    private RaiderApproachAbilityDefinition raider;
    private Transform target;
    private Vector3 flankDestination;
    private Vector3 lastProgressPosition;
    private bool hasFlankDestination;
    private bool hasCommittedCharge;
    private bool preferRightFlank;
    private float noProgressElapsed;

    public bool IsActive => activeSet != null && raider != null;
    public EnemyAbilitySetDefinition ActiveSet => activeSet;
    public Transform ActiveTarget => target;
    public RaiderTacticsPhase Phase => tactics.Phase;
    public Vector3 FlankDestination => flankDestination;
    public bool HasFlankDestination => hasFlankDestination;
    public int SuccessfulFlankSelections { get; private set; }
    public int PathFailureCount { get; private set; }
    public float AttackRange => IsActive ? raider.AttackRange : 2.8f;
    public float WindupDuration => IsActive ? raider.WindupDuration : 0.55f;
    public float AttackCooldown => IsActive ? raider.AttackCooldown : 1.35f;
    public float DamageMultiplier => IsActive ? raider.DamageMultiplier : 1f;

    private void Awake()
    {
        navigation = GetComponent<EnemyNavigationController>();
        overhead = GetComponent<EnemyBurnEffectController>();
        enemy = GetComponent<EnemyController>();
        tactics.Reset(false);
    }

    public bool ApplyAbilitySet(
        EnemyAbilitySetDefinition definition,
        Transform newTarget)
    {
        ClearForPool();

        if (definition == null)
        {
            return false;
        }

        RaiderApproachAbilityDefinition approach =
            definition.FindAbility<RaiderApproachAbilityDefinition>();

        if (approach == null)
        {
            return false;
        }

        activeSet = definition;
        raider = approach;
        target = newTarget;
        preferRightFlank = enemy != null &&
            enemy.SpawnResetCount % 2 == 0;
        tactics.Reset(true);
        EnsureOverhead();
        overhead?.SetRoleStatus(
            definition.StatusLabel,
            definition.StatusColor);
        navigation?.SetSpeedMultiplier(raider.FlankSpeedMultiplier);
        return true;
    }

    public bool TryResolveChaseDestination(
        Transform chaseTarget,
        bool hasVisualContact,
        float distance,
        float deltaTime,
        out Vector3 destination)
    {
        destination = chaseTarget != null
            ? chaseTarget.position
            : transform.position;

        if (!IsActive || chaseTarget == null)
        {
            EndEngagement();
            return false;
        }

        target = chaseTarget;

        if (!hasVisualContact &&
            !hasFlankDestination &&
            !hasCommittedCharge)
        {
            EndEngagement();
            return false;
        }

        if (tactics.Phase == RaiderTacticsPhase.Regrouping &&
            !tactics.TickRegroup(deltaTime))
        {
            navigation.SetSpeedMultiplier(1f);
            return false;
        }

        if (hasFlankDestination)
        {
            if (navigation.HasReachedDestination)
            {
                BeginCharge();
                destination = chaseTarget.position;
                return true;
            }

            if (HasStoppedMakingProgress(deltaTime))
            {
                FailFlank();
                return false;
            }

            tactics.BeginFlank();
            navigation.SetSpeedMultiplier(raider.FlankSpeedMultiplier);
            destination = flankDestination;
            return true;
        }

        if (hasCommittedCharge)
        {
            BeginCharge();
            destination = chaseTarget.position;
            return true;
        }

        if (distance <= raider.ChargeDistance)
        {
            BeginCharge();
            destination = chaseTarget.position;
            return true;
        }

        if (!TrySelectFlank(chaseTarget, out destination))
        {
            FailFlank();
            return false;
        }

        return true;
    }

    public void NotifyAttackDecision(EnemyAttackDecision decision)
    {
        if (!IsActive)
        {
            return;
        }

        tactics.ApplyAttackDecision(decision);
    }

    public void EndEngagement()
    {
        if (!IsActive)
        {
            return;
        }

        hasFlankDestination = false;
        hasCommittedCharge = false;
        noProgressElapsed = 0f;
        tactics.BeginRegroup(raider.RegroupDuration);
        navigation?.SetSpeedMultiplier(1f);
    }

    public void ClearForPool()
    {
        activeSet = null;
        raider = null;
        target = null;
        hasFlankDestination = false;
        hasCommittedCharge = false;
        flankDestination = Vector3.zero;
        lastProgressPosition = Vector3.zero;
        noProgressElapsed = 0f;
        SuccessfulFlankSelections = 0;
        PathFailureCount = 0;
        tactics.Reset(false);
        navigation?.ResetMovementProfile();
        EnsureOverhead();
        overhead?.ClearRoleStatus();
    }

    private bool TrySelectFlank(
        Transform chaseTarget,
        out Vector3 resolved)
    {
        Vector3 targetForward = chaseTarget.forward;
        targetForward.y = 0f;

        if (targetForward.sqrMagnitude <= 0.001f)
        {
            targetForward = Vector3.forward;
        }

        targetForward.Normalize();
        Vector3 targetRight = Vector3.Cross(
            Vector3.up,
            targetForward).normalized;
        Vector3 rear = -targetForward * raider.FlankRearOffset;
        Vector3 first = chaseTarget.position + rear +
            targetRight * (preferRightFlank
                ? raider.FlankDistance
                : -raider.FlankDistance);
        Vector3 second = chaseTarget.position + rear +
            targetRight * (preferRightFlank
                ? -raider.FlankDistance
                : raider.FlankDistance);

        if (!navigation.TryResolveReachableDestination(
                first,
                raider.FlankSampleRadius,
                out resolved) &&
            !navigation.TryResolveReachableDestination(
                second,
                raider.FlankSampleRadius,
                out resolved))
        {
            return false;
        }

        flankDestination = resolved;
        hasFlankDestination = true;
        preferRightFlank = !preferRightFlank;
        lastProgressPosition = transform.position;
        noProgressElapsed = 0f;
        SuccessfulFlankSelections++;
        tactics.BeginFlank();
        navigation.SetSpeedMultiplier(raider.FlankSpeedMultiplier);
        return true;
    }

    private bool HasStoppedMakingProgress(float deltaTime)
    {
        Vector3 movement = transform.position - lastProgressPosition;
        movement.y = 0f;

        if (movement.sqrMagnitude >= ProgressThreshold * ProgressThreshold)
        {
            lastProgressPosition = transform.position;
            noProgressElapsed = 0f;
            return false;
        }

        noProgressElapsed += Mathf.Max(0f, deltaTime);
        return noProgressElapsed >= raider.NoProgressTimeout;
    }

    private void BeginCharge()
    {
        hasFlankDestination = false;
        hasCommittedCharge = true;
        noProgressElapsed = 0f;
        tactics.BeginCharge();
        navigation.SetSpeedMultiplier(raider.ChargeSpeedMultiplier);
    }

    private void FailFlank()
    {
        PathFailureCount++;
        hasFlankDestination = false;
        hasCommittedCharge = false;
        noProgressElapsed = 0f;
        tactics.BeginRegroup(raider.RegroupDuration);
        navigation.SetSpeedMultiplier(1f);
    }

    private void EnsureOverhead()
    {
        overhead ??= GetComponent<EnemyBurnEffectController>();
    }
}
