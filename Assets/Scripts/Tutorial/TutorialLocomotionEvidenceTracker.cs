using UnityEngine;

public enum TutorialJumpPhase
{
    Inactive = 0,
    AwaitingTakeoff = 1,
    Airborne = 2
}

public enum TutorialCrouchPhase
{
    Inactive = 0,
    AwaitingCrouch = 1,
    AwaitingStand = 2
}

[DisallowMultipleComponent]
[DefaultExecutionOrder(110)]
public sealed class TutorialLocomotionEvidenceTracker : MonoBehaviour
{
    private const float MinimumJumpRise = 0.001f;
    private const float MinimumAirborneTime = 0.08f;
    private const float MaximumMovementSample = 0.5f;

    [SerializeField] private TutorialFlowController flow;
    [SerializeField] private PlayerController player;

    private string observedStepId = string.Empty;
    private Vector3 previousPosition;
    private float takeoffHeight;
    private float peakHeight;
    private float airborneStartedAt;
    private bool previousGrounded;
    private bool previousCrouching;

    public TutorialFlowController Flow => flow;
    public PlayerController Player => player;
    public TutorialJumpPhase JumpPhase { get; private set; }
    public TutorialCrouchPhase CrouchPhase { get; private set; }
    public float SprintDistance { get; private set; }
    public int CrouchTransitionCount { get; private set; }
    public float JumpRise => Mathf.Max(0f, peakHeight - takeoffHeight);
    public float JumpAirborneDuration => JumpPhase ==
        TutorialJumpPhase.Airborne
            ? Mathf.Max(0f, Time.time - airborneStartedAt)
            : 0f;
    public bool LastGroundedSample => previousGrounded;

    public void Configure(
        TutorialFlowController configuredFlow,
        PlayerController configuredPlayer)
    {
        flow = configuredFlow;
        player = configuredPlayer;
    }

    private void Start()
    {
        if (flow == null || player == null || !flow.IsInitialized)
        {
            Debug.LogError(
                $"[{nameof(TutorialLocomotionEvidenceTracker)}] " +
                "缺少已初始化的教学流程或玩家引用。",
                this);
            enabled = false;
            return;
        }

        previousPosition = player.transform.position;
        previousGrounded = player.IsGrounded;
        previousCrouching = player.IsCrouching;
        flow.Progression.ProgressChanged += HandleProgressChanged;
        ObserveStep(flow.Progression.Snapshot);
    }

    private void LateUpdate()
    {
        if (flow == null || !flow.IsInitialized || flow.Progression.IsComplete)
        {
            return;
        }

        TutorialEvidenceType evidence =
            flow.Progression.CurrentStep.EvidenceType;
        switch (evidence)
        {
            case TutorialEvidenceType.Jump:
                TrackJump();
                break;
            case TutorialEvidenceType.Sprint:
                TrackSprint();
                break;
            case TutorialEvidenceType.Crouch:
                TrackCrouch();
                break;
            default:
                previousPosition = player.transform.position;
                previousGrounded = player.IsGrounded;
                previousCrouching = player.IsCrouching;
                break;
        }
    }

    private void TrackJump()
    {
        bool grounded = player.IsGrounded;
        float height = player.transform.position.y;
        bool hasUpwardTakeoff = !grounded &&
                                player.VerticalVelocity > 0f &&
                                height > previousPosition.y +
                                    MinimumJumpRise;
        if (JumpPhase == TutorialJumpPhase.AwaitingTakeoff &&
            hasUpwardTakeoff)
        {
            JumpPhase = TutorialJumpPhase.Airborne;
            takeoffHeight = Mathf.Min(previousPosition.y, height);
            peakHeight = height;
            airborneStartedAt = Time.time;
            flow.Hud?.ShowActivityHint("已离地：保持控制并安全落地");
        }
        else if (JumpPhase == TutorialJumpPhase.Airborne)
        {
            peakHeight = Mathf.Max(peakHeight, height);
            if (grounded)
            {
                bool completedCycle = peakHeight - takeoffHeight >=
                                      MinimumJumpRise &&
                                      Time.time - airborneStartedAt >=
                                      MinimumAirborneTime;
                if (completedCycle)
                {
                    flow.Hud?.ClearActivityHint();
                    flow.ReportEvidence(TutorialEvidenceType.Jump);
                }
                else if (player.VerticalVelocity <= 0f)
                {
                    JumpPhase = TutorialJumpPhase.AwaitingTakeoff;
                }
            }
        }

        previousGrounded = grounded;
        previousPosition = player.transform.position;
    }

    private void TrackSprint()
    {
        Vector3 current = player.transform.position;
        Vector3 worldDisplacement = current - previousPosition;
        previousPosition = current;
        worldDisplacement.y = 0f;
        float sample = worldDisplacement.magnitude;
        if (sample <= 0f || sample > MaximumMovementSample)
        {
            return;
        }

        Vector3 localDisplacement = player.transform.InverseTransformVector(
            worldDisplacement);
        if (!player.IsSprinting || localDisplacement.z <= 0f)
        {
            if (sample > 0.001f)
            {
                flow.Hud?.ShowActivityHint(
                    "需要同时按住 W 与 Shift，并保持向前冲刺");
            }
            return;
        }

        flow.Hud?.ClearActivityHint();
        SprintDistance += localDisplacement.z;
        flow.ReportEvidence(
            TutorialEvidenceType.Sprint,
            localDisplacement.z);
    }

    private void TrackCrouch()
    {
        bool crouching = player.IsCrouching;
        if (CrouchPhase == TutorialCrouchPhase.AwaitingCrouch &&
            !previousCrouching && crouching)
        {
            CrouchPhase = TutorialCrouchPhase.AwaitingStand;
            CrouchTransitionCount++;
            flow.ReportEvidence(TutorialEvidenceType.Crouch);
            flow.Hud?.ShowActivityHint("已下蹲：再次按 C 恢复站立");
        }
        else if (CrouchPhase == TutorialCrouchPhase.AwaitingStand &&
                 previousCrouching && !crouching)
        {
            CrouchTransitionCount++;
            flow.Hud?.ClearActivityHint();
            flow.ReportEvidence(TutorialEvidenceType.Crouch);
        }

        previousCrouching = crouching;
        previousPosition = player.transform.position;
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
        JumpPhase = TutorialJumpPhase.Inactive;
        CrouchPhase = TutorialCrouchPhase.Inactive;
        SprintDistance = 0f;
        CrouchTransitionCount = 0;
        flow?.Hud?.ClearActivityHint();

        TutorialEvidenceType evidence = snapshot.CurrentStep?.EvidenceType ??
                                        TutorialEvidenceType.Manual;
        if (evidence is TutorialEvidenceType.Jump or
            TutorialEvidenceType.Sprint or TutorialEvidenceType.Crouch)
        {
            player?.TrySetCrouching(false);
        }

        previousPosition = player != null
            ? player.transform.position
            : Vector3.zero;
        previousGrounded = player != null && player.IsGrounded;
        previousCrouching = player != null && player.IsCrouching;

        if (evidence == TutorialEvidenceType.Jump)
        {
            JumpPhase = TutorialJumpPhase.AwaitingTakeoff;
        }
        else if (evidence == TutorialEvidenceType.Crouch)
        {
            CrouchPhase = TutorialCrouchPhase.AwaitingCrouch;
        }
    }

    private void OnDestroy()
    {
        if (flow?.Progression != null)
        {
            flow.Progression.ProgressChanged -= HandleProgressChanged;
        }
    }
}
