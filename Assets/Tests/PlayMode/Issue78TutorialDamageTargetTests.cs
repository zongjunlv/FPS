using System.Collections;
using FPS.Core.GameModes;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace FPS.Tests.PlayMode
{
    public sealed class Issue78TutorialDamageTargetTests
    {
        [UnityTest]
        public IEnumerator TargetAppearsOnlyForDamageLessonsAndNeverMoves()
        {
            yield return EnterTutorialAtStep(12);
            TutorialFlowController flow = FindFlow();
            TutorialDamageTrainingTarget target = FindTarget();

            Assert.That(target.IsTrainingActive, Is.False);
            Assert.That(Object.FindObjectsByType<EnemyController>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None), Is.Empty);

            flow.ReportEvidence(TutorialEvidenceType.Reload);
            yield return null;
            Assert.That(flow.Progression.CurrentStepIndex, Is.EqualTo(13));
            Assert.That(target.IsTrainingActive, Is.True);
            Vector3 position = target.transform.position;
            Quaternion rotation = target.transform.rotation;

            for (int frame = 0; frame < 20; frame++)
            {
                yield return null;
            }

            Assert.That(Vector3.Distance(target.transform.position, position),
                Is.LessThan(0.0001f));
            Assert.That(Quaternion.Angle(target.transform.rotation, rotation),
                Is.LessThan(0.001f));
            Assert.That(target.GetComponent<Rigidbody>(), Is.Null);
            Assert.That(target.GetComponent<EnemyCombatController>(), Is.Null);
        }

        [UnityTest]
        public IEnumerator BodyAndHeadReportConfiguredDamageWithSameWeapon()
        {
            yield return EnterTutorialAtStep(13);
            TutorialFlowController flow = FindFlow();
            TutorialDamageTrainingTarget target = FindTarget();
            PlayerGameplayRig rig = flow.Environment.PlayerRig;
            WeaponController rifle = rig.Combat.EquippedWeapon;
            Vector3 firingPosition = rig.Player.transform.position;
            firingPosition.x = target.transform.position.x;
            firingPosition.z = target.transform.position.z - 5f;
            Assert.That(rig.Player.TryRestoreSnapshotPose(
                firingPosition,
                Quaternion.identity,
                0f,
                false,
                out string poseError), Is.True, poseError);
            Physics.SyncTransforms();
            rifle.SetSpreadSampleOverride(Vector2.zero);
            Assert.That(rifle.TryFire(), Is.True);
            yield return null;

            Assert.That(rifle.LastShotResult.DamageTarget,
                Is.SameAs(target.TargetHealth.gameObject));
            Assert.That(flow.Progression.CurrentStepIndex, Is.EqualTo(14));
            float bodyFinal = target.LastFinalDamage;
            Assert.That(target.LastHitRegion, Is.EqualTo(HitRegion.Body));
            Assert.That(target.LastBaseDamage, Is.EqualTo(rifle.Damage));
            Assert.That(target.LastMultiplier, Is.EqualTo(1f));
            StringAssert.Contains("身体", target.LastFeedbackText);
            StringAssert.Contains("基础", target.LastFeedbackText);
            StringAssert.Contains("倍率", target.LastFeedbackText);
            StringAssert.Contains("最终", target.LastFeedbackText);
            rifle.ClearSpreadSampleOverride();

            Assert.That(rig.Loadout.RestoreEquippedWeapon(1), Is.True);
            yield return null;
            WeaponController pistol = rig.Combat.EquippedWeapon;
            DamageResult wrongWeaponDamage = ApplyHit(
                target,
                target.HeadHitbox,
                pistol.Damage,
                rig.gameObject);
            Assert.That(target.EvaluateShot(CreateShot(
                target,
                target.HeadHitbox,
                wrongWeaponDamage)), Is.False);
            Assert.That(flow.Progression.CurrentStepIndex, Is.EqualTo(14));

            Assert.That(rig.Loadout.RestoreEquippedWeapon(0), Is.True);
            yield return null;
            DamageResult headDamage = ApplyHit(
                target,
                target.HeadHitbox,
                rifle.Damage,
                rig.gameObject);
            Assert.That(target.EvaluateShot(CreateShot(
                target,
                target.HeadHitbox,
                headDamage)), Is.True);

            Assert.That(target.LastHitRegion, Is.EqualTo(HitRegion.Head));
            Assert.That(target.LastMultiplier, Is.EqualTo(2f));
            Assert.That(target.LastFinalDamage,
                Is.EqualTo(rifle.Damage * 2f).Within(0.001f));
            Assert.That(target.LastFinalDamage, Is.GreaterThan(bodyFinal));
            StringAssert.Contains("头部", target.LastFeedbackText);
            Assert.That(flow.Progression.IsComplete, Is.True);
            Assert.That(target.IsTrainingActive, Is.False);
        }

        [UnityTest]
        public IEnumerator LowHealthResetsSafelyWithoutDeathPipeline()
        {
            yield return EnterTutorialAtStep(13);
            TutorialFlowController flow = FindFlow();
            TutorialDamageTrainingTarget target = FindTarget();
            WeaponController weapon = flow.Environment.PlayerRig.Combat
                .EquippedWeapon;
            bool died = false;
            target.TargetHealth.Died += () => died = true;
            Assert.That(target.TargetHealth.TryRestoreSnapshotVitals(85f, 0f),
                Is.True);

            DamageResult damage = ApplyHit(
                target,
                target.HeadHitbox,
                weapon.Damage,
                flow.Environment.PlayerRig.gameObject);
            target.EvaluateShot(CreateShot(
                target,
                target.HeadHitbox,
                damage));

            Assert.That(died, Is.False);
            Assert.That(target.UnexpectedDeathCount, Is.Zero);
            Assert.That(target.SafeResetCount, Is.EqualTo(1));
            Assert.That(target.TargetHealth.IsDead, Is.False);
            Assert.That(target.TargetHealth.CurrentHealth,
                Is.EqualTo(target.TargetHealth.MaxHealth));
            Assert.That(flow.Progression.CurrentStepIndex, Is.EqualTo(13),
                "身体教学阶段命中头部不能推进流程。");
        }

        private static DamageResult ApplyHit(
            TutorialDamageTrainingTarget target,
            DamageHitbox hitbox,
            float baseDamage,
            GameObject source)
        {
            return hitbox.ApplyDamage(new DamageInfo(
                baseDamage,
                hitbox.transform.position,
                Vector3.forward,
                source,
                DamageType.Hitscan));
        }

        private static ShotResult CreateShot(
            TutorialDamageTrainingTarget target,
            DamageHitbox hitbox,
            DamageResult damage)
        {
            return new ShotResult(
                true,
                hitbox.transform.position,
                Vector3.back,
                SurfaceType.Concrete,
                damage,
                target.TargetHealth.gameObject,
                hitbox.gameObject);
        }

        private static TutorialFlowController FindFlow()
        {
            return Object.FindAnyObjectByType<TutorialFlowController>();
        }

        private static TutorialDamageTrainingTarget FindTarget()
        {
            return Object.FindAnyObjectByType<TutorialDamageTrainingTarget>();
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
            TutorialDamageTrainingTarget target = null;
            while (Time.realtimeSinceStartup < timeout)
            {
                flow = FindFlow();
                target = FindTarget();
                if (SceneManager.GetActiveScene().path ==
                        GameModeScenePaths.Tutorial &&
                    !GameModeContext.IsTransitioning &&
                    flow != null && flow.IsInitialized &&
                    target != null && target.enabled)
                {
                    break;
                }
                yield return null;
            }

            Assert.That(flow, Is.Not.Null);
            Assert.That(target, Is.Not.Null);
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
            flow.ReportEvidence(TutorialEvidenceType.WeaponSwitch, 2f);
            if (targetStep >= 13)
            {
                flow.ReportEvidence(TutorialEvidenceType.Reload);
            }
        }
    }
}
