using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace FPS.Tests.PlayMode
{
    public class CityNewSmokeTests : InputTestFixture
    {
        private const string ScenePath =
            "Assets/ImportPackages/CSAssets2026/Scenes/CityNew.unity";

        [UnityTest]
        public IEnumerator CityNewBuildSceneLoadsCoreGameplay()
        {
            yield return LoadCityNew();

            Scene activeScene = SceneManager.GetActiveScene();
            Assert.That(activeScene.path, Is.EqualTo(ScenePath));

            List<GameObject> sceneObjects = GetSceneObjects(activeScene);
            GameObject player = FindObjectWithComponent(
                sceneObjects,
                "PlayerController");

            Assert.That(player, Is.Not.Null, "PlayerController is missing.");
            Assert.That(
                player.GetComponent<CharacterController>(),
                Is.Not.Null,
                "CharacterController is missing from the player.");
            Assert.That(
                player.GetComponent("PlayerInputReader"),
                Is.Not.Null,
                "PlayerInputReader is missing from the player.");
            Assert.That(
                player.GetComponent("PlayerCombatController"),
                Is.Not.Null,
                "PlayerCombatController is missing from the player.");
            Assert.That(
                FindObjectWithComponent(sceneObjects, "WeaponController"),
                Is.Not.Null,
                "WeaponController is missing from the scene.");
            Assert.That(
                FindObjectWithComponent(sceneObjects, "EnemyController"),
                Is.Not.Null,
                "EnemyController is missing from the scene.");
            Assert.That(
                FindObjectWithComponent(sceneObjects, "ThirdPersonLookController"),
                Is.Null,
                "The obsolete third-person look component must be removed.");

            foreach (GameObject sceneObject in sceneObjects)
            {
                foreach (Component component in sceneObject.GetComponents<Component>())
                {
                    Assert.That(
                        component,
                        Is.Not.Null,
                        $"{GetHierarchyPath(sceneObject)} has a missing script.");
                }
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator WeaponFiresProjectileAndEnemyCanDie()
        {
            yield return LoadCityNew();

            List<GameObject> sceneObjects =
                GetSceneObjects(SceneManager.GetActiveScene());
            GameObject weaponObject = FindObjectWithComponent(
                sceneObjects,
                "WeaponController");
            GameObject enemyObject = FindObjectWithComponent(
                sceneObjects,
                "EnemyController");

            Assert.That(weaponObject, Is.Not.Null);
            Assert.That(enemyObject, Is.Not.Null);

            Component weapon = weaponObject.GetComponent("WeaponController");
            MethodInfo tryFire = weapon.GetType().GetMethod("TryFire");
            Assert.That(tryFire, Is.Not.Null, "WeaponController.TryFire is missing.");

            int projectileCountBefore = CountComponentsByName(
                "ProjectileController");
            bool didFire = (bool)tryFire.Invoke(weapon, null);
            int projectileCountAfter = CountComponentsByName(
                "ProjectileController");

            Assert.That(didFire, Is.True, "The equipped weapon did not fire.");
            Assert.That(
                projectileCountAfter,
                Is.GreaterThan(projectileCountBefore),
                "Firing must instantiate a projectile.");

            Component enemy = enemyObject.GetComponent("EnemyController");
            MethodInfo getHit = enemy.GetType().GetMethod("GetHit");
            Assert.That(getHit, Is.Not.Null, "EnemyController.GetHit is missing.");

            getHit.Invoke(enemy, new object[] { 10000f });
            yield return null;

            Assert.That(
                enemyObject == null,
                Is.True,
                "Lethal damage must destroy the enemy.");
        }

        [UnityTest]
        public IEnumerator SuccessfulShotConsumesExactlyOneRound()
        {
            yield return LoadCityNew();

            GameObject weaponObject = FindObjectWithComponent(
                GetSceneObjects(SceneManager.GetActiveScene()),
                "WeaponController");
            Component weapon = weaponObject.GetComponent("WeaponController");
            MethodInfo tryFire = weapon.GetType().GetMethod("TryFire");
            PropertyInfo currentAmmo =
                weapon.GetType().GetProperty("CurrentAmmo");

            Assert.That(
                currentAmmo,
                Is.Not.Null,
                "WeaponController.CurrentAmmo is missing.");

            int ammoBefore = (int)currentAmmo.GetValue(weapon);
            bool didFire = (bool)tryFire.Invoke(weapon, null);
            int ammoAfter = (int)currentAmmo.GetValue(weapon);

            Assert.That(didFire, Is.True);
            Assert.That(
                ammoAfter,
                Is.EqualTo(ammoBefore - 1),
                "A successful shot must consume exactly one round.");
        }

        [Test]
        public void RuntimeAmmoStatePartiallyReloadsWithoutChangingCapacity()
        {
            System.Type ammoStateType =
                System.Type.GetType("WeaponAmmoState, Assembly-CSharp");

            Assert.That(
                ammoStateType,
                Is.Not.Null,
                "WeaponAmmoState runtime module is missing.");

            object ammoState = System.Activator.CreateInstance(
                ammoStateType,
                new object[] { 30, 5 });
            MethodInfo tryConsumeRound =
                ammoStateType.GetMethod("TryConsumeRound");
            MethodInfo tryBeginReload =
                ammoStateType.GetMethod("TryBeginReload");
            MethodInfo advanceReload =
                ammoStateType.GetMethod("AdvanceReload");
            PropertyInfo currentAmmo =
                ammoStateType.GetProperty("CurrentAmmo");
            PropertyInfo reserveAmmo =
                ammoStateType.GetProperty("ReserveAmmo");
            PropertyInfo magazineCapacity =
                ammoStateType.GetProperty("MagazineCapacity");

            Assert.That((bool)tryConsumeRound.Invoke(ammoState, null), Is.True);
            Assert.That((bool)tryConsumeRound.Invoke(ammoState, null), Is.True);

            Assert.That((bool)tryBeginReload.Invoke(ammoState, null), Is.True);
            Assert.That(
                (bool)advanceReload.Invoke(
                    ammoState,
                    new object[] { 1f, 0.5f }),
                Is.True);

            Assert.That((int)currentAmmo.GetValue(ammoState), Is.EqualTo(30));
            Assert.That((int)reserveAmmo.GetValue(ammoState), Is.EqualTo(3));
            Assert.That(
                (int)magazineCapacity.GetValue(ammoState),
                Is.EqualTo(30),
                "Reloading must not mutate static magazine capacity.");
        }

        [Test]
        public void RuntimeAmmoStateUsesOnlyAvailableReserve()
        {
            System.Type ammoStateType =
                System.Type.GetType("WeaponAmmoState, Assembly-CSharp");
            object ammoState = System.Activator.CreateInstance(
                ammoStateType,
                new object[] { 3, 2 });
            MethodInfo tryConsumeRound =
                ammoStateType.GetMethod("TryConsumeRound");
            MethodInfo tryBeginReload =
                ammoStateType.GetMethod("TryBeginReload");
            MethodInfo advanceReload =
                ammoStateType.GetMethod("AdvanceReload");
            PropertyInfo currentAmmo =
                ammoStateType.GetProperty("CurrentAmmo");
            PropertyInfo reserveAmmo =
                ammoStateType.GetProperty("ReserveAmmo");

            Assert.That(
                (bool)tryBeginReload.Invoke(ammoState, null),
                Is.False,
                "A full magazine must not start reloading.");

            for (int index = 0; index < 3; index++)
            {
                Assert.That(
                    (bool)tryConsumeRound.Invoke(ammoState, null),
                    Is.True);
            }

            Assert.That(
                (bool)tryConsumeRound.Invoke(ammoState, null),
                Is.False,
                "An empty magazine cannot consume another round.");
            Assert.That((bool)tryBeginReload.Invoke(ammoState, null), Is.True);
            Assert.That(
                (bool)advanceReload.Invoke(
                    ammoState,
                    new object[] { 1f, 0.5f }),
                Is.True);
            Assert.That((int)currentAmmo.GetValue(ammoState), Is.EqualTo(2));
            Assert.That((int)reserveAmmo.GetValue(ammoState), Is.EqualTo(0));
            Assert.That(
                (bool)tryBeginReload.Invoke(ammoState, null),
                Is.False,
                "Reloading must not start when reserve ammo is empty.");
        }

        [Test]
        public void CancellingReloadDoesNotTransferAmmo()
        {
            System.Type ammoStateType =
                System.Type.GetType("WeaponAmmoState, Assembly-CSharp");
            object ammoState = System.Activator.CreateInstance(
                ammoStateType,
                new object[] { 30, 90 });
            MethodInfo tryConsumeRound =
                ammoStateType.GetMethod("TryConsumeRound");
            MethodInfo tryBeginReload =
                ammoStateType.GetMethod("TryBeginReload");
            MethodInfo cancelReload =
                ammoStateType.GetMethod("CancelReload");
            PropertyInfo currentAmmo =
                ammoStateType.GetProperty("CurrentAmmo");
            PropertyInfo reserveAmmo =
                ammoStateType.GetProperty("ReserveAmmo");
            PropertyInfo isReloading =
                ammoStateType.GetProperty("IsReloading");

            Assert.That((bool)tryConsumeRound.Invoke(ammoState, null), Is.True);
            int currentBeforeReload = (int)currentAmmo.GetValue(ammoState);
            int reserveBeforeReload = (int)reserveAmmo.GetValue(ammoState);

            Assert.That((bool)tryBeginReload.Invoke(ammoState, null), Is.True);
            Assert.That((bool)cancelReload.Invoke(ammoState, null), Is.True);

            Assert.That((bool)isReloading.GetValue(ammoState), Is.False);
            Assert.That(
                (int)currentAmmo.GetValue(ammoState),
                Is.EqualTo(currentBeforeReload));
            Assert.That(
                (int)reserveAmmo.GetValue(ammoState),
                Is.EqualTo(reserveBeforeReload));
        }

        [UnityTest]
        public IEnumerator EmptyMagazineDoesNotSpawnProjectileAndThrottlesFeedback()
        {
            yield return LoadCityNew();

            GameObject weaponObject = FindObjectWithComponent(
                GetSceneObjects(SceneManager.GetActiveScene()),
                "WeaponController");
            Component weapon = weaponObject.GetComponent("WeaponController");
            MethodInfo tryFire = weapon.GetType().GetMethod("TryFire");
            PropertyInfo currentAmmo =
                weapon.GetType().GetProperty("CurrentAmmo");
            PropertyInfo magazineCapacity =
                weapon.GetType().GetProperty("MagazineCapacity");
            PropertyInfo fireInterval =
                weapon.GetType().GetProperty("FireInterval");
            PropertyInfo dryFireFeedbackCount =
                weapon.GetType().GetProperty("DryFireFeedbackCount");
            int capacity = (int)magazineCapacity.GetValue(weapon);
            float shotDelay = (float)fireInterval.GetValue(weapon) + 0.01f;

            for (int index = 0; index < capacity; index++)
            {
                if (index > 0)
                {
                    yield return new WaitForSeconds(shotDelay);
                }

                Assert.That(
                    (bool)tryFire.Invoke(weapon, null),
                    Is.True,
                    $"Shot {index + 1} should fire.");
            }

            Assert.That((int)currentAmmo.GetValue(weapon), Is.EqualTo(0));
            yield return new WaitForSeconds(shotDelay);

            int projectilesBefore =
                CountComponentsByName("ProjectileController");
            int feedbackBefore =
                (int)dryFireFeedbackCount.GetValue(weapon);

            Assert.That((bool)tryFire.Invoke(weapon, null), Is.False);
            Assert.That(
                CountComponentsByName("ProjectileController"),
                Is.EqualTo(projectilesBefore),
                "Dry firing must not instantiate a damaging projectile.");
            Assert.That(
                (int)dryFireFeedbackCount.GetValue(weapon),
                Is.EqualTo(feedbackBefore + 1));

            Assert.That((bool)tryFire.Invoke(weapon, null), Is.False);
            Assert.That(
                (int)dryFireFeedbackCount.GetValue(weapon),
                Is.EqualTo(feedbackBefore + 1),
                "Automatic dry-fire feedback must be rate limited.");
        }

        [UnityTest]
        public IEnumerator ReloadBlocksFireAndTransfersAmmoOnCompletion()
        {
            yield return LoadCityNew();

            GameObject playerObject = FindObjectWithComponent(
                GetSceneObjects(SceneManager.GetActiveScene()),
                "PlayerAnimatorController");
            Component playerAnimator =
                playerObject.GetComponent("PlayerAnimatorController");
            PropertyInfo isReloadAnimationPlaying =
                playerAnimator.GetType().GetProperty(
                    "IsReloadAnimationPlaying");
            GameObject weaponObject = FindObjectWithComponent(
                GetSceneObjects(SceneManager.GetActiveScene()),
                "WeaponController");
            Component weapon = weaponObject.GetComponent("WeaponController");
            MethodInfo tryFire = weapon.GetType().GetMethod("TryFire");
            MethodInfo tryStartReload =
                weapon.GetType().GetMethod("TryStartReload");
            PropertyInfo currentAmmo =
                weapon.GetType().GetProperty("CurrentAmmo");
            PropertyInfo reserveAmmo =
                weapon.GetType().GetProperty("ReserveAmmo");
            PropertyInfo magazineCapacity =
                weapon.GetType().GetProperty("MagazineCapacity");
            PropertyInfo reloadDuration =
                weapon.GetType().GetProperty("ReloadDuration");
            PropertyInfo isReloading =
                weapon.GetType().GetProperty("IsReloading");

            Assert.That(tryStartReload, Is.Not.Null);
            Assert.That(reserveAmmo, Is.Not.Null);
            Assert.That(magazineCapacity, Is.Not.Null);
            Assert.That(reloadDuration, Is.Not.Null);
            Assert.That(isReloading, Is.Not.Null);

            Assert.That((bool)tryFire.Invoke(weapon, null), Is.True);
            int ammoAfterShot = (int)currentAmmo.GetValue(weapon);
            int reserveBeforeReload = (int)reserveAmmo.GetValue(weapon);

            Assert.That(
                (bool)tryStartReload.Invoke(weapon, null),
                Is.True);
            Assert.That((bool)isReloading.GetValue(weapon), Is.True);
            Assert.That(
                (bool)isReloadAnimationPlaying.GetValue(playerAnimator),
                Is.True);
            Assert.That(
                (bool)tryFire.Invoke(weapon, null),
                Is.False,
                "Reloading must block firing.");
            Assert.That(
                (int)currentAmmo.GetValue(weapon),
                Is.EqualTo(ammoAfterShot));

            yield return new WaitForSeconds(
                (float)reloadDuration.GetValue(weapon) + 0.1f);

            Assert.That((bool)isReloading.GetValue(weapon), Is.False);
            Assert.That(
                (int)currentAmmo.GetValue(weapon),
                Is.EqualTo((int)magazineCapacity.GetValue(weapon)));
            Assert.That(
                (int)reserveAmmo.GetValue(weapon),
                Is.EqualTo(reserveBeforeReload - 1));
            Assert.That(
                (bool)isReloadAnimationPlaying.GetValue(playerAnimator),
                Is.False,
                "Completing a reload must leave the arms animation state.");

            Assert.That((bool)tryFire.Invoke(weapon, null), Is.True);
            int currentBeforeCancelledReload =
                (int)currentAmmo.GetValue(weapon);
            int reserveBeforeCancelledReload =
                (int)reserveAmmo.GetValue(weapon);
            MethodInfo cancelReload =
                weapon.GetType().GetMethod("CancelReload");

            Assert.That(
                (bool)tryStartReload.Invoke(weapon, null),
                Is.True);
            Assert.That((bool)cancelReload.Invoke(weapon, null), Is.True);
            Assert.That(
                (bool)isReloadAnimationPlaying.GetValue(playerAnimator),
                Is.False,
                "Cancelling a reload must leave the arms animation state.");
            Assert.That(
                (int)currentAmmo.GetValue(weapon),
                Is.EqualTo(currentBeforeCancelledReload));
            Assert.That(
                (int)reserveAmmo.GetValue(weapon),
                Is.EqualTo(reserveBeforeCancelledReload));
        }

        [UnityTest]
        public IEnumerator ReloadInputStartsReloadAndExitsAim()
        {
            Keyboard keyboard = InputSystem.AddDevice<Keyboard>();

            try
            {
                yield return LoadCityNew();

                List<GameObject> sceneObjects =
                    GetSceneObjects(SceneManager.GetActiveScene());
                GameObject player = FindObjectWithComponent(
                    sceneObjects,
                    "PlayerController");
                Component playerController =
                    player.GetComponent("PlayerController");
                Component inputReader =
                    player.GetComponent("PlayerInputReader");
                Component playerAnimator =
                    player.GetComponent("PlayerAnimatorController");
                Component weapon = FindObjectWithComponent(
                    sceneObjects,
                    "WeaponController").GetComponent("WeaponController");
                MethodInfo trySetAiming =
                    playerController.GetType().GetMethod("TrySetAiming");
                MethodInfo tryFire = weapon.GetType().GetMethod("TryFire");
                PropertyInfo isAiming =
                    playerController.GetType().GetProperty("IsAiming");
                PropertyInfo isReloading =
                    weapon.GetType().GetProperty("IsReloading");
                PropertyInfo reloadPressed =
                    inputReader.GetType().GetProperty("ReloadPressed");
                PropertyInfo isReloadAnimationPlaying =
                    playerAnimator.GetType().GetProperty(
                        "IsReloadAnimationPlaying");

                Assert.That((bool)tryFire.Invoke(weapon, null), Is.True);
                Assert.That(
                    (bool)trySetAiming.Invoke(
                        playerController,
                        new object[] { true }),
                    Is.True);

                PressAndRelease(keyboard.rKey);
                yield return null;
                Assert.That(
                    (bool)reloadPressed.GetValue(inputReader),
                    Is.True,
                    "Reload input callback must latch R until LateUpdate.");
                yield return null;

                Assert.That(
                    (bool)isReloading.GetValue(weapon),
                    Is.True,
                    "Pressing R must start a valid reload.");
                Assert.That(
                    (bool)isAiming.GetValue(playerController),
                    Is.False,
                    "Starting a reload must exit ADS.");
                Assert.That(
                    isReloadAnimationPlaying,
                    Is.Not.Null,
                    "PlayerAnimatorController must expose reload animation state.");
                Assert.That(
                    (bool)isReloadAnimationPlaying.GetValue(playerAnimator),
                    Is.True,
                    "A successful reload must animate the first-person arms.");
            }
            finally
            {
                InputSystem.RemoveDevice(keyboard);
            }
        }

        [UnityTest]
        public IEnumerator AmmoHudTracksWeaponRuntimeState()
        {
            yield return LoadCityNew();

            List<GameObject> sceneObjects =
                GetSceneObjects(SceneManager.GetActiveScene());
            GameObject weaponObject = FindObjectWithComponent(
                sceneObjects,
                "WeaponController");
            Component weapon = weaponObject.GetComponent("WeaponController");
            GameObject hudObject = FindObjectWithComponent(
                sceneObjects,
                "AmmoHudPresenter");

            Assert.That(
                hudObject,
                Is.Not.Null,
                "AmmoHudPresenter is missing from the player.");

            Component hud = hudObject.GetComponent("AmmoHudPresenter");
            PropertyInfo displayText =
                hud.GetType().GetProperty("DisplayText");
            PropertyInfo currentAmmo =
                weapon.GetType().GetProperty("CurrentAmmo");
            PropertyInfo reserveAmmo =
                weapon.GetType().GetProperty("ReserveAmmo");
            MethodInfo tryFire = weapon.GetType().GetMethod("TryFire");

            string initialText =
                $"{currentAmmo.GetValue(weapon)} / " +
                $"{reserveAmmo.GetValue(weapon)}";
            Assert.That(
                (string)displayText.GetValue(hud),
                Is.EqualTo(initialText));

            Assert.That((bool)tryFire.Invoke(weapon, null), Is.True);
            yield return null;

            string textAfterShot =
                $"{currentAmmo.GetValue(weapon)} / " +
                $"{reserveAmmo.GetValue(weapon)}";
            Assert.That(
                (string)displayText.GetValue(hud),
                Is.EqualTo(textAfterShot));
        }

        [UnityTest]
        public IEnumerator PlayerCrouchLowersCapsuleAndViewThenRestoresThem()
        {
            yield return LoadCityNew();

            GameObject player = FindObjectWithComponent(
                GetSceneObjects(SceneManager.GetActiveScene()),
                "PlayerController");
            Component controller = player.GetComponent("PlayerController");
            CharacterController capsule =
                player.GetComponent<CharacterController>();
            Transform cameraPivot = FindChildByName(
                player.transform,
                "CameraPitchPivot");
            MethodInfo trySetCrouching =
                controller.GetType().GetMethod("TrySetCrouching");
            PropertyInfo isCrouching =
                controller.GetType().GetProperty("IsCrouching");

            Assert.That(
                trySetCrouching,
                Is.Not.Null,
                "PlayerController.TrySetCrouching is missing.");
            Assert.That(
                isCrouching,
                Is.Not.Null,
                "PlayerController.IsCrouching is missing.");
            Assert.That(cameraPivot, Is.Not.Null);

            float standingHeight = capsule.height;
            float standingViewHeight = cameraPivot.localPosition.y;

            Assert.That(standingHeight, Is.EqualTo(1.8f).Within(0.01f));
            Assert.That(capsule.radius, Is.EqualTo(0.4f).Within(0.01f));
            Assert.That(
                capsule.center.y - capsule.height * 0.5f,
                Is.EqualTo(0f).Within(0.01f),
                "The standing capsule must keep its feet at the player root.");
            Assert.That(
                standingViewHeight,
                Is.EqualTo(1.65f).Within(0.01f),
                "The camera must use eye height instead of capsule-top height.");

            Assert.That(
                (bool)trySetCrouching.Invoke(controller, new object[] { true }),
                Is.True);
            yield return WaitForStanceTransition();

            Assert.That((bool)isCrouching.GetValue(controller), Is.True);
            Assert.That(capsule.height, Is.EqualTo(1.2f).Within(0.01f));
            Assert.That(capsule.center.y, Is.EqualTo(0.6f).Within(0.01f));
            Assert.That(
                capsule.center.y - capsule.height * 0.5f,
                Is.EqualTo(0f).Within(0.01f),
                "Crouching must not lift or sink the player's feet.");
            Assert.That(
                cameraPivot.localPosition.y,
                Is.EqualTo(1.05f).Within(0.01f));

            Assert.That(
                (bool)trySetCrouching.Invoke(controller, new object[] { false }),
                Is.True);
            yield return WaitForStanceTransition();

            Assert.That((bool)isCrouching.GetValue(controller), Is.False);
            Assert.That(capsule.height, Is.EqualTo(standingHeight).Within(0.01f));
            Assert.That(
                cameraPivot.localPosition.y,
                Is.EqualTo(standingViewHeight).Within(0.01f));
        }

        [UnityTest]
        public IEnumerator PlayerCannotStandUnderLowCeiling()
        {
            yield return LoadCityNew();

            GameObject player = FindObjectWithComponent(
                GetSceneObjects(SceneManager.GetActiveScene()),
                "PlayerController");
            Component controller = player.GetComponent("PlayerController");
            MethodInfo trySetCrouching =
                controller.GetType().GetMethod("TrySetCrouching");
            PropertyInfo isCrouching =
                controller.GetType().GetProperty("IsCrouching");

            Assert.That(
                (bool)trySetCrouching.Invoke(controller, new object[] { true }),
                Is.True);
            yield return WaitForStanceTransition();

            GameObject ceiling = GameObject.CreatePrimitive(
                PrimitiveType.Cube);
            ceiling.name = "CrouchClearanceTestCeiling";
            ceiling.transform.position =
                player.transform.position + Vector3.up * 1.45f;
            ceiling.transform.localScale = new Vector3(2f, 0.2f, 2f);
            Physics.SyncTransforms();

            Assert.That(
                (bool)trySetCrouching.Invoke(controller, new object[] { false }),
                Is.False,
                "Standing must be rejected while headroom is blocked.");
            Assert.That((bool)isCrouching.GetValue(controller), Is.True);

            Object.Destroy(ceiling);
            yield return null;
            Physics.SyncTransforms();

            Assert.That(
                (bool)trySetCrouching.Invoke(controller, new object[] { false }),
                Is.True);
            yield return WaitForStanceTransition();

            Assert.That((bool)isCrouching.GetValue(controller), Is.False);
        }

        [UnityTest]
        public IEnumerator PlayerPauseStopsTimeAndReleasesCursor()
        {
            yield return LoadCityNew();

            GameObject player = FindObjectWithComponent(
                GetSceneObjects(SceneManager.GetActiveScene()),
                "PlayerController");
            Component controller = player.GetComponent("PlayerController");
            MethodInfo setPaused =
                controller.GetType().GetMethod("SetPaused");
            PropertyInfo isPaused =
                controller.GetType().GetProperty("IsPaused");

            Assert.That(setPaused, Is.Not.Null);
            Assert.That(
                isPaused,
                Is.Not.Null,
                "PlayerController.IsPaused is missing.");

            try
            {
                setPaused.Invoke(controller, new object[] { true });

                Assert.That((bool)isPaused.GetValue(controller), Is.True);
                Assert.That(Time.timeScale, Is.EqualTo(0f));
                Assert.That(Cursor.lockState, Is.EqualTo(CursorLockMode.None));
                Assert.That(Cursor.visible, Is.True);
            }
            finally
            {
                setPaused.Invoke(controller, new object[] { false });
            }

            Assert.That((bool)isPaused.GetValue(controller), Is.False);
            Assert.That(Time.timeScale, Is.EqualTo(1f));
        }

        [UnityTest]
        public IEnumerator PlayerLookUsesDeviceSensitivityAndInvertY()
        {
            yield return LoadCityNew();

            GameObject player = FindObjectWithComponent(
                GetSceneObjects(SceneManager.GetActiveScene()),
                "PlayerController");
            Component controller = player.GetComponent("PlayerController");
            MethodInfo calculateLookDelta =
                controller.GetType().GetMethod("CalculateLookDelta");
            PropertyInfo invertY =
                controller.GetType().GetProperty("InvertY");

            Assert.That(
                calculateLookDelta,
                Is.Not.Null,
                "PlayerController.CalculateLookDelta is missing.");
            Assert.That(
                invertY,
                Is.Not.Null,
                "PlayerController.InvertY is missing.");

            Vector2 mouseDelta = (Vector2)calculateLookDelta.Invoke(
                controller,
                new object[] { new Vector2(10f, 5f), true, 0.5f });
            Vector2 gamepadDelta = (Vector2)calculateLookDelta.Invoke(
                controller,
                new object[] { new Vector2(1f, 0.5f), false, 0.5f });

            Assert.That(mouseDelta.x, Is.EqualTo(1f).Within(0.01f));
            Assert.That(mouseDelta.y, Is.EqualTo(-0.5f).Within(0.01f));
            Assert.That(gamepadDelta.x, Is.EqualTo(90f).Within(0.01f));
            Assert.That(gamepadDelta.y, Is.EqualTo(-45f).Within(0.01f));

            invertY.SetValue(controller, true);
            Vector2 invertedMouseDelta =
                (Vector2)calculateLookDelta.Invoke(
                    controller,
                    new object[] { new Vector2(10f, 5f), true, 0.5f });

            Assert.That(
                invertedMouseDelta.y,
                Is.EqualTo(0.5f).Within(0.01f));
        }

        [UnityTest]
        public IEnumerator PlayerMovementRulesPreventSpeedBoosts()
        {
            yield return LoadCityNew();

            GameObject player = FindObjectWithComponent(
                GetSceneObjects(SceneManager.GetActiveScene()),
                "PlayerController");
            Component controller = player.GetComponent("PlayerController");
            MethodInfo calculateVelocity =
                controller.GetType().GetMethod("CalculateHorizontalVelocity");
            MethodInfo trySetCrouching =
                controller.GetType().GetMethod("TrySetCrouching");

            Assert.That(
                calculateVelocity,
                Is.Not.Null,
                "PlayerController.CalculateHorizontalVelocity is missing.");

            Vector3 idleVelocity = (Vector3)calculateVelocity.Invoke(
                controller,
                new object[] { Vector2.zero, true });
            Vector3 diagonalSprintVelocity =
                (Vector3)calculateVelocity.Invoke(
                    controller,
                    new object[] { Vector2.one, true });
            Vector3 backwardVelocity = (Vector3)calculateVelocity.Invoke(
                controller,
                new object[] { Vector2.down, true });

            Assert.That(idleVelocity.magnitude, Is.EqualTo(0f).Within(0.01f));
            Assert.That(
                diagonalSprintVelocity.magnitude,
                Is.EqualTo(5f).Within(0.01f),
                "Diagonal movement must not exceed sprint speed.");
            Assert.That(
                backwardVelocity.magnitude,
                Is.EqualTo(2f).Within(0.01f),
                "Backward movement must use walk speed.");

            trySetCrouching.Invoke(controller, new object[] { true });
            Vector3 crouchingVelocity = (Vector3)calculateVelocity.Invoke(
                controller,
                new object[] { Vector2.one, true });

            Assert.That(
                crouchingVelocity.magnitude,
                Is.EqualTo(1.5f).Within(0.01f),
                "Crouching must override sprint input.");
        }

        [UnityTest]
        public IEnumerator PlayerAimTransitionsCameraWeaponCrosshairAndSensitivity()
        {
            yield return LoadCityNew();

            GameObject player = FindObjectWithComponent(
                GetSceneObjects(SceneManager.GetActiveScene()),
                "PlayerController");
            Component controller = player.GetComponent("PlayerController");
            MethodInfo trySetAiming =
                controller.GetType().GetMethod("TrySetAiming");
            MethodInfo calculateLookDelta =
                controller.GetType().GetMethod("CalculateLookDelta");
            PropertyInfo isAiming =
                controller.GetType().GetProperty("IsAiming");
            PropertyInfo aimBlend =
                controller.GetType().GetProperty("AimBlend");
            PropertyInfo isAdsCrosshairActive =
                controller.GetType().GetProperty("IsAdsCrosshairActive");
            PropertyInfo aimMode =
                controller.GetType().GetProperty("AimMode");
            Camera playerCamera =
                FindChildByName(player.transform, "MainCamera")
                    .GetComponent<Camera>();
            Transform weaponTransform = FindObjectWithComponent(
                GetSceneObjects(SceneManager.GetActiveScene()),
                "WeaponController").transform;
            Animator animator = player.GetComponentInChildren<Animator>();

            Assert.That(trySetAiming, Is.Not.Null);
            Assert.That(aimBlend, Is.Not.Null);
            Assert.That(isAdsCrosshairActive, Is.Not.Null);
            Assert.That(aimMode, Is.Not.Null);
            Assert.That(playerCamera, Is.Not.Null);
            Assert.That(weaponTransform, Is.Not.Null);
            Assert.That(animator, Is.Not.Null);
            aimMode.SetValue(
                controller,
                System.Enum.Parse(aimMode.PropertyType, "Toggle"));

            float hipFov = playerCamera.fieldOfView;
            Vector3 hipWeaponPosition = playerCamera.transform
                .InverseTransformPoint(weaponTransform.position);
            Vector2 hipLookDelta = (Vector2)calculateLookDelta.Invoke(
                controller,
                new object[] { new Vector2(10f, 0f), true, 0.5f });

            Assert.That(
                (bool)trySetAiming.Invoke(controller, new object[] { true }),
                Is.True);
            Assert.That(
                playerCamera.fieldOfView,
                Is.EqualTo(hipFov).Within(0.01f),
                "Entering ADS must start a transition instead of snapping.");

            yield return new WaitForSeconds(0.1f);

            Assert.That(
                playerCamera.fieldOfView,
                Is.LessThan(hipFov - 0.1f)
                    .And.GreaterThan(hipFov - 14.9f),
                "ADS FOV must be between the hip and target values mid-transition.");

            yield return new WaitForSeconds(0.2f);

            Assert.That((bool)isAiming.GetValue(controller), Is.True);
            Assert.That(
                (float)aimBlend.GetValue(controller),
                Is.GreaterThan(0.95f));
            Assert.That(playerCamera.fieldOfView, Is.LessThan(hipFov - 5f));
            Assert.That(
                Vector3.Distance(
                    playerCamera.transform.InverseTransformPoint(
                        weaponTransform.position),
                    hipWeaponPosition),
                Is.GreaterThan(0.001f));
            Assert.That(
                (bool)isAdsCrosshairActive.GetValue(controller),
                Is.True);
            Assert.That(
                animator.GetFloat("Aiming"),
                Is.GreaterThan(0.8f),
                "Animator Aiming must continue to drive the weapon pose.");

            Vector2 adsLookDelta = (Vector2)calculateLookDelta.Invoke(
                controller,
                new object[] { new Vector2(10f, 0f), true, 0.5f });
            Assert.That(
                adsLookDelta.x,
                Is.LessThan(hipLookDelta.x * 0.75f),
                "ADS must reduce look sensitivity.");

            trySetAiming.Invoke(controller, new object[] { false });
            yield return new WaitForSeconds(0.3f);

            Assert.That((bool)isAiming.GetValue(controller), Is.False);
            Assert.That(playerCamera.fieldOfView, Is.EqualTo(hipFov).Within(0.1f));
            Assert.That(
                (bool)isAdsCrosshairActive.GetValue(controller),
                Is.False);
            Assert.That(animator.GetFloat("Aiming"), Is.LessThan(0.2f));
        }

        [UnityTest]
        public IEnumerator SprintCancelsAimAndBlocksReentry()
        {
            yield return LoadCityNew();

            GameObject player = FindObjectWithComponent(
                GetSceneObjects(SceneManager.GetActiveScene()),
                "PlayerController");
            Component controller = player.GetComponent("PlayerController");
            MethodInfo trySetAiming =
                controller.GetType().GetMethod("TrySetAiming");
            PropertyInfo isAiming =
                controller.GetType().GetProperty("IsAiming");
            PropertyInfo isSprinting =
                controller.GetType().GetProperty("IsSprinting");
            PropertyInfo aimMode =
                controller.GetType().GetProperty("AimMode");
            MethodInfo applyAimingState = controller.GetType().GetMethod(
                "ApplyAimingState",
                BindingFlags.Instance | BindingFlags.NonPublic);

            aimMode.SetValue(
                controller,
                System.Enum.Parse(aimMode.PropertyType, "Toggle"));
            Assert.That(applyAimingState, Is.Not.Null);
            Assert.That(
                (bool)trySetAiming.Invoke(controller, new object[] { true }),
                Is.True);
            Assert.That((bool)isAiming.GetValue(controller), Is.True);

            Assert.That(
                (bool)applyAimingState.Invoke(
                    controller,
                    new object[] { true, true }),
                Is.False,
                "ADS cannot start while sprinting.");
            Assert.That(
                (bool)isAiming.GetValue(controller),
                Is.False,
                "Starting a sprint must cancel ADS.");
            Assert.That(
                (bool)isSprinting.GetValue(controller),
                Is.False,
                "The scene starts without sprint input; movement-state " +
                "simulation must not leak into live input.");
        }

        [UnityTest]
        public IEnumerator ToggleAimRespondsToTwoRightMouseClicks()
        {
            Mouse mouse = InputSystem.AddDevice<Mouse>();

            try
            {
                yield return LoadCityNew();

                GameObject player = FindObjectWithComponent(
                    GetSceneObjects(SceneManager.GetActiveScene()),
                    "PlayerController");
                Component controller =
                    player.GetComponent("PlayerController");
                PropertyInfo aimMode =
                    controller.GetType().GetProperty("AimMode");
                PropertyInfo isAiming =
                    controller.GetType().GetProperty("IsAiming");

                aimMode.SetValue(
                    controller,
                    System.Enum.Parse(aimMode.PropertyType, "Toggle"));

                Press(mouse.rightButton);
                yield return null;

                Assert.That(
                    (bool)isAiming.GetValue(controller),
                    Is.True,
                    "The first right click must enter Toggle ADS.");

                Release(mouse.rightButton);
                yield return null;
                Press(mouse.rightButton);
                yield return null;

                Assert.That(
                    (bool)isAiming.GetValue(controller),
                    Is.False,
                    "The second right click must exit Toggle ADS.");
            }
            finally
            {
                InputSystem.RemoveDevice(mouse);
            }
        }

        private static IEnumerator WaitForStanceTransition()
        {
            yield return new WaitForSeconds(0.25f);
        }

        private static IEnumerator LoadCityNew()
        {
            int buildIndex = SceneUtility.GetBuildIndexByScenePath(ScenePath);

            Assert.That(
                buildIndex,
                Is.GreaterThanOrEqualTo(0),
                $"{ScenePath} must be enabled in Build Settings.");

            yield return SceneManager.LoadSceneAsync(
                buildIndex,
                LoadSceneMode.Single);
            yield return null;
        }

        private static int CountComponentsByName(string componentName)
        {
            int count = 0;

            foreach (GameObject sceneObject in
                     GetSceneObjects(SceneManager.GetActiveScene()))
            {
                if (sceneObject.GetComponent(componentName) != null)
                {
                    count++;
                }
            }

            return count;
        }

        private static Transform FindChildByName(
            Transform root,
            string childName)
        {
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                if (child.name == childName)
                {
                    return child;
                }
            }

            return null;
        }

        private static List<GameObject> GetSceneObjects(Scene scene)
        {
            var results = new List<GameObject>();

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                {
                    results.Add(child.gameObject);
                }
            }

            return results;
        }

        private static GameObject FindObjectWithComponent(
            IEnumerable<GameObject> sceneObjects,
            string componentName)
        {
            foreach (GameObject sceneObject in sceneObjects)
            {
                if (sceneObject.GetComponent(componentName) != null)
                {
                    return sceneObject;
                }
            }

            return null;
        }

        private static string GetHierarchyPath(GameObject gameObject)
        {
            string path = gameObject.name;
            Transform parent = gameObject.transform.parent;

            while (parent != null)
            {
                path = $"{parent.name}/{path}";
                parent = parent.parent;
            }

            return path;
        }
    }
}
