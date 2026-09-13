using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public sealed class ModuleEnemySpawnPointResolver : MonoBehaviour,
    IEnemySpawnPointResolver, IDirectedEnemySpawnPointResolver
{
    private NavMeshEnemySpawnPointResolver fallback;
    private float safetyDistance = 10f;
    private float enemySpacing = 3f;
    private int cursor;

    public string LastFailureReason { get; private set; } = string.Empty;

    private void Awake()
    {
        fallback = GetComponent<NavMeshEnemySpawnPointResolver>();
        fallback ??= gameObject.AddComponent<NavMeshEnemySpawnPointResolver>();
    }

    public void Configure(WaveDefinition definition)
    {
        safetyDistance = definition.PlayerSafetyDistance;
        enemySpacing = definition.EnemySpacing;
        cursor = 0;
        fallback.Configure(definition);
    }

    public bool TryResolve(
        Transform player,
        IReadOnlyList<Vector3> occupiedPositions,
        out Vector3 spawnPoint)
    {
        IReadOnlyList<Vector3> anchors =
            CityNewModularLayoutBootstrap.Active?.EnemySpawnPoints;
        if (TryAnchors(player, occupiedPositions, anchors, null, out spawnPoint))
            return true;
        bool resolved = fallback.TryResolve(player, occupiedPositions, out spawnPoint);
        LastFailureReason = resolved ? string.Empty : fallback.LastFailureReason;
        return resolved;
    }

    public bool TryResolveDirected(
        Transform player,
        IReadOnlyList<Vector3> occupiedPositions,
        int signedDirectionDegrees,
        int retryIndex,
        out Vector3 spawnPoint)
    {
        Vector3 desiredDirection = player != null
            ? Quaternion.Euler(0f, signedDirectionDegrees, 0f) * player.forward
            : Vector3.forward;
        IReadOnlyList<Vector3> anchors =
            CityNewModularLayoutBootstrap.Active?.EnemySpawnPoints;
        if (TryAnchors(player, occupiedPositions, anchors, desiredDirection,
                out spawnPoint))
            return true;
        bool resolved = fallback.TryResolveDirected(
            player, occupiedPositions, signedDirectionDegrees, retryIndex,
            out spawnPoint);
        LastFailureReason = resolved ? string.Empty : fallback.LastFailureReason;
        return resolved;
    }

    private bool TryAnchors(
        Transform player,
        IReadOnlyList<Vector3> occupied,
        IReadOnlyList<Vector3> anchors,
        Vector3? desiredDirection,
        out Vector3 result)
    {
        result = default;
        if (player == null || anchors == null || anchors.Count == 0 ||
            !RuntimeNavMeshBootstrap.IsSceneReady ||
            !NavMesh.SamplePosition(player.position, out NavMeshHit playerHit,
                4f, NavMesh.AllAreas))
        {
            LastFailureReason = "模块出生锚点或玩家导航位置尚未准备完成。";
            return false;
        }
        var order = new List<int>(anchors.Count);
        for (int index = 0; index < anchors.Count; index++) order.Add(index);
        if (desiredDirection.HasValue)
        {
            order.Sort((left, right) =>
            {
                float a = DirectionScore(player.position, anchors[left], desiredDirection.Value);
                float b = DirectionScore(player.position, anchors[right], desiredDirection.Value);
                return b.CompareTo(a);
            });
        }
        for (int attempt = 0; attempt < order.Count; attempt++)
        {
            int index = order[(cursor + attempt) % order.Count];
            if (!TryValidate(player, playerHit, anchors[index], occupied, out result))
                continue;
            cursor = (index + 1) % order.Count;
            LastFailureReason = string.Empty;
            return true;
        }
        LastFailureReason = "所有模块出生锚点都处于安全半径、占位冲突或不可达状态。";
        return false;
    }

    private bool TryValidate(
        Transform player,
        NavMeshHit playerHit,
        Vector3 desired,
        IReadOnlyList<Vector3> occupied,
        out Vector3 result)
    {
        result = default;
        if (!NavMesh.SamplePosition(desired, out NavMeshHit hit, 3f, NavMesh.AllAreas) ||
            HorizontalSqr(hit.position, player.position) < safetyDistance * safetyDistance)
            return false;
        if (occupied != null)
            for (int index = 0; index < occupied.Count; index++)
                if (HorizontalSqr(hit.position, occupied[index]) < enemySpacing * enemySpacing)
                    return false;
        var path = new NavMeshPath();
        if (!NavMesh.CalculatePath(playerHit.position, hit.position, NavMesh.AllAreas, path) ||
            path.status != NavMeshPathStatus.PathComplete)
            return false;
        result = hit.position;
        return true;
    }

    private static float DirectionScore(Vector3 origin, Vector3 point, Vector3 desired)
    {
        Vector3 direction = point - origin;
        direction.y = 0f;
        return direction.sqrMagnitude > 0.001f
            ? Vector3.Dot(direction.normalized, desired.normalized)
            : -1f;
    }

    private static float HorizontalSqr(Vector3 a, Vector3 b)
    {
        float x = a.x - b.x;
        float z = a.z - b.z;
        return x * x + z * z;
    }
}
