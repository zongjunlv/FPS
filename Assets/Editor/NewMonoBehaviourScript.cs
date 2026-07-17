using UnityEditor;
using UnityEngine;

public static class MissingScriptCleaner
{
    [MenuItem("GameObject/Remove Missing Scripts From Selection")]
    private static void RemoveMissingScripts()
    {
        int removedCount = 0;

        foreach (GameObject root in Selection.gameObjects)
        {
            Transform[] objects =
                root.GetComponentsInChildren<Transform>(true);

            foreach (Transform item in objects)
            {
                GameObject target = item.gameObject;

                Undo.RegisterCompleteObjectUndo(
                    target,
                    "Remove Missing Scripts"
                );

                int count =
                    GameObjectUtility
                        .RemoveMonoBehavioursWithMissingScript(target);

                if (count > 0)
                {
                    removedCount += count;
                    EditorUtility.SetDirty(target);
                }
            }
        }

        Debug.Log($"Removed {removedCount} missing script(s).");
    }
}