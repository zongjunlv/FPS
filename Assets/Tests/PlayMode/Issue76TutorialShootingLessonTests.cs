using System.Collections;
using FPS.Core.GameModes;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace FPS.Tests.PlayMode
{
    public sealed class Issue76TutorialShootingLessonTests : InputTestFixture
    {
        [UnityTest]
        public IEnumerator HipFireOnlyCountsRealHitsInsideTeachingRegion()
        {
            Mouse mouse = InputSystem.AddDevice<Mouse>();
            try
            {
                yield return EnterTutorialAtStep(7);
                TutorialFlowController flow = FindFlow();
                TutorialShootingEvidenceTracker tracker = FindTracker();
                PlayerController player = flow.Environment.PlayerRig.Player;
                WeaponController weapon =
                    flow.Environment.PlayerRig.Combat.EquippedWeapon;
                weapon.SetSpreadSampleOverride(Vector2.zero);

                Vector3 center = player.transform.position;
                center.x = 0f;
                center.z = 4f;
                Vector3 outside = center;
                outside.x = 6f;
                Assert.That(player.TryRestoreSnapshotPose(
                    outside,
                    Quaternion.identity,
                    0f,
                    false,
                    out string error), Is.True, error);
                yield return FireClick(mouse, weapon);
                Assert.That(flow.Progression.CurrentValue, Is.Zero,
                    "教学区域外的墙面命中不能推进腰射步骤。");

                Assert.That(player.TryRestoreSnapshotPose(
                    center,
                    Quaternion.identity,
                    0f,
                    false,
                    out error), Is.True, error);
                for (int index = 0; index < 3; index++)
                {
                    yield return WaitForCooldown(weapon);
                    yield return FireClick(mouse, weapon);
                }

                Assert.That(tracker.HipFireHitCount, Is.EqualTo(3));
                Assert.That(flow.Progression.CurrentStepIndex, Is.EqualTo(8));
                weapon.ClearSpreadSampleOverride();
            }
            finally
            {
                InputSystem.RemoveDevice(mouse);
            }
        }

        [UnityTest]
        public IEnumerator AdsHitRequiresAimStateToBeFullyActive()
        {
            Mouse mouse = InputSystem.AddDevice<Mouse>();
            try
            {
                yield return EnterTutorialAtStep(8);
                TutorialFlowController flow = FindFlow();
                TutorialShootingEvidenceTracker tracker = FindTracker();
                PlayerGameplayRig rig = flow.Environment.PlayerRig;
                yield return MoveToShootingLane(rig.Player);
                WeaponController weapon = rig.Combat.EquippedWeapon;
                weapon.SetSpreadSampleOverride(Vector2.zero);

                yield return FireClick(mouse, weapon);
                Assert.That(flow.Progression.CurrentValue, Is.Zero,
                    "未进入 ADS 的射击不能推进瞄准步骤。");

                Press(mouse.rightButton);
                float aimTimeout = Time.realtimeSinceStartup + 2f;
                while (!rig.Player.IsAdsCrosshairActive &&
                       Time.realtimeSinceStartup < aimTimeout)
                {
                    yield return null;
                }
                Assert.That(rig.Player.IsAdsCrosshairActive, Is.True);

                for (int index = 0; index < 3; index++)
                {
                    yield return WaitForCooldown(weapon);
                    yield return FireClick(mouse, weapon);
                }
                Release(mouse.rightButton);
                yield return null;

                Assert.That(tracker.AimFireHitCount, Is.EqualTo(3));
                Assert.That(flow.Progression.CurrentStepIndex, Is.EqualTo(9));
                weapon.ClearSpreadSampleOverride();
            }
            finally
            {
                InputSystem.RemoveDevice(mouse);
            }
        }

        [UnityTest]
        public IEnumerator SemiAutomaticLessonRequiresDistinctPressCycles()
        {
            Mouse mouse = InputSystem.AddDevice<Mouse>();
            try
            {
                yield return EnterTutorialAtStep(9);
                TutorialFlowController flow = FindFlow();
                TutorialShootingEvidenceTracker tracker = FindTracker();
                PlayerCombatController combat =
                    flow.Environment.PlayerRig.Combat;
                yield return MoveToShootingLane(
                    flow.Environment.PlayerRig.Player);
                yield return SelectWeapon(combat, 1);
                WeaponController pistol = combat.EquippedWeapon;
                Assert.That(pistol.IsAutomatic, Is.False);
                pistol.SetSpreadSampleOverride(Vector2.zero);

                Assert.That(pistol.TryRestoreAmmo(0, pistol.ReserveAmmo),
                    Is.True);
                Assert.That(pistol.TryFire(), Is.False);
                yield return null;
                Assert.That(flow.Progression.CurrentValue, Is.Zero,
                    "空弹操作不能推进点射步骤。");
                Assert.That(pistol.TryRestoreAmmo(
                    pistol.MagazineCapacity,
                    pistol.ReserveAmmo), Is.True);

                int evaluatedBefore =
                    flow.Environment.ShootingTarget.EvaluatedShotCount;
                int ammoBefore = pistol.CurrentAmmo;
                Press(mouse.leftButton);
                float shotTimeout = Time.realtimeSinceStartup + 1f;
                while (pistol.CurrentAmmo == ammoBefore &&
                       Time.realtimeSinceStartup < shotTimeout)
                {
                    yield return null;
                }
                Assert.That(flow.Progression.CurrentValue, Is.EqualTo(1f),
                    $"ammo={pistol.CurrentAmmo}, " +
                    $"evaluated={flow.Environment.ShootingTarget.EvaluatedShotCount}, " +
                    $"valid={flow.Environment.ShootingTarget.ValidHitCount}, " +
                    $"lastValid={flow.Environment.ShootingTarget.LastShotWasValid}, " +
                    $"cycles={tracker.AttackCycle}, " +
                    $"taps={tracker.IndependentTapCount}");
                Assert.That(pistol.TryFire(), Is.False,
                    "首发后的射速冷却窗口必须拒绝额外射击。");
                Assert.That(flow.Progression.CurrentValue, Is.EqualTo(1f),
                    "射速冷却内的开火尝试不能产生伪进度。");
                float holdUntil = Time.realtimeSinceStartup +
                                  pistol.FireInterval * 3f + 0.2f;
                while (Time.realtimeSinceStartup < holdUntil)
                {
                    yield return null;
                }
                Assert.That(
                    flow.Environment.ShootingTarget.EvaluatedShotCount,
                    Is.EqualTo(evaluatedBefore + 1),
                    "半自动手枪长按只能产生一次有效点击射击。");
                Assert.That(flow.Progression.CurrentValue, Is.EqualTo(1f));
                Release(mouse.leftButton);
                yield return null;

                for (int index = 0; index < 2; index++)
                {
                    yield return WaitForCooldown(pistol);
                    yield return FireClick(mouse, pistol);
                }

                Assert.That(tracker.IndependentTapCount, Is.EqualTo(3));
                Assert.That(flow.Progression.CurrentStepIndex, Is.EqualTo(10));
                pistol.ClearSpreadSampleOverride();
            }
            finally
            {
                InputSystem.RemoveDevice(mouse);
            }
        }

        [UnityTest]
        public IEnumerator AutomaticBurstNeedsOneHoldAndAccumulatedRecoil()
        {
            Mouse mouse = InputSystem.AddDevice<Mouse>();
            try
            {
                yield return EnterTutorialAtStep(10);
                TutorialFlowController flow = FindFlow();
                TutorialShootingEvidenceTracker tracker = FindTracker();
                yield return MoveToShootingLane(
                    flow.Environment.PlayerRig.Player);
                WeaponController rifle =
                    flow.Environment.PlayerRig.Combat.EquippedWeapon;
                Assert.That(rifle.IsAutomatic, Is.True);
                rifle.SetSpreadSampleOverride(Vector2.zero);

                Press(mouse.leftButton);
                float interruptedTimeout = Time.realtimeSinceStartup + 2f;
                while (tracker.BurstValidHitCount < 2 &&
                       Time.realtimeSinceStartup < interruptedTimeout)
                {
                    yield return null;
                }
                Assert.That(tracker.BurstValidHitCount,
                    Is.GreaterThanOrEqualTo(2));
                Release(mouse.leftButton);
                yield return null;
                yield return null;
                Assert.That(flow.Progression.CurrentStepIndex, Is.EqualTo(10));
                Assert.That(tracker.BurstValidHitCount, Is.Zero,
                    "连射中途松开必须清空本次尝试，不能跨按压累计。");

                yield return WaitForCooldown(rifle);
                Press(mouse.leftButton);
                float timeout = Time.realtimeSinceStartup + 3f;
                while (flow.Progression.CurrentStepIndex == 10 &&
                       Time.realtimeSinceStartup < timeout)
                {
                    yield return null;
                }
                Release(mouse.leftButton);
                yield return null;

                Assert.That(tracker.BurstValidHitCount,
                    Is.GreaterThanOrEqualTo(4));
                Assert.That(tracker.BurstRecoilObserved, Is.True);
                Assert.That(tracker.MaximumBurstRecoil, Is.GreaterThan(0.05f));
                Assert.That(flow.Progression.CurrentStepIndex, Is.EqualTo(11));
                rifle.ClearSpreadSampleOverride();
            }
            finally
            {
                InputSystem.RemoveDevice(mouse);
            }
        }

        private static TutorialFlowController FindFlow()
        {
            return Object.FindAnyObjectByType<TutorialFlowController>();
        }

        private static TutorialShootingEvidenceTracker FindTracker()
        {
            return Object.FindAnyObjectByType<
                TutorialShootingEvidenceTracker>();
        }

        private IEnumerator FireClick(
            Mouse mouse,
            WeaponController weapon)
        {
            int ammoBefore = weapon.CurrentAmmo;
            Press(mouse.leftButton);
            yield return null;
            Release(mouse.leftButton);
            yield return null;

            if (ammoBefore > 0)
            {
                Assert.That(weapon.CurrentAmmo, Is.EqualTo(ammoBefore - 1));
            }
        }

        private static IEnumerator WaitForCooldown(WeaponController weapon)
        {
            float until = Time.time + weapon.FireInterval + 0.02f;
            while (Time.time < until)
            {
                yield return null;
            }
        }

        private static IEnumerator SelectWeapon(
            PlayerCombatController combat,
            int index)
        {
            Assert.That(combat.TrySelectWeapon(index), Is.True);
            float timeout = Time.realtimeSinceStartup + 3f;
            while ((combat.IsSwitching ||
                    combat.EquippedWeaponIndex != index) &&
                   Time.realtimeSinceStartup < timeout)
            {
                yield return null;
            }

            Assert.That(combat.IsSwitching, Is.False);
            Assert.That(combat.EquippedWeaponIndex, Is.EqualTo(index));
        }

        private static IEnumerator MoveToShootingLane(
            PlayerController player)
        {
            Vector3 position = player.transform.position;
            position.x = 0f;
            position.z = 4f;
            Assert.That(player.TryRestoreSnapshotPose(
                position,
                Quaternion.identity,
                0f,
                false,
                out string error), Is.True, error);
            Physics.SyncTransforms();
            yield return null;
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
            TutorialShootingEvidenceTracker tracker = null;
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
            if (targetStep >= 8)
            {
                flow.ReportEvidence(TutorialEvidenceType.HipFireHit, 3f);
            }
            if (targetStep >= 9)
            {
                flow.ReportEvidence(TutorialEvidenceType.AimFireHit, 3f);
            }
            if (targetStep >= 10)
            {
                flow.ReportEvidence(
                    TutorialEvidenceType.SemiAutomaticShot,
                    3f);
            }
        }
    }
}
