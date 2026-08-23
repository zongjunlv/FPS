using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace FPS.Tests.PlayMode
{
    public sealed class Issue28CompositionRootTests
    {
        private const string CityNewScene =
            "Assets/ImportPackages/CSAssets2026/Scenes/CityNew.unity";

        [UnityTest]
        public IEnumerator CityNewCompositionRootBuildsPlayableCombatSlice()
        {
            yield return SceneManager.LoadSceneAsync(
                CityNewScene,
                LoadSceneMode.Single);
            yield return null;
            yield return null;

            PlayerCombatCompositionRoot root =
                Object.FindFirstObjectByType<PlayerCombatCompositionRoot>();

            Assert.That(root, Is.Not.Null);
            Assert.That(root.HasConfigurationError, Is.False,
                root.ConfigurationError);
            Assert.That(root.IsInitialized, Is.True);
            Assert.That(root.RuntimeStats, Is.Not.Null);
            Assert.That(root.PlayerHealth, Is.Not.Null);
            Assert.That(root.CombatFeedback, Is.Not.Null);
            Assert.That(root.CombatFeedback.IsInitialized, Is.True);
            Assert.That(root.HudBootstrap, Is.Not.Null);
            Assert.That(root.GetComponent<PlayerCombatController>()
                .IsInitialized, Is.True);
            Assert.That(
                root.GetComponents<PlayerRuntimeCombatStats>().Length,
                Is.EqualTo(1));
            Assert.That(
                root.GetComponents<PlayerCombatFeedbackController>().Length,
                Is.EqualTo(1));
        }

        [Test]
        public void MissingRequiredDependenciesStopsInitializationSafely()
        {
            GameObject host = new GameObject("Incomplete Combat Root");

            try
            {
                LogAssert.Expect(
                    LogType.Error,
                    new System.Text.RegularExpressions.Regex(
                        "PlayerCombatCompositionRoot.*Missing required " +
                        "dependencies.*Initialization stopped safely"));

                PlayerCombatCompositionRoot root =
                    host.AddComponent<PlayerCombatCompositionRoot>();

                Assert.That(root.HasConfigurationError, Is.True);
                Assert.That(root.ConfigurationError,
                    Does.Contain(nameof(PlayerInputReader)));
                Assert.That(root.IsInitialized, Is.False);
                Assert.That(root.enabled, Is.False);
                Assert.That(
                    host.GetComponent<PlayerRuntimeCombatStats>(),
                    Is.Null,
                    "配置不完整时不应创建半初始化的运行时服务。");
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }
    }
}
