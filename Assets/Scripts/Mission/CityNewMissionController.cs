using System;
using System.Collections.Generic;
using FPS.Simulation;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

[RequireComponent(
    typeof(Health),
    typeof(PlayerController),
    typeof(PlayerCombatController))]
public sealed class CityNewMissionController : MonoBehaviour
{
    private MissionFlowStateMachine flow = new();
    private RunSimulationKernel simulation;
    private readonly MissionRunStatistics statistics = new();
    private Health playerHealth;
    private PlayerController player;
    private PlayerCombatController combat;
    private PlayerFailureFlowController failure;
    private PlayerHudVisualProfile profile;
    private GUIStyle headerStyle;
    private GUIStyle objectiveStyle;
    private GUIStyle markerStyle;
    private GUIStyle resultTitleStyle;
    private GUIStyle buttonStyle;
    private bool configured;
    private GameplayLockCoordinator gameplayLocks;
    private GameplayLockLease outcomeLock;
    private WaveDirector waveDirector;
    private PlayerRunProgression progression;
    private PlayerUpgradeController upgrades;
    private PlayerLootRewardController lootRewards;
    private UnifiedGameHud hud;
    private MissionOutcomeView outcomeView;
    private RunSnapshotMenu snapshotMenu;

    public TerminalInteractable Terminal { get; private set; }
    public Health TargetHealth { get; private set; }
    public MissionExtractionZone ExtractionZone { get; private set; }
    public MissionRunStatistics Statistics => statistics;
    public MissionFlowState State => flow.State;
    public bool ExtractionAvailable =>
        State == MissionFlowState.ExtractionAvailable;
    public bool QuitRequested { get; private set; }
    public bool IsRestarting { get; private set; }
    public MissionRunSummary OutcomeSummary { get; private set; }
    public MissionOutcomeView OutcomeView => outcomeView;

    private void Awake()
    {
        playerHealth = GetComponent<Health>();
        player = GetComponent<PlayerController>();
        combat = GetComponent<PlayerCombatController>();
        gameplayLocks = GetComponent<GameplayLockCoordinator>();
        progression = GetComponent<PlayerRunProgression>();
        upgrades = GetComponent<PlayerUpgradeController>();
        lootRewards = GetComponent<PlayerLootRewardController>();
        snapshotMenu = GetComponent<RunSnapshotMenu>();
        profile = Resources.Load<PlayerHudVisualProfile>(
            "PlayerHudVisualProfile");
    }

    private void Update()
    {
        if (!configured)
        {
            return;
        }

        if (flow.IsOutcome)
        {
            EnsureOutcomePresentation();

            if (Keyboard.current != null &&
                Keyboard.current.rKey.wasPressedThisFrame)
            {
                RestartLevel();
            }

            return;
        }

        if (!player.IsPaused &&
            Time.timeScale > 0f)
        {
            statistics.AdvanceTime(Time.unscaledDeltaTime);
        }
    }

    public void Configure(
        TerminalInteractable terminal,
        Health targetHealth,
        Vector3 extractionPosition)
    {
        Unbind();
        Terminal = terminal;
        TargetHealth = targetHealth;
        simulation = null;
        statistics.Reset();
        flow = new MissionFlowStateMachine();
        flow.Configure(1);
        QuitRequested = false;
        IsRestarting = false;
        OutcomeSummary = default;
        CreateExtractionZone(extractionPosition);

        if (Terminal != null)
        {
            Terminal.Completed += HandleTerminalCompleted;
        }

        if (TargetHealth != null)
        {
            TargetHealth.Died += HandleTargetEliminated;

            if (TargetHealth.IsDead)
            {
                flow.RegisterTargetEliminated();
            }
        }

        CompleteAlreadyActivatedTerminal();

        playerHealth.DamageApplied += HandlePlayerDamageApplied;
        playerHealth.Died += HandlePlayerDied;
        combat.ShotResolved += HandleShotResolved;
        flow.StateChanged += HandleStateChanged;
        failure = GetComponent<PlayerFailureFlowController>();
        failure?.SetExternalPresentation(true);
        TerminalMissionHudPresenter legacyHud =
            GetComponent<TerminalMissionHudPresenter>();
        legacyHud?.SetVisible(false);
        configured = true;
        HandleStateChanged(flow.State);
    }

    public void ConfigureWave(
        TerminalInteractable terminal,
        WaveDirector configuredWaveDirector,
        Vector3 extractionPosition)
    {
        Unbind();
        Terminal = terminal;
        TargetHealth = null;
        waveDirector = configuredWaveDirector;
        statistics.Reset();
        statistics.UseAuthoritativeKillTracking();
        flow = new MissionFlowStateMachine();
        flow.Configure(1);
        QuitRequested = false;
        IsRestarting = false;
        OutcomeSummary = default;
        CreateExtractionZone(extractionPosition);

        if (Terminal != null)
        {
            Terminal.Completed += HandleTerminalCompleted;
        }

        if (waveDirector != null)
        {
            waveDirector.EnemyDied += HandleEnemyDied;
            waveDirector.WaveEnded += HandleWaveEnded;
            waveDirector.WaveCompleted += HandleWaveCompleted;
            waveDirector.SimulationReady += HandleSimulationReady;
            if (waveDirector.Simulation != null)
            {
                AttachSimulation(waveDirector.Simulation);
            }

            if (waveDirector.IsCompleted)
            {
                if (simulation == null)
                {
                    flow.RegisterTargetEliminated();
                }
            }
        }

        CompleteAlreadyActivatedTerminal();

        playerHealth.DamageApplied += HandlePlayerDamageApplied;
        playerHealth.Died += HandlePlayerDied;
        combat.ShotResolved += HandleShotResolved;
        flow.StateChanged += HandleStateChanged;
        failure = GetComponent<PlayerFailureFlowController>();
        failure?.SetExternalPresentation(true);
        TerminalMissionHudPresenter legacyHud =
            GetComponent<TerminalMissionHudPresenter>();
        legacyHud?.SetVisible(false);
        configured = true;
        HandleStateChanged(flow.State);
    }

    public bool TryEnterExtraction(GameObject actor)
    {
        if (!configured || actor != gameObject)
        {
            return false;
        }
        bool extracted = simulation != null
            ? simulation.Submit(SimulationCommand.Create(
                simulation.Tick,
                SimulationCommandType.EnterExtraction))
            : flow.TryExtract();
        if (!extracted)
        {
            return false;
        }

        GetComponent<EncounterRuntimeController>()
            ?.NotifyExtractionReached();

        EndRunSystems();
        ApplyOutcome();
        return true;
    }

    public bool ResumeGame()
    {
        if (flow.IsOutcome || !player.IsPaused)
        {
            return false;
        }

        player.SetPaused(false);
        return true;
    }

    public bool SaveRunSnapshot()
    {
        snapshotMenu ??= GetComponent<RunSnapshotMenu>();
        return snapshotMenu != null &&
               snapshotMenu.SaveTo(RunSnapshotSession.DefaultPath);
    }

    public bool LoadRunSnapshot()
    {
        snapshotMenu ??= GetComponent<RunSnapshotMenu>();
        return snapshotMenu != null &&
               snapshotMenu.LoadFrom(RunSnapshotSession.DefaultPath);
    }

    public bool StartNewRun()
    {
        snapshotMenu ??= GetComponent<RunSnapshotMenu>();
        return snapshotMenu != null && snapshotMenu.StartNewGame();
    }

    public MissionFlowRestoreState CaptureMissionState()
    {
        return flow.CaptureState();
    }

    public float CaptureTerminalProgress()
    {
        return Terminal != null ? Terminal.ProgressNormalized : 0f;
    }

    public bool TryRestoreMissionSilently(
        MissionFlowRestoreState snapshot,
        float terminalProgressNormalized,
        out string error)
    {
        if (Terminal == null ||
            float.IsNaN(terminalProgressNormalized) ||
            float.IsInfinity(terminalProgressNormalized) ||
            terminalProgressNormalized < 0f ||
            terminalProgressNormalized > 1f ||
            snapshot.TerminalCompleted &&
            terminalProgressNormalized < 1f)
        {
            error = "终端存档状态无效。";
            return false;
        }

        if (simulation != null)
        {
            MissionFlowRestoreState current = flow.CaptureState();
            if (current.State != snapshot.State ||
                current.RequiredTargets != snapshot.RequiredTargets ||
                current.EliminatedTargets != snapshot.EliminatedTargets ||
                current.TerminalCompleted != snapshot.TerminalCompleted)
            {
                error = "任务状态与权威战局内核不一致。";
                return false;
            }
        }
        else if (!flow.TryRestoreSilently(snapshot, out error))
        {
            return false;
        }

        // Inputs were validated before the flow commit, so this cannot fail.
        Terminal.TryRestoreSilently(
            snapshot.TerminalCompleted,
            terminalProgressNormalized);
        HandleStateChanged(flow.State);
        error = string.Empty;
        return true;
    }

    public bool RestartLevel()
    {
        if (IsRestarting || (!flow.IsOutcome && !player.IsPaused))
        {
            return false;
        }

        IsRestarting = true;
        EndRunSystems();
        waveDirector?.StopRun(State == MissionFlowState.Defeat
            ? WaveStopReason.PlayerDied
            : WaveStopReason.Reconfigured);
        outcomeView?.Hide();
        hud?.SetOutcomePresentation(false);
        outcomeLock?.Dispose();
        outcomeLock = null;
        gameplayLocks?.ResetForSceneTransition();
        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        return true;
    }

    public void RequestQuit()
    {
        QuitRequested = true;

        if (!Application.isEditor)
        {
            Application.Quit();
        }
    }

    private void OnDestroy()
    {
        outcomeLock?.Dispose();
        outcomeLock = null;
        Unbind();
    }

    private void Unbind()
    {
        if (Terminal != null)
        {
            Terminal.Completed -= HandleTerminalCompleted;
        }

        if (TargetHealth != null)
        {
            TargetHealth.Died -= HandleTargetEliminated;
        }

        if (waveDirector != null)
        {
            waveDirector.EnemyDied -= HandleEnemyDied;
            waveDirector.WaveEnded -= HandleWaveEnded;
            waveDirector.WaveCompleted -= HandleWaveCompleted;
            waveDirector.SimulationReady -= HandleSimulationReady;
            waveDirector = null;
        }

        if (playerHealth != null)
        {
            playerHealth.DamageApplied -= HandlePlayerDamageApplied;
            playerHealth.Died -= HandlePlayerDied;
        }

        if (combat != null)
        {
            combat.ShotResolved -= HandleShotResolved;
        }

        flow.StateChanged -= HandleStateChanged;
        simulation = null;
    }

    private void HandleTerminalCompleted(
        TerminalInteractable completedTerminal)
    {
        if (simulation != null)
        {
            simulation.Submit(SimulationCommand.Create(
                simulation.Tick,
                SimulationCommandType.TerminalCompleted));
            return;
        }
        flow.CompleteTerminal();
    }

    private void HandleTargetEliminated()
    {
        flow.RegisterTargetEliminated();
        CompleteAlreadyActivatedTerminal();
    }

    private void HandleWaveCompleted()
    {
        if (simulation == null)
        {
            flow.RegisterTargetEliminated();
        }
        CompleteAlreadyActivatedTerminal();
    }

    private void HandleEnemyDied(EnemyDeathEvent death)
    {
        if (!flow.IsOutcome)
        {
            statistics.RegisterEnemyDeath(death, gameObject);
        }
    }

    private void HandleWaveEnded(int waveNumber)
    {
        if (!flow.IsOutcome)
        {
            statistics.RegisterWaveCompleted(waveNumber);
        }
    }

    private void HandlePlayerDamageApplied(DamageResult result)
    {
        if (!flow.IsOutcome)
        {
            statistics.RegisterAppliedDamage(result);
        }
    }

    private void HandlePlayerDied()
    {
        bool failed = simulation != null
            ? simulation.Submit(SimulationCommand.Create(
                simulation.Tick,
                SimulationCommandType.PlayerDefeated))
            : flow.Fail();
        if (failed)
        {
            waveDirector?.StopRun(WaveStopReason.PlayerDied);
            EndRunSystems();
            ApplyOutcome();
        }
    }

    private void HandleSimulationReady(RunSimulationKernel readySimulation)
    {
        AttachSimulation(readySimulation);
        HandleStateChanged(flow.State);
    }

    private void AttachSimulation(RunSimulationKernel readySimulation)
    {
        if (readySimulation == null || ReferenceEquals(simulation, readySimulation))
        {
            return;
        }
        bool wasConfigured = configured;
        flow.StateChanged -= HandleStateChanged;
        simulation = readySimulation;
        flow = simulation.Mission;
        if (wasConfigured)
        {
            flow.StateChanged += HandleStateChanged;
        }
    }

    private void HandleShotResolved(ShotResult result)
    {
        if (!flow.IsOutcome)
        {
            statistics.RegisterShot(result);
        }
    }

    private void HandleStateChanged(MissionFlowState state)
    {
        Terminal?.SetMissionAvailable(
            state == MissionFlowState.ActivateTerminal);
        ExtractionZone?.SetAvailable(
            state == MissionFlowState.ExtractionAvailable);
    }

    private void ApplyOutcome()
    {
        if (!OutcomeSummary.IsValid)
        {
            OutcomeSummary = CaptureOutcomeSummary();
        }

        GameplayLockReason reason = State == MissionFlowState.Victory
            ? GameplayLockReason.Victory
            : GameplayLockReason.Defeat;
        outcomeLock ??= gameplayLocks != null
            ? gameplayLocks.Acquire(reason)
            : null;

        if (gameplayLocks == null)
        {
            player.SetGameplayInputEnabled(false);
            combat.SetGameplayInputEnabled(false);
            Time.timeScale = 0f;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        EnsureOutcomePresentation();
    }

    private void EndRunSystems()
    {
        lootRewards ??= GetComponent<PlayerLootRewardController>();
        progression ??= GetComponent<PlayerRunProgression>();
        upgrades ??= GetComponent<PlayerUpgradeController>();
        lootRewards?.EndRun();
        progression?.EndRun();
        upgrades?.EndRun();
        hud ??= FindAnyObjectByType<UnifiedGameHud>();
        hud?.ClearRewardCues();
    }

    private MissionRunSummary CaptureOutcomeSummary()
    {
        int completedWaves = waveDirector != null
            ? Mathf.Max(statistics.CompletedWaves, waveDirector.CompletedWaveCount)
            : statistics.CompletedWaves;
        int totalWaves = waveDirector != null
            ? waveDirector.CurrentProgress.TotalWaves
            : 1;
        int level = progression != null
            ? progression.CurrentProgress.Level
            : 1;
        return new MissionRunSummary(
            State,
            completedWaves,
            totalWaves,
            statistics.Kills,
            statistics.ShotsFired,
            statistics.Hits,
            statistics.ElapsedSeconds,
            statistics.DamageTakenCount,
            statistics.DamageTakenAmount,
            level,
            BuildSelectedUpgradeSummary());
    }

    private IReadOnlyList<string> BuildSelectedUpgradeSummary()
    {
        if (upgrades == null || upgrades.SelectionHistory.Count == 0)
        {
            return Array.Empty<string>();
        }

        var definitions = new Dictionary<string, UpgradeDefinition>(
            StringComparer.Ordinal);
        for (int index = 0; index < upgrades.AvailableUpgrades.Count; index++)
        {
            UpgradeDefinition definition = upgrades.AvailableUpgrades[index];
            if (definition != null && !string.IsNullOrWhiteSpace(definition.StableId))
            {
                definitions[definition.StableId] = definition;
            }
        }

        var orderedIds = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int index = 0; index < upgrades.SelectionHistory.Count; index++)
        {
            string id = upgrades.SelectionHistory[index];
            if (seen.Add(id))
            {
                orderedIds.Add(id);
            }
        }

        var result = new List<string>(orderedIds.Count);
        for (int index = 0; index < orderedIds.Count; index++)
        {
            string id = orderedIds[index];
            string title = definitions.TryGetValue(id, out UpgradeDefinition definition) &&
                           !string.IsNullOrWhiteSpace(definition.Title)
                ? definition.Title
                : id;
            result.Add($"{title}  ×{upgrades.GetUpgradeLevel(id)}");
        }
        return result;
    }

    private void EnsureOutcomeView()
    {
        hud ??= FindAnyObjectByType<UnifiedGameHud>();
        if (hud == null || hud.OutcomeLayer == null)
        {
            return;
        }

        outcomeView ??= hud.GetComponent<MissionOutcomeView>();
        outcomeView ??= hud.gameObject.AddComponent<MissionOutcomeView>();
        outcomeView.Initialize(hud.OutcomeLayer);
    }

    private void EnsureOutcomePresentation()
    {
        if (!flow.IsOutcome || !OutcomeSummary.IsValid ||
            (outcomeView != null && outcomeView.IsVisible))
        {
            return;
        }

        EnsureOutcomeView();

        if (outcomeView == null)
        {
            return;
        }

        hud?.ClearRewardCues();
        hud?.SetOutcomePresentation(true);
        outcomeView.Show(OutcomeSummary, () => RestartLevel());
    }

    private void CreateExtractionZone(Vector3 position)
    {
        if (ExtractionZone != null)
        {
            Destroy(ExtractionZone.gameObject);
        }

        GameObject zone = new GameObject("MISSION EXTRACTION ZONE");
        zone.transform.position = position;
        ExtractionZone = zone.AddComponent<MissionExtractionZone>();
        ExtractionZone.Configure(this, gameObject, 3f);
    }

    private void OnGUI()
    {
        if (!configured)
        {
            return;
        }

        EnsureStyles();

        if (flow.IsOutcome)
        {
            return;
        }

        DrawObjective();

        if (player.IsPaused &&
            (gameplayLocks == null ||
             gameplayLocks.IsTopmost(GameplayLockReason.PauseMenu)))
        {
            DrawPauseMenu();
        }
    }

    private void DrawObjective()
    {
        GUI.depth = -25;
        Color previous = GUI.color;
        Rect panel = new Rect(14f, 14f, 270f, 70f);
        GUI.color = profile != null
            ? profile.PanelColor
            : new Color(0.02f, 0.03f, 0.04f, 0.82f);
        GUI.DrawTexture(panel, Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUI.Label(
            new Rect(panel.x + 12f, panel.y + 7f, 246f, 18f),
            "OBJECTIVE",
            headerStyle);
        GUI.Label(
            new Rect(panel.x + 12f, panel.y + 29f, 246f, 30f),
            GetObjectiveText(),
            objectiveStyle);
        DrawWorldMarker();
        GUI.color = previous;
    }

    private string GetObjectiveText()
    {
        return State switch
        {
            MissionFlowState.ActivateTerminal =>
                "接入城市控制终端   0/1",
            MissionFlowState.EliminateTargets =>
                waveDirector != null
                    ? "清除所有波次怪物"
                    : $"清除高价值目标   " +
                      $"{flow.EliminatedTargets}/{flow.RequiredTargets}",
            MissionFlowState.ExtractionAvailable =>
                "前往撤离点   已开放",
            _ => string.Empty
        };
    }

    private void CompleteAlreadyActivatedTerminal()
    {
        if (State == MissionFlowState.ActivateTerminal &&
            Terminal != null &&
            Terminal.State == TerminalInteractionState.Completed)
        {
            if (simulation != null)
            {
                simulation.Submit(SimulationCommand.Create(
                    simulation.Tick,
                    SimulationCommandType.TerminalCompleted));
            }
            else
            {
                flow.CompleteTerminal();
            }
        }
    }

    private void DrawWorldMarker()
    {
        Camera camera = player.AimCamera;
        Transform target = State switch
        {
            MissionFlowState.ActivateTerminal =>
                Terminal != null ? Terminal.transform : null,
            MissionFlowState.EliminateTargets =>
                TargetHealth != null ? TargetHealth.transform : null,
            MissionFlowState.ExtractionAvailable =>
                ExtractionZone != null
                    ? ExtractionZone.transform
                    : null,
            _ => null
        };

        if (camera == null || target == null)
        {
            return;
        }

        Vector3 screen = camera.WorldToScreenPoint(
            target.position + Vector3.up * 1.2f);

        if (screen.z <= 0f)
        {
            screen.x = Screen.width - screen.x;
            screen.y = Screen.height - screen.y;
        }

        float x = Mathf.Clamp(screen.x, 70f, Screen.width - 70f);
        float y = Mathf.Clamp(
            Screen.height - screen.y,
            105f,
            Screen.height - 70f);
        float distance = Vector3.Distance(
            transform.position,
            target.position);
        string label = State switch
        {
            MissionFlowState.EliminateTargets =>
                $"◆ 高价值目标  {distance:0}m",
            MissionFlowState.ExtractionAvailable =>
                $"◆ 撤离点  {distance:0}m",
            _ => $"◆ 终端  {distance:0}m"
        };
        GUI.Label(
            new Rect(x - 100f, y - 16f, 200f, 32f),
            label,
            markerStyle);
    }

    private void DrawPauseMenu()
    {
        GUI.depth = -90;
        DrawScreenDim();
        Rect panel = CenterPanel(480f, 560f);
        DrawPanel(panel);
        GUI.Label(
            new Rect(panel.x, panel.y + 24f, panel.width, 58f),
            "暂停菜单",
            resultTitleStyle);
        float buttonX = panel.center.x - 130f;
        float buttonY = panel.y + 96f;
        const float buttonStep = 62f;

        if (GUI.Button(
                new Rect(buttonX, buttonY, 260f, 46f),
                "继续游戏",
                buttonStyle))
        {
            ResumeGame();
        }

        if (GUI.Button(
                new Rect(buttonX, buttonY + buttonStep, 260f, 46f),
                "保存当前战局",
                buttonStyle))
        {
            SaveRunSnapshot();
        }

        if (GUI.Button(
                new Rect(buttonX, buttonY + buttonStep * 2f, 260f, 46f),
                snapshotMenu != null && snapshotMenu.RequiresRecoveryChoice
                    ? "再次尝试读取"
                    : "读取战局",
                buttonStyle))
        {
            LoadRunSnapshot();
        }

        if (GUI.Button(
                new Rect(buttonX, buttonY + buttonStep * 3f, 260f, 46f),
                snapshotMenu != null && snapshotMenu.RequiresRecoveryChoice
                    ? "开始新战局（保留损坏文件）"
                    : "开始新战局（保留存档）",
                buttonStyle))
        {
            StartNewRun();
        }

        if (GUI.Button(
                new Rect(buttonX, buttonY + buttonStep * 4f, 260f, 46f),
                "退出游戏",
                buttonStyle))
        {
            RequestQuit();
        }

        snapshotMenu ??= GetComponent<RunSnapshotMenu>();
        string status = snapshotMenu != null
            ? snapshotMenu.StatusMessage
            : "存档系统尚未就绪。";
        GUI.Label(
            new Rect(panel.x + 35f, panel.y + 416f, panel.width - 70f, 76f),
            status,
            objectiveStyle);
        GUI.Label(
            new Rect(panel.x + 35f, panel.y + 515f, panel.width - 70f, 24f),
            "ESC  返回游戏",
            headerStyle);
    }

    private static void DrawScreenDim()
    {
        Color previous = GUI.color;
        GUI.color = new Color(0.01f, 0.015f, 0.02f, 0.9f);
        GUI.DrawTexture(
            new Rect(0f, 0f, Screen.width, Screen.height),
            Texture2D.whiteTexture);
        GUI.color = previous;
    }

    private void DrawPanel(Rect panel)
    {
        Color previous = GUI.color;
        GUI.color = profile != null
            ? profile.PanelColor
            : new Color(0.02f, 0.025f, 0.03f, 0.96f);
        GUI.DrawTexture(panel, Texture2D.whiteTexture);
        GUI.color = previous;
    }

    private static Rect CenterPanel(float width, float height)
    {
        width = Mathf.Min(width, Screen.width * 0.86f);
        height = Mathf.Min(height, Screen.height * 0.82f);
        return new Rect(
            (Screen.width - width) * 0.5f,
            (Screen.height - height) * 0.5f,
            width,
            height);
    }

    private void EnsureStyles()
    {
        if (headerStyle != null)
        {
            return;
        }

        Font font = profile != null ? profile.Font : null;
        headerStyle = new GUIStyle(GUI.skin.label)
        {
            font = font,
            fontSize = 12,
            fontStyle = FontStyle.Bold,
            normal = { textColor = new Color(1f, 0.76f, 0.16f) }
        };
        objectiveStyle = new GUIStyle(headerStyle)
        {
            fontSize = 16,
            normal = { textColor = Color.white }
        };
        markerStyle = new GUIStyle(objectiveStyle)
        {
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = new Color(0.1f, 1f, 0.72f) }
        };
        resultTitleStyle = new GUIStyle(objectiveStyle)
        {
            fontSize = 38,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        };
        buttonStyle = new GUIStyle(GUI.skin.button)
        {
            font = font,
            fontSize = 19,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        };
    }
}
