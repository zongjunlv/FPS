using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class Issue68PlayerGameplayRigBuilder
{
    public const string RigPrefabPath =
        "Assets/Resources/Player/PlayerGameplayRig.prefab";
    public const string ValidationScenePath =
        "Assets/Scenes/Validation/PlayerGameplayRigValidation.unity";
    public const string CityNewScenePath =
        "Assets/ImportPackages/CSAssets2026/Scenes/CityNew.unity";

    [MenuItem("FPS/Content/Issue 68/Rebuild Player Gameplay Rig")]
    public static void RebuildAndMigrateCityNew()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            throw new InvalidOperationException(
                "Exit Play Mode before rebuilding the player gameplay rig.");
        }

        EnsureFolder("Assets", "Resources");
        EnsureFolder("Assets/Resources", "Player");
        EnsureFolder("Assets", "Scenes");
        EnsureFolder("Assets/Scenes", "Validation");
        BuildPrefabIfMissing();
        MigrateCityNew();
        BuildValidationScene();
        AssetDatabase.SaveAssets();
        Debug.Log(
            "Issue68: reusable player gameplay rig, CityNew instance, " +
            "and validation scene are ready.");
    }

    private static void BuildPrefabIfMissing()
    {
        GameObject existing =
            AssetDatabase.LoadAssetAtPath<GameObject>(RigPrefabPath);

        if (existing != null)
        {
            ValidatePrefab(existing);
            return;
        }

        Scene preview = EditorSceneManager.OpenPreviewScene(CityNewScenePath);
        GameObject clone = null;

        try
        {
            PlayerCombatCompositionRoot source = preview.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<
                    PlayerCombatCompositionRoot>(true))
                .SingleOrDefault();

            if (source == null)
            {
                throw new InvalidOperationException(
                    "CityNew must contain exactly one player combat root.");
            }

            clone = Object.Instantiate(source.gameObject);
            clone.name = "Player";
            clone.tag = "Player";
            clone.transform.SetParent(null);
            clone.transform.SetPositionAndRotation(
                Vector3.zero,
                Quaternion.identity);
            clone.transform.localScale = Vector3.one;
            clone.SetActive(true);

            PlayerGameplayRig rig =
                clone.GetComponent<PlayerGameplayRig>() ??
                clone.AddComponent<PlayerGameplayRig>();
            rig.RefreshReferences();

            if (!rig.TryValidate(out string error))
            {
                throw new InvalidOperationException(
                    $"Player gameplay rig source is invalid: {error}");
            }

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(
                clone,
                RigPrefabPath);

            if (saved == null)
            {
                throw new InvalidOperationException(
                    "Could not save the reusable player gameplay rig prefab.");
            }

            ValidatePrefab(saved);
        }
        finally
        {
            if (clone != null)
            {
                Object.DestroyImmediate(clone);
            }

            EditorSceneManager.ClosePreviewScene(preview);
        }
    }

    private static void MigrateCityNew()
    {
        Scene scene = SceneManager.GetSceneByPath(CityNewScenePath);

        if (!scene.IsValid() || !scene.isLoaded)
        {
            scene = EditorSceneManager.OpenScene(
                CityNewScenePath,
                OpenSceneMode.Single);
        }

        PlayerCombatCompositionRoot source = scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<
                PlayerCombatCompositionRoot>(true))
            .SingleOrDefault();

        if (source == null)
        {
            throw new InvalidOperationException(
                "CityNew must contain exactly one player combat root.");
        }

        GameObject sourceObject = source.gameObject;
        string currentSource =
            PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(
                sourceObject);

        if (!string.Equals(
                currentSource,
                RigPrefabPath,
                StringComparison.Ordinal))
        {
            List<string> externalReferences =
                FindExternalReferences(scene, sourceObject);

            if (externalReferences.Count > 0)
            {
                throw new InvalidOperationException(
                    "CityNew contains references into the old player hierarchy: " +
                    string.Join("; ", externalReferences));
            }

            Vector3 position = sourceObject.transform.position;
            Quaternion rotation = sourceObject.transform.rotation;
            Vector3 scale = sourceObject.transform.localScale;
            int siblingIndex = sourceObject.transform.GetSiblingIndex();
            int layer = sourceObject.layer;
            string tag = sourceObject.tag;
            bool active = sourceObject.activeSelf;
            GameObject prefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(RigPrefabPath);
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(
                prefab,
                scene);
            instance.name = "Player";
            instance.tag = tag;
            instance.layer = layer;
            instance.transform.SetPositionAndRotation(position, rotation);
            instance.transform.localScale = scale;
            instance.transform.SetSiblingIndex(siblingIndex);
            instance.SetActive(active);
            Object.DestroyImmediate(sourceObject);
            Selection.activeGameObject = instance;
        }

        EnsureCityNewInstaller(scene);
        EditorSceneManager.MarkSceneDirty(scene);

        if (!EditorSceneManager.SaveScene(scene))
        {
            throw new InvalidOperationException("Could not save the migrated CityNew scene.");
        }
    }

    private static void EnsureCityNewInstaller(Scene scene)
    {
        CityNewPlayerModeInstaller[] installers = scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<
                CityNewPlayerModeInstaller>(true))
            .ToArray();

        if (installers.Length == 1)
        {
            return;
        }

        if (installers.Length > 1)
        {
            throw new InvalidOperationException(
                "CityNew contains more than one player mode installer.");
        }

        GameObject installerObject = new GameObject("CityNew Player Mode");
        SceneManager.MoveGameObjectToScene(installerObject, scene);
        installerObject.AddComponent<CityNewPlayerModeInstaller>();
    }

    private static void BuildValidationScene()
    {
        Scene previous = SceneManager.GetActiveScene();
        Scene validation = EditorSceneManager.NewScene(
            NewSceneSetup.EmptyScene,
            NewSceneMode.Additive);

        try
        {
            SceneManager.SetActiveScene(validation);
            GameObject prefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(RigPrefabPath);
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(
                prefab,
                validation);
            instance.transform.SetPositionAndRotation(
                new Vector3(0f, 0.16f, 0f),
                Quaternion.identity);

            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Validation Ground";
            ground.transform.localScale = new Vector3(4f, 1f, 4f);

            GameObject lightObject = new GameObject(
                "Validation Light",
                typeof(Light));
            Light light = lightObject.GetComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            lightObject.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            if (!EditorSceneManager.SaveScene(validation, ValidationScenePath))
            {
                throw new InvalidOperationException(
                    "Could not save the player gameplay rig validation scene.");
            }
        }
        finally
        {
            EditorSceneManager.CloseScene(validation, true);

            if (previous.IsValid() && previous.isLoaded)
            {
                SceneManager.SetActiveScene(previous);
            }
        }
    }

    private static void ValidatePrefab(GameObject prefab)
    {
        PlayerGameplayRig rig = prefab.GetComponent<PlayerGameplayRig>();

        string error = rig == null
            ? "PlayerGameplayRig component is missing."
            : string.Empty;

        if (rig == null || !rig.TryValidate(out error))
        {
            throw new InvalidOperationException(
                $"Saved player gameplay rig is invalid: {error}");
        }

        if (prefab.tag != "Player" ||
            prefab.transform.localPosition != Vector3.zero ||
            prefab.transform.localRotation != Quaternion.identity ||
            prefab.transform.localScale != Vector3.one)
        {
            throw new InvalidOperationException(
                "The reusable player rig root must use the Player tag and an identity transform.");
        }
    }

    private static List<string> FindExternalReferences(
        Scene scene,
        GameObject player)
    {
        var references = new List<string>();

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (Component component in
                     root.GetComponentsInChildren<Component>(true))
            {
                if (component == null || component.transform.IsChildOf(player.transform))
                {
                    continue;
                }

                var serialized = new SerializedObject(component);
                SerializedProperty property = serialized.GetIterator();
                bool enterChildren = true;

                while (property.NextVisible(enterChildren))
                {
                    enterChildren = false;

                    if (property.propertyType !=
                        SerializedPropertyType.ObjectReference ||
                        property.objectReferenceValue == null)
                    {
                        continue;
                    }

                    Transform referenced = GetTransform(
                        property.objectReferenceValue);

                    if (referenced != null &&
                        referenced.IsChildOf(player.transform))
                    {
                        references.Add(
                            $"{component.name}/{component.GetType().Name}." +
                            property.propertyPath);
                    }
                }
            }
        }

        return references;
    }

    private static Transform GetTransform(Object value)
    {
        return value switch
        {
            GameObject gameObject => gameObject.transform,
            Component component => component.transform,
            _ => null
        };
    }

    private static void EnsureFolder(string parent, string child)
    {
        string path = parent + "/" + child;

        if (!AssetDatabase.IsValidFolder(path))
        {
            AssetDatabase.CreateFolder(parent, child);
        }
    }
}
