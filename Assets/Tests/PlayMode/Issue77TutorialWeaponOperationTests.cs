using System.Collections;
using FPS.Core.GameModes;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace FPS.Tests.PlayMode
{
    public sealed class Issue77TutorialWeaponOperationTests : TutorialInputTestFixture
    {
        [UnityTest]
        public IEnumerator DigitTwoThenWheelRequireActualCompletedSwitches()
        {
            Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
            Mouse mouse = InputSystem.AddDevice<Mouse>();
            try
            {
                yield return EnterTutorialAtStep(11);
                TutorialFlowController flow = FindFlow();
                TutorialWeaponOperationEvidenceTracker tracker = FindTracker();
                PlayerGameplayRig rig = flow.Environment.PlayerRig;
                PlayerWorldPickupController pickups = rig.GetComponent<
                    PlayerWorldPickupController>();
                PlayerInventoryController inventory = rig.GetComponent<
                    PlayerInventoryController>();
                int pickupScrollBefore = pickups.ScrollSelectionCount;

                Press(keyboard.digit2Key);
                yield return null;
                Release(keyboard.digit2Key);
                yield return WaitForSwitch(rig.Combat, PistolIndex);

                Assert.That(rig.Combat.EquippedWeaponIndex,
                    Is.EqualTo(PistolIndex));
                Assert.That(tracker.SwitchPhase, Is.EqualTo(
                    TutorialWeaponSwitchPhase.AwaitingScrollToRifle));
                Assert.That(flow.Progression.CurrentValue, Is.EqualTo(1f));

                Set(mouse.scroll, new Vector2(0f, -120f));
                yield return null;
                Set(mouse.scroll, Vector2.zero);
                yield return WaitForSwitch(rig.Combat, RifleIndex);

                Assert.That(rig.Combat.EquippedWeaponIndex,
                    Is.EqualTo(RifleIndex));
                Assert.That(tracker.CompletedSwitchInputCount, Is.EqualTo(2));
                Assert.That(flow.Progression.CurrentStepIndex, Is.EqualTo(12));
                Assert.That(pickups.NearbyPickupCount, Is.Zero);
                Assert.That(pickups.ScrollSelectionCount,
                    Is.EqualTo(pickupScrollBefore));
                Assert.That(inventory.IsOpen, Is.False);
            }
            finally
            {
                InputSystem.RemoveDevice(mouse);
                InputSystem.RemoveDevice(keyboard);
            }
        }

        [UnityTest]
        public IEnumerator SprintInterruptedSwitchDoesNotAdvanceLesson()
        {
            Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
            try
            {
                yield return EnterTutorialAtStep(11);
                TutorialFlowController flow = FindFlow();
                TutorialWeaponOperationEvidenceTracker tracker = FindTracker();
                PlayerCombatController combat =
                    flow.Environment.PlayerRig.Combat;

                InputSystem.QueueStateEvent(
                    keyboard,
                    new KeyboardState(Key.W, Key.LeftShift, Key.Digit2));
                InputSystem.Update();
                yield return null;
                InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                InputSystem.Update();
                yield return null;

                Assert.That(combat.IsSwitching, Is.False);
                Assert.That(combat.EquippedWeaponIndex, Is.EqualTo(RifleIndex));
                Assert.That(flow.Progression.CurrentValue, Is.Zero);
                Assert.That(flow.Progression.CurrentStepIndex, Is.EqualTo(11));
                Assert.That(tracker.SwitchPhase,
                    Is.EqualTo(TutorialWeaponSwitchPhase.AwaitingDigitTwo));
            }
            finally
            {
                InputSystem.RemoveDevice(keyboard);
            }
        }

        [UnityTest]
        public IEnumerator InvalidAndInterruptedReloadsNeverAdvance()
        {
            Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
            try
            {
                yield return EnterTutorialAtStep(12);
                TutorialFlowController flow = FindFlow();
                TutorialWeaponOperationEvidenceTracker tracker = FindTracker();
                PlayerGameplayRig rig = flow.Environment.PlayerRig;
                WeaponController weapon = rig.Combat.EquippedWeapon;

                Assert.That(weapon.TryRestoreAmmo(
                    weapon.MagazineCapacity,
                    tracker.PreparedReserve), Is.True);
                yield return TapKey(keyboard.rKey);
                Assert.That(weapon.IsReloading, Is.False);
                Assert.That(tracker.ReloadStartCount, Is.Zero);

                Assert.That(weapon.TryRestoreAmmo(
                    tracker.PreparedMagazine,
                    0), Is.True);
                yield return TapKey(keyboard.rKey);
                Assert.That(weapon.IsReloading, Is.False);
                Assert.That(tracker.ReloadStartCount, Is.Zero);

                Assert.That(weapon.TryRestoreAmmo(
                    tracker.PreparedMagazine,
                    tracker.PreparedReserve), Is.True);
                yield return TapKey(keyboard.rKey);
                Assert.That(weapon.IsReloading, Is.True);
                Assert.That(tracker.ReloadStartCount, Is.EqualTo(1));

                InputSystem.QueueStateEvent(
                    keyboard,
                    new KeyboardState(Key.W, Key.LeftShift));
                InputSystem.Update();
                yield return null;
                InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                InputSystem.Update();
                yield return null;

                Assert.That(weapon.IsReloading, Is.False);
                Assert.That(tracker.ReloadInterruptionCount, Is.EqualTo(1));
                Assert.That(tracker.ReloadCompletionCount, Is.Zero);
                Assert.That(flow.Progression.CurrentStepIndex, Is.EqualTo(12));
            }
            finally
            {
                InputSystem.RemoveDevice(keyboard);
            }
        }

        [UnityTest]
        public IEnumerator CompleteReloadUpdatesHudAndAdvances()
        {
            Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
            try
            {
                yield return EnterTutorialAtStep(12);
                TutorialFlowController flow = FindFlow();
                TutorialWeaponOperationEvidenceTracker tracker = FindTracker();
                PlayerGameplayRig rig = flow.Environment.PlayerRig;
                WeaponController weapon = rig.Combat.EquippedWeapon;
                AmmoHudPresenter hud = rig.GetComponent<AmmoHudPresenter>();
                int missing = weapon.MagazineCapacity -
                              tracker.PreparedMagazine;

                Assert.That(weapon.CurrentAmmo,
                    Is.EqualTo(tracker.PreparedMagazine));
                Assert.That(weapon.ReserveAmmo,
                    Is.EqualTo(tracker.PreparedReserve));
                Assert.That(hud.DisplayText,
                    Is.EqualTo($"{tracker.PreparedMagazine} / " +
                               $"{tracker.PreparedReserve}"));

                yield return TapKey(keyboard.rKey);
                Assert.That(weapon.IsReloading, Is.True);
                Assert.That(hud.StatusText, Is.EqualTo("RELOADING"));
                Assert.That(hud.DisplayText,
                    Is.EqualTo($"{tracker.PreparedMagazine} / " +
                               $"{tracker.PreparedReserve}"));

                float timeout = Time.realtimeSinceStartup + 5f;
                while (flow.Progression.CurrentStepIndex == 12 &&
                       Time.realtimeSinceStartup < timeout)
                {
                    yield return null;
                }

                Assert.That(tracker.ReloadStartCount, Is.EqualTo(1));
                Assert.That(tracker.ReloadCompletionCount, Is.EqualTo(1));
                Assert.That(weapon.IsReloading, Is.False);
                Assert.That(weapon.CurrentAmmo,
                    Is.EqualTo(weapon.MagazineCapacity));
                Assert.That(weapon.ReserveAmmo,
                    Is.EqualTo(tracker.PreparedReserve - missing));
                Assert.That(hud.DisplayText,
                    Is.EqualTo($"{weapon.MagazineCapacity} / " +
                               $"{tracker.PreparedReserve - missing}"));
                Assert.That(flow.Progression.CurrentStepIndex, Is.EqualTo(13));
            }
            finally
            {
                InputSystem.RemoveDevice(keyboard);
            }
        }

        private const int RifleIndex = 0;
        private const int PistolIndex = 1;

        private static TutorialFlowController FindFlow()
        {
            return Object.FindAnyObjectByType<TutorialFlowController>();
        }

        private static TutorialWeaponOperationEvidenceTracker FindTracker()
        {
            return Object.FindAnyObjectByType<
                TutorialWeaponOperationEvidenceTracker>();
        }

        private IEnumerator TapKey(KeyControl key)
        {
            Press(key);
            yield return null;
            Release(key);
            yield return null;
        }

        private static IEnumerator WaitForSwitch(
            PlayerCombatController combat,
            int expectedIndex)
        {
            float timeout = Time.realtimeSinceStartup + 3f;
            while ((combat.IsSwitching ||
                    combat.EquippedWeaponIndex != expectedIndex) &&
                   Time.realtimeSinceStartup < timeout)
            {
                yield return null;
            }
            Assert.That(combat.IsSwitching, Is.False);
            Assert.That(combat.EquippedWeaponIndex, Is.EqualTo(expectedIndex));
        }

        private static IEnumerator EnterTutorialAtStep(int targetStep)
        {
            yield return SceneManager.LoadSceneAsync(
                GameModeScenePaths.Entry,
                LoadSceneMode.Single);
            yield return null;
            ModeEntryView entry = Object.FindAnyObjectByType<ModeEntryView>();
            entry.GetButton(GameModeId.Tutorial).onClick.Invoke();

            float timeout = Time.realtimeSinceStartup + 20f;
            TutorialFlowController flow = null;
            TutorialWeaponOperationEvidenceTracker tracker = null;
            while (Time.realtimeSinceStartup < timeout)
            {
                flow = FindFlow();
                tracker = FindTracker();
                if (SceneManager.GetActiveScene().path ==
                        GameModeScenePaths.Tutorial &&
                    !GameModeContext.IsTransitioning &&
                    flow != null && flow.IsInitialized &&
                    tracker != null && tracker.enabled)
                {
                    break;
                }
                yield return null;
            }

            Assert.That(flow, Is.Not.Null);
            Assert.That(tracker, Is.Not.Null);
            AdvanceToStep(flow, targetStep);
            yield return null;
            Assert.That(flow.Progression.CurrentStepIndex,
                Is.EqualTo(targetStep));
        }

        private static void AdvanceToStep(
            TutorialFlowController flow,
            int targetStep)
        {
            flow.ReportEvidence(TutorialEvidenceType.MoveForwardDistance, 1f);
            flow.ReportEvidence(TutorialEvidenceType.MoveBackwardDistance, 1f);
            flow.ReportEvidence(TutorialEvidenceType.MoveLeftDistance, 1f);
            flow.ReportEvidence(TutorialEvidenceType.MoveRightDistance, 1f);
            flow.ReportEvidence(TutorialEvidenceType.Jump);
            flow.ReportEvidence(TutorialEvidenceType.Sprint, 2f);
            flow.ReportEvidence(TutorialEvidenceType.Crouch, 2f);
            flow.ReportEvidence(TutorialEvidenceType.HipFireHit, 3f);
            flow.ReportEvidence(TutorialEvidenceType.AimFireHit, 3f);
            flow.ReportEvidence(TutorialEvidenceType.SemiAutomaticShot, 3f);
            flow.ReportEvidence(TutorialEvidenceType.AutomaticBurst, 4f);
            if (targetStep >= 12)
            {
                flow.ReportEvidence(TutorialEvidenceType.WeaponSwitch, 2f);
            }
        }
    }
}
