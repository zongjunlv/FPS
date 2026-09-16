using System.Collections;
using System.Collections.Generic;
using System.Linq;
using FPS.Core.GameModes;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace FPS.Tests.PlayMode
{
    public sealed class Issue75TutorialShootingWallTests : InputTestFixture
    {
        [UnityTest]
        public IEnumerator RealShotsAcceptCenterAndRejectOutsideRegion()
        {
            yield return EnterTutorial();
            TutorialTrainingEnvironment environment = FindEnvironment();
            TutorialShootingTarget target = environment.ShootingTarget;
            PlayerGameplayRig rig = environment.PlayerRig;
            WeaponController weapon = rig.Combat.EquippedWeapon;
            WeaponImpactFeedbackController impact =
                weapon.GetComponent<WeaponImpactFeedbackController>();
            MuzzleFlashController muzzle = weapon
                .GetComponentInChildren<MuzzleFlashController>(true);
            target.ResetEvidence();
            weapon.SetSpreadSampleOverride(Vector2.zero);
            int feedbackBefore = impact.FeedbackCount;
            int muzzleBefore = muzzle.PlayCount;

            Assert.That(weapon.TryFire(), Is.True);
            yield return null;

            Assert.That(weapon.LastShotResult.DidHit, Is.True);
            Assert.That(weapon.LastShotResult.HitObject,
                Is.SameAs(environment.ShootingWall.gameObject));
            Assert.That(target.ValidHitCount, Is.EqualTo(1));
            Assert.That(target.RejectedHitCount, Is.Zero);
            Assert.That(target.LastShotWasValid, Is.True);
            Assert.That(impact.FeedbackCount, Is.EqualTo(feedbackBefore + 1));
            Assert.That(muzzle.PlayCount, Is.EqualTo(muzzleBefore + 1));
            Assert.That(rig.TracerPool.ActiveCount, Is.GreaterThan(0));

            PlayerController player = rig.Player;
            Vector3 outsidePosition = player.transform.position;
            outsidePosition.x = 6f;
            Assert.That(player.TryRestoreSnapshotPose(
                outsidePosition,
                Quaternion.identity,
                0f,
                false,
                out string error), Is.True, error);
            yield return new WaitForSeconds(weapon.FireInterval + 0.05f);

            Assert.That(weapon.TryFire(), Is.True);
            yield return null;
            weapon.ClearSpreadSampleOverride();

            Assert.That(weapon.LastShotResult.DidHit, Is.True);
            Assert.That(target.ValidHitCount, Is.EqualTo(1));
            Assert.That(target.RejectedHitCount, Is.EqualTo(1));
            Assert.That(target.LastShotWasValid, Is.False);
        }

        [UnityTest]
        public IEnumerator ImpactMarkerRemainsAttachedToWallSurface()
        {
            GameObject poolHost = null;
            yield return EnterTutorial();
            TutorialShootingTarget target =
                FindEnvironment().ShootingTarget;
            Collider wall = target.WallCollider;
            Vector3 originalWallPosition = wall.transform.position;
            try
            {
                poolHost = new GameObject("Issue75 Effect Pool");
                CombatEffectPool pool = CombatEffectPool.Ensure(
                    poolHost.transform,
                    null);
                Vector3 hitPoint = target.HitRegion.bounds.center;
                var result = new ShotResult(
                    true,
                    hitPoint,
                    target.transform.forward,
                    SurfaceType.Concrete,
                    DamageResult.None,
                    null,
                    wall.gameObject);
                pool.PresentImpact(result);
                PooledEffectInstance marker = pool
                    .GetComponentsInChildren<PooledEffectInstance>(true)
                    .Single(value => value.gameObject.activeSelf);
                Vector3 localPoint = wall.transform.InverseTransformPoint(
                    marker.transform.position);

                wall.transform.position +=
                    new Vector3(0.7f, 0.4f, 0f);
                yield return null;

                Vector3 expected = wall.transform.TransformPoint(localPoint);
                Assert.That(Vector3.Distance(
                    marker.transform.position,
                    expected), Is.LessThan(0.002f),
                    "弹痕必须跟随命中墙体，不能停留在空气中。");
            }
            finally
            {
                wall.transform.position = originalWallPosition;
                if (poolHost != null)
                {
                    Object.Destroy(poolHost);
                }
            }
        }

        [UnityTest]
        public IEnumerator SustainedFireShowsSpreadAndAccumulatedRecoil()
        {
            Mouse mouse = InputSystem.AddDevice<Mouse>();
            var acceptedPoints = new List<Vector3>();
            TutorialShootingTarget target = null;
            try
            {
                yield return EnterTutorial();
                TutorialTrainingEnvironment environment = FindEnvironment();
                target = environment.ShootingTarget;
                target.ResetEvidence();
                target.ShotEvaluated += (result, _) =>
                {
                    if (result.DidHit && result.HitObject ==
                        target.WallCollider.gameObject)
                    {
                        acceptedPoints.Add(result.Point);
                    }
                };
                PlayerRecoilController recoil =
                    environment.PlayerRig.Recoil;
                WeaponController weapon =
                    environment.PlayerRig.Combat.EquippedWeapon;
                weapon.ClearSpreadSampleOverride();
                float maxRecoil = 0f;

                Press(mouse.leftButton);
                float timeout = Time.realtimeSinceStartup + 0.75f;
                while (Time.realtimeSinceStartup < timeout)
                {
                    yield return null;
                    maxRecoil = Mathf.Max(
                        maxRecoil,
                        recoil.TargetRecoil.x,
                        recoil.CurrentRecoil.x);
                }
                Release(mouse.leftButton);
                yield return null;

                Assert.That(target.EvaluatedShotCount,
                    Is.GreaterThanOrEqualTo(3));
                Assert.That(target.ValidHitCount, Is.GreaterThanOrEqualTo(1));
                Assert.That(maxRecoil, Is.GreaterThan(0.05f));
                Assert.That(acceptedPoints.Count, Is.GreaterThanOrEqualTo(3));
                bool observedSpread = acceptedPoints.Skip(1).Any(point =>
                    Vector3.Distance(point, acceptedPoints[0]) > 0.01f);
                Assert.That(observedSpread, Is.True,
                    "连续开火的弹着点应在教学墙上呈现散布。");
            }
            finally
            {
                InputSystem.RemoveDevice(mouse);
            }
        }

        private static TutorialTrainingEnvironment FindEnvironment()
        {
            return Object.FindAnyObjectByType<
                TutorialTrainingEnvironment>();
        }

        private static IEnumerator EnterTutorial()
        {
            yield return SceneManager.LoadSceneAsync(
                GameModeScenePaths.Entry,
                LoadSceneMode.Single);
            yield return null;
            ModeEntryView entry = Object.FindAnyObjectByType<ModeEntryView>();
            entry.GetButton(GameModeId.Tutorial).onClick.Invoke();

            float timeout = Time.realtimeSinceStartup + 20f;
            TutorialTrainingEnvironment environment = null;
            while (Time.realtimeSinceStartup < timeout)
            {
                environment = FindEnvironment();
                if (SceneManager.GetActiveScene().path ==
                        GameModeScenePaths.Tutorial &&
                    !GameModeContext.IsTransitioning &&
                    environment != null && environment.IsReady &&
                    environment.ShootingTarget != null &&
                    environment.ShootingTarget.enabled &&
                    environment.PlayerRig.Combat.IsInitialized)
                {
                    break;
                }
                yield return null;
            }

            Assert.That(environment, Is.Not.Null);
            Assert.That(environment.IsReady, Is.True,
                environment.InitializationError);
            Assert.That(environment.ShootingTarget, Is.Not.Null);
            Assert.That(environment.ShootingTarget.enabled, Is.True);
            yield return null;
        }
    }
}
