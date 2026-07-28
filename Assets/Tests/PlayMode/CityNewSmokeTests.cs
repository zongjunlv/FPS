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
