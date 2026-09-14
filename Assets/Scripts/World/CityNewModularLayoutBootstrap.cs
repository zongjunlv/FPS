using System;
using System.Collections;
using System.Collections.Generic;
using FPS.SaveGame;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

[DefaultExecutionOrder(-2000)]
public sealed class CityNewModularLayoutBootstrap : MonoBehaviour
{
    private static readonly Vector3 LegacyPlayerSpawn =
        new(49.761f, 0.16f, 59.719f);
    private static readonly Vector3 LegacyExtraction =
        new(48.414f, 0.05f, 41.41f);
    private static CityNewModularLayoutBootstrap instance;

    private readonly List<RendererState> legacyRenderers = new();
    private readonly List<ColliderState> legacyColliders = new();
    private readonly List<Vector3> combatCenters = new();
    private readonly List<Vector3> enemySpawnPoints = new();
    private GameObject generatedRoot;
    private Transform legacyEnvironment;
    private ModularCombatLayoutSet layoutSet;

    public static CityNewModularLayoutBootstrap Active => instance;
    public static bool IsGeometryReady => instance != null && instance.GeometryReady;
    public static bool IsSceneReady => instance != null && instance.IsReady;

    public static int SelectNewRunSeed(int proposedSeed)
    {
        return instance != null && !instance.IsUsingFallback &&
               instance.layoutSet != null &&
               instance.CurrentPlan != null
            ? CombatLayoutReroll.SelectSeedForDifferentRoute(
                instance.layoutSet,
                instance.CurrentPlan,
                proposedSeed)
            : proposedSeed;
    }

    public bool GeometryReady { get; private set; }
    public bool IsReady { get; private set; }
    public bool IsUsingFallback { get; private set; }
    public CombatLayoutPlan CurrentPlan { get; private set; }
    public Vector3 PlayerSpawn { get; private set; }
    public GameObject TerminalObject { get; private set; }
    public Vector3 ExtractionPoint { get; private set; }
    public IReadOnlyList<Vector3> CombatCenters => combatCenters;
    public IReadOnlyList<Vector3> EnemySpawnPoints => enemySpawnPoints;
    public string LastDiagnostic { get; private set; } = string.Empty;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => instance = null;

    public static CityNewModularLayoutBootstrap EnsureForActiveScene()
    {
        if (SceneManager.GetActiveScene().name != "CityNew") return null;
        if (instance != null) return instance;
        var root = new GameObject("Modular Combat Layout Bootstrap");
        instance = root.AddComponent<CityNewModularLayoutBootstrap>();
        return instance;
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            enabled = false;
            return;
        }
        instance = this;
    }

    private IEnumerator Start()
    {
        CityNewContentCatalog catalog = CityNewContentCatalog.LoadDefault();
        layoutSet = catalog != null ? catalog.LayoutSet : null;
        GameObject player = GameObject.FindGameObjectWithTag("Player");

        if (catalog == null || !catalog.UseRuntimeModularLayout)
        {
            ActivateLegacyFallback(
                "正式战局已停用模块化测试区域，使用 CityNew 原始场景。");
            yield break;
        }

        int sceneSeed = player != null &&
                        player.GetComponent<PlayerUpgradeController>() != null
            ? player.GetComponent<PlayerUpgradeController>().RunSeed
            : 18018;
        RunSnapshotSession.PeekLayoutBootstrap(
            sceneSeed,
            out int runSeed,
            out LayoutSaveSnapshot savedLayout);

        if (layoutSet == null)
        {
            ActivateLegacyFallback("内容阶段失败：布局集为空。");
            yield break;
        }
        if (!layoutSet.TryValidate(out string catalogError))
        {
            ActivateLegacyFallback("内容阶段失败：" + catalogError);
            yield break;
        }

        CombatLayoutPlan plan;
        if (savedLayout != null)
        {
            if (!CombatLayoutPersistence.TryRestore(
                    layoutSet,
                    savedLayout,
                    runSeed,
                    out plan,
                    out string restoreError))
            {
                ActivateLegacyFallback("存档布局恢复失败：" + restoreError);
                yield break;
            }
        }
        else
        {
            plan = DeterministicModularLayoutGenerator.Generate(layoutSet, runSeed);
        }

        if (plan.UsedFallback && plan.Placements.Count == 0)
        {
            ActivateLegacyFallback(plan.Diagnostic);
            yield break;
        }
        string failure = string.Empty;
        if (!CombatLayoutGraphValidator.TryValidate(layoutSet, plan, out string graphError))
            failure = graphError;
        else if (!TryInstantiate(plan, out string instantiateError))
            failure = instantiateError;
        if (!string.IsNullOrEmpty(failure))
        {
            ActivateLegacyFallback("布局装配失败：" + failure);
            yield break;
        }

        CurrentPlan = plan;
        IsUsingFallback = false;
        LastDiagnostic = string.IsNullOrEmpty(plan.Diagnostic)
            ? $"布局 {plan.LayoutId} 已装配，等待导航校验。"
            : plan.Diagnostic;
        GeometryReady = true;
        MovePlayerToSpawn(player);
    }

    public bool TryFinalizeNavigation(out string error)
    {
        if (IsUsingFallback)
        {
            IsReady = true;
            error = string.Empty;
            return true;
        }
        if (!GeometryReady || CurrentPlan == null)
        {
            error = "布局几何尚未准备完成。";
            return false;
        }
        var required = new List<(string label, Vector3 point)>
        {
            ("玩家出生点", PlayerSpawn),
            ("终端", TerminalObject != null ? TerminalObject.transform.position : Vector3.positiveInfinity),
            ("撤离点", ExtractionPoint)
        };
        for (int index = 0; index < combatCenters.Count; index++)
            required.Add(($"战斗区 {index + 1}", combatCenters[index]));

        if (!NavMesh.SamplePosition(PlayerSpawn, out NavMeshHit origin, 3f, NavMesh.AllAreas))
        {
            error = "导航阶段失败：玩家出生点未落在 NavMesh 上。";
            return false;
        }
        foreach ((string label, Vector3 point) in required)
        {
            if (!Finite(point) ||
                !NavMesh.SamplePosition(point, out NavMeshHit destination, 3f, NavMesh.AllAreas) ||
                !CompletePath(origin.position, destination.position))
            {
                error = $"导航阶段失败：{label} 无法从出生区到达。";
                return false;
            }
        }
        for (int index = 0; index < enemySpawnPoints.Count; index++)
        {
            if (!NavMesh.SamplePosition(enemySpawnPoints[index], out NavMeshHit spawn, 3f,
                    NavMesh.AllAreas) || !CompletePath(origin.position, spawn.position))
            {
                error = $"导航阶段失败：敌人出生点 {index + 1} 无法到达玩法区域。";
                return false;
            }
        }
        LastDiagnostic = $"布局 {CurrentPlan.LayoutId} 已通过连接、碰撞、任务点、出生点与导航可达性校验。";
        IsReady = true;
        error = string.Empty;
        return true;
    }

    public void ActivateLegacyFallback(string diagnostic)
    {
        if (generatedRoot != null) Destroy(generatedRoot);
        generatedRoot = null;
        RestoreLegacyEnvironment();
        combatCenters.Clear();
        enemySpawnPoints.Clear();
        GameObject terminal = FindLegacy("controlunit");
        GameObject extraction = FindLegacy("Point light (1)");
        PlayerSpawn = LegacyPlayerSpawn;
        TerminalObject = terminal;
        ExtractionPoint = extraction != null
            ? new Vector3(extraction.transform.position.x, LegacyExtraction.y,
                extraction.transform.position.z)
            : LegacyExtraction;
        combatCenters.Add(new Vector3(50f, 0f, 60f));
        CurrentPlan = new CombatLayoutPlan(
            RunSnapshotSession.PeekSeed(18018),
            layoutSet != null ? layoutSet.ContentVersion : 1,
            layoutSet != null ? layoutSet.GeneratorVersion : 1,
            Array.Empty<CombatLayoutPlacement>(),
            Array.Empty<CombatLayoutConnection>(),
            true,
            diagnostic);
        IsUsingFallback = true;
        GeometryReady = true;
        IsReady = false;
        LastDiagnostic = "已切换安全布局：" + diagnostic;
        Debug.LogWarning("[ModularLayout] " + LastDiagnostic, this);
    }

    public void MarkFallbackNavigationReady()
    {
        IsReady = true;
        LastDiagnostic += " 安全布局导航已恢复，可继续完整战局。";
    }

    public LayoutSaveSnapshot CaptureSnapshot() =>
        CombatLayoutPersistence.ToSnapshot(CurrentPlan);

    private bool TryInstantiate(CombatLayoutPlan plan, out string error)
    {
        var prefabs = new Dictionary<string, GameObject>(StringComparer.Ordinal);
        foreach (CombatLayoutPlacement placement in plan.Placements)
        {
            CombatAreaModuleDefinition definition = layoutSet.Find(placement.DefinitionId);
            GameObject prefab = definition != null
                ? Resources.Load<GameObject>(definition.ResourceAddress)
                : null;
            if (prefab == null)
            {
                error = $"模块 '{placement.DefinitionId}' 的资源地址无效。";
                return false;
            }
            prefabs[placement.InstanceId] = prefab;
        }

        generatedRoot = new GameObject("GENERATED COMBAT LAYOUT");
        Vector3 origin = new(50f, 0.04f, 50f);
        combatCenters.Clear();
        enemySpawnPoints.Clear();
        TerminalObject = null;
        foreach (CombatLayoutPlacement placement in plan.Placements)
        {
            CombatAreaModuleDefinition definition = layoutSet.Find(placement.DefinitionId);
            GameObject module = Instantiate(prefabs[placement.InstanceId], generatedRoot.transform);
            module.name = placement.InstanceId + " [" + definition.StableId + "]";
            module.transform.position = origin + new Vector3(
                placement.GridX * layoutSet.TileSize,
                0f,
                placement.GridZ * layoutSet.TileSize);
            module.transform.rotation = Quaternion.Euler(0f, placement.QuarterTurns * 90f, 0f);
            foreach (LayoutPointDefinition point in definition.TaskPoints)
            {
                Vector3 world = module.transform.TransformPoint(point.LocalPosition);
                switch (point.StableId)
                {
                    case "player_spawn": PlayerSpawn = world; break;
                    case "combat_center": combatCenters.Add(world); break;
                    case "terminal":
                        TerminalObject = FindDescendant(module.transform, "Terminal")?.gameObject;
                        if (TerminalObject == null)
                        {
                            error = $"事件模块 '{definition.StableId}' 缺少 Terminal 对象。";
                            Destroy(generatedRoot);
                            generatedRoot = null;
                            return false;
                        }
                        break;
                    case "extraction": ExtractionPoint = world; break;
                }
            }
            foreach (LayoutPointDefinition point in definition.EnemySpawnPoints)
                enemySpawnPoints.Add(module.transform.TransformPoint(point.LocalPosition));
        }
        RelocateSceneEnemies();
        HideLegacyEnvironment();
        Physics.SyncTransforms();
        error = string.Empty;
        return true;
    }

    private void RelocateSceneEnemies()
    {
        if (combatCenters.Count == 0) return;
        EnemyNavigationController[] enemies =
            FindObjectsByType<EnemyNavigationController>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);
        for (int index = 0; index < enemies.Length; index++)
        {
            Vector3 center = combatCenters[index % combatCenters.Count];
            float lane = index / combatCenters.Count;
            enemies[index].transform.position =
                center + new Vector3(lane * 1.5f, 0.15f, 0f);
        }
    }

    private void HideLegacyEnvironment()
    {
        legacyEnvironment = GameObject.Find("Other")?.transform.Find("GameObject");
        if (legacyEnvironment == null) return;
        legacyRenderers.Clear();
        legacyColliders.Clear();
        foreach (Renderer renderer in legacyEnvironment.GetComponentsInChildren<Renderer>(true))
        {
            legacyRenderers.Add(new RendererState(renderer, renderer.enabled));
            renderer.enabled = false;
        }
        foreach (Collider collider in legacyEnvironment.GetComponentsInChildren<Collider>(true))
        {
            legacyColliders.Add(new ColliderState(collider, collider.enabled));
            collider.enabled = false;
        }
    }

    private void RestoreLegacyEnvironment()
    {
        foreach (RendererState state in legacyRenderers)
            if (state.Value != null) state.Value.enabled = state.Enabled;
        foreach (ColliderState state in legacyColliders)
            if (state.Value != null) state.Value.enabled = state.Enabled;
        legacyRenderers.Clear();
        legacyColliders.Clear();
        Physics.SyncTransforms();
    }

    private void MovePlayerToSpawn(GameObject player)
    {
        if (player == null) return;
        CharacterController controller = player.GetComponent<CharacterController>();
        bool wasEnabled = controller != null && controller.enabled;
        if (wasEnabled) controller.enabled = false;
        player.transform.SetPositionAndRotation(PlayerSpawn, Quaternion.identity);
        if (wasEnabled) controller.enabled = true;
        RelocateStarterPickups();
    }

    private void RelocateStarterPickups()
    {
        WorldItemPickup[] pickups = FindObjectsByType<WorldItemPickup>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);
        if (pickups.Length == 0) return;

        Vector3 centroid = Vector3.zero;
        foreach (WorldItemPickup pickup in pickups)
            centroid += pickup.transform.position;
        centroid /= pickups.Length;

        Vector3 targetCentroid = PlayerSpawn + new Vector3(0f, 0.03f, 3f);
        Vector3 offset = targetCentroid - centroid;
        foreach (WorldItemPickup pickup in pickups)
            pickup.transform.position += offset;
        Physics.SyncTransforms();
    }

    private static bool CompletePath(Vector3 from, Vector3 to)
    {
        var path = new NavMeshPath();
        return NavMesh.CalculatePath(from, to, NavMesh.AllAreas, path) &&
               path.status == NavMeshPathStatus.PathComplete;
    }

    private static bool Finite(Vector3 value) =>
        !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
        !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
        !float.IsNaN(value.z) && !float.IsInfinity(value.z);

    private static Transform FindDescendant(Transform root, string name)
    {
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            if (child.name == name) return child;
        return null;
    }

    private static GameObject FindLegacy(string name)
    {
        Transform root = GameObject.Find("Other")?.transform.Find("GameObject");
        return root != null ? FindDescendant(root, name)?.gameObject : null;
    }

    private void OnDestroy()
    {
        RestoreLegacyEnvironment();
        if (instance == this) instance = null;
    }

    private readonly struct RendererState
    {
        public RendererState(Renderer value, bool enabled)
        { Value = value; Enabled = enabled; }
        public Renderer Value { get; }
        public bool Enabled { get; }
    }

    private readonly struct ColliderState
    {
        public ColliderState(Collider value, bool enabled)
        { Value = value; Enabled = enabled; }
        public Collider Value { get; }
        public bool Enabled { get; }
    }

}
