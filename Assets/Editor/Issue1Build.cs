using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class Issue1Build
{
    private const string ScenePath =
        "Assets/ImportPackages/CSAssets2026/Scenes/CityNew.unity";
    private const string TerrainDataPath =
        "Assets/ImportPackages/CSAssets2026/LowPolyBuildings/New Terrain.asset";

    public static void SanitizeLegacyTerrainDetails()
    {
        TerrainData terrainData =
            AssetDatabase.LoadAssetAtPath<TerrainData>(TerrainDataPath);

        if (terrainData == null)
        {
            throw new InvalidOperationException(
                $"TerrainData not found: {TerrainDataPath}");
        }

        int prototypeCount = terrainData.detailPrototypes.Length;

        if (prototypeCount == 0)
        {
            Debug.Log("Legacy TerrainData has no detail prototypes to sanitize.");
            return;
        }

        terrainData.detailPrototypes = Array.Empty<DetailPrototype>();
        EditorUtility.SetDirty(terrainData);
        AssetDatabase.SaveAssets();

        Debug.Log(
            $"Removed {prototypeCount} incompatible detail prototype(s) " +
            $"from {TerrainDataPath}. Heightmap and terrain layers were preserved.");
    }

    public static void CleanCityNewCompatibilityComponents()
    {
        Scene scene = EditorSceneManager.OpenScene(
            ScenePath,
            OpenSceneMode.Single);
        int obsoleteLookControllers = 0;

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (ThirdPersonLookController controller in
                     root.GetComponentsInChildren<ThirdPersonLookController>(true))
            {
                UnityEngine.Object.DestroyImmediate(controller);
                obsoleteLookControllers++;
            }
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        Debug.Log(
            $"CityNew compatibility cleanup removed " +
            $"{obsoleteLookControllers} obsolete look component(s).");
    }

    public static void BuildMacDevelopment()
    {
        string projectRoot = Directory.GetParent(Application.dataPath)!.FullName;
        string outputPath = Path.Combine(
            projectRoot,
            "Builds",
            "Issue1",
            "FPS.app");

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        var options = new BuildPlayerOptions
        {
            scenes = EditorBuildSettings.scenes
                .Where(scene => scene.enabled)
                .Select(scene => scene.path)
                .ToArray(),
            locationPathName = outputPath,
            target = BuildTarget.StandaloneOSX,
            options = BuildOptions.Development
        };

        BuildReport report = BuildPipeline.BuildPlayer(options);

        if (report.summary.result != BuildResult.Succeeded)
        {
            throw new InvalidOperationException(
                $"Issue #1 development build failed: {report.summary.result}");
        }

        Debug.Log(
            $"Issue #1 development build succeeded: {outputPath} " +
            $"({report.summary.totalSize} bytes)");
    }
}
