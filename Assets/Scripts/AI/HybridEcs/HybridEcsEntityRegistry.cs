using System;
using System.Collections.Generic;
using Unity.Entities;

namespace FPS.AI.HybridEcs
{
    public readonly struct HybridEcsAgentHandle
    {
        public HybridEcsAgentHandle(string stableId, int generation, Entity entity)
        {
            StableId = stableId ?? string.Empty;
            Generation = generation;
            Entity = entity;
        }

        public string StableId { get; }
        public int Generation { get; }
        public Entity Entity { get; }
    }

    /// <summary>
    /// ECS 实体生命周期边界。对象池复用时必须先释放旧 generation，再注册新
    /// generation；场景重开、读取存档或退出战局统一调用 DestroyAll。
    /// </summary>
    public sealed class HybridEcsEntityRegistry : IDisposable
    {
        private readonly EntityManager entityManager;
        private readonly Dictionary<string, HybridEcsAgentHandle> active =
            new(StringComparer.Ordinal);
        private EntityQuery allAgentsQuery;
        private bool disposed;

        public HybridEcsEntityRegistry(EntityManager manager)
        {
            entityManager = manager;
            allAgentsQuery = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<HybridEcsAgent>());
        }

        public int RegisteredCount => active.Count;

        public HybridEcsAgentHandle Register(
            HybridEcsAgent agent,
            HybridEcsPerceptionInput perception,
            HybridEcsDecisionConfig config,
            EnemyUtilityProfileDefinition profile = null)
        {
            ThrowIfDisposed();
            string stableId = agent.StableId.ToString();

            if (string.IsNullOrWhiteSpace(stableId))
            {
                throw new ArgumentException(
                    "Hybrid ECS agent requires a non-empty stable id.",
                    nameof(agent));
            }

            if (active.TryGetValue(stableId, out HybridEcsAgentHandle old))
            {
                if (entityManager.Exists(old.Entity))
                {
                    entityManager.DestroyEntity(old.Entity);
                }

                active.Remove(stableId);
            }

            Entity entity = entityManager.CreateEntity(
                typeof(HybridEcsAgent),
                typeof(HybridEcsPerceptionInput),
                typeof(HybridEcsNeighborhoodFacts),
                typeof(HybridEcsPerceptionFacts),
                typeof(HybridEcsActionIntent),
                typeof(HybridEcsDecisionConfig),
                typeof(HybridEcsBatchOwned),
                typeof(HybridEcsHandoffPending));
            entityManager.SetComponentData(entity, agent);
            entityManager.SetComponentData(entity, perception);
            entityManager.SetComponentData(entity, config);
            entityManager.AddComponentObject(
                entity,
                new HybridEcsDecisionRuntime(profile));
            entityManager.SetComponentEnabled<HybridEcsBatchOwned>(entity, true);
            entityManager.SetComponentEnabled<HybridEcsHandoffPending>(
                entity,
                false);

            var handle = new HybridEcsAgentHandle(
                stableId,
                agent.Generation,
                entity);
            active.Add(stableId, handle);
            return handle;
        }

        public bool Synchronize(
            HybridEcsAgentHandle handle,
            HybridEcsAgent agent,
            HybridEcsPerceptionInput perception)
        {
            ThrowIfDisposed();

            if (!IsCurrent(handle) ||
                agent.Generation != handle.Generation ||
                !string.Equals(
                    agent.StableId.ToString(),
                    handle.StableId,
                    StringComparison.Ordinal))
            {
                return false;
            }

            entityManager.SetComponentData(handle.Entity, agent);
            entityManager.SetComponentData(handle.Entity, perception);
            return true;
        }

        public bool SetBatchActive(
            HybridEcsAgentHandle handle,
            bool activeForBatch)
        {
            ThrowIfDisposed();

            if (!IsCurrent(handle))
            {
                return false;
            }

            entityManager.SetComponentEnabled<HybridEcsBatchOwned>(
                handle.Entity,
                activeForBatch);
            return true;
        }

        public bool TryReadIntent(
            HybridEcsAgentHandle handle,
            out HybridEcsActionIntent intent)
        {
            ThrowIfDisposed();

            if (!IsCurrent(handle))
            {
                intent = default;
                return false;
            }

            intent = entityManager.GetComponentData<HybridEcsActionIntent>(
                handle.Entity);
            return true;
        }

        public bool AcknowledgeHandoff(HybridEcsAgentHandle handle)
        {
            ThrowIfDisposed();

            if (!IsCurrent(handle))
            {
                return false;
            }

            entityManager.SetComponentEnabled<HybridEcsHandoffPending>(
                handle.Entity,
                false);
            return true;
        }

        public bool TryGet(string stableId, out HybridEcsAgentHandle handle)
        {
            ThrowIfDisposed();
            return active.TryGetValue(stableId ?? string.Empty, out handle) &&
                   entityManager.Exists(handle.Entity);
        }

        public bool Despawn(string stableId, int generation)
        {
            ThrowIfDisposed();

            if (!active.TryGetValue(stableId ?? string.Empty, out var handle) ||
                handle.Generation != generation)
            {
                return false;
            }

            if (entityManager.Exists(handle.Entity))
            {
                entityManager.DestroyEntity(handle.Entity);
            }

            active.Remove(handle.StableId);
            return true;
        }

        public int DestroyAll()
        {
            ThrowIfDisposed();
            int count = allAgentsQuery.CalculateEntityCount();

            if (count > 0)
            {
                entityManager.DestroyEntity(allAgentsQuery);
            }

            active.Clear();
            return count;
        }

        public int CountGhostEntities()
        {
            ThrowIfDisposed();
            int ghosts = 0;

            foreach (HybridEcsAgentHandle handle in active.Values)
            {
                if (!entityManager.Exists(handle.Entity))
                {
                    ghosts++;
                }
            }

            using var entities = allAgentsQuery.ToEntityArray(
                Unity.Collections.Allocator.Temp);

            for (int index = 0; index < entities.Length; index++)
            {
                HybridEcsAgent agent = entityManager.GetComponentData<
                    HybridEcsAgent>(entities[index]);
                string stableId = agent.StableId.ToString();

                if (!active.TryGetValue(stableId, out var handle) ||
                    handle.Generation != agent.Generation ||
                    handle.Entity != entities[index])
                {
                    ghosts++;
                }
            }

            return ghosts;
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            if (entityManager.World.IsCreated)
            {
                DestroyAll();
                allAgentsQuery.Dispose();
            }

            disposed = true;
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(HybridEcsEntityRegistry));
            }
        }

        private bool IsCurrent(HybridEcsAgentHandle handle)
        {
            return active.TryGetValue(handle.StableId, out var current) &&
                   current.Generation == handle.Generation &&
                   current.Entity == handle.Entity &&
                   entityManager.Exists(handle.Entity);
        }
    }
}
