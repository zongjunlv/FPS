using System.Linq;
using FPS.Core.GameModes;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

public sealed class Issue78TutorialDamageTargetContentTests
{
    [Test]
    public void DamageTargetHasConfiguredBodyAndHeadWithoutEnemySystems()
    {
        Scene scene = EditorSceneManager.OpenPreviewScene(
            GameModeScenePaths.Tutorial);
        try
        {
            TutorialDamageTrainingTarget target = scene
                .GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<
                    TutorialDamageTrainingTarget>(true))
                .Single();

            Assert.That(target.TryValidate(out string error),
                Is.True, error);
            Assert.That(target.PresentationRoot.activeSelf, Is.False);
            Assert.That(target.BodyHitbox.Region, Is.EqualTo(HitRegion.Body));
            Assert.That(target.HeadHitbox.Region, Is.EqualTo(HitRegion.Head));
            Assert.That(target.BodyHitbox.DamageMultiplier, Is.EqualTo(1f));
            Assert.That(target.HeadHitbox.DamageMultiplier, Is.EqualTo(2f));
            Assert.That(target.GetComponentInChildren<EnemyController>(true),
                Is.Null);
            Assert.That(target.GetComponentInChildren<EnemyCombatController>(
                true), Is.Null);
            Assert.That(target.GetComponentInChildren<
                EnemyPerceptionController>(true), Is.Null);
            Assert.That(target.GetComponentInChildren<NavMeshAgent>(true),
                Is.Null);
            Assert.That(target.GetComponentInChildren<WaveEnemyLifecycle>(true),
                Is.Null);
            Assert.That(target.GetComponentInChildren<
                EnemyDeathEffectController>(true), Is.Null);
            Assert.That(target.GetComponentInChildren<
                PlayerLootRewardController>(true), Is.Null);
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    [Test]
    public void FinalSequenceStepsExplainSameWeaponDamageComparison()
    {
        TutorialSequenceDefinition definition =
            AssetDatabase.LoadAssetAtPath<TutorialSequenceDefinition>(
                "Assets/Resources/Tutorial/Definitions/" +
                "DefaultTutorialSequence.asset");
        TutorialStepDefinition body = definition.Steps.Single(step =>
            step.EvidenceType == TutorialEvidenceType.BodyHit);
        TutorialStepDefinition head = definition.Steps.Single(step =>
            step.EvidenceType == TutorialEvidenceType.HeadHit);

        StringAssert.Contains("基础伤害", body.Instruction);
        StringAssert.Contains("倍率", body.Instruction);
        StringAssert.Contains("同一把", head.Instruction);
        StringAssert.Contains("最终伤害", head.Instruction);
    }
}
