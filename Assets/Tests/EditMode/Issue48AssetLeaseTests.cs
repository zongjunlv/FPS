using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

public sealed class Issue48AssetLeaseTests
{
    private sealed class Pending<T> : ISharedAssetLoadOperation<T> where T : Object
    {
        public bool IsDone { get; set; }
        public bool Succeeded { get; set; }
        public T Asset { get; set; }
        public string Error { get; set; }
        public float Progress => IsDone ? 1f : 0.5f;
        public int Releases;
        public void Dispose() => Releases++;
    }

    private sealed class Backend : ISharedAssetLoadBackend
    {
        public int Loads;
        public bool Throw;
        public readonly List<object> Operations = new();
        public ISharedAssetLoadOperation<T> Load<T>(string address) where T : Object
        {
            if (Throw) throw new InvalidOperationException("Simulated backend exception.");
            Loads++;
            var operation = new Pending<T>();
            Operations.Add(operation);
            return operation;
        }
    }

    [Test]
    public void ConcurrentRequestsShareOneHandleAndReleaseOnlyAfterLastLease()
    {
        var backend = new Backend();
        var service = new SharedAssetLeaseService(backend);
        var first = service.Acquire<GameObject>("enemy");
        var second = service.Acquire<GameObject>("enemy");
        var operation = (Pending<GameObject>)backend.Operations[0];
        Assert.That(backend.Loads, Is.EqualTo(1));
        Assert.That(service.ActiveHandleCount, Is.EqualTo(1));
        Assert.That(service.GetReferenceCount<GameObject>("enemy"), Is.EqualTo(2));
        first.Dispose();
        Assert.That(operation.Releases, Is.Zero);
        second.Dispose();
        second.Dispose();
        Assert.That(operation.Releases, Is.EqualTo(1));
        Assert.That(service.ReferenceCount, Is.Zero);
        Assert.That(service.ActiveHandleCount, Is.Zero);
    }

    [Test]
    public void CancellingOnePendingConsumerDoesNotCancelTheOther()
    {
        var backend = new Backend();
        var service = new SharedAssetLeaseService(backend);
        using var first = service.Acquire<GameObject>("enemy");
        using var second = service.Acquire<GameObject>("enemy");
        var operation = (Pending<GameObject>)backend.Operations[0];
        first.Cancel();
        first.Cancel();
        Assert.That(first.IsDone && first.IsCancelled, Is.True);
        Assert.That(first.Succeeded, Is.False);
        Assert.That(second.IsDone, Is.False);
        Assert.That(operation.Releases, Is.Zero);
        operation.IsDone = operation.Succeeded = true;
        Assert.That(second.Succeeded, Is.True);
        Assert.That(service.ReferenceCount, Is.EqualTo(1));
    }

    [Test]
    public void LastPendingCancellationReleasesHandleAndLateCompletionCannotResurrectLease()
    {
        var backend = new Backend();
        var service = new SharedAssetLeaseService(backend);
        var lease = service.Acquire<GameObject>("enemy");
        var operation = (Pending<GameObject>)backend.Operations[0];
        lease.Cancel();
        operation.IsDone = operation.Succeeded = true;
        Assert.That(operation.Releases, Is.EqualTo(1));
        Assert.That(lease.Succeeded, Is.False);
        Assert.That(lease.Asset, Is.Null);
        Assert.That(service.ActiveHandleCount, Is.Zero);
        using var retry = service.Acquire<GameObject>("enemy");
        Assert.That(backend.Loads, Is.EqualTo(2));
    }

    [Test]
    public void SceneScopeDisposalDoesNotReleaseOtherScenesSharedResource()
    {
        var backend = new Backend();
        var service = new SharedAssetLeaseService(backend);
        using var sceneA = service.CreateScope();
        using var sceneB = service.CreateScope();
        var first = sceneA.Acquire<Sprite>("icon");
        var second = sceneB.Acquire<Sprite>("icon");
        var operation = (Pending<Sprite>)backend.Operations[0];
        sceneA.Dispose();
        sceneA.Dispose();
        Assert.That(first.IsDisposed, Is.True);
        Assert.That(second.IsDisposed, Is.False);
        Assert.That(operation.Releases, Is.Zero);
        Assert.That(sceneA.LeaseCount, Is.Zero);
        Assert.Throws<ObjectDisposedException>(() => sceneA.Acquire<Sprite>("icon"));
        second.Cancel();
        Assert.That(sceneB.LeaseCount, Is.Zero);
        Assert.That(operation.Releases, Is.EqualTo(1));
    }

    [Test]
    public void TypesAndAddressesHaveSeparateOwnership()
    {
        var backend = new Backend();
        var service = new SharedAssetLeaseService(backend);
        using var first = service.Acquire<GameObject>("shared");
        using var second = service.Acquire<Sprite>("shared");
        using var third = service.Acquire<GameObject>("other");
        Assert.That(backend.Loads, Is.EqualTo(3));
        Assert.That(service.ReferenceCount, Is.EqualTo(3));
    }

    [Test]
    public void FailedLoadReleasesAndCanBeRetriedAfterConsumersFinish()
    {
        var backend = new Backend();
        var service = new SharedAssetLeaseService(backend);
        var lease = service.Acquire<GameObject>("missing");
        var operation = (Pending<GameObject>)backend.Operations[0];
        operation.IsDone = true;
        operation.Error = "Not found";
        Assert.That(lease.IsDone, Is.True);
        Assert.That(lease.Succeeded, Is.False);
        Assert.That(lease.Error, Is.EqualTo("Not found"));
        lease.Dispose();
        using var retry = service.Acquire<GameObject>("missing");
        Assert.That(backend.Loads, Is.EqualTo(2));
        Assert.That(operation.Releases, Is.EqualTo(1));
    }

    [Test]
    public void InvalidAddressAndBackendExceptionDoNotCreatePhantomReferences()
    {
        var backend = new Backend { Throw = true };
        var service = new SharedAssetLeaseService(backend);
        Assert.Throws<ArgumentException>(() => service.Acquire<GameObject>(" "));
        Assert.Throws<InvalidOperationException>(() => service.Acquire<GameObject>("enemy"));
        Assert.That(service.ReferenceCount, Is.Zero);
        Assert.That(service.ActiveHandleCount, Is.Zero);
    }

    [Test]
    public void RepeatedSceneEntryAndExitReturnsDiagnosticsToBaseline()
    {
        var backend = new Backend();
        var service = new SharedAssetLeaseService(backend);
        for (int index = 0; index < 30; index++)
        {
            using (var scene = service.CreateScope())
            {
                scene.Acquire<GameObject>("enemy");
                scene.Acquire<GameObject>("enemy");
                scene.Acquire<Sprite>("icon");
            }
            Assert.That(service.ActiveHandleCount, Is.Zero);
            Assert.That(service.ReferenceCount, Is.Zero);
        }
        foreach (object operation in backend.Operations)
        {
            int releases = operation is Pending<GameObject> prefab ? prefab.Releases : ((Pending<Sprite>)operation).Releases;
            Assert.That(releases, Is.EqualTo(1));
        }
    }
}
