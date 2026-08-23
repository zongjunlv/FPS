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
    private EnemyPerceptionScheduler perceptionScheduler;
    private int lastSightCheckFrame = -1;

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
    }

    private void OnEnable()
    {
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
        if (target == null)
        {
            GameObject player =
                GameObject.FindGameObjectWithTag("Player");
            target = player != null ? player.transform : null;
        }
    }

    private void OnDisable()
    {
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

        if (HasVisualContact)
        {
            investigatingSound = false;
            hasSquadSearchAssignment = false;
            awareness.Observe(target.position, Time.deltaTime);

            if (lastSightCheckFrame == Time.frameCount)
            {
                latestVisualIntelTime = Time.time;
            }

            if (awareness.State == EnemyAwarenessState.Alert)
            {
                squadCoordinator?.TryBroadcast(
                    this,
                    target.position,
                    1f,
                    Time.time);
            }
        }
        else if (awareness.State == EnemyAwarenessState.Search)
        {
            awareness.AdvanceSearch(
                Time.deltaTime,
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
            awareness.Tick(Time.deltaTime);
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
        navigation.SetDestination(squadSearchDestination);
        return true;
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
    }

    public void ResetForSpawn(Transform newTarget)
    {
        awareness.Reset();
        squadAlertMemory.Reset();
        target = newTarget;
        investigatingSound = false;
        hasSquadSearchAssignment = false;
        squadSearchDestination = Vector3.zero;
        latestVisualIntelTime = float.NegativeInfinity;
        HasVisualContact = false;
        lastSightCheckFrame = -1;
        SightCheckCount = 0;
        SaturatedSightQueryCount = 0;
        MaximumSightCheckLatencyFrames = 0;
    }

    public void PrepareForPool()
    {
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
        if (lastSightCheckFrame >= 0)
        {
            MaximumSightCheckLatencyFrames = Mathf.Max(
                MaximumSightCheckLatencyFrames,
                Time.frameCount - lastSightCheckFrame);
        }

        HasVisualContact = CanSeeTarget();
        SightCheckCount++;
        lastSightCheckFrame = Time.frameCount;
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
