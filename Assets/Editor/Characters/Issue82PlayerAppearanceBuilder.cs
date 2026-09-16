using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class Issue82PlayerAppearanceBuilder
{
    public const string Root =
        "Assets/Resources/Content/Characters/PlayerAppearances";
    public const string DefinitionsRoot = Root + "/Definitions";
    public const string HostPrefabPath =
        PlayerAppearanceCatalog.HostPrefabAssetPath;
    public const string PreviewScenePath =
        PlayerAppearanceCatalog.PreviewSceneAssetPath;

    private static readonly PlayerCollisionReference CollisionReference =
        new(1.8f, 0.4f, new Vector3(0f, 0.9f, 0f));
    private static readonly PlayerAppearanceLodConfiguration LodConfiguration =
        new(0.08f, LODFadeMode.CrossFade, 0.12f);

    [MenuItem("FPS/Content/Issue 82/Rebuild Player Appearance Data")]
    public static void Build()
    {
        EnsureFolder(Root);
        EnsureFolder(DefinitionsRoot);
        EnsureFolder("Assets/Scenes/CharacterCalibration");

        HumanoidCharacterStandard standard = AssetDatabase.LoadAssetAtPath<
            HumanoidCharacterStandard>(HumanoidCharacterStandard.DefaultAssetPath);
        string standardError = "Humanoid 标准资源不存在。";
        if (standard == null || !standard.TryValidate(out standardError))
        {
            throw new InvalidOperationException(
                $"Issue #81 Humanoid 标准不可用：{standardError}");
        }

        var definitions = new List<PlayerAppearanceDefinition>();
        foreach (HumanoidCharacterEntry entry in standard.Characters)
        {
            definitions.Add(BuildDefinition(entry, standard));
        }

        PlayerAppearanceCatalog catalog = BuildCatalog(definitions);
        BuildHostPrefab(catalog);
        BuildPreviewScene(catalog);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        if (!catalog.TryValidate(out string error))
            throw new InvalidOperationException($"Issue #82 构建失败：{error}");
        Debug.Log("Issue #82 玩家外观定义、工厂宿主与可旋转预览已生成。");
    }

    private static PlayerAppearanceDefinition BuildDefinition(
        HumanoidCharacterEntry entry, HumanoidCharacterStandard standard)
    {
        string fileName = entry.StableId.Replace('.', '_');
        string path = $"{DefinitionsRoot}/{fileName}.asset";
        PlayerAppearanceDefinition definition =
            AssetDatabase.LoadAssetAtPath<PlayerAppearanceDefinition>(path);
        if (definition == null)
        {
            definition = ScriptableObject.CreateInstance<
                PlayerAppearanceDefinition>();
            AssetDatabase.CreateAsset(definition, path);
        }

        Animator animator = entry.Prefab.GetComponent<Animator>();
        Material[] materials = entry.Prefab.GetComponentsInChildren<Renderer>(true)
            .SelectMany(renderer => renderer.sharedMaterials)
            .Where(material => material != null)
            .Distinct()
            .ToArray();
        definition.Configure(entry.StableId, entry.DisplayName, entry.Prefab,
            animator.avatar, standard.SharedAnimatorController, materials,
            CollisionReference, new Vector3(0f, 1.65f, 0f), LodConfiguration);
        EditorUtility.SetDirty(definition);
        return definition;
    }

    private static PlayerAppearanceCatalog BuildCatalog(
        IReadOnlyList<PlayerAppearanceDefinition> definitions)
    {
        PlayerAppearanceCatalog catalog =
            AssetDatabase.LoadAssetAtPath<PlayerAppearanceCatalog>(
                PlayerAppearanceCatalog.DefaultAssetPath);
        if (catalog == null)
        {
            catalog = ScriptableObject.CreateInstance<PlayerAppearanceCatalog>();
            AssetDatabase.CreateAsset(catalog,
                PlayerAppearanceCatalog.DefaultAssetPath);
        }
        catalog.Configure(definitions[0].StableId, definitions);
        EditorUtility.SetDirty(catalog);
        return catalog;
    }

    private static void BuildHostPrefab(PlayerAppearanceCatalog catalog)
    {
        var root = new GameObject("PlayerLogicRoot");
        try
        {
            CharacterController controller =
                root.AddComponent<CharacterController>();
            controller.height = CollisionReference.Height;
            controller.radius = CollisionReference.Radius;
            controller.center = CollisionReference.Center;

            var visualObject = new GameObject(PlayerAppearanceHost.VisualRootName);
            visualObject.transform.SetParent(root.transform, false);
            PlayerAppearanceHost host = root.AddComponent<PlayerAppearanceHost>();
            host.Configure(catalog, visualObject.transform,
                catalog.DefaultAppearanceId, false);
            host.Apply(catalog.DefaultAppearanceId);
            PrefabUtility.SaveAsPrefabAsset(root, HostPrefabPath);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    private static void BuildPreviewScene(PlayerAppearanceCatalog catalog)
    {
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,
            NewSceneMode.Single);
        GameObject root = new("Player Appearance Preview");
        var anchorObject = new GameObject("Preview Anchor");
        anchorObject.transform.SetParent(root.transform, false);

        PlayerAppearancePreviewController preview =
            root.AddComponent<PlayerAppearancePreviewController>();
        preview.Configure(catalog, anchorObject.transform);

        Camera camera = new GameObject("Preview Camera", typeof(Camera))
            .GetComponent<Camera>();
        camera.tag = "MainCamera";
        camera.fieldOfView = 38f;
        camera.transform.position = new Vector3(0f, 1.05f, 4.2f);
        camera.transform.LookAt(new Vector3(0f, 0.9f, 0f));
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.035f, 0.045f, 0.065f);

        CreateLight("Key Light", new Vector3(42f, 150f, 0f), 1.25f,
            new Color(1f, 0.91f, 0.8f));
        CreateLight("Fill Light", new Vector3(30f, -55f, 0f), 0.7f,
            new Color(0.45f, 0.65f, 1f));
        GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.name = "Preview Floor";
        floor.transform.localScale = new Vector3(0.45f, 1f, 0.45f);
        floor.GetComponent<Renderer>().sharedMaterial = CreatePreviewMaterial();

        if (!EditorSceneManager.SaveScene(scene, PreviewScenePath))
            throw new InvalidOperationException("无法保存玩家外观预览场景。");
    }

    private static void CreateLight(string name, Vector3 euler,
        float intensity, Color color)
    {
        Light light = new GameObject(name, typeof(Light)).GetComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = intensity;
        light.color = color;
        light.transform.rotation = Quaternion.Euler(euler);
    }

    private static Material CreatePreviewMaterial()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit") ??
                        Shader.Find("Standard");
        var material = new Material(shader) { name = "Preview Floor Material" };
        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", new Color(0.08f, 0.1f, 0.14f));
        if (material.HasProperty("_Color"))
            material.SetColor("_Color", new Color(0.08f, 0.1f, 0.14f));
        return material;
    }

    private static void EnsureFolder(string path)
    {
        string current = "Assets";
        string[] segments = path.Split('/');
        for (int index = 1; index < segments.Length; index++)
        {
            string next = current + "/" + segments[index];
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, segments[index]);
            current = next;
        }
    }
}
