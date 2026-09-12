using System.Collections;
using System.Linq;
using FPS.Determinism;
using FPS.GameplayEffects;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace FPS.Tests.PlayMode
{
    public sealed class Issue59CombatRuleRuntimeTests
    {
        private const string CityNewScene =
            "Assets/ImportPackages/CSAssets2026/Scenes/CityNew.unity";

        [SetUp]
        public void IgnoreHeadlessPresentationWarnings()
        {
            LogAssert.ignoreFailingMessages = true;
        }

        [TearDown]
        public void RestoreLogState()
        {
            LogAssert.ignoreFailingMessages = false;
        }

        [UnityTest]
        public IEnumerator BurningKillSpreadsToNearbyEnemiesAndShowsHudCue()
        {
            yield return SceneManager.LoadSceneAsync(
                CityNewScene,
                LoadSceneMode.Single);
            yield return null;

            PlayerCombatCompositionRoot root =
                Object.FindFirstObjectByType<PlayerCombatCompositionRoot>();
            Assert.That(root, Is.Not.Null);
            Assert.That(root.CombatBuilds, Is.Not.Null);
            Assert.That(root.CombatBuilds.IsInitialized, Is.True);
            Assert.That(root.CombatBuilds.ActiveRuleCount, Is.EqualTo(2));
            Assert.That(root.CombatBuilds.InstalledBuildIds,
                Does.Contain("build.ember_chain"));
            yield return new WaitUntil(() =>
                root.HudBootstrap != null &&
                root.HudBootstrap.IsInitialized);

            EnemyController origin = CreateEnemy(
                "Issue59 Burning Origin",
                new Vector3(1000f, 1000f, 1000f));
            EnemyController nearA = CreateEnemy(
                "Issue59 Near A",
                new Vector3(1003f, 1000f, 1000f));
            EnemyController nearB = CreateEnemy(
                "Issue59 Near B",
                new Vector3(997f, 1000f, 1000f));
            EnemyController far = CreateEnemy(
                "Issue59 Far",
                new Vector3(1020f, 1000f, 1000f));

            try
            {
                Synchronize(origin, nearA, nearB, far);
                root.CombatBuilds.PublishHit(
                    origin.gameObject,
                    new DamageResult(
                        true,
                        false,
                        10f,
                        HitRegion.Body),
                    origin.transform.position,
                    DamageType.Hitscan);
                EnemyBurnEffectController originBurn =
                    origin.GetComponent<EnemyBurnEffectController>();
                Assert.That(originBurn.IsBurning, Is.True,
                    "命中规则应先给目标施加燃烧。" );

                Health originHealth = origin.GetComponent<Health>();
                DamageInfo killingDamage = new(
                    originHealth.CurrentHealth + originHealth.CurrentArmor,
                    origin.transform.position,
                    Vector3.forward,
                    root.gameObject,
                    DamageType.Hitscan);
                originHealth.ApplyDamage(killingDamage);
                Assert.That(originBurn.WasBurningWhenKilled, Is.True);
                Assert.That(root.CombatEvents.TryPublishEnemyDeath(
                    new EnemyDeathEvent(
                        origin,
                        5901,
                        1,
                        killingDamage,
                        10)),
                    Is.True);

                Assert.That(root.CombatBuilds.LastSpreadCount,
                    Is.EqualTo(2));
                Assert.That(nearA.GetComponent<EnemyBurnEffectController>()
                    .IsBurning, Is.True);
                Assert.That(nearB.GetComponent<EnemyBurnEffectController>()
                    .IsBurning, Is.True);
                Assert.That(far.GetComponent<EnemyBurnEffectController>()
                    .IsBurning, Is.False);

                UnifiedGameHud hud =
                    Object.FindFirstObjectByType<UnifiedGameHud>();
                Assert.That(hud, Is.Not.Null);
                Assert.That(hud.RewardCueCount, Is.GreaterThan(0));
                Assert.That(hud.RewardCueText, Does.Contain("余烬扩散"));
                Assert.That(hud.RewardCueText, Does.Contain("2"));

                CombatRuleRuntimeSnapshot runtimeState =
                    root.CombatBuilds.CaptureSnapshot();
                Assert.That(runtimeState.InstalledBuildIds,
                    Does.Contain("build.ember_chain"));
                Assert.That(runtimeState.ProcessedEventIds,
                    Is.Not.Empty);

                RunReplayRuntimeAdapter replayAdapter =
                    root.GetComponent<RunReplayRuntimeAdapter>() ??
                    root.gameObject.AddComponent<RunReplayRuntimeAdapter>();
                replayAdapter.Configure(
                    root.GetComponent<PlayerInputReader>(),
                    WaveDirector.Active);
                ReplayStateSnapshot replay = replayAdapter.CaptureState();
                Assert.That(replay.Fields.Any(field =>
                    field.Path == "combatBuild/installed/0" &&
                    field.Value == "build.ember_chain"), Is.True);
                Assert.That(replay.Fields.Any(field =>
                    field.Path == "combatBuild/events/count" &&
                    field.Value != "0"), Is.True);
            }
            finally
            {
                Destroy(origin);
                Destroy(nearA);
                Destroy(nearB);
                Destroy(far);
            }
        }

        private static EnemyController CreateEnemy(
            string objectName,
            Vector3 position)
        {
            var enemy = new GameObject(objectName);
            enemy.transform.position = position;
            enemy.AddComponent<CapsuleCollider>();
            return enemy.AddComponent<EnemyController>();
        }

        private static void Synchronize(params EnemyController[] enemies)
        {
            EnemySpatialIndexService index =
                EnemySpatialIndexService.EnsureForActiveScene();
            for (int item = 0; item < enemies.Length; item++)
            {
                index.Synchronize(
                    enemies[item]
                        .GetComponent<EnemyPerceptionController>());
            }
        }

        private static void Destroy(EnemyController enemy)
        {
            if (enemy != null)
            {
                Object.DestroyImmediate(enemy.gameObject);
            }
        }
    }
}
