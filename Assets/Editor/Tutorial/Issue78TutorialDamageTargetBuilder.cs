using System;
using System.Linq;
using FPS.Core.GameModes;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;

public static class Issue78TutorialDamageTargetBuilder
{
    private const string RootName = "Tutorial Damage Training Target";
    private const string PresentationName = "Training Dummy Presentation";
    private const string BodyName = "Body Damage Zone";
    private const string HeadName = "Head Damage Zone";

    [MenuItem("FPS/Content/Issue 78/Rebuild Damage Training Target")]
    public static void Rebuild()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            throw new InvalidOperationException(
                "Exit Play Mode before rebuilding the damage target.");
        }

        Issue72TutorialContentBuilder.EnsureDefinition();
        Scene scene = EditorSceneManager.OpenScene(
            GameModeScenePaths.Tutorial,
            OpenSceneMode.Single);
        EnsureSceneContent(scene);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, GameModeScenePaths.Tutorial);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("Issue #78 静止部位伤害训练单位与数值反馈已接入。");
    }

    public static TutorialDamageTrainingTarget EnsureSceneContent(Scene scene)
    {
        TutorialFlowController flow = scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<
                TutorialFlowController>(true))
            .Single();
        TutorialTrainingEnvironment environment = flow.Environment;
        Transform root = environment.transform.Find(RootName);
        if (root == null)
        {
            root = new GameObject(RootName).transform;
            root.SetParent(environment.transform, true);
        }

        Vector3 towardPlayer = environment.SpawnPoint.position -
                               environment.DamageTrainingPoint.position;
        towardPlayer.y = 0f;
        if (towardPlayer.sqrMagnitude < 0.001f)
        {
            towardPlayer = Vector3.back;
        }
        root.SetPositionAndRotation(
            environment.DamageTrainingPoint.position,
            Quaternion.LookRotation(towardPlayer.normalized, Vector3.up));
        root.localScale = Vector3.one;

        Health health = GetOrAdd<Health>(root.gameObject);
        Transform presentation = EnsureChild(root, PresentationName);
        Material bodyMaterial = AssetDatabase.LoadAssetAtPath<Material>(
            "Assets/Resources/Tutorial/Materials/" +
            "TutorialTargetAccent.mat");
        Material headMaterial = AssetDatabase.LoadAssetAtPath<Material>(
            "Assets/Resources/Tutorial/Materials/" +
            "TutorialTargetCenter.mat");
        if (bodyMaterial == null || headMaterial == null)
        {
            throw new InvalidOperationException(
                "Issue #75 target materials are required for the damage dummy.");
        }

        DamageHitbox body = EnsureHitbox(
            presentation,
            BodyName,
            PrimitiveType.Cube,
            new Vector3(0f, 1.15f, 0f),
            new Vector3(1.05f, 1.7f, 0.58f),
            bodyMaterial,
            health,
            1f,
            HitRegion.Body);
        DamageHitbox head = EnsureHitbox(
            presentation,
            HeadName,
            PrimitiveType.Sphere,
            new Vector3(0f, 2.35f, 0f),
            new Vector3(0.72f, 0.72f, 0.72f),
            headMaterial,
            health,
            2f,
            HitRegion.Head);
        EnsureBase(presentation, bodyMaterial);

        TutorialDamageTrainingTarget target =
            GetOrAdd<TutorialDamageTrainingTarget>(root.gameObject);
        target.Configure(
            flow,
            environment.PlayerRig,
            presentation.gameObject,
            health,
            body,
            head,
            250f,
            80f);
        presentation.gameObject.SetActive(false);

        EditorUtility.SetDirty(health);
        EditorUtility.SetDirty(body);
        EditorUtility.SetDirty(head);
        EditorUtility.SetDirty(target);
        return target;
    }

    private static DamageHitbox EnsureHitbox(
        Transform parent,
        string name,
        PrimitiveType primitiveType,
        Vector3 localPosition,
        Vector3 localScale,
        Material material,
        Health health,
        float multiplier,
        HitRegion region)
    {
        Transform child = parent.Find(name);
        GameObject hitboxObject = child != null
            ? child.gameObject
            : GameObject.CreatePrimitive(primitiveType);
        hitboxObject.name = name;
        hitboxObject.layer = 0;
        hitboxObject.transform.SetParent(parent, false);
        hitboxObject.transform.SetLocalPositionAndRotation(
            localPosition,
            Quaternion.identity);
        hitboxObject.transform.localScale = localScale;

        Renderer renderer = hitboxObject.GetComponent<Renderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.On;
        renderer.receiveShadows = true;
        DamageHitbox hitbox = GetOrAdd<DamageHitbox>(hitboxObject);
        hitbox.ConfigureRegion(health, multiplier, region);
        EditorUtility.SetDirty(renderer);
        return hitbox;
    }

    private static void EnsureBase(Transform parent, Material material)
    {
        const string name = "Training Dummy Base";
        Transform child = parent.Find(name);
        GameObject baseObject = child != null
            ? child.gameObject
            : GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        baseObject.name = name;
        baseObject.transform.SetParent(parent, false);
        baseObject.transform.SetLocalPositionAndRotation(
            new Vector3(0f, 0.12f, 0f),
            Quaternion.identity);
        baseObject.transform.localScale = new Vector3(0.85f, 0.12f, 0.85f);
        Collider collider = baseObject.GetComponent<Collider>();
        if (collider != null)
        {
            UnityEngine.Object.DestroyImmediate(collider);
        }
        Renderer renderer = baseObject.GetComponent<Renderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.On;
        renderer.receiveShadows = true;
        EditorUtility.SetDirty(renderer);
    }

    private static Transform EnsureChild(Transform parent, string name)
    {
        Transform child = parent.Find(name);
        if (child == null)
        {
            child = new GameObject(name).transform;
            child.SetParent(parent, false);
        }
        child.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        child.localScale = Vector3.one;
        return child;
    }

    private static T GetOrAdd<T>(GameObject target)
        where T : Component
    {
        T component = target.GetComponent<T>();
        return component != null ? component : target.AddComponent<T>();
    }
}
