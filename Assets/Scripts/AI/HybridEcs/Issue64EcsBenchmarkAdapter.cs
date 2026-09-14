using System;
using System.Collections.Generic;
using System.Diagnostics;
using FPS.AI.Hybrid.Shared;
using FPS.Performance.HybridAi;
using Unity.Collections;
using Unity.Core;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Profiling;

namespace FPS.AI.HybridEcs
{
    /// <summary>
    /// Issue 64 的正式 ECS A/B 适配器。场景事实与摘要均调用共享 kernel，
    /// 本类只投影为 Entities 组件并驱动两个 ECS 系统。
    /// </summary>
    public sealed class Issue64EcsBenchmarkAdapter :
        IIssue64BenchmarkAdapter
    {
        private readonly List<HybridAiActionIntent> intents = new(512);
        private World world;
        private HybridEcsEntityRegistry registry;
        private HybridEcsPerceptionNeighborhoodSystem perceptionSystem;
        private HybridEcsUtilityIntentSystem utilitySystem;
        private HybridAiBenchmarkAgentState[] agents =
            Array.Empty<HybridAiBenchmarkAgentState>();
        private HybridEcsAgentHandle[] handles =
            Array.Empty<HybridEcsAgentHandle>();
        private EnemyUtilityProfileDefinition profile;
        private int seed;
        private int tick;
        private double elapsedTime;
        private double lastMainThreadMilliseconds;
        private long lastGcBytes;
        private long lastMemoryBytes;
        private string lastIntentDigest = string.Empty;
        private bool disposed;

        public HybridAiBenchmarkMode Mode => HybridAiBenchmarkMode.Ecs;
        public bool IsAvailable => !disposed;

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RegisterRuntimeFactory()
        {
            RegisterFactory();
        }

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        private static void RegisterEditorFactory()
        {
            RegisterFactory();
        }
#endif

        public static void RegisterFactory()
        {
            Issue64BenchmarkAdapterRegistry.Register(
                HybridAiBenchmarkMode.Ecs,
                static () => new Issue64EcsBenchmarkAdapter());
        }

        public void Configure(int enemyCount, int configuredSeed)
        {
            ThrowIfDisposed();
            ReleaseWorld();
            seed = configuredSeed;
            tick = 0;
            elapsedTime = 0d;
            lastMainThreadMilliseconds = 0d;
            lastGcBytes = 0L;
            lastMemoryBytes = 0L;
            lastIntentDigest = string.Empty;
            intents.Clear();
            profile = Resources.Load<EnemyUtilityProfileDefinition>(
                "Content/CityNew/Enemies/Utility/RaiderUtility");

            if (profile == null)
            {
                throw new InvalidOperationException(
                    "Issue 64 requires the RaiderUtility profile.");
            }

            world = new World("Issue 64 ECS Benchmark");
            registry = new HybridEcsEntityRegistry(world.EntityManager);
            perceptionSystem = world.CreateSystemManaged<
                HybridEcsPerceptionNeighborhoodSystem>();
            utilitySystem = world.CreateSystemManaged<
                HybridEcsUtilityIntentSystem>();
            agents = HybridAiBenchmarkScenarioKernel.CreateAgents(
                enemyCount,
                seed);
            handles = new HybridEcsAgentHandle[agents.Length];

            for (int index = 0; index < agents.Length; index++)
            {
                HybridAiBenchmarkAgentState agent = agents[index];
                HybridAiBenchmarkPerception perception =
                    HybridAiBenchmarkScenarioKernel.SamplePerception(
                        index,
                        seed,
                        tick);
                handles[index] = registry.Register(
                    ToEcsAgent(agent),
                    ToEcsPerception(perception),
                    CreateConfig(index),
                    profile);
            }
        }

        public void Step(float deltaTime)
        {
            ThrowIfDisposed();

            if (world == null || !world.IsCreated)
            {
                throw new InvalidOperationException(
                    "Configure must be called before Step.");
            }

            float safeDelta = math.max(0f, deltaTime);
            long gcBefore = GC.GetAllocatedBytesForCurrentThread();
            long start = Stopwatch.GetTimestamp();

            for (int index = 0; index < handles.Length; index++)
            {
                HybridAiBenchmarkPerception sampled =
                    HybridAiBenchmarkScenarioKernel.SamplePerception(
                        index,
                        seed,
                        tick);
                registry.Synchronize(
                    handles[index],
                    ToEcsAgent(agents[index]),
                    ToEcsPerception(sampled));
            }

            elapsedTime += safeDelta;
            world.SetTime(new TimeData(elapsedTime, safeDelta));
            perceptionSystem.Update();
            utilitySystem.Update();
            CaptureIntentDigest();
            long end = Stopwatch.GetTimestamp();
            lastMainThreadMilliseconds =
                (end - start) * 1000d / Stopwatch.Frequency;
            lastGcBytes = Math.Max(
                0L,
                GC.GetAllocatedBytesForCurrentThread() - gcBefore);
            lastMemoryBytes = Math.Max(
                0L,
                Profiler.GetTotalAllocatedMemoryLong());
            tick++;
        }

        public Issue64AdapterMetricsSnapshot CaptureMetrics()
        {
            ThrowIfDisposed();
            return new Issue64AdapterMetricsSnapshot(
                registry?.RegisteredCount ?? 0,
                registry?.CountGhostEntities() ?? 0,
                Average(utilitySystem?.LastDecisionLatencies ??
                    Array.Empty<double>()),
                Percentile(utilitySystem?.LastDecisionLatencies ??
                    Array.Empty<double>(), 0.95d),
                Percentile(utilitySystem?.LastDecisionLatencies ??
                    Array.Empty<double>(), 0.99d),
                lastMainThreadMilliseconds,
                lastGcBytes,
                lastMemoryBytes,
                lastIntentDigest);
        }

        public void Reset()
        {
            ThrowIfDisposed();
            ReleaseWorld();
            agents = Array.Empty<HybridAiBenchmarkAgentState>();
            handles = Array.Empty<HybridEcsAgentHandle>();
            profile = null;
            intents.Clear();
            tick = 0;
            elapsedTime = 0d;
            lastMainThreadMilliseconds = 0d;
            lastGcBytes = 0L;
            lastMemoryBytes = 0L;
            lastIntentDigest = string.Empty;
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            ReleaseWorld();
            disposed = true;
        }

        private void CaptureIntentDigest()
        {
            intents.Clear();

            for (int index = 0; index < handles.Length; index++)
            {
                if (!registry.TryReadIntent(handles[index], out var intent))
                {
                    continue;
                }

                HybridEcsAgent agent = world.EntityManager.GetComponentData<
                    HybridEcsAgent>(handles[index].Entity);
                intents.Add(new HybridAiActionIntent(
                    agent.StableId.ToString(),
                    agent.Generation,
                    intent.ActionId.ToString(),
                    intent.Kind,
                    ToVector3(intent.AnchorPosition),
                    intent.MovementSpeedMultiplier,
                    intent.DecisionReason.ToString(),
                    intent.Changed != 0,
                    intent.RangeBand,
                    intent.ExecutionOwner,
                    intent.HandoffSignal));
            }

            lastIntentDigest = HybridAiIntentDigestKernel.Compute(intents);
        }

        private HybridEcsDecisionConfig CreateConfig(int agentIndex)
        {
            return new HybridEcsDecisionConfig
            {
                FriendlyScanRadius = HybridAiBenchmarkScenarioKernel
                    .FriendlyRadius,
                NearMaximumDistance = 12f,
                MidMaximumDistance = 28f,
                HandoffHysteresis = 2f,
                DecisionSeed = HybridAiBenchmarkScenarioKernel.Hash(
                    seed,
                    agentIndex)
            };
        }

        private static HybridEcsAgent ToEcsAgent(
            in HybridAiBenchmarkAgentState agent)
        {
            return new HybridEcsAgent
            {
                StableId = new FixedString64Bytes(agent.StableId),
                Generation = agent.Generation,
                Position = ToFloat3(agent.Position),
                HealthRatio = agent.HealthRatio,
                RoleFlags = (byte)agent.Role,
                IsAlive = agent.IsAlive ? (byte)1 : (byte)0
            };
        }

        private static HybridEcsPerceptionInput ToEcsPerception(
            in HybridAiBenchmarkPerception perception)
        {
            return new HybridEcsPerceptionInput
            {
                TargetPosition = ToFloat3(perception.TargetPosition),
                HasLineOfSight = perception.HasLineOfSight ? (byte)1 : (byte)0,
                TargetInCover = perception.TargetInCover ? (byte)1 : (byte)0
            };
        }

        private void ReleaseWorld()
        {
            registry?.Dispose();
            registry = null;
            perceptionSystem = null;
            utilitySystem = null;

            if (world != null && world.IsCreated)
            {
                world.Dispose();
            }

            world = null;
        }

        private static double Average(IReadOnlyList<double> values)
        {
            if (values.Count == 0)
            {
                return 0d;
            }

            double total = 0d;
            for (int index = 0; index < values.Count; index++)
            {
                total += values[index];
            }

            return total / values.Count;
        }

        private static double Percentile(
            IReadOnlyList<double> values,
            double percentile)
        {
            if (values.Count == 0)
            {
                return 0d;
            }

            var sorted = new double[values.Count];
            for (int index = 0; index < values.Count; index++)
            {
                sorted[index] = values[index];
            }

            Array.Sort(sorted);
            int rank = Math.Max(
                0,
                (int)Math.Ceiling(percentile * sorted.Length) - 1);
            return sorted[Math.Min(rank, sorted.Length - 1)];
        }

        private static float3 ToFloat3(Vector3 value) =>
            new(value.x, value.y, value.z);

        private static Vector3 ToVector3(float3 value) =>
            new(value.x, value.y, value.z);

        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(
                    nameof(Issue64EcsBenchmarkAdapter));
            }
        }
    }
}
