using System.Collections;
using FPS.Networking.Domain;
using FPS.Networking.Netcode;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FPS.Tests.PlayMode.Issue65
{
    public sealed class Issue91ThirdPersonAnimationTests
    {
        private GameObject authorityObject;
        private GameObject replicaObject;
        private GameObject appearanceObject;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (appearanceObject != null) Object.Destroy(appearanceObject);
            if (replicaObject != null) Object.Destroy(replicaObject);
            if (authorityObject != null) Object.Destroy(authorityObject);
            yield return null;
        }

        [UnityTest]
        public IEnumerator DriverMapsWorldMotionStanceAndPitchWithDamping()
        {
            NetworkThirdPersonAnimator driver = CreateDriver(out Animator animator);
            driver.ApplyPresentation(Vector3.zero, 0f,
                crouching: false, grounded: true, 0f, immediate: true);
            driver.ApplyPresentation(new Vector3(6f, 3f, 0f), 44.5f,
                crouching: true, grounded: false, 1f, immediate: true);

            Assert.That(driver.NormalizedSpeed, Is.EqualTo(1f).Within(0.001f));
            Assert.That(driver.LocalMovement.x, Is.EqualTo(1f).Within(0.001f));
            Assert.That(driver.LocalMovement.y, Is.EqualTo(0f).Within(0.001f));
            Assert.That(driver.NormalizedAimPitch,
                Is.EqualTo(0.5f).Within(0.001f));
            Assert.That(animator.GetFloat(ThirdPersonAnimationParameters.MoveX),
                Is.EqualTo(1f).Within(0.001f));
            Assert.That(animator.GetFloat(
                ThirdPersonAnimationParameters.VerticalSpeed),
                Is.EqualTo(3f).Within(0.001f));
            Assert.That(animator.GetBool(
                ThirdPersonAnimationParameters.Crouching), Is.True);
            Assert.That(animator.GetBool(
                ThirdPersonAnimationParameters.Grounded), Is.False);
            yield return null;
        }

        [UnityTest]
        public IEnumerator CombatActionsStayOnUpperBodyLayer()
        {
            NetworkThirdPersonAnimator driver = CreateDriver(out Animator animator);
            driver.ApplyPresentation(Vector3.forward * 4f, 0f,
                crouching: false, grounded: true, 0f, immediate: true);
            animator.Update(0.05f);
            driver.PlayCombatAction(ThirdPersonCombatAction.Shoot);
            animator.Update(0.05f);
            yield return null;

            int baseLayer = animator.GetLayerIndex(
                ThirdPersonAnimationParameters.BaseLayerName);
            int upperLayer = animator.GetLayerIndex(
                ThirdPersonAnimationParameters.UpperBodyLayerName);
            Assert.That(baseLayer, Is.GreaterThanOrEqualTo(0));
            Assert.That(upperLayer, Is.GreaterThanOrEqualTo(0));
            Assert.That(animator.GetCurrentAnimatorStateInfo(baseLayer)
                    .IsName("Shoot"), Is.False,
                "射击不得覆盖或冻结腿部移动层。");
            Assert.That(animator.GetCurrentAnimatorStateInfo(upperLayer)
                    .IsName("Shoot") ||
                animator.GetNextAnimatorStateInfo(upperLayer).IsName("Shoot"),
                Is.True);
            Assert.That(driver.LastCombatAction,
                Is.EqualTo(ThirdPersonCombatAction.Shoot));
            Assert.That(driver.CombatActionCount, Is.EqualTo(1));
            Assert.That(animator.applyRootMotion, Is.False);
        }

        [UnityTest]
        public IEnumerator AllThreeAppearancesEvaluateFiniteHumanoidPoses()
        {
            HumanoidCharacterStandard standard = Resources.Load<
                HumanoidCharacterStandard>(
                "Content/Characters/Humanoid/HumanoidCharacterStandard");
            Assert.That(standard, Is.Not.Null);
            foreach (HumanoidCharacterEntry entry in standard.Characters)
            {
                GameObject instance = Object.Instantiate(entry.Prefab);
                Animator animator = instance.GetComponent<Animator>();
                animator.SetFloat(ThirdPersonAnimationParameters.MoveX, 0.6f);
                animator.SetFloat(ThirdPersonAnimationParameters.MoveY, 0.8f);
                animator.SetFloat(ThirdPersonAnimationParameters.AimPitch, 0.5f);
                animator.SetBool(ThirdPersonAnimationParameters.Grounded, true);
                animator.SetBool(ThirdPersonAnimationParameters.Aiming, true);
                animator.Update(0.25f);
                var pose = new HumanPose();
                using (var handler = new HumanPoseHandler(
                           animator.avatar, instance.transform))
                {
                    handler.GetHumanPose(ref pose);
                }
                Assert.That(pose.muscles, Is.Not.Null, entry.StableId);
                foreach (float muscle in pose.muscles)
                {
                    Assert.That(float.IsFinite(muscle), Is.True,
                        entry.StableId);
                    Assert.That(Mathf.Abs(muscle), Is.LessThanOrEqualTo(1.5f),
                        entry.StableId);
                }
                Object.Destroy(instance);
            }
            yield return null;
        }

        private NetworkThirdPersonAnimator CreateDriver(out Animator animator)
        {
            authorityObject = new GameObject("Issue91 Authority");
            NetworkCoopSessionAuthority authority =
                authorityObject.AddComponent<NetworkCoopSessionAuthority>();
            authority.EnableServerTestHook();
            authority.ConfigureServer(new CoopServerRules(
                    tickRate: 10,
                    maximumPastCommandTicks: 8,
                    historyCapacity: 16,
                    maximumMoveSpeed: 6d,
                    sprintSpeed: 6d),
                new[] { new CoopPlayerSpawn(1, default) },
                new[]
                {
                    new CoopTargetSpawn(1,
                        new NetVector3(0d, 0d, 20d), 1d, 100d)
                });

            replicaObject = new GameObject("Issue91 Replica");
            NetworkPlayerReplica replica =
                replicaObject.AddComponent<NetworkPlayerReplica>();
            NetworkThirdPersonAnimator driver =
                replicaObject.AddComponent<NetworkThirdPersonAnimator>();
            replica.ConfigureServerIdentity(authority, 1,
                "character.quaternius.male-light");

            PlayerAppearanceCatalog catalog = Resources.Load<
                PlayerAppearanceCatalog>(PlayerAppearanceCatalog.ResourcesPath);
            appearanceObject = Object.Instantiate(
                catalog.DefaultDefinition.VisualPrefab);
            animator = appearanceObject.GetComponent<Animator>();
            driver.BindAnimator(animator);
            return driver;
        }
    }
}
