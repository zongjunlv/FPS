using System;
using System.Linq;
using FPS.Core.GameModes;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;

public static class Issue75TutorialShootingWallBuilder
{
    private const string TargetRootName = "Tutorial Shooting Target";
    private const string HitRegionName = "Teaching Hit Region";
    private const string MaterialFolder =
        "Assets/Resources/Tutorial/Materials";
    private const float TargetWidth = 5f;
    private const float TargetHeight = 4f;

    [MenuItem("FPS/Content/Issue 75/Rebuild Tutorial Shooting Wall")]
    public static void Rebuild()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            throw new InvalidOperationException(
                "Exit Play Mode before rebuilding the tutorial shooting wall.");
        }

        Scene scene = EditorSceneManager.OpenScene(
            GameModeScenePaths.Tutorial,
            OpenSceneMode.Single);
        TutorialTrainingEnvironment environment = scene
            .GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<
                TutorialTrainingEnvironment>(true))
            .Single();
        TutorialShootingTarget target = EnsureSceneContent(
            scene,
            environment);
        environment.ConfigureShootingTarget(target);
        EditorUtility.SetDirty(environment);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, GameModeScenePaths.Tutorial);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log(
            "Issue #75 教学射击墙已生成：命中区域、中心区、散布参考线与墙面附着反馈均已接入。");
    }

    public static TutorialShootingTarget EnsureSceneContent(
        Scene scene,
        TutorialTrainingEnvironment environment)
    {
        if (!scene.IsValid() || environment == null ||
            environment.PlayerRig == null ||
            environment.ShootingWall == null)
        {
            throw new InvalidOperationException(
                "Tutorial environment must be configured before building the shooting wall.");
        }

        Collider wall = environment.ShootingWall;
        PlayerCombatController combat = environment.PlayerRig.Combat;
        Transform root = EnsureTargetRoot(wall, combat.transform.position);
        BoxCollider hitRegion = EnsureHitRegion(root);
        Material accent = EnsureMaterial(
            $"{MaterialFolder}/TutorialTargetAccent.mat",
            new Color(0.08f, 0.94f, 0.82f, 1f));
        Material center = EnsureMaterial(
            $"{MaterialFolder}/TutorialTargetCenter.mat",
            new Color(1f, 0.68f, 0.16f, 1f));

        EnsureFrame(root, accent);
        EnsureCrosshair(root, accent);
        EnsureRing(root, "Spread Ring Inner", 0.8f, 0.045f, accent);
        EnsureRing(root, "Spread Ring Outer", 1.6f, 0.03f, accent);
        EnsureCenter(root, center);

        TutorialShootingTarget target =
            root.GetComponent<TutorialShootingTarget>();
        if (target == null)
        {
            target = root.gameObject.AddComponent<
                TutorialShootingTarget>();
        }
        target.Configure(combat, wall, hitRegion);

        SurfaceDescriptor surface =
            wall.GetComponent<SurfaceDescriptor>();
        if (surface == null)
        {
            surface = wall.gameObject.AddComponent<SurfaceDescriptor>();
        }
        surface.Configure(SurfaceType.Concrete);

        EditorUtility.SetDirty(target);
        EditorUtility.SetDirty(surface);
        EditorUtility.SetDirty(hitRegion);
        return target;
    }

    private static Transform EnsureTargetRoot(
        Collider wall,
        Vector3 playerPosition)
    {
        Transform root = wall.transform.Find(TargetRootName);
        if (root == null)
        {
            root = new GameObject(TargetRootName).transform;
        }

        Vector3 towardPlayer = Vector3.ProjectOnPlane(
            playerPosition - wall.bounds.center,
            Vector3.up);
        if (towardPlayer.sqrMagnitude < 0.001f)
        {
            towardPlayer = -wall.transform.forward;
        }
        towardPlayer.Normalize();
        Vector3 wallFace = wall.ClosestPoint(
            wall.bounds.center + towardPlayer * 100f);
        root.SetPositionAndRotation(
            wallFace + towardPlayer * 0.012f,
            Quaternion.LookRotation(towardPlayer, Vector3.up));
        root.SetParent(wall.transform, true);
        root.gameObject.layer = LayerMask.NameToLayer("Ignore Raycast");
        return root;
    }

    private static BoxCollider EnsureHitRegion(Transform root)
    {
        Transform child = root.Find(HitRegionName);
        GameObject regionObject = child != null
            ? child.gameObject
            : new GameObject(HitRegionName);
        regionObject.transform.SetParent(root, false);
        regionObject.transform.SetLocalPositionAndRotation(
            new Vector3(0f, 0f, -0.012f),
            Quaternion.identity);
        regionObject.transform.localScale = Vector3.one;
        regionObject.layer = LayerMask.NameToLayer("Ignore Raycast");
        BoxCollider region = regionObject.GetComponent<BoxCollider>();
        if (region == null)
        {
            region = regionObject.AddComponent<BoxCollider>();
        }
        region.center = Vector3.zero;
        region.size = new Vector3(TargetWidth, TargetHeight, 0.12f);
        region.isTrigger = true;
        return region;
    }

    private static void EnsureFrame(Transform root, Material material)
    {
        const float thickness = 0.055f;
        EnsureVisualCube(
            root,
            "Target Frame Top",
            new Vector3(0f, TargetHeight * 0.5f, 0.004f),
            new Vector3(TargetWidth, thickness, 0.025f),
            material);
        EnsureVisualCube(
            root,
            "Target Frame Bottom",
            new Vector3(0f, TargetHeight * -0.5f, 0.004f),
            new Vector3(TargetWidth, thickness, 0.025f),
            material);
        EnsureVisualCube(
            root,
            "Target Frame Left",
            new Vector3(TargetWidth * -0.5f, 0f, 0.004f),
            new Vector3(thickness, TargetHeight, 0.025f),
            material);
        EnsureVisualCube(
            root,
            "Target Frame Right",
            new Vector3(TargetWidth * 0.5f, 0f, 0.004f),
            new Vector3(thickness, TargetHeight, 0.025f),
            material);
    }

    private static void EnsureCrosshair(
        Transform root,
        Material material)
    {
        EnsureVisualCube(
            root,
            "Spread Guide Horizontal",
            new Vector3(0f, 0f, 0.005f),
            new Vector3(4.5f, 0.035f, 0.025f),
            material);
        EnsureVisualCube(
            root,
            "Spread Guide Vertical",
            new Vector3(0f, 0f, 0.005f),
            new Vector3(0.035f, 3.5f, 0.025f),
            material);
    }

    private static void EnsureRing(
        Transform root,
        string name,
        float radius,
        float width,
        Material material)
    {
        Transform child = root.Find(name);
        GameObject ringObject = child != null
            ? child.gameObject
            : new GameObject(name);
        ringObject.transform.SetParent(root, false);
        ringObject.transform.SetLocalPositionAndRotation(
            new Vector3(0f, 0f, 0.008f),
            Quaternion.identity);
        ringObject.transform.localScale = Vector3.one;
        ringObject.layer = LayerMask.NameToLayer("Ignore Raycast");
        LineRenderer line = ringObject.GetComponent<LineRenderer>();
        if (line == null)
        {
            line = ringObject.AddComponent<LineRenderer>();
        }
        line.useWorldSpace = false;
        line.loop = true;
        line.positionCount = 64;
        line.startWidth = width;
        line.endWidth = width;
        line.sharedMaterial = material;
        line.startColor = material.color;
        line.endColor = material.color;
        line.shadowCastingMode = ShadowCastingMode.Off;
        line.receiveShadows = false;
        for (int index = 0; index < line.positionCount; index++)
        {
            float angle = index * Mathf.PI * 2f / line.positionCount;
            line.SetPosition(
                index,
                new Vector3(
                    Mathf.Cos(angle) * radius,
                    Mathf.Sin(angle) * radius,
                    0f));
        }
        EditorUtility.SetDirty(line);
    }

    private static void EnsureCenter(
        Transform root,
        Material material)
    {
        const string name = "Target Center Zone";
        Transform child = root.Find(name);
        GameObject center = child != null
            ? child.gameObject
            : GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        center.name = name;
        center.transform.SetParent(root, false);
        center.transform.SetLocalPositionAndRotation(
            new Vector3(0f, 0f, 0.012f),
            Quaternion.Euler(90f, 0f, 0f));
        center.transform.localScale = new Vector3(0.24f, 0.012f, 0.24f);
        center.layer = LayerMask.NameToLayer("Ignore Raycast");
        RemoveCollider(center);
        ApplyMaterial(center, material);
    }

    private static void EnsureVisualCube(
        Transform root,
        string name,
        Vector3 localPosition,
        Vector3 localScale,
        Material material)
    {
        Transform child = root.Find(name);
        GameObject visual = child != null
            ? child.gameObject
            : GameObject.CreatePrimitive(PrimitiveType.Cube);
        visual.name = name;
        visual.transform.SetParent(root, false);
        visual.transform.SetLocalPositionAndRotation(
            localPosition,
            Quaternion.identity);
        visual.transform.localScale = localScale;
        visual.layer = LayerMask.NameToLayer("Ignore Raycast");
        RemoveCollider(visual);
        ApplyMaterial(visual, material);
    }

    private static void RemoveCollider(GameObject target)
    {
        Collider collider = target.GetComponent<Collider>();
        if (collider != null)
        {
            UnityEngine.Object.DestroyImmediate(collider);
        }
    }

    private static void ApplyMaterial(
        GameObject target,
        Material material)
    {
        Renderer renderer = target.GetComponent<Renderer>();
        if (renderer == null)
        {
            return;
        }

        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        EditorUtility.SetDirty(renderer);
    }

    private static Material EnsureMaterial(string path, Color color)
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            Shader shader = Shader.Find(
                "Universal Render Pipeline/Unlit") ??
                Shader.Find("Sprites/Default") ??
                Shader.Find("Standard");
            material = new Material(shader)
            {
                name = System.IO.Path.GetFileNameWithoutExtension(path)
            };
            AssetDatabase.CreateAsset(material, path);
        }

        material.color = color;
        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", color);
        }
        EditorUtility.SetDirty(material);
        return material;
    }
}
