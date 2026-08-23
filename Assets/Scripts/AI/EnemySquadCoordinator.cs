using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public readonly struct EnemyAlertDebugRelation
{
    public EnemyAlertDebugRelation(
        Vector3 receiverPosition,
        Vector3 searchPosition,
        bool accepted)
    {
        ReceiverPosition = receiverPosition;
        SearchPosition = searchPosition;
        Accepted = accepted;
    }

    public Vector3 ReceiverPosition { get; }
    public Vector3 SearchPosition { get; }
    public bool Accepted { get; }
}

public sealed class EnemySquadCoordinator : MonoBehaviour
{
    private readonly List<EnemyPerceptionController> members = new();
    private readonly Dictionary<EnemyPerceptionController, float>
        lastBroadcastTime = new();
    private readonly List<EnemyAlertDebugRelation> debugRelations = new();
    private readonly List<Vector3> reservedSearchPoints = new();
    private float alertRadius = 18f;
    private float broadcastCooldown = 1f;
    private float alertLifetime = 3f;
    private float searchRadius = 4f;
    private int nextSequence;
    private EnemySquadAlertDebugView debugView;

    public static EnemySquadCoordinator Instance { get; private set; }
    public int BroadcastCount { get; private set; }
    public int LastRecipientCount { get; private set; }
    public int DebugRelationCount => debugRelations.Count;
    public EnemySquadAlert LastAlert { get; private set; }
    public bool DebugVisualizationActive =>
        debugView != null && debugView.IsActive;
    public float LastDebugRadius =>
        debugView != null ? debugView.DisplayedRadius : 0f;

    public static EnemySquadCoordinator EnsureForActiveScene()
    {
        if (Instance != null)
        {
            return Instance;
        }

        GameObject coordinatorObject =
            new GameObject("Enemy Squad Coordinator");
        return coordinatorObject.AddComponent<EnemySquadCoordinator>();
    }

    private void Awake()
    {
        Instance = this;
        debugView = GetComponent<EnemySquadAlertDebugView>();

        if (debugView == null)
        {
            debugView =
                gameObject.AddComponent<EnemySquadAlertDebugView>();
        }

        if (SceneManager.GetActiveScene().name == "CityNew" &&
            GetComponent<CityNewWaveBootstrap>() == null)
        {
            gameObject.AddComponent<CityNewWaveBootstrap>();
        }

        if (SceneManager.GetActiveScene().name == "CityNew" &&
            GetComponent<CityNewEnemySquadBootstrap>() == null)
        {
            gameObject.AddComponent<CityNewEnemySquadBootstrap>();
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    public void Configure(
        float configuredRadius,
        float configuredCooldown,
        float configuredLifetime,
        float configuredSearchRadius)
    {
        alertRadius = Mathf.Max(0.1f, configuredRadius);
        broadcastCooldown = Mathf.Max(0f, configuredCooldown);
        alertLifetime = Mathf.Max(0f, configuredLifetime);
        searchRadius = Mathf.Max(0.5f, configuredSearchRadius);
        lastBroadcastTime.Clear();
        BroadcastCount = 0;
        LastRecipientCount = 0;
        nextSequence = 0;
        debugRelations.Clear();
        debugView?.Hide();

        foreach (EnemyPerceptionController member in members)
        {
            member?.BindSquadCoordinator(this, alertLifetime);
        }
    }

    public void Register(EnemyPerceptionController member)
    {
        if (member == null || members.Contains(member))
        {
            return;
        }

        members.Add(member);
        member.BindSquadCoordinator(this, alertLifetime);
    }

    public void Unregister(EnemyPerceptionController member)
    {
        if (member == null)
        {
            return;
        }

        members.Remove(member);
        lastBroadcastTime.Remove(member);
    }

    public bool TryBroadcast(
        EnemyPerceptionController source,
        Vector3 lastKnownPosition,
        float confidence,
        float timestamp)
    {
        if (source == null || confidence <= 0f)
        {
            return false;
        }

        Register(source);

        if (lastBroadcastTime.TryGetValue(
                source,
                out float previousTime) &&
            timestamp < previousTime + broadcastCooldown)
        {
            return false;
        }

        lastBroadcastTime[source] = timestamp;
        LastAlert = new EnemySquadAlert(
            source.gameObject,
            source.transform.position,
            lastKnownPosition,
            timestamp,
            confidence,
            alertRadius,
            ++nextSequence);
        BroadcastCount++;
        LastRecipientCount = 0;
        debugRelations.Clear();
        reservedSearchPoints.Clear();
        int receiverSlot = 0;

        for (int index = members.Count - 1; index >= 0; index--)
        {
            EnemyPerceptionController receiver = members[index];

            if (receiver == null)
            {
                members.RemoveAt(index);
                continue;
            }

            if (receiver == source)
            {
                continue;
            }

            Vector3 searchPoint = ResolveSearchPoint(
                receiver,
                lastKnownPosition,
                receiverSlot,
                Mathf.Max(1, members.Count - 1),
                reservedSearchPoints);
            bool accepted = receiver.ReceiveSquadAlert(
                LastAlert,
                searchPoint,
                timestamp);
            debugRelations.Add(
                new EnemyAlertDebugRelation(
                    receiver.transform.position,
                    searchPoint,
                    accepted));

            if (accepted)
            {
                reservedSearchPoints.Add(searchPoint);
                LastRecipientCount++;
                receiverSlot++;
            }
        }

        debugView?.Show(LastAlert, debugRelations);
        return true;
    }

    private Vector3 ResolveSearchPoint(
        EnemyPerceptionController receiver,
        Vector3 center,
        int slot,
        int receiverCount,
        List<Vector3> reserved)
    {
        int candidateCount = Mathf.Max(8, receiverCount * 4);

        for (int attempt = 0; attempt < candidateCount; attempt++)
        {
            int candidateIndex =
                slot + attempt * Mathf.Max(1, receiverCount);
            float angle = candidateIndex * Mathf.PI * 2f /
                candidateCount;
            float ring = searchRadius *
                (1f + (attempt / Mathf.Max(1, receiverCount)) * 0.35f);
            Vector3 candidate = center + new Vector3(
                Mathf.Cos(angle),
                0f,
                Mathf.Sin(angle)) * ring;

            if (!receiver.TryResolveSquadSearchPoint(
                    candidate,
                    out Vector3 resolved) ||
                IsReserved(resolved, reserved))
            {
                continue;
            }

            return resolved;
        }

        return center;
    }

    private static bool IsReserved(
        Vector3 candidate,
        List<Vector3> reserved)
    {
        foreach (Vector3 point in reserved)
        {
            if (Vector3.Distance(candidate, point) < 1.5f)
            {
                return true;
            }
        }

        return false;
    }

    private void OnDrawGizmos()
    {
        if (BroadcastCount <= 0)
        {
            return;
        }

        Gizmos.color = new Color(1f, 0.25f, 0.08f, 0.45f);
        Gizmos.DrawWireSphere(
            LastAlert.SourcePosition,
            LastAlert.Radius);
        Gizmos.DrawSphere(LastAlert.LastKnownPosition, 0.25f);

    }
}
