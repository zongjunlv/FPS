using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Profiling;

public sealed class Issue13PerformanceBenchmark : MonoBehaviour
{
    private const int DefaultEnemyCount = 24;
    private const float DefaultWarmupSeconds = 5f;
    private const float DefaultSampleSeconds = 20f;
    private const int DefaultSeed = 13013;
    private const int DefaultPerceptionBudget = 4;
    private const string DefaultQualityLevel = "PC";
    private const string BehaviorProfile = "alert-chase-fire-v1";
    private const int MaximumSampleFrames = 24000;

    private readonly double[] frameMilliseconds =
        new double[MaximumSampleFrames];
    private readonly double[] mainThreadMilliseconds =
        new double[MaximumSampleFrames];
    private readonly long[] gcBytes =
        new long[MaximumSampleFrames];

    private WeaponController weapon;
    private Health playerHealth;
    private ProfilerRecorder mainThreadRecorder;
    private ProfilerRecorder gcRecorder;
    private bool scenarioReady;
    private int requestedEnemyCount;
    private float warmupSeconds;
    private float sampleSeconds;
    private int randomSeed;
    private int benchmarkWidth;
    private int benchmarkHeight;
    private FullScreenMode benchmarkScreenMode;
    private string benchmarkQualityLevel;
    private int perceptionBudget;
    private bool aiLodEnabled = true;
    private string variant;
    private string outputPath;
    private int shots;
    private int hits;
    private long peakUnityUsedMemory;
    private long peakGcUsedMemory;
    private PooledEnemyFactory enemyPool;
    private readonly List<EnemySpawnHandle> benchmarkEnemies = new();
    private int poolInstantiateAtSampleStart;

    [RuntimeInitializeOnLoadMethod(
        RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void StartFromCommandLine()
    {
        string[] arguments = Environment.GetCommandLineArgs();

        if (!HasArgument(arguments, "-fps-benchmark"))
        {
            return;
        }

        GameObject host =
            new GameObject("Issue 13 Performance Benchmark");
        host.AddComponent<Issue13PerformanceBenchmark>()
            .Configure(arguments);
    }

    private void Configure(string[] arguments)
    {
        requestedEnemyCount = ReadInt(
            arguments,
            "-benchmark-enemies",
            DefaultEnemyCount);
        warmupSeconds = ReadFloat(
            arguments,
            "-benchmark-warmup",
            DefaultWarmupSeconds);
        sampleSeconds = ReadFloat(
            arguments,
            "-benchmark-duration",
            DefaultSampleSeconds);
        randomSeed = ReadInt(
            arguments,
            "-benchmark-seed",
            DefaultSeed);
        perceptionBudget = Mathf.Max(
            1,
            ReadInt(
                arguments,
                "-benchmark-perception-budget",
                DefaultPerceptionBudget));
        aiLodEnabled = !string.Equals(
            ReadString(arguments, "-benchmark-ai-lod", "enabled"),
            "disabled",
            StringComparison.OrdinalIgnoreCase);
        EnemyAiLodController.SetGlobalEnabled(aiLodEnabled);
        benchmarkWidth = Mathf.Max(
            640,
            ReadInt(arguments, "-benchmark-width", 1280));
        benchmarkHeight = Mathf.Max(
            360,
            ReadInt(arguments, "-benchmark-height", 720));
        benchmarkScreenMode =
            HasArgument(arguments, "-benchmark-fullscreen")
                ? FullScreenMode.FullScreenWindow
                : FullScreenMode.Windowed;
        benchmarkQualityLevel = ReadString(
            arguments,
            "-benchmark-quality",
            DefaultQualityLevel);
        variant = ReadString(
            arguments,
            "-benchmark-variant",
            "unspecified");
        outputPath = ReadString(
            arguments,
            "-benchmark-output",
            Path.Combine(
                Application.persistentDataPath,
                $"issue-13-{variant}.json"));
        requestedEnemyCount = Mathf.Max(3, requestedEnemyCount);
        warmupSeconds = Mathf.Max(1f, warmupSeconds);
        sampleSeconds = Mathf.Max(2f, sampleSeconds);
        int qualityIndex = Array.FindIndex(
            QualitySettings.names,
            quality => string.Equals(
                quality,
                benchmarkQualityLevel,
                StringComparison.OrdinalIgnoreCase));

        if (qualityIndex < 0)
        {
            FailAndQuit(
                $"Unknown quality level '{benchmarkQualityLevel}'.");
            return;
        }

        QualitySettings.SetQualityLevel(qualityIndex, true);
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = -1;
        Application.runInBackground = true;
        Screen.SetResolution(
            benchmarkWidth,
            benchmarkHeight,
            benchmarkScreenMode);
        UnityEngine.Random.InitState(randomSeed);
        StartCoroutine(Run());
    }

    private IEnumerator Run()
    {
        float setupDeadline = Time.realtimeSinceStartup + 15f;

        while (!RuntimeNavMeshBootstrap.IsSceneReady &&
               Time.realtimeSinceStartup < setupDeadline)
        {
            yield return null;
        }

        PlayerCombatController combat =
            FindFirstObjectByType<PlayerCombatController>();

        while ((combat == null || combat.EquippedWeapon == null) &&
               Time.realtimeSinceStartup < setupDeadline)
        {
            combat = FindFirstObjectByType<PlayerCombatController>();
            yield return null;
        }

        if (combat == null || combat.EquippedWeapon == null)
        {
            FailAndQuit("Player weapon was not ready.");
            yield break;
        }

        weapon = combat.EquippedWeapon;
        playerHealth = combat.GetComponent<Health>();
        playerHealth?.Initialize(1000000f);
        weapon.SetSpreadSampleOverride(Vector2.zero);
        weapon.ShotResolved += CountShotResult;
        EnemyPerceptionScheduler.EnsureForActiveScene()
            .Configure(perceptionBudget);
        int actualEnemyCount = BuildEnemyStressGroup(
            combat.transform);

        if (actualEnemyCount != requestedEnemyCount)
        {
            FailAndQuit(
                $"Expected {requestedEnemyCount} enemies, " +
                $"spawned {actualEnemyCount}.");
            yield break;
        }

        scenarioReady = true;
        double warmupEnd =
            Time.realtimeSinceStartupAsDouble + warmupSeconds;

        while (Time.realtimeSinceStartupAsDouble < warmupEnd)
        {
            yield return null;
        }

        StartRecorders();
        poolInstantiateAtSampleStart =
            enemyPool != null ? enemyPool.InstantiateCount : 0;
        int sampleCount = 0;
        double sampleStart = Time.realtimeSinceStartupAsDouble;
        double sampleEnd = sampleStart + sampleSeconds;

        while (Time.realtimeSinceStartupAsDouble < sampleEnd &&
               sampleCount < MaximumSampleFrames)
        {
            yield return null;
            double frameMs = Time.unscaledDeltaTime * 1000.0;
            frameMilliseconds[sampleCount] = frameMs;
            mainThreadMilliseconds[sampleCount] =
                mainThreadRecorder.Valid
                    ? mainThreadRecorder.LastValue / 1000000.0
                    : frameMs;
            gcBytes[sampleCount] = gcRecorder.Valid
                ? Math.Max(0L, gcRecorder.LastValue)
                : 0L;
            peakUnityUsedMemory = Math.Max(
                peakUnityUsedMemory,
                Profiler.GetTotalAllocatedMemoryLong());
            peakGcUsedMemory = Math.Max(
                peakGcUsedMemory,
                GC.GetTotalMemory(false));
            sampleCount++;
        }

        double actualDuration =
            Time.realtimeSinceStartupAsDouble - sampleStart;
        Issue13BenchmarkReport report = CreateReport(
            actualEnemyCount,
            sampleCount,
            actualDuration);
        StopRecorders();
        string directory = Path.GetDirectoryName(outputPath);

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(
            outputPath,
            JsonUtility.ToJson(report, true));
        Debug.Log(
            $"ISSUE13_PERF_RESULT {outputPath}\n" +
            JsonUtility.ToJson(report));
        Application.Quit(0);
    }

    private void Update()
    {
        if (!scenarioReady || weapon == null)
        {
            return;
        }

        if (weapon.CurrentAmmo == 0)
        {
            weapon.TryStartReload();
            return;
        }

        weapon.TryFire();

        if (playerHealth != null &&
            playerHealth.CurrentHealth < 500000f)
        {
            playerHealth.Initialize(1000000f);
        }
    }

    private int BuildEnemyStressGroup(Transform player)
    {
        CityNewWaveBootstrap waveBootstrap =
            FindFirstObjectByType<CityNewWaveBootstrap>();
        waveBootstrap?.Director?.StopRun(WaveStopReason.Disabled);
        enemyPool = waveBootstrap != null
            ? waveBootstrap.EnemyPool
            : FindFirstObjectByType<PooledEnemyFactory>();
        enemyPool?.FlushPendingReleases();
        EnemyController[] existing = FindObjectsByType<EnemyController>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        if (existing.Length == 0 || enemyPool == null)
        {
            return 0;
        }

        enemyPool.EnsureCapacity(requestedEnemyCount);

        EnemyController template = existing[0];

        foreach (EnemyController enemy in existing)
        {
            if (enemy.name == "SPIDER_BOT")
            {
                template = enemy;
                break;
            }
        }

        var occupied = new List<Vector3>(requestedEnemyCount);

        float angleOffset = Mathf.Abs(randomSeed % 360) * Mathf.Deg2Rad;

        for (int candidate = 0;
             candidate < 4096 &&
             benchmarkEnemies.Count < requestedEnemyCount;
             candidate++)
        {
            float angle = angleOffset +
                candidate * 137.50776f * Mathf.Deg2Rad;
            float radius = 6f + Mathf.Sqrt(candidate) * 1.7f;
            Vector3 desired = template.transform.position +
                new Vector3(
                    Mathf.Cos(angle),
                    0f,
                    Mathf.Sin(angle)) * radius;

            if (!NavMesh.SamplePosition(
                    desired,
                    out NavMeshHit hit,
                    3f,
                    NavMesh.AllAreas) ||
                IsTooClose(hit.position, occupied))
            {
                continue;
            }

            var request = new EnemySpawnRequest(
                260000 + benchmarkEnemies.Count,
                1,
                null,
                hit.position,
                template.transform.rotation,
                player);

            if (!enemyPool.TrySpawn(
                    request,
                    (_, _) => { },
                    out EnemySpawnHandle handle))
            {
                continue;
            }

            handle.Controller.name =
                $"BENCHMARK SPIDER {benchmarkEnemies.Count + 1:00}";
            PrepareEnemy(handle.Controller, player);
            benchmarkEnemies.Add(handle);
            occupied.Add(hit.position);
        }

        return benchmarkEnemies.Count;
    }

    private static void PrepareEnemy(
        EnemyController enemy,
        Transform player)
    {
        enemy.GetComponent<Health>()?.Initialize(1000000f);
        EnemyPerceptionController perception =
            enemy.GetComponent<EnemyPerceptionController>();

        if (perception == null)
        {
            return;
        }

        perception.SetTarget(player);
        perception.Configure(100f, 360f, 100f, 0.4f, 5f);
        FieldInfo overlayField =
            typeof(EnemyPerceptionController).GetField(
                "showDebugOverlay",
                BindingFlags.Instance | BindingFlags.NonPublic);
        overlayField?.SetValue(perception, false);
    }

    private static bool IsTooClose(
        Vector3 candidate,
        List<Vector3> occupied)
    {
        foreach (Vector3 position in occupied)
        {
            if (Vector3.Distance(candidate, position) < 1.2f)
            {
                return true;
            }
        }

        return false;
    }

    private void CountShotResult(ShotResult result)
    {
        shots++;

        if (result.DidHit)
        {
            hits++;
        }
    }

    private void StartRecorders()
    {
        mainThreadRecorder = ProfilerRecorder.StartNew(
            ProfilerCategory.Internal,
            "Main Thread",
            1);
        gcRecorder = ProfilerRecorder.StartNew(
            ProfilerCategory.Memory,
            "GC Allocated In Frame",
            1);
    }

    private void StopRecorders()
    {
        mainThreadRecorder.Dispose();
        gcRecorder.Dispose();
    }

    private Issue13BenchmarkReport CreateReport(
        int actualEnemyCount,
        int sampleCount,
        double actualDuration)
    {
        Array.Sort(frameMilliseconds, 0, sampleCount);
        Array.Sort(mainThreadMilliseconds, 0, sampleCount);
        Array.Sort(gcBytes, 0, sampleCount);
        double averageFrameMs =
            Average(frameMilliseconds, sampleCount);
        long allocatedFrames = 0;

        for (int index = 0; index < sampleCount; index++)
        {
            if (gcBytes[index] > 0L)
            {
                allocatedFrames++;
            }
        }

        return new Issue13BenchmarkReport
        {
            variant = variant,
            unityVersion = Application.unityVersion,
            operatingSystem = SystemInfo.operatingSystem,
            processor = SystemInfo.processorType,
            processorCount = SystemInfo.processorCount,
            systemMemoryMb = SystemInfo.systemMemorySize,
            graphicsDevice = SystemInfo.graphicsDeviceName,
            graphicsMemoryMb = SystemInfo.graphicsMemorySize,
            resolution = $"{Screen.width}x{Screen.height}",
            configuredResolution =
                $"{benchmarkWidth}x{benchmarkHeight}",
            screenMode = Screen.fullScreenMode.ToString(),
            qualityLevel = QualitySettings.names[
                QualitySettings.GetQualityLevel()],
            buildType = Debug.isDebugBuild
                ? "Development"
                : "Release",
            behaviorProfile = BehaviorProfile,
            aiLodEnabled = aiLodEnabled,
            perceptionChecksPerFrame = perceptionBudget,
            enemyCount = actualEnemyCount,
            warmupSeconds = warmupSeconds,
            configuredSampleSeconds = sampleSeconds,
            sampleSeconds = actualDuration,
            seed = randomSeed,
            sampleFrames = sampleCount,
            shots = shots,
            hits = hits,
            averageFps = actualDuration > 0.0
                ? sampleCount / actualDuration
                : 0.0,
            averageFrameMs = averageFrameMs,
            p95FrameMs = Percentile(
                frameMilliseconds,
                sampleCount,
                0.95),
            p99FrameMs = Percentile(
                frameMilliseconds,
                sampleCount,
                0.99),
            onePercentLowFps = SlowestPercentAverage(
                    frameMilliseconds,
                    sampleCount,
                    0.01) > 0.0
                ? 1000.0 / SlowestPercentAverage(
                    frameMilliseconds,
                    sampleCount,
                    0.01)
                : 0.0,
            averageMainThreadMs = Average(
                mainThreadMilliseconds,
                sampleCount),
            p95MainThreadMs = Percentile(
                mainThreadMilliseconds,
                sampleCount,
                0.95),
            p99MainThreadMs = Percentile(
                mainThreadMilliseconds,
                sampleCount,
                0.99),
            averageGcBytesPerFrame = Average(
                gcBytes,
                sampleCount),
            p95GcBytesPerFrame = Percentile(
                gcBytes,
                sampleCount,
                0.95),
            maximumGcBytesInFrame = sampleCount > 0
                ? gcBytes[sampleCount - 1]
                : 0L,
            totalGcBytes = Sum(gcBytes, sampleCount),
            gcAllocFramePercent = sampleCount > 0
                ? allocatedFrames * 100.0 / sampleCount
                : 0.0,
            peakUnityUsedMemoryMb =
                peakUnityUsedMemory / 1048576.0,
            peakManagedMemoryMb =
                peakGcUsedMemory / 1048576.0,
            mainThreadRecorderValid = mainThreadRecorder.Valid,
            gcRecorderValid = gcRecorder.Valid,
            perceptionChecks = ReadPerceptionChecks(),
            maximumPerceptionLatencyFrames =
                ReadMaximumPerceptionLatencyFrames(),
            maximumPerceptionLatencyMs =
                ReadMaximumPerceptionLatencyFrames() * averageFrameMs,
            nearEnemyCount = CountLodTier(EnemyAiLodTier.Near),
            midEnemyCount = CountLodTier(EnemyAiLodTier.Mid),
            farEnemyCount = CountLodTier(EnemyAiLodTier.Far),
            aiDecisionTicks = ReadLodMetric(
                controller => controller.ExecutedDecisionTicks),
            aiSkippedDecisionTicks = ReadLodMetric(
                controller => controller.SkippedDecisionTicks),
            maximumAiDecisionLatencyFrames = (int)ReadLodMetric(
                controller => controller.MaximumDecisionLatencyFrames,
                true),
            enemyPoolObjects = enemyPool?.PooledObjectCount ?? 0,
            enemyPoolReuseCount = enemyPool?.ReuseCount ?? 0,
            enemyPoolExpansionCount = enemyPool?.ExpansionCount ?? 0,
            stableSampleInstantiateCount = enemyPool != null
                ? enemyPool.InstantiateCount - poolInstantiateAtSampleStart
                : -1,
            poolSummary = ReadPoolSummary()
        };
    }

    private static int ReadMaximumPerceptionLatencyFrames()
    {
        int maximum = 0;

        foreach (EnemyPerceptionController perception in
                 FindObjectsByType<EnemyPerceptionController>(
                     FindObjectsInactive.Exclude,
                     FindObjectsSortMode.None))
        {
            maximum = Mathf.Max(
                maximum,
                perception.MaximumSightCheckLatencyFrames);
        }

        return maximum;
    }

    private static long ReadPerceptionChecks()
    {
        EnemyPerceptionScheduler scheduler =
            EnemyPerceptionScheduler.Instance;
        return scheduler != null
            ? scheduler.TotalCheckCount
            : -1L;
    }

    private static int CountLodTier(EnemyAiLodTier tier)
    {
        int count = 0;

        foreach (EnemyAiLodController controller in
                 FindObjectsByType<EnemyAiLodController>(
                     FindObjectsInactive.Exclude,
                     FindObjectsSortMode.None))
        {
            if (controller.CurrentTier == tier)
            {
                count++;
            }
        }

        return count;
    }

    private static long ReadLodMetric(
        Func<EnemyAiLodController, int> selector,
        bool maximum = false)
    {
        long value = 0L;

        foreach (EnemyAiLodController controller in
                 FindObjectsByType<EnemyAiLodController>(
                     FindObjectsInactive.Exclude,
                     FindObjectsSortMode.None))
        {
            value = maximum
                ? Math.Max(value, selector(controller))
                : value + selector(controller);
        }

        return value;
    }

    private static string ReadPoolSummary()
    {
        CombatEffectPool pool =
            FindFirstObjectByType<CombatEffectPool>();

        if (pool == null)
        {
            return "not active";
        }

        return $"concrete={pool.ConcreteCapacity}, " +
               $"metal={pool.MetalCapacity}, " +
               $"audio={pool.AudioCapacityValue}";
    }

    private static void FailAndQuit(string reason)
    {
        Debug.LogError($"ISSUE13_PERF_FAILED {reason}");
        Application.Quit(2);
    }

    private static bool HasArgument(
        string[] arguments,
        string key)
    {
        return Array.IndexOf(arguments, key) >= 0;
    }

    private static string ReadString(
        string[] arguments,
        string key,
        string fallback)
    {
        int index = Array.IndexOf(arguments, key);
        return index >= 0 && index + 1 < arguments.Length
            ? arguments[index + 1]
            : fallback;
    }

    private static int ReadInt(
        string[] arguments,
        string key,
        int fallback)
    {
        return int.TryParse(
            ReadString(arguments, key, fallback.ToString()),
            out int value)
            ? value
            : fallback;
    }

    private static float ReadFloat(
        string[] arguments,
        string key,
        float fallback)
    {
        return float.TryParse(
            ReadString(arguments, key, fallback.ToString()),
            out float value)
            ? value
            : fallback;
    }

    private static double Average(
        double[] values,
        int count)
    {
        if (count <= 0)
        {
            return 0.0;
        }

        double sum = 0.0;

        for (int index = 0; index < count; index++)
        {
            sum += values[index];
        }

        return sum / count;
    }

    private static double Average(long[] values, int count)
    {
        if (count <= 0)
        {
            return 0.0;
        }

        double sum = 0.0;

        for (int index = 0; index < count; index++)
        {
            sum += values[index];
        }

        return sum / count;
    }

    private static double Percentile(
        double[] values,
        int count,
        double percentile)
    {
        if (count <= 0)
        {
            return 0.0;
        }

        int index = Mathf.Clamp(
            Mathf.CeilToInt(
                (float)(count * percentile)) - 1,
            0,
            count - 1);
        return values[index];
    }

    private static double SlowestPercentAverage(
        double[] values,
        int count,
        double fraction)
    {
        if (count <= 0)
        {
            return 0.0;
        }

        int slowCount = Math.Max(1, (int)Math.Ceiling(count * fraction));
        double sum = 0.0;

        for (int index = count - slowCount; index < count; index++)
        {
            sum += values[index];
        }

        return sum / slowCount;
    }

    private static long Sum(long[] values, int count)
    {
        long sum = 0L;

        for (int index = 0; index < count; index++)
        {
            sum += values[index];
        }

        return sum;
    }

    private static double Percentile(
        long[] values,
        int count,
        double percentile)
    {
        if (count <= 0)
        {
            return 0.0;
        }

        int index = Mathf.Clamp(
            Mathf.CeilToInt(
                (float)(count * percentile)) - 1,
            0,
            count - 1);
        return values[index];
    }

    private void OnDestroy()
    {
        if (weapon != null)
        {
            weapon.ShotResolved -= CountShotResult;
        }

        if (enemyPool != null)
        {
            foreach (EnemySpawnHandle handle in benchmarkEnemies)
            {
                enemyPool.Release(handle);
            }
        }

        if (mainThreadRecorder.Valid)
        {
            mainThreadRecorder.Dispose();
        }

        if (gcRecorder.Valid)
        {
            gcRecorder.Dispose();
        }

        EnemyAiLodController.SetGlobalEnabled(true);
    }
}

[Serializable]
public sealed class Issue13BenchmarkReport
{
    public string variant;
    public string unityVersion;
    public string operatingSystem;
    public string processor;
    public int processorCount;
    public int systemMemoryMb;
    public string graphicsDevice;
    public int graphicsMemoryMb;
    public string resolution;
    public string configuredResolution;
    public string screenMode;
    public string qualityLevel;
    public string buildType;
    public string behaviorProfile;
    public bool aiLodEnabled;
    public int perceptionChecksPerFrame;
    public int enemyCount;
    public double warmupSeconds;
    public double configuredSampleSeconds;
    public double sampleSeconds;
    public int seed;
    public int sampleFrames;
    public int shots;
    public int hits;
    public double averageFps;
    public double averageFrameMs;
    public double p95FrameMs;
    public double p99FrameMs;
    public double onePercentLowFps;
    public double averageMainThreadMs;
    public double p95MainThreadMs;
    public double p99MainThreadMs;
    public double averageGcBytesPerFrame;
    public double p95GcBytesPerFrame;
    public long maximumGcBytesInFrame;
    public long totalGcBytes;
    public double gcAllocFramePercent;
    public double peakUnityUsedMemoryMb;
    public double peakManagedMemoryMb;
    public bool mainThreadRecorderValid;
    public bool gcRecorderValid;
    public long perceptionChecks;
    public int maximumPerceptionLatencyFrames;
    public double maximumPerceptionLatencyMs;
    public int nearEnemyCount;
    public int midEnemyCount;
    public int farEnemyCount;
    public long aiDecisionTicks;
    public long aiSkippedDecisionTicks;
    public int maximumAiDecisionLatencyFrames;
    public int enemyPoolObjects;
    public int enemyPoolReuseCount;
    public int enemyPoolExpansionCount;
    public int stableSampleInstantiateCount;
    public string poolSummary;
}
