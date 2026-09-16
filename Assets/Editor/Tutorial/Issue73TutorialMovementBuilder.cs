using System;
using System.Linq;
using FPS.Core.GameModes;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

public static class Issue73TutorialMovementBuilder
{
    [MenuItem("FPS/Content/Issue 73/Rebuild Movement Tutorial")]
    public static void Rebuild()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            throw new InvalidOperationException(
                "Exit Play Mode before rebuilding the movement tutorial.");
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
            "Issue #73 WASD 实际位移教学已接入教学场景。");
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
        TutorialMovementEvidenceTracker[] trackers =
            scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<
                    TutorialMovementEvidenceTracker>(true))
                .ToArray();
        TutorialMovementEvidenceTracker tracker;
        if (trackers.Length == 0)
        {
            tracker = flow.gameObject.AddComponent<
                TutorialMovementEvidenceTracker>();
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
