using System;
using System.Collections;
using System.Reflection;
using FPS.Networking.Domain;
using FPS.Networking.Netcode;
using FPS.Networking.Session;
using NUnit.Framework;
using Unity.Collections;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace FPS.Tests.PlayMode.Issue65
{
    public sealed class CoopVisibleTargetShotViewTickTests
    {
        private GameObject presenterHost;

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            if (presenterHost != null) Object.Destroy(presenterHost);
            yield return null;
            yield return null;
        }

        [UnityTest]
        public IEnumerator VisibleFormalEnemyRayUsesActualInterpolatedAndClampedPoseTicks()
        {
            presenterHost = new GameObject("Visible target shot timestamp presenter");
            var presenter = presenterHost.AddComponent<CoopNetworkWorldPresenter>();
            // Use the explicit PresentForTests interface for each rendered pose,
            // without an unrelated scene authority's Update replacing this buffer.
            presenter.enabled = false;
            NetcodeTargetState state = State();
            presenter.PresentForTests(new[] { state }, 1f / 60f, presentationTick: 10d);
            Assert.That(presenter.TryGetTargetView(1, out GameObject view), Is.True);
            float deadline = Time.realtimeSinceStartup + 15f;
            while (view.GetComponentInChildren<CoopEnemyPresentationDefinition>(true) == null &&
                   Time.realtimeSinceStartup < deadline)
                yield return null;
            presenter.PresentForTests(new[] { state }, 1f / 60f, presentationTick: 10d);
            var model = view.GetComponentInChildren<CoopEnemyPresentationDefinition>(true);
            Assert.That(model, Is.Not.Null, "必须加载正式模型，不能使用测试 primitive 代替。");
            Assert.That(model.SourceAddress, Is.EqualTo("enemy/trilobite-assault"));
            Assert.That(model.GetComponentInChildren<Renderer>(true), Is.Not.Null);
            Assert.That(model.BodyHalfExtents.sqrMagnitude, Is.GreaterThan(0f));

            PropertyInfo sampledTick = typeof(CoopNetworkWorldPresenter).GetProperty(
                "LastResolvedShotViewTick", BindingFlags.Instance | BindingFlags.Public);
            state.Position += Vector3.right * 2f;
            state.SnapshotTick = 20;
            presenter.PresentForTests(new[] { state }, 1f / 60f, presentationTick: 15d);
            Assert.That(view.transform.position.x, Is.EqualTo(1f).Within(0.0001f),
                "前置条件：真实buffer在tick10和20之间展示tick15姿态。");
            AssertVisibleCalibratedBodyRayHits(presenter, view, model);
            object interpolatedTick = sampledTick?.GetValue(presenter);

            presenter.PresentForTests(new[] { state }, 1f / 60f, presentationTick: 100d);
            Assert.That(view.transform.position.x, Is.EqualTo(2f).Within(0.0001f),
                "前置条件：clock cursor到100，但真实buffer已经clamp在最后的tick20姿态。");
            AssertVisibleCalibratedBodyRayHits(presenter, view, model);
            object clampedTick = sampledTick?.GetValue(presenter);

            Assert.That(sampledTick, Is.Not.Null,
                "可见目标命中必须暴露实际sample tick，不能让射击请求直接采用越过缓冲的clock cursor。");
            Assert.That(Convert.ToDouble(interpolatedTick), Is.EqualTo(15d));
            Assert.That(Convert.ToDouble(clampedTick), Is.EqualTo(20d),
                "看到的模型停在tick20时必须回溯tick20，而不是上传cursor100。");
            presenter.ResetShotViewSample();
            Assert.That(presenter.LastResolvedShotViewTick, Is.EqualTo(-1d),
                "下一枪未调用模型解析器时，不能继承上一枪的可见目标时间。");
        }

        private static void AssertVisibleCalibratedBodyRayHits(CoopNetworkWorldPresenter presenter,
            GameObject view, CoopEnemyPresentationDefinition model)
        {
            Assert.That(view.activeInHierarchy, Is.True);
            Assert.That(model.GetComponentInChildren<Renderer>().gameObject.activeInHierarchy, Is.True);
            Vector3 center = view.transform.TransformPoint(model.BodyCenter);
            var ray = new Ray(center - Vector3.forward * (model.BodyHalfExtents.z + 2f), Vector3.forward);
            Vector3? hit = presenter.RaycastVisibleTarget(ray, 10f);
            Assert.That(hit.HasValue, Is.True,
                "射线必须真正命中当前可见正式模型的校准BodyCenter，不能只测反射属性或假状态。");
        }

        private static NetcodeTargetState State() => new()
        {
            TargetId = 1,
            Position = Vector3.forward * 8f,
            Radius = 0.8f,
            Health = 100f,
            MaximumHealth = 100f,
            Active = true,
            SpawnGeneration = 1,
            RunGeneration = 1,
            SnapshotTick = 10,
            Role = AuthoritativeEnemyRole.Assault,
            Behavior = AuthoritativeEnemyBehavior.Pursue,
            ArchetypeId = new FixedString64Bytes("enemy.archetype.spider_assault"),
            PresentationAddress = new FixedString64Bytes("enemy/trilobite-assault")
        };
    }
}
