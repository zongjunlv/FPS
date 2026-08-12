using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(-200)]
public sealed class EnemyPerceptionScheduler : MonoBehaviour
{
    [SerializeField, Min(1)] private int maxChecksPerFrame = 4;

    private readonly List<EnemyPerceptionController> members = new();
    private int nextMemberIndex;

    public static EnemyPerceptionScheduler Instance { get; private set; }
    public int MaxChecksPerFrame => maxChecksPerFrame;
    public int RegisteredCount => members.Count;
    public int LastFrameCheckCount { get; private set; }
    public long TotalCheckCount { get; private set; }

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
    }

    public void Register(EnemyPerceptionController member)
    {
        if (member == null || members.Contains(member))
        {
            return;
        }

        members.Add(member);
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

        if (Time.timeScale <= 0f)
        {
            return;
        }

        RemoveDestroyedMembers();

        int scheduledCount = Mathf.Min(
            maxChecksPerFrame,
            members.Count);

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

            member.PerformScheduledSightCheck();
            LastFrameCheckCount++;
            TotalCheckCount++;
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
