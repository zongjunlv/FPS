using System.Linq;
using FPS.Core.GameModes;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

public sealed class Issue77TutorialWeaponOperationContentTests
{
    private const string DefinitionPath =
        "Assets/Resources/Tutorial/Definitions/DefaultTutorialSequence.asset";

    [Test]
    public void WeaponStepsDescribeOrderedInputAndCompleteReload()
    {
        TutorialSequenceDefinition definition =
            AssetDatabase.LoadAssetAtPath<TutorialSequenceDefinition>(
                DefinitionPath);
        TutorialStepDefinition weaponSwitch = definition.Steps.Single(step =>
            step.EvidenceType == TutorialEvidenceType.WeaponSwitch);
        TutorialStepDefinition reload = definition.Steps.Single(step =>
            step.EvidenceType == TutorialEvidenceType.Reload);

        StringAssert.Contains("按 2", weaponSwitch.Instruction);
        StringAssert.Contains("滚轮", weaponSwitch.Instruction);
        StringAssert.Contains("R", reload.Instruction);
        StringAssert.Contains("完整", reload.Instruction);
        Assert.That(weaponSwitch.TargetValue, Is.EqualTo(2));
        Assert.That(reload.TargetValue, Is.EqualTo(1));
    }

    [Test]
    public void TutorialSceneOwnsOneValidatedWeaponOperationTracker()
    {
        Scene scene = EditorSceneManager.OpenPreviewScene(
            GameModeScenePaths.Tutorial);
        try
        {
            TutorialWeaponOperationEvidenceTracker tracker = scene
                .GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<
                    TutorialWeaponOperationEvidenceTracker>(true))
                .Single();

            Assert.That(tracker.TryValidate(out string error),
                Is.True, error);
            Assert.That(tracker.PlayerRig,
                Is.SameAs(tracker.Flow.Environment.PlayerRig));
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }
}
