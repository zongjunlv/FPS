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
    public sealed class Issue61EncounterRuntimeTests
    {
        private const string CityNewScene =
            "Assets/ImportPackages/CSAssets2026/Scenes/CityNew.unity";

        [SetUp]
        public void IgnoreHeadlessPresentationWarnings()
        {
            LogAssert.ignoreFailingMessages = true;
        }

        [TearDown]
        public void RestoreRuntimeState()
        {
            LogAssert.ignoreFailingMessages = false;
            Time.timeScale = 1f;
        }

        [UnityTest]
        public IEnumerator CityNewStartsVisiblePooledAmbushAndSavesItsClock()
        {
            yield return SceneManager.LoadSceneAsync(
                CityNewScene,
                LoadSceneMode.Single);
            yield return WaitUntil(
                () => WaveDirector.Active != null &&
                      WaveDirector.Active.IsRunning &&
                      Object.FindAnyObjectByType<EncounterRuntimeController>()
                          is { IsConfigured: true },
                35f,
                "等待 CityNew 遭遇系统初始化超时。");

            EncounterRuntimeController encounters =
                Object.FindAnyObjectByType<EncounterRuntimeController>();
            yield return WaitUntil(
                () => encounters.Sequence.HasActiveEncounter &&
                      encounters.CurrentDefinition != null &&
                      encounters.CurrentHud.Visible &&
                      encounters.ActiveEncounterEnemyCount > 0,
                15f,
                "第一波没有触发可见的侧后伏击或敌人没有进入对象池。" );

            Assert.That(encounters.CurrentDefinition.Kind,
                Is.EqualTo(EncounterKind.Ambush));
            Assert.That(encounters.CurrentDefinition.MainFlowPolicy,
                Is.EqualTo(EncounterMainFlowPolicy.Parallel));
            Assert.That(encounters.CurrentHud.Objective, Is.Not.Empty);
            Assert.That(WaveDirector.Active.EncounterEnemies, Is.Not.Empty);

            UnifiedGameHud hud =
                Object.FindAnyObjectByType<UnifiedGameHud>();
            Assert.That(hud.IsEncounterVisible, Is.True);
            Assert.That(hud.EncounterText, Does.Contain("侧后伏击"));

            PlayerCombatCompositionRoot player =
                Object.FindAnyObjectByType<PlayerCombatCompositionRoot>();
            RunSnapshot snapshot = player
                .GetComponent<RunSnapshotRuntimeAdapter>()
                .Capture();
            Assert.That(snapshot.Encounter, Is.Not.Null);
            Assert.That(snapshot.Encounter.CurrentTick,
                Is.GreaterThanOrEqualTo(snapshot.Encounter.StartedTick));
            Assert.That(snapshot.Encounter.ActiveRosterTokens, Is.Not.Empty);
            Assert.That(SnapshotValidation.TryValidate(snapshot, out string error),
                Is.True,
                error);

            RunDeterminismRecorder recorder = RunDeterminismRecorder.Active;
            Assert.That(recorder.Record.Events.Any(value =>
                value.Type == RunEventType.EncounterTransition), Is.True);
        }

        [UnityTest]
        public IEnumerator MidAmbushLoadRestoresProgressClockAndRemainingRoster()
        {
            yield return SceneManager.LoadSceneAsync(
                CityNewScene,
                LoadSceneMode.Single);
            yield return WaitUntil(
                () => WaveDirector.Active != null &&
                      WaveDirector.Active.EncounterEnemies.Count >= 2 &&
                      Object.FindAnyObjectByType<EncounterRuntimeController>()
                          is { IsConfigured: true },
                35f,
                "等待可保存的伏击事件超时。" );

            EncounterRuntimeController previous =
                Object.FindAnyObjectByType<EncounterRuntimeController>();
            PlayerCombatCompositionRoot player =
                Object.FindAnyObjectByType<PlayerCombatCompositionRoot>();
            EnemySpawnHandle victim =
                WaveDirector.Active.EncounterEnemies.Values.First();
            Health victimHealth = victim.Controller.GetComponent<Health>();
            victimHealth.ApplyDamage(new DamageInfo(
                100000f,
                victim.Controller.transform.position,
                Vector3.forward,
                player.gameObject));
            yield return WaitUntil(
                () => previous.CurrentHud.Progress == 1,
                5f,
                "伏击击杀进度没有进入存档状态。" );

            player.GetComponent<PlayerController>().SetPaused(true);
            RunSnapshot snapshot = player
                .GetComponent<RunSnapshotRuntimeAdapter>()
                .Capture();
            int savedProgress = snapshot.Encounter.Progress;
            long savedTick = snapshot.Encounter.CurrentTick;
            int savedRosterCount = snapshot.Encounter.ActiveRosterTokens.Count;
            string savedEncounterId = snapshot.Encounter.ActiveEncounterId;

            RunSnapshotSession.ReloadSnapshot(snapshot);
            yield return WaitUntil(
                () => !RunSnapshotSession.HasPendingWorldRestore &&
                      Object.FindAnyObjectByType<EncounterRuntimeController>()
                          is EncounterRuntimeController current &&
                      current != previous && current.IsConfigured &&
                      current.Sequence.HasActiveEncounter &&
                      current.ActiveEncounterEnemyCount == savedRosterCount,
                40f,
                "读档后伏击事件没有完整恢复。" );

            EncounterRuntimeController restored =
                Object.FindAnyObjectByType<EncounterRuntimeController>();
            EncounterRuntimeRestoreSnapshot restoredState =
                restored.CaptureRuntimeState();
            Assert.That(restored.Sequence.CurrentDefinition.StableId,
                Is.EqualTo(savedEncounterId));
            Assert.That(restored.Sequence.Progress, Is.EqualTo(savedProgress));
            Assert.That(restoredState.CurrentTick,
                Is.GreaterThanOrEqualTo(savedTick));
            Assert.That(restored.CurrentHud.Visible, Is.True);
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
