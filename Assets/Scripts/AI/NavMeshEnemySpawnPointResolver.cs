using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public interface IEnemySpawnPointResolver
{
    bool TryResolve(
        Transform player,
        IReadOnlyList<Vector3> occupiedPositions,
        out Vector3 spawnPoint);
}

public sealed class NavMeshEnemySpawnPointResolver : MonoBehaviour,
    IEnemySpawnPointResolver
{
    private const float GoldenAngle = 137.50776f;

    private float safetyDistance = 10f;
    private float enemySpacing = 3f;
    private float minimumRadius = 12f;
    private float maximumRadius = 24f;
    private int candidateIndex;

    public int AttemptCount { get; private set; }
    public string LastFailureReason { get; private set; } = string.Empty;

    public void Configure(WaveDefinition definition)
    {
        safetyDistance = definition.PlayerSafetyDistance;
        enemySpacing = definition.EnemySpacing;
        minimumRadius = definition.MinimumSpawnRadius;
        maximumRadius = definition.MaximumSpawnRadius;
        candidateIndex = 0;
        AttemptCount = 0;
        LastFailureReason = string.Empty;
    }

    public bool TryResolve(
        Transform player,
        IReadOnlyList<Vector3> occupiedPositions,
        out Vector3 spawnPoint)
    {
        AttemptCount++;

        if (player == null || !RuntimeNavMeshBootstrap.IsSceneReady)
        {
            LastFailureReason = "Player or runtime NavMesh is not ready.";
            spawnPoint = default;
            return false;
        }

        if (!NavMesh.SamplePosition(
                player.position,
                out NavMeshHit playerHit,
                4f,
                NavMesh.AllAreas))
        {
            LastFailureReason = "Player is not near a NavMesh surface.";
            spawnPoint = default;
            return false;
        }

        for (int localAttempt = 0; localAttempt < 12; localAttempt++)
        {
            int index = candidateIndex++;
            float ringBlend = (index % 7) / 6f;
            float radius = Mathf.Lerp(
                minimumRadius,
                maximumRadius,
                ringBlend);
            float radians =
                index * GoldenAngle * Mathf.Deg2Rad;
            Vector3 desired = player.position + new Vector3(
                Mathf.Cos(radians),
                0f,
                Mathf.Sin(radians)) * radius;

            if (!NavMesh.SamplePosition(
                    desired,
                    out NavMeshHit hit,
                    2.25f,
                    NavMesh.AllAreas))
            {
                LastFailureReason = "Candidate is outside NavMesh.";
                continue;
            }

            if (HorizontalSqrDistance(hit.position, player.position) <
                safetyDistance * safetyDistance)
            {
                LastFailureReason = "Candidate is inside player safety radius.";
                continue;
            }

            if (Mathf.Abs(hit.position.y - desired.y) > 2f ||
                HorizontalSqrDistance(hit.position, desired) > 2.25f * 2.25f)
            {
                LastFailureReason = "NavMesh projection moved to another level.";
                continue;
            }

            if (IsTooCloseToEnemy(hit.position, occupiedPositions))
            {
                LastFailureReason = "Candidate is too close to an active enemy.";
                continue;
            }

            var path = new NavMeshPath();

            if (!NavMesh.CalculatePath(
                    playerHit.position,
                    hit.position,
                    NavMesh.AllAreas,
                    path) ||
                path.status != NavMeshPathStatus.PathComplete)
            {
                LastFailureReason = "Candidate is not reachable from the player.";
                continue;
            }

            LastFailureReason = string.Empty;
            spawnPoint = hit.position;
            return true;
        }

        spawnPoint = default;
        return false;
    }

    private bool IsTooCloseToEnemy(
        Vector3 candidate,
        IReadOnlyList<Vector3> occupiedPositions)
    {
        if (occupiedPositions == null)
        {
            return false;
        }

        float minimumSqrDistance = enemySpacing * enemySpacing;

        for (int index = 0; index < occupiedPositions.Count; index++)
        {
            if (HorizontalSqrDistance(
                    candidate,
                    occupiedPositions[index]) < minimumSqrDistance)
            {
                return true;
            }
        }

        return false;
    }

    private static float HorizontalSqrDistance(Vector3 a, Vector3 b)
    {
        float x = a.x - b.x;
        float z = a.z - b.z;
        return x * x + z * z;
    }
}
