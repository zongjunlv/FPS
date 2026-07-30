using System.Collections;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

public sealed class RuntimeNavMeshBootstrap : MonoBehaviour
{
    private static RuntimeNavMeshBootstrap instance;
    private NavMeshSurface surface;

    public bool IsReady { get; private set; }
    public static bool IsSceneReady =>
        instance != null && instance.IsReady;

    public static void EnsureForActiveScene()
    {
        if (instance != null ||
            SceneManager.GetActiveScene().name != "CityNew")
        {
            return;
        }

        GameObject root = new GameObject("Runtime NavMesh");
        instance = root.AddComponent<RuntimeNavMeshBootstrap>();
    }

    private IEnumerator Start()
    {
        yield return null;
        surface = gameObject.AddComponent<NavMeshSurface>();
        surface.collectObjects = CollectObjects.All;
        surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
        surface.layerMask = Physics.DefaultRaycastLayers;
        surface.BuildNavMesh();
        EnemyNavigationController[] navigators =
            FindObjectsByType<EnemyNavigationController>(
                FindObjectsInactive.Exclude);

        foreach (EnemyNavigationController navigator in navigators)
        {
            if (navigator.enabled)
            {
                navigator.AttachToNavMesh();
            }
        }

        PlaceAgentsOnNavMesh();
        IsReady = true;
    }

    private static void PlaceAgentsOnNavMesh()
    {
        NavMeshAgent[] agents =
            FindObjectsByType<NavMeshAgent>(
                FindObjectsInactive.Exclude);

        foreach (NavMeshAgent agent in agents)
        {
            EnemyNavigationController navigator =
                agent.GetComponent<EnemyNavigationController>();

            if (navigator != null && !navigator.enabled)
            {
                continue;
            }

            if (agent.isOnNavMesh)
            {
                continue;
            }

            if (NavMesh.SamplePosition(
                    agent.transform.position,
                    out NavMeshHit hit,
                    3f,
                    NavMesh.AllAreas))
            {
                agent.Warp(hit.position);
            }
        }
    }
}
