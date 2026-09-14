using System;
using System.Collections.Generic;
using System.Diagnostics;
using FPS.AI.Hybrid.Shared;
using FPS.Performance.HybridAi;
using UnityEngine;
using UnityEngine.Profiling;
using Object = UnityEngine.Object;

public sealed class Issue64GameObjectBenchmarkAdapter :
    IIssue64BenchmarkAdapter
{
    private const string ProfileResourcePath =
        "Content/CityNew/Enemies/Utility/RaiderUtility";

    private readonly List<HybridGameObjectUtilityAdapter> adapters = new();
    private HybridAiBenchmarkAgentState[] agents =
        Array.Empty<HybridAiBenchmarkAgentState>();
    private HybridAiActionIntent[] intents =
        Array.Empty<HybridAiActionIntent>();
    private double[] decisionLatencies = Array.Empty<double>();
    private EnemyUtilityProfileDefinition profile;
    private GameObject root;
    private int seed;
    private int tick;
    private double lastMainThreadMilliseconds;
    private long lastAllocatedBytes;
    private long lastMemoryBytes;
    private string lastIntentDigest = string.Empty;

    public HybridAiBenchmarkMode Mode => HybridAiBenchmarkMode.GameObject;
    public bool IsAvailable => ResolveProfile() != null;

    public void Configure(int enemyCount, int configuredSeed)
    {
        if (enemyCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(enemyCount));
        }

        Reset();
        profile = ResolveProfile();

        if (profile == null)
        {
            throw new InvalidOperationException(
                $"Missing Utility AI profile at Resources/{ProfileResourcePath}.");
        }

        seed = configuredSeed;
        tick = 0;
        agents = HybridAiBenchmarkScenarioKernel.CreateAgents(
            enemyCount,
            seed);
        intents = new HybridAiActionIntent[enemyCount];
        decisionLatencies = new double[enemyCount];
        root = new GameObject("Issue64 GO Benchmark Agents");
        root.hideFlags = HideFlags.HideAndDontSave;

        for (int index = 0; index < enemyCount; index++)
        {
            var host = new GameObject(agents[index].StableId);
            host.hideFlags = HideFlags.HideAndDontSave;
            host.transform.SetParent(root.transform, false);
            HybridGameObjectUtilityAdapter adapter =
                host.AddComponent<HybridGameObjectUtilityAdapter>();
            adapter.Configure(
                profile,
                HybridAiExecutionPolicy.HybridDefault);
            adapters.Add(adapter);
        }
    }

    public void Step(float deltaTime)
    {
        if (adapters.Count == 0 || agents.Length != adapters.Count)
        {
            throw new InvalidOperationException(
                "Configure must be called before stepping the GO benchmark.");
        }

        float elapsed = Mathf.Max(0f, deltaTime);
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        long stepStarted = Stopwatch.GetTimestamp();

        for (int index = 0; index < agents.Length; index++)
        {
            HybridAiBenchmarkPerception perception =
                HybridAiBenchmarkScenarioKernel.SamplePerception(
                    index,
                    seed,
                    tick);
            HybridAiBenchmarkNeighborhood neighborhood =
                HybridAiBenchmarkScenarioKernel.CountNeighborhood(
                    agents,
                    index);
            HybridAiWorldSnapshot world =
                HybridAiBenchmarkScenarioKernel.ProjectWorld(
                    agents[index],
                    perception,
                    neighborhood,
                    HybridAiExecutionPolicy.HybridDefault);
            long decisionStarted = Stopwatch.GetTimestamp();
            intents[index] = adapters[index].Evaluate(
                world,
                elapsed,
                DecisionSeed(seed, index));
            decisionLatencies[index] = ElapsedMilliseconds(decisionStarted);
        }

        lastIntentDigest = HybridAiIntentDigestKernel.Compute(intents);
        lastMainThreadMilliseconds = ElapsedMilliseconds(stepStarted);
        lastAllocatedBytes = Math.Max(
            0L,
            GC.GetAllocatedBytesForCurrentThread() - allocatedBefore);
        lastMemoryBytes = Math.Max(0L, Profiler.GetTotalAllocatedMemoryLong());
        tick++;
    }

    public Issue64AdapterMetricsSnapshot CaptureMetrics()
    {
        double[] ordered = new double[decisionLatencies.Length];
        Array.Copy(decisionLatencies, ordered, decisionLatencies.Length);
        Array.Sort(ordered);
        return new Issue64AdapterMetricsSnapshot(
            adapters.Count,
            0,
            Average(ordered),
            Percentile(ordered, 0.95d),
            Percentile(ordered, 0.99d),
            lastMainThreadMilliseconds,
            lastAllocatedBytes,
            lastMemoryBytes,
            lastIntentDigest);
    }

    public void Reset()
    {
        for (int index = 0; index < adapters.Count; index++)
        {
            adapters[index]?.PrepareForPool();
        }

        adapters.Clear();

        if (root != null)
        {
            root.SetActive(false);

            if (Application.isPlaying)
            {
                Object.Destroy(root);
            }
            else
            {
                Object.DestroyImmediate(root);
            }
        }

        root = null;
        agents = Array.Empty<HybridAiBenchmarkAgentState>();
        intents = Array.Empty<HybridAiActionIntent>();
        decisionLatencies = Array.Empty<double>();
        tick = 0;
        lastMainThreadMilliseconds = 0d;
        lastAllocatedBytes = 0L;
        lastMemoryBytes = 0L;
        lastIntentDigest = string.Empty;
    }

    public void Dispose()
    {
        Reset();
    }

    public static long DecisionSeed(int runSeed, int agentIndex)
    {
        return HybridAiBenchmarkScenarioKernel.Hash(runSeed, agentIndex);
    }

    private EnemyUtilityProfileDefinition ResolveProfile()
    {
        profile ??= Resources.Load<EnemyUtilityProfileDefinition>(
            ProfileResourcePath);
        return profile;
    }

    private static double ElapsedMilliseconds(long started)
    {
        return (Stopwatch.GetTimestamp() - started) * 1000d /
            Stopwatch.Frequency;
    }

    private static double Average(IReadOnlyList<double> values)
    {
        if (values == null || values.Count == 0)
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
        IReadOnlyList<double> ordered,
        double percentile)
    {
        if (ordered == null || ordered.Count == 0)
        {
            return 0d;
        }

        int rank = Math.Max(
            0,
            (int)Math.Ceiling(percentile * ordered.Count) - 1);
        return ordered[Math.Min(rank, ordered.Count - 1)];
    }
}

public static class Issue64GameObjectBenchmarkRegistration
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void RegisterRuntime()
    {
        RegisterFactory();
    }

#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
    private static void RegisterEditor()
    {
        RegisterFactory();
    }
#endif

    public static void RegisterFactory()
    {
        Issue64BenchmarkAdapterRegistry.Register(
            HybridAiBenchmarkMode.GameObject,
            () => new Issue64GameObjectBenchmarkAdapter());
    }
}
