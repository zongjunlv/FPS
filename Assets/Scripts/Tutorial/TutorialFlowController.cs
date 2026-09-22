using FPS.Core.GameModes;
using UnityEngine;

[DisallowMultipleComponent]
[DefaultExecutionOrder(-50)]
public sealed class TutorialFlowController : MonoBehaviour
{
    [SerializeField] private TutorialSequenceDefinition definition;
    [SerializeField] private TutorialTrainingEnvironment environment;

    public TutorialSequenceDefinition Definition => definition;
    public TutorialTrainingEnvironment Environment => environment;
    public TutorialProgressionStateMachine Progression { get; private set; }
    public TutorialTopHud Hud { get; private set; }
    public TutorialPauseView PauseView { get; private set; }
    public TutorialCompletionView CompletionView { get; private set; }
    public bool IsInitialized { get; private set; }
    public bool IsLeavingTutorial { get; private set; }
    public string InitializationError { get; private set; } = string.Empty;

    public void Configure(
        TutorialSequenceDefinition configuredDefinition,
        TutorialTrainingEnvironment configuredEnvironment)
    {
        definition = configuredDefinition;
        environment = configuredEnvironment;
    }

    public bool ReportEvidence(
        TutorialEvidenceType evidenceType,
        float amount = 1f,
        string evidenceKey = null)
    {
        return IsInitialized &&
               Progression.ReportEvidence(evidenceType, amount, evidenceKey);
    }

    public void ResetProgress()
    {
        if (IsInitialized && !IsLeavingTutorial)
        {
            SetPlayerInputEnabled(true);
            CompletionView?.SetVisible(false);
            Progression.Reset();
        }
    }

    public bool TryEnterBattleMode()
    {
        return TryLeaveTutorial(
            flow => flow.TryEnterMode(GameModeId.SoloBattle));
    }

    public bool TryRestartTutorial()
    {
        return TryLeaveTutorial(
            flow => flow.TryEnterMode(GameModeId.Tutorial));
    }

    public bool TryReturnToModeEntry()
    {
        return TryLeaveTutorial(flow => flow.TryReturnToEntry());
    }

    public bool ResumeTutorial()
    {
        PlayerController player = environment != null
            ? environment.PlayerRig?.Player
            : null;
        if (!IsInitialized || IsLeavingTutorial ||
            player == null || !player.IsPaused)
        {
            return false;
        }

        player.SetPaused(false);
        PauseView?.SetVisible(false);
        return true;
    }

    private void Start()
    {
        if (!GameModeContext.IsActive(
                GameModeId.Tutorial,
                GameModeStage.Tutorial))
        {
            Fail("当前模式不是新手教学，教学流程未启动。");
            return;
        }

        if (environment == null)
        {
            Fail("训练场无效：缺少训练环境引用。");
            return;
        }

        if (!environment.TryValidate(out string environmentError))
        {
            Fail("训练场无效：" + environmentError);
            return;
        }

        if (definition == null)
        {
            Fail("缺少教学步骤配置。");
            return;
        }

        if (!definition.TryValidate(out string error))
        {
            Fail(error);
            return;
        }

        Progression = new TutorialProgressionStateMachine(definition);
        Hud = TutorialTopHud.Create(transform);
        Hud.Bind(Progression);
        Progression.SequenceCompleted += HandleSequenceCompleted;
        PauseView = TutorialPauseView.Create(transform, this);
        CompletionView = TutorialCompletionView.Create(transform, this);
        IsInitialized = true;
    }

    private void LateUpdate()
    {
        if (!IsInitialized || IsLeavingTutorial || PauseView == null)
        {
            return;
        }

        PlayerController player = environment != null
            ? environment.PlayerRig?.Player
            : null;
        bool completionVisible = CompletionView != null &&
                                 CompletionView.IsVisible;
        if (completionVisible)
        {
            PauseView.SetVisible(false);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            return;
        }

        PauseView.SetVisible(
            player != null && player.IsPaused);
    }

    private void OnDestroy()
    {
        ReleaseRuntimeBindings();

        if (Hud != null)
        {
            Destroy(Hud.gameObject);
        }

        if (PauseView != null)
        {
            Destroy(PauseView.gameObject);
        }

        if (CompletionView != null)
        {
            Destroy(CompletionView.gameObject);
        }
    }

    private void HandleSequenceCompleted(TutorialProgressSnapshot snapshot)
    {
        PauseView?.SetVisible(false);
        SetPlayerInputEnabled(false);
        CompletionView?.SetVisible(true);
    }

    private bool TryLeaveTutorial(
        System.Func<GameModeFlowController, bool> request)
    {
        if (!IsInitialized || IsLeavingTutorial || request == null)
        {
            return false;
        }

        GameModeFlowController modeFlow =
            GameModeFlowController.Instance ??
            GameModeFlowController.Ensure(GameModeCatalog.LoadDefault());
        if (modeFlow == null || modeFlow.IsLoading || !request(modeFlow))
        {
            return false;
        }

        IsLeavingTutorial = true;
        PlayerController player = environment != null
            ? environment.PlayerRig?.Player
            : null;
        if (player != null && player.IsPaused)
        {
            player.SetPaused(false);
        }
        SetPlayerInputEnabled(false);
        ReleaseRuntimeBindings();
        if (Hud != null)
        {
            Hud.gameObject.SetActive(false);
        }
        PauseView?.SetInteractionEnabled(false);
        CompletionView?.SetInteractionEnabled(false);
        return true;
    }

    private void SetPlayerInputEnabled(bool enabled)
    {
        PlayerGameplayRig rig = environment != null
            ? environment.PlayerRig
            : null;
        rig?.Player?.SetGameplayInputEnabled(enabled);
        rig?.Combat?.SetGameplayInputEnabled(enabled);
    }

    private void ReleaseRuntimeBindings()
    {
        if (Progression != null)
        {
            Progression.SequenceCompleted -= HandleSequenceCompleted;
        }

        Hud?.Unbind();
        CompletionView?.Unbind();
    }

    private void Fail(string error)
    {
        InitializationError = error;
        Debug.LogError($"[{nameof(TutorialFlowController)}] {error}", this);
        enabled = false;
    }
}
