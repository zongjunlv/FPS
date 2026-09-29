using System;
using System.Collections.Generic;
using System.Linq;
using FPS.Networking.Session;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>Reconstructs visual hierarchies; never instantiates a gameplay enemy.</summary>
public static class CoopEnemyPresentationBuilder
{
    private const string Root = "Assets/Resources/CoopPresentation/EnemyModels";
    private static readonly (string file, string address)[] Sources =
    {
        ("Spider", "enemy/spider"),
        ("TrilobiteAssault", "enemy/trilobite-assault"),
        ("EyeDroneSuppressor", "enemy/eye-drone-suppressor"),
        ("EyeDroneSupport", "enemy/eye-drone-support"),
        ("QuadShellElite", "enemy/quad-shell-elite")
    };

    public static string Build()
    {
        EnsureFolder(Root);
        AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.GetSettings(true);
        AddressableAssetGroup group = settings.FindGroup("Coop Presentation") ??
            settings.CreateGroup("Coop Presentation", false, false, true, null,
                typeof(BundledAssetGroupSchema), typeof(ContentUpdateGroupSchema));
        foreach (var spec in Sources)
        {
            string sourcePath = $"Assets/AddressableAssets/Enemies/{spec.file}.prefab";
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
            if (source == null) throw new InvalidOperationException(sourcePath);
            string sourceAddress = settings.FindAssetEntry(AssetDatabase.AssetPathToGUID(sourcePath))?.address
                ?? spec.address;
            BuildOne(source, spec.file, sourceAddress);
            string path = $"{Root}/{spec.file}.prefab";
            var entry = settings.CreateOrMoveEntry(AssetDatabase.AssetPathToGUID(path), group);
            entry.address = "coop/presentation/" + sourceAddress;
        }
        EditorUtility.SetDirty(group);
        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssets();
        return "Built 5 pure enemy presentation prefabs with authored hit calibration.";
    }

    private static void BuildOne(GameObject source, string name, string address)
    {
        var map = new Dictionary<Transform, Transform>();
        GameObject root = CloneTransforms(source.transform, null, map).gameObject;
        try
        {
            root.name = name;
            root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            root.transform.localScale = Vector3.one;
            foreach (var pair in map)
            {
                MeshFilter filter = pair.Key.GetComponent<MeshFilter>();
                if (filter != null) pair.Value.gameObject.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                MeshRenderer mesh = pair.Key.GetComponent<MeshRenderer>();
                if (mesh != null) EditorUtility.CopySerialized(mesh, pair.Value.gameObject.AddComponent<MeshRenderer>());
                SkinnedMeshRenderer skin = pair.Key.GetComponent<SkinnedMeshRenderer>();
                if (skin != null)
                {
                    var clone = pair.Value.gameObject.AddComponent<SkinnedMeshRenderer>();
                    EditorUtility.CopySerialized(skin, clone);
                    clone.bones = skin.bones.Select(bone => bone != null && map.TryGetValue(bone, out var mapped)
                        ? mapped : null).ToArray();
                    clone.rootBone = skin.rootBone != null && map.TryGetValue(skin.rootBone, out var rootBone)
                        ? rootBone : null;
                    clone.updateWhenOffscreen = true;
                }
                Animator animator = pair.Key.GetComponent<Animator>();
                if (animator != null)
                {
                    var clone = pair.Value.gameObject.AddComponent<Animator>();
                    clone.avatar = animator.avatar;
                    clone.runtimeAnimatorController = BuildController(name, animator.runtimeAnimatorController);
                    clone.applyRootMotion = false;
                    clone.fireEvents = false;
                    clone.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                }
            }
            var calibration = root.AddComponent<CoopEnemyPresentationDefinition>();
            calibration.SourceAddress = address;
            Bounds bounds = BoundsOf(root.GetComponentsInChildren<Renderer>(true));
            calibration.StatusAnchor = new Vector3(bounds.center.x, bounds.max.y + 0.16f, bounds.center.z);
            ReadHitbox(source, HitRegion.Body, bounds, out calibration.BodyCenter, out calibration.BodyHalfExtents);
            ReadHitbox(source, HitRegion.Head, bounds, out calibration.HeadCenter, out calibration.HeadHalfExtents);
            calibration.HasLocomotionClip = source.GetComponentsInChildren<Animator>(true)
                .Where(value => value.runtimeAnimatorController != null)
                .SelectMany(value => value.runtimeAnimatorController.animationClips)
                .Any(clip => Contains(clip.name, "run") || Contains(clip.name, "walk"));
            // Colliders, NavMeshAgent, Health, damage handlers and animation-event receivers
            // were never copied, so activation cannot install local gameplay components.
            if (PrefabUtility.SaveAsPrefabAsset(root, $"{Root}/{name}.prefab") == null)
                throw new InvalidOperationException("Could not save pure presentation: " + name);
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    private static Transform CloneTransforms(Transform source, Transform parent,
        Dictionary<Transform, Transform> map)
    {
        var target = new GameObject(source.name).transform;
        target.SetParent(parent, false);
        target.SetLocalPositionAndRotation(source.localPosition, source.localRotation);
        target.localScale = source.localScale;
        target.gameObject.layer = source.gameObject.layer;
        target.gameObject.SetActive(source.gameObject.activeSelf);
        map.Add(source, target);
        foreach (Transform child in source) CloneTransforms(child, target, map);
        return target;
    }

    private static RuntimeAnimatorController BuildController(string name, RuntimeAnimatorController original)
    {
        if (original == null) return null;
        string path = $"{Root}/{name}.controller";
        var controller = AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(path) ??
            UnityEditor.Animations.AnimatorController.CreateAnimatorControllerAtPath(path);
        AnimationClip[] clips = original.animationClips.Distinct().ToArray();
        AnimationClip idle = clips.FirstOrDefault(clip => Contains(clip.name, "idle")) ?? clips.FirstOrDefault();
        AnimationClip run = clips.FirstOrDefault(clip => Contains(clip.name, "run") || Contains(clip.name, "walk")) ?? idle;
        AnimationClip attack = clips.FirstOrDefault(clip => Contains(clip.name, "attack")) ?? idle;
        AnimatorStateMachine machine = controller.layers[0].stateMachine;
        foreach (var spec in new[] { ("Idle", idle), ("Run", run), ("Attack", attack) })
        {
            var state = machine.states.Select(value => value.state)
                .FirstOrDefault(value => value.name == spec.Item1) ?? machine.AddState(spec.Item1);
            state.motion = spec.Item2;
            if (spec.Item1 == "Idle") machine.defaultState = state;
        }
        EditorUtility.SetDirty(controller);
        return controller;
    }

    private static void ReadHitbox(GameObject source, HitRegion region, Bounds fallback,
        out Vector3 center, out Vector3 half)
    {
        DamageHitbox hit = source.GetComponentsInChildren<DamageHitbox>(true)
            .FirstOrDefault(value => value.Region == region);
        BoxCollider box = hit != null ? hit.GetComponent<BoxCollider>() : null;
        if (box != null)
        {
            center = source.transform.InverseTransformPoint(box.transform.TransformPoint(box.center));
            Vector3 scaled = source.transform.InverseTransformVector(box.transform.TransformVector(box.size * 0.5f));
            half = new Vector3(Mathf.Abs(scaled.x), Mathf.Abs(scaled.y), Mathf.Abs(scaled.z));
            return;
        }
        center = fallback.center;
        half = fallback.extents;
        if (region == HitRegion.Head)
        {
            center.y = fallback.max.y - fallback.size.y * 0.15f;
            half = Vector3.Scale(half, new Vector3(0.55f, 0.3f, 0.55f));
        }
    }

    private static Bounds BoundsOf(Renderer[] renderers)
    {
        if (renderers.Length == 0) throw new InvalidOperationException("Enemy has no renderer.");
        Bounds bounds = renderers[0].bounds;
        foreach (Renderer renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
        return bounds;
    }
    private static bool Contains(string text, string value) => text.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
    private static void EnsureFolder(string path)
    {
        string parent = System.IO.Path.GetDirectoryName(path)?.Replace('\\', '/');
        if (AssetDatabase.IsValidFolder(path)) return;
        if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
    }
}
