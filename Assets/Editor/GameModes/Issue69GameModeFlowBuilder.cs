using System.Collections.Generic;
using System.Linq;
using FPS.Core.GameModes;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class Issue69GameModeFlowBuilder
{
    public const string CatalogPath =
        "Assets/Resources/GameModes/GameModeCatalog.asset";

    [MenuItem("FPS/Content/Issue 69/Rebuild Game Mode Flow")]
    public static void Rebuild()
    {
        EnsureFolder("Assets/Resources/GameModes");
        EnsureFolder("Assets/Scenes/Modes");

        GameModeCatalog catalog = BuildCatalog();
        BuildFlowScene(
            GameModeScenePaths.Entry,
            "模式入口",
            GameModeId.None,
            GameModeStage.Entry,
            catalog);
        BuildFlowScene(
            GameModeScenePaths.Tutorial,
            "新手教学入口",
            GameModeId.Tutorial,
            GameModeStage.Tutorial,
            catalog);
        BuildFlowScene(
            GameModeScenePaths.BattlePreparation,
            "战斗准备入口",
            GameModeId.SoloBattle,
            GameModeStage.BattlePreparation,
            catalog);
        BuildFlowScene(
            GameModeScenePaths.CoopLogin,
            "多人合作登录入口",
            GameModeId.Coop,
            GameModeStage.CoopLogin,
            catalog);
        MarkCityNewBattleScene();
        ConfigureBuildScenes();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        EditorSceneManager.OpenScene(
            GameModeScenePaths.Entry,
            OpenSceneMode.Single);
        Debug.Log(
            "Issue #69 模式入口已生成：教学、单人战斗和多人合作均使用稳定模式 ID 路由。");
    }

    private static GameModeCatalog BuildCatalog()
    {
        GameModeCatalog catalog =
            AssetDatabase.LoadAssetAtPath<GameModeCatalog>(CatalogPath);
        if (catalog == null)
        {
            catalog = ScriptableObject.CreateInstance<GameModeCatalog>();
            AssetDatabase.CreateAsset(catalog, CatalogPath);
        }

        catalog.Configure(
            GameModeScenePaths.Entry,
            new[]
            {
                Definition(
                    GameModeId.Tutorial,
                    "新手教学",
                    "在无敌人环境中学习移动、射击和伤害判定。",
                    GameModeScenePaths.Tutorial,
                    GameModeStage.Tutorial),
                Definition(
                    GameModeId.SoloBattle,
                    "战斗模式",
                    "进入角色选择与战局准备，再开始完整肉鸽战斗。",
                    GameModeScenePaths.BattlePreparation,
                    GameModeStage.BattlePreparation),
                Definition(
                    GameModeId.Coop,
                    "多人合作",
                    "先登录账号，再进入双人合作大厅和联机战局。",
                    GameModeScenePaths.CoopLogin,
                    GameModeStage.CoopLogin)
            });
        EditorUtility.SetDirty(catalog);
        return catalog;
    }

    private static GameModeDefinition Definition(
        GameModeId mode,
        string displayName,
        string description,
        string scenePath,
        GameModeStage stage)
    {
        var definition = new GameModeDefinition();
        definition.Configure(
            mode,
            displayName,
            description,
            scenePath,
            stage);
        return definition;
    }

    private static void BuildFlowScene(
        string path,
        string rootName,
        GameModeId mode,
        GameModeStage stage,
        GameModeCatalog catalog)
    {
        Scene scene = EditorSceneManager.NewScene(
            NewSceneSetup.EmptyScene,
            NewSceneMode.Single);
        var root = new GameObject(rootName);
        GameModeSceneMarker marker =
            root.AddComponent<GameModeSceneMarker>();
        marker.Configure(mode, stage);
        GameModeSceneBootstrap bootstrap =
            root.AddComponent<GameModeSceneBootstrap>();
        bootstrap.Configure(catalog, marker);

        var cameraObject = new GameObject("Menu Camera");
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.018f, 0.035f, 0.05f, 1f);
        camera.orthographic = true;
        cameraObject.tag = "MainCamera";

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, path);
    }

    private static void MarkCityNewBattleScene()
    {
        Scene scene = EditorSceneManager.OpenScene(
            GameModeScenePaths.CityNew,
            OpenSceneMode.Single);
        GameModeSceneMarker marker = scene.GetRootGameObjects()
            .SelectMany(root =>
                root.GetComponentsInChildren<GameModeSceneMarker>(true))
            .FirstOrDefault();
        if (marker == null)
        {
            var root = new GameObject("Game Mode Context");
            marker = root.AddComponent<GameModeSceneMarker>();
        }

        marker.Configure(GameModeId.SoloBattle, GameModeStage.Battle);
        EditorUtility.SetDirty(marker);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    private static void ConfigureBuildScenes()
    {
        string[] required =
        {
            GameModeScenePaths.Entry,
            GameModeScenePaths.Tutorial,
            GameModeScenePaths.BattlePreparation,
            GameModeScenePaths.CoopLogin,
            GameModeScenePaths.CityNew
        };
        var scenes = required
            .Select(path => new EditorBuildSettingsScene(path, true))
            .ToList();
        HashSet<string> included = new(required);
        foreach (EditorBuildSettingsScene existing in
                 EditorBuildSettings.scenes)
        {
            if (included.Add(existing.path))
            {
                scenes.Add(existing);
            }
        }

        EditorBuildSettings.scenes = scenes.ToArray();
    }

    private static void EnsureFolder(string path)
    {
        string current = "Assets";
        string[] segments = path.Split('/');
        for (int index = 1; index < segments.Length; index++)
        {
            string next = current + "/" + segments[index];
            if (!AssetDatabase.IsValidFolder(next))
            {
                AssetDatabase.CreateFolder(current, segments[index]);
            }

            current = next;
        }
    }
}
