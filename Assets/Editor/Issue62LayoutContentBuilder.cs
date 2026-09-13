using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public static class Issue62LayoutContentBuilder
{
    private const string Root = "Assets/Resources/Content/CityNew/Layouts";
    private const string PrefabRoot = Root + "/Prefabs";
    private const float TileSize = 16f;

    [MenuItem("FPS/Content/Rebuild CityNew Modular Layout Assets")]
    public static ModularCombatLayoutSet Build()
    {
        EnsureFolder("Assets/Resources/Content/CityNew", "Layouts");
        EnsureFolder(Root, "Prefabs");

        Material floorMaterial = LoadMaterial(
            "Assets/ImportPackages/CSAssets2026/LowPolyBuildings/Models/parkinglot_part.fbx");
        Material wallMaterial = LoadMaterial(
            "Assets/ImportPackages/CSAssets2026/LowPolyBuildings/Prefabs/Stone/Wall_Concrete.prefab");

        BuildPrefab("SpawnYard", CombatAreaModuleKind.Spawn,
            floorMaterial, wallMaterial);
        BuildPrefab("CombatOpen", CombatAreaModuleKind.Combat,
            floorMaterial, wallMaterial);
        BuildPrefab("CombatAlley", CombatAreaModuleKind.Combat,
            floorMaterial, wallMaterial);
        BuildPrefab("CombatRuins", CombatAreaModuleKind.Combat,
            floorMaterial, wallMaterial);
        BuildPrefab("CombatIndustrial", CombatAreaModuleKind.Combat,
            floorMaterial, wallMaterial);
        BuildPrefab("TerminalDepot", CombatAreaModuleKind.Event,
            floorMaterial, wallMaterial);
        BuildPrefab("ExtractionRuins", CombatAreaModuleKind.Extraction,
            floorMaterial, wallMaterial);

        LayoutConnectorDefinition[] connectors = Connectors();
        var definitions = new List<CombatAreaModuleDefinition>
        {
            Module("Modules/SpawnYard.asset", "city_new.module.spawn_yard",
                CombatAreaModuleKind.Spawn, "SpawnYard", connectors,
                new[] { Point("player_spawn", new Vector3(0f, 0.16f, 0f)) },
                Array.Empty<LayoutPointDefinition>()),
            Module("Modules/CombatOpen.asset", "city_new.module.combat_open",
                CombatAreaModuleKind.Combat, "CombatOpen", connectors,
                new[] { Point("combat_center", Vector3.zero) }, Spawns()),
            Module("Modules/CombatAlley.asset", "city_new.module.combat_alley",
                CombatAreaModuleKind.Combat, "CombatAlley", connectors,
                new[] { Point("combat_center", Vector3.zero) }, Spawns()),
            Module("Modules/CombatRuins.asset", "city_new.module.combat_ruins",
                CombatAreaModuleKind.Combat, "CombatRuins", connectors,
                new[] { Point("combat_center", Vector3.zero) }, Spawns()),
            Module("Modules/CombatIndustrial.asset", "city_new.module.combat_industrial",
                CombatAreaModuleKind.Combat, "CombatIndustrial", connectors,
                new[] { Point("combat_center", Vector3.zero) }, Spawns()),
            Module("Modules/TerminalDepot.asset", "city_new.module.terminal_depot",
                CombatAreaModuleKind.Event, "TerminalDepot", connectors,
                new[] { Point("terminal", new Vector3(0f, 0f, 3.5f)) },
                Array.Empty<LayoutPointDefinition>()),
            Module("Modules/ExtractionRuins.asset", "city_new.module.extraction_ruins",
                CombatAreaModuleKind.Extraction, "ExtractionRuins", connectors,
                new[] { Point("extraction", Vector3.zero) },
                Array.Empty<LayoutPointDefinition>())
        };

        ModularCombatLayoutSet set = Asset<ModularCombatLayoutSet>(
            "CityNewModularLayoutSet.asset");
        set.Configure("city_new.layouts.default", 1, 1, TileSize, 3, definitions);
        EditorUtility.SetDirty(set);
        foreach (CombatAreaModuleDefinition definition in definitions)
            EditorUtility.SetDirty(definition);
        AssetDatabase.SaveAssets();
        return set;
    }

    private static CombatAreaModuleDefinition Module(
        string relativePath,
        string id,
        CombatAreaModuleKind kind,
        string prefabName,
        IEnumerable<LayoutConnectorDefinition> connectors,
        IEnumerable<LayoutPointDefinition> tasks,
        IEnumerable<LayoutPointDefinition> spawns)
    {
        CombatAreaModuleDefinition definition = Asset<CombatAreaModuleDefinition>(
            relativePath);
        definition.Configure(
            id,
            kind,
            "Content/CityNew/Layouts/Prefabs/" + prefabName,
            Vector2Int.one,
            connectors,
            tasks,
            spawns);
        return definition;
    }

    private static LayoutConnectorDefinition[] Connectors() => new[]
    {
        new LayoutConnectorDefinition("north", LayoutConnectorDirection.North, CombatAreaModuleKindMask.All),
        new LayoutConnectorDefinition("east", LayoutConnectorDirection.East, CombatAreaModuleKindMask.All),
        new LayoutConnectorDefinition("south", LayoutConnectorDirection.South, CombatAreaModuleKindMask.All),
        new LayoutConnectorDefinition("west", LayoutConnectorDirection.West, CombatAreaModuleKindMask.All)
    };

    private static LayoutPointDefinition[] Spawns() => new[]
    {
        Point("enemy_center", Vector3.zero),
        Point("enemy_north", new Vector3(0f, 0f, 4f)),
        Point("enemy_south", new Vector3(0f, 0f, -4f))
    };

    private static LayoutPointDefinition Point(string id, Vector3 position) =>
        new(id, position);

    private static void BuildPrefab(
        string name,
        CombatAreaModuleKind kind,
        Material floorMaterial,
        Material wallMaterial)
    {
        var root = new GameObject(name);
        try
        {
            Primitive(root.transform, "Floor", new Vector3(0f, -0.2f, 0f),
                new Vector3(TileSize, 0.4f, TileSize), floorMaterial);
            AddBoundary(root.transform, wallMaterial);
            AddDecoration(root.transform, name, kind);
            PrefabUtility.SaveAsPrefabAsset(root, PrefabRoot + "/" + name + ".prefab");
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    private static void AddBoundary(Transform root, Material material)
    {
        const float edge = 7.8f;
        const float segment = 4.4f;
        foreach (float offset in new[] { -5.7f, 5.7f })
        {
            Primitive(root, "Boundary", new Vector3(offset, 0.8f, edge),
                new Vector3(segment, 1.6f, 0.35f), material);
            Primitive(root, "Boundary", new Vector3(offset, 0.8f, -edge),
                new Vector3(segment, 1.6f, 0.35f), material);
            Primitive(root, "Boundary", new Vector3(edge, 0.8f, offset),
                new Vector3(0.35f, 1.6f, segment), material);
            Primitive(root, "Boundary", new Vector3(-edge, 0.8f, offset),
                new Vector3(0.35f, 1.6f, segment), material);
        }
    }

    private static void AddDecoration(
        Transform root,
        string name,
        CombatAreaModuleKind kind)
    {
        if (name == "CombatOpen" || kind == CombatAreaModuleKind.Spawn)
        {
            PlaceModel(root,
                "Assets/ImportPackages/CSAssets2026/LowPolyBuildings/Prefabs/concrete_barrier.fbx",
                "ConcreteCover", new Vector3(-2.5f, 0f, 1.5f),
                Quaternion.Euler(0f, 90f, 0f), Vector3.one);
            PlaceModel(root,
                "Assets/ImportPackages/CSAssets2026/LowPolyBuildings/Prefabs/concrete_barrier.fbx",
                "ConcreteCover", new Vector3(3f, 0f, -2f),
                Quaternion.identity, Vector3.one);
        }
        if (name == "CombatAlley")
        {
            PlaceModel(root,
                "Assets/ImportPackages/CSAssets2026/LowPolyBuildings/Models/container.fbx",
                "Container", new Vector3(-4.8f, 0f, 0f),
                Quaternion.Euler(270f, 0f, 0f), Vector3.one);
            PlaceModel(root,
                "Assets/ImportPackages/CSAssets2026/LowPolyBuildings/Models/container.fbx",
                "Container", new Vector3(4.8f, 0f, 1.5f),
                Quaternion.Euler(270f, 180f, 0f), Vector3.one);
        }
        if (name == "CombatRuins" || name == "ExtractionRuins")
        {
            PlaceModel(root,
                "Assets/ImportPackages/CSAssets2026/LowPolyBuildings/Prefabs/house_destroyed.fbx",
                "DestroyedHouse", new Vector3(4.8f, 0f, 4.8f),
                Quaternion.Euler(0f, 35f, 0f), Vector3.one * 0.72f);
        }
        if (name == "CombatIndustrial" || name == "TerminalDepot")
        {
            PlaceModel(root,
                "Assets/ImportPackages/CSAssets2026/LowPolyBuildings/Prefabs/silo.fbx",
                "Silo", new Vector3(-4.5f, 0f, -3.8f),
                Quaternion.identity, Vector3.one);
            PlaceModel(root,
                "Assets/ImportPackages/CSAssets2026/LowPolyBuildings/Prefabs/garage.fbx",
                "Garage", new Vector3(4.5f, 0f, -4.5f),
                Quaternion.Euler(270f, 0f, 0f), Vector3.one);
        }
        if (kind == CombatAreaModuleKind.Event)
        {
            GameObject terminal = PlaceModel(root,
                "Assets/ImportPackages/CSAssets2026/LowPolyBuildings/Models/controlunit.fbx",
                "Terminal", new Vector3(0f, 0f, 3.5f),
                Quaternion.Euler(270f, 180f, 0f), Vector3.one * 2.2f);
            if (terminal != null && terminal.GetComponentInChildren<Collider>() == null)
                terminal.AddComponent<BoxCollider>();
        }
        if (kind == CombatAreaModuleKind.Extraction)
        {
            var marker = new GameObject("ExtractionBeacon");
            marker.transform.SetParent(root, false);
            marker.transform.localPosition = new Vector3(0f, 2.5f, 0f);
            Light light = marker.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(0.1f, 1f, 0.8f);
            light.range = 10f;
            light.intensity = 3f;
        }
    }

    private static GameObject PlaceModel(
        Transform parent,
        string path,
        string name,
        Vector3 position,
        Quaternion rotation,
        Vector3 scale)
    {
        GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (asset == null) return null;
        GameObject instance = PrefabUtility.InstantiatePrefab(asset) as GameObject;
        if (instance == null) return null;
        instance.name = name;
        instance.transform.SetParent(parent, false);
        instance.transform.localPosition = position;
        instance.transform.localRotation = rotation;
        instance.transform.localScale = scale;
        return instance;
    }

    private static GameObject Primitive(
        Transform parent,
        string name,
        Vector3 position,
        Vector3 scale,
        Material material)
    {
        GameObject value = GameObject.CreatePrimitive(PrimitiveType.Cube);
        value.name = name;
        value.transform.SetParent(parent, false);
        value.transform.localPosition = position;
        value.transform.localScale = scale;
        if (material != null) value.GetComponent<MeshRenderer>().sharedMaterial = material;
        return value;
    }

    private static Material LoadMaterial(string assetPath)
    {
        GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
        return asset != null
            ? asset.GetComponentInChildren<Renderer>(true)?.sharedMaterial
            : null;
    }

    private static T Asset<T>(string relativePath) where T : ScriptableObject
    {
        string path = Root + "/" + relativePath;
        string directory = System.IO.Path.GetDirectoryName(path)?.Replace('\\', '/');
        if (!AssetDatabase.IsValidFolder(directory))
        {
            string parent = System.IO.Path.GetDirectoryName(directory)?.Replace('\\', '/');
            EnsureFolder(parent, System.IO.Path.GetFileName(directory));
        }
        T value = AssetDatabase.LoadAssetAtPath<T>(path);
        if (value != null) return value;
        value = ScriptableObject.CreateInstance<T>();
        AssetDatabase.CreateAsset(value, path);
        return value;
    }

    private static void EnsureFolder(string parent, string name)
    {
        string path = parent.TrimEnd('/') + "/" + name;
        if (!AssetDatabase.IsValidFolder(path)) AssetDatabase.CreateFolder(parent, name);
    }
}
