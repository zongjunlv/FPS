using FPS.Core.GameModes;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class TutorialFlowController : MonoBehaviour
{
    [SerializeField] private TutorialSequenceDefinition definition;
    [SerializeField] private TutorialTrainingEnvironment environment;

    public TutorialSequenceDefinition Definition => definition;
    public TutorialTrainingEnvironment Environment => environment;
    public TutorialProgressionStateMachine Progression { get; private set; }
    public TutorialTopHud Hud { get; private set; }
    public bool IsInitialized { get; private set; }
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
        int amount = 1,
        string evidenceKey = null)
    {
        return IsInitialized &&
               Progression.ReportEvidence(evidenceType, amount, evidenceKey);
    }

    public void ResetProgress()
    {
        if (IsInitialized)
        {
            Progression.Reset();
        }
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
        IsInitialized = true;
    }

    private void OnDestroy()
    {
        if (Hud != null)
        {
            Destroy(Hud.gameObject);
        }
    }

    private void Fail(string error)
    {
        InitializationError = error;
        Debug.LogError($"[{nameof(TutorialFlowController)}] {error}", this);
        enabled = false;
    }
}
