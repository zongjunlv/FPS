using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace FPS.Tests.PlayMode
{
    public sealed class Issue68ReusablePlayerRigTests
    {
        private const string CityNewScene =
            "Assets/ImportPackages/CSAssets2026/Scenes/CityNew.unity";

        [UnityTest]
        public IEnumerator StandaloneRigInitializesPlayableSliceInEmptyScene()
        {
            Scene emptyScene = SceneManager.CreateScene(
                "Issue68 Empty Runtime Scene");
            SceneManager.SetActiveScene(emptyScene);

            for (int index = SceneManager.sceneCount - 1; index >= 0; index--)
            {
                Scene loadedScene = SceneManager.GetSceneAt(index);

                if (loadedScene != emptyScene)
                {
                    yield return SceneManager.UnloadSceneAsync(loadedScene);
                }
            }

            PlayerGameplayRig prefab = PlayerGameplayRig.LoadPrefab();
            Assert.That(prefab, Is.Not.Null);
            PlayerGameplayRig rig = PlayerGameplayRig.Create(
                Vector3.zero,
                Quaternion.identity);

            yield return null;
            yield return null;
            yield return null;

            Assert.That(rig.TryValidate(out string error), Is.True, error);
            Assert.That(rig.CompositionRoot.HasConfigurationError, Is.False,
                rig.CompositionRoot.ConfigurationError);
            Assert.That(rig.CompositionRoot.IsInitialized, Is.True);
            Assert.That(rig.Combat.IsInitialized, Is.True);
            Assert.That(rig.CompositionRoot.RuntimeStats, Is.Not.Null);
            Assert.That(rig.CompositionRoot.PlayerHealth, Is.Not.Null);
            Assert.That(rig.CompositionRoot.HudBootstrap, Is.Not.Null);
            Assert.That(rig.GetComponent<PlayerInventoryController>(), Is.Not.Null);
            Assert.That(rig.GetComponent<GameplayLockCoordinator>(), Is.Not.Null);
            Assert.That(rig.GetComponent<CityNewTerminalMissionBootstrap>(), Is.Null);
            Assert.That(rig.GetComponent<CityNewInventoryBootstrap>(), Is.Null);
            Assert.That(Object.FindObjectsByType<UnifiedGameHud>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None), Has.Length.EqualTo(1));
            Assert.That(rig.Weapons.Count, Is.EqualTo(2));

            yield return CleanupRuntimeObjects(rig.gameObject);
        }

        [UnityTest]
        public IEnumerator CityNewUsesOneReusableRigAndPreservesRuntimeState()
        {
            yield return SceneManager.LoadSceneAsync(
                CityNewScene,
                LoadSceneMode.Single);
            yield return null;
            yield return null;
            yield return null;

            PlayerGameplayRig[] rigs = Object.FindObjectsByType<
                PlayerGameplayRig>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            Assert.That(rigs, Has.Length.EqualTo(1));
            PlayerGameplayRig rig = rigs[0];
            Assert.That(rig.TryValidate(out string error), Is.True, error);
            Assert.That(rig.CompositionRoot.IsInitialized, Is.True);
            Assert.That(rig.Loadout.WeaponCount, Is.EqualTo(2));
            Assert.That(rig.Loadout.CurrentIndex, Is.EqualTo(0));
            Assert.That(rig.Loadout.CurrentWeapon.name, Is.EqualTo("AR"));
            Assert.That(rig.GetComponent<PlayerInventoryController>(), Is.Not.Null);
            Assert.That(rig.GetComponent<CityNewTerminalMissionBootstrap>(), Is.Not.Null);
            Assert.That(rig.GetComponent<CityNewInventoryBootstrap>(), Is.Not.Null);
            Assert.That(Object.FindObjectsByType<UnifiedGameHud>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None), Has.Length.EqualTo(1));
            Assert.That(rig.CompositionRoot.PlayerHealth.CurrentHealth,
                Is.EqualTo(100f));
            Assert.That(rig.CompositionRoot.PlayerHealth.CurrentArmor,
                Is.EqualTo(100f));
        }

        private static IEnumerator CleanupRuntimeObjects(GameObject rig)
        {
            foreach (UnifiedGameHud hud in Object.FindObjectsByType<
                         UnifiedGameHud>(
                         FindObjectsInactive.Include,
                         FindObjectsSortMode.None))
            {
                Object.Destroy(hud.gameObject);
            }

            foreach (EventSystem eventSystem in Object.FindObjectsByType<
                         EventSystem>(
                         FindObjectsInactive.Include,
                         FindObjectsSortMode.None))
            {
                Object.Destroy(eventSystem.gameObject);
            }

            Object.Destroy(rig);
            yield return null;
        }
    }
}
