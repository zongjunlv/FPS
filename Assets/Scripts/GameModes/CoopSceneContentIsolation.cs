using FPS.Core.GameModes;
using FPS.Networking.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Co-op keeps the authored environment and shared HUD, but never the solo AI,
/// economy or defeat flow. Those controllers would compete with server state.
/// </summary>
public static class CoopSceneContentIsolation
{
    // Requested routes supersede the old active scene during a transition.
    // Otherwise leaving a co-op battle would also isolate the new solo rig.
    public static bool UsesAuthoritativeGameplay =>
        DedicatedServerRuntime.IsActive ||
        GameModeContext.RequestedMode == GameModeId.Coop &&
        GameModeContext.RequestedStage == GameModeStage.CoopBattle;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Register()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!UsesAuthoritativeGameplay) return;
        DisableLegacySceneEnemies();
    }

    public static int DisableLegacySceneEnemies()
    {
        int disabled = 0;
        disabled += DisableBehaviours<CityNewWaveBootstrap>();
        disabled += DisableBehaviours<CityNewTerminalMissionBootstrap>();
        disabled += DisableBehaviours<CityNewMissionController>();
        disabled += DisableBehaviours<TerminalMissionHudPresenter>();
        disabled += DisableLegacyPlayerEconomy();
        disabled += DisableLegacyWorldPickups();
        EnemyController[] enemies = Object.FindObjectsByType<EnemyController>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);
        for (int index = 0; index < enemies.Length; index++)
        {
            EnemyController enemy = enemies[index];
            if (enemy == null || !enemy.gameObject.scene.IsValid()) continue;
            enemy.gameObject.SetActive(false);
            disabled++;
        }
        if (disabled > 0)
            Debug.Log($"[COOP_CONTENT] 已隔离 {disabled} 个本地战斗/经济对象。");
        return disabled;
    }

    /// <summary>
    /// Retire logic only, not its reusable InventoryView/UpgradeChoiceView/HUD.
    /// Disabling is immediate; destruction runs existing OnDestroy unsubscribe
    /// contracts before the next frame and prevents a later re-enable.
    /// </summary>
    public static int DisableLegacyPlayerEconomy(GameObject playerRoot = null)
    {
        int disabled = RetireControllers<CityNewInventoryBootstrap>(playerRoot);
        int inventories = RetireControllers<PlayerInventoryController>(playerRoot);
        disabled += inventories;
        disabled += RetireControllers<PlayerUpgradeController>(playerRoot);
        disabled += RetireControllers<PlayerWorldPickupController>(playerRoot);
        disabled += RetireControllers<PlayerRunProgression>(playerRoot);
        disabled += RetireControllers<PlayerLootRewardController>(playerRoot);
        disabled += RetireControllers<WorldItemFactory>(playerRoot);
        disabled += RetireControllers<PlayerFailureFlowController>(playerRoot);
        if (inventories > 0) ClearLegacyQuickSlotBindings();
        return disabled;
    }

    private static int RetireControllers<T>(GameObject playerRoot)
        where T : MonoBehaviour
    {
        T[] controllers = playerRoot != null
            ? playerRoot.GetComponents<T>()
            : Object.FindObjectsByType<T>(FindObjectsInactive.Include,
                FindObjectsSortMode.None);
        int retired = 0;
        foreach (T controller in controllers)
        {
            if (controller == null || !controller.gameObject.scene.IsValid()) continue;
            // Died delegates still exist until OnDestroy. Suppress the solo
            // presentation now, even if a death arrives in this same frame.
            if (controller is PlayerFailureFlowController failure)
                failure.SetExternalPresentation(true);
            if (controller is CityNewInventoryBootstrap inventoryBootstrap)
                inventoryBootstrap.ReleaseGeneratedPickups();
            controller.StopAllCoroutines();
            controller.enabled = false;
            Object.Destroy(controller);
            retired++;
        }
        return retired;
    }

    private static void ClearLegacyQuickSlotBindings()
    {
        ConsumableQuickSlotHud[] quickSlots = Object.FindObjectsByType<ConsumableQuickSlotHud>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (ConsumableQuickSlotHud quickSlot in quickSlots)
        {
            if (quickSlot == null || !quickSlot.gameObject.scene.IsValid()) continue;
            quickSlot.Bind(null, null, null, null);
            UnifiedGameHud hud = quickSlot.GetComponent<UnifiedGameHud>();
            Transform root = hud?.HudLayer?.Find("ConsumableQuickSlotHud");
            if (root != null) root.gameObject.SetActive(false);
        }
    }

    private static int DisableLegacyWorldPickups()
    {
        int disabled = 0;
        WorldItemPickup[] pickups = Object.FindObjectsByType<WorldItemPickup>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (WorldItemPickup pickup in pickups)
        {
            if (pickup == null || !pickup.gameObject.scene.IsValid() ||
                !pickup.gameObject.activeSelf) continue;
            // Server packages are pure visuals and have no WorldItemPickup.
            pickup.gameObject.SetActive(false);
            disabled++;
        }
        return disabled;
    }

    private static int DisableBehaviours<T>() where T : Behaviour
    {
        int disabled = 0;
        T[] behaviours = Object.FindObjectsByType<T>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);
        for (int index = 0; index < behaviours.Length; index++)
        {
            T behaviour = behaviours[index];
            if (behaviour == null ||
                !behaviour.gameObject.scene.IsValid() ||
                !behaviour.enabled)
                continue;
            behaviour.enabled = false;
            disabled++;
        }
        return disabled;
    }

}
