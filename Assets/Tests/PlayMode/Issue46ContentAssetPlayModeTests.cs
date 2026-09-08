using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace FPS.Tests.PlayMode
{
    public sealed class Issue46ContentAssetPlayModeTests
    {
        private const string CityNewScene =
            "Assets/ImportPackages/CSAssets2026/Scenes/CityNew.unity";

        [UnityTest]
        public IEnumerator CityNewRuntimeUsesTheFormalContentCatalog()
        {
            LogAssert.ignoreFailingMessages = true;
            yield return SceneManager.LoadSceneAsync(
                CityNewScene,
                LoadSceneMode.Single);

            CityNewWaveBootstrap waves = null;
            CityNewInventoryBootstrap inventory = null;
            PlayerUpgradeController upgrades = null;
            float deadline = Time.realtimeSinceStartup + 25f;

            while (Time.realtimeSinceStartup < deadline)
            {
                waves = Object.FindAnyObjectByType<CityNewWaveBootstrap>();
                inventory =
                    Object.FindAnyObjectByType<CityNewInventoryBootstrap>();
                upgrades = Object.FindAnyObjectByType<PlayerUpgradeController>();
                if (waves?.ContentCatalog != null &&
                    waves.Director != null && waves.Director.IsRunning &&
                    inventory?.Definitions?.Length == 4 &&
                    upgrades?.AvailableUpgrades?.Count == 13)
                {
                    break;
                }

                yield return null;
            }

            CityNewContentCatalog catalog =
                CityNewContentCatalog.LoadDefault();
            Assert.That(waves, Is.Not.Null);
            Assert.That(waves.ConfigurationError, Is.Empty);
            Assert.That(waves.ContentCatalog, Is.SameAs(catalog));
            Assert.That(waves.Director.CurrentProgress.TotalWaves,
                Is.EqualTo(catalog.WaveSequence.WaveCount));
            Assert.That(inventory.Definitions,
                Is.EqualTo(catalog.Items));
            Assert.That(upgrades.AvailableUpgrades,
                Is.EqualTo(catalog.Upgrades));

            WaveEnemyLifecycle activeEnemy =
                Object.FindObjectsByType<WaveEnemyLifecycle>(
                        FindObjectsInactive.Exclude,
                        FindObjectsSortMode.None)
                    .FirstOrDefault(lifecycle => lifecycle.IsArmed);
            Assert.That(activeEnemy, Is.Not.Null);
            Assert.That(catalog.EnemyArchetypes.Any(archetype =>
                    archetype.EnemyTypeId == activeEnemy.EnemyTypeId),
                Is.True);
        }
    }
}
