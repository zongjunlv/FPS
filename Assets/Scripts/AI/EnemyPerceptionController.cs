using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(EnemyNavigationController))]
public sealed class EnemyPerceptionController : MonoBehaviour
{
    [Header("Vision")]
    [SerializeField, Min(1f)] private float sightDistance = 22f;
    [SerializeField, Range(1f, 360f)] private float fieldOfView = 110f;
    [SerializeField, Min(0.01f)] private float alertSpeed = 0.75f;
    [SerializeField, Min(0.01f)] private float awarenessDecaySpeed = 0.4f;
    [Header("Investigation")]
    [SerializeField, Min(0.1f)] private float searchDuration = 5f;
    [SerializeField, Min(0.1f)] private float hearingSensitivity = 1.35f;
    private EnemyAwarenessStateMachine awareness;
    private EnemyNavigationController navigation;
    private CombatSoundEventChannel soundEvents;
    private EnemySquadCoordinator squadCoordinator;
    private readonly EnemyAlertMemory squadAlertMemory = new();
    private Transform target;
    private bool investigatingSound;
    private bool hasSquadSearchAssignment;
    private Vector3 squadSearchDestination;
    private float latestVisualIntelTime = float.NegativeInfinity;
    private readonly RaycastHit[] sightHits = new RaycastHit[32];
    private readonly EntityId[] selfSightColliderIds = new EntityId[32];
    private readonly EntityId[] targetSightColliderIds = new EntityId[32];
    private readonly List<Collider> sightColliderBuffer = new(32);
    private EnemyPerceptionScheduler perceptionScheduler;
    private EnemyAiLodController lod;
    private Health health;
    private bool healthLookupCompleted;
    private int lastSightCheckFrame = -1;
    private int selfSightColliderCount;
    private int targetSightColliderCount;
    private int sightGeneration;
    private Vector3 latestSightTargetPosition;
    private EnemyAiLodTier lastSightCheckLodTier = EnemyAiLodTier.Far;

    public EnemyAwarenessState State =>
        awareness?.State ?? EnemyAwarenessState.Patrol;
    public float Awareness => awareness?.Awareness ?? 0f;
    public Vector3 LastKnownPosition =>
        awareness?.LastKnownPosition ?? transform.position;
    public Vector3 Destination =>
        navigation != null
            ? navigation.Destination
            : transform.position;
    public bool HasVisualContact { get; private set; }
    public int PatrolPointCount =>
        navigation != null ? navigation.PatrolPointCount : 0;
    public Transform Target => target;
    public int ReceivedSquadAlertCount =>
        squadAlertMemory.AcceptedCount;
    public bool HasSquadSearchAssignment =>
        hasSquadSearchAssignment;
    public Vector3 SquadSearchDestination =>
        squadSearchDestination;
    public EnemySquadAlert LastReceivedSquadAlert =>
        squadAlertMemory.LatestAlert;
    public int SightCheckCount { get; private set; }
    public int SaturatedSightQueryCount { get; private set; }
    public int MaximumSightCheckLatencyFrames { get; private set; }
    public long TotalSightCheckLatencyFrames { get; private set; }
    public int SightCheckLatencySampleCount { get; private set; }
    public float AverageSightCheckLatencyFrames =>
        SightCheckLatencySampleCount > 0
            ? (float)TotalSightCheckLatencyFrames /
              SightCheckLatencySampleCount
            : 0f;
    public int MaximumNearSightCheckLatencyFrames { get; private set; }
    public int MaximumSightResultDelayFrames { get; private set; }
    public int MaximumNearSightResultDelayFrames { get; private set; }
    public EnemyAiLodTier LodTier => lod != null
        ? lod.CurrentTier
        : EnemyAiLodTier.Near;
    internal EnemyAiLodController Lod => lod;
    internal int SightGeneration => sightGeneration;

    private void Awake()
    {
        navigation = GetComponent<EnemyNavigationController>();
        awareness = new EnemyAwarenessStateMachine();
        awareness.Configure(
            alertSpeed,
            awarenessDecaySpeed,
            searchDuration);
        soundEvents =
            Resources.Load<CombatSoundEventChannel>(
                "CombatSoundEvents");
        squadAlertMemory.Configure(3f);
        squadCoordinator = EnemySquadCoordinator.Instance;
        perceptionScheduler =
            EnemyPerceptionScheduler.EnsureForActiveScene();
        lod = GetComponent<EnemyAiLodController>();
    }

    private void OnEnable()
    {
        sightGeneration++;
        RefreshSightColliderCache();
        squadCoordinator ??= EnemySquadCoordinator.Instance ??
            EnemySquadCoordinator.EnsureForActiveScene();
        perceptionScheduler ??=
            EnemyPerceptionScheduler.EnsureForActiveScene();

        if (soundEvents != null)
        {
            soundEvents.SoundPublished += HandleSound;
        }

        squadCoordinator?.Register(this);
        perceptionScheduler?.Register(this);
    }

    private void Start()
    {
        ResolveHealth();

        if (target == null)
        {
            GameObject player =
                GameObject.FindGameObjectWithTag("Player");
            target = player != null ? player.transform : null;
        }
    }

    private void OnDisable()
    {
        sightGeneration++;
        if (soundEvents != null)
        {
            soundEvents.SoundPublished -= HandleSound;
        }

        squadCoordinator?.Unregister(this);
        perceptionScheduler?.Unregister(this);
    }

    private void Update()
    {
        if (Time.timeScale <= 0f)
        {
            return;
        }

        lod ??= GetComponent<EnemyAiLodController>();
        float elapsedTime = Time.deltaTime;

        if (lod != null && !lod.TryAcquireTick(
                EnemyAiLodChannel.Perception,
                out elapsedTime))
        {
            return;
        }

        if (target == null)
        {
            HasVisualContact = false;
        }

        if (HasVisualContact)
        {
            investigatingSound = false;
            hasSquadSearchAssignment = false;
            awareness.Observe(latestSightTargetPosition, elapsedTime);

            if (lastSightCheckFrame == Time.frameCount)
            {
                latestVisualIntelTime = Time.time;
            }

            if (awareness.State == EnemyAwarenessState.Alert)
            {
                squadCoordinator?.TryBroadcastBudgeted(
                    this,
                    latestSightTargetPosition,
                    1f,
                    Time.time);
            }
        }
        else if (awareness.State == EnemyAwarenessState.Search)
        {
            awareness.AdvanceSearch(
                elapsedTime,
                navigation.HasReachedDestination);
        }
        else if (investigatingSound)
        {
            if (navigation.HasReachedDestination)
            {
                investigatingSound = false;
                awareness.BeginSearchAt(
                    awareness.LastKnownPosition);
            }
        }
        else
        {
            awareness.Tick(elapsedTime);
        }

        if (awareness.State == EnemyAwarenessState.Patrol)
        {
            hasSquadSearchAssignment = false;
            navigation.TickPatrol();
        }
        else if (awareness.State != EnemyAwarenessState.Alert)
        {
            navigation.SetDestination(
                hasSquadSearchAssignment
                    ? squadSearchDestination
                    : awareness.LastKnownPosition);
        }
    }

    public void BindSquadCoordinator(
        EnemySquadCoordinator coordinator,
        float alertLifetime)
    {
        squadCoordinator = coordinator;
        squadAlertMemory.Configure(alertLifetime);
    }

    public bool ReceiveSquadAlert(
        EnemySquadAlert alert,
        Vector3 assignedSearchPoint,
        float receivedAt)
    {
        if (HasVisualContact ||
            alert.Timestamp <= latestVisualIntelTime ||
            !squadAlertMemory.TryAccept(
                alert,
                transform.position,
                receivedAt))
        {
            return false;
        }

        investigatingSound = false;
        hasSquadSearchAssignment = true;
        squadSearchDestination = assignedSearchPoint;
        awareness.ApplySharedAlert(
            alert.LastKnownPosition,
            alert.Confidence);
        lod ??= GetComponent<EnemyAiLodController>();
        lod?.RequestImmediateEvaluation(false);
        navigation.SetDestination(squadSearchDestination);
        return true;
    }

    public bool ShouldConsiderSquadAlert(float timestamp)
    {
        return !HasVisualContact && timestamp > latestVisualIntelTime;
    }

    public bool TryResolveSquadSearchPoint(
        Vector3 desired,
        out Vector3 resolved)
    {
        return navigation.TryResolveReachableDestination(
            desired,
            1.75f,
            out resolved);
    }

    public void SetTarget(Transform newTarget)
    {
        target = newTarget;
        RefreshSightColliderCache();
    }

    public void ResetForSpawn(Transform newTarget)
    {
        sightGeneration++;
        awareness.Reset();
        squadAlertMemory.Reset();
        target = newTarget;
        investigatingSound = false;
        hasSquadSearchAssignment = false;
        squadSearchDestination = Vector3.zero;
        latestVisualIntelTime = float.NegativeInfinity;
        latestSightTargetPosition = transform.position;
        HasVisualContact = false;
        lastSightCheckFrame = -1;
        SightCheckCount = 0;
        SaturatedSightQueryCount = 0;
        MaximumSightCheckLatencyFrames = 0;
        TotalSightCheckLatencyFrames = 0;
        SightCheckLatencySampleCount = 0;
        MaximumNearSightCheckLatencyFrames = 0;
        lastSightCheckLodTier = EnemyAiLodTier.Far;
        MaximumSightResultDelayFrames = 0;
        MaximumNearSightResultDelayFrames = 0;
        RefreshSightColliderCache();
    }

    public void PrepareForPool()
    {
        sightGeneration++;
        target = null;
        investigatingSound = false;
        hasSquadSearchAssignment = false;
        HasVisualContact = false;
    }

    public void Configure(
        float configuredSightDistance,
        float configuredFieldOfView,
        float configuredAlertSpeed,
        float configuredDecaySpeed,
        float configuredSearchDuration)
    {
        sightDistance = Mathf.Max(1f, configuredSightDistance);
        fieldOfView = Mathf.Clamp(
            configuredFieldOfView,
            1f,
            360f);
        alertSpeed = Mathf.Max(0.01f, configuredAlertSpeed);
        awarenessDecaySpeed =
            Mathf.Max(0.01f, configuredDecaySpeed);
        searchDuration =
            Mathf.Max(0.1f, configuredSearchDuration);
        awareness.Configure(
            alertSpeed,
            awarenessDecaySpeed,
            searchDuration);
    }

    public bool CanSeeTarget()
    {
        if (target == null)
        {
            return false;
        }

        Vector3 eye = transform.position + Vector3.up * 0.75f;
        Vector3 targetPoint =
            target.position + Vector3.up * 0.9f;
        Vector3 toTarget = targetPoint - eye;
        float squaredDistance = toTarget.sqrMagnitude;

        if (squaredDistance >
                sightDistance * sightDistance ||
            squaredDistance <= 0.000001f)
        {
            return false;
        }

        float distance = Mathf.Sqrt(squaredDistance);
        Vector3 sightDirection = toTarget / distance;
        float minimumDot = Mathf.Cos(
            fieldOfView * 0.5f * Mathf.Deg2Rad);

        if (Vector3.Dot(transform.forward, sightDirection) <
            minimumDot)
        {
            return false;
        }

        int hitCount = Physics.RaycastNonAlloc(
            eye,
            sightDirection,
            sightHits,
            distance + 0.1f,
            Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore);

        if (hitCount >= sightHits.Length)
        {
            SaturatedSightQueryCount++;
        }

        RaycastHit? nearestRelevantHit = null;

        for (int index = 0; index < hitCount; index++)
        {
            RaycastHit hit = sightHits[index];

            if (hit.transform == transform ||
                hit.transform.IsChildOf(transform))
            {
                continue;
            }

            if (!nearestRelevantHit.HasValue ||
                hit.distance < nearestRelevantHit.Value.distance)
            {
                nearestRelevantHit = hit;
            }
        }

        if (!nearestRelevantHit.HasValue)
        {
            return true;
        }

        Transform hitTransform = nearestRelevantHit.Value.transform;
        return hitTransform == target ||
            hitTransform.IsChildOf(target);
    }

    internal void PerformScheduledSightCheck()
    {
        bool visible = CanSeeTarget();
        CommitSightResult(
            visible,
            false,
            target != null ? target.position : transform.position,
            LodTier);
    }

    internal EnemySightQueryDescriptor CreateSightQueryDescriptor()
    {
        return new EnemySightQueryDescriptor(
            target != null,
            transform.position + Vector3.up * 0.75f,
            target != null
                ? target.position + Vector3.up * 0.9f
                : transform.position,
            target != null ? target.position : transform.position,
            transform.forward,
            sightDistance,
            Mathf.Cos(fieldOfView * 0.5f * Mathf.Deg2Rad),
            selfSightColliderIds,
            selfSightColliderCount,
            targetSightColliderIds,
            targetSightColliderCount);
    }

    internal bool ApplyBatchedSightResult(
        int generation,
        bool visible,
        bool saturated,
        int submittedFrame,
        Vector3 observedTargetPosition,
        EnemyAiLodTier submittedLodTier)
    {
        if (generation != sightGeneration || !isActiveAndEnabled)
        {
            return false;
        }

        ResolveHealth();

        if (health != null && health.IsDead)
        {
            return false;
        }

        MaximumSightResultDelayFrames = Mathf.Max(
            MaximumSightResultDelayFrames,
            Time.frameCount - submittedFrame);
        if (submittedLodTier == EnemyAiLodTier.Near)
        {
            MaximumNearSightResultDelayFrames = Mathf.Max(
                MaximumNearSightResultDelayFrames,
                Time.frameCount - submittedFrame);
        }
        CommitSightResult(
            visible,
            saturated,
            observedTargetPosition,
            submittedLodTier);
        return true;
    }

    private void CommitSightResult(
        bool visible,
        bool saturated,
        Vector3 observedTargetPosition,
        EnemyAiLodTier submittedLodTier)
    {
        if (lastSightCheckFrame >= 0)
        {
            int latencyFrames = Time.frameCount - lastSightCheckFrame;
            MaximumSightCheckLatencyFrames = Mathf.Max(
                MaximumSightCheckLatencyFrames,
                latencyFrames);
            TotalSightCheckLatencyFrames += latencyFrames;
            SightCheckLatencySampleCount++;
            if (submittedLodTier == EnemyAiLodTier.Near &&
                lastSightCheckLodTier == EnemyAiLodTier.Near)
            {
                MaximumNearSightCheckLatencyFrames = Mathf.Max(
                    MaximumNearSightCheckLatencyFrames,
                    Time.frameCount - lastSightCheckFrame);
            }
        }

        HasVisualContact = visible;

        if (visible)
        {
            latestSightTargetPosition = observedTargetPosition;
        }

        if (saturated)
        {
            SaturatedSightQueryCount++;
        }

        SightCheckCount++;
        lastSightCheckFrame = Time.frameCount;
        lastSightCheckLodTier = submittedLodTier;
        lod?.NotifySightCheck(Time.frameCount);
    }

    private void RefreshSightColliderCache()
    {
        selfSightColliderCount = CacheColliderIds(
            transform,
            selfSightColliderIds);
        targetSightColliderCount = CacheColliderIds(
            target,
            targetSightColliderIds);
    }

    private int CacheColliderIds(Transform root, EntityId[] destination)
    {
        if (root == null)
        {
            return 0;
        }

        sightColliderBuffer.Clear();
        root.GetComponentsInChildren(true, sightColliderBuffer);
        int count = Mathf.Min(
            destination.Length,
            sightColliderBuffer.Count);

        for (int index = 0; index < count; index++)
        {
            destination[index] = sightColliderBuffer[index].GetEntityId();
        }

        return count;
    }

    private void ResolveHealth()
    {
        if (healthLookupCompleted)
        {
            return;
        }

        health = GetComponent<Health>();
        healthLookupCompleted = true;
    }

    private void HandleSound(SoundStimulus stimulus)
    {
        float strength = stimulus.StrengthAt(transform.position) *
            hearingSensitivity;

        if (strength <= 0f)
        {
            return;
        }

        awareness.Hear(stimulus.Position, strength);
        investigatingSound = true;
        lod ??= GetComponent<EnemyAiLodController>();
        lod?.RequestImmediateEvaluation(false);
        navigation.SetDestination(stimulus.Position);
    }

    private void OnDrawGizmosSelected()
    {
        Vector3 eye = transform.position + Vector3.up * 0.75f;
        Gizmos.color = new Color(1f, 0.75f, 0.1f, 0.35f);
        Gizmos.DrawWireSphere(eye, sightDistance);
        Quaternion left = Quaternion.Euler(
            0f,
            -fieldOfView * 0.5f,
            0f);
        Quaternion right = Quaternion.Euler(
            0f,
            fieldOfView * 0.5f,
            0f);
        Gizmos.DrawRay(
            eye,
            left * transform.forward * sightDistance);
        Gizmos.DrawRay(
            eye,
            right * transform.forward * sightDistance);
    }
}
