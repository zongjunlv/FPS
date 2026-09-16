using System;
using System.Linq;
using FPS.Core.GameModes;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class Issue77TutorialWeaponOperationBuilder
{
    [MenuItem("FPS/Content/Issue 77/Rebuild Weapon Operation Lessons")]
    public static void Rebuild()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            throw new InvalidOperationException(
                "Exit Play Mode before rebuilding weapon operation lessons.");
        }

        Issue72TutorialContentBuilder.EnsureDefinition();
        Scene scene = EditorSceneManager.OpenScene(
            GameModeScenePaths.Tutorial,
            OpenSceneMode.Single);
        EnsureSceneContent(scene);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, GameModeScenePaths.Tutorial);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("Issue #77 数字键、滚轮切枪与完整换弹教学已接入。");
    }

    public static TutorialWeaponOperationEvidenceTracker EnsureSceneContent(
        Scene scene)
    {
        TutorialFlowController flow = scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<
                TutorialFlowController>(true))
            .Single();
        TutorialWeaponOperationEvidenceTracker[] trackers = scene
            .GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<
                TutorialWeaponOperationEvidenceTracker>(true))
            .ToArray();

        TutorialWeaponOperationEvidenceTracker tracker;
        if (trackers.Length == 0)
        {
            tracker = flow.gameObject.AddComponent<
                TutorialWeaponOperationEvidenceTracker>();
        }
        else
        {
            tracker = trackers[0];
            for (int index = 1; index < trackers.Length; index++)
            {
                UnityEngine.Object.DestroyImmediate(trackers[index]);
            }
        }

        tracker.Configure(flow, flow.Environment.PlayerRig);
        if (!tracker.TryValidate(out string error))
        {
            throw new InvalidOperationException(error);
        }

        EditorUtility.SetDirty(tracker);
        return tracker;
    }
}
