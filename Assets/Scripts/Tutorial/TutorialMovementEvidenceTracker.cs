using UnityEngine;

[DisallowMultipleComponent]
[DefaultExecutionOrder(100)]
public sealed class TutorialMovementEvidenceTracker : MonoBehaviour
{
    private const float MinimumSampleDistance = 0.0005f;
    private const float MaximumSampleDistance = 0.5f;

    [SerializeField] private TutorialFlowController flow;
    [SerializeField] private PlayerController player;

    private Vector3 previousPosition;
    private string observedStepId = string.Empty;

    public TutorialFlowController Flow => flow;
    public PlayerController Player => player;
    public float CorrectDirectionDistance { get; private set; }
    public float WrongDirectionDistance { get; private set; }
    public Vector3 LastLocalDisplacement { get; private set; }
    public bool IsTrackingMovementStep =>
        flow != null && flow.IsInitialized &&
        IsMovementEvidence(flow.Progression.CurrentStep?.EvidenceType ??
            TutorialEvidenceType.Manual);

    public void Configure(
        TutorialFlowController configuredFlow,
        PlayerController configuredPlayer)
    {
        flow = configuredFlow;
        player = configuredPlayer;
    }

    private void Start()
    {
        if (flow == null || player == null)
        {
            Debug.LogError(
                $"[{nameof(TutorialMovementEvidenceTracker)}] " +
                "缺少教学流程或玩家引用。",
                this);
            enabled = false;
            return;
        }

        if (!flow.IsInitialized)
        {
            Debug.LogError(
                $"[{nameof(TutorialMovementEvidenceTracker)}] " +
                "教学流程尚未初始化。",
                this);
            enabled = false;
            return;
        }

        previousPosition = player.transform.position;
        flow.Progression.ProgressChanged += HandleProgressChanged;
        ObserveStep(flow.Progression.Snapshot);
    }

    private void LateUpdate()
    {
        if (!IsTrackingMovementStep)
        {
            previousPosition = player.transform.position;
            return;
        }

        Vector3 currentPosition = player.transform.position;
        Vector3 worldDisplacement = currentPosition - previousPosition;
        previousPosition = currentPosition;
        worldDisplacement.y = 0f;
        float sampleDistance = worldDisplacement.magnitude;
        if (sampleDistance < MinimumSampleDistance ||
            sampleDistance > MaximumSampleDistance)
        {
            LastLocalDisplacement = Vector3.zero;
            return;
        }

        LastLocalDisplacement = player.transform.InverseTransformVector(
            worldDisplacement);
        TutorialEvidenceType evidence =
            flow.Progression.CurrentStep.EvidenceType;
        float expectedDistance = ExpectedProjection(
            evidence,
            LastLocalDisplacement);

        if (expectedDistance > MinimumSampleDistance)
        {
            CorrectDirectionDistance += expectedDistance;
            flow.Hud?.ClearActivityHint();
            flow.ReportEvidence(evidence, expectedDistance);
            return;
        }

        WrongDirectionDistance += sampleDistance;
        flow.Hud?.ShowActivityHint(
            $"方向不符：已移动 {WrongDirectionDistance:0.0}m，" +
            $"本步骤需要{DirectionLabel(evidence)}");
    }

    private void HandleProgressChanged(TutorialProgressSnapshot snapshot)
    {
        ObserveStep(snapshot);
    }

    private void ObserveStep(TutorialProgressSnapshot snapshot)
    {
        string stepId = snapshot.CurrentStep?.StableId ?? string.Empty;
        if (stepId == observedStepId)
        {
            return;
        }

        observedStepId = stepId;
        CorrectDirectionDistance = 0f;
        WrongDirectionDistance = 0f;
        LastLocalDisplacement = Vector3.zero;
        previousPosition = player != null
            ? player.transform.position
            : Vector3.zero;
        flow?.Hud?.ClearActivityHint();
    }

    private void OnDestroy()
    {
        if (flow?.Progression != null)
        {
            flow.Progression.ProgressChanged -= HandleProgressChanged;
        }
    }

    public static bool IsMovementEvidence(TutorialEvidenceType evidence)
    {
        return evidence == TutorialEvidenceType.MoveForwardDistance ||
               evidence == TutorialEvidenceType.MoveBackwardDistance ||
               evidence == TutorialEvidenceType.MoveLeftDistance ||
               evidence == TutorialEvidenceType.MoveRightDistance;
    }

    private static float ExpectedProjection(
        TutorialEvidenceType evidence,
        Vector3 localDisplacement)
    {
        return evidence switch
        {
            TutorialEvidenceType.MoveForwardDistance => localDisplacement.z,
            TutorialEvidenceType.MoveBackwardDistance => -localDisplacement.z,
            TutorialEvidenceType.MoveLeftDistance => -localDisplacement.x,
            TutorialEvidenceType.MoveRightDistance => localDisplacement.x,
            _ => 0f
        };
    }

    private static string DirectionLabel(TutorialEvidenceType evidence)
    {
        return evidence switch
        {
            TutorialEvidenceType.MoveForwardDistance => "向前移动（W）",
            TutorialEvidenceType.MoveBackwardDistance => "向后移动（S）",
            TutorialEvidenceType.MoveLeftDistance => "向左移动（A）",
            TutorialEvidenceType.MoveRightDistance => "向右移动（D）",
            _ => "按当前提示操作"
        };
    }
}
