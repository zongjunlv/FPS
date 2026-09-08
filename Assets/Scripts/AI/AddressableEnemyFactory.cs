using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public interface IEnemyTemplateLoadOperation : IDisposable
{
    bool IsDone { get; }
    bool Succeeded { get; }
    float Progress { get; }
    GameObject Template { get; }
    string Error { get; }
}

public interface IEnemyTemplateLoader
{
    IEnemyTemplateLoadOperation Load(string address);
}

public sealed class AddressableEnemyTemplateLoader : IEnemyTemplateLoader
{
    private sealed class Operation : IEnemyTemplateLoadOperation
    {
        private readonly SharedAssetLease<GameObject> lease;
        public Operation(string address) => lease = SharedAssetLeaseService.Default.Acquire<GameObject>(address);
        public bool IsDone => lease.IsDone;
        public bool Succeeded => lease.Succeeded;
        public float Progress => lease.Progress;
        public GameObject Template => lease.Asset;
        public string Error => lease.Error;
        public void Dispose() => lease.Dispose();
    }

    public IEnemyTemplateLoadOperation Load(string address) => new Operation(address);
}

public sealed class AddressableEnemyFactory : MonoBehaviour, IAsyncEnemyFactory
{
    private readonly List<string> addresses = new();
    private readonly Dictionary<string, EnemyController> templates = new();
    private readonly List<IEnemyTemplateLoadOperation> loads = new();
    private IEnemyTemplateLoader loader;
    private PooledEnemyFactory pool;
    private int prewarmPerTemplate;
    private int capacityLimit;
    private bool configured;
    private EnemyController fallbackTemplate;

    public EnemyFactoryPreparationState PreparationState { get; private set; }
    public float PreparationProgress { get; private set; }
    public string PreparationError { get; private set; } = string.Empty;
    public PooledEnemyFactory Pool => pool;
    public int LoadedTemplateCount => templates.Count;
    public int FallbackTemplateCount { get; private set; }

    public void Configure(
        IEnumerable<EnemyArchetypeDefinition> archetypes,
        int prewarmCount = 4,
        int maximumCapacity = 64,
        IEnemyTemplateLoader templateLoader = null,
        EnemyController safeFallbackTemplate = null)
    {
        if (PreparationState == EnemyFactoryPreparationState.Disposed)
            throw new ObjectDisposedException(nameof(AddressableEnemyFactory));
        if (configured) throw new InvalidOperationException("Enemy factory is already configured.");
        if (archetypes == null) throw new ArgumentNullException(nameof(archetypes));
        foreach (EnemyArchetypeDefinition archetype in archetypes)
        {
            string address = archetype != null ? archetype.TemplateAddress : null;
            if (string.IsNullOrWhiteSpace(address))
                throw new ArgumentException("Every enemy archetype requires a template address.");
            if (!addresses.Contains(address)) addresses.Add(address);
        }
        if (addresses.Count == 0) throw new ArgumentException("No enemy template addresses configured.");
        prewarmPerTemplate = Mathf.Max(1, prewarmCount);
        capacityLimit = Mathf.Max(maximumCapacity, prewarmPerTemplate * addresses.Count);
        loader = templateLoader ?? new AddressableEnemyTemplateLoader();
        fallbackTemplate = safeFallbackTemplate;
        pool = GetComponent<PooledEnemyFactory>() ?? gameObject.AddComponent<PooledEnemyFactory>();
        configured = true;
    }

    public IEnumerator PrepareAsync()
    {
        if (!configured) throw new InvalidOperationException("Configure the factory before preparation.");
        if (PreparationState != EnemyFactoryPreparationState.Idle)
        {
            while (PreparationState == EnemyFactoryPreparationState.Loading ||
                   PreparationState == EnemyFactoryPreparationState.Prewarming) yield return null;
            yield break;
        }
        PreparationState = EnemyFactoryPreparationState.Loading;
        for (int index = 0; index < addresses.Count; index++)
        {
            IEnemyTemplateLoadOperation operation = null;
            string loadError = null;
            try
            {
                operation = loader.Load(addresses[index]);
                if (operation == null) throw new InvalidOperationException("Loader returned no operation.");
                loads.Add(operation);
            }
            catch (Exception exception)
            {
                loadError = exception.Message;
            }
            while (operation != null && !operation.IsDone)
            {
                if (PreparationState == EnemyFactoryPreparationState.Disposed) yield break;
                PreparationProgress = (index + operation.Progress) / addresses.Count * 0.5f;
                yield return null;
            }
            if (PreparationState == EnemyFactoryPreparationState.Disposed) yield break;
            EnemyController template = operation?.Template != null
                ? operation.Template.GetComponent<EnemyController>() : null;
            if (operation == null || !operation.Succeeded || template == null)
            {
                string reason = $"{addresses[index]}: {loadError ?? operation?.Error ?? "EnemyController missing from prefab."}";
                if (fallbackTemplate == null)
                {
                    Fail(reason);
                    yield break;
                }
                if (operation != null)
                {
                    loads.Remove(operation);
                    operation.Dispose();
                }
                template = fallbackTemplate;
                FallbackTemplateCount++;
                Debug.LogWarning($"敌人资源加载失败，已使用场景备用模板：{reason}", this);
            }
            templates.Add(addresses[index], template);
        }

        PreparationState = EnemyFactoryPreparationState.Prewarming;
        try
        {
            pool.ConfigurePrefab(templates[addresses[0]], capacityLimit);
        }
        catch (Exception exception)
        {
            Fail($"Enemy pool configuration failed: {exception.Message}");
            yield break;
        }
        int warmed = 0;
        foreach (string address in addresses)
        {
            for (int index = 0; index < prewarmPerTemplate; index++)
            {
                if (PreparationState == EnemyFactoryPreparationState.Disposed) yield break;
                bool allocated;
                try
                {
                    allocated = pool.PrewarmOne(templates[address]);
                }
                catch (Exception exception)
                {
                    Fail($"{address}: Enemy pool prewarm failed: {exception.Message}");
                    yield break;
                }
                if (!allocated)
                {
                    Fail("Enemy pool rejected prewarm allocation.");
                    yield break;
                }
                warmed++;
                PreparationProgress = 0.5f + 0.5f * warmed / (prewarmPerTemplate * addresses.Count);
                yield return null;
            }
        }
        if (PreparationState == EnemyFactoryPreparationState.Disposed) yield break;
        PreparationProgress = 1f;
        PreparationState = EnemyFactoryPreparationState.Ready;
    }

    public bool TrySpawn(EnemySpawnRequest request,
        Action<EnemySpawnHandle, EnemyExitReason> onEnded, out EnemySpawnHandle handle)
    {
        handle = default;
        string address = request.Entry?.Archetype?.TemplateAddress;
        if (PreparationState != EnemyFactoryPreparationState.Ready ||
            string.IsNullOrEmpty(address) || !templates.TryGetValue(address, out EnemyController template)) return false;
        return pool.TrySpawnWithTemplate(request, template, onEnded, out handle);
    }

    public void Release(EnemySpawnHandle handle)
    {
        if (PreparationState != EnemyFactoryPreparationState.Disposed && pool != null) pool.Release(handle);
    }

    public void DisposeFactory()
    {
        if (PreparationState == EnemyFactoryPreparationState.Disposed) return;
        PreparationState = EnemyFactoryPreparationState.Disposed;
        ReleaseResources();
    }

    public void CancelPreparation() => DisposeFactory();

    private void Fail(string error)
    {
        PreparationError = error;
        PreparationState = EnemyFactoryPreparationState.Failed;
        ReleaseResources();
    }

    private void ReleaseResources()
    {
        if (pool != null) pool.DisposePool();
        foreach (IEnemyTemplateLoadOperation operation in loads) operation.Dispose();
        loads.Clear();
        templates.Clear();
    }

    private void OnDestroy() => DisposeFactory();
}
