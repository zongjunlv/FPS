using UnityEngine;

public enum TutorialWeaponSwitchPhase
{
    Inactive = 0,
    AwaitingDigitTwo = 1,
    SwitchingToPistol = 2,
    AwaitingScrollToRifle = 3,
    SwitchingToRifle = 4,
    Complete = 5
}

internal enum TutorialWeaponSwitchIntent
{
    None = 0,
    DigitTwo = 1,
    ScrollToRifle = 2
}

[DisallowMultipleComponent]
[DefaultExecutionOrder(170)]
public sealed class TutorialWeaponOperationEvidenceTracker : MonoBehaviour
{
    private const int RifleIndex = 0;
    private const int PistolIndex = 1;
    private const int PreparedMissingRounds = 5;

    [SerializeField] private TutorialFlowController flow;
    [SerializeField] private PlayerGameplayRig playerRig;

    private string observedStepId = string.Empty;
    private TutorialWeaponSwitchIntent pendingSwitchIntent;
    private WeaponController observedReloadWeapon;
    private bool reloadStarted;
    private bool reloadCompletionReceived;

    public TutorialFlowController Flow => flow;
    public PlayerGameplayRig PlayerRig => playerRig;
    public TutorialWeaponSwitchPhase SwitchPhase { get; private set; }
    public int CompletedSwitchInputCount { get; private set; }
    public int ReloadStartCount { get; private set; }
    public int ReloadCompletionCount { get; private set; }
    public int ReloadInterruptionCount { get; private set; }
    public int PreparedMagazine { get; private set; }
    public int PreparedReserve { get; private set; }

    public void Configure(
        TutorialFlowController configuredFlow,
        PlayerGameplayRig configuredPlayerRig)
    {
        Unsubscribe();
        flow = configuredFlow;
        playerRig = configuredPlayerRig;

        if (Application.isPlaying && isActiveAndEnabled)
        {
            Subscribe();
        }
    }

    public bool TryValidate(out string error)
    {
        if (flow == null || playerRig == null ||
            playerRig.Input == null || playerRig.Combat == null ||
            playerRig.Loadout == null)
        {
            error = "武器教学追踪器缺少流程、玩家输入、战斗或配装引用。";
            return false;
        }

        if (flow.Environment?.PlayerRig != playerRig ||
            playerRig.Combat.WeaponCount < 2)
        {
            error = "武器教学追踪器没有绑定当前教学玩家的步枪和手枪。";
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
                $"[{nameof(TutorialWeaponOperationEvidenceTracker)}] " +
                (string.IsNullOrEmpty(error)
                    ? "教学流程尚未初始化。"
                    : error),
                this);
            enabled = false;
            return;
        }

        Subscribe();
        ObserveStep(flow.Progression.Snapshot);
    }

    private void HandleInputSampled(PlayerInputSample sample)
    {
        if (!IsCurrentStep(TutorialEvidenceType.WeaponSwitch))
        {
            return;
        }

        if (!playerRig.Loadout.IsSwitching &&
            pendingSwitchIntent != TutorialWeaponSwitchIntent.None &&
            sample.WeaponSelection < 0 &&
            sample.WeaponCycleDirection == 0)
        {
            pendingSwitchIntent = TutorialWeaponSwitchIntent.None;
        }

        if (SwitchPhase == TutorialWeaponSwitchPhase.AwaitingDigitTwo &&
            sample.WeaponSelection == PistolIndex)
        {
            pendingSwitchIntent = TutorialWeaponSwitchIntent.DigitTwo;
        }
        else if (SwitchPhase ==
                     TutorialWeaponSwitchPhase.AwaitingScrollToRifle &&
                 sample.WeaponCycleDirection != 0)
        {
            pendingSwitchIntent =
                TutorialWeaponSwitchIntent.ScrollToRifle;
        }
    }

    private void HandleSwitchStarted()
    {
        if (!IsCurrentStep(TutorialEvidenceType.WeaponSwitch))
        {
            return;
        }

        if (SwitchPhase == TutorialWeaponSwitchPhase.AwaitingDigitTwo &&
            pendingSwitchIntent == TutorialWeaponSwitchIntent.DigitTwo)
        {
            SwitchPhase = TutorialWeaponSwitchPhase.SwitchingToPistol;
        }
        else if (SwitchPhase ==
                     TutorialWeaponSwitchPhase.AwaitingScrollToRifle &&
                 pendingSwitchIntent ==
                     TutorialWeaponSwitchIntent.ScrollToRifle)
        {
            SwitchPhase = TutorialWeaponSwitchPhase.SwitchingToRifle;
        }
    }

    private void HandleSwitchCompleted()
    {
        if (!IsCurrentStep(TutorialEvidenceType.WeaponSwitch))
        {
            return;
        }

        if (SwitchPhase == TutorialWeaponSwitchPhase.SwitchingToPistol &&
            playerRig.Loadout.CurrentIndex == PistolIndex)
        {
            CompletedSwitchInputCount++;
            pendingSwitchIntent = TutorialWeaponSwitchIntent.None;
            SwitchPhase = TutorialWeaponSwitchPhase.AwaitingScrollToRifle;
            flow.Hud?.ShowActivityHint("手枪已装备：滚动滚轮切回自动步枪");
            flow.ReportEvidence(
                TutorialEvidenceType.WeaponSwitch,
                1f,
                "switch:digit2:pistol");
        }
        else if (SwitchPhase ==
                     TutorialWeaponSwitchPhase.SwitchingToRifle &&
                 playerRig.Loadout.CurrentIndex == RifleIndex)
        {
            CompletedSwitchInputCount++;
            pendingSwitchIntent = TutorialWeaponSwitchIntent.None;
            SwitchPhase = TutorialWeaponSwitchPhase.Complete;
            flow.Hud?.ClearActivityHint();
            flow.ReportEvidence(
                TutorialEvidenceType.WeaponSwitch,
                1f,
                "switch:wheel:rifle");
        }
    }

    private void HandleSwitchInterrupted()
    {
        pendingSwitchIntent = TutorialWeaponSwitchIntent.None;
        if (SwitchPhase == TutorialWeaponSwitchPhase.SwitchingToPistol)
        {
            SwitchPhase = TutorialWeaponSwitchPhase.AwaitingDigitTwo;
        }
        else if (SwitchPhase ==
                 TutorialWeaponSwitchPhase.SwitchingToRifle)
        {
            SwitchPhase =
                TutorialWeaponSwitchPhase.AwaitingScrollToRifle;
        }
    }

    private void HandleEquippedWeaponChanged(WeaponController weapon)
    {
        BindReloadWeapon(weapon);
        if (IsCurrentStep(TutorialEvidenceType.Reload))
        {
            PrepareReloadCondition();
        }
    }

    private void HandleReloadStateChanged()
    {
        if (!IsCurrentStep(TutorialEvidenceType.Reload) ||
            observedReloadWeapon == null)
        {
            return;
        }

        if (observedReloadWeapon.IsReloading)
        {
            reloadStarted = true;
            reloadCompletionReceived = false;
            ReloadStartCount++;
            return;
        }

        if (reloadStarted && !reloadCompletionReceived)
        {
            reloadStarted = false;
            ReloadInterruptionCount++;
            flow.Hud?.ShowActivityHint("换弹已被打断，请停止冲刺后重新按 R");
        }
    }

    private void HandleReloadCompleted()
    {
        if (!IsCurrentStep(TutorialEvidenceType.Reload) ||
            observedReloadWeapon == null || !reloadStarted)
        {
            return;
        }

        reloadCompletionReceived = true;
        reloadStarted = false;
        ReloadCompletionCount++;
        flow.Hud?.ClearActivityHint();
        flow.ReportEvidence(
            TutorialEvidenceType.Reload,
            1f,
            $"reload:{ReloadCompletionCount}");
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
        pendingSwitchIntent = TutorialWeaponSwitchIntent.None;
        flow?.Hud?.ClearActivityHint();

        TutorialEvidenceType evidence = snapshot.CurrentStep?.EvidenceType ??
                                        TutorialEvidenceType.Manual;
        if (evidence == TutorialEvidenceType.WeaponSwitch)
        {
            CompletedSwitchInputCount = 0;
            SwitchPhase = TutorialWeaponSwitchPhase.AwaitingDigitTwo;
        }
        else if (evidence == TutorialEvidenceType.Reload)
        {
            SwitchPhase = TutorialWeaponSwitchPhase.Complete;
            ReloadStartCount = 0;
            ReloadCompletionCount = 0;
            ReloadInterruptionCount = 0;
            if (playerRig.Combat.EquippedWeapon ==
                playerRig.Loadout.CurrentWeapon)
            {
                PrepareReloadCondition();
            }
        }
    }

    private void PrepareReloadCondition()
    {
        WeaponController weapon = playerRig.Combat.EquippedWeapon;
        BindReloadWeapon(weapon);
        int missing = Mathf.Min(
            PreparedMissingRounds,
            Mathf.Max(1, weapon.MagazineCapacity - 1));
        PreparedMagazine = weapon.MagazineCapacity - missing;
        PreparedReserve = Mathf.Clamp(
            Mathf.Max(weapon.ReserveAmmo, missing),
            missing,
            weapon.BaseMaximumReserveAmmo);
        if (!weapon.TryRestoreAmmo(PreparedMagazine, PreparedReserve))
        {
            Debug.LogError("无法为换弹教学准备弹匣与备弹。", this);
        }

        reloadStarted = false;
        reloadCompletionReceived = false;
    }

    private void BindReloadWeapon(WeaponController weapon)
    {
        if (observedReloadWeapon == weapon)
        {
            return;
        }

        if (observedReloadWeapon != null)
        {
            observedReloadWeapon.ReloadStateChanged -=
                HandleReloadStateChanged;
            observedReloadWeapon.ReloadCompleted -= HandleReloadCompleted;
        }

        observedReloadWeapon = weapon;
        if (observedReloadWeapon != null)
        {
            observedReloadWeapon.ReloadStateChanged +=
                HandleReloadStateChanged;
            observedReloadWeapon.ReloadCompleted += HandleReloadCompleted;
        }
    }

    private bool IsCurrentStep(TutorialEvidenceType evidence)
    {
        return flow != null && flow.IsInitialized &&
               !flow.Progression.IsComplete &&
               flow.Progression.CurrentStep.EvidenceType == evidence;
    }

    private void Subscribe()
    {
        if (flow == null || playerRig == null)
        {
            return;
        }

        Unsubscribe();
        playerRig.Input.InputSampled += HandleInputSampled;
        playerRig.Loadout.SwitchStarted += HandleSwitchStarted;
        playerRig.Loadout.SwitchCompleted += HandleSwitchCompleted;
        playerRig.Loadout.SwitchInterrupted += HandleSwitchInterrupted;
        playerRig.Combat.EquippedWeaponChanged +=
            HandleEquippedWeaponChanged;
        BindReloadWeapon(playerRig.Combat.EquippedWeapon);
        if (flow.Progression != null)
        {
            flow.Progression.ProgressChanged += HandleProgressChanged;
        }
    }

    private void Unsubscribe()
    {
        if (playerRig != null)
        {
            playerRig.Input.InputSampled -= HandleInputSampled;
            playerRig.Loadout.SwitchStarted -= HandleSwitchStarted;
            playerRig.Loadout.SwitchCompleted -= HandleSwitchCompleted;
            playerRig.Loadout.SwitchInterrupted -= HandleSwitchInterrupted;
            playerRig.Combat.EquippedWeaponChanged -=
                HandleEquippedWeaponChanged;
        }
        BindReloadWeapon(null);
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
