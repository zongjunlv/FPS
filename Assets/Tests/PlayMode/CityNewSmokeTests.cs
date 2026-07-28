using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace FPS.Tests.PlayMode
{
    public class CityNewSmokeTests
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
