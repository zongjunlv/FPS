using System;
using UnityEngine;
using UnityEngine.AI;

public sealed class SceneEnemyFactory : MonoBehaviour, IEnemyFactory
{
    private EnemyController sceneTemplate;
    private bool sceneTemplateAdopted;

    public int SuccessfulSpawnCount { get; private set; }

    public void Configure(EnemyController template)
    {
        sceneTemplate = template;
        sceneTemplateAdopted = false;
        SuccessfulSpawnCount = 0;
    }

    public bool TrySpawn(
        EnemySpawnRequest request,
        Action<EnemySpawnHandle, EnemyExitReason> onEnded,
        out EnemySpawnHandle handle)
    {
        EnemyController source = request.Entry?.Template;

        if (source == null)
        {
            source = sceneTemplate;
        }

        if (source == null)
        {
            handle = default;
            return false;
        }

        bool adoptSceneTemplate =
            !sceneTemplateAdopted && source == sceneTemplate;
        EnemyController instance = adoptSceneTemplate
            ? source
            : Instantiate(
                source,
                request.Position,
                request.Rotation);

        if (instance == null)
        {
            handle = default;
            return false;
        }

        instance.name = $"SPIDER_BOT WAVE {request.SpawnId:000}";
        instance.transform.position = request.Position;
        instance.transform.rotation = request.Rotation;
        instance.SetFactoryManaged(true);

        if (!instance.gameObject.activeSelf)
        {
            instance.gameObject.SetActive(true);
        }

        instance.ResetForSpawn(request.Target);
        instance.ApplyAffix(request.Entry?.Affix);
        instance.ApplyAbilitySet(
            request.Entry?.AbilitySet,
            request.Target);

        NavMeshAgent agent = instance.GetComponent<NavMeshAgent>();

        if (agent != null && agent.enabled && agent.isOnNavMesh)
        {
            agent.Warp(request.Position);
        }
        else
        {
            instance.transform.position = request.Position;
        }
        EnemyNavigationController navigation =
            instance.GetComponent<EnemyNavigationController>();

        if (navigation == null || !navigation.UsesNavMesh)
        {
            instance.PrepareForPool();

            if (!adoptSceneTemplate)
            {
                Destroy(instance.gameObject);
            }
            else
            {
                instance.gameObject.SetActive(false);
            }

            handle = default;
            return false;
        }

        EnemyPerceptionController perception =
            instance.GetComponent<EnemyPerceptionController>();
        perception?.SetTarget(request.Target);
        WaveEnemyLifecycle lifecycle =
            instance.GetComponent<WaveEnemyLifecycle>();

        if (lifecycle == null)
        {
            lifecycle =
                instance.gameObject.AddComponent<WaveEnemyLifecycle>();
        }

        lifecycle.Arm(
            request.SpawnId,
            request.WaveNumber,
            instance,
            request.Entry?.EnemyTypeId,
            request.Entry?.RewardTier ?? LootRewardTier.Normal,
            onEnded);
        handle = new EnemySpawnHandle(
            request.SpawnId,
            request.WaveNumber,
            instance,
            lifecycle,
            request.Entry?.EnemyTypeId,
            request.Entry?.RewardTier ?? LootRewardTier.Normal);
        sceneTemplateAdopted |= adoptSceneTemplate;
        SuccessfulSpawnCount++;
        return true;
    }

    public void Release(EnemySpawnHandle handle)
    {
        if (!handle.IsValid)
        {
            return;
        }

        handle.Lifecycle.Disarm();
        handle.Controller.PrepareForPool();

        if (handle.Controller == sceneTemplate)
        {
            handle.Controller.gameObject.SetActive(false);
            return;
        }

        Destroy(handle.Controller.gameObject);
    }
}
