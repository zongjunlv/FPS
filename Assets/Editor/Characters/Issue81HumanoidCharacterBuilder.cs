using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;
using EditorAnimatorController = UnityEditor.Animations.AnimatorController;

public static class Issue81HumanoidCharacterBuilder
{
    private const string Root =
        "Assets/Resources/Content/Characters/Humanoid";
    private const string MaleModelPath =
        "Assets/ThirdParty/Quaternius/UniversalBaseCharacters/Models/Superhero_Male_FullBody.fbx";
    private const string FemaleModelPath =
        "Assets/ThirdParty/Quaternius/UniversalBaseCharacters/Models/Superhero_Female_FullBody.fbx";
    private const string AnimationModelPath =
        "Assets/ThirdParty/Quaternius/UniversalAnimationLibrary/Animations/UAL1_Standard.fbx";
    private const string BaseCharactersSourceId =
        "source.quaternius.universal-base-characters";
    private const string AnimationLibrarySourceId =
        "source.quaternius.universal-animation-library";

    private static readonly (HumanBodyBones Human, string Skeleton)[]
        HumanBoneMappings =
        {
            (HumanBodyBones.Hips, "pelvis"),
            (HumanBodyBones.Spine, "spine_01"),
            (HumanBodyBones.Chest, "spine_02"),
            (HumanBodyBones.UpperChest, "spine_03"),
            (HumanBodyBones.Neck, "neck_01"),
            (HumanBodyBones.Head, "Head"),
            (HumanBodyBones.LeftShoulder, "clavicle_l"),
            (HumanBodyBones.LeftUpperArm, "upperarm_l"),
            (HumanBodyBones.LeftLowerArm, "lowerarm_l"),
            (HumanBodyBones.LeftHand, "hand_l"),
            (HumanBodyBones.RightShoulder, "clavicle_r"),
            (HumanBodyBones.RightUpperArm, "upperarm_r"),
            (HumanBodyBones.RightLowerArm, "lowerarm_r"),
            (HumanBodyBones.RightHand, "hand_r"),
            (HumanBodyBones.LeftUpperLeg, "thigh_l"),
            (HumanBodyBones.LeftLowerLeg, "calf_l"),
            (HumanBodyBones.LeftFoot, "foot_l"),
            (HumanBodyBones.LeftToes, "ball_l"),
            (HumanBodyBones.RightUpperLeg, "thigh_r"),
            (HumanBodyBones.RightLowerLeg, "calf_r"),
            (HumanBodyBones.RightFoot, "foot_r"),
            (HumanBodyBones.RightToes, "ball_r"),
            (HumanBodyBones.LeftThumbProximal, "thumb_01_l"),
            (HumanBodyBones.LeftThumbIntermediate, "thumb_02_l"),
            (HumanBodyBones.LeftThumbDistal, "thumb_03_l"),
            (HumanBodyBones.LeftIndexProximal, "index_01_l"),
            (HumanBodyBones.LeftIndexIntermediate, "index_02_l"),
            (HumanBodyBones.LeftIndexDistal, "index_03_l"),
            (HumanBodyBones.LeftMiddleProximal, "middle_01_l"),
            (HumanBodyBones.LeftMiddleIntermediate, "middle_02_l"),
            (HumanBodyBones.LeftMiddleDistal, "middle_03_l"),
            (HumanBodyBones.LeftRingProximal, "ring_01_l"),
            (HumanBodyBones.LeftRingIntermediate, "ring_02_l"),
            (HumanBodyBones.LeftRingDistal, "ring_03_l"),
            (HumanBodyBones.LeftLittleProximal, "pinky_01_l"),
            (HumanBodyBones.LeftLittleIntermediate, "pinky_02_l"),
            (HumanBodyBones.LeftLittleDistal, "pinky_03_l"),
            (HumanBodyBones.RightThumbProximal, "thumb_01_r"),
            (HumanBodyBones.RightThumbIntermediate, "thumb_02_r"),
            (HumanBodyBones.RightThumbDistal, "thumb_03_r"),
            (HumanBodyBones.RightIndexProximal, "index_01_r"),
            (HumanBodyBones.RightIndexIntermediate, "index_02_r"),
            (HumanBodyBones.RightIndexDistal, "index_03_r"),
            (HumanBodyBones.RightMiddleProximal, "middle_01_r"),
            (HumanBodyBones.RightMiddleIntermediate, "middle_02_r"),
            (HumanBodyBones.RightMiddleDistal, "middle_03_r"),
            (HumanBodyBones.RightRingProximal, "ring_01_r"),
            (HumanBodyBones.RightRingIntermediate, "ring_02_r"),
            (HumanBodyBones.RightRingDistal, "ring_03_r"),
            (HumanBodyBones.RightLittleProximal, "pinky_01_r"),
            (HumanBodyBones.RightLittleIntermediate, "pinky_02_r"),
            (HumanBodyBones.RightLittleDistal, "pinky_03_r")
        };

    [MenuItem("FPS/Content/Issue 81/Rebuild Humanoid Character Pipeline")]
    public static void Build()
    {
        EnsureFolders();
        ConfigureTextureImports();

        Avatar maleAvatar = ConfigureCharacterModel(MaleModelPath);
        Avatar femaleAvatar = ConfigureCharacterModel(FemaleModelPath);
        ConfigureAnimationLibrary();
        Dictionary<string, AnimationClip> clips = LoadAnimationClips();
        EditorAnimatorController controller = BuildAnimatorController(clips);

        Material eyes = BuildMaterial(
            "Eyes",
            "T_Eye_Brown.png",
            "T_Eye_Normal.png");
        Material maleLight = BuildMaterial(
            "MaleLightBody",
            "T_Superhero_Male_Ligh.png",
            "T_Superhero_Male_Normal.png");
        Material maleDark = BuildMaterial(
            "MaleDarkBody",
            "T_Superhero_Male_Dark.png",
            "T_Superhero_Male_Normal.png");
        Material femaleDark = BuildMaterial(
            "FemaleDarkBody",
            "T_Superhero_Female_Dark_BaseColor.png",
            "T_Superhero_Female_Normal.png");

        GameObject maleLightPrefab = BuildCharacterPrefab(
            "character.quaternius.male-light",
            "先锋·男",
            MaleModelPath,
            maleAvatar,
            controller,
            maleLight,
            eyes,
            "OperatorMaleLight.prefab");
        GameObject maleDarkPrefab = BuildCharacterPrefab(
            "character.quaternius.male-dark",
            "先锋·男（深肤色）",
            MaleModelPath,
            maleAvatar,
            controller,
            maleDark,
            eyes,
            "OperatorMaleDark.prefab");
        GameObject femaleDarkPrefab = BuildCharacterPrefab(
            "character.quaternius.female-dark",
            "先锋·女",
            FemaleModelPath,
            femaleAvatar,
            controller,
            femaleDark,
            eyes,
            "OperatorFemaleDark.prefab");

        HumanoidCharacterStandard standard = BuildStandard(
            controller,
            clips,
            new[]
            {
                CharacterEntry(
                    "character.quaternius.male-light", "先锋·男", maleLightPrefab),
                CharacterEntry(
                    "character.quaternius.male-dark", "先锋·男（深肤色）", maleDarkPrefab),
                CharacterEntry(
                    "character.quaternius.female-dark", "先锋·女", femaleDarkPrefab)
            });
        BuildPreviewScene(standard);
        UpdateLicenseManifest();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        if (!standard.TryValidate(out string error))
        {
            throw new InvalidOperationException(error);
        }
        ThirdPartyLicenseAuditReport licenseReport =
            ThirdPartyAssetLicenseAuditor.Audit();
        if (!licenseReport.IsValid)
        {
            throw new InvalidOperationException(
                "Humanoid 角色授权证据未通过：\n" +
                string.Join("\n", licenseReport.Entries
                    .Where(entry => entry.Severity ==
                                    ThirdPartyLicenseAuditSeverity.Error)
                    .Select(entry => $"[{entry.Code}] {entry.Message}")));
        }
        Debug.Log(
            "Issue #81 Humanoid 管线已生成：3 个标准角色、10 类移动/战斗动作、" +
            "共享动画图和可一键切换的校准预览场景均已通过。");
    }

    private static Avatar ConfigureCharacterModel(string path)
    {
        ModelImporter importer = RequireImporter(path);
        ConfigureCommonModelSettings(importer);
        importer.importAnimation = false;
        importer.animationType = ModelImporterAnimationType.Human;
        importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        importer.sourceAvatar = null;
        importer.autoGenerateAvatarMappingIfUnspecified = true;
        importer.SaveAndReimport();

        Avatar avatar = LoadAvatar(path);
        if (avatar == null || !avatar.isValid || !avatar.isHuman)
        {
            HumanDescription description = importer.humanDescription;
            description.human = HumanBoneMappings.Select(mapping =>
                new HumanBone
                {
                    humanName = mapping.Human.ToString(),
                    boneName = mapping.Skeleton,
                    limit = new HumanLimit { useDefaultValues = true }
                }).ToArray();
            description.hasTranslationDoF = false;
            importer.humanDescription = description;
            importer.SaveAndReimport();
            avatar = LoadAvatar(path);
        }
        if (avatar == null || !avatar.isValid || !avatar.isHuman)
        {
            throw new InvalidOperationException(
                $"模型未能生成有效 Humanoid Avatar：{path}");
        }
        return avatar;
    }

    private static void ConfigureAnimationLibrary()
    {
        ModelImporter importer = RequireImporter(AnimationModelPath);
        ConfigureCommonModelSettings(importer);
        importer.importAnimation = true;
        importer.animationType = ModelImporterAnimationType.Human;
        importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        importer.sourceAvatar = null;
        importer.autoGenerateAvatarMappingIfUnspecified = true;
        importer.SaveAndReimport();

        Avatar animationAvatar = LoadAvatar(AnimationModelPath);
        if (animationAvatar == null || !animationAvatar.isValid ||
            !animationAvatar.isHuman)
        {
            HumanDescription description = importer.humanDescription;
            description.human = HumanBoneMappings.Select(mapping =>
                new HumanBone
                {
                    humanName = mapping.Human.ToString(),
                    boneName = mapping.Skeleton,
                    limit = new HumanLimit { useDefaultValues = true }
                }).ToArray();
            description.hasTranslationDoF = false;
            importer.humanDescription = description;
            importer.SaveAndReimport();
            animationAvatar = LoadAvatar(AnimationModelPath);
        }
        if (animationAvatar == null || !animationAvatar.isValid ||
            !animationAvatar.isHuman)
        {
            throw new InvalidOperationException(
                "动画库未能生成独立有效的 Humanoid Avatar。");
        }

        ModelImporterClipAnimation[] clips = importer.defaultClipAnimations;
        foreach (ModelImporterClipAnimation clip in clips)
        {
            clip.name = StandardClipName(clip.name);
            clip.loopTime = clip.name.EndsWith(
                "_Loop", StringComparison.OrdinalIgnoreCase);
            clip.keepOriginalOrientation = true;
            clip.keepOriginalPositionY = true;
            clip.keepOriginalPositionXZ = true;
        }
        importer.clipAnimations = clips;
        importer.SaveAndReimport();

        foreach (AnimationClip clip in AssetDatabase
                     .LoadAllAssetsAtPath(AnimationModelPath)
                     .OfType<AnimationClip>()
                     .Where(value => !value.name.StartsWith(
                         "__preview__", StringComparison.Ordinal)))
        {
            if (!clip.isHumanMotion)
            {
                throw new InvalidOperationException(
                    $"动画 '{clip.name}' 未能转换为 Humanoid Motion。");
            }
        }
    }

    private static void ConfigureCommonModelSettings(ModelImporter importer)
    {
        importer.globalScale = 1f;
        importer.useFileScale = true;
        importer.bakeAxisConversion = true;
        importer.importCameras = false;
        importer.importLights = false;
        importer.importVisibility = false;
        importer.importBlendShapes = true;
        importer.importConstraints = false;
        importer.materialImportMode = ModelImporterMaterialImportMode.None;
        importer.meshCompression = ModelImporterMeshCompression.Medium;
        importer.isReadable = false;
        importer.optimizeMeshPolygons = true;
        importer.optimizeMeshVertices = true;
        importer.animationCompression = ModelImporterAnimationCompression.Optimal;
        importer.animationRotationError = 0.25f;
        importer.animationPositionError = 0.25f;
        importer.animationScaleError = 0.25f;
    }

    private static Dictionary<string, AnimationClip> LoadAnimationClips()
    {
        Dictionary<string, AnimationClip> clips = AssetDatabase
            .LoadAllAssetsAtPath(AnimationModelPath)
            .OfType<AnimationClip>()
            .Where(value => !value.name.StartsWith(
                "__preview__", StringComparison.Ordinal))
            .ToDictionary(value => value.name, StringComparer.Ordinal);
        string[] required =
        {
            "Idle_Loop", "Walk_Loop", "Sprint_Loop", "Crouch_Idle_Loop",
            "Jump_Start", "Jump_Loop", "Jump_Land", "Pistol_Aim_Neutral",
            "Pistol_Shoot", "Pistol_Reload"
        };
        foreach (string name in required)
        {
            if (!clips.ContainsKey(name))
            {
                throw new InvalidOperationException($"动画库缺少必需动作：{name}");
            }
        }
        return clips;
    }

    private static EditorAnimatorController BuildAnimatorController(
        IReadOnlyDictionary<string, AnimationClip> clips)
    {
        return Issue91ThirdPersonAnimationBuilder.BuildController(clips);
    }

    private static GameObject BuildCharacterPrefab(
        string stableId,
        string displayName,
        string modelPath,
        Avatar avatar,
        RuntimeAnimatorController controller,
        Material bodyMaterial,
        Material eyeMaterial,
        string fileName)
    {
        GameObject modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
        if (modelAsset == null)
        {
            throw new InvalidOperationException($"角色模型未导入：{modelPath}");
        }

        GameObject root = new(displayName);
        root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        root.transform.localScale = Vector3.one;
        GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset);
        model.name = "Model";
        model.transform.SetParent(root.transform, false);
        model.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        model.transform.localScale = Vector3.one;
        foreach (Animator childAnimator in root.GetComponentsInChildren<Animator>(true))
        {
            Object.DestroyImmediate(childAnimator);
        }
        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            bool isEye = renderer.name.IndexOf(
                "Eye", StringComparison.OrdinalIgnoreCase) >= 0;
            Material selected = isEye ? eyeMaterial : bodyMaterial;
            Material[] materials = renderer.sharedMaterials;
            if (materials.Length == 0)
            {
                materials = new[] { selected };
            }
            else
            {
                for (int index = 0; index < materials.Length; index++)
                {
                    materials[index] = selected;
                }
            }
            renderer.sharedMaterials = materials;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;
        }
        NormalizeModelTransform(model.transform, root.transform);

        Animator animator = root.AddComponent<Animator>();
        animator.avatar = avatar;
        animator.runtimeAnimatorController = controller;
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        HumanoidCharacterMarker marker =
            root.AddComponent<HumanoidCharacterMarker>();
        marker.Configure(
            stableId,
            HumanoidCharacterStandard.StandardHeight,
            modelPath);

        string path = $"{Root}/Prefabs/{fileName}";
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);
        if (prefab == null)
        {
            throw new InvalidOperationException($"无法保存角色 Prefab：{path}");
        }
        return prefab;
    }

    private static void NormalizeModelTransform(
        Transform model,
        Transform root)
    {
        Bounds bounds = CalculateBounds(root.gameObject);
        if (bounds.size.y <= 0.01f)
        {
            throw new InvalidOperationException("角色模型没有有效渲染高度。");
        }
        float scale = HumanoidCharacterStandard.StandardHeight / bounds.size.y;
        if (!float.IsFinite(scale) || scale <= 0f)
        {
            throw new InvalidOperationException("角色标准化缩放无效。");
        }
        model.localScale = Vector3.one * scale;
        bounds = CalculateBounds(root.gameObject);
        model.localPosition += Vector3.up * -bounds.min.y;
    }

    private static Bounds CalculateBounds(GameObject root)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
        {
            return new Bounds(root.transform.position, Vector3.zero);
        }
        Bounds bounds = renderers[0].bounds;
        for (int index = 1; index < renderers.Length; index++)
        {
            bounds.Encapsulate(renderers[index].bounds);
        }
        return bounds;
    }

    private static HumanoidCharacterStandard BuildStandard(
        RuntimeAnimatorController controller,
        IReadOnlyDictionary<string, AnimationClip> clips,
        HumanoidCharacterEntry[] characters)
    {
        HumanoidCharacterStandard standard =
            AssetDatabase.LoadAssetAtPath<HumanoidCharacterStandard>(
                HumanoidCharacterStandard.DefaultAssetPath);
        if (standard == null)
        {
            standard = ScriptableObject.CreateInstance<HumanoidCharacterStandard>();
            AssetDatabase.CreateAsset(
                standard,
                HumanoidCharacterStandard.DefaultAssetPath);
        }
        var motions = new HumanoidAnimationSet();
        motions.Configure(
            clips["Idle_Loop"],
            clips["Walk_Loop"],
            clips["Sprint_Loop"],
            clips["Crouch_Idle_Loop"],
            clips["Jump_Start"],
            clips["Jump_Loop"],
            clips["Jump_Land"],
            clips["Pistol_Aim_Neutral"],
            clips["Pistol_Shoot"],
            clips["Pistol_Reload"]);
        standard.Configure(
            "characters.humanoid.standard",
            2,
            controller,
            motions,
            characters);
        EditorUtility.SetDirty(standard);
        return standard;
    }

    private static HumanoidCharacterEntry CharacterEntry(
        string id,
        string name,
        GameObject prefab)
    {
        var entry = new HumanoidCharacterEntry();
        entry.Configure(
            id,
            name,
            prefab,
            HumanoidCharacterStandard.StandardHeight);
        return entry;
    }

    private static Material BuildMaterial(
        string name,
        string baseTextureName,
        string normalTextureName)
    {
        string path = $"{Root}/Materials/{name}.mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        Shader shader = Shader.Find("Universal Render Pipeline/Lit") ??
                        Shader.Find("Standard");
        if (shader == null)
        {
            throw new InvalidOperationException("未找到可用的 Lit Shader。");
        }
        if (material == null)
        {
            material = new Material(shader) { name = name };
            AssetDatabase.CreateAsset(material, path);
        }
        else
        {
            material.shader = shader;
        }
        string textureRoot =
            "Assets/ThirdParty/Quaternius/UniversalBaseCharacters/Textures/";
        Texture2D baseMap = AssetDatabase.LoadAssetAtPath<Texture2D>(
            textureRoot + baseTextureName);
        Texture2D normalMap = AssetDatabase.LoadAssetAtPath<Texture2D>(
            textureRoot + normalTextureName);
        if (baseMap == null || normalMap == null)
        {
            throw new InvalidOperationException($"材质纹理缺失：{name}");
        }
        SetTexture(material, "_BaseMap", "_MainTex", baseMap);
        if (material.HasProperty("_BumpMap"))
        {
            material.SetTexture("_BumpMap", normalMap);
            material.EnableKeyword("_NORMALMAP");
        }
        if (material.HasProperty("_Smoothness"))
        {
            material.SetFloat("_Smoothness", 0.28f);
        }
        material.enableInstancing = true;
        EditorUtility.SetDirty(material);
        return material;
    }

    private static void SetTexture(
        Material material,
        string preferred,
        string fallback,
        Texture texture)
    {
        if (material.HasProperty(preferred)) material.SetTexture(preferred, texture);
        else if (material.HasProperty(fallback)) material.SetTexture(fallback, texture);
    }

    private static void ConfigureTextureImports()
    {
        const string root =
            "Assets/ThirdParty/Quaternius/UniversalBaseCharacters/Textures";
        foreach (string path in Directory.GetFiles(root, "*.png"))
        {
            string assetPath = path.Replace('\\', '/');
            TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null) continue;
            bool normal = assetPath.IndexOf(
                "Normal", StringComparison.OrdinalIgnoreCase) >= 0;
            importer.textureType = normal
                ? TextureImporterType.NormalMap
                : TextureImporterType.Default;
            importer.sRGBTexture = !normal && assetPath.IndexOf(
                "Roughness", StringComparison.OrdinalIgnoreCase) < 0;
            importer.mipmapEnabled = true;
            importer.maxTextureSize = 2048;
            importer.textureCompression = TextureImporterCompression.Compressed;
            importer.SaveAndReimport();
        }
    }

    private static void BuildPreviewScene(HumanoidCharacterStandard standard)
    {
        Scene scene = EditorSceneManager.NewScene(
            NewSceneSetup.EmptyScene,
            NewSceneMode.Single);
        var previewRoot = new GameObject("Humanoid Character Preview");
        var charactersRoot = new GameObject("Characters");
        charactersRoot.transform.SetParent(previewRoot.transform, false);
        GameObject[] instances = new GameObject[standard.Characters.Count];
        for (int index = 0; index < standard.Characters.Count; index++)
        {
            instances[index] = (GameObject)PrefabUtility.InstantiatePrefab(
                standard.Characters[index].Prefab,
                scene);
            instances[index].name = standard.Characters[index].DisplayName;
            instances[index].transform.SetParent(charactersRoot.transform, false);
            instances[index].transform.SetLocalPositionAndRotation(
                Vector3.zero,
                Quaternion.identity);
            instances[index].transform.localScale = Vector3.one;
            instances[index].SetActive(index == 0);
        }
        var controls = new GameObject("Preview Controls");
        controls.transform.SetParent(previewRoot.transform, false);
        HumanoidCharacterPreviewController controller =
            controls.AddComponent<HumanoidCharacterPreviewController>();
        controller.Configure(standard, instances);

        BuildPreviewEnvironment(previewRoot.transform);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(
            scene,
            HumanoidCharacterStandard.PreviewScenePath);
    }

    private static void BuildPreviewEnvironment(Transform parent)
    {
        GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.name = "Calibration Floor";
        floor.transform.SetParent(parent, false);
        floor.transform.localScale = new Vector3(0.6f, 1f, 0.6f);

        GameObject cameraObject = new("Preview Camera");
        cameraObject.transform.SetParent(parent, false);
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.035f, 0.045f, 0.06f, 1f);
        camera.fieldOfView = 42f;
        cameraObject.transform.position = new Vector3(0f, 1.05f, 3.5f);
        cameraObject.transform.rotation = Quaternion.LookRotation(
            new Vector3(0f, 0.95f, 0f) - cameraObject.transform.position,
            Vector3.up);
        cameraObject.tag = "MainCamera";

        GameObject keyLightObject = new("Key Light");
        keyLightObject.transform.SetParent(parent, false);
        Light keyLight = keyLightObject.AddComponent<Light>();
        keyLight.type = LightType.Directional;
        keyLight.intensity = 1.15f;
        keyLightObject.transform.rotation = Quaternion.Euler(38f, -32f, 0f);

        GameObject fillLightObject = new("Fill Light");
        fillLightObject.transform.SetParent(parent, false);
        Light fillLight = fillLightObject.AddComponent<Light>();
        fillLight.type = LightType.Directional;
        fillLight.intensity = 0.55f;
        fillLight.color = new Color(0.58f, 0.72f, 1f);
        fillLightObject.transform.rotation = Quaternion.Euler(25f, 145f, 0f);

        CreateGuide(parent, "Height 1.8m", new Vector3(-1.15f, 0.9f, 0f),
            new Vector3(0.025f, 1.8f, 0.025f), new Color(0.1f, 0.95f, 0.8f));
        CreateGuide(parent, "Foot Origin", new Vector3(0f, 0.01f, 0f),
            new Vector3(2.4f, 0.02f, 0.025f), new Color(1f, 0.65f, 0.1f));
        CreateGuide(parent, "Forward +Z", new Vector3(0f, 0.02f, 0.8f),
            new Vector3(0.025f, 0.025f, 1.6f), new Color(0.25f, 0.65f, 1f));
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
        var material = new Material(shader);
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Color")) material.SetColor("_Color", color);
        guide.GetComponent<Renderer>().sharedMaterial = material;
    }

    private static void UpdateLicenseManifest()
    {
        ThirdPartyAssetLicenseManifest manifest =
            AssetDatabase.LoadAssetAtPath<ThirdPartyAssetLicenseManifest>(
                ThirdPartyAssetLicenseManifest.DefaultAssetPath);
        if (manifest == null)
        {
            throw new InvalidOperationException("Issue #80 授权清单缺失。");
        }
        List<ThirdPartySourceRecord> sources = manifest.Sources
            .Where(source => source.StableId != BaseCharactersSourceId &&
                             source.StableId != AnimationLibrarySourceId)
            .ToList();
        sources.Add(BaseCharacterSource());
        sources.Add(AnimationLibrarySource());

        List<ThirdPartyAssetRecord> assets = manifest.Assets
            .Where(asset => asset.SourceStableId != BaseCharactersSourceId &&
                            asset.SourceStableId != AnimationLibrarySourceId)
            .ToList();
        assets.Add(AssetRecord(
            "model.quaternius.superhero.male-light",
            "Universal Base Character 男性（浅肤色）",
            ThirdPartyAssetKind.CharacterModel,
            BaseCharactersSourceId,
            MaleModelPath,
            "79344418d754a59730b79d1874752e9592143db34abe8adf138fa9a92a4768e9"));
        assets.Add(AssetRecord(
            "model.quaternius.superhero.male-dark",
            "Universal Base Character 男性（深肤色）",
            ThirdPartyAssetKind.CharacterModel,
            BaseCharactersSourceId,
            MaleModelPath,
            "79344418d754a59730b79d1874752e9592143db34abe8adf138fa9a92a4768e9"));
        assets.Add(AssetRecord(
            "model.quaternius.superhero.female-dark",
            "Universal Base Character 女性（深肤色）",
            ThirdPartyAssetKind.CharacterModel,
            BaseCharactersSourceId,
            FemaleModelPath,
            "0727e7b236eeea4115531e07aeb2bb7690c1a58155f743bbf54282944fb97ea9"));
        assets.Add(AssetRecord(
            "animation.quaternius.ual1.standard",
            "Universal Animation Library 标准动作集",
            ThirdPartyAssetKind.AnimationSet,
            AnimationLibrarySourceId,
            AnimationModelPath,
            "21b32d912da3cb93426d974fb945e86f5b2e86970acd2ce89905e0fbf9f1dcc2",
            "Idle_Loop", "Walk_Loop", "Sprint_Loop", "Crouch_Idle_Loop",
            "Jump_Start", "Jump_Loop", "Jump_Land", "Pistol_Aim_Neutral",
            "Pistol_Shoot", "Pistol_Reload"));
        manifest.Configure(
            manifest.StableId,
            sources,
            assets,
            new[]
            {
                "model.quaternius.superhero.male-light",
                "model.quaternius.superhero.male-dark",
                "model.quaternius.superhero.female-dark"
            });
        EditorUtility.SetDirty(manifest);
    }

    private static ThirdPartySourceRecord BaseCharacterSource()
    {
        var source = new ThirdPartySourceRecord();
        source.Configure(
            BaseCharactersSourceId,
            "Quaternius Universal Base Characters",
            "Quaternius",
            "Standard 2025-12-16",
            "https://quaternius.com/packs/universalbasecharacters.html",
            "https://quaternius.itch.io/universal-base-characters/purchase",
            "CC0-1.0",
            "https://creativecommons.org/publicdomain/zero/1.0/legalcode",
            "Assets/ThirdParty/Quaternius/UniversalBaseCharacters/License/License_Standard.txt",
            "0f4beaf0fe360a7732e58bbe3dbf60a2422367fbea60cb9ea4add968f383268e",
            "Assets/ThirdParty/Kenney/BlockyCharacters/License/CC0-1.0-Legal-Code.txt",
            "a2010f343487d3f7618affe54f789f5487602331c0a8d03f49e9a7c547cf0499",
            "2026-09-16",
            "fdbf1804c90dfc1ea03e992bff7da2dfd1a79318e13270a660180f9308455f40",
            true, true, true, false, true);
        return source;
    }

    private static ThirdPartySourceRecord AnimationLibrarySource()
    {
        var source = new ThirdPartySourceRecord();
        source.Configure(
            AnimationLibrarySourceId,
            "Quaternius Universal Animation Library",
            "Quaternius / Gonzalo Furnier",
            "Standard v3.0",
            "https://quaternius.com/packs/universalanimationlibrary.html",
            "https://quaternius.itch.io/universal-animation-library/purchase",
            "CC0-1.0",
            "https://creativecommons.org/publicdomain/zero/1.0/legalcode",
            "Assets/ThirdParty/Quaternius/UniversalAnimationLibrary/License/License.txt",
            "6d01f55c6e4c49a2c9963e147e561945ae2c83958c8ca667d90a6bffdbfac061",
            "Assets/ThirdParty/Kenney/BlockyCharacters/License/CC0-1.0-Legal-Code.txt",
            "a2010f343487d3f7618affe54f789f5487602331c0a8d03f49e9a7c547cf0499",
            "2026-09-16",
            "cc73fc4e495b82958207316596317a3f40b9fa38065bde1027937452da537724",
            true, true, true, false, true);
        return source;
    }

    private static ThirdPartyAssetRecord AssetRecord(
        string id,
        string displayName,
        ThirdPartyAssetKind kind,
        string sourceId,
        string path,
        string hash,
        params string[] subAssets)
    {
        var record = new ThirdPartyAssetRecord();
        record.Configure(
            id, displayName, kind, sourceId, path, hash, true, subAssets);
        return record;
    }

    private static ModelImporter RequireImporter(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Humanoid 源文件缺失。", path);
        }
        AssetDatabase.ImportAsset(
            path,
            ImportAssetOptions.ForceSynchronousImport |
            ImportAssetOptions.ForceUpdate);
        return AssetImporter.GetAtPath(path) as ModelImporter ??
               throw new InvalidOperationException($"无法读取模型导入器：{path}");
    }

    private static Avatar LoadAvatar(string path) => AssetDatabase
        .LoadAllAssetsAtPath(path)
        .OfType<Avatar>()
        .FirstOrDefault();

    private static string StandardClipName(string value)
    {
        int separator = value.LastIndexOf('|');
        return separator >= 0 ? value[(separator + 1)..] : value;
    }

    private static void EnsureFolders()
    {
        foreach (string path in new[]
                 {
                     Root,
                     Root + "/Animation",
                     Root + "/Materials",
                     Root + "/Prefabs",
                     "Assets/Scenes/CharacterCalibration"
                 })
        {
            EnsureFolder(path);
        }
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
