using System;
using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class Issue47AddressableEnemyBuilder
{
    public const string PrefabPath = "Assets/AddressableAssets/Enemies/Spider.prefab";
    public const string Address = "enemy/spider";
    private const string ScenePath = "Assets/ImportPackages/CSAssets2026/Scenes/CityNew.unity";

    [MenuItem("FPS/Content/Create Addressable Enemy Assets")]
    public static void Build()
    {
        EnsureFolder("Assets", "AddressableAssets");
        EnsureFolder("Assets/AddressableAssets", "Enemies");
        // Never overwrite an existing tuned prefab or save the user's scene.
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null)
        {
            var scene = EditorSceneManager.OpenPreviewScene(ScenePath);
            GameObject clone = null;
            try
            {
                EnemyController source = scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<EnemyController>(true))
                    .FirstOrDefault(enemy => enemy.name == "SPIDER_BOT");
                if (source == null)
                    throw new InvalidOperationException("CityNew SPIDER_BOT template is missing.");
                clone = UnityEngine.Object.Instantiate(source.gameObject);
                clone.name = "Spider";
                if (PrefabUtility.IsPartOfPrefabInstance(clone))
                    PrefabUtility.UnpackPrefabInstance(clone,
                        PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                clone.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                clone.SetActive(true);
                if (PrefabUtility.SaveAsPrefabAsset(clone, PrefabPath) == null)
                    throw new InvalidOperationException("Could not save addressable enemy prefab.");
            }
            finally
            {
                if (clone != null) UnityEngine.Object.DestroyImmediate(clone);
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        var settings = AddressableAssetSettingsDefaultObject.GetSettings(true);
        var group = settings.FindGroup("Local Enemies") ?? settings.CreateGroup(
            "Local Enemies", false, false, true, null,
            typeof(BundledAssetGroupSchema), typeof(ContentUpdateGroupSchema));
        var schema = group.GetSchema<BundledAssetGroupSchema>();
        schema.BuildPath.SetVariableByName(settings, AddressableAssetSettings.kLocalBuildPath);
        schema.LoadPath.SetVariableByName(settings, AddressableAssetSettings.kLocalLoadPath);
        settings.CreateOrMoveEntry(AssetDatabase.AssetPathToGUID(PrefabPath), group).address = Address;
        settings.BuildAddressablesWithPlayerBuild =
            AddressableAssetSettings.PlayerBuildOption.BuildWithPlayer;
        EditorUtility.SetDirty(settings);
        EditorUtility.SetDirty(group);
        AssetDatabase.SaveAssets();
        Debug.Log("Issue47: registered enemy/spider with local bundled content.");
    }

    private static void EnsureFolder(string parent, string child)
    {
        if (!AssetDatabase.IsValidFolder(parent + "/" + child))
            AssetDatabase.CreateFolder(parent, child);
    }
}
