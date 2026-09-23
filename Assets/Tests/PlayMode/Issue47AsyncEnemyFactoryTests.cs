using System;
using System.Collections;
using System.Collections.Generic;
using FPS.Core.GameModes;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace FPS.Tests.PlayMode
{
    public sealed class Issue47AsyncEnemyFactoryTests
    {
        private readonly List<Object> ownedAssets = new();
        private Scene testScene;
        private Scene previousScene;
        private AddressableEnemyFactory factory;

        [SetUp]
        public void SetUp()
        {
            previousScene = SceneManager.GetActiveScene();
            GameModeFlowController.ResetRuntimeForTests();
            foreach (CityNewWaveBootstrap bootstrap in Object
                .FindObjectsByType<CityNewWaveBootstrap>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None))
            {
                Object.DestroyImmediate(bootstrap);
            }
            testScene = SceneManager.CreateScene("Issue47 Factory Test");
            SceneManager.SetActiveScene(testScene);
            factory = new GameObject("Async Factory").AddComponent<AddressableEnemyFactory>();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (factory != null) factory.DisposeFactory();
            foreach (Object asset in ownedAssets) Object.Destroy(asset);
            ownedAssets.Clear();
            if (previousScene.IsValid() && previousScene.isLoaded)
                SceneManager.SetActiveScene(previousScene);
            if (testScene.IsValid() && testScene.isLoaded)
                yield return SceneManager.UnloadSceneAsync(testScene);
        }

        [Test]
        public void LoadingRejectsSpawnsAndReportsProgress()
        {
            var operation = new FakeOperation { Progress = 0.4f };
            var loader = new FakeLoader(operation);
            EnemyArchetypeDefinition archetype = Archetype("enemy/test");
            factory.Configure(new[] { archetype }, 2, 4, loader);
            IEnumerator preparation = factory.PrepareAsync();

            Assert.That(preparation.MoveNext(), Is.True);
            Assert.That(factory.PreparationState, Is.EqualTo(EnemyFactoryPreparationState.Loading));
            Assert.That(factory.PreparationProgress, Is.EqualTo(0.2f).Within(0.001f));
            Assert.That(factory.TrySpawn(new EnemySpawnRequest(1,
                    new WaveEnemyEntry(archetype), Vector3.zero, Quaternion.identity, null),
                null, out EnemySpawnHandle handle), Is.False);
            Assert.That(handle.IsValid, Is.False);
            Assert.That(factory.Pool.PooledObjectCount, Is.Zero);
        }

        [Test]
        public void WaveDirectorRefusesToStartBeforeFactoryIsReady()
        {
            var operation = new FakeOperation();
            EnemyArchetypeDefinition archetype = Archetype("enemy/test");
            factory.Configure(new[] { archetype }, 2, 4, new FakeLoader(operation));
            var wave = ScriptableObject.CreateInstance<WaveDefinition>();
            ownedAssets.Add(wave);
            wave.Configure(2, 1, 0.1f, new[] { new WaveEnemyEntry(archetype) });
            var director = new GameObject("Wave Director").AddComponent<WaveDirector>();
            var resolver = director.gameObject.AddComponent<NavMeshEnemySpawnPointResolver>();
            director.Configure(wave, factory, resolver, new GameObject("Player Target").transform);

            Assert.That(director.StartRun(), Is.False, "未开始加载时也不能提前推进波次。");
            IEnumerator preparation = factory.PrepareAsync();
            Assert.That(preparation.MoveNext(), Is.True);
            Assert.That(director.StartRun(), Is.False, "加载未完成时不能开始波次。");
            operation.IsDone = true;
            operation.Error = "missing template";
            Assert.That(preparation.MoveNext(), Is.False);
            Assert.That(director.StartRun(), Is.False, "资源失败不能被当作准备完成。");
            Assert.That(director.IsRunning, Is.False);
            Assert.That(director.SpawnAttemptCount, Is.Zero);
            Assert.That(director.WaveStartedEventCount, Is.Zero);
        }

        [Test]
        public void FailedLoadStopsPreparationAndReleasesItsOperation()
        {
            var operation = new FakeOperation { IsDone = true, Error = "asset unavailable" };
            factory.Configure(new[] { Archetype("enemy/missing") }, 2, 4, new FakeLoader(operation));

            Assert.That(factory.PrepareAsync().MoveNext(), Is.False);
            Assert.That(factory.PreparationState, Is.EqualTo(EnemyFactoryPreparationState.Failed));
            Assert.That(factory.PreparationError, Does.Contain("enemy/missing").And.Contain("asset unavailable"));
            Assert.That(factory.LoadedTemplateCount, Is.Zero);
            Assert.That(factory.Pool.PooledObjectCount, Is.Zero);
            Assert.That(operation.DisposeCount, Is.EqualTo(1));
            factory.DisposeFactory();
            Assert.That(operation.DisposeCount, Is.EqualTo(1));
        }

        [Test]
        public void LoaderExceptionBecomesAnActionableFailure()
        {
            factory.Configure(new[] { Archetype("enemy/test") }, 2, 4,
                new FakeLoader { LoadException = new InvalidOperationException("catalog unavailable") });

            Assert.That(factory.PrepareAsync().MoveNext(), Is.False);
            Assert.That(factory.PreparationState, Is.EqualTo(EnemyFactoryPreparationState.Failed));
            Assert.That(factory.PreparationError, Does.Contain("catalog unavailable"));
            Assert.That(factory.Pool.PooledObjectCount, Is.Zero);
        }

        [Test]
        public void ExitWhileLoadingReleasesOnceAndCannotBecomeReadyLater()
        {
            var operation = new FakeOperation();
            var loader = new FakeLoader(operation);
            factory.Configure(new[] { Archetype("enemy/test") }, 2, 4, loader);
            IEnumerator preparation = factory.PrepareAsync();
            Assert.That(preparation.MoveNext(), Is.True);

            factory.DisposeFactory();
            operation.IsDone = true;
            operation.Succeeded = true;
            Assert.That(preparation.MoveNext(), Is.False);
            factory.DisposeFactory();
            Assert.That(factory.PreparationState, Is.EqualTo(EnemyFactoryPreparationState.Disposed));
            Assert.That(operation.DisposeCount, Is.EqualTo(1));
            Assert.That(factory.Pool.PooledObjectCount, Is.Zero);
            Assert.That(factory.LoadedTemplateCount, Is.Zero);
            Assert.That(loader.Addresses.Count, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator SharedAddressLoadsOnceAndPrewarmsAcrossFramesWithoutMutatingTemplate()
        {
            GameObject template = Template();
            var operation = new FakeOperation { IsDone = true, Succeeded = true, Template = template };
            var loader = new FakeLoader(operation);
            factory.Configure(new[] { Archetype("enemy/shared"), Archetype("enemy/shared") }, 3, 6, loader);
            IEnumerator preparation = factory.PrepareAsync();

            for (int count = 1; count <= 3; count++)
            {
                Assert.That(preparation.MoveNext(), Is.True);
                Assert.That(factory.PreparationState, Is.EqualTo(EnemyFactoryPreparationState.Prewarming));
                Assert.That(factory.Pool.PooledObjectCount, Is.EqualTo(count),
                    "每次恢复协程只应预热一个对象，避免单帧集中实例化。");
                yield return null;
            }

            Assert.That(preparation.MoveNext(), Is.False);
            Assert.That(factory.PreparationState, Is.EqualTo(EnemyFactoryPreparationState.Ready));
            Assert.That(factory.PreparationProgress, Is.EqualTo(1f));
            Assert.That(factory.LoadedTemplateCount, Is.EqualTo(1));
            Assert.That(loader.Addresses, Is.EqualTo(new[] { "enemy/shared" }));
            Assert.That(factory.Pool.AvailableCount, Is.EqualTo(3));
            Assert.That(factory.Pool.ExpansionCount, Is.Zero);
            Assert.That(template.activeSelf, Is.False);
            Assert.That(template.transform.parent, Is.Null);
            Assert.That(template.GetComponent<EnemyController>().PoolPreparationCount, Is.Zero);
            Assert.That(factory.PrepareAsync().MoveNext(), Is.False);
            Assert.That(loader.Addresses.Count, Is.EqualTo(1), "重复准备不能重新加载资源或扩容。");
            factory.DisposeFactory();
            Assert.That(operation.DisposeCount, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator ExitDuringPrewarmClearsPartialPool()
        {
            var operation = new FakeOperation { IsDone = true, Succeeded = true, Template = Template() };
            factory.Configure(new[] { Archetype("enemy/test") }, 3, 6, new FakeLoader(operation));
            IEnumerator preparation = factory.PrepareAsync();
            Assert.That(preparation.MoveNext(), Is.True);
            Assert.That(factory.Pool.PooledObjectCount, Is.EqualTo(1));

            factory.DisposeFactory();
            Assert.That(preparation.MoveNext(), Is.False);
            yield return null;
            Assert.That(factory.PreparationState, Is.EqualTo(EnemyFactoryPreparationState.Disposed));
            Assert.That(factory.Pool.PooledObjectCount, Is.Zero);
            Assert.That(factory.Pool.AvailableCount, Is.Zero);
            Assert.That(factory.Pool.GetComponentsInChildren<EnemyController>(true), Is.Empty);
            Assert.That(operation.DisposeCount, Is.EqualTo(1));
        }

        private EnemyArchetypeDefinition Archetype(string address)
        {
            var archetype = ScriptableObject.CreateInstance<EnemyArchetypeDefinition>();
            archetype.ConfigureTemplateAddress(address);
            ownedAssets.Add(archetype);
            return archetype;
        }

        private static GameObject Template()
        {
            var template = new GameObject("Fake Enemy Template");
            template.SetActive(false);
            template.AddComponent<EnemyController>();
            return template;
        }

        private sealed class FakeLoader : IEnemyTemplateLoader
        {
            private readonly IEnemyTemplateLoadOperation operation;
            public readonly List<string> Addresses = new();
            public Exception LoadException;

            public FakeLoader(IEnemyTemplateLoadOperation result = null) => operation = result;

            public IEnemyTemplateLoadOperation Load(string address)
            {
                Addresses.Add(address);
                if (LoadException != null) throw LoadException;
                return operation;
            }
        }

        private sealed class FakeOperation : IEnemyTemplateLoadOperation
        {
            public bool IsDone { get; set; }
            public bool Succeeded { get; set; }
            public float Progress { get; set; }
            public GameObject Template { get; set; }
            public string Error { get; set; }
            public int DisposeCount { get; private set; }
            public void Dispose() => DisposeCount++;
        }
    }
}
