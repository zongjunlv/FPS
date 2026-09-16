using System;
using System.Linq;
using FPS.Core.GameModes;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class Issue71TutorialSceneBuilder
{
    public const string RigPrefabPath =
        "Assets/Resources/Player/PlayerGameplayRig.prefab";
    private const string MaterialFolder =
        "Assets/Resources/Tutorial/Materials";

    private static readonly Vector3 SpawnPosition =
        new(0f, 0.16f, -14f);
    private static readonly Bounds ArenaBounds =
        new(Vector3.up * 2f, new Vector3(28f, 8f, 40f));

    [MenuItem("FPS/Content/Issue 71/Rebuild Tutorial Training Scene")]
    public static void Rebuild()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            throw new InvalidOperationException(
                "Exit Play Mode before rebuilding the tutorial scene.");
        }

        RebuildScene();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log(
            "Issue #71 教学训练场已生成：统一玩家 Rig、封闭场地、射击墙与跌落恢复区均已就绪。");
    }

    public static void RebuildScene()
    {
        EnsureFolder(MaterialFolder);
        Scene scene = EditorSceneManager.OpenScene(
            GameModeScenePaths.Tutorial,
            OpenSceneMode.Single);

        GameModeSceneMarker marker = EnsureModeMarker(scene);
        RemoveMenuPresentation(scene, marker);

        Material floorMaterial = EnsureMaterial(
            "TutorialFloor",
            new Color(0.12f, 0.16f, 0.18f, 1f),
            0.08f,
            0.62f);
        Material boundaryMaterial = EnsureMaterial(
            "TutorialBoundary",
            new Color(0.045f, 0.08f, 0.1f, 1f),
            0.25f,
            0.48f);
        Material laneMaterial = EnsureMaterial(
            "TutorialLane",
            new Color(0.04f, 0.34f, 0.34f, 1f),
            0.12f,
            0.52f);
        Material targetMaterial = EnsureMaterial(
            "TutorialTargetWall",
            new Color(0.34f, 0.16f, 0.08f, 1f),
            0.05f,
            0.7f);

        Transform environmentRoot = EnsureRoot(scene, "Tutorial Environment");
        TutorialTrainingEnvironment environment =
            environmentRoot.GetComponent<TutorialTrainingEnvironment>() ??
            environmentRoot.gameObject.AddComponent<
                TutorialTrainingEnvironment>();

        PlayerGameplayRig rig = EnsurePlayer(scene);
        Transform spawnPoint = EnsureTransform(
            environmentRoot,
            "Safe Spawn",
            SpawnPosition,
            Quaternion.identity);
        BoxCollider floor = EnsureCube(
            environmentRoot,
            "Safe Training Floor",
            new Vector3(0f, -0.5f, 0f),
            new Vector3(28f, 1f, 40f),
            floorMaterial,
            true);

        EnsureVisualPad(
            environmentRoot,
            "Movement Training Zone",
            new Vector3(0f, 0.015f, -8f),
            new Vector3(10f, 0.03f, 10f),
            laneMaterial);
        EnsureVisualPad(
            environmentRoot,
            "Shooting Lane",
            new Vector3(0f, 0.02f, 3f),
            new Vector3(16f, 0.04f, 14f),
            targetMaterial);

        BoxCollider shootingWall = EnsureCube(
            environmentRoot,
            "Tutorial Shooting Wall",
            new Vector3(0f, 3f, 11f),
            new Vector3(16f, 6f, 0.6f),
            targetMaterial,
            true);

        BoxCollider[] boundaries =
        {
            EnsureCube(
                environmentRoot,
                "Boundary Left",
                new Vector3(-14.5f, 2f, 0f),
                new Vector3(1f, 4f, 41f),
                boundaryMaterial,
                true),
            EnsureCube(
                environmentRoot,
                "Boundary Right",
                new Vector3(14.5f, 2f, 0f),
                new Vector3(1f, 4f, 41f),
                boundaryMaterial,
                true),
            EnsureCube(
                environmentRoot,
                "Boundary Back",
                new Vector3(0f, 2f, -20.5f),
                new Vector3(28f, 4f, 1f),
                boundaryMaterial,
                true),
            EnsureCube(
                environmentRoot,
                "Boundary Front",
                new Vector3(0f, 2f, 20.5f),
                new Vector3(28f, 4f, 1f),
                boundaryMaterial,
                true)
        };

        GameObject damagePad = EnsurePrimitive(
            environmentRoot,
            "Damage Training Station",
            PrimitiveType.Cylinder);
        damagePad.transform.SetLocalPositionAndRotation(
            new Vector3(10f, 0.1f, 9f),
            Quaternion.identity);
        damagePad.transform.localScale = new Vector3(2.25f, 0.1f, 2.25f);
        SetMaterial(damagePad, laneMaterial);
        Transform damageTrainingPoint = EnsureTransform(
            environmentRoot,
            "Damage Training Point",
            new Vector3(10f, 0.16f, 9f),
            Quaternion.identity);

        BoxCollider recoveryTrigger = EnsureCube(
            environmentRoot,
            "Fall Recovery Volume",
            new Vector3(0f, -4.5f, 0f),
            new Vector3(60f, 2f, 70f),
            null,
            false);
        recoveryTrigger.isTrigger = true;
        TutorialSafetyResetVolume recovery =
            recoveryTrigger.GetComponent<TutorialSafetyResetVolume>() ??
            recoveryTrigger.gameObject.AddComponent<
                TutorialSafetyResetVolume>();
        recovery.Configure(spawnPoint);

        EnsureLighting(scene);
        environment.Configure(
            rig,
            spawnPoint,
            floor,
            shootingWall,
            damageTrainingPoint,
            boundaries,
            recovery,
            ArenaBounds);
        Issue72TutorialContentBuilder.EnsureSceneContent(scene, environment);
        Issue73TutorialMovementBuilder.EnsureSceneContent(scene);
        Issue74TutorialLocomotionBuilder.EnsureSceneContent(scene);

        EditorUtility.SetDirty(marker);
        EditorUtility.SetDirty(environment);
        EditorUtility.SetDirty(recovery);
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(
                scene,
                GameModeScenePaths.Tutorial))
        {
            throw new InvalidOperationException(
                "Could not save the tutorial training scene.");
        }
    }

    private static GameModeSceneMarker EnsureModeMarker(Scene scene)
    {
        GameModeSceneMarker[] markers = scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<
                GameModeSceneMarker>(true))
            .ToArray();
        if (markers.Length > 1)
        {
            throw new InvalidOperationException(
                "Tutorial scene contains more than one game mode marker.");
        }

        GameModeSceneMarker marker;
        if (markers.Length == 1)
        {
            marker = markers[0];
            marker.gameObject.name = "Tutorial Mode Context";
        }
        else
        {
            var markerObject = new GameObject("Tutorial Mode Context");
            SceneManager.MoveGameObjectToScene(markerObject, scene);
            marker = markerObject.AddComponent<GameModeSceneMarker>();
        }

        marker.Configure(GameModeId.Tutorial, GameModeStage.Tutorial);
        return marker;
    }

    private static void RemoveMenuPresentation(
        Scene scene,
        GameModeSceneMarker marker)
    {
        foreach (GameModeSceneBootstrap bootstrap in
                 scene.GetRootGameObjects()
                     .SelectMany(root => root.GetComponentsInChildren<
                         GameModeSceneBootstrap>(true))
                     .ToArray())
        {
            Object.DestroyImmediate(bootstrap);
        }

        foreach (Camera camera in scene.GetRootGameObjects()
                     .SelectMany(root => root.GetComponentsInChildren<
                         Camera>(true))
                     .Where(camera => camera.name == "Menu Camera")
                     .ToArray())
        {
            Object.DestroyImmediate(camera.gameObject);
        }

        marker.transform.SetParent(null);
    }

    private static PlayerGameplayRig EnsurePlayer(Scene scene)
    {
        PlayerGameplayRig[] rigs = scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<
                PlayerGameplayRig>(true))
            .ToArray();
        if (rigs.Length > 1)
        {
            throw new InvalidOperationException(
                "Tutorial scene contains more than one player rig.");
        }

        PlayerGameplayRig rig;
        if (rigs.Length == 1)
        {
            rig = rigs[0];
        }
        else
        {
            GameObject prefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(RigPrefabPath);
            if (prefab == null)
            {
                throw new InvalidOperationException(
                    "Reusable player gameplay rig is missing.");
            }

            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(
                prefab,
                scene);
            rig = instance.GetComponent<PlayerGameplayRig>();
        }

        rig.gameObject.name = "Player";
        rig.transform.SetPositionAndRotation(
            SpawnPosition,
            Quaternion.identity);
        rig.transform.localScale = Vector3.one;
        rig.gameObject.SetActive(true);
        return rig;
    }

    private static void EnsureLighting(Scene scene)
    {
        Light light = scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<Light>(true))
            .FirstOrDefault(value => value.type == LightType.Directional);
        if (light == null)
        {
            var lightObject = new GameObject(
                "Tutorial Directional Light",
                typeof(Light));
            SceneManager.MoveGameObjectToScene(lightObject, scene);
            light = lightObject.GetComponent<Light>();
        }

        light.type = LightType.Directional;
        light.color = new Color(0.82f, 0.9f, 1f, 1f);
        light.intensity = 1.25f;
        light.shadows = LightShadows.Soft;
        light.transform.rotation = Quaternion.Euler(48f, -32f, 0f);
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.22f, 0.27f, 0.31f, 1f);
    }

    private static Transform EnsureRoot(Scene scene, string name)
    {
        Transform existing = scene.GetRootGameObjects()
            .FirstOrDefault(root => root.name == name)?.transform;
        if (existing != null)
        {
            existing.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            existing.localScale = Vector3.one;
            return existing;
        }

        var root = new GameObject(name);
        SceneManager.MoveGameObjectToScene(root, scene);
        return root.transform;
    }

    private static Transform EnsureTransform(
        Transform parent,
        string name,
        Vector3 position,
        Quaternion rotation,
        bool worldSpace = true)
    {
        Transform transform = parent.Find(name);
        if (transform == null)
        {
            transform = new GameObject(name).transform;
            transform.SetParent(parent, false);
        }

        if (worldSpace)
        {
            transform.SetPositionAndRotation(position, rotation);
        }
        else
        {
            transform.SetLocalPositionAndRotation(position, rotation);
        }

        transform.localScale = Vector3.one;
        return transform;
    }

    private static BoxCollider EnsureCube(
        Transform parent,
        string name,
        Vector3 localPosition,
        Vector3 localScale,
        Material material,
        bool visible)
    {
        GameObject cube = EnsurePrimitive(parent, name, PrimitiveType.Cube);
        cube.transform.SetLocalPositionAndRotation(
            localPosition,
            Quaternion.identity);
        cube.transform.localScale = localScale;
        BoxCollider collider = cube.GetComponent<BoxCollider>();
        collider.enabled = true;
        collider.isTrigger = false;
        MeshRenderer renderer = cube.GetComponent<MeshRenderer>();
        renderer.enabled = visible;
        if (material != null)
        {
            renderer.sharedMaterial = material;
        }

        return collider;
    }

    private static void EnsureVisualPad(
        Transform parent,
        string name,
        Vector3 localPosition,
        Vector3 localScale,
        Material material)
    {
        GameObject pad = EnsurePrimitive(parent, name, PrimitiveType.Cube);
        pad.transform.SetLocalPositionAndRotation(
            localPosition,
            Quaternion.identity);
        pad.transform.localScale = localScale;
        Collider collider = pad.GetComponent<Collider>();
        if (collider != null)
        {
            Object.DestroyImmediate(collider);
        }

        SetMaterial(pad, material);
    }

    private static GameObject EnsurePrimitive(
        Transform parent,
        string name,
        PrimitiveType type)
    {
        Transform existing = parent.Find(name);
        GameObject result = existing != null
            ? existing.gameObject
            : GameObject.CreatePrimitive(type);
        result.name = name;
        result.transform.SetParent(parent, false);
        return result;
    }

    private static void SetMaterial(GameObject target, Material material)
    {
        MeshRenderer renderer = target.GetComponent<MeshRenderer>();
        if (renderer != null)
        {
            renderer.sharedMaterial = material;
        }
    }

    private static Material EnsureMaterial(
        string name,
        Color color,
        float metallic,
        float smoothness)
    {
        string path = $"{MaterialFolder}/{name}.mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        Shader shader =
            Shader.Find("Universal Render Pipeline/Lit") ??
            Shader.Find("Standard");
        if (shader == null)
        {
            throw new InvalidOperationException(
                "No supported lit shader is available for tutorial materials.");
        }

        if (material == null)
        {
            material = new Material(shader) { name = name };
            AssetDatabase.CreateAsset(material, path);
        }
        else if (material.shader != shader)
        {
            material.shader = shader;
        }

        material.color = color;
        if (material.HasProperty("_Metallic"))
        {
            material.SetFloat("_Metallic", metallic);
        }

        if (material.HasProperty("_Glossiness"))
        {
            material.SetFloat("_Glossiness", smoothness);
        }

        EditorUtility.SetDirty(material);
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
            {
                AssetDatabase.CreateFolder(current, segments[index]);
            }

            current = next;
        }
    }
}
