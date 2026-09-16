using System.Collections;
using FPS.Core.GameModes;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace FPS.Tests.PlayMode
{
    public sealed class Issue79TutorialCompletionTests
    {
        [UnityTest]
        public IEnumerator CompletionShowsThreeActionsAndLocksGameplay()
        {
            yield return EnterTutorial();
            TutorialFlowController flow = FindFlow();

            CompleteSequence(flow);
            yield return null;

            Assert.That(flow.Progression.IsComplete, Is.True);
            Assert.That(flow.CompletionView, Is.Not.Null);
            Assert.That(flow.CompletionView.IsVisible, Is.True);
            Assert.That(flow.CompletionView.IsBound, Is.True);
            Assert.That(flow.CompletionView.EnterBattleButton, Is.Not.Null);
            Assert.That(flow.CompletionView.RestartButton, Is.Not.Null);
            Assert.That(flow.CompletionView.ReturnButton, Is.Not.Null);
            Assert.That(flow.Environment.PlayerRig.Player
                .GameplayInputEnabled, Is.False);
            Assert.That(flow.Environment.PlayerRig.Combat
                .GameplayInputEnabled, Is.False);
            Assert.That(Cursor.lockState, Is.EqualTo(CursorLockMode.None));
            if (!Application.isBatchMode)
            {
                Assert.That(Cursor.visible, Is.True);
            }
            Assert.That(Object.FindObjectsByType<EnemyController>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None), Is.Empty);
        }

        [UnityTest]
        public IEnumerator RestartRebuildsCleanTutorialRuntimeState()
        {
            yield return EnterTutorial();
            TutorialFlowController oldFlow = FindFlow();
            PlayerGameplayRig oldRig = oldFlow.Environment.PlayerRig;
            int weaponCount = oldRig.Weapons.Count;
            int[] initialMagazine = new int[weaponCount];
            int[] initialReserve = new int[weaponCount];
            for (int index = 0; index < weaponCount; index++)
            {
                initialMagazine[index] = oldRig.Weapons[index].CurrentAmmo;
                initialReserve[index] = oldRig.Weapons[index].ReserveAmmo;
                Assert.That(oldRig.Weapons[index].TryRestoreAmmo(1, 0),
                    Is.True);
            }

            CombatEffectPool oldEffects =
                oldRig.GetComponent<CombatEffectPool>();
            Assert.That(oldEffects, Is.Not.Null);
            oldEffects.PresentImpact(new ShotResult(
                true,
                oldFlow.Environment.ShootingWall.bounds.center,
                Vector3.back,
                SurfaceType.Concrete,
                DamageResult.None,
                null,
                oldFlow.Environment.ShootingWall.gameObject));
            Assert.That(oldEffects.ConcreteActiveCount, Is.GreaterThan(0));

            TutorialDamageTrainingTarget oldTarget =
                Object.FindAnyObjectByType<TutorialDamageTrainingTarget>();
            AdvanceUntil(oldFlow, TutorialEvidenceType.BodyHit);
            Assert.That(oldTarget.IsTrainingActive, Is.True);
            Assert.That(oldTarget.TargetHealth.TryRestoreSnapshotVitals(
                100f,
                0f), Is.True);
            oldFlow.ReportEvidence(TutorialEvidenceType.BodyHit);
            oldFlow.ReportEvidence(TutorialEvidenceType.HeadHit);
            yield return null;
            Assert.That(oldFlow.Progression.IsComplete, Is.True);
            Assert.That(oldTarget.TargetHealth.CurrentHealth,
                Is.LessThan(oldTarget.TargetHealth.MaxHealth));

            oldFlow.CompletionView.RestartButton.onClick.Invoke();
            yield return WaitForTutorial(oldFlow);

            TutorialFlowController newFlow = FindFlow();
            PlayerGameplayRig newRig = newFlow.Environment.PlayerRig;
            TutorialDamageTrainingTarget newTarget =
                Object.FindAnyObjectByType<TutorialDamageTrainingTarget>();
            Assert.That(newFlow, Is.Not.SameAs(oldFlow));
            Assert.That(newFlow.Progression.CurrentStepIndex, Is.Zero);
            Assert.That(newFlow.Progression.CurrentValue, Is.Zero);
            Assert.That(newFlow.Progression.IsComplete, Is.False);
            Assert.That(newFlow.CompletionView.IsVisible, Is.False);
            Assert.That(newTarget.IsTrainingActive, Is.False);
            Assert.That(newTarget.TargetHealth.CurrentHealth,
                Is.EqualTo(newTarget.TargetHealth.MaxHealth));
            Assert.That(newRig.TracerPool.ActiveCount, Is.Zero);
            Assert.That(newRig.GetComponent<CombatEffectPool>()
                .ConcreteActiveCount, Is.Zero);
            Assert.That(newRig.Weapons.Count, Is.EqualTo(weaponCount));
            for (int index = 0; index < weaponCount; index++)
            {
                Assert.That(newRig.Weapons[index].CurrentAmmo,
                    Is.EqualTo(initialMagazine[index]));
                Assert.That(newRig.Weapons[index].ReserveAmmo,
                    Is.EqualTo(initialReserve[index]));
            }

            Assert.That(Object.FindObjectsByType<EnemyController>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None), Is.Empty);
        }

        [UnityTest]
        public IEnumerator CompletionCanEnterBattlePreparationOrReturnToEntry()
        {
            yield return EnterTutorial();
            TutorialFlowController flow = FindFlow();
            CompleteSequence(flow);
            yield return null;
            flow.CompletionView.EnterBattleButton.onClick.Invoke();
            yield return WaitForScene(GameModeScenePaths.BattlePreparation);

            Assert.That(GameModeContext.IsActive(
                GameModeId.SoloBattle,
                GameModeStage.BattlePreparation), Is.True);
            Assert.That(Object.FindAnyObjectByType<TutorialFlowController>(),
                Is.Null);
            Assert.That(Object.FindAnyObjectByType<TutorialTopHud>(), Is.Null);
            Assert.That(Object.FindAnyObjectByType<TutorialCompletionView>(),
                Is.Null);
            Assert.That(Object.FindAnyObjectByType<ModeDestinationView>(),
                Is.Not.Null);

            yield return EnterTutorial();
            flow = FindFlow();
            CompleteSequence(flow);
            yield return null;
            flow.CompletionView.ReturnButton.onClick.Invoke();
            yield return WaitForScene(GameModeScenePaths.Entry);

            Assert.That(GameModeContext.IsActive(
                GameModeId.None,
                GameModeStage.Entry), Is.True);
            Assert.That(Object.FindAnyObjectByType<TutorialFlowController>(),
                Is.Null);
            Assert.That(Object.FindAnyObjectByType<TutorialCompletionView>(),
                Is.Null);
            Assert.That(Object.FindAnyObjectByType<ModeEntryView>(),
                Is.Not.Null);
        }

        [UnityTest]
        public IEnumerator BattleRouteKeepsCityNewCoreSystemsAndNoTutorialState()
        {
            yield return EnterTutorial();
            TutorialFlowController flow = FindFlow();
            CompleteSequence(flow);
            yield return null;
            flow.CompletionView.EnterBattleButton.onClick.Invoke();
            yield return WaitForScene(GameModeScenePaths.BattlePreparation);

            ModeDestinationView preparation =
                Object.FindAnyObjectByType<ModeDestinationView>();
            Assert.That(preparation.PrimaryActionButton, Is.Not.Null);
            preparation.PrimaryActionButton.onClick.Invoke();

            float deadline = Time.realtimeSinceStartup + 35f;
            PlayerGameplayRig rig = null;
            CityNewWaveBootstrap wave = null;
            while (Time.realtimeSinceStartup < deadline)
            {
                rig = Object.FindAnyObjectByType<PlayerGameplayRig>();
                wave = Object.FindAnyObjectByType<CityNewWaveBootstrap>();
                if (SceneManager.GetActiveScene().path ==
                        GameModeScenePaths.CityNew &&
                    !GameModeContext.IsTransitioning &&
                    rig != null && wave?.Director != null &&
                    wave.Director.IsRunning &&
                    rig.GetComponent<CityNewMissionController>() != null &&
                    rig.GetComponent<PlayerInventoryController>()?.Inventory !=
                        null &&
                    rig.GetComponent<PlayerRunProgression>() != null &&
                    Object.FindAnyObjectByType<UnifiedGameHud>() != null)
                {
                    break;
                }

                yield return null;
            }

            Assert.That(SceneManager.GetActiveScene().path,
                Is.EqualTo(GameModeScenePaths.CityNew));
            Assert.That(GameModeContext.IsActive(
                GameModeId.SoloBattle,
                GameModeStage.Battle), Is.True);
            Assert.That(wave?.Director?.IsRunning, Is.True);
            Assert.That(rig.GetComponent<CityNewMissionController>(),
                Is.Not.Null);
            Assert.That(rig.GetComponent<PlayerInventoryController>()
                .Inventory, Is.Not.Null);
            Assert.That(rig.GetComponent<PlayerRunProgression>(), Is.Not.Null);
            Assert.That(Object.FindAnyObjectByType<UnifiedGameHud>(),
                Is.Not.Null);
            Assert.That(Object.FindAnyObjectByType<TutorialFlowController>(),
                Is.Null);
            Assert.That(Object.FindAnyObjectByType<TutorialTrainingEnvironment>(),
                Is.Null);
            Assert.That(Object.FindAnyObjectByType<TutorialDamageTrainingTarget>(),
                Is.Null);
            Assert.That(Object.FindAnyObjectByType<TutorialTopHud>(), Is.Null);
            Assert.That(Object.FindAnyObjectByType<TutorialCompletionView>(),
                Is.Null);
        }

        private static TutorialFlowController FindFlow()
        {
            return Object.FindAnyObjectByType<TutorialFlowController>();
        }

        private static IEnumerator EnterTutorial()
        {
            yield return SceneManager.LoadSceneAsync(
                GameModeScenePaths.Entry,
                LoadSceneMode.Single);
            yield return null;
            ModeEntryView entry = Object.FindAnyObjectByType<ModeEntryView>();
            Assert.That(entry, Is.Not.Null);
            entry.GetButton(GameModeId.Tutorial).onClick.Invoke();
            yield return WaitForTutorial();
        }

        private static IEnumerator WaitForTutorial(
            TutorialFlowController previous = null)
        {
            float deadline = Time.realtimeSinceStartup + 20f;
            while (Time.realtimeSinceStartup < deadline)
            {
                TutorialFlowController flow = FindFlow();
                if (SceneManager.GetActiveScene().path ==
                        GameModeScenePaths.Tutorial &&
                    !GameModeContext.IsTransitioning &&
                    flow != null && flow != previous &&
                    flow.IsInitialized)
                {
                    yield break;
                }

                yield return null;
            }

            Assert.Fail("教学场景未在期限内完成初始化。");
        }

        private static IEnumerator WaitForScene(string scenePath)
        {
            float deadline = Time.realtimeSinceStartup + 20f;
            while (Time.realtimeSinceStartup < deadline)
            {
                if (SceneManager.GetActiveScene().path == scenePath &&
                    !GameModeContext.IsTransitioning &&
                    !GameModeFlowController.Instance.IsLoading)
                {
                    yield break;
                }

                yield return null;
            }

            Assert.Fail($"场景未在期限内完成切换：{scenePath}");
        }

        private static void CompleteSequence(TutorialFlowController flow)
        {
            while (!flow.Progression.IsComplete)
            {
                TutorialStepDefinition step = flow.Progression.CurrentStep;
                Assert.That(flow.ReportEvidence(
                    step.EvidenceType,
                    step.TargetValue), Is.True, step.StableId);
            }
        }

        private static void AdvanceUntil(
            TutorialFlowController flow,
            TutorialEvidenceType target)
        {
            while (!flow.Progression.IsComplete &&
                   flow.Progression.CurrentStep.EvidenceType != target)
            {
                TutorialStepDefinition step = flow.Progression.CurrentStep;
                Assert.That(flow.ReportEvidence(
                    step.EvidenceType,
                    step.TargetValue), Is.True, step.StableId);
            }

            Assert.That(flow.Progression.IsComplete, Is.False);
            Assert.That(flow.Progression.CurrentStep.EvidenceType,
                Is.EqualTo(target));
        }
    }
}
