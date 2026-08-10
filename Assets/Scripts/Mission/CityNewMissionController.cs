using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

[RequireComponent(
    typeof(Health),
    typeof(PlayerController),
    typeof(PlayerCombatController))]
public sealed class CityNewMissionController : MonoBehaviour
{
    private readonly MissionFlowStateMachine flow = new();
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
    private GUIStyle resultBodyStyle;
    private GUIStyle buttonStyle;
    private bool configured;
    private GameplayLockCoordinator gameplayLocks;
    private GameplayLockLease outcomeLock;
    private WaveDirector waveDirector;

    public TerminalInteractable Terminal { get; private set; }
    public Health TargetHealth { get; private set; }
    public MissionExtractionZone ExtractionZone { get; private set; }
    public MissionRunStatistics Statistics => statistics;
    public MissionFlowState State => flow.State;
    public bool ExtractionAvailable =>
        State == MissionFlowState.ExtractionAvailable;
    public bool QuitRequested { get; private set; }

    private void Awake()
    {
        playerHealth = GetComponent<Health>();
        player = GetComponent<PlayerController>();
        combat = GetComponent<PlayerCombatController>();
        gameplayLocks = GetComponent<GameplayLockCoordinator>();
        profile = Resources.Load<PlayerHudVisualProfile>(
            "PlayerHudVisualProfile");
    }

    private void Update()
    {
        if (!configured)
        {
            return;
        }

        if (!flow.IsOutcome &&
            !player.IsPaused &&
            Time.timeScale > 0f)
        {
            statistics.AdvanceTime(Time.unscaledDeltaTime);
        }

        if (flow.IsOutcome &&
            Keyboard.current != null &&
            Keyboard.current.rKey.wasPressedThisFrame)
        {
            RestartLevel();
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
        statistics.Reset();
        flow.Configure(1);
        QuitRequested = false;
        CreateExtractionZone(extractionPosition);

        if (Terminal != null)
        {
            Terminal.Completed += HandleTerminalCompleted;

            if (Terminal.State == TerminalInteractionState.Completed)
            {
                flow.CompleteTerminal();
            }
        }

        if (TargetHealth != null)
        {
            TargetHealth.Died += HandleTargetEliminated;

            if (TargetHealth.IsDead)
            {
                flow.RegisterTargetEliminated();
            }
        }

        playerHealth.Damaged += HandlePlayerDamaged;
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
        flow.Configure(1);
        QuitRequested = false;
        CreateExtractionZone(extractionPosition);

        if (Terminal != null)
        {
            Terminal.Completed += HandleTerminalCompleted;

            if (Terminal.State == TerminalInteractionState.Completed)
            {
                flow.CompleteTerminal();
            }
        }

        if (waveDirector != null)
        {
            waveDirector.WaveCompleted += HandleWaveCompleted;

            if (waveDirector.IsCompleted)
            {
                flow.RegisterTargetEliminated();
            }
        }

        playerHealth.Damaged += HandlePlayerDamaged;
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
        if (!configured ||
            actor != gameObject ||
            !flow.TryExtract())
        {
            return false;
        }

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

    public bool RestartLevel()
    {
        if (!flow.IsOutcome && !player.IsPaused)
        {
            return false;
        }

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
            waveDirector.WaveCompleted -= HandleWaveCompleted;
            waveDirector = null;
        }

        if (playerHealth != null)
        {
            playerHealth.Damaged -= HandlePlayerDamaged;
            playerHealth.Died -= HandlePlayerDied;
        }

        if (combat != null)
        {
            combat.ShotResolved -= HandleShotResolved;
        }

        flow.StateChanged -= HandleStateChanged;
    }

    private void HandleTerminalCompleted(
        TerminalInteractable completedTerminal)
    {
        flow.CompleteTerminal();
    }

    private void HandleTargetEliminated()
    {
        flow.RegisterTargetEliminated();
    }

    private void HandleWaveCompleted()
    {
        flow.RegisterTargetEliminated();
    }

    private void HandlePlayerDamaged(DamageInfo damage)
    {
        if (!flow.IsOutcome)
        {
            statistics.RegisterDamageTaken();
        }
    }

    private void HandlePlayerDied()
    {
        if (flow.Fail())
        {
            ApplyOutcome();
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
        ExtractionZone?.SetAvailable(
            state == MissionFlowState.ExtractionAvailable);
    }

    private void ApplyOutcome()
    {
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
            DrawResult();
            return;
        }

        DrawObjective();

        if (player.IsPaused)
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
                $"清除高价值目标   " +
                $"{flow.EliminatedTargets}/{flow.RequiredTargets}",
            MissionFlowState.ExtractionAvailable =>
                "前往撤离点   已开放",
            _ => string.Empty
        };
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
        Rect panel = CenterPanel(440f, 360f);
        DrawPanel(panel);
        GUI.Label(
            new Rect(panel.x, panel.y + 35f, panel.width, 58f),
            "PAUSED",
            resultTitleStyle);
        float buttonX = panel.center.x - 130f;

        if (GUI.Button(
                new Rect(buttonX, panel.y + 125f, 260f, 48f),
                "继续游戏",
                buttonStyle))
        {
            ResumeGame();
        }

        if (GUI.Button(
                new Rect(buttonX, panel.y + 190f, 260f, 48f),
                "重新开始",
                buttonStyle))
        {
            RestartLevel();
        }

        if (GUI.Button(
                new Rect(buttonX, panel.y + 255f, 260f, 48f),
                "退出游戏",
                buttonStyle))
        {
            RequestQuit();
        }
    }

    private void DrawResult()
    {
        GUI.depth = -100;
        DrawScreenDim();
        Rect panel = CenterPanel(620f, 500f);
        DrawPanel(panel);
        string title =
            State == MissionFlowState.Victory
                ? "MISSION COMPLETE"
                : "MISSION FAILED";
        GUI.Label(
            new Rect(panel.x, panel.y + 30f, panel.width, 62f),
            title,
            resultTitleStyle);
        string summary =
            $"完成时间    {FormatTime(statistics.ElapsedSeconds)}\n" +
            $"开火数      {statistics.ShotsFired}\n" +
            $"命中数      {statistics.Hits}\n" +
            $"命中率      {statistics.Accuracy * 100f:0.0}%\n" +
            $"击杀数      {statistics.Kills}\n" +
            $"受伤次数    {statistics.DamageTakenCount}";
        GUI.Label(
            new Rect(panel.x + 120f, panel.y + 110f, 380f, 240f),
            summary,
            resultBodyStyle);

        if (GUI.Button(
                new Rect(
                    panel.center.x - 150f,
                    panel.yMax - 90f,
                    300f,
                    52f),
                "重新开始  [R]",
                buttonStyle))
        {
            RestartLevel();
        }
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

    private static string FormatTime(float seconds)
    {
        int totalSeconds = Mathf.Max(0, Mathf.FloorToInt(seconds));
        return $"{totalSeconds / 60:00}:{totalSeconds % 60:00}";
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
        resultBodyStyle = new GUIStyle(objectiveStyle)
        {
            fontSize = 20,
            alignment = TextAnchor.UpperLeft
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
