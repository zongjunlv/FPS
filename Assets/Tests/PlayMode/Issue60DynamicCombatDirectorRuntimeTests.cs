using System.Collections;
using System.Linq;
using FPS.Determinism;
using FPS.SaveGame;
using FPS.Simulation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace FPS.Tests.PlayMode
{
    public sealed class Issue60DynamicCombatDirectorRuntimeTests
    {
        private const string CityNewScene =
            "Assets/ImportPackages/CSAssets2026/Scenes/CityNew.unity";

        [SetUp]
        public void IgnoreHeadlessPresentationWarnings()
        {
            LogAssert.ignoreFailingMessages = true;
        }

        [TearDown]
        public void RestoreLogState()
        {
            LogAssert.ignoreFailingMessages = false;
            Time.timeScale = 1f;
        }

        [UnityTest]
        public IEnumerator CityNewWarnsThenSpawnsReachableBoundedRearFlank()
        {
            yield return SceneManager.LoadSceneAsync(
                CityNewScene,
                LoadSceneMode.Single);
            yield return WaitReady();

            WaveDirector director = WaveDirector.Active;
            PlayerCombatCompositionRoot player =
                Object.FindFirstObjectByType<PlayerCombatCompositionRoot>();
            UnifiedGameHud hud = Object.FindFirstObjectByType<UnifiedGameHud>();
            Vector3 directedPosition = default;
            bool capturedDirectedSpawn = false;
            director.EnemySpawned += spawned =>
            {
                if (director.CombatDirectorPhase !=
                    CombatDirectorPhase.Deploying) return;
                capturedDirectedSpawn = true;
                directedPosition = spawned.Request.Position;
            };

            yield return WaitUntil(
                () => director.CombatDirectorPhase ==
                      CombatDirectorPhase.Warning,
                6f,
                "动态导演未进入预警阶段。");
            CombatDirectorRuntimeDiagnostics warning =
                director.CaptureCombatDiagnostics().CombatDirector;
            Assert.That(warning, Is.Not.Null);
            Assert.That(warning.SelectedScore, Is.GreaterThanOrEqualTo(0.55f));
            Assert.That(warning.RequestedCount, Is.InRange(1, 2));
            Assert.That(warning.DirectedSpawnReady, Is.True);
            Assert.That(hud.CombatWarningText, Does.Contain("战术警报"));

            yield return WaitUntil(
                () => capturedDirectedSpawn,
                6f,
                "预警结束后没有执行侧后方增援。" );
            Vector3 offset = directedPosition - player.transform.position;
            offset.y = 0f;
            float angle = Mathf.Abs(Vector3.SignedAngle(
                player.transform.forward,
                offset,
                Vector3.up));
            Assert.That(angle, Is.GreaterThanOrEqualTo(85f));
            Assert.That(offset.magnitude, Is.GreaterThanOrEqualTo(9f));
            Assert.That(director.CurrentProgress.AliveCount,
                Is.LessThanOrEqualTo(
                    director.CaptureRuntimeState().Flow.CurrentWaveState
                        .MaximumAliveCount));

            RunDeterminismRecorder recorder = RunDeterminismRecorder.Active;
            Assert.That(recorder, Is.Not.Null);
            Assert.That(recorder.Record.Events.Any(value =>
                value.Type == RunEventType.CombatDirectorDecision), Is.True);

            PlayerController controller = player.GetComponent<PlayerController>();
            controller.SetPaused(true);
            RunSnapshotRuntimeAdapter adapter =
                player.GetComponent<RunSnapshotRuntimeAdapter>();
            RunSnapshot snapshot = adapter.Capture();
            Assert.That(snapshot.CombatDirector, Is.Not.Null);
            Assert.That(SnapshotValidation.TryValidate(snapshot, out string error),
                Is.True,
                error);
            string json = RunSnapshotCodec.Serialize(snapshot);
            Assert.That(RunSnapshotCodec.TryDeserialize(
                    json,
                    out RunSnapshot restored,
                    out error),
                Is.True,
                error);
            Assert.That(restored.CombatDirector.Phase,
                Is.EqualTo(snapshot.CombatDirector.Phase));
            Assert.That(restored.CombatDirector.RandomState,
                Is.EqualTo(snapshot.CombatDirector.RandomState));
            Assert.That(restored.CombatDirector.CurrentEvent.EventId,
                Is.EqualTo(snapshot.CombatDirector.CurrentEvent.EventId));

            restored.CombatDirector.Phase =
                (int)CombatDirectorPhase.Warning;
            restored.CombatDirector.CurrentEvent = null;
            Assert.That(SnapshotValidation.TryValidate(restored, out error),
                Is.False);
            Assert.That(error, Does.Contain("导演"));
        }

        private static IEnumerator WaitReady()
        {
            yield return WaitUntil(
                () => WaveDirector.Active != null &&
                      WaveDirector.Active.IsRunning &&
                      WaveDirector.Active.Simulation != null &&
                      Object.FindFirstObjectByType<PlayerCombatCompositionRoot>() != null &&
                      Object.FindFirstObjectByType<UnifiedGameHud>() != null,
                35f,
                "等待 CityNew 战局初始化超时。");
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
