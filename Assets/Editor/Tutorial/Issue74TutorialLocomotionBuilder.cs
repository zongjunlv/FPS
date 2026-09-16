using System;
using System.Linq;
using FPS.Core.GameModes;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

public static class Issue74TutorialLocomotionBuilder
{
    [MenuItem("FPS/Content/Issue 74/Rebuild Locomotion Tutorial")]
    public static void Rebuild()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            throw new InvalidOperationException(
                "Exit Play Mode before rebuilding the locomotion tutorial.");
        }

        Scene scene = EditorSceneManager.OpenScene(
            GameModeScenePaths.Tutorial,
            OpenSceneMode.Single);
        EnsureSceneContent(scene);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, GameModeScenePaths.Tutorial);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        UnityEngine.Debug.Log(
            "Issue #74 跳跃、冲刺与下蹲教学已接入教学场景。");
    }

    public static void EnsureSceneContent(Scene scene)
    {
        TutorialFlowController flow = scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<
                TutorialFlowController>(true))
            .Single();
        PlayerController player = scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<
                PlayerController>(true))
            .Single();
        TutorialLocomotionEvidenceTracker[] trackers =
            scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<
                    TutorialLocomotionEvidenceTracker>(true))
                .ToArray();
        TutorialLocomotionEvidenceTracker tracker;
        if (trackers.Length == 0)
        {
            tracker = flow.gameObject.AddComponent<
                TutorialLocomotionEvidenceTracker>();
        }
        else
        {
            tracker = trackers[0];
            for (int index = 1; index < trackers.Length; index++)
            {
                UnityEngine.Object.DestroyImmediate(trackers[index]);
            }
        }

        tracker.Configure(flow, player);
        EditorUtility.SetDirty(tracker);
    }
}
