using System.Collections;
using FPS.Core.GameModes;
using FPS.Networking.Netcode;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;

public sealed class RuntimeNavMeshBootstrap : MonoBehaviour
{
    private static RuntimeNavMeshBootstrap instance;
    private NavMeshSurface surface;

    public bool IsReady { get; private set; }
    public static bool IsSceneReady =>
        instance != null && instance.IsReady;

    public static void EnsureForActiveScene()
    {
        bool supportedBattle = GameModeContext.IsActive(
                                   GameModeId.SoloBattle,
                                   GameModeStage.Battle) ||
                               GameModeContext.IsActive(
                                   GameModeId.Coop,
                                   GameModeStage.CoopBattle) ||
                               DedicatedServerRuntime.IsActive;
        if (instance != null || !supportedBattle)
        {
            return;
        }

        GameObject root = new GameObject("Runtime NavMesh");
        instance = root.AddComponent<RuntimeNavMeshBootstrap>();
        CityNewModularLayoutBootstrap.EnsureForActiveScene();
    }

    private IEnumerator Start()
    {
        CityNewModularLayoutBootstrap layout =
            CityNewModularLayoutBootstrap.EnsureForActiveScene();
        for (int frame = 0;
             frame < 600 && layout != null && !layout.GeometryReady;
             frame++)
            yield return null;
        if (layout != null && !layout.GeometryReady)
            layout.ActivateLegacyFallback("布局生成超时。");

        surface = gameObject.AddComponent<NavMeshSurface>();
        surface.collectObjects = CollectObjects.All;
        surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
        surface.layerMask = Physics.DefaultRaycastLayers;
        try
        {
            surface.BuildNavMesh();
        }
        catch (System.Exception exception)
        {
            if (layout == null || layout.IsUsingFallback) throw;
            layout.ActivateLegacyFallback("导航构建异常：" + exception.Message);
            surface.RemoveData();
            surface.BuildNavMesh();
        }
        if (layout != null && !layout.TryFinalizeNavigation(out string error))
        {
            layout.ActivateLegacyFallback(error);
            surface.RemoveData();
            surface.BuildNavMesh();
            layout.MarkFallbackNavigationReady();
        }
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

    private void OnDestroy()
    {
        IsReady = false;
        if (instance == this)
        {
            instance = null;
        }
    }
}
