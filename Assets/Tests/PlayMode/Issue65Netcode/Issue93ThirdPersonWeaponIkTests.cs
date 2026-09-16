using System.Collections;
using FPS.Networking.Domain;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FPS.Tests.PlayMode.Issue65
{
    public sealed class Issue93ThirdPersonWeaponIkTests
    {
        private GameObject testRoot;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (testRoot != null) Object.Destroy(testRoot);
            yield return null;
        }

        [Test]
        public void CombatActionsReleaseAndRecoverIkWeightsSmoothly()
        {
            ThirdPersonWeaponIkController controller = CreateController(
                out _, out _);

            controller.BeginCombatAction(ThirdPersonCombatAction.Shoot);
            controller.Tick(0.02f);
            Assert.That(controller.LeftHandWeight,
                Is.InRange(0.89f, 1f));

            controller.BeginCombatAction(ThirdPersonCombatAction.Reload);
            controller.Tick(0.05f);
            float reloadWeight = controller.LeftHandWeight;
            Assert.That(reloadWeight, Is.LessThan(0.9f));
            Assert.That(reloadWeight, Is.GreaterThan(0f));

            controller.BeginCombatAction(ThirdPersonCombatAction.SwitchWeapon);
            float minimumWeight = controller.LeftHandWeight;
            for (int index = 0; index < 8; index++)
            {
                controller.Tick(0.05f);
                minimumWeight = Mathf.Min(
                    minimumWeight, controller.LeftHandWeight);
            }
            Assert.That(minimumWeight, Is.LessThan(0.05f));
            for (int index = 0; index < 8; index++) controller.Tick(0.05f);
            Assert.That(controller.LeftHandWeight, Is.GreaterThan(0.95f));
            Assert.That(controller.AimConstraintWeight,
                Is.GreaterThan(0.95f));
        }

        [Test]
        public void MuzzleFeedbackRayMatchesAuthoritativeAimConvention()
        {
            ThirdPersonWeaponIkController controller = CreateController(
                out ThirdPersonWeaponRig rig, out _);
            testRoot.transform.rotation = Quaternion.Euler(0f, 37f, 0f);
            controller.SetAimPitch(28f);
            controller.Tick(0f);

            NetVector3 expectedDomain = CoopGameplayRules.AimDirection(37d, 28d);
            Vector3 expected = new(
                (float)expectedDomain.X,
                (float)expectedDomain.Y,
                (float)expectedDomain.Z);
            Assert.That(Vector3.Dot(controller.AimDirection, expected),
                Is.GreaterThan(0.9999f));
            Assert.That(Vector3.Dot(controller.FeedbackRay.direction, expected),
                Is.GreaterThan(0.9999f));
            Assert.That(Vector3.Dot(rig.MuzzlePoint.forward, expected),
                Is.GreaterThan(0.9999f));
            Assert.That(controller.FeedbackRay.origin,
                Is.EqualTo(rig.MuzzlePoint.position));
        }

        [UnityTest]
        public IEnumerator ThreeCharactersAndTwoWeaponsKeepSafeExtremeAimPoses()
        {
            PlayerAppearanceCatalog appearances = Resources.Load<
                PlayerAppearanceCatalog>(PlayerAppearanceCatalog.ResourcesPath);
            ThirdPersonWeaponCatalog weapons = Resources.Load<
                ThirdPersonWeaponCatalog>(ThirdPersonWeaponCatalog.ResourcesPath);
            testRoot = new GameObject("Issue93 Pose Matrix");

            foreach (PlayerAppearanceDefinition appearance in
                     appearances.Definitions)
            foreach (ThirdPersonWeaponDefinition weapon in weapons.Definitions)
            foreach (float pitch in new[] { -45f, 45f })
            {
                var anchor = new GameObject(
                    $"{appearance.StableId}_{weapon.StableId}_{pitch}");
                anchor.transform.SetParent(testRoot.transform, false);
                GameObject character = PlayerAppearanceFactory.Create(
                    appearances, appearance.StableId, anchor.transform,
                    out _, out _);
                Animator animator = character.GetComponent<Animator>();
                animator.Rebind();
                animator.SetBool(ThirdPersonAnimationParameters.Grounded, true);
                animator.SetBool(ThirdPersonAnimationParameters.Aiming, true);
                animator.SetFloat(ThirdPersonAnimationParameters.AimPitch,
                    ThirdPersonWeaponIkController
                        .ResolvePresentationAimParameter(pitch));
                ThirdPersonWeaponRig rig = ThirdPersonWeaponFactory.Create(
                    weapon, animator, out _);
                ThirdPersonWeaponIkController ik = character.AddComponent<
                    ThirdPersonWeaponIkController>();
                ik.Configure(animator, rig, character.transform);
                ik.SetAiming(true);
                ik.SetAimPitch(pitch);
                ik.Tick(1f);
                animator.Update(0.2f);
                yield return null;

                Transform hand = animator.GetBoneTransform(
                    HumanBodyBones.LeftHand);
                Transform lowerArm = animator.GetBoneTransform(
                    HumanBodyBones.LeftLowerArm);
                Transform upperArm = animator.GetBoneTransform(
                    HumanBodyBones.LeftUpperArm);
                Transform chest = animator.GetBoneTransform(
                                      HumanBodyBones.UpperChest) ??
                                  animator.GetBoneTransform(
                                      HumanBodyBones.Chest);
                AssertFinite(hand.position, appearance.StableId);
                AssertFinite(lowerArm.position, appearance.StableId);
                AssertFinite(upperArm.position, appearance.StableId);
                Assert.That(Vector3.Distance(
                        hand.position, rig.LeftHandGrip.position),
                    Is.LessThan(0.12f),
                    $"{appearance.StableId}/{weapon.StableId}/{pitch}");
                Assert.That(Vector3.Dot(
                        rig.MuzzlePoint.position - character.transform.position,
                        character.transform.forward),
                    Is.GreaterThan(0.05f),
                    $"枪口不得越过角色根平面进入身体后方：" +
                    $"{appearance.StableId}/" +
                    $"{weapon.StableId}/{pitch}");
                Assert.That(Vector3.Distance(
                        rig.MuzzlePoint.position, chest.position),
                    Is.GreaterThan(0.22f),
                    $"枪口不得进入胸腔：{appearance.StableId}/" +
                    $"{weapon.StableId}/{pitch}");
                Vector3 elbowToShoulder =
                    (upperArm.position - lowerArm.position).normalized;
                Vector3 elbowToHand =
                    (hand.position - lowerArm.position).normalized;
                Assert.That(Vector3.Dot(elbowToShoulder, elbowToHand),
                    Is.GreaterThan(-0.9999f),
                    $"肘关节不得反向锁死：{appearance.StableId}/" +
                    $"{weapon.StableId}/{pitch}");
                Object.Destroy(anchor);
                yield return null;
            }
        }

        private ThirdPersonWeaponIkController CreateController(
            out ThirdPersonWeaponRig rig,
            out Animator animator)
        {
            PlayerAppearanceCatalog appearances = Resources.Load<
                PlayerAppearanceCatalog>(PlayerAppearanceCatalog.ResourcesPath);
            ThirdPersonWeaponCatalog weapons = Resources.Load<
                ThirdPersonWeaponCatalog>(ThirdPersonWeaponCatalog.ResourcesPath);
            testRoot = new GameObject("Issue93 Fixture");
            GameObject character = PlayerAppearanceFactory.Create(
                appearances, appearances.DefaultAppearanceId,
                testRoot.transform, out _, out _);
            animator = character.GetComponent<Animator>();
            rig = ThirdPersonWeaponFactory.Create(
                weapons.Definitions[0], animator, out _);
            ThirdPersonWeaponIkController controller = character.AddComponent<
                ThirdPersonWeaponIkController>();
            controller.Configure(animator, rig, testRoot.transform);
            controller.SetAiming(true);
            controller.SetAimPitch(0f);
            return controller;
        }

        private static void AssertFinite(Vector3 value, string message)
        {
            Assert.That(float.IsFinite(value.x) && float.IsFinite(value.y) &&
                        float.IsFinite(value.z), Is.True, message);
        }
    }
}
