using UnityEngine;

[DefaultExecutionOrder(-180)]
[DisallowMultipleComponent]
public sealed class EnemyAiLodController : MonoBehaviour
{
    private const int ChannelCount = 4;

    [SerializeField, Min(1f)] private float nearDistance = 12f;
    [SerializeField, Min(2f)] private float midDistance = 30f;
    [SerializeField, Min(0.1f)] private float damagePromotionSeconds = 2f;

    private readonly int[] lastExecutionFrames = new int[ChannelCount];
    private readonly float[] lastExecutionTimes = new float[ChannelCount];
    private EnemyPerceptionController perception;
    private EnemyNavigationController navigation;
    private Health health;
    private Transform target;
    private EnemyAiLodPolicy policy;
    private float recentlyDamagedUntil;
    private int forceChannelsThroughFrame = -1;
    private int forceSightThroughFrame = -1;
    private int stableScheduleSlot;
    private int lastSightCheckFrame = -1;
    private bool healthBound;

    public static bool GlobalEnabled { get; private set; } = true;
    public EnemyAiLodTier CurrentTier { get; private set; } =
        EnemyAiLodTier.Near;
    public EnemyAiLodTier PreviousTier { get; private set; } =
        EnemyAiLodTier.Near;
    public float CurrentDistance { get; private set; }
    public int StableScheduleSlot => stableScheduleSlot;
    public int TierChangeCount { get; private set; }
    public int ImmediatePromotionCount { get; private set; }
    public int ExecutedDecisionTicks { get; private set; }
    public int SkippedDecisionTicks { get; private set; }
    public int MaximumDecisionLatencyFrames { get; private set; }
    public int SightIntervalFrames => GlobalEnabled
        ? EnemyAiLodPolicy.SightInterval(CurrentTier)
        : 1;

    public static void SetGlobalEnabled(bool enabled)
    {
        GlobalEnabled = enabled;
    }

    private void Awake()
    {
        policy = new EnemyAiLodPolicy(nearDistance, midDistance);
        ResetTickHistory();
    }

    private void Start()
    {
        EnsureBindings();
        RefreshTier();
    }

    private void OnEnable()
    {
        forceChannelsThroughFrame = Time.frameCount + 1;
        forceSightThroughFrame = Time.frameCount + 1;
    }

    private void OnDisable()
    {
        navigation?.SetLodSuspended(false);
    }

    private void OnDestroy()
    {
        UnbindHealth();
    }

    private void Update()
    {
        if (Time.timeScale <= 0f)
        {
            return;
        }

        EnsureBindings();
        RefreshTier();
    }

    public void Configure(float near, float mid, float damagePromotion)
    {
        nearDistance = Mathf.Max(1f, near);
        midDistance = Mathf.Max(nearDistance + 1f, mid);
        damagePromotionSeconds = Mathf.Max(0.1f, damagePromotion);
        policy = new EnemyAiLodPolicy(nearDistance, midDistance);
        RefreshTier();
    }

    public void BindScheduleSlot(int slot)
    {
        stableScheduleSlot = Mathf.Max(0, slot);
        forceSightThroughFrame = Time.frameCount + 1;
    }

    public void ResetForSpawn(Transform newTarget)
    {
        EnsureBindings();
        target = newTarget;
        recentlyDamagedUntil = float.NegativeInfinity;
        CurrentTier = EnemyAiLodTier.Near;
        PreviousTier = EnemyAiLodTier.Near;
        TierChangeCount = 0;
        ImmediatePromotionCount = 0;
        ExecutedDecisionTicks = 0;
        SkippedDecisionTicks = 0;
        MaximumDecisionLatencyFrames = 0;
        lastSightCheckFrame = -1;
        forceChannelsThroughFrame = Time.frameCount + 1;
        forceSightThroughFrame = Time.frameCount + 1;
        ResetTickHistory();
        navigation?.SetLodSuspended(false);
    }

    public void PrepareForPool()
    {
        target = null;
        recentlyDamagedUntil = float.NegativeInfinity;
        CurrentTier = EnemyAiLodTier.Near;
        PreviousTier = EnemyAiLodTier.Near;
        forceChannelsThroughFrame = -1;
        forceSightThroughFrame = -1;
        lastSightCheckFrame = -1;
        navigation?.SetLodSuspended(false);
    }

    public bool TryAcquireTick(
        EnemyAiLodChannel channel,
        out float elapsedTime)
    {
        int channelIndex = (int)channel;
        int currentFrame = Time.frameCount;
        int interval = GlobalEnabled
            ? EnemyAiLodPolicy.DecisionInterval(CurrentTier)
            : 1;
        bool forced = currentFrame <= forceChannelsThroughFrame;
        bool scheduled = forced ||
            currentFrame - lastExecutionFrames[channelIndex] >= interval ||
            EnemyAiLodPolicy.IsScheduledFrame(
                currentFrame,
                stableScheduleSlot,
                interval) &&
            lastExecutionFrames[channelIndex] < 0;

        if (!scheduled)
        {
            elapsedTime = 0f;
            SkippedDecisionTicks++;
            return false;
        }

        int previousFrame = lastExecutionFrames[channelIndex];
        elapsedTime = lastExecutionTimes[channelIndex] >= 0f
            ? Mathf.Max(
                Time.deltaTime,
                Time.time - lastExecutionTimes[channelIndex])
            : Time.deltaTime;
        lastExecutionFrames[channelIndex] = currentFrame;
        lastExecutionTimes[channelIndex] = Time.time;
        ExecutedDecisionTicks++;

        if (previousFrame >= 0)
        {
            MaximumDecisionLatencyFrames = Mathf.Max(
                MaximumDecisionLatencyFrames,
                currentFrame - previousFrame);
        }

        return true;
    }

    public bool IsSightCheckDue(int frame)
    {
        if (!GlobalEnabled || frame <= forceSightThroughFrame)
        {
            return true;
        }

        int interval = SightIntervalFrames;

        if (lastSightCheckFrame < 0)
        {
            return EnemyAiLodPolicy.IsScheduledFrame(
                frame,
                stableScheduleSlot,
                interval);
        }

        return frame - lastSightCheckFrame >= interval;
    }

    public bool IsSightCheckUrgent(int frame)
    {
        return GlobalEnabled && frame <= forceSightThroughFrame;
    }

    public void NotifySightCheck(int frame)
    {
        lastSightCheckFrame = frame;
        forceSightThroughFrame = -1;
        RefreshTier();
    }

    public void RequestImmediateEvaluation(bool promoteToNear)
    {
        if (promoteToNear)
        {
            recentlyDamagedUntil = Mathf.Max(
                recentlyDamagedUntil,
                Time.time + damagePromotionSeconds);
        }

        forceChannelsThroughFrame = Time.frameCount + 1;
        forceSightThroughFrame = Time.frameCount + 1;
        RefreshTier();
    }

    private void RefreshTier()
    {
        target = perception != null && perception.Target != null
            ? perception.Target
            : target;
        CurrentDistance = target != null
            ? Vector3.Distance(transform.position, target.position)
            : float.PositiveInfinity;
        EnemyAiLodTier next = !GlobalEnabled
            ? EnemyAiLodTier.Near
            : policy.Classify(
                CurrentDistance,
                perception != null && perception.HasVisualContact,
                Time.time <= recentlyDamagedUntil,
                perception != null
                    ? perception.State
                    : EnemyAwarenessState.Patrol);

        if (next == CurrentTier)
        {
            navigation?.SetLodSuspended(
                next == EnemyAiLodTier.Far);
            return;
        }

        PreviousTier = CurrentTier;
        CurrentTier = next;
        TierChangeCount++;

        if ((int)next < (int)PreviousTier)
        {
            forceChannelsThroughFrame = Time.frameCount + 1;
            forceSightThroughFrame = Time.frameCount + 1;
            ImmediatePromotionCount++;
        }

        navigation?.SetLodSuspended(next == EnemyAiLodTier.Far);
    }

    private void EnsureBindings()
    {
        perception ??= GetComponent<EnemyPerceptionController>();
        navigation ??= GetComponent<EnemyNavigationController>();

        if (healthBound && health != null)
        {
            return;
        }

        Health resolvedHealth = GetComponent<Health>();

        UnbindHealth();
        health = resolvedHealth;

        if (health != null)
        {
            health.Damaged += HandleDamaged;
            healthBound = true;
        }
    }

    private void UnbindHealth()
    {
        if (health != null && healthBound)
        {
            health.Damaged -= HandleDamaged;
        }

        healthBound = false;
    }

    private void HandleDamaged(DamageInfo damage)
    {
        recentlyDamagedUntil = Time.time + damagePromotionSeconds;
        RefreshTier();
    }

    private void ResetTickHistory()
    {
        for (int index = 0; index < ChannelCount; index++)
        {
            lastExecutionFrames[index] = -1;
            lastExecutionTimes[index] = -1f;
        }
    }
}
