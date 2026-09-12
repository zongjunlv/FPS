using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.AI;

public static class Issue66EnemyModelBuilder
{
    private const string ThirdPartyRoot =
        "Assets/ThirdParty/Quaternius/SciFiEssentials";
    private const string ModelRoot = ThirdPartyRoot + "/Models";
    private const string TextureRoot = ThirdPartyRoot + "/Textures";
    private const string GeneratedRoot =
        "Assets/Generated/EnemyVisuals/QuaterniusSciFi";
    private const string PrefabRoot = "Assets/AddressableAssets/Enemies";
    private const string SpiderPrefabPath = PrefabRoot + "/Spider.prefab";
    private const string EnemyDefinitionPath =
        "Assets/DefineAssets/Enemy/Spider.asset";

    private sealed class VisualSpec
    {
        public string Name;
        public string DisplayName;
        public string ModelPath;
        public string PrefabPath;
        public string Address;
        public string ArchetypePath;
        public string MaterialName;
        public Color Tint;
        public Color Emission;
        public bool UsesLargeTextures;
        public float Height;
        public float Hover;
    }

    private static readonly VisualSpec[] Specs =
    {
        new()
        {
            Name = "Trilobite Assault",
            DisplayName = "TRILOBITE",
            ModelPath = ModelRoot + "/Enemy_Trilobite.fbx",
            PrefabPath = PrefabRoot + "/TrilobiteAssault.prefab",
            Address = "enemy/trilobite-assault",
            ArchetypePath =
                "Assets/Resources/Content/CityNew/Enemies/Archetypes/SpiderAssault.asset",
            MaterialName = "M_Trilobite_Assault",
            Tint = new Color(1f, 0.78f, 0.68f, 1f),
            Emission = new Color(1f, 0.16f, 0.03f, 1f),
            Height = 1.05f,
            Hover = 0.02f
        },
        new()
        {
            Name = "Eye Drone Suppressor",
            ModelPath = ModelRoot + "/Enemy_EyeDrone.fbx",
            PrefabPath = PrefabRoot + "/EyeDroneSuppressor.prefab",
            Address = "enemy/eye-drone-suppressor",
            ArchetypePath =
                "Assets/Resources/Content/CityNew/Enemies/Archetypes/SpiderSuppressor.asset",
            MaterialName = "M_EyeDrone_Suppressor",
            Tint = new Color(0.42f, 0.2f, 1f, 1f),
            Emission = new Color(0.72f, 0.08f, 1f, 1f),
            Height = 0.82f,
            Hover = 0.48f
        },
        new()
        {
            Name = "Eye Drone Support",
            ModelPath = ModelRoot + "/Enemy_EyeDrone.fbx",
            PrefabPath = PrefabRoot + "/EyeDroneSupport.prefab",
            Address = "enemy/eye-drone-support",
            ArchetypePath =
                "Assets/Resources/Content/CityNew/Enemies/Archetypes/SpiderSupport.asset",
            MaterialName = "M_EyeDrone_Support",
            Tint = new Color(0.08f, 1f, 0.36f, 1f),
            Emission = new Color(0.04f, 1f, 0.55f, 1f),
            Height = 0.82f,
            Hover = 0.58f
        },
        new()
        {
            Name = "Quad Shell Elite",
            ModelPath = ModelRoot + "/Enemy_QuadShell.fbx",
            PrefabPath = PrefabRoot + "/QuadShellElite.prefab",
            Address = "enemy/quad-shell-elite",
            ArchetypePath =
                "Assets/Resources/Content/CityNew/Enemies/Archetypes/SpiderElite.asset",
            MaterialName = "M_QuadShell_Elite",
            Tint = new Color(1f, 0.68f, 0.38f, 1f),
            Emission = new Color(1f, 0.18f, 0.02f, 1f),
            UsesLargeTextures = true,
            Height = 2.05f,
            Hover = 0.02f
        }
    };

    [MenuItem("FPS/Content/Build Enemy Visual Diversity")]
    public static void Build()
    {
        EnsureFolder("Assets", "Generated");
        EnsureFolder("Assets/Generated", "EnemyVisuals");
        EnsureFolder("Assets/Generated/EnemyVisuals", "QuaterniusSciFi");
        EnsureFolder(GeneratedRoot, "Materials");
        EnsureFolder(GeneratedRoot, "Controllers");

        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        ConfigureTextureImporters();

        foreach (string modelPath in Specs.Select(spec => spec.ModelPath).Distinct())
        {
            ConfigureModelImporter(modelPath);
        }

        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        EnemyDefinition enemyDefinition =
            AssetDatabase.LoadAssetAtPath<EnemyDefinition>(EnemyDefinitionPath);
        EnemyController spider =
            AssetDatabase.LoadAssetAtPath<GameObject>(SpiderPrefabPath)
                ?.GetComponent<EnemyController>();

        if (enemyDefinition == null || spider == null)
        {
            throw new InvalidOperationException(
                "Issue66 requires the existing Spider enemy assets.");
        }

        UnityEngine.Object deathEffect = ReadObjectReference(
            spider,
            "bombEffect");
        AddressableAssetSettings settings =
            AddressableAssetSettingsDefaultObject.GetSettings(true);
        AddressableAssetGroup group = settings.FindGroup("Local Enemies") ??
            settings.CreateGroup(
                "Local Enemies",
                false,
                false,
                true,
                null,
                typeof(BundledAssetGroupSchema),
                typeof(ContentUpdateGroupSchema));

        foreach (VisualSpec spec in Specs)
        {
            Material material = BuildMaterial(spec);
            Material beaconMaterial = spec.ModelPath.EndsWith(
                "Enemy_EyeDrone.fbx",
                StringComparison.Ordinal)
                ? BuildBeaconMaterial(spec)
                : null;
            UnityEditor.Animations.AnimatorController controller = BuildAnimatorController(
                spec.ModelPath,
                spec.MaterialName);
            BuildEnemyPrefab(
                spec,
                material,
                beaconMaterial,
                controller,
                enemyDefinition,
                deathEffect);
            RegisterAddress(settings, group, spec.PrefabPath, spec.Address);
            UpdateArchetypeAddress(spec.ArchetypePath, spec.Address);
        }

        settings.BuildAddressablesWithPlayerBuild =
            AddressableAssetSettings.PlayerBuildOption.BuildWithPlayer;
        EditorUtility.SetDirty(settings);
        EditorUtility.SetDirty(group);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log(
            "Issue66: built four role-specific enemy prefabs from three CC0 animated models.");
    }

    [MenuItem("FPS/Content/Render Enemy Visual Preview")]
    public static void RenderPreview()
    {
        string[] paths =
        {
            SpiderPrefabPath,
            PrefabRoot + "/TrilobiteAssault.prefab",
            PrefabRoot + "/EyeDroneSuppressor.prefab",
            PrefabRoot + "/EyeDroneSupport.prefab",
            PrefabRoot + "/QuadShellElite.prefab"
        };
        var previewRoot = new GameObject("Issue66 Enemy Preview");
        var cameraObject = new GameObject("Preview Camera");
        var lightObject = new GameObject("Preview Key Light");
        var fillObject = new GameObject("Preview Fill Light");
        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        var target = new RenderTexture(1800, 700, 24, RenderTextureFormat.ARGB32);
        Texture2D capture = null;
        Material groundMaterial = null;

        try
        {
            float[] positions = { -5.2f, -2.6f, -0.4f, 1.8f, 5f };

            for (int index = 0; index < paths.Length; index++)
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(paths[index]);

                if (prefab == null)
                {
                    throw new InvalidOperationException($"Preview prefab is missing: {paths[index]}");
                }

                GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                instance.transform.SetParent(previewRoot.transform, false);
                instance.transform.position = new Vector3(positions[index], 0f, 0f);
                instance.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
            }

            ground.name = "Preview Ground";
            ground.transform.position = new Vector3(0f, -0.02f, 0f);
            ground.transform.localScale = new Vector3(1.4f, 1f, 0.42f);
            groundMaterial = new Material(ResolveLitShader());
            groundMaterial.color = new Color(0.09f, 0.11f, 0.14f, 1f);
            groundMaterial.SetColor(
                "_BaseColor",
                new Color(0.09f, 0.11f, 0.14f, 1f));
            ground.GetComponent<Renderer>().sharedMaterial = groundMaterial;

            var camera = cameraObject.AddComponent<Camera>();
            camera.transform.position = new Vector3(0f, 3.25f, -11.8f);
            camera.transform.rotation = Quaternion.LookRotation(
                new Vector3(0f, 1.05f, 0f) - camera.transform.position);
            camera.fieldOfView = 42f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.025f, 0.035f, 0.055f, 1f);
            camera.targetTexture = target;

            var key = lightObject.AddComponent<Light>();
            key.type = LightType.Directional;
            key.intensity = 1.4f;
            key.color = new Color(0.82f, 0.9f, 1f, 1f);
            lightObject.transform.rotation = Quaternion.Euler(35f, -35f, 0f);
            var fill = fillObject.AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.intensity = 0.75f;
            fill.color = new Color(1f, 0.5f, 0.3f, 1f);
            fillObject.transform.rotation = Quaternion.Euler(15f, 145f, 0f);

            camera.Render();
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = target;
            capture = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            capture.ReadPixels(new Rect(0f, 0f, target.width, target.height), 0, 0);
            capture.Apply();
            RenderTexture.active = previous;
            File.WriteAllBytes(
                "/tmp/fps-issue66-enemy-preview.png",
                capture.EncodeToPNG());
            Debug.Log("Issue66 preview: /tmp/fps-issue66-enemy-preview.png");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(previewRoot);
            UnityEngine.Object.DestroyImmediate(cameraObject);
            UnityEngine.Object.DestroyImmediate(lightObject);
            UnityEngine.Object.DestroyImmediate(fillObject);
            UnityEngine.Object.DestroyImmediate(ground);
            UnityEngine.Object.DestroyImmediate(target);
            UnityEngine.Object.DestroyImmediate(capture);
            UnityEngine.Object.DestroyImmediate(groundMaterial);
        }
    }

    public static void BuildAddressableContent()
    {
        AddressableAssetSettings.BuildPlayerContent();
        Debug.Log("Issue66: Addressables player content build completed.");
    }

    private static void ConfigureModelImporter(string path)
    {
        if (AssetImporter.GetAtPath(path) is not ModelImporter importer)
        {
            throw new InvalidOperationException($"Enemy model is missing: {path}");
        }

        importer.animationType = ModelImporterAnimationType.Generic;
        importer.importAnimation = true;
        importer.materialImportMode = ModelImporterMaterialImportMode.None;
        importer.importCameras = false;
        importer.importLights = false;
        importer.optimizeGameObjects = false;
        importer.SaveAndReimport();

        ModelImporterClipAnimation[] clips = importer.defaultClipAnimations;

        for (int index = 0; index < clips.Length; index++)
        {
            string clipName = clips[index].name.ToLowerInvariant();
            clips[index].loopTime = clipName.Contains("idle") ||
                clipName.Contains("walk") || clipName.Contains("run");
            clips[index].loopPose = clips[index].loopTime;
        }

        importer.clipAnimations = clips;
        importer.SaveAndReimport();
    }

    private static void ConfigureTextureImporters()
    {
        foreach (string path in new[]
                 {
                     TextureRoot + "/T_Enemies_Normal.png",
                     TextureRoot + "/T_Enemies_Large_Normal.png"
                 })
        {
            if (AssetImporter.GetAtPath(path) is TextureImporter importer &&
                importer.textureType != TextureImporterType.NormalMap)
            {
                importer.textureType = TextureImporterType.NormalMap;
                importer.SaveAndReimport();
            }
        }
    }

    private static Material BuildMaterial(VisualSpec spec)
    {
        string path = $"{GeneratedRoot}/Materials/{spec.MaterialName}.mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);

        if (material == null)
        {
            material = new Material(ResolveLitShader()) { name = spec.MaterialName };
            AssetDatabase.CreateAsset(material, path);
        }

        material.shader = ResolveLitShader();

        string prefix = spec.UsesLargeTextures
            ? "T_Enemies_Large"
            : "T_Enemies";
        Texture2D baseColor = AssetDatabase.LoadAssetAtPath<Texture2D>(
            $"{TextureRoot}/{prefix}_BaseColor.png");
        Texture2D normal = AssetDatabase.LoadAssetAtPath<Texture2D>(
            $"{TextureRoot}/{prefix}_Normal.png");
        Texture2D emission = AssetDatabase.LoadAssetAtPath<Texture2D>(
            $"{TextureRoot}/{prefix}_Emissive.png");
        material.SetTexture("_MainTex", baseColor);
        material.SetColor("_Color", spec.Tint);
        material.SetTexture("_BaseMap", baseColor);
        material.SetColor("_BaseColor", spec.Tint);
        material.SetTexture("_BumpMap", normal);
        material.EnableKeyword("_NORMALMAP");
        material.SetTexture("_EmissionMap", emission);
        material.SetColor("_EmissionColor", spec.Emission * 6f);
        material.EnableKeyword("_EMISSION");
        material.globalIlluminationFlags =
            MaterialGlobalIlluminationFlags.RealtimeEmissive;
        EditorUtility.SetDirty(material);
        return material;
    }

    private static Material BuildBeaconMaterial(VisualSpec spec)
    {
        string path = $"{GeneratedRoot}/Materials/{spec.MaterialName}_Beacon.mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ??
            ResolveLitShader();

        if (material == null)
        {
            material = new Material(shader)
            {
                name = spec.MaterialName + "_Beacon"
            };
            AssetDatabase.CreateAsset(material, path);
        }

        material.shader = shader;
        material.SetColor("_BaseColor", spec.Emission * 2.5f);
        material.SetColor("_Color", spec.Emission * 2.5f);
        EditorUtility.SetDirty(material);
        return material;
    }

    private static UnityEditor.Animations.AnimatorController BuildAnimatorController(
        string modelPath,
        string assetName)
    {
        string path = $"{GeneratedRoot}/Controllers/{assetName}.controller";
        UnityEditor.Animations.AnimatorController controller =
            AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(path);

        if (controller == null)
        {
            controller = UnityEditor.Animations.AnimatorController
                .CreateAnimatorControllerAtPath(path);
        }

        AnimatorStateMachine stateMachine = controller.layers[0].stateMachine;

        foreach (ChildAnimatorState child in stateMachine.states.ToArray())
        {
            stateMachine.RemoveState(child.state);
        }

        AnimationClip[] clips = AssetDatabase.LoadAllAssetsAtPath(modelPath)
            .OfType<AnimationClip>()
            .Where(clip => !clip.name.StartsWith("__preview__", StringComparison.Ordinal))
            .ToArray();
        AnimationClip idle = FindClip(clips, "Idle") ?? clips.FirstOrDefault();
        AnimationClip run = FindClip(clips, "Run") ??
            FindClip(clips, "Walk") ?? idle;
        AnimationClip attack = FindClip(clips, "AttackAuto") ??
            FindClip(clips, "Attack") ?? idle;

        if (idle == null || attack == null)
        {
            throw new InvalidOperationException(
                $"No usable animations were imported from {modelPath}.");
        }

        AnimatorState idleState = stateMachine.AddState("Idle");
        idleState.motion = idle;
        AnimatorState runState = stateMachine.AddState("Run");
        runState.motion = run;
        AnimatorState attackState = stateMachine.AddState("Attack");
        attackState.motion = attack;
        stateMachine.defaultState = idleState;
        EditorUtility.SetDirty(controller);
        return controller;
    }

    private static void BuildEnemyPrefab(
        VisualSpec spec,
        Material material,
        Material beaconMaterial,
        RuntimeAnimatorController controller,
        EnemyDefinition enemyDefinition,
        UnityEngine.Object deathEffect)
    {
        GameObject modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(
            spec.ModelPath);

        if (modelAsset == null)
        {
            throw new InvalidOperationException($"Enemy model could not load: {spec.ModelPath}");
        }

        var root = new GameObject(spec.Name);

        try
        {
            var model = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset);
            model.name = "Visual";
            model.transform.SetParent(root.transform, false);
            Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);

            if (renderers.Length == 0)
            {
                throw new InvalidOperationException($"Enemy model has no renderer: {spec.ModelPath}");
            }

            Bounds initialBounds = Encapsulate(renderers);
            float scale = spec.Height / Mathf.Max(0.01f, initialBounds.size.y);
            model.transform.localScale = Vector3.one * scale;
            Bounds scaledBounds = Encapsulate(renderers);
            model.transform.localPosition += Vector3.up *
                (spec.Hover - scaledBounds.min.y);
            Bounds bounds = Encapsulate(renderers);

            foreach (Renderer renderer in renderers)
            {
                renderer.sharedMaterials = Enumerable.Repeat(
                    material,
                    renderer.sharedMaterials.Length).ToArray();
            }

            if (beaconMaterial != null)
            {
                GameObject beacon = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                beacon.name = "Role Beacon";
                beacon.transform.SetParent(root.transform, false);
                beacon.transform.localPosition = new Vector3(
                    0f,
                    bounds.max.y + 0.13f,
                    0f);
                beacon.transform.localScale = Vector3.one * 0.16f;
                UnityEngine.Object.DestroyImmediate(beacon.GetComponent<Collider>());
                beacon.GetComponent<Renderer>().sharedMaterial = beaconMaterial;
            }

            Animator animator = model.GetComponentInChildren<Animator>(true) ??
                model.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;

            var collider = root.AddComponent<BoxCollider>();
            collider.center = root.transform.InverseTransformPoint(bounds.center);
            Vector3 size = bounds.size;
            size.x = Mathf.Max(0.5f, size.x * 0.9f);
            size.y = Mathf.Max(0.65f, size.y * 0.92f);
            size.z = Mathf.Max(0.5f, size.z * 0.9f);
            collider.size = size;

            var agent = root.AddComponent<NavMeshAgent>();
            agent.radius = Mathf.Clamp(Mathf.Min(size.x, size.z) * 0.38f, 0.28f, 0.7f);
            agent.height = Mathf.Max(1.2f, bounds.max.y);
            agent.baseOffset = 0f;
            agent.obstacleAvoidanceType = ObstacleAvoidanceType.MedQualityObstacleAvoidance;

            var enemy = root.AddComponent<EnemyController>();
            SerializedObject serializedEnemy = new(enemy);
            serializedEnemy.FindProperty("currentEnemy").objectReferenceValue = enemyDefinition;
            serializedEnemy.FindProperty("bombEffect").objectReferenceValue = deathEffect;
            serializedEnemy.FindProperty("displayName").stringValue =
                spec.DisplayName ?? string.Empty;
            serializedEnemy.ApplyModifiedPropertiesWithoutUndo();

            AnimationClip attack = AssetDatabase.LoadAllAssetsAtPath(spec.ModelPath)
                .OfType<AnimationClip>()
                .FirstOrDefault(clip =>
                    clip.name.IndexOf("AttackAuto", StringComparison.OrdinalIgnoreCase) >= 0) ??
                AssetDatabase.LoadAllAssetsAtPath(spec.ModelPath)
                    .OfType<AnimationClip>()
                    .FirstOrDefault(clip =>
                        clip.name.IndexOf("Attack", StringComparison.OrdinalIgnoreCase) >= 0);
            EnemyVisualAnimator visualAnimator = root.AddComponent<EnemyVisualAnimator>();
            visualAnimator.Configure(attack != null ? attack.length : 0.6f);
            root.tag = "Enemy";

            if (PrefabUtility.SaveAsPrefabAsset(root, spec.PrefabPath) == null)
            {
                throw new InvalidOperationException($"Could not save {spec.PrefabPath}.");
            }
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    private static Bounds Encapsulate(IReadOnlyList<Renderer> renderers)
    {
        Bounds bounds = renderers[0].bounds;

        for (int index = 1; index < renderers.Count; index++)
        {
            bounds.Encapsulate(renderers[index].bounds);
        }

        return bounds;
    }

    private static AnimationClip FindClip(
        IEnumerable<AnimationClip> clips,
        string fragment)
    {
        return clips.FirstOrDefault(clip =>
            clip.name.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private static void RegisterAddress(
        AddressableAssetSettings settings,
        AddressableAssetGroup group,
        string path,
        string address)
    {
        settings.CreateOrMoveEntry(AssetDatabase.AssetPathToGUID(path), group)
            .address = address;
    }

    private static void UpdateArchetypeAddress(string path, string address)
    {
        EnemyArchetypeDefinition archetype =
            AssetDatabase.LoadAssetAtPath<EnemyArchetypeDefinition>(path);

        if (archetype == null)
        {
            throw new InvalidOperationException($"Enemy archetype is missing: {path}");
        }

        archetype.ConfigureTemplateAddress(address);
        EditorUtility.SetDirty(archetype);
    }

    private static UnityEngine.Object ReadObjectReference(
        UnityEngine.Object target,
        string propertyName)
    {
        return new SerializedObject(target)
            .FindProperty(propertyName)
            .objectReferenceValue;
    }

    private static void EnsureFolder(string parent, string child)
    {
        string path = $"{parent}/{child}";

        if (!AssetDatabase.IsValidFolder(path))
        {
            AssetDatabase.CreateFolder(parent, child);
        }
    }

    private static Shader ResolveLitShader()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit") ??
            Shader.Find("Standard");

        if (shader == null)
        {
            throw new InvalidOperationException("No supported lit shader is available.");
        }

        return shader;
    }
}
