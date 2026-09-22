using FPS.Core.GameModes;
using FPS.Networking.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// CityNew keeps one authored Spider in the scene as the solo-mode factory
/// template. Co-op uses server-authoritative presentation objects instead, so
/// that local actor must never remain active in a network battle.
/// </summary>
public static class CoopSceneContentIsolation
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Register()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!ShouldIsolate()) return;
        DisableLegacySceneEnemies();
    }

    public static int DisableLegacySceneEnemies()
    {
        int disabled = 0;
        disabled += DisableBehaviours<CityNewWaveBootstrap>();
        disabled += DisableBehaviours<CityNewTerminalMissionBootstrap>();
        disabled += DisableBehaviours<CityNewMissionController>();
        disabled += DisableBehaviours<TerminalMissionHudPresenter>();
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
            Debug.Log($"[COOP_CONTENT] 已隔离 {disabled} 个本地敌人模板。 ");
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

    private static bool ShouldIsolate() =>
        DedicatedServerRuntime.IsActive ||
        GameModeContext.IsActive(GameModeId.Coop,
            GameModeStage.CoopBattle) ||
        GameModeContext.RequestedMode == GameModeId.Coop &&
        GameModeContext.RequestedStage == GameModeStage.CoopBattle;
}
