using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(-200)]
public sealed class EnemyPerceptionScheduler : MonoBehaviour
{
    [SerializeField, Min(1)] private int maxChecksPerFrame = 4;

    private readonly List<EnemyPerceptionController> members = new();
    private int nextMemberIndex;
    private int nextScheduleSlot;

    public static EnemyPerceptionScheduler Instance { get; private set; }
    public int MaxChecksPerFrame => maxChecksPerFrame;
    public int RegisteredCount => members.Count;
    public int LastFrameCheckCount { get; private set; }
    public long TotalCheckCount { get; private set; }
    public int LastNearCheckCount { get; private set; }
    public int LastMidCheckCount { get; private set; }
    public int LastFarCheckCount { get; private set; }

    public static EnemyPerceptionScheduler EnsureForActiveScene()
    {
        if (Instance != null)
        {
            return Instance;
        }

        GameObject schedulerObject =
            new GameObject("Enemy Perception Scheduler");
        return schedulerObject.AddComponent<EnemyPerceptionScheduler>();
    }

    private void Awake()
    {
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    public void Configure(int configuredMaxChecksPerFrame)
    {
        maxChecksPerFrame = Mathf.Max(1, configuredMaxChecksPerFrame);
        nextMemberIndex = 0;
        LastFrameCheckCount = 0;
        TotalCheckCount = 0;
        nextScheduleSlot = 0;
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

        if (!EnemyAiLodController.GlobalEnabled)
        {
            RunLegacySchedule();
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

            PerformCheck(member);
        }

        int inspected = 0;

        int scheduled = 0;

        while (inspected < members.Count &&
            scheduled < maxChecksPerFrame)
        {
            if (nextMemberIndex >= members.Count)
            {
                nextMemberIndex = 0;
            }

            EnemyPerceptionController member =
                members[nextMemberIndex++];
            inspected++;

            if (member == null || !member.isActiveAndEnabled ||
                member.Lod != null &&
                !member.Lod.IsSightCheckDue(currentFrame))
            {
                continue;
            }

            PerformCheck(member);
            scheduled++;
        }
    }

    private void RunLegacySchedule()
    {
        int scheduledCount = Mathf.Min(maxChecksPerFrame, members.Count);

        for (int index = 0; index < scheduledCount; index++)
        {
            if (nextMemberIndex >= members.Count)
            {
                nextMemberIndex = 0;
            }

            EnemyPerceptionController member =
                members[nextMemberIndex];
            nextMemberIndex++;

            if (member == null || !member.isActiveAndEnabled)
            {
                continue;
            }

            PerformCheck(member);
        }
    }

    private void PerformCheck(EnemyPerceptionController member)
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
