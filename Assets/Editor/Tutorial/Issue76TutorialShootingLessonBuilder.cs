using System;
using System.Linq;
using FPS.Core.GameModes;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class Issue76TutorialShootingLessonBuilder
{
    [MenuItem("FPS/Content/Issue 76/Rebuild Shooting Lessons")]
    public static void Rebuild()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            throw new InvalidOperationException(
                "Exit Play Mode before rebuilding the shooting lessons.");
        }

        TutorialSequenceDefinition definition =
            Issue72TutorialContentBuilder.EnsureDefinition();
        Scene scene = EditorSceneManager.OpenScene(
            GameModeScenePaths.Tutorial,
            OpenSceneMode.Single);
        EnsureSceneContent(scene);
        EditorUtility.SetDirty(definition);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, GameModeScenePaths.Tutorial);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log(
            "Issue #76 腰射、ADS、半自动点射与自动连射教学已接入。");
    }

    public static TutorialShootingEvidenceTracker EnsureSceneContent(
        Scene scene)
    {
        TutorialFlowController flow = scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<
                TutorialFlowController>(true))
            .Single();
        TutorialTrainingEnvironment environment = flow.Environment;
        TutorialShootingEvidenceTracker[] trackers = scene
            .GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<
                TutorialShootingEvidenceTracker>(true))
            .ToArray();

        TutorialShootingEvidenceTracker tracker;
        if (trackers.Length == 0)
        {
            tracker = flow.gameObject.AddComponent<
                TutorialShootingEvidenceTracker>();
        }
        else
        {
            tracker = trackers[0];
            for (int index = 1; index < trackers.Length; index++)
            {
                UnityEngine.Object.DestroyImmediate(trackers[index]);
            }
        }

        tracker.Configure(
            flow,
            environment.ShootingTarget,
            environment.PlayerRig);
        if (!tracker.TryValidate(out string error))
        {
            throw new InvalidOperationException(error);
        }

        EditorUtility.SetDirty(tracker);
        return tracker;
    }
}
