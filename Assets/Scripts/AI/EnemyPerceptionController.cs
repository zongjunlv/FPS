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
    [Header("Debug")]
    [SerializeField] private bool showDebugOverlay = true;

    private EnemyAwarenessStateMachine awareness;
    private EnemyNavigationController navigation;
    private CombatSoundEventChannel soundEvents;
    private Transform target;
    private bool investigatingSound;
    private readonly RaycastHit[] sightHits = new RaycastHit[16];

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
    }

    private void OnEnable()
    {
        if (soundEvents != null)
        {
            soundEvents.SoundPublished += HandleSound;
        }
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
    }

    private void Update()
    {
        HasVisualContact = CanSeeTarget();

        if (HasVisualContact)
        {
            investigatingSound = false;
            awareness.Observe(target.position, Time.deltaTime);
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
            navigation.TickPatrol();
        }
        else if (awareness.State != EnemyAwarenessState.Alert)
        {
            navigation.SetDestination(
                awareness.LastKnownPosition);
        }
    }

    public void SetTarget(Transform newTarget)
    {
        target = newTarget;
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
        float distance = toTarget.magnitude;

        if (distance > sightDistance ||
            distance <= 0.001f)
        {
            return false;
        }

        float angle = Vector3.Angle(
            transform.forward,
            toTarget);

        if (angle > fieldOfView * 0.5f)
        {
            return false;
        }

        int hitCount = Physics.RaycastNonAlloc(
            eye,
            toTarget.normalized,
            sightHits,
            distance + 0.1f,
            Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore);
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

    private void OnGUI()
    {
        if (!showDebugOverlay)
        {
            return;
        }

        Vector3 screenPoint = Camera.main != null
            ? Camera.main.WorldToScreenPoint(
                transform.position + Vector3.up * 1.8f)
            : Vector3.zero;

        if (screenPoint.z <= 0f)
        {
            return;
        }

        Rect rect = new Rect(
            screenPoint.x - 80f,
            Screen.height - screenPoint.y,
            160f,
            42f);
        GUI.color = State switch
        {
            EnemyAwarenessState.Alert => Color.red,
            EnemyAwarenessState.Search => Color.yellow,
            EnemyAwarenessState.Suspicious =>
                new Color(1f, 0.65f, 0.1f),
            _ => Color.cyan
        };
        GUI.Label(
            rect,
            $"{State}  {Awareness:P0}\n" +
            $"目标: {Destination.x:F1}, {Destination.z:F1}");
        GUI.color = Color.white;
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
