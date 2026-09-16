using System.Linq;
using FPS.Core.GameModes;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class Issue74TutorialLocomotionContentTests
{
    private const string DefinitionPath =
        "Assets/Resources/Tutorial/Definitions/DefaultTutorialSequence.asset";

    [Test]
    public void JumpSprintAndCrouchDeclareFullStateTargets()
    {
        TutorialSequenceDefinition definition =
            AssetDatabase.LoadAssetAtPath<TutorialSequenceDefinition>(
                DefinitionPath);
        TutorialStepDefinition[] steps = definition.Steps.Skip(4).Take(3)
            .ToArray();

        Assert.That(definition.Version, Is.GreaterThanOrEqualTo(3));
        Assert.That(steps.Select(step => step.EvidenceType), Is.EqualTo(new[]
        {
            TutorialEvidenceType.Jump,
            TutorialEvidenceType.Sprint,
            TutorialEvidenceType.Crouch
        }));
        Assert.That(steps.Select(step => step.TargetValue),
            Is.EqualTo(new[] { 1, 2, 2 }));
        StringAssert.Contains("腾空", steps[0].Instruction);
        StringAssert.Contains("2 米", steps[1].Instruction);
        StringAssert.Contains("恢复站立", steps[2].Instruction);
    }

    [Test]
    public void SceneHasOneLocomotionTrackerBoundToPlayer()
    {
        Scene scene = EditorSceneManager.OpenPreviewScene(
            GameModeScenePaths.Tutorial);
        try
        {
            TutorialLocomotionEvidenceTracker tracker = scene
                .GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<
                    TutorialLocomotionEvidenceTracker>(true))
                .Single();
            TutorialFlowController flow = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<
                    TutorialFlowController>(true))
                .Single();
            PlayerController player = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<
                    PlayerController>(true))
                .Single();

            Assert.That(tracker.Flow, Is.SameAs(flow));
            Assert.That(tracker.Player, Is.SameAs(player));
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }
}
