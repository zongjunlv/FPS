using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public sealed class PooledEnemyFactory : MonoBehaviour, IEnemyFactory
{
    private sealed class Bucket
    {
        public EnemyController Template;
        public readonly Stack<EnemyController> Available = new();
        public readonly HashSet<EnemyController> All = new();
    }

    private readonly struct Lease
    {
        public Lease(int spawnId, Bucket bucket)
        {
            SpawnId = spawnId;
            Bucket = bucket;
        }

        public int SpawnId { get; }
        public Bucket Bucket { get; }
    }

    [SerializeField, Min(1)] private int prewarmCapacity = 4;
    [SerializeField, Min(1)] private int maximumCapacity = 64;

    private EnemyController defaultTemplate;
    private readonly Dictionary<EnemyController, Bucket> buckets = new();
    private readonly Dictionary<EnemyController, Lease> active = new();
    private readonly List<(EnemyController Controller, Bucket Bucket)> pending = new();
    private Transform poolRoot;

    public int ActiveCount => active.Count;
    public int PendingReleaseCount => pending.Count;
    public int AvailableCount { get; private set; }
    public int PooledObjectCount { get; private set; }
    public int InstantiateCount { get; private set; }
    public int ReuseCount { get; private set; }
    public int ExpansionCount { get; private set; }
    public int ReleaseCount { get; private set; }
    public int SuccessfulSpawnCount { get; private set; }

    public void Configure(
        EnemyController template,
        int configuredPrewarmCapacity,
        int configuredMaximumCapacity)
    {
        if (template == null)
        {
            throw new ArgumentNullException(nameof(template));
        }

        if (active.Count > 0 || pending.Count > 0)
        {
            throw new InvalidOperationException(
                "Cannot reconfigure an enemy pool while leases are active.");
        }

        ClearPool();
        defaultTemplate = template;
        prewarmCapacity = Mathf.Max(1, configuredPrewarmCapacity);
        maximumCapacity = Mathf.Max(prewarmCapacity, configuredMaximumCapacity);
        EnsureRoot();
        Bucket bucket = GetOrCreateBucket(template);

        // 先复制活动模板，避免从未完成 Awake 的停用对象创建副本。
        for (int index = 1; index < prewarmCapacity; index++)
        {
            EnemyController clone = CreateInstance(template, bucket, false);
            ReturnImmediately(clone, bucket);
        }

        template.SetFactoryManaged(true);
        bucket.All.Add(template);
        PooledObjectCount++;
        template.GetComponent<WaveEnemyLifecycle>()?.Disarm();
        template.PrepareForPool();
        template.gameObject.SetActive(false);
        template.transform.SetParent(poolRoot, true);
        bucket.Available.Push(template);
        AvailableCount++;
    }

    public void EnsureCapacity(int requiredCapacity)
    {
        if (defaultTemplate == null)
        {
            throw new InvalidOperationException(
                "Enemy pool must be configured before it is resized.");
        }

        if (active.Count > 0 || pending.Count > 0)
        {
            throw new InvalidOperationException(
                "Cannot resize an enemy pool while leases are active.");
        }

        int targetCapacity = Mathf.Max(1, requiredCapacity);
        maximumCapacity = Mathf.Max(maximumCapacity, targetCapacity);
        Bucket bucket = GetOrCreateBucket(defaultTemplate);

        while (PooledObjectCount < targetCapacity)
        {
            EnemyController clone = CreateInstance(
                defaultTemplate,
                bucket,
                false);
            ReturnImmediately(clone, bucket);
        }
    }

    public bool TrySpawn(
        EnemySpawnRequest request,
        Action<EnemySpawnHandle, EnemyExitReason> onEnded,
        out EnemySpawnHandle handle)
    {
        EnemyController source = request.Entry?.Template ?? defaultTemplate;

        if (source == null)
        {
            handle = default;
            return false;
        }

        Bucket bucket = GetOrCreateBucket(source);
        EnemyController instance = Rent(bucket);

        if (instance == null)
        {
            handle = default;
            return false;
        }

        instance.transform.SetParent(null, true);
        instance.transform.SetPositionAndRotation(
            request.Position,
            request.Rotation);
        instance.name = $"SPIDER_BOT WAVE {request.SpawnId:000}";
        instance.SetFactoryManaged(true);
        instance.ResetForSpawn(request.Target);
        instance.gameObject.SetActive(true);

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
        navigation?.ResetForSpawn();

        if (navigation == null || !navigation.UsesNavMesh)
        {
            instance.PrepareForPool();
            instance.gameObject.SetActive(false);
            instance.transform.SetParent(poolRoot, true);
            bucket.Available.Push(instance);
            AvailableCount++;
            handle = default;
            return false;
        }

        WaveEnemyLifecycle lifecycle =
            instance.GetComponent<WaveEnemyLifecycle>() ??
            instance.gameObject.AddComponent<WaveEnemyLifecycle>();
        string enemyTypeId = request.Entry?.EnemyTypeId;
        LootRewardTier rewardTier =
            request.Entry?.RewardTier ?? LootRewardTier.Normal;
        lifecycle.Arm(
            request.SpawnId,
            request.WaveNumber,
            instance,
            enemyTypeId,
            rewardTier,
            instance.SpawnResetCount,
            onEnded);
        handle = new EnemySpawnHandle(
            request.SpawnId,
            request.WaveNumber,
            instance,
            lifecycle,
            enemyTypeId,
            rewardTier,
            instance.SpawnResetCount);
        active.Add(instance, new Lease(request.SpawnId, bucket));
        SuccessfulSpawnCount++;
        return true;
    }

    public void Release(EnemySpawnHandle handle)
    {
        if (!handle.IsValid ||
            !active.TryGetValue(handle.Controller, out Lease lease) ||
            lease.SpawnId != handle.SpawnId ||
            handle.Lifecycle.SpawnId != handle.SpawnId ||
            handle.Generation != handle.Controller.SpawnResetCount ||
            handle.Lifecycle.Generation != handle.Generation)
        {
            return;
        }

        active.Remove(handle.Controller);
        handle.Lifecycle.Disarm();
        handle.Controller.PrepareForPool();
        pending.Add((handle.Controller, lease.Bucket));
        ReleaseCount++;
    }

    public void FlushPendingReleases()
    {
        for (int index = 0; index < pending.Count; index++)
        {
            (EnemyController controller, Bucket bucket) = pending[index];

            if (controller == null)
            {
                continue;
            }

            controller.gameObject.SetActive(false);
            controller.transform.SetParent(poolRoot, true);
            bucket.Available.Push(controller);
            AvailableCount++;
        }

        pending.Clear();
    }

    private void LateUpdate()
    {
        FlushPendingReleases();
    }

    private EnemyController Rent(Bucket bucket)
    {
        EnemyController instance = null;

        while (bucket.Available.Count > 0 && instance == null)
        {
            instance = bucket.Available.Pop();
            AvailableCount--;
        }

        if (instance != null)
        {
            ReuseCount++;
            return instance;
        }

        if (PooledObjectCount >= maximumCapacity)
        {
            return null;
        }

        ExpansionCount++;
        return CreateInstance(bucket.Template, bucket, true);
    }

    private EnemyController CreateInstance(
        EnemyController template,
        Bucket bucket,
        bool expansion)
    {
        EnemyController instance = Instantiate(template, poolRoot);
        instance.name = $"{template.name} [Pool]";
        instance.SetFactoryManaged(true);
        bucket.All.Add(instance);
        PooledObjectCount++;
        InstantiateCount++;

        // 由休眠模板复制的对象不会立刻执行 Awake。扩容阶段先激活一次，
        // 确保 EnemyController 的碰撞体、刚体和动画缓存已完整建立。
        if (!instance.gameObject.activeSelf)
        {
            instance.gameObject.SetActive(true);
        }

        if (expansion)
        {
            instance.PrepareForPool();
            instance.gameObject.SetActive(false);
        }

        return instance;
    }

    private void ReturnImmediately(EnemyController instance, Bucket bucket)
    {
        instance.GetComponent<WaveEnemyLifecycle>()?.Disarm();
        instance.PrepareForPool();
        instance.gameObject.SetActive(false);
        bucket.Available.Push(instance);
        AvailableCount++;
    }

    private Bucket GetOrCreateBucket(EnemyController template)
    {
        if (buckets.TryGetValue(template, out Bucket existing))
        {
            return existing;
        }

        var bucket = new Bucket { Template = template };
        buckets.Add(template, bucket);
        return bucket;
    }

    private void EnsureRoot()
    {
        if (poolRoot != null)
        {
            return;
        }

        var root = new GameObject("Enemy Pool");
        root.transform.SetParent(transform, false);
        poolRoot = root.transform;
    }

    private void ClearPool()
    {
        foreach (Bucket bucket in buckets.Values)
        {
            foreach (EnemyController controller in bucket.All)
            {
                if (controller != null && controller != defaultTemplate)
                {
                    Destroy(controller.gameObject);
                }
            }
        }

        buckets.Clear();
        active.Clear();
        pending.Clear();
        AvailableCount = 0;
        PooledObjectCount = 0;
        InstantiateCount = 0;
        ReuseCount = 0;
        ExpansionCount = 0;
        ReleaseCount = 0;
        SuccessfulSpawnCount = 0;
    }
}
