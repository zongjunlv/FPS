using UnityEngine;

[DisallowMultipleComponent]
[DefaultExecutionOrder(160)]
public sealed class TutorialShootingEvidenceTracker : MonoBehaviour
{
    private const float AdsReadyThreshold = 0.5f;
    private const float MinimumAccumulatedRecoil = 0.05f;

    [SerializeField] private TutorialFlowController flow;
    [SerializeField] private TutorialShootingTarget target;
    [SerializeField] private PlayerGameplayRig playerRig;

    private string observedStepId = string.Empty;
    private int attackCycle;
    private int lastAcceptedTapCycle = -1;
    private int burstCycle = -1;
    private bool attackHeld;
    private bool burstCompletionReported;

    public TutorialFlowController Flow => flow;
    public TutorialShootingTarget Target => target;
    public PlayerGameplayRig PlayerRig => playerRig;
    public int AttackCycle => attackCycle;
    public int HipFireHitCount { get; private set; }
    public int AimFireHitCount { get; private set; }
    public int IndependentTapCount { get; private set; }
    public int BurstValidHitCount { get; private set; }
    public float MaximumBurstRecoil { get; private set; }
    public bool BurstRecoilObserved =>
        MaximumBurstRecoil >= MinimumAccumulatedRecoil;

    public void Configure(
        TutorialFlowController configuredFlow,
        TutorialShootingTarget configuredTarget,
        PlayerGameplayRig configuredPlayerRig)
    {
        Unsubscribe();
        flow = configuredFlow;
        target = configuredTarget;
        playerRig = configuredPlayerRig;

        if (Application.isPlaying && isActiveAndEnabled)
        {
            Subscribe();
        }
    }

    public bool TryValidate(out string error)
    {
        if (flow == null || target == null || playerRig == null)
        {
            error = "射击教学追踪器缺少流程、教学靶或玩家 Rig。";
            return false;
        }

        if (target.Combat != playerRig.Combat ||
            flow.Environment?.PlayerRig != playerRig ||
            flow.Environment?.ShootingTarget != target)
        {
            error = "射击教学追踪器绑定了不一致的玩家或教学靶。";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private void Start()
    {
        if (!TryValidate(out string error) || !flow.IsInitialized)
        {
            Debug.LogError(
                $"[{nameof(TutorialShootingEvidenceTracker)}] {error}",
                this);
            enabled = false;
            return;
        }

        Subscribe();
        ObserveStep(flow.Progression.Snapshot);
    }

    private void LateUpdate()
    {
        if (flow == null || !flow.IsInitialized ||
            flow.Progression.IsComplete ||
            flow.Progression.CurrentStep.EvidenceType !=
                TutorialEvidenceType.AutomaticBurst ||
            burstCompletionReported)
        {
            return;
        }

        Vector2 targetRecoil = playerRig.Recoil.TargetRecoil;
        Vector2 currentRecoil = playerRig.Recoil.CurrentRecoil;
        MaximumBurstRecoil = Mathf.Max(
            MaximumBurstRecoil,
            Mathf.Max(targetRecoil.magnitude, currentRecoil.magnitude));

        int requiredHits = flow.Progression.CurrentStep.TargetValue;
        if (attackHeld && burstCycle == attackCycle &&
            BurstValidHitCount >= requiredHits && BurstRecoilObserved)
        {
            burstCompletionReported = true;
            flow.Hud?.ClearActivityHint();
            flow.ReportEvidence(
                TutorialEvidenceType.AutomaticBurst,
                requiredHits,
                $"burst:{burstCycle}");
        }
    }

    private void HandleInputSampled(PlayerInputSample sample)
    {
        bool beginsPress = sample.AttackPressed ||
                           (!attackHeld && sample.AttackHeld);
        if (beginsPress)
        {
            attackCycle++;
        }

        bool released = attackHeld && !sample.AttackHeld;
        attackHeld = sample.AttackHeld;

        if (released && IsCurrentStep(TutorialEvidenceType.AutomaticBurst) &&
            !burstCompletionReported)
        {
            ResetBurstAttempt();
            flow.Hud?.ShowActivityHint(
                "连射中途松开了左键，请重新完成一次连续射击");
        }
    }

    private void HandleShotEvaluated(ShotResult result, bool valid)
    {
        if (!valid || !attackHeld || flow == null ||
            !flow.IsInitialized || flow.Progression.IsComplete)
        {
            return;
        }

        switch (flow.Progression.CurrentStep.EvidenceType)
        {
            case TutorialEvidenceType.HipFireHit:
                RecordHipFire();
                break;
            case TutorialEvidenceType.AimFireHit:
                RecordAimFire();
                break;
            case TutorialEvidenceType.SemiAutomaticShot:
                RecordSemiAutomaticTap();
                break;
            case TutorialEvidenceType.AutomaticBurst:
                RecordAutomaticBurstHit();
                break;
        }
    }

    private void RecordHipFire()
    {
        PlayerController player = playerRig.Player;
        if (player.IsAiming || player.AimBlend >= AdsReadyThreshold)
        {
            flow.Hud?.ShowActivityHint("腰射阶段请松开鼠标右键");
            return;
        }

        HipFireHitCount++;
        flow.Hud?.ClearActivityHint();
        flow.ReportEvidence(TutorialEvidenceType.HipFireHit);
    }

    private void RecordAimFire()
    {
        PlayerController player = playerRig.Player;
        if (!player.IsAiming || player.AimBlend < AdsReadyThreshold)
        {
            flow.Hud?.ShowActivityHint(
                "请先按住鼠标右键，等待瞄准状态生效后再射击");
            return;
        }

        AimFireHitCount++;
        flow.Hud?.ClearActivityHint();
        flow.ReportEvidence(TutorialEvidenceType.AimFireHit);
    }

    private void RecordSemiAutomaticTap()
    {
        WeaponController weapon = playerRig.Combat.EquippedWeapon;
        if (weapon == null || weapon.IsAutomatic)
        {
            flow.Hud?.ShowActivityHint(
                "请切换到 2 号位半自动手枪，每次单独点击左键");
            return;
        }

        if (attackCycle == lastAcceptedTapCycle)
        {
            return;
        }

        lastAcceptedTapCycle = attackCycle;
        IndependentTapCount++;
        flow.Hud?.ClearActivityHint();
        flow.ReportEvidence(
            TutorialEvidenceType.SemiAutomaticShot,
            1f,
            $"tap:{attackCycle}");
    }

    private void RecordAutomaticBurstHit()
    {
        WeaponController weapon = playerRig.Combat.EquippedWeapon;
        if (weapon == null || !weapon.IsAutomatic)
        {
            flow.Hud?.ShowActivityHint(
                "请切换到 1 号位自动步枪，并持续按住鼠标左键");
            return;
        }

        if (burstCycle < 0)
        {
            burstCycle = attackCycle;
        }

        if (burstCycle != attackCycle)
        {
            ResetBurstAttempt();
            burstCycle = attackCycle;
        }

        BurstValidHitCount++;
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
        lastAcceptedTapCycle = -1;
        flow?.Hud?.ClearActivityHint();

        TutorialEvidenceType evidence = snapshot.CurrentStep?.EvidenceType ??
                                        TutorialEvidenceType.Manual;
        switch (evidence)
        {
            case TutorialEvidenceType.HipFireHit:
                HipFireHitCount = 0;
                break;
            case TutorialEvidenceType.AimFireHit:
                AimFireHitCount = 0;
                break;
            case TutorialEvidenceType.SemiAutomaticShot:
                IndependentTapCount = 0;
                break;
            case TutorialEvidenceType.AutomaticBurst:
                ResetBurstAttempt();
                break;
        }
    }

    private void ResetBurstAttempt()
    {
        burstCycle = -1;
        BurstValidHitCount = 0;
        MaximumBurstRecoil = 0f;
        burstCompletionReported = false;
    }

    private bool IsCurrentStep(TutorialEvidenceType evidence)
    {
        return flow != null && flow.IsInitialized &&
               !flow.Progression.IsComplete &&
               flow.Progression.CurrentStep.EvidenceType == evidence;
    }

    private void Subscribe()
    {
        if (flow == null || target == null || playerRig?.Input == null)
        {
            return;
        }

        Unsubscribe();
        playerRig.Input.InputSampled += HandleInputSampled;
        target.ShotEvaluated += HandleShotEvaluated;
        if (flow.Progression != null)
        {
            flow.Progression.ProgressChanged += HandleProgressChanged;
        }
    }

    private void Unsubscribe()
    {
        if (playerRig?.Input != null)
        {
            playerRig.Input.InputSampled -= HandleInputSampled;
        }
        if (target != null)
        {
            target.ShotEvaluated -= HandleShotEvaluated;
        }
        if (flow?.Progression != null)
        {
            flow.Progression.ProgressChanged -= HandleProgressChanged;
        }
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    private void OnDestroy()
    {
        Unsubscribe();
    }
}
