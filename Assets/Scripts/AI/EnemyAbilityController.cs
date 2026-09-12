using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(EnemyNavigationController))]
public sealed class EnemyAbilityController : MonoBehaviour
{
    private const float ProgressThreshold = 0.15f;
    private const float CandidateEyeHeight = 0.7f;
    private const int LineOfFireHitCapacity = 8;

    private readonly RaiderTacticsStateMachine tactics = new();
    private readonly SuppressorTacticsStateMachine suppressorTactics = new();
    private readonly EnemyUtilityDecisionEngine utilityDecisionEngine = new();
    private readonly EnemyUtilityWorldFactCollector utilityFactCollector = new();
    private readonly RaycastHit[] lineOfFireHits =
        new RaycastHit[LineOfFireHitCapacity];
    private EnemyNavigationController navigation;
    private EnemyBurnEffectController overhead;
    private EnemyController enemy;
    private EnemyAbilitySetDefinition activeSet;
    private RaiderApproachAbilityDefinition raider;
    private SuppressorRangedAbilityDefinition suppressor;
    private EnemySupportAuraAbilityDefinition support;
    private EnemyUtilityProfileDefinition utilityProfile;
    private Transform target;
    private Vector3 flankDestination;
    private Vector3 retreatDestination;
    private Vector3 lastProgressPosition;
    private bool hasFlankDestination;
    private bool hasRetreatDestination;
    private bool hasCommittedCharge;
    private bool preferRightFlank;
    private float noProgressElapsed;
    private readonly List<EnemyController> supportCandidates = new();
    private readonly List<EnemyController> desiredSupportTargets = new();
    private readonly List<EnemyController> activeSupportTargets = new();
    private IEnemyNeighborQuery neighborQuery;
    private Health health;
    private float supportCooldownRemaining;
    private EnemyAiLodController lod;
    private EnemyUtilityDecisionResult lastUtilityDecision;

    public bool IsActive => activeSet != null &&
        (raider != null || suppressor != null || support != null);
    public bool IsRaider => activeSet != null && raider != null;
    public bool IsSuppressor => activeSet != null && suppressor != null;
    public bool IsSupport => activeSet != null && support != null;
    public EnemyAbilitySetDefinition ActiveSet => activeSet;
    public Transform ActiveTarget => target;
    public RaiderTacticsPhase Phase => tactics.Phase;
    public SuppressorTacticsPhase SuppressorPhase =>
        suppressorTactics.Phase;
    public Vector3 FlankDestination => flankDestination;
    public bool HasFlankDestination => hasFlankDestination;
    public int SuccessfulFlankSelections { get; private set; }
    public int PathFailureCount { get; private set; }
    public int ActiveSupportTargetCount => activeSupportTargets.Count;
    public int SupportPulseCount { get; private set; }
    public EnemyUtilityDecisionResult LastUtilityDecision =>
        lastUtilityDecision;
    public bool UsesUtilityAi => IsRaider && utilityProfile != null;
    public string SupportSourceId => enemy != null
        ? $"{gameObject.GetEntityId()}:{enemy.SpawnResetCount}"
        : $"{gameObject.GetEntityId()}:0";
    public float AttackRange => IsRaider
        ? raider.AttackRange
        : IsSuppressor
            ? suppressor.MaximumRange
            : 2.8f;
    public float WindupDuration => IsRaider
        ? raider.WindupDuration
        : IsSuppressor
            ? suppressor.WindupDuration
            : 0.55f;
    public float AttackCooldown => IsRaider
        ? raider.AttackCooldown
        : IsSuppressor
            ? suppressor.AttackCooldown
            : 1.35f;
    public float DamageMultiplier => IsRaider
        ? raider.DamageMultiplier
        : IsSuppressor
            ? suppressor.DamageMultiplier
            : 1f;
    public float TracerSpeed => IsSuppressor
        ? suppressor.TracerSpeed
        : 260f;
    public float SupportRadius => IsSupport ? support.Radius : 0f;
    public EnemyAttackMode AttackMode => IsSuppressor
        ? EnemyAttackMode.Hitscan
        : EnemyAttackMode.Melee;

    private void Awake()
    {
        navigation = GetComponent<EnemyNavigationController>();
        overhead = GetComponent<EnemyBurnEffectController>();
        enemy = GetComponent<EnemyController>();
        health = GetComponent<Health>();
        lod = GetComponent<EnemyAiLodController>();
        neighborQuery = EnemySpatialIndexService.Instance ??
            EnemySpatialIndexService.EnsureForActiveScene();
        tactics.Reset(false);
        suppressorTactics.Reset(false);

        if (health != null)
        {
            health.Died += HandleSupporterDeath;
        }
    }

    private void Update()
    {
        if (!IsSupport || Time.timeScale <= 0f)
        {
            return;
        }

        if (health != null && health.IsDead)
        {
            ClearOutgoingSupport();
            return;
        }

        lod ??= GetComponent<EnemyAiLodController>();
        float elapsedTime = Time.deltaTime;

        if (lod != null && !lod.TryAcquireTick(
                EnemyAiLodChannel.Ability,
                out elapsedTime))
        {
            return;
        }

        PruneOutgoingSupport();
        supportCooldownRemaining -= elapsedTime;

        if (supportCooldownRemaining <= 0f)
        {
            PulseSupportNow();
        }
    }

    private void OnDisable()
    {
        ClearOutgoingSupport();
    }

    private void OnDestroy()
    {
        if (health != null)
        {
            health.Died -= HandleSupporterDeath;
        }
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
        SuppressorRangedAbilityDefinition ranged =
            definition.FindAbility<SuppressorRangedAbilityDefinition>();
        EnemySupportAuraAbilityDefinition aura =
            definition.FindAbility<EnemySupportAuraAbilityDefinition>();

        if (approach == null && ranged == null && aura == null)
        {
            return false;
        }

        if (approach != null && ranged != null)
        {
            Debug.LogError(
                "An enemy ability set can own only one tactical " +
                "movement ability.",
                this);
            return false;
        }

        activeSet = definition;
        raider = approach;
        suppressor = ranged;
        support = aura;
        utilityProfile = raider != null ? raider.UtilityProfile : null;
        utilityDecisionEngine.Reset(utilityProfile);
        lastUtilityDecision = null;
        target = newTarget;
        preferRightFlank = enemy != null &&
            enemy.SpawnResetCount % 2 == 0;
        tactics.Reset(raider != null);
        suppressorTactics.Reset(suppressor != null);
        EnsureOverhead();
        overhead?.SetRoleStatus(
            definition.StatusLabel,
            definition.StatusColor);
        navigation?.SetSpeedMultiplier(raider != null
            ? raider.FlankSpeedMultiplier
            : suppressor != null
                ? suppressor.MovementSpeedMultiplier
                : 1f);
        supportCooldownRemaining = 0f;
        return true;
    }

    public EnemyMovementDirective ResolveMovement(
        Transform chaseTarget,
        Vector3 knownTargetPosition,
        bool hasVisualContact,
        float distance,
        float deltaTime)
    {
        if (!IsActive || chaseTarget == null)
        {
            EndEngagement();
            return EnemyMovementDirective.None;
        }

        target = chaseTarget;

        if (IsSuppressor)
        {
            return ResolveSuppressorMovement(
                chaseTarget,
                knownTargetPosition,
                hasVisualContact,
                distance,
                deltaTime);
        }

        if (UsesUtilityAi)
        {
            return ResolveRaiderUtilityMovement(
                chaseTarget,
                knownTargetPosition,
                hasVisualContact,
                distance,
                deltaTime);
        }

        if (!IsRaider)
        {
            return EnemyMovementDirective.None;
        }

        if (hasVisualContact && distance <= raider.AttackRange)
        {
            hasFlankDestination = false;
            return EnemyMovementDirective.None;
        }

        if (TryResolveChaseDestination(
                chaseTarget,
                hasVisualContact,
                distance,
                deltaTime,
                out Vector3 destination))
        {
            return EnemyMovementDirective.MoveTo(destination);
        }

        return tactics.Phase == RaiderTacticsPhase.Regrouping
            ? EnemyMovementDirective.Hold
            : EnemyMovementDirective.None;
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

        if (!IsRaider || chaseTarget == null)
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

    private EnemyMovementDirective ResolveRaiderUtilityMovement(
        Transform chaseTarget,
        Vector3 knownTargetPosition,
        bool hasVisualContact,
        float distance,
        float deltaTime)
    {
        EnemyUtilityWorldFacts facts = utilityFactCollector.Capture(
            enemy,
            chaseTarget,
            hasVisualContact,
            utilityProfile,
            neighborQuery);
        EnemyUtilityDecisionResult decision = utilityDecisionEngine.Evaluate(
            facts,
            deltaTime,
            ResolveUtilitySeed(),
            IsUtilityActionExecutable);
        ApplyUtilityDecision(decision);

        if (!decision.HasSelection)
        {
            navigation.SetSpeedMultiplier(1f);
            return EnemyMovementDirective.MoveTo(knownTargetPosition);
        }

        EnemyUtilityActionDefinition action = decision.SelectedAction;

        if (action.Kind != EnemyUtilityActionKind.Flank &&
            !hasCommittedCharge)
        {
            hasFlankDestination = false;
        }

        if (action.Kind != EnemyUtilityActionKind.Retreat)
        {
            hasRetreatDestination = false;
        }

        switch (action.Kind)
        {
            case EnemyUtilityActionKind.Flank:
                return ResolveUtilityFlank(
                    action,
                    chaseTarget,
                    knownTargetPosition,
                    hasVisualContact,
                    distance,
                    deltaTime,
                    facts);
            case EnemyUtilityActionKind.Retreat:
                return ResolveUtilityRetreat(
                    action,
                    chaseTarget,
                    knownTargetPosition,
                    deltaTime,
                    facts);
            case EnemyUtilityActionKind.Chase:
            default:
                hasCommittedCharge = false;
                navigation.SetSpeedMultiplier(
                    action.MovementSpeedMultiplier);
                return hasVisualContact && distance <= raider.AttackRange
                    ? EnemyMovementDirective.None
                    : EnemyMovementDirective.MoveTo(knownTargetPosition);
        }
    }

    private EnemyMovementDirective ResolveUtilityFlank(
        EnemyUtilityActionDefinition action,
        Transform chaseTarget,
        Vector3 knownTargetPosition,
        bool hasVisualContact,
        float distance,
        float deltaTime,
        EnemyUtilityWorldFacts facts)
    {
        if (hasCommittedCharge)
        {
            tactics.BeginCharge();
            navigation.SetSpeedMultiplier(raider.ChargeSpeedMultiplier);
            return hasVisualContact && distance <= raider.AttackRange
                ? EnemyMovementDirective.None
                : EnemyMovementDirective.MoveTo(knownTargetPosition);
        }

        if (hasFlankDestination)
        {
            if (navigation.HasReachedDestination)
            {
                utilityDecisionEngine.CompleteActive();
                BeginCharge();
                return EnemyMovementDirective.MoveTo(knownTargetPosition);
            }

            if (HasStoppedMakingProgress(deltaTime))
            {
                FailFlank();
                return ResolveUtilityFailureFallback(
                    action,
                    knownTargetPosition,
                    facts);
            }

            tactics.BeginFlank();
            navigation.SetSpeedMultiplier(action.MovementSpeedMultiplier);
            return EnemyMovementDirective.MoveTo(flankDestination);
        }

        if (!TrySelectFlank(chaseTarget, out Vector3 destination))
        {
            FailFlank();
            return ResolveUtilityFailureFallback(
                action,
                knownTargetPosition,
                facts);
        }

        navigation.SetSpeedMultiplier(action.MovementSpeedMultiplier);
        return EnemyMovementDirective.MoveTo(destination);
    }

    private EnemyMovementDirective ResolveUtilityRetreat(
        EnemyUtilityActionDefinition action,
        Transform chaseTarget,
        Vector3 knownTargetPosition,
        float deltaTime,
        EnemyUtilityWorldFacts facts)
    {
        hasCommittedCharge = false;
        hasFlankDestination = false;
        tactics.BeginRetreat();

        if (hasRetreatDestination && navigation.HasReachedDestination)
        {
            hasRetreatDestination = false;
            utilityDecisionEngine.CompleteActive();
            navigation.SetSpeedMultiplier(1f);
            return EnemyMovementDirective.Hold;
        }

        if (hasRetreatDestination && HasStoppedMakingProgress(deltaTime))
        {
            PathFailureCount++;
            hasRetreatDestination = false;
            noProgressElapsed = 0f;
            return ResolveUtilityFailureFallback(
                action,
                knownTargetPosition,
                facts);
        }

        if (!hasRetreatDestination &&
            !TrySelectRetreatDestination(chaseTarget, action, out _))
        {
            PathFailureCount++;
            return ResolveUtilityFailureFallback(
                action,
                knownTargetPosition,
                facts);
        }

        navigation.SetSpeedMultiplier(action.MovementSpeedMultiplier);
        return EnemyMovementDirective.MoveTo(retreatDestination);
    }

    private bool TrySelectRetreatDestination(
        Transform chaseTarget,
        EnemyUtilityActionDefinition action,
        out Vector3 resolved)
    {
        Vector3 away = transform.position - chaseTarget.position;
        away.y = 0f;

        if (away.sqrMagnitude <= 0.001f)
        {
            away = -chaseTarget.forward;
            away.y = 0f;
        }

        away.Normalize();
        float distance = Mathf.Max(2f, action.MovementDistance);
        float sampleRadius = Mathf.Max(1f, raider.FlankSampleRadius);
        int firstSide = preferRightFlank ? 1 : -1;

        for (int index = 0; index < 3; index++)
        {
            float angle = index == 0
                ? 0f
                : firstSide * (index == 1 ? 45f : -45f);
            Vector3 candidate = transform.position +
                Quaternion.AngleAxis(angle, Vector3.up) * away * distance;

            if (!navigation.TryResolveReachableDestination(
                    candidate,
                    sampleRadius,
                    out resolved))
            {
                continue;
            }

            retreatDestination = resolved;
            hasRetreatDestination = true;
            lastProgressPosition = transform.position;
            noProgressElapsed = 0f;
            preferRightFlank = !preferRightFlank;
            return true;
        }

        resolved = default;
        return false;
    }

    private EnemyMovementDirective ResolveUtilityFailureFallback(
        EnemyUtilityActionDefinition failedAction,
        Vector3 knownTargetPosition,
        EnemyUtilityWorldFacts facts)
    {
        utilityDecisionEngine.ReportFailure(failedAction.StableId);
        EnemyUtilityDecisionResult fallback = utilityDecisionEngine.Evaluate(
            facts,
            0f,
            ResolveUtilitySeed(),
            IsUtilityActionExecutable);
        ApplyUtilityDecision(fallback);
        navigation.SetSpeedMultiplier(
            fallback.SelectedAction != null
                ? fallback.SelectedAction.MovementSpeedMultiplier
                : 1f);
        return EnemyMovementDirective.MoveTo(knownTargetPosition);
    }

    private bool IsUtilityActionExecutable(
        EnemyUtilityActionDefinition action)
    {
        if (action == null)
        {
            return false;
        }

        return action.Kind switch
        {
            EnemyUtilityActionKind.Chase => true,
            EnemyUtilityActionKind.Flank => navigation != null,
            EnemyUtilityActionKind.Retreat => navigation != null,
            _ => false
        };
    }

    private void ApplyUtilityDecision(EnemyUtilityDecisionResult decision)
    {
        lastUtilityDecision = decision;

        if (decision == null || !decision.Changed)
        {
            return;
        }

        WaveEnemyLifecycle lifecycle = GetComponent<WaveEnemyLifecycle>();
        RunDeterminismRecorder.Active?.TryRecordAiDecision(
            lifecycle != null && lifecycle.IsArmed ? lifecycle.SpawnId : 0,
            decision.PreviousActionId,
            decision.SelectedAction?.StableId ?? string.Empty,
            decision.ReasonCode,
            FindSelectedScore(decision));
    }

    private static float FindSelectedScore(
        EnemyUtilityDecisionResult decision)
    {
        if (decision?.SelectedAction == null)
        {
            return 0f;
        }

        for (int index = 0; index < decision.Candidates.Count; index++)
        {
            EnemyUtilityCandidateScore candidate = decision.Candidates[index];

            if (ReferenceEquals(candidate.Action, decision.SelectedAction))
            {
                return candidate.Score;
            }
        }

        return 0f;
    }

    private static long ResolveUtilitySeed()
    {
        return RunDeterminismRecorder.Active?.Record?.RunSeed ?? 18018L;
    }

    public void NotifyAttackDecision(EnemyAttackDecision decision)
    {
        if (!IsActive)
        {
            return;
        }

        if (IsRaider)
        {
            tactics.ApplyAttackDecision(decision);
        }
        else if (IsSuppressor)
        {
            suppressorTactics.ApplyAttackDecision(decision);
        }
    }

    public void EndEngagement()
    {
        if (!IsActive)
        {
            return;
        }

        hasFlankDestination = false;
        hasRetreatDestination = false;
        hasCommittedCharge = false;
        flankDestination = Vector3.zero;
        retreatDestination = Vector3.zero;
        noProgressElapsed = 0f;
        utilityDecisionEngine.CancelActive();
        if (IsRaider)
        {
            tactics.BeginRegroup(raider.RegroupDuration);
        }
        else if (IsSuppressor)
        {
            suppressorTactics.BeginRecovery(suppressor.RecoveryDuration);
        }
        navigation?.SetSpeedMultiplier(1f);
    }

    public void ClearForPool()
    {
        ClearOutgoingSupport();
        activeSet = null;
        raider = null;
        suppressor = null;
        support = null;
        utilityProfile = null;
        target = null;
        hasFlankDestination = false;
        hasRetreatDestination = false;
        hasCommittedCharge = false;
        flankDestination = Vector3.zero;
        retreatDestination = Vector3.zero;
        lastProgressPosition = Vector3.zero;
        noProgressElapsed = 0f;
        SuccessfulFlankSelections = 0;
        PathFailureCount = 0;
        SupportPulseCount = 0;
        supportCooldownRemaining = 0f;
        lastUtilityDecision = null;
        utilityDecisionEngine.Reset(null);
        tactics.Reset(false);
        suppressorTactics.Reset(false);
        navigation?.ResetMovementProfile();
        EnsureOverhead();
        overhead?.ClearRoleStatus();
    }

    public int PulseSupportNow()
    {
        if (!IsSupport || support.BuffEffect == null ||
            health == null || health.IsDead)
        {
            return 0;
        }

        neighborQuery ??= EnemySpatialIndexService.Instance ??
            EnemySpatialIndexService.EnsureForActiveScene();
        neighborQuery.CollectAliveNeighbors(
            enemy,
            support.Radius,
            support.RequiredTargetTag,
            supportCandidates);
        SortSupportCandidates();
        desiredSupportTargets.Clear();
        int selectedCount = Mathf.Min(
            support.MaximumTargets,
            supportCandidates.Count);

        for (int index = 0; index < selectedCount; index++)
        {
            desiredSupportTargets.Add(supportCandidates[index]);
        }

        for (int index = activeSupportTargets.Count - 1;
             index >= 0;
             index--)
        {
            EnemyController previous = activeSupportTargets[index];

            if (previous != null &&
                desiredSupportTargets.Contains(previous))
            {
                continue;
            }

            previous?.SupportEffects?.RemoveSource(SupportSourceId);
            activeSupportTargets.RemoveAt(index);
        }

        for (int index = 0; index < desiredSupportTargets.Count; index++)
        {
            EnemyController selected = desiredSupportTargets[index];

            if (selected?.SupportEffects == null ||
                !selected.SupportEffects.ApplyOrRefresh(
                    SupportSourceId,
                    gameObject,
                    support.BuffEffect,
                    support.EffectDuration))
            {
                continue;
            }

            if (!activeSupportTargets.Contains(selected))
            {
                activeSupportTargets.Add(selected);
            }
        }

        SupportPulseCount++;
        supportCooldownRemaining = support.Cooldown;
        return activeSupportTargets.Count;
    }

    private void PruneOutgoingSupport()
    {
        if (!IsSupport)
        {
            return;
        }

        for (int index = activeSupportTargets.Count - 1;
             index >= 0;
             index--)
        {
            EnemyController supported = activeSupportTargets[index];
            bool remainsValid = EnemyNeighborQueryUtility.IsInRange(
                    enemy,
                    supported,
                    support.Radius) &&
                supported.HasGameplayTag(support.RequiredTargetTag) &&
                supported.SupportEffects != null &&
                supported.SupportEffects.HasSource(SupportSourceId);

            if (remainsValid)
            {
                continue;
            }

            supported?.SupportEffects?.RemoveSource(SupportSourceId);
            activeSupportTargets.RemoveAt(index);
        }
    }

    private void ClearOutgoingSupport()
    {
        string sourceId = SupportSourceId;

        for (int index = activeSupportTargets.Count - 1;
             index >= 0;
             index--)
        {
            activeSupportTargets[index]
                ?.SupportEffects
                ?.RemoveSource(sourceId);
        }

        activeSupportTargets.Clear();
        desiredSupportTargets.Clear();
        supportCandidates.Clear();
    }

    private void SortSupportCandidates()
    {
        for (int index = 1; index < supportCandidates.Count; index++)
        {
            EnemyController candidate = supportCandidates[index];
            int insertion = index - 1;

            while (insertion >= 0 &&
                   CompareSupportTargets(
                       candidate,
                       supportCandidates[insertion]) < 0)
            {
                supportCandidates[insertion + 1] =
                    supportCandidates[insertion];
                insertion--;
            }

            supportCandidates[insertion + 1] = candidate;
        }
    }

    private int CompareSupportTargets(
        EnemyController left,
        EnemyController right)
    {
        int comparison = support.TargetPriority switch
        {
            EnemySupportTargetPriority.LowestHealthRatio =>
                HealthRatio(left).CompareTo(HealthRatio(right)),
            EnemySupportTargetPriority.Nearest =>
                SquaredDistance(left).CompareTo(SquaredDistance(right)),
            EnemySupportTargetPriority.HighestAttackDamage =>
                right.AttackDamage.CompareTo(left.AttackDamage),
            _ => 0
        };
        return comparison != 0
            ? comparison
            : left.gameObject.GetEntityId().CompareTo(
                right.gameObject.GetEntityId());
    }

    private float SquaredDistance(EnemyController candidate)
    {
        Vector3 offset = candidate.transform.position - transform.position;
        offset.y = 0f;
        return offset.sqrMagnitude;
    }

    private static float HealthRatio(EnemyController candidate)
    {
        Health candidateHealth = candidate.GetComponent<Health>();
        return candidateHealth != null && candidateHealth.MaxHealth > 0f
            ? candidateHealth.CurrentHealth / candidateHealth.MaxHealth
            : 1f;
    }

    private void HandleSupporterDeath()
    {
        ClearOutgoingSupport();
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

    private EnemyMovementDirective ResolveSuppressorMovement(
        Transform chaseTarget,
        Vector3 knownTargetPosition,
        bool hasVisualContact,
        float distance,
        float deltaTime)
    {
        if (suppressorTactics.Phase ==
                SuppressorTacticsPhase.Recovering &&
            !suppressorTactics.TickRecovery(deltaTime))
        {
            navigation.SetSpeedMultiplier(1f);
            return EnemyMovementDirective.Hold;
        }

        if (hasFlankDestination)
        {
            if (navigation.HasReachedDestination)
            {
                hasFlankDestination = false;
                noProgressElapsed = 0f;
            }
            else if (HasStoppedMakingSuppressorProgress(deltaTime))
            {
                FailSuppressorPosition();
                return EnemyMovementDirective.Hold;
            }
            else
            {
                navigation.SetSpeedMultiplier(
                    suppressor.MovementSpeedMultiplier);
                return EnemyMovementDirective.MoveTo(flankDestination);
            }
        }

        SuppressorMovementIntent intent =
            suppressorTactics.EvaluateMovement(
                distance,
                hasVisualContact,
                suppressor.MinimumRange,
                suppressor.MaximumRange);

        if (intent == SuppressorMovementIntent.Hold)
        {
            navigation.SetSpeedMultiplier(1f);
            return EnemyMovementDirective.None;
        }

        if (!TrySelectSuppressorPosition(
                chaseTarget,
                knownTargetPosition,
                hasVisualContact,
                intent,
                out Vector3 destination))
        {
            FailSuppressorPosition();
            return EnemyMovementDirective.Hold;
        }

        navigation.SetSpeedMultiplier(
            suppressor.MovementSpeedMultiplier);
        return EnemyMovementDirective.MoveTo(destination);
    }

    private bool TrySelectSuppressorPosition(
        Transform chaseTarget,
        Vector3 knownTargetPosition,
        bool hasVisualContact,
        SuppressorMovementIntent intent,
        out Vector3 resolved)
    {
        Vector3 radial = transform.position - knownTargetPosition;
        radial.y = 0f;

        if (radial.sqrMagnitude <= 0.001f)
        {
            radial = -chaseTarget.forward;
            radial.y = 0f;
        }

        radial.Normalize();
        float radius = suppressor.PreferredRange;
        float baseAngle = Mathf.Clamp(
            Mathf.Atan2(
                suppressor.RepositionLateralOffset,
                radius) * Mathf.Rad2Deg,
            15f,
            45f);
        int firstSide = preferRightFlank ? 1 : -1;
        int candidateCount = intent ==
            SuppressorMovementIntent.Relocate ? 6 : 5;
        bool found = false;
        resolved = default;

        for (int index = 0; index < candidateCount; index++)
        {
            float angle;

            if (intent != SuppressorMovementIntent.Relocate && index == 0)
            {
                angle = 0f;
            }
            else
            {
                int pairedIndex = intent ==
                    SuppressorMovementIntent.Relocate
                    ? index
                    : index - 1;
                int ringStep = pairedIndex / 2 + 1;
                int side = pairedIndex % 2 == 0
                    ? firstSide
                    : -firstSide;
                angle = side * baseAngle * ringStep;
            }

            Vector3 candidateDirection =
                Quaternion.AngleAxis(angle, Vector3.up) * radial;
            Vector3 candidate = knownTargetPosition +
                candidateDirection * radius;

            if (TryResolveFiringPosition(
                    candidate,
                    knownTargetPosition,
                    hasVisualContact ? chaseTarget : null,
                    out resolved))
            {
                found = true;
                break;
            }
        }

        if (!found)
        {
            return false;
        }

        flankDestination = resolved;
        hasFlankDestination = true;
        preferRightFlank = !preferRightFlank;
        lastProgressPosition = transform.position;
        noProgressElapsed = 0f;
        SuccessfulFlankSelections++;
        return true;
    }

    private bool TryResolveFiringPosition(
        Vector3 desired,
        Vector3 targetPosition,
        Transform expectedTarget,
        out Vector3 resolved)
    {
        if (!navigation.TryResolveReachableDestination(
                desired,
                suppressor.PositionSampleRadius,
                out resolved))
        {
            return false;
        }

        return HasClearLineOfFireFrom(
            resolved,
            targetPosition,
            expectedTarget);
    }

    public bool HasClearLineOfFireFrom(
        Vector3 observerPosition,
        Transform expectedTarget)
    {
        if (expectedTarget == null)
        {
            return false;
        }

        return HasClearLineOfFireFrom(
            observerPosition,
            expectedTarget.position,
            expectedTarget);
    }

    private bool HasClearLineOfFireFrom(
        Vector3 observerPosition,
        Vector3 targetPosition,
        Transform expectedTarget)
    {
        Vector3 origin = observerPosition +
            Vector3.up * CandidateEyeHeight;
        Vector3 targetPoint = targetPosition +
            Vector3.up * CandidateEyeHeight;
        Vector3 offset = targetPoint - origin;
        float distance = offset.magnitude;

        if (distance <= 0.001f)
        {
            return true;
        }

        int hitCount = Physics.RaycastNonAlloc(
            origin,
            offset / distance,
            lineOfFireHits,
            distance,
            Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore);
        float nearestBlockingDistance = float.PositiveInfinity;

        for (int index = 0; index < hitCount; index++)
        {
            Transform hitTransform = lineOfFireHits[index].transform;

            if (hitTransform == null ||
                hitTransform == transform ||
                hitTransform.IsChildOf(transform))
            {
                continue;
            }

            if (expectedTarget != null &&
                (hitTransform == expectedTarget ||
                 hitTransform.IsChildOf(expectedTarget) ||
                 expectedTarget.IsChildOf(hitTransform)))
            {
                continue;
            }

            nearestBlockingDistance = Mathf.Min(
                nearestBlockingDistance,
                lineOfFireHits[index].distance);
        }

        return float.IsPositiveInfinity(nearestBlockingDistance);
    }

    private bool HasStoppedMakingSuppressorProgress(float deltaTime)
    {
        Vector3 movement = transform.position - lastProgressPosition;
        movement.y = 0f;

        if (movement.sqrMagnitude >=
            ProgressThreshold * ProgressThreshold)
        {
            lastProgressPosition = transform.position;
            noProgressElapsed = 0f;
            return false;
        }

        noProgressElapsed += Mathf.Max(0f, deltaTime);
        return noProgressElapsed >= suppressor.NoProgressTimeout;
    }

    private void FailSuppressorPosition()
    {
        PathFailureCount++;
        hasFlankDestination = false;
        noProgressElapsed = 0f;
        preferRightFlank = !preferRightFlank;
        suppressorTactics.BeginRecovery(suppressor.RecoveryDuration);
        navigation.SetSpeedMultiplier(1f);
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
