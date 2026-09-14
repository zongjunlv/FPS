using System;
using System.Collections.Generic;
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
        public Vector3 BodyCenterNormalized;
        public Vector3 BodySizeNormalized;
        public Vector3 HeadCenterNormalized;
        public Vector3 HeadSizeNormalized;
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
            Hover = 0.02f,
            // 四肢会随 Idle/Run 大幅摆动，身体判定只覆盖中央甲壳，
            // 避免不同动画采样帧把腿部之间的空气当作有效命中。
            BodyCenterNormalized = new Vector3(0f, -0.03f, 0f),
            BodySizeNormalized = new Vector3(0.7f, 0.46f, 0.66f),
            HeadCenterNormalized = new Vector3(0f, 0.31f, -0.04f),
            HeadSizeNormalized = new Vector3(0.62f, 0.28f, 0.62f)
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
            Hover = 0.48f,
            BodyCenterNormalized = new Vector3(0f, -0.08f, 0f),
            BodySizeNormalized = new Vector3(0.94f, 0.7f, 0.9f),
            HeadCenterNormalized = new Vector3(0f, 0.27f, -0.06f),
            HeadSizeNormalized = new Vector3(0.58f, 0.3f, 0.58f)
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
            Hover = 0.58f,
            BodyCenterNormalized = new Vector3(0f, -0.08f, 0f),
            BodySizeNormalized = new Vector3(0.94f, 0.7f, 0.9f),
            HeadCenterNormalized = new Vector3(0f, 0.27f, -0.06f),
            HeadSizeNormalized = new Vector3(0.58f, 0.3f, 0.58f)
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
            Hover = 0.02f,
            // Quad Shell 的腿部在 Idle 中会明显内收；身体受击区只覆盖稳定的
            // 装甲核心，避免用导入姿态的全包围盒把四肢间的空气也算作命中。
            BodyCenterNormalized = new Vector3(0f, -0.04f, 0f),
            BodySizeNormalized = new Vector3(0.72f, 0.52f, 0.7f),
            HeadCenterNormalized = new Vector3(0f, 0.31f, -0.03f),
            HeadSizeNormalized = new Vector3(0.56f, 0.28f, 0.58f)
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

        // Reuse the existing states so rebuilding generated enemy content is
        // idempotent and does not churn AnimatorState file IDs on every run.
        AnimatorState idleState = GetOrCreateState(stateMachine, "Idle");
        idleState.motion = idle;
        AnimatorState runState = GetOrCreateState(stateMachine, "Run");
        runState.motion = run;
        AnimatorState attackState = GetOrCreateState(stateMachine, "Attack");
        attackState.motion = attack;
        stateMachine.defaultState = idleState;
        EditorUtility.SetDirty(controller);
        return controller;
    }

    private static AnimatorState GetOrCreateState(
        AnimatorStateMachine stateMachine,
        string stateName)
    {
        foreach (ChildAnimatorState child in stateMachine.states)
        {
            if (child.state != null && string.Equals(
                    child.state.name,
                    stateName,
                    StringComparison.Ordinal))
            {
                return child.state;
            }
        }

        return stateMachine.AddState(stateName);
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
            // The root collider is retained as a stable visual envelope for
            // navigation and overhead UI placement. Damage is handled only by
            // the explicit model-specific Body/Head children below.
            collider.enabled = false;

            var agent = root.AddComponent<NavMeshAgent>();
            agent.radius = Mathf.Clamp(Mathf.Min(size.x, size.z) * 0.38f, 0.28f, 0.7f);
            agent.height = Mathf.Max(1.2f, bounds.max.y);
            agent.baseOffset = 0f;
            agent.obstacleAvoidanceType = ObstacleAvoidanceType.MedQualityObstacleAvoidance;

            Health health = root.AddComponent<Health>();
            CreateHitbox(
                root,
                health,
                bounds,
                "Body Hitbox",
                spec.BodyCenterNormalized,
                spec.BodySizeNormalized,
                1f,
                HitRegion.Body);
            CreateHitbox(
                root,
                health,
                bounds,
                "Head Hitbox",
                spec.HeadCenterNormalized,
                spec.HeadSizeNormalized,
                2f,
                HitRegion.Head);

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
            visualAnimator.ConfigureProceduralLocomotion(
                !HasLocomotionClip(spec.ModelPath),
                0.035f,
                4.5f,
                4f);
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

    private static void CreateHitbox(
        GameObject root,
        Health health,
        Bounds visualBounds,
        string hitboxName,
        Vector3 normalizedCenter,
        Vector3 normalizedSize,
        float damageMultiplier,
        HitRegion region)
    {
        Vector3 localBoundsCenter = root.transform.InverseTransformPoint(
            visualBounds.center);
        Vector3 localBoundsSize = Abs(
            root.transform.InverseTransformVector(visualBounds.size));
        Vector3 localCenter = localBoundsCenter + Vector3.Scale(
            localBoundsSize,
            normalizedCenter);
        Vector3 localSize = Vector3.Max(
            Vector3.one * 0.08f,
            Vector3.Scale(localBoundsSize, normalizedSize));
        var hitboxObject = new GameObject(hitboxName);
        hitboxObject.layer = root.layer;
        hitboxObject.transform.SetParent(root.transform, false);
        hitboxObject.transform.localPosition = localCenter;
        BoxCollider hitboxCollider =
            hitboxObject.AddComponent<BoxCollider>();
        hitboxCollider.center = Vector3.zero;
        hitboxCollider.size = localSize;
        DamageHitbox hitbox = hitboxObject.AddComponent<DamageHitbox>();
        hitbox.ConfigureRegion(health, damageMultiplier, region);
    }

    private static Vector3 Abs(Vector3 value)
    {
        return new Vector3(
            Mathf.Abs(value.x),
            Mathf.Abs(value.y),
            Mathf.Abs(value.z));
    }

    private static bool HasLocomotionClip(string modelPath)
    {
        AnimationClip[] clips = AssetDatabase.LoadAllAssetsAtPath(modelPath)
            .OfType<AnimationClip>()
            .Where(clip => !clip.name.StartsWith(
                "__preview__",
                StringComparison.Ordinal))
            .ToArray();
        return FindClip(clips, "Run") != null ||
            FindClip(clips, "Walk") != null;
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
