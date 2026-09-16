using UnityEngine;

[DisallowMultipleComponent]
[DefaultExecutionOrder(180)]
public sealed class TutorialDamageTrainingTarget : MonoBehaviour
{
    [SerializeField] private TutorialFlowController flow;
    [SerializeField] private PlayerGameplayRig playerRig;
    [SerializeField] private GameObject presentationRoot;
    [SerializeField] private Health health;
    [SerializeField] private DamageHitbox bodyHitbox;
    [SerializeField] private DamageHitbox headHitbox;
    [SerializeField, Min(1f)] private float configuredMaxHealth = 250f;
    [SerializeField, Min(1f)] private float safeResetThreshold = 80f;

    private string requiredWeaponId = string.Empty;
    private string observedStepId = string.Empty;
    private Vector3 anchoredPosition;
    private Quaternion anchoredRotation;

    public TutorialFlowController Flow => flow;
    public PlayerGameplayRig PlayerRig => playerRig;
    public GameObject PresentationRoot => presentationRoot;
    public Health TargetHealth => health;
    public DamageHitbox BodyHitbox => bodyHitbox;
    public DamageHitbox HeadHitbox => headHitbox;
    public bool IsTrainingActive =>
        presentationRoot != null && presentationRoot.activeSelf;
    public string RequiredWeaponId => requiredWeaponId;
    public string LastFeedbackText { get; private set; } = string.Empty;
    public HitRegion LastHitRegion { get; private set; } = HitRegion.Generic;
    public float LastBaseDamage { get; private set; }
    public float LastMultiplier { get; private set; }
    public float LastFinalDamage { get; private set; }
    public int AcceptedBodyHits { get; private set; }
    public int AcceptedHeadHits { get; private set; }
    public int SafeResetCount { get; private set; }
    public int UnexpectedDeathCount { get; private set; }

    public void Configure(
        TutorialFlowController configuredFlow,
        PlayerGameplayRig configuredPlayerRig,
        GameObject configuredPresentationRoot,
        Health configuredHealth,
        DamageHitbox configuredBodyHitbox,
        DamageHitbox configuredHeadHitbox,
        float maxHealth,
        float resetThreshold)
    {
        Unsubscribe();
        flow = configuredFlow;
        playerRig = configuredPlayerRig;
        presentationRoot = configuredPresentationRoot;
        health = configuredHealth;
        bodyHitbox = configuredBodyHitbox;
        headHitbox = configuredHeadHitbox;
        configuredMaxHealth = Mathf.Max(1f, maxHealth);
        safeResetThreshold = Mathf.Clamp(
            resetThreshold,
            1f,
            configuredMaxHealth - 0.01f);
        anchoredPosition = transform.position;
        anchoredRotation = transform.rotation;

        if (!Application.isPlaying && presentationRoot != null)
        {
            presentationRoot.SetActive(false);
        }
        else if (Application.isPlaying && isActiveAndEnabled)
        {
            Subscribe();
        }
    }

    public bool TryValidate(out string error)
    {
        if (flow == null || playerRig == null || presentationRoot == null ||
            health == null || bodyHitbox == null || headHitbox == null)
        {
            error = "伤害训练单位缺少流程、玩家、显示根节点、生命或命中区域。";
            return false;
        }

        if (flow.Environment?.PlayerRig != playerRig ||
            bodyHitbox.Region != HitRegion.Body ||
            headHitbox.Region != HitRegion.Head ||
            bodyHitbox.DamageMultiplier <= 0f ||
            headHitbox.DamageMultiplier <= bodyHitbox.DamageMultiplier)
        {
            error = "伤害训练单位的玩家绑定或身体/头部倍率配置无效。";
            return false;
        }

        if (!bodyHitbox.transform.IsChildOf(presentationRoot.transform) ||
            !headHitbox.transform.IsChildOf(presentationRoot.transform) ||
            presentationRoot.transform.parent != transform)
        {
            error = "伤害训练单位的显示层级无效。";
            return false;
        }

        error = string.Empty;
        return true;
    }

    public bool EvaluateShot(ShotResult result)
    {
        if (!IsTrainingActive || !result.Damage.WasApplied ||
            result.DamageTarget != health.gameObject ||
            result.HitObject == null || flow == null ||
            !flow.IsInitialized || flow.Progression.IsComplete)
        {
            return false;
        }

        DamageHitbox hitbox = result.HitObject.GetComponent<DamageHitbox>();
        if (hitbox == null ||
            (hitbox != bodyHitbox && hitbox != headHitbox))
        {
            return false;
        }

        WeaponController weapon = playerRig.Combat.EquippedWeapon;
        if (weapon == null)
        {
            return false;
        }

        string weaponId = weapon.StableId ?? weapon.WeaponName;
        if (string.IsNullOrEmpty(requiredWeaponId))
        {
            requiredWeaponId = weaponId;
        }
        else if (requiredWeaponId != weaponId)
        {
            flow.Hud?.ShowActivityHint("请使用与身体命中相同的武器完成对比");
            return false;
        }

        LastHitRegion = result.Damage.Region;
        LastBaseDamage = weapon.Damage;
        LastMultiplier = hitbox.DamageMultiplier;
        LastFinalDamage = result.Damage.AppliedAmount;
        string regionLabel = LastHitRegion == HitRegion.Head
            ? "头部"
            : "身体";
        LastFeedbackText =
            $"命中 {regionLabel} · 基础 {LastBaseDamage:0.#} × " +
            $"倍率 {LastMultiplier:0.##} = 最终 {LastFinalDamage:0.#}";
        flow.Hud?.ShowActivityHint(LastFeedbackText);

        TutorialEvidenceType evidence =
            flow.Progression.CurrentStep.EvidenceType;
        bool accepted = false;
        if (evidence == TutorialEvidenceType.BodyHit &&
            LastHitRegion == HitRegion.Body)
        {
            AcceptedBodyHits++;
            accepted = flow.ReportEvidence(TutorialEvidenceType.BodyHit);
        }
        else if (evidence == TutorialEvidenceType.HeadHit &&
                 LastHitRegion == HitRegion.Head)
        {
            AcceptedHeadHits++;
            accepted = flow.ReportEvidence(TutorialEvidenceType.HeadHit);
        }

        if (!health.IsDead && health.CurrentHealth <= safeResetThreshold)
        {
            health.Initialize(configuredMaxHealth);
            SafeResetCount++;
        }

        return accepted;
    }

    private void Start()
    {
        if (!TryValidate(out string error) || !flow.IsInitialized)
        {
            Debug.LogError(
                $"[{nameof(TutorialDamageTrainingTarget)}] " +
                (string.IsNullOrEmpty(error)
                    ? "教学流程尚未初始化。"
                    : error),
                this);
            enabled = false;
            return;
        }

        anchoredPosition = transform.position;
        anchoredRotation = transform.rotation;
        health.Initialize(configuredMaxHealth);
        Subscribe();
        UpdateVisibility(flow.Progression.Snapshot);
    }

    private void LateUpdate()
    {
        if (!IsTrainingActive)
        {
            return;
        }

        transform.SetPositionAndRotation(anchoredPosition, anchoredRotation);
    }

    private void HandleShotResolved(ShotResult result)
    {
        EvaluateShot(result);
    }

    private void HandleProgressChanged(TutorialProgressSnapshot snapshot)
    {
        UpdateVisibility(snapshot);
    }

    private void HandleSequenceCompleted(TutorialProgressSnapshot snapshot)
    {
        UpdateVisibility(snapshot);
    }

    private void UpdateVisibility(TutorialProgressSnapshot snapshot)
    {
        string stepId = snapshot.CurrentStep?.StableId ?? string.Empty;
        bool stepChanged = stepId != observedStepId;
        observedStepId = stepId;
        TutorialEvidenceType evidence = snapshot.CurrentStep?.EvidenceType ??
                                        TutorialEvidenceType.Manual;
        bool visible = evidence is TutorialEvidenceType.BodyHit or
            TutorialEvidenceType.HeadHit;
        if (presentationRoot.activeSelf != visible)
        {
            presentationRoot.SetActive(visible);
        }

        if (stepChanged && visible &&
            evidence == TutorialEvidenceType.BodyHit)
        {
            requiredWeaponId = string.Empty;
            AcceptedBodyHits = 0;
            AcceptedHeadHits = 0;
            LastFeedbackText = string.Empty;
            health.Initialize(configuredMaxHealth);
        }
    }

    private void HandleUnexpectedDeath()
    {
        UnexpectedDeathCount++;
        health.Initialize(configuredMaxHealth);
    }

    private void Subscribe()
    {
        if (flow == null || playerRig?.Combat == null || health == null)
        {
            return;
        }

        Unsubscribe();
        playerRig.Combat.ShotResolved += HandleShotResolved;
        health.Died += HandleUnexpectedDeath;
        if (flow.Progression != null)
        {
            flow.Progression.ProgressChanged += HandleProgressChanged;
            flow.Progression.SequenceCompleted += HandleSequenceCompleted;
        }
    }

    private void Unsubscribe()
    {
        if (playerRig?.Combat != null)
        {
            playerRig.Combat.ShotResolved -= HandleShotResolved;
        }
        if (health != null)
        {
            health.Died -= HandleUnexpectedDeath;
        }
        if (flow?.Progression != null)
        {
            flow.Progression.ProgressChanged -= HandleProgressChanged;
            flow.Progression.SequenceCompleted -= HandleSequenceCompleted;
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
