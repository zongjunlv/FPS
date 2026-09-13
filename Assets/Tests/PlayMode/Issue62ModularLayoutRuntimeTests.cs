using System.Collections;
using System.Linq;
using FPS.Determinism;
using FPS.SaveGame;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace FPS.Tests.PlayMode
{
    public sealed class Issue62ModularLayoutRuntimeTests
    {
        private const string CityNewScene =
            "Assets/ImportPackages/CSAssets2026/Scenes/CityNew.unity";

        [SetUp]
        public void IgnoreHeadlessPresentationWarnings() =>
            LogAssert.ignoreFailingMessages = true;

        [TearDown]
        public void RestoreState()
        {
            LogAssert.ignoreFailingMessages = false;
            Time.timeScale = 1f;
        }

        [UnityTest]
        public IEnumerator GeneratedLayoutIsReachableRecordedAndRestoredExactly()
        {
            yield return SceneManager.LoadSceneAsync(CityNewScene, LoadSceneMode.Single);
            yield return WaitUntil(
                () => CityNewModularLayoutBootstrap.IsSceneReady &&
                      RuntimeNavMeshBootstrap.IsSceneReady &&
                      WaveDirector.Active is { IsRunning: true } &&
                      Object.FindFirstObjectByType<CityNewMissionController>()
                          is { Terminal: not null },
                45f,
                "模块布局、导航、波次或任务没有按顺序初始化完成。");

            CityNewModularLayoutBootstrap layout =
                CityNewModularLayoutBootstrap.Active;
            Assert.That(layout.IsUsingFallback, Is.False, layout.LastDiagnostic);
            Assert.That(layout.CurrentPlan.Placements.Count, Is.EqualTo(6));
            Assert.That(layout.CombatCenters.Count, Is.EqualTo(3));
            Assert.That(layout.EnemySpawnPoints.Count, Is.GreaterThanOrEqualTo(9));
            Assert.That(GameObject.Find("GENERATED COMBAT LAYOUT"), Is.Not.Null);
            Assert.That(layout.LastDiagnostic, Does.Contain("可达性校验"));

            WorldItemPickup[] starterPickups =
                Object.FindObjectsByType<WorldItemPickup>(
                    FindObjectsInactive.Exclude,
                    FindObjectsSortMode.None);
            Assert.That(starterPickups.Length, Is.GreaterThanOrEqualTo(4));
            foreach (WorldItemPickup pickup in starterPickups)
            {
                Assert.That(Vector2.Distance(
                        new Vector2(pickup.transform.position.x,
                            pickup.transform.position.z),
                        new Vector2(layout.PlayerSpawn.x,
                            layout.PlayerSpawn.z)),
                    Is.LessThan(6f),
                    $"初始补给 {pickup.name} 没有随出生模块迁移。");
            }

            Assert.That(NavMesh.SamplePosition(
                layout.PlayerSpawn, out NavMeshHit origin, 3f, NavMesh.AllAreas),
                Is.True);
            foreach (Vector3 point in layout.CombatCenters
                         .Concat(new[] { layout.ExtractionPoint }))
            {
                Assert.That(NavMesh.SamplePosition(
                    point, out NavMeshHit target, 3f, NavMesh.AllAreas), Is.True);
                var path = new NavMeshPath();
                Assert.That(NavMesh.CalculatePath(
                    origin.position, target.position, NavMesh.AllAreas, path), Is.True);
                Assert.That(path.status, Is.EqualTo(NavMeshPathStatus.PathComplete));
            }

            RunDeterminismRecorder recorder = RunDeterminismRecorder.Active;
            Assert.That(recorder.Record.Events.Count(value =>
                value.Type == RunEventType.LayoutGenerated), Is.EqualTo(1));
            RunEvent layoutEvent = recorder.Record.Events.Single(value =>
                value.Type == RunEventType.LayoutGenerated);
            Assert.That(layoutEvent.Tick, Is.Zero);
            Assert.That(layoutEvent.Payload, Does.Contain(layout.CurrentPlan.Fingerprint));

            PlayerCombatCompositionRoot player =
                Object.FindFirstObjectByType<PlayerCombatCompositionRoot>();
            player.GetComponent<PlayerController>().SetPaused(true);
            RunSnapshot snapshot = player.GetComponent<RunSnapshotRuntimeAdapter>().Capture();
            string expectedFingerprint = snapshot.Layout.Fingerprint;
            string expectedLayoutId = snapshot.Layout.LayoutId;
            Vector3 expectedPlayerPosition = player.transform.position;
            CityNewModularLayoutBootstrap previous = layout;

            RunSnapshotSession.ReloadSnapshot(snapshot);
            yield return WaitUntil(
                () => CityNewModularLayoutBootstrap.Active != null &&
                      CityNewModularLayoutBootstrap.Active != previous &&
                      CityNewModularLayoutBootstrap.IsSceneReady &&
                      !RunSnapshotSession.HasPendingWorldRestore,
                50f,
                "读取存档后没有恢复保存的模块布局与任务状态。");

            CityNewModularLayoutBootstrap restored =
                CityNewModularLayoutBootstrap.Active;
            Assert.That(restored.CurrentPlan.LayoutId, Is.EqualTo(expectedLayoutId));
            Assert.That(restored.CurrentPlan.Fingerprint, Is.EqualTo(expectedFingerprint));
            Assert.That(restored.CurrentPlan.Placements.Select(Describe),
                Is.EqualTo(snapshot.Layout.Modules.Select(module =>
                    $"{module.InstanceId}:{module.DefinitionId}:{module.GridX}:{module.GridZ}:{module.QuarterTurns}")));
            Vector3 restoredPlayerPosition =
                Object.FindFirstObjectByType<PlayerCombatCompositionRoot>()
                    .transform.position;
            Assert.That(Vector3.Distance(
                    restoredPlayerPosition,
                    expectedPlayerPosition), Is.LessThan(0.15f),
                $"saved={expectedPlayerPosition} restored={restoredPlayerPosition} " +
                $"moduleSpawn={restored.PlayerSpawn}");
        }

        [UnityTest]
        public IEnumerator StartingNewRunRerollsVisibleLayoutAndSynchronizesSeed()
        {
            yield return SceneManager.LoadSceneAsync(
                CityNewScene,
                LoadSceneMode.Single);
            yield return WaitUntil(
                () =>
                {
                    CityNewWaveBootstrap wave =
                        Object.FindFirstObjectByType<CityNewWaveBootstrap>();
                    return CityNewModularLayoutBootstrap.IsSceneReady &&
                           GameObject.FindGameObjectWithTag("Player") != null &&
                           wave != null && wave.Director != null &&
                           wave.Director.IsRunning;
                },
                45f,
                "初始模块布局没有准备完成。");

            CityNewModularLayoutBootstrap previous =
                CityNewModularLayoutBootstrap.Active;
            string previousLayoutId = previous.CurrentPlan.LayoutId;
            string previousRoute = CombatLayoutReroll.DescribeVisibleRoute(
                previous.CurrentPlan);
            int previousSeed = GameObject.FindGameObjectWithTag("Player")
                .GetComponent<PlayerUpgradeController>().RunSeed;
            Assert.That(previous.CurrentPlan.RunSeed, Is.EqualTo(previousSeed),
                "测试起点必须是已经完成初始化的战局。");

            RunSnapshotSession.NewGame();
            int requestedSeed = RunSnapshotSession.PeekSeed(18018);
            Assert.That(requestedSeed, Is.Not.EqualTo(previousSeed),
                "新战局请求必须立即生成不同的随机种子。");
            yield return WaitUntil(
                () =>
                {
                    CityNewWaveBootstrap wave =
                        Object.FindFirstObjectByType<CityNewWaveBootstrap>();
                    return CityNewModularLayoutBootstrap.Active != null &&
                           CityNewModularLayoutBootstrap.Active != previous &&
                           CityNewModularLayoutBootstrap.IsSceneReady &&
                           GameObject.FindGameObjectWithTag("Player") != null &&
                           wave != null && wave.Director != null &&
                           wave.Director.IsRunning;
                },
                50f,
                "开始新战局后没有生成新的模块布局。");

            CityNewModularLayoutBootstrap current =
                CityNewModularLayoutBootstrap.Active;
            int currentSeed = GameObject.FindGameObjectWithTag("Player")
                .GetComponent<PlayerUpgradeController>().RunSeed;
            Assert.That(current.CurrentPlan.RunSeed, Is.EqualTo(requestedSeed),
                $"布局没有采用新战局种子；玩家种子={currentSeed}。");
            Assert.That(currentSeed, Is.EqualTo(requestedSeed),
                "玩家系统没有采用新战局种子。");
            Assert.That(current.CurrentPlan.LayoutId,
                Is.Not.EqualTo(previousLayoutId),
                "开始新战局必须生成一条可见不同的路线。");
            Assert.That(
                CombatLayoutReroll.DescribeVisibleRoute(current.CurrentPlan),
                Is.Not.EqualTo(previousRoute),
                "开始新战局后模块坐标序列必须发生可见变化。");
        }

        private static string Describe(CombatLayoutPlacement placement) =>
            $"{placement.InstanceId}:{placement.DefinitionId}:{placement.GridX}:{placement.GridZ}:{placement.QuarterTurns}";

        private static IEnumerator WaitUntil(
            System.Func<bool> predicate,
            float seconds,
            string failure)
        {
            float deadline = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < deadline)
            {
                if (predicate()) yield break;
                yield return null;
            }
            Assert.Fail(failure);
        }
    }
}
