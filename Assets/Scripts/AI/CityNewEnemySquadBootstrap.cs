using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

[RequireComponent(typeof(EnemySquadCoordinator))]
public sealed class CityNewEnemySquadBootstrap : MonoBehaviour
{
    [SerializeField, Min(2)] private int desiredSquadSize = 3;
    [SerializeField, Min(2f)] private float spawnRadius = 7f;
    [SerializeField, Min(0.5f)] private float minimumSpacing = 3f;

    private IEnumerator Start()
    {
        if (SceneManager.GetActiveScene().name != "CityNew")
        {
            yield break;
        }

        if (CityNewWaveBootstrap.IsWaveModeActive)
        {
            yield break;
        }

        for (int frame = 0;
             frame < 240 && !RuntimeNavMeshBootstrap.IsSceneReady;
             frame++)
        {
            yield return null;
        }

        if (!RuntimeNavMeshBootstrap.IsSceneReady)
        {
            yield break;
        }

        if (CityNewWaveBootstrap.IsWaveModeActive)
        {
            yield break;
        }

        yield return null;
        EnemyController[] existing =
            FindObjectsByType<EnemyController>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);

        if (existing.Length == 0 ||
            existing.Length >= desiredSquadSize)
        {
            yield break;
        }

        EnemyController template = FindOriginal(existing);
        var occupied = new List<Vector3>();

        foreach (EnemyController enemy in existing)
        {
            occupied.Add(enemy.transform.position);
        }

        int required = desiredSquadSize - existing.Length;
        int spawned = 0;
        int candidateCount = 32;

        for (int index = 0;
             index < candidateCount && spawned < required;
             index++)
        {
            float angle = index * Mathf.PI * 2f / candidateCount;
            float radius = spawnRadius *
                Mathf.Lerp(0.65f, 1.15f, (index % 4) / 3f);
            Vector3 desired = template.transform.position +
                new Vector3(
                    Mathf.Cos(angle),
                    0f,
                    Mathf.Sin(angle)) * radius;

            if (!TryResolveSpawnPoint(
                    template.transform.position,
                    desired,
                    occupied,
                    out Vector3 spawnPoint))
            {
                continue;
            }

            EnemyController clone = Instantiate(
                template,
                spawnPoint,
                template.transform.rotation);
            clone.name = $"SPIDER_BOT SUPPORT {spawned + 1}";
            EnemyNavigationController navigation =
                clone.GetComponent<EnemyNavigationController>();
            navigation?.AttachToNavMesh();
            occupied.Add(spawnPoint);
            spawned++;
        }
    }

    private bool TryResolveSpawnPoint(
        Vector3 origin,
        Vector3 desired,
        List<Vector3> occupied,
        out Vector3 resolved)
    {
        if (!NavMesh.SamplePosition(
                desired,
                out NavMeshHit hit,
                2.5f,
                NavMesh.AllAreas))
        {
            resolved = default;
            return false;
        }

        foreach (Vector3 position in occupied)
        {
            if (Vector3.Distance(position, hit.position) <
                minimumSpacing)
            {
                resolved = default;
                return false;
            }
        }

        NavMeshPath path = new NavMeshPath();

        if (!NavMesh.CalculatePath(
                origin,
                hit.position,
                NavMesh.AllAreas,
                path) ||
            path.status != NavMeshPathStatus.PathComplete)
        {
            resolved = default;
            return false;
        }

        resolved = hit.position;
        return true;
    }

    private static EnemyController FindOriginal(
        EnemyController[] existing)
    {
        foreach (EnemyController enemy in existing)
        {
            if (enemy.name == "SPIDER_BOT")
            {
                return enemy;
            }
        }

        return existing[0];
    }
}
