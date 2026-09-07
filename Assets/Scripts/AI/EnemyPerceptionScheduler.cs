using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(-200)]
public sealed class EnemyPerceptionScheduler : MonoBehaviour
{
    [SerializeField, Min(1)] private int maxChecksPerFrame = 4;

    private readonly List<EnemyPerceptionController> members = new();
    private readonly List<EnemyPerceptionController> scheduledMembers = new();
    private EnemySightBatchProcessor batchProcessor;
    private int nextMemberIndex;
    private int nextScheduleSlot;

    public static EnemyPerceptionScheduler Instance { get; private set; }
    public static bool BatchEnabled { get; private set; } = true;
    public int MaxChecksPerFrame => maxChecksPerFrame;
    public int RegisteredCount => members.Count;
    public int LastFrameCheckCount { get; private set; }
    public long TotalCheckCount { get; private set; }
    public int LastNearCheckCount { get; private set; }
    public int LastMidCheckCount { get; private set; }
    public int LastFarCheckCount { get; private set; }
    public long ScheduledBatchCount =>
        batchProcessor?.ScheduledBatchCount ?? 0;
    public long CompletedBatchCount =>
        batchProcessor?.CompletedBatchCount ?? 0;
    public long ScheduledCommandCount =>
        batchProcessor?.ScheduledCommandCount ?? 0;
    public long DiscardedStaleResultCount =>
        batchProcessor?.DiscardedStaleResultCount ?? 0;
    public int PeakBatchSize => batchProcessor?.PeakBatchSize ?? 0;
    public int PendingBatchCount => batchProcessor?.PendingCount ?? 0;

    public static EnemyPerceptionScheduler EnsureForActiveScene()
    {
        if (Instance != null)
        {
            return Instance;
        }

        GameObject schedulerObject =
            new("Enemy Perception Scheduler");
        return schedulerObject.AddComponent<EnemyPerceptionScheduler>();
    }

    public static void SetBatchEnabled(bool enabled)
    {
        Instance?.CompletePendingBatch();
        BatchEnabled = enabled;
    }

    private void Awake()
    {
        Instance = this;
        batchProcessor = new EnemySightBatchProcessor(maxChecksPerFrame);
    }

    private void OnDestroy()
    {
        batchProcessor?.Dispose();
        batchProcessor = null;

        if (Instance == this)
        {
            Instance = null;
        }
    }

    public void Configure(int configuredMaxChecksPerFrame)
    {
        CompletePendingBatch();
        maxChecksPerFrame = Mathf.Max(1, configuredMaxChecksPerFrame);
        nextMemberIndex = 0;
        nextScheduleSlot = 0;
        ResetMetrics();
    }

    public void ResetMetrics()
    {
        CompletePendingBatch();
        LastFrameCheckCount = 0;
        TotalCheckCount = 0;
        LastNearCheckCount = 0;
        LastMidCheckCount = 0;
        LastFarCheckCount = 0;
        batchProcessor?.ResetMetrics();
    }

    public void Register(EnemyPerceptionController member)
    {
        if (member == null || members.Contains(member))
        {
            return;
        }

        members.Add(member);
        member.Lod?.BindScheduleSlot(nextScheduleSlot++);
    }

    public void Unregister(EnemyPerceptionController member)
    {
        int removedIndex = members.IndexOf(member);

        if (removedIndex < 0)
        {
            return;
        }

        members.RemoveAt(removedIndex);

        if (removedIndex < nextMemberIndex)
        {
            nextMemberIndex--;
        }

        if (nextMemberIndex >= members.Count)
        {
            nextMemberIndex = 0;
        }
    }

    private void Update()
    {
        LastFrameCheckCount = 0;
        LastNearCheckCount = 0;
        LastMidCheckCount = 0;
        LastFarCheckCount = 0;

        if (Time.timeScale <= 0f)
        {
            return;
        }

        RemoveDestroyedMembers();

        if (!BatchEnabled)
        {
            RunSynchronousSchedule();
            return;
        }

        CompletePendingBatch();
        CollectScheduledMembers();
        batchProcessor ??= new EnemySightBatchProcessor(maxChecksPerFrame);
        batchProcessor.Schedule(scheduledMembers, Time.frameCount);
    }

    private void CollectScheduledMembers()
    {
        scheduledMembers.Clear();

        if (!EnemyAiLodController.GlobalEnabled)
        {
            CollectLegacyMembers();
            return;
        }

        int currentFrame = Time.frameCount;

        for (int index = 0; index < members.Count; index++)
        {
            EnemyPerceptionController member = members[index];

            if (member == null || !member.isActiveAndEnabled ||
                member.Lod == null ||
                !member.Lod.IsSightCheckUrgent(currentFrame))
            {
                continue;
            }

            scheduledMembers.Add(member);
        }

        int inspected = 0;
        int scheduled = 0;

        while (inspected < members.Count && scheduled < maxChecksPerFrame)
        {
            if (nextMemberIndex >= members.Count)
            {
                nextMemberIndex = 0;
            }

            EnemyPerceptionController member = members[nextMemberIndex++];
            inspected++;

            if (member == null || !member.isActiveAndEnabled ||
                scheduledMembers.Contains(member) ||
                member.Lod != null &&
                !member.Lod.IsSightCheckDue(
                    currentFrame + (BatchEnabled ? 1 : 0)))
            {
                continue;
            }

            scheduledMembers.Add(member);
            scheduled++;
        }
    }

    private void CollectLegacyMembers()
    {
        int inspected = 0;

        while (inspected < members.Count &&
               scheduledMembers.Count < maxChecksPerFrame)
        {
            if (nextMemberIndex >= members.Count)
            {
                nextMemberIndex = 0;
            }

            EnemyPerceptionController member = members[nextMemberIndex++];
            inspected++;

            if (member != null && member.isActiveAndEnabled)
            {
                scheduledMembers.Add(member);
            }
        }
    }

    private void RunSynchronousSchedule()
    {
        CollectScheduledMembers();

        for (int index = 0; index < scheduledMembers.Count; index++)
        {
            RecordSynchronousCheck(scheduledMembers[index]);
        }
    }

    private void RecordSynchronousCheck(EnemyPerceptionController member)
    {
        member.PerformScheduledSightCheck();
        LastFrameCheckCount++;
        TotalCheckCount++;

        switch (member.LodTier)
        {
            case EnemyAiLodTier.Near:
                LastNearCheckCount++;
                break;
            case EnemyAiLodTier.Mid:
                LastMidCheckCount++;
                break;
            default:
                LastFarCheckCount++;
                break;
        }
    }

    private void CompletePendingBatch()
    {
        if (batchProcessor == null || !batchProcessor.HasPending)
        {
            return;
        }

        int applied = batchProcessor.CompleteAndApply(
            out int near,
            out int mid,
            out int far);
        LastFrameCheckCount += applied;
        TotalCheckCount += applied;
        LastNearCheckCount += near;
        LastMidCheckCount += mid;
        LastFarCheckCount += far;
    }

    private void RemoveDestroyedMembers()
    {
        for (int index = members.Count - 1; index >= 0; index--)
        {
            if (members[index] == null)
            {
                members.RemoveAt(index);
            }
        }

        if (nextMemberIndex >= members.Count)
        {
            nextMemberIndex = 0;
        }
    }
}
