using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using Object = UnityEngine.Object;

public interface ISharedAssetLoadOperation<T> : IDisposable where T : Object
{
    bool IsDone { get; }
    bool Succeeded { get; }
    T Asset { get; }
    string Error { get; }
    float Progress { get; }
}

public interface ISharedAssetLoadBackend
{
    ISharedAssetLoadOperation<T> Load<T>(string address) where T : Object;
}

/// <summary>Main-thread-only Addressables ownership. Each consumer owns a lease, never a raw handle.</summary>
public sealed class SharedAssetLeaseService
{
    private interface IEntry { int References { get; set; } }
    private sealed class Entry<T> : IEntry where T : Object
    {
        public ISharedAssetLoadOperation<T> Operation;
        public int References { get; set; }
    }

    private sealed class AddressableBackend : ISharedAssetLoadBackend
    {
        public ISharedAssetLoadOperation<T> Load<T>(string address) where T : Object => new Operation<T>(address);
        private sealed class Operation<T> : ISharedAssetLoadOperation<T> where T : Object
        {
            private AsyncOperationHandle<T> handle;
            public Operation(string address) => handle = Addressables.LoadAssetAsync<T>(address);
            public bool IsDone => handle.IsValid() && handle.IsDone;
            public bool Succeeded => IsDone && handle.Status == AsyncOperationStatus.Succeeded;
            public T Asset => Succeeded ? handle.Result : null;
            public string Error => handle.IsValid() ? handle.OperationException?.Message : "Load handle released.";
            public float Progress => handle.IsValid() ? handle.PercentComplete : 0f;
            public void Dispose()
            {
                // Release also works while loading: Addressables releases the result on completion.
                if (handle.IsValid()) Addressables.Release(handle);
                handle = default;
            }
        }
    }

    private static SharedAssetLeaseService defaultService;
    public static SharedAssetLeaseService Default => defaultService ??= new SharedAssetLeaseService();
    private readonly Dictionary<(string, Type), IEntry> entries = new();
    private readonly ISharedAssetLoadBackend backend;
    private readonly int ownerThread = Thread.CurrentThread.ManagedThreadId;
    public int ActiveHandleCount => entries.Count;
    public int ReferenceCount { get; private set; }

    public SharedAssetLeaseService(ISharedAssetLoadBackend backend = null) => this.backend = backend ?? new AddressableBackend();

    public SharedAssetLease<T> Acquire<T>(string address) where T : Object
    {
        AssertOwnerThread();
        if (string.IsNullOrWhiteSpace(address)) throw new ArgumentException("Asset address is required.", nameof(address));
        var key = (address, typeof(T));
        if (!entries.TryGetValue(key, out IEntry existing))
        {
            var operation = backend.Load<T>(address) ?? throw new InvalidOperationException("Asset backend returned no operation.");
            existing = new Entry<T> { Operation = operation };
            entries.Add(key, existing);
        }
        var entry = (Entry<T>)existing;
        entry.References++;
        ReferenceCount++;
        return new SharedAssetLease<T>(entry.Operation, AssertOwnerThread, () =>
        {
            entry.References--;
            ReferenceCount--;
            if (entry.References != 0) return;
            entries.Remove(key);
            entry.Operation.Dispose();
        });
    }

    public int GetReferenceCount<T>(string address) where T : Object
    {
        AssertOwnerThread();
        return entries.TryGetValue((address, typeof(T)), out IEntry entry) ? entry.References : 0;
    }

    public SharedAssetLeaseScope CreateScope()
    {
        AssertOwnerThread();
        return new SharedAssetLeaseScope(this);
    }

    internal void AssertOwnerThread()
    {
        if (Thread.CurrentThread.ManagedThreadId != ownerThread)
            throw new InvalidOperationException("Asset leases must be used on their owning Unity main thread.");
    }
}

public sealed class SharedAssetLease<T> : IDisposable where T : Object
{
    private readonly ISharedAssetLoadOperation<T> operation;
    private readonly Action assertOwnerThread;
    private Action release;
    internal Action OnReleased;
    public bool IsDisposed => release == null;
    public bool IsCancelled { get; private set; }
    public bool IsDone => IsDisposed || operation.IsDone;
    public bool Succeeded => !IsDisposed && operation.Succeeded;
    public T Asset => Succeeded ? operation.Asset : null;
    public string Error => IsDisposed ? (IsCancelled ? "Asset request cancelled." : "Asset lease released.") : operation.Error;
    public float Progress => IsDisposed ? 1f : operation.Progress;

    internal SharedAssetLease(ISharedAssetLoadOperation<T> operation, Action assertOwnerThread, Action release)
    {
        this.operation = operation;
        this.assertOwnerThread = assertOwnerThread;
        this.release = release;
    }

    public void Cancel()
    {
        assertOwnerThread();
        if (IsDisposed) return;
        IsCancelled = true;
        Dispose();
    }

    public void Dispose()
    {
        assertOwnerThread();
        Action action = release;
        if (action == null) return;
        release = null;
        try { action(); }
        finally { OnReleased?.Invoke(); OnReleased = null; }
    }
}

/// <summary>Owned by a scene or feature; dispose after all consumers/instances in that scope are destroyed.</summary>
public sealed class SharedAssetLeaseScope : IDisposable
{
    private readonly SharedAssetLeaseService service;
    private readonly HashSet<IDisposable> leases = new();
    private bool disposed;
    internal SharedAssetLeaseScope(SharedAssetLeaseService service) => this.service = service;
    public int LeaseCount => leases.Count;
    public SharedAssetLease<T> Acquire<T>(string address) where T : Object
    {
        service.AssertOwnerThread();
        if (disposed) throw new ObjectDisposedException(nameof(SharedAssetLeaseScope));
        SharedAssetLease<T> lease = service.Acquire<T>(address);
        leases.Add(lease);
        lease.OnReleased = () => leases.Remove(lease);
        return lease;
    }
    public void Dispose()
    {
        service.AssertOwnerThread();
        if (disposed) return;
        disposed = true;
        // Releasing a lease removes it from this set.
        foreach (IDisposable lease in new List<IDisposable>(leases)) lease.Dispose();
        leases.Clear();
    }
}
