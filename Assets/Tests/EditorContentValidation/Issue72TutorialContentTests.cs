using System.Linq;
using FPS.Core.GameModes;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class Issue72TutorialContentTests
{
    private const string DefinitionPath =
        "Assets/Resources/Tutorial/Definitions/DefaultTutorialSequence.asset";

    [Test]
    public void DefaultSequenceHasStableOrderedChineseSteps()
    {
        TutorialSequenceDefinition definition =
            AssetDatabase.LoadAssetAtPath<TutorialSequenceDefinition>(
                DefinitionPath);

        Assert.That(definition, Is.Not.Null);
        Assert.That(definition.TryValidate(out string error), Is.True, error);
        Assert.That(definition.Steps.Count, Is.EqualTo(15));
        Assert.That(definition.Steps.Select(step => step.StableId).Distinct()
            .Count(), Is.EqualTo(definition.Steps.Count));
        Assert.That(definition.Steps.All(step =>
            ContainsChinese(step.Chapter) &&
            ContainsChinese(step.Title) &&
            ContainsChinese(step.Instruction)), Is.True);
        Assert.That(definition.Steps.All(step => step.TargetValue > 0), Is.True);
    }

    [Test]
    public void TutorialSceneOwnsOneConfiguredFlowController()
    {
        Scene scene = EditorSceneManager.OpenPreviewScene(
            GameModeScenePaths.Tutorial);
        try
        {
            TutorialFlowController controller = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<
                    TutorialFlowController>(true))
                .Single();
            TutorialTrainingEnvironment environment = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<
                    TutorialTrainingEnvironment>(true))
                .Single();

            Assert.That(controller.Definition, Is.Not.Null);
            Assert.That(controller.Definition.TryValidate(out string error),
                Is.True,
                error);
            Assert.That(controller.Environment, Is.SameAs(environment));
            Assert.That(scene.GetRootGameObjects().SelectMany(root =>
                root.GetComponentsInChildren<TutorialTopHud>(true)), Is.Empty);
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    private static bool ContainsChinese(string value)
    {
        return !string.IsNullOrEmpty(value) && value.Any(character =>
            character is >= '\u4e00' and <= '\u9fff');
    }
}
