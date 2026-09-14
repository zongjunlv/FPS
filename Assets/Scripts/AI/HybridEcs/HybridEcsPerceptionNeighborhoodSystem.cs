using FPS.AI.Hybrid.Shared;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace FPS.AI.HybridEcs
{
    /// <summary>
    /// 以确定性 StableId 顺序批处理远中距离敌人的距离感知与友军邻域。
    /// 输入只来自 adapter 已采集的事实，不访问 GameObject、NavMesh 或物理世界。
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class HybridEcsPerceptionNeighborhoodSystem : SystemBase
    {
        private EntityQuery sourceQuery;
        private EntityQuery populationQuery;

        public int LastProcessedCount { get; private set; }
        public long TotalProcessedCount { get; private set; }
        public long TotalCandidateVisitCount { get; private set; }

        protected override void OnCreate()
        {
            sourceQuery = GetEntityQuery(
                ComponentType.ReadOnly<HybridEcsAgent>(),
                ComponentType.ReadOnly<HybridEcsPerceptionInput>(),
                ComponentType.ReadOnly<HybridEcsDecisionConfig>(),
                ComponentType.ReadWrite<HybridEcsNeighborhoodFacts>(),
                ComponentType.ReadWrite<HybridEcsPerceptionFacts>(),
                ComponentType.ReadOnly<HybridEcsBatchOwned>());
            populationQuery = GetEntityQuery(
                ComponentType.ReadOnly<HybridEcsAgent>());
        }

        protected override void OnUpdate()
        {
            LastProcessedCount = sourceQuery.CalculateEntityCount();

            if (LastProcessedCount == 0)
            {
                return;
            }

            using NativeArray<Entity> sourceEntities =
                sourceQuery.ToEntityArray(Allocator.TempJob);
            using NativeArray<HybridEcsAgent> sourceAgents =
                sourceQuery.ToComponentDataArray<HybridEcsAgent>(
                    Allocator.TempJob);
            using NativeArray<HybridEcsPerceptionInput> sourcePerception =
                sourceQuery.ToComponentDataArray<HybridEcsPerceptionInput>(
                    Allocator.TempJob);
            using NativeArray<HybridEcsDecisionConfig> sourceConfigs =
                sourceQuery.ToComponentDataArray<HybridEcsDecisionConfig>(
                    Allocator.TempJob);
            using NativeArray<HybridEcsAgent> population =
                populationQuery.ToComponentDataArray<HybridEcsAgent>(
                    Allocator.TempJob);
            var sources = new NativeArray<WorkItem>(
                LastProcessedCount,
                Allocator.TempJob,
                NativeArrayOptions.UninitializedMemory);
            var outputs = new NativeArray<WorkResult>(
                LastProcessedCount,
                Allocator.TempJob,
                NativeArrayOptions.UninitializedMemory);
            try
            {
                for (int index = 0; index < LastProcessedCount; index++)
                {
                    sources[index] = new WorkItem
                    {
                        Entity = sourceEntities[index],
                        Agent = sourceAgents[index],
                        Perception = sourcePerception[index],
                        Config = sourceConfigs[index]
                    };
                }

                new StableSortJob { Values = sources }.Run();
                new NeighborhoodJob
                {
                    Sources = sources,
                    Population = population,
                    Results = outputs
                }.Schedule(LastProcessedCount, 32).Complete();

                for (int index = 0; index < LastProcessedCount; index++)
                {
                    WorkResult output = outputs[index];
                    EntityManager.SetComponentData(
                        sources[index].Entity,
                        output.Neighborhood);
                    EntityManager.SetComponentData(
                        sources[index].Entity,
                        output.Perception);
                }
            }
            finally
            {
                outputs.Dispose();
                sources.Dispose();
            }

            TotalProcessedCount += LastProcessedCount;
            TotalCandidateVisitCount +=
                (long)LastProcessedCount * population.Length;
        }

        private struct WorkItem
        {
            public Entity Entity;
            public HybridEcsAgent Agent;
            public HybridEcsPerceptionInput Perception;
            public HybridEcsDecisionConfig Config;
        }

        private struct WorkResult
        {
            public HybridEcsNeighborhoodFacts Neighborhood;
            public HybridEcsPerceptionFacts Perception;
        }

        [BurstCompile]
        private struct StableSortJob : IJob
        {
            public NativeArray<WorkItem> Values;

            public void Execute()
            {
                for (int index = 1; index < Values.Length; index++)
                {
                    WorkItem value = Values[index];
                    int insertionIndex = index - 1;

                    while (insertionIndex >= 0 &&
                           Compare(value, Values[insertionIndex]) < 0)
                    {
                        Values[insertionIndex + 1] = Values[insertionIndex];
                        insertionIndex--;
                    }

                    Values[insertionIndex + 1] = value;
                }
            }

            private static int Compare(WorkItem left, WorkItem right)
            {
                int stableId = left.Agent.StableId.CompareTo(
                    right.Agent.StableId);
                return stableId != 0
                    ? stableId
                    : left.Agent.Generation.CompareTo(right.Agent.Generation);
            }
        }

        [BurstCompile]
        private struct NeighborhoodJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<WorkItem> Sources;
            [ReadOnly] public NativeArray<HybridEcsAgent> Population;
            [WriteOnly] public NativeArray<WorkResult> Results;

            public void Execute(int index)
            {
                WorkItem source = Sources[index];
                float radius = math.max(0f, source.Config.FriendlyScanRadius);
                float radiusSquared = radius * radius;
                int raiders = 0;
                int suppressors = 0;
                int supporters = 0;
                bool supportCoverage = false;

                for (int candidateIndex = 0;
                     candidateIndex < Population.Length;
                     candidateIndex++)
                {
                    HybridEcsAgent candidate = Population[candidateIndex];

                    if (candidate.IsAlive == 0 ||
                        candidate.Generation == source.Agent.Generation &&
                        candidate.StableId.Equals(source.Agent.StableId))
                    {
                        continue;
                    }

                    float2 offset = candidate.Position.xz -
                        source.Agent.Position.xz;

                    if (math.lengthsq(offset) > radiusSquared)
                    {
                        continue;
                    }

                    HybridEcsRoleFlags roles =
                        (HybridEcsRoleFlags)candidate.RoleFlags;

                    if ((roles & HybridEcsRoleFlags.Raider) != 0)
                    {
                        raiders++;
                    }

                    if ((roles & HybridEcsRoleFlags.Suppressor) != 0)
                    {
                        suppressors++;
                    }

                    if ((roles & HybridEcsRoleFlags.Support) != 0)
                    {
                        supporters++;
                        supportCoverage = true;
                    }
                }

                float distance = math.distance(
                    source.Agent.Position,
                    source.Perception.TargetPosition);
                HybridAiRangeBand band = distance <=
                    math.max(0f, source.Config.NearMaximumDistance)
                    ? HybridAiRangeBand.Near
                    : distance <= math.max(
                        source.Config.NearMaximumDistance,
                        source.Config.MidMaximumDistance)
                        ? HybridAiRangeBand.Mid
                        : HybridAiRangeBand.Far;
                Results[index] = new WorkResult
                {
                    Neighborhood = new HybridEcsNeighborhoodFacts
                    {
                        FriendlyRaiderCount = raiders,
                        FriendlySuppressorCount = suppressors,
                        FriendlySupportCount = supporters,
                        HasSupportCoverage = supportCoverage ? (byte)1 : (byte)0
                    },
                    Perception = new HybridEcsPerceptionFacts
                    {
                        TargetDistance = distance,
                        RangeBand = band
                    }
                };
            }
        }
    }
}
