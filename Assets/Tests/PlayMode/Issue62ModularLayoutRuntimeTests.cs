using System.Collections;
using System.Linq;
using FPS.SaveGame;
using NUnit.Framework;
using UnityEngine;
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
        public IEnumerator CityNewKeepsAuthoredEnvironmentAndSavesFallbackLayout()
        {
            yield return SceneManager.LoadSceneAsync(CityNewScene, LoadSceneMode.Single);
            yield return WaitUntil(
                () => CityNewModularLayoutBootstrap.IsSceneReady &&
                      RuntimeNavMeshBootstrap.IsSceneReady &&
                      WaveDirector.Active is { IsRunning: true } &&
                      Object.FindAnyObjectByType<CityNewMissionController>()
                          is { Terminal: not null },
                45f,
                "CityNew 原始场景、导航、波次或任务没有按顺序初始化完成。");

            CityNewModularLayoutBootstrap layout =
                CityNewModularLayoutBootstrap.Active;
            Assert.That(layout.IsUsingFallback, Is.True, layout.LastDiagnostic);
            Assert.That(layout.CurrentPlan.Placements, Is.Empty);
            Assert.That(layout.CombatCenters.Count, Is.EqualTo(1));
            Assert.That(layout.EnemySpawnPoints, Is.Empty);
            Assert.That(GameObject.Find("GENERATED COMBAT LAYOUT"), Is.Null);
            Assert.That(layout.LastDiagnostic, Does.Contain("CityNew 原始场景"));

            Transform authoredEnvironment =
                GameObject.Find("Other")?.transform.Find("GameObject");
            Assert.That(authoredEnvironment, Is.Not.Null);
            Assert.That(
                authoredEnvironment.GetComponentsInChildren<Renderer>(true)
                    .Count(renderer => renderer.enabled),
                Is.GreaterThan(100),
                "原始 CityNew 的建筑与环境渲染器不应被隐藏。");
            Assert.That(layout.TerminalObject, Is.Not.Null);
            Assert.That(layout.TerminalObject.name, Is.EqualTo("controlunit"));

            PlayerCombatCompositionRoot player =
                Object.FindAnyObjectByType<PlayerCombatCompositionRoot>();
            player.GetComponent<PlayerController>().SetPaused(true);
            RunSnapshot snapshot = player.GetComponent<RunSnapshotRuntimeAdapter>().Capture();
            Assert.That(snapshot.Layout.UsedFallback, Is.True);
            Assert.That(snapshot.Layout.LayoutId,
                Is.EqualTo("city_new.layout.safe"));
            Assert.That(snapshot.Layout.Modules, Is.Empty);
            Vector3 expectedPlayerPosition = player.transform.position;
            CityNewModularLayoutBootstrap previous = layout;

            RunSnapshotSession.ReloadSnapshot(snapshot);
            yield return WaitUntil(
                () => CityNewModularLayoutBootstrap.Active != null &&
                      CityNewModularLayoutBootstrap.Active != previous &&
                      CityNewModularLayoutBootstrap.IsSceneReady &&
                      !RunSnapshotSession.HasPendingWorldRestore,
                50f,
                "读取存档后没有恢复 CityNew 原始地图与任务状态。");

            CityNewModularLayoutBootstrap restored =
                CityNewModularLayoutBootstrap.Active;
            Assert.That(restored.IsUsingFallback, Is.True);
            Assert.That(restored.CurrentPlan.Placements, Is.Empty);
            Assert.That(GameObject.Find("GENERATED COMBAT LAYOUT"), Is.Null);
            Vector3 restoredPlayerPosition =
                Object.FindAnyObjectByType<PlayerCombatCompositionRoot>()
                    .transform.position;
            Assert.That(Vector3.Distance(
                    restoredPlayerPosition,
                    expectedPlayerPosition), Is.LessThan(0.15f),
                $"saved={expectedPlayerPosition} restored={restoredPlayerPosition}");
        }

        [UnityTest]
        public IEnumerator StartingNewRunKeepsOriginalCityNewAndSynchronizesSeed()
        {
            yield return SceneManager.LoadSceneAsync(
                CityNewScene,
                LoadSceneMode.Single);
            yield return WaitUntil(
                () =>
                {
                    CityNewWaveBootstrap wave =
                        Object.FindAnyObjectByType<CityNewWaveBootstrap>();
                    return CityNewModularLayoutBootstrap.IsSceneReady &&
                           GameObject.FindGameObjectWithTag("Player") != null &&
                           wave != null && wave.Director != null &&
                           wave.Director.IsRunning;
                },
                45f,
                "CityNew 原始场景没有准备完成。");

            CityNewModularLayoutBootstrap previous =
                CityNewModularLayoutBootstrap.Active;
            int previousSeed = GameObject.FindGameObjectWithTag("Player")
                .GetComponent<PlayerUpgradeController>().RunSeed;
            Assert.That(previous.CurrentPlan.RunSeed, Is.EqualTo(previousSeed),
                "测试起点必须是已经完成初始化的原始 CityNew 战局。");
            Assert.That(previous.IsUsingFallback, Is.True);
            Assert.That(GameObject.Find("GENERATED COMBAT LAYOUT"), Is.Null);

            RunSnapshotSession.NewGame();
            int requestedSeed = RunSnapshotSession.PeekSeed(18018);
            Assert.That(requestedSeed, Is.Not.EqualTo(previousSeed),
                "新战局请求必须立即生成不同的随机种子。");
            yield return WaitUntil(
                () =>
                {
                    CityNewWaveBootstrap wave =
                        Object.FindAnyObjectByType<CityNewWaveBootstrap>();
                    return CityNewModularLayoutBootstrap.Active != null &&
                           CityNewModularLayoutBootstrap.Active != previous &&
                           CityNewModularLayoutBootstrap.IsSceneReady &&
                           GameObject.FindGameObjectWithTag("Player") != null &&
                           wave != null && wave.Director != null &&
                           wave.Director.IsRunning;
                },
                50f,
                "开始新战局后没有恢复 CityNew 原始场景。");

            CityNewModularLayoutBootstrap current =
                CityNewModularLayoutBootstrap.Active;
            int currentSeed = GameObject.FindGameObjectWithTag("Player")
                .GetComponent<PlayerUpgradeController>().RunSeed;
            Assert.That(current.CurrentPlan.RunSeed, Is.EqualTo(requestedSeed),
                $"布局没有采用新战局种子；玩家种子={currentSeed}。");
            Assert.That(currentSeed, Is.EqualTo(requestedSeed),
                "玩家系统没有采用新战局种子。");
            Assert.That(current.IsUsingFallback, Is.True);
            Assert.That(current.CurrentPlan.Placements, Is.Empty);
            Assert.That(GameObject.Find("GENERATED COMBAT LAYOUT"), Is.Null,
                "开始新战局也不能重新生成三区域测试地图。");
        }

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
