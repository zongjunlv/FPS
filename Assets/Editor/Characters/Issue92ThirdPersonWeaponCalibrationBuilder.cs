using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class Issue92ThirdPersonWeaponCalibrationBuilder
{
    public const string Root =
        "Assets/Resources/Content/Weapons/ThirdPerson";
    public const string PrefabRoot = Root + "/Prefabs";
    public const string DefinitionRoot = Root + "/Definitions";
    public const string PreviewScenePath =
        "Assets/Scenes/CharacterCalibration/WeaponCalibration.unity";
    private const string ReplicaPath =
        "Assets/Resources/Networking/CoopPlayerReplica.prefab";
    private const string SourceRoot =
        "Assets/ImportPackages/CSAssets2026/Infima Games/" +
        "Low Poly Shooter Pack - Free Sample/Prefabs/Weapons/";

    private static readonly WeaponBuildSpec[] Specs =
    {
        new(
            "weapon.lpsp.ar",
            "AR 突击步枪",
            ThirdPersonWeaponKind.Rifle,
            SourceRoot + "P_LPSP_WEP_AR_01.prefab",
            PrefabRoot + "/CalibratedAR.prefab",
            DefinitionRoot + "/weapon_lpsp_ar.asset",
            new Vector3(0.015f, -0.015f, 0f),
            Vector3.zero,
            Vector3.one),
        new(
            "weapon.lpsp.handgun",
            "战术手枪",
            ThirdPersonWeaponKind.Handgun,
            SourceRoot + "P_LPSP_WEP_Handgun_03.prefab",
            PrefabRoot + "/CalibratedHandgun.prefab",
            DefinitionRoot + "/weapon_lpsp_handgun.asset",
            new Vector3(-0.012f, -0.006f, 0.008f),
            new Vector3(0f, 0.6f, 0f),
            Vector3.one)
    };

    [MenuItem("FPS/Content/Issue 92/Rebuild Third Person Weapon Calibration")]
    public static void Build()
    {
        EnsureFolder(Root);
        EnsureFolder(PrefabRoot);
        EnsureFolder(DefinitionRoot);
        EnsureFolder("Assets/Scenes/CharacterCalibration");

        var definitions = new List<ThirdPersonWeaponDefinition>();
        foreach (WeaponBuildSpec spec in Specs)
        {
            GameObject prefab = BuildCalibratedPrefab(spec);
            definitions.Add(BuildDefinition(spec, prefab));
        }
        ThirdPersonWeaponCatalog catalog = BuildCatalog(definitions);
        PlayerAppearanceCatalog appearances = AssetDatabase.LoadAssetAtPath<
            PlayerAppearanceCatalog>(PlayerAppearanceCatalog.DefaultAssetPath);
        if (appearances == null)
        {
            throw new InvalidOperationException("玩家外观目录不存在。");
        }
        if (!appearances.TryValidate(out string appearanceError))
            throw new InvalidOperationException(
                $"玩家外观目录不可用：{appearanceError}");
        ThirdPersonWeaponCalibrationMatrix matrix = BuildMatrix(
            appearances, catalog);
        ConfigureNetworkReplica(catalog);
        BuildPreviewScene(appearances, catalog);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        catalog = AssetDatabase.LoadAssetAtPath<ThirdPersonWeaponCatalog>(
            ThirdPersonWeaponCatalog.DefaultAssetPath);
        appearances = AssetDatabase.LoadAssetAtPath<PlayerAppearanceCatalog>(
            PlayerAppearanceCatalog.DefaultAssetPath);
        matrix = AssetDatabase.LoadAssetAtPath<
            ThirdPersonWeaponCalibrationMatrix>(
            ThirdPersonWeaponCalibrationMatrix.DefaultAssetPath);
        ThirdPersonWeaponCalibrationAuditReport report =
            ThirdPersonWeaponCalibrationAuditor.Audit(
                catalog, appearances, matrix);
        if (!report.IsValid)
        {
            throw new InvalidOperationException(
                "Issue #92 校准审计失败：\n" + string.Join("\n",
                    report.Issues.Select(issue =>
                        $"[{issue.Code}] {issue.Message}")));
        }
        Debug.Log(
            "Issue #92 已生成统一 WeaponSocket、AR/手枪独立校准 Prefab、" +
            "6 组角色/武器矩阵、校准预览场景与资产审计。 ");
    }

    [MenuItem("FPS/Content/Issue 92/Capture Calibration Matrix")]
    public static void CaptureCalibrationMatrix()
    {
        Scene scene = EditorSceneManager.OpenScene(
            PreviewScenePath, OpenSceneMode.Single);
        WeaponCalibrationPreviewController preview = scene
            .GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<
                WeaponCalibrationPreviewController>(true))
            .Single();
        string outputDirectory = Path.Combine(
            Path.GetTempPath(), "fps-issue92-calibration-preview");
        Directory.CreateDirectory(outputDirectory);
        foreach (int character in Enumerable.Range(0, 3))
        foreach (int weapon in Enumerable.Range(0, 2))
        foreach (WeaponCalibrationView view in new[]
                 {
                     WeaponCalibrationView.Front,
                     WeaponCalibrationView.Side,
                     WeaponCalibrationView.Aim
                 })
        {
            preview.SelectCharacter(character);
            preview.SelectWeapon(weapon);
            preview.SetView(view);
            CaptureCamera(preview.PreviewCamera, Path.Combine(
                outputDirectory,
                $"character-{character + 1}_weapon-{weapon + 1}_" +
                $"{view.ToString().ToLowerInvariant()}.png"));
        }
        Debug.Log($"Issue #92 校准截图已写入：{outputDirectory}");
    }

    private static void CaptureCamera(Camera camera, string outputPath)
    {
        const int width = 960;
        const int height = 720;
        RenderTexture previousTarget = camera.targetTexture;
        RenderTexture previousActive = RenderTexture.active;
        RenderTexture target = RenderTexture.GetTemporary(
            width, height, 24, RenderTextureFormat.ARGB32);
        var image = new Texture2D(width, height, TextureFormat.RGB24, false);
        try
        {
            camera.targetTexture = target;
            camera.Render();
            RenderTexture.active = target;
            image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            image.Apply();
            File.WriteAllBytes(outputPath, image.EncodeToPNG());
        }
        finally
        {
            camera.targetTexture = previousTarget;
            RenderTexture.active = previousActive;
            RenderTexture.ReleaseTemporary(target);
            Object.DestroyImmediate(image);
        }
    }

    private static GameObject BuildCalibratedPrefab(WeaponBuildSpec spec)
    {
        GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(
            spec.SourcePath);
        if (source == null)
            throw new InvalidOperationException(
                $"武器源 Prefab 缺失：{spec.SourcePath}");

        var root = new GameObject($"Calibrated_{spec.StableId}");
        try
        {
            ThirdPersonWeaponRig rig = root.AddComponent<ThirdPersonWeaponRig>();
            Transform rightHand = CreatePoint(
                root.transform,
                ThirdPersonWeaponRig.RightHandReferenceName,
                Vector3.zero);
            Transform calibrationRoot = new GameObject(
                ThirdPersonWeaponRig.CalibrationRootName).transform;
            calibrationRoot.SetParent(root.transform, false);
            calibrationRoot.localPosition = spec.LocalPosition;
            calibrationRoot.localRotation = Quaternion.Euler(spec.LocalEuler);
            calibrationRoot.localScale = spec.LocalScale;

            GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(
                source);
            model.name = "Model";
            model.transform.SetParent(calibrationRoot, false);
            model.transform.SetLocalPositionAndRotation(
                Vector3.zero, Quaternion.identity);
            model.transform.localScale = Vector3.one;
            Transform sourceGrip = FindRequired(model.transform, "SOCKET_Grip");
            Transform sourceAim = FindRequired(model.transform, "SOCKET_Scope");
            Transform sourceMuzzle = FindRequired(
                model.transform, "SOCKET_Muzzle");
            Transform sourceEject = FindOptional(
                model.transform, "SOCKET_Eject");

            Transform leftGrip = CreatePoint(calibrationRoot,
                ThirdPersonWeaponRig.LeftHandGripName,
                calibrationRoot.InverseTransformPoint(sourceGrip.position));
            Transform aim = CreatePoint(calibrationRoot,
                ThirdPersonWeaponRig.AimPointName,
                calibrationRoot.InverseTransformPoint(sourceAim.position));
            Transform muzzle = CreatePoint(calibrationRoot,
                ThirdPersonWeaponRig.MuzzlePointName,
                calibrationRoot.InverseTransformPoint(sourceMuzzle.position));
            Transform casing = sourceEject == null
                ? null
                : CreatePoint(calibrationRoot,
                    ThirdPersonWeaponRig.CasingEjectionPointName,
                    calibrationRoot.InverseTransformPoint(
                        sourceEject.position));
            if (casing != null)
                casing.localRotation = Quaternion.Euler(0f, 90f, 0f);

            StripGameplayComponents(model);
            rig.Configure(calibrationRoot, model.transform, rightHand,
                leftGrip, aim, muzzle, casing);
            if (!rig.TryValidate(out string error))
                throw new InvalidOperationException(
                    $"{spec.StableId} 校准失败：{error}");

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(
                root, spec.PrefabPath);
            if (prefab == null)
                throw new InvalidOperationException(
                    $"无法保存校准 Prefab：{spec.PrefabPath}");
            return prefab;
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    private static ThirdPersonWeaponDefinition BuildDefinition(
        WeaponBuildSpec spec,
        GameObject prefab)
    {
        ThirdPersonWeaponDefinition definition =
            AssetDatabase.LoadAssetAtPath<ThirdPersonWeaponDefinition>(
                spec.DefinitionPath);
        if (definition == null)
        {
            definition = ScriptableObject.CreateInstance<
                ThirdPersonWeaponDefinition>();
            AssetDatabase.CreateAsset(definition, spec.DefinitionPath);
        }
        definition.Configure(spec.StableId, spec.DisplayName, spec.Kind,
            prefab, hasCasingEjection: true);
        EditorUtility.SetDirty(definition);
        return definition;
    }

    private static ThirdPersonWeaponCatalog BuildCatalog(
        IReadOnlyList<ThirdPersonWeaponDefinition> definitions)
    {
        ThirdPersonWeaponCatalog catalog =
            AssetDatabase.LoadAssetAtPath<ThirdPersonWeaponCatalog>(
                ThirdPersonWeaponCatalog.DefaultAssetPath);
        if (catalog == null)
        {
            DeleteBrokenAssetIfPresent(
                ThirdPersonWeaponCatalog.DefaultAssetPath);
            catalog = ScriptableObject.CreateInstance<
                ThirdPersonWeaponCatalog>();
            AssetDatabase.CreateAsset(catalog,
                ThirdPersonWeaponCatalog.DefaultAssetPath);
        }
        catalog.Configure(Specs[0].StableId, definitions);
        EditorUtility.SetDirty(catalog);
        return catalog;
    }

    private static ThirdPersonWeaponCalibrationMatrix BuildMatrix(
        PlayerAppearanceCatalog appearances,
        ThirdPersonWeaponCatalog weapons)
    {
        ThirdPersonWeaponCalibrationMatrix matrix =
            AssetDatabase.LoadAssetAtPath<
                ThirdPersonWeaponCalibrationMatrix>(
                ThirdPersonWeaponCalibrationMatrix.DefaultAssetPath);
        if (matrix == null)
        {
            DeleteBrokenAssetIfPresent(
                ThirdPersonWeaponCalibrationMatrix.DefaultAssetPath);
            matrix = ScriptableObject.CreateInstance<
                ThirdPersonWeaponCalibrationMatrix>();
            AssetDatabase.CreateAsset(matrix,
                ThirdPersonWeaponCalibrationMatrix.DefaultAssetPath);
        }
        matrix.Configure(
            from appearance in appearances.Definitions
            from weapon in weapons.Definitions
            select new ThirdPersonWeaponCalibrationEntry(
                appearance.StableId, weapon.StableId));
        EditorUtility.SetDirty(matrix);
        return matrix;
    }

    private static void ConfigureNetworkReplica(
        ThirdPersonWeaponCatalog catalog)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(ReplicaPath);
        try
        {
            NetworkPlayerAppearancePresenter presenter =
                root.GetComponent<NetworkPlayerAppearancePresenter>();
            if (presenter == null)
                throw new InvalidOperationException(
                    "CoopPlayerReplica 缺少外观 Presenter。");
            NetworkThirdPersonAnimator animation =
                root.GetComponent<NetworkThirdPersonAnimator>();
            if (animation == null)
                animation = root.AddComponent<NetworkThirdPersonAnimator>();
            presenter.Configure(presenter.VisualRoot, catalog,
                catalog.DefaultWeaponId, castOwnerShadows: true);
            PrefabUtility.SaveAsPrefabAsset(root, ReplicaPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void BuildPreviewScene(
        PlayerAppearanceCatalog appearances,
        ThirdPersonWeaponCatalog weapons)
    {
        Scene scene = EditorSceneManager.NewScene(
            NewSceneSetup.EmptyScene,
            NewSceneMode.Single);
        var root = new GameObject("Weapon Calibration Preview");
        var anchor = new GameObject("Preview Anchor");
        anchor.transform.SetParent(root.transform, false);

        var cameraObject = new GameObject("Preview Camera");
        cameraObject.transform.SetParent(root.transform, false);
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.028f, 0.038f, 0.055f, 1f);
        camera.fieldOfView = 42f;
        cameraObject.tag = "MainCamera";

        WeaponCalibrationPreviewController controller =
            root.AddComponent<WeaponCalibrationPreviewController>();
        controller.Configure(appearances, weapons, anchor.transform, camera);
        BuildPreviewEnvironment(root.transform);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, PreviewScenePath);
    }

    private static void BuildPreviewEnvironment(Transform parent)
    {
        GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.name = "Calibration Floor";
        floor.transform.SetParent(parent, false);
        floor.transform.localScale = new Vector3(0.7f, 1f, 0.7f);
        Collider collider = floor.GetComponent<Collider>();
        if (collider != null) Object.DestroyImmediate(collider);

        var keyObject = new GameObject("Key Light");
        keyObject.transform.SetParent(parent, false);
        Light key = keyObject.AddComponent<Light>();
        key.type = LightType.Directional;
        key.intensity = 1.2f;
        keyObject.transform.rotation = Quaternion.Euler(38f, -35f, 0f);

        var fillObject = new GameObject("Fill Light");
        fillObject.transform.SetParent(parent, false);
        Light fill = fillObject.AddComponent<Light>();
        fill.type = LightType.Directional;
        fill.intensity = 0.55f;
        fill.color = new Color(0.55f, 0.7f, 1f);
        fillObject.transform.rotation = Quaternion.Euler(25f, 145f, 0f);

        CreateGuide(parent, "Forward +Z", new Vector3(0f, 0.02f, 0.75f),
            new Vector3(0.025f, 0.025f, 1.5f),
            new Color(0.15f, 0.7f, 1f));
        CreateGuide(parent, "Right +X", new Vector3(0.75f, 0.02f, 0f),
            new Vector3(1.5f, 0.025f, 0.025f),
            new Color(1f, 0.25f, 0.2f));
    }

    private static void CreateGuide(
        Transform parent,
        string name,
        Vector3 position,
        Vector3 scale,
        Color color)
    {
        GameObject guide = GameObject.CreatePrimitive(PrimitiveType.Cube);
        guide.name = name;
        guide.transform.SetParent(parent, false);
        guide.transform.localPosition = position;
        guide.transform.localScale = scale;
        Collider collider = guide.GetComponent<Collider>();
        if (collider != null) Object.DestroyImmediate(collider);
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ??
                        Shader.Find("Unlit/Color");
        if (shader == null) return;
        var material = new Material(shader);
        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Color"))
            material.SetColor("_Color", color);
        guide.GetComponent<Renderer>().sharedMaterial = material;
    }

    private static Transform CreatePoint(
        Transform parent,
        string name,
        Vector3 localPosition)
    {
        Transform point = new GameObject(name).transform;
        point.SetParent(parent, false);
        point.localPosition = localPosition;
        point.localRotation = Quaternion.identity;
        point.localScale = Vector3.one;
        return point;
    }

    private static Transform FindRequired(Transform root, string name)
    {
        Transform result = FindOptional(root, name);
        return result ?? throw new InvalidOperationException(
            $"源武器缺少点位：{name}");
    }

    private static Transform FindOptional(Transform root, string name)
    {
        return root.GetComponentsInChildren<Transform>(true)
            .FirstOrDefault(value => value.name == name);
    }

    private static void StripGameplayComponents(GameObject model)
    {
        foreach (MonoBehaviour behaviour in model
                     .GetComponentsInChildren<MonoBehaviour>(true))
            Object.DestroyImmediate(behaviour);
        foreach (Animator animator in model
                     .GetComponentsInChildren<Animator>(true))
            Object.DestroyImmediate(animator);
        foreach (Collider collider in model
                     .GetComponentsInChildren<Collider>(true))
            Object.DestroyImmediate(collider);
        foreach (Rigidbody body in model
                     .GetComponentsInChildren<Rigidbody>(true))
            Object.DestroyImmediate(body);
        foreach (AudioSource source in model
                     .GetComponentsInChildren<AudioSource>(true))
            Object.DestroyImmediate(source);
        foreach (ParticleSystem particles in model
                     .GetComponentsInChildren<ParticleSystem>(true))
            Object.DestroyImmediate(particles.gameObject);
        foreach (Renderer renderer in model
                     .GetComponentsInChildren<Renderer>(true))
        {
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;
        }
    }

    private static void EnsureFolder(string path)
    {
        string normalized = path.Replace('\\', '/').TrimEnd('/');
        if (AssetDatabase.IsValidFolder(normalized)) return;
        string parent = normalized[..normalized.LastIndexOf('/')];
        string name = normalized[(normalized.LastIndexOf('/') + 1)..];
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, name);
    }

    private static void DeleteBrokenAssetIfPresent(string path)
    {
        if (string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(path))) return;
        if (!AssetDatabase.DeleteAsset(path))
            throw new InvalidOperationException($"无法替换失效资产：{path}");
    }

    private readonly struct WeaponBuildSpec
    {
        public WeaponBuildSpec(
            string stableId,
            string displayName,
            ThirdPersonWeaponKind kind,
            string sourcePath,
            string prefabPath,
            string definitionPath,
            Vector3 localPosition,
            Vector3 localEuler,
            Vector3 localScale)
        {
            StableId = stableId;
            DisplayName = displayName;
            Kind = kind;
            SourcePath = sourcePath;
            PrefabPath = prefabPath;
            DefinitionPath = definitionPath;
            LocalPosition = localPosition;
            LocalEuler = localEuler;
            LocalScale = localScale;
        }

        public string StableId { get; }
        public string DisplayName { get; }
        public ThirdPersonWeaponKind Kind { get; }
        public string SourcePath { get; }
        public string PrefabPath { get; }
        public string DefinitionPath { get; }
        public Vector3 LocalPosition { get; }
        public Vector3 LocalEuler { get; }
        public Vector3 LocalScale { get; }
    }
}
