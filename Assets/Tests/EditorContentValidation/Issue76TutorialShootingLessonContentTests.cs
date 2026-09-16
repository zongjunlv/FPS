using System.Linq;
using FPS.Core.GameModes;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class Issue76TutorialShootingLessonContentTests
{
    private const string DefinitionPath =
        "Assets/Resources/Tutorial/Definitions/DefaultTutorialSequence.asset";

    [Test]
    public void ShootingStepsDistinguishHipAdsSemiAndAutomaticLessons()
    {
        TutorialSequenceDefinition definition =
            AssetDatabase.LoadAssetAtPath<TutorialSequenceDefinition>(
                DefinitionPath);

        TutorialStepDefinition hip = FindStep(
            definition,
            TutorialEvidenceType.HipFireHit);
        TutorialStepDefinition ads = FindStep(
            definition,
            TutorialEvidenceType.AimFireHit);
        TutorialStepDefinition semi = FindStep(
            definition,
            TutorialEvidenceType.SemiAutomaticShot);
        TutorialStepDefinition burst = FindStep(
            definition,
            TutorialEvidenceType.AutomaticBurst);

        StringAssert.Contains("腰射", hip.Instruction);
        StringAssert.Contains("ADS", ads.Instruction);
        StringAssert.Contains("半自动手枪", semi.Instruction);
        StringAssert.Contains("自动步枪", burst.Instruction);
        Assert.That(hip.TargetValue, Is.EqualTo(3));
        Assert.That(ads.TargetValue, Is.EqualTo(3));
        Assert.That(semi.TargetValue, Is.EqualTo(3));
        Assert.That(burst.TargetValue, Is.EqualTo(4));
    }

    [Test]
    public void TutorialSceneOwnsOneValidatedShootingEvidenceTracker()
    {
        Scene scene = EditorSceneManager.OpenPreviewScene(
            GameModeScenePaths.Tutorial);
        try
        {
            TutorialShootingEvidenceTracker tracker = scene
                .GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<
                    TutorialShootingEvidenceTracker>(true))
                .Single();

            Assert.That(tracker.TryValidate(out string error),
                Is.True, error);
            Assert.That(tracker.Target,
                Is.SameAs(tracker.Flow.Environment.ShootingTarget));
            Assert.That(tracker.PlayerRig,
                Is.SameAs(tracker.Flow.Environment.PlayerRig));
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    private static TutorialStepDefinition FindStep(
        TutorialSequenceDefinition definition,
        TutorialEvidenceType evidence)
    {
        Assert.That(definition, Is.Not.Null);
        return definition.Steps.Single(step => step.EvidenceType == evidence);
    }
}
