using System.Linq;
using FPS.Core.GameModes;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class Issue73TutorialMovementContentTests
{
    private const string DefinitionPath =
        "Assets/Resources/Tutorial/Definitions/DefaultTutorialSequence.asset";

    [Test]
    public void FirstFourStepsTeachWsadInRequiredOrder()
    {
        TutorialSequenceDefinition definition =
            AssetDatabase.LoadAssetAtPath<TutorialSequenceDefinition>(
                DefinitionPath);
        TutorialEvidenceType[] evidence = definition.Steps
            .Take(4)
            .Select(step => step.EvidenceType)
            .ToArray();

        Assert.That(evidence, Is.EqualTo(new[]
        {
            TutorialEvidenceType.MoveForwardDistance,
            TutorialEvidenceType.MoveBackwardDistance,
            TutorialEvidenceType.MoveLeftDistance,
            TutorialEvidenceType.MoveRightDistance
        }));
        Assert.That(definition.Steps.Take(4)
            .All(step => step.TargetValue == 1), Is.True);
        Assert.That(definition.Steps.Take(4)
            .Select(step => step.StableId), Is.EqualTo(new[]
        {
            "tutorial.move.forward",
            "tutorial.move.backward",
            "tutorial.move.left",
            "tutorial.move.right"
        }));
    }

    [Test]
    public void TutorialSceneHasOneTrackerBoundToFlowAndPlayer()
    {
        Scene scene = EditorSceneManager.OpenPreviewScene(
            GameModeScenePaths.Tutorial);
        try
        {
            TutorialMovementEvidenceTracker tracker = scene
                .GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<
                    TutorialMovementEvidenceTracker>(true))
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
