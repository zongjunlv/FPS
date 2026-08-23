using System.Collections;
using System.Collections.Generic;
using FPS.GameplayEffects;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace FPS.Tests.PlayMode
{
    public sealed class Issue31VitalityEffectTests
    {
        private const string CityNewScene =
            "Assets/ImportPackages/CSAssets2026/Scenes/CityNew.unity";

        [UnityTest]
        public IEnumerator VitalityCardAppliesStacksRemovesAndEndsCleanly()
        {
            LogAssert.ignoreFailingMessages = true;
            UpgradeDefinition definition = null;

            try
            {
                yield return SceneManager.LoadSceneAsync(
                    CityNewScene,
                    LoadSceneMode.Single);

                for (int frame = 0; frame < 6; frame++)
                {
                    yield return null;
                }

                PlayerUpgradeController controller =
                    Object.FindFirstObjectByType<PlayerUpgradeController>();
                Health health = controller.GetComponent<Health>();
                UnifiedGameHud hud = Object.FindFirstObjectByType<UnifiedGameHud>();
                definition = ScriptableObject.CreateInstance<UpgradeDefinition>();
                definition.Configure(
                    "issue31_vitality",
                    "生命强化",
                    "每层使最大生命值提高 20%。",
                    null,
                    UpgradeRarity.Common,
                    3,
                    UpgradeEffectType.MaximumHealth,
                    0.2f);
                controller.ConfigureRun(
                    31031,
                    new List<UpgradeDefinition> { definition });
                health.Initialize(100f, 0f);
                health.ApplyDamage(new DamageInfo(
                    20f,
                    Vector3.zero,
                    Vector3.forward,
                    controller.gameObject));

                yield return SelectOne(controller);
                Assert.That(health.MaxHealth, Is.EqualTo(120f).Within(0.001f));
                Assert.That(health.CurrentHealth, Is.EqualTo(100f).Within(0.001f));
                Assert.That(controller.ActiveGameplayEffectCount, Is.EqualTo(1));
                Assert.That(hud.HealthText, Does.Contain("100 / 120"));

                yield return SelectOne(controller);
                Assert.That(health.MaxHealth, Is.EqualTo(140f).Within(0.001f));
                Assert.That(health.CurrentHealth, Is.EqualTo(120f).Within(0.001f));
                Assert.That(controller.ActiveGameplayEffectCount, Is.EqualTo(2));
                GameplayEffectInstance newest =
                    controller.ActiveGameplayEffects[1];
                Assert.That(newest.Context.SourceId,
                    Is.EqualTo("issue31_vitality"));
                Assert.That(newest.Context.Source, Is.SameAs(definition));
                Assert.That(newest.Context.Target,
                    Is.SameAs(controller.gameObject));

                Assert.That(
                    controller.RemoveGameplayEffect(newest.InstanceId),
                    Is.True);
                Assert.That(health.MaxHealth, Is.EqualTo(120f).Within(0.001f));
                Assert.That(health.CurrentHealth, Is.EqualTo(100f).Within(0.001f),
                    "移除一层时应保留20点已损失生命。 ");
                Assert.That(controller.ActiveGameplayEffectCount, Is.EqualTo(1));

                controller.EndRun();
                Assert.That(controller.ActiveGameplayEffectCount, Is.Zero);
                Assert.That(health.MaxHealth, Is.EqualTo(100f).Within(0.001f));
                Assert.That(health.CurrentHealth, Is.EqualTo(80f).Within(0.001f),
                    "结算清理后应回到基础上限并保留已损失生命。 ");
            }
            finally
            {
                Time.timeScale = 1f;
                LogAssert.ignoreFailingMessages = false;

                if (definition != null)
                {
                    Object.DestroyImmediate(definition);
                }
            }
        }

        private static IEnumerator SelectOne(
            PlayerUpgradeController controller)
        {
            controller.QueueUpgradeChoices(1);
            yield return null;
            Assert.That(controller.IsChoiceOpen, Is.True);
            Assert.That(controller.TrySelect(0), Is.True);
            yield return null;
        }
    }
}
