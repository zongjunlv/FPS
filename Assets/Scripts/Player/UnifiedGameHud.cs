using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class UnifiedGameHud : MonoBehaviour
{
    private const float VitalsBarWidth = 290f;
    private const float ExperienceBarWidth = 238f;
    private static readonly string[] CachedIntegerText =
        BuildCachedIntegerText(1000);

    private HudIconCatalog iconCatalog;

    private PlayerHudVisualProfile profile;
    private TMP_FontAsset fontAsset;
    private Health health;
    private PlayerCombatController combat;
    private WeaponController weapon;
    private AmmoHudPresenter ammoPresenter;
    private PlayerVitalsHudPresenter vitalsPresenter;
    private PlayerCrosshairPresenter crosshairPresenter;
    private PlayerCombatFeedbackController feedbackPresenter;
    private PlayerRuntimeCombatStats runtimeStats;
    private PlayerLowHealthFireRateEffectController lowHealthFireRateEffect;
    private IWaveProgressSource waveSource;
    private IRunProgressionSource progressionSource;

    private Image healthFill;
    private Image healthTrail;
    private Image armorFill;
    private Image armorTrail;
    private Image weaponIcon;
    private Image crosshairLeft;
    private Image crosshairRight;
    private Image crosshairTop;
    private Image crosshairBottom;
    private Image crosshairDot;
    private Image damageFlash;
    private Image damageIndicator;
    private TMP_Text healthText;
    private TMP_Text armorText;
    private TMP_Text movementText;
    private TMP_Text weaponText;
    private TMP_Text fireModeText;
    private Text ammoText;
    private Text reserveAmmoText;
    private Text ammoSeparatorText;
    private TMP_Text weaponSlotText;
    private TMP_Text weaponStatusText;
    private TMP_Text hitFeedbackText;
    private RectTransform waveHudRoot;
    private TMP_Text waveTitleText;
    private TMP_Text waveSpawnedText;
    private TMP_Text waveRemainingText;
    private TMP_Text wavePhaseText;
    private TMP_Text waveCountdownText;
    private TMP_Text waveCueText;
    private TMP_Text rewardCueText;
    private TMP_Text combatWarningText;
    private RectTransform encounterHudRoot;
    private TMP_Text encounterTitleText;
    private TMP_Text encounterObjectiveText;
    private TMP_Text encounterProgressText;
    private Image encounterProgressFill;
    private TMP_FontAsset runtimeRewardFontAsset;
    private readonly Queue<RewardCueNotice> rewardCueQueue = new();
    private Coroutine rewardCueRoutine;
    private Coroutine combatWarningRoutine;
    private RectTransform progressionHudRoot;
    private Image experienceFill;
    private TMP_Text levelText;
    private TMP_Text experienceText;

    public Canvas RootCanvas { get; private set; }
    public RectTransform SafeArea { get; private set; }
    public RectTransform HudLayer { get; private set; }
    public RectTransform ModalLayer { get; private set; }
    public RectTransform OverlayLayer { get; private set; }
    public RectTransform OutcomeLayer { get; private set; }
    public bool IsBound { get; private set; }
    public int VitalsRefreshCount { get; private set; }
    public int WeaponRefreshCount { get; private set; }
    public int CrosshairRefreshCount { get; private set; }
    public int DamageRefreshCount { get; private set; }
    public int WaveRefreshCount { get; private set; }
    public int ProgressionRefreshCount { get; private set; }
    public int RewardCueCount { get; private set; }
    public string HealthText => healthText != null ? healthText.text : string.Empty;
    public string ArmorText => armorText != null ? armorText.text : string.Empty;
    public string MovementText =>
        movementText != null ? movementText.text : string.Empty;
    public string AmmoText => ammoText != null && reserveAmmoText != null
        ? $"{ammoText.text} / {reserveAmmoText.text}"
        : string.Empty;
    public string WeaponStatusText =>
        weaponStatusText != null ? weaponStatusText.text : string.Empty;
    public string FireModeText =>
        fireModeText != null ? fireModeText.text : string.Empty;
    public string WaveText =>
        waveTitleText != null ? waveTitleText.text : string.Empty;
    public string SpawnedText =>
        waveSpawnedText != null ? waveSpawnedText.text : string.Empty;
    public string RemainingEnemiesText =>
        waveRemainingText != null ? waveRemainingText.text : string.Empty;
    public string WavePhaseText =>
        wavePhaseText != null ? wavePhaseText.text : string.Empty;
    public string WaveCountdownText =>
        waveCountdownText != null ? waveCountdownText.text : string.Empty;
    public string WaveCueText =>
        waveCueText != null ? waveCueText.text : string.Empty;
    public string RewardCueText =>
        rewardCueText != null ? rewardCueText.text : string.Empty;
    public string CombatWarningText =>
        combatWarningText != null ? combatWarningText.text : string.Empty;
    public string EncounterText => encounterTitleText != null
        ? encounterTitleText.text
        : string.Empty;
    public bool IsEncounterVisible => encounterHudRoot != null &&
        encounterHudRoot.gameObject.activeSelf;
    public string LevelText =>
        levelText != null ? levelText.text : string.Empty;
    public string ExperienceText =>
        experienceText != null ? experienceText.text : string.Empty;
    public float ExperienceNormalized =>
        experienceFill != null
            ? experienceFill.rectTransform.sizeDelta.x /
              ExperienceBarWidth
            : 0f;
    public bool IsWaveCountdownVisible =>
        waveCountdownText != null && waveCountdownText.gameObject.activeSelf;
    public bool IsWaveCueVisible =>
        waveCueText != null && waveCueText.gameObject.activeSelf;
    public bool IsRewardCueVisible =>
        rewardCueText != null && rewardCueText.gameObject.activeSelf;
    public bool IsWaveBound => waveSource != null;
    public bool LegacyPresentationsDisabled =>
        ammoPresenter != null && !ammoPresenter.LegacyOnGuiEnabled &&
        vitalsPresenter != null && !vitalsPresenter.LegacyOnGuiEnabled &&
        crosshairPresenter != null && !crosshairPresenter.LegacyOnGuiEnabled &&
        feedbackPresenter != null && !feedbackPresenter.LegacyOnGuiEnabled;

    private void Awake()
    {
        RootCanvas = GetComponent<Canvas>();
        iconCatalog = new HudIconCatalog();
        profile = Resources.Load<PlayerHudVisualProfile>(
            "PlayerHudVisualProfile");
        fontAsset = Resources.Load<TMP_FontAsset>(
            "Fonts & Materials/LiberationSans SDF");
        BuildHierarchy();
    }

    private void OnDestroy()
    {
        iconCatalog?.Dispose();
        UnbindWave();
        UnbindProgression();
        Unbind();

        if (runtimeRewardFontAsset != null)
        {
            Destroy(runtimeRewardFontAsset);
        }
    }

    public void ShowRewardCue(string message, bool eliteOrFinal)
    {
        if (rewardCueText == null || string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        rewardCueQueue.Enqueue(new RewardCueNotice(
            message,
            eliteOrFinal));
        RewardCueCount++;

        if (rewardCueRoutine == null)
        {
            rewardCueRoutine = StartCoroutine(ProcessRewardCueQueue());
        }
    }

    public void ShowCombatWarning(string message, float durationSeconds = 2f)
    {
        if (combatWarningText == null || string.IsNullOrWhiteSpace(message))
        {
            return;
        }
        if (combatWarningRoutine != null)
            StopCoroutine(combatWarningRoutine);
        combatWarningText.text = message;
        combatWarningText.gameObject.SetActive(true);
        combatWarningRoutine = StartCoroutine(
            HideCombatWarningAfter(Mathf.Max(0.25f, durationSeconds)));
    }

    public void ShowEncounter(EncounterHudSnapshot snapshot)
    {
        if (encounterHudRoot == null) return;
        encounterHudRoot.gameObject.SetActive(snapshot.Visible);
        if (!snapshot.Visible) return;
        encounterTitleText.text = "遭遇 · " + snapshot.DisplayName;
        encounterObjectiveText.text = snapshot.Objective;
        string phase = snapshot.Phase == FPS.Simulation.EncounterPhase.Intro
            ? "部署中"
            : "进行中";
        encounterProgressText.text = snapshot.SecondsRemaining > 0
            ? $"{phase}  {snapshot.Progress}/{snapshot.Target}  ·  {snapshot.SecondsRemaining}s"
            : $"{phase}  {snapshot.Progress}/{snapshot.Target}";
        RectTransform fill = encounterProgressFill.rectTransform;
        Vector2 size = fill.sizeDelta;
        size.x = 272f * (snapshot.Target > 0
            ? Mathf.Clamp01(snapshot.Progress / (float)snapshot.Target)
            : 0f);
        fill.sizeDelta = size;
    }

    public void ShowEncounterOutcome(string message, bool warning)
    {
        ShowCombatWarning(message, warning ? 2.6f : 2f);
    }

    private IEnumerator HideCombatWarningAfter(float seconds)
    {
        yield return new WaitForSecondsRealtime(seconds);
        if (combatWarningText != null)
            combatWarningText.gameObject.SetActive(false);
        combatWarningRoutine = null;
    }

    public void ClearRewardCues()
    {
        rewardCueQueue.Clear();

        if (rewardCueRoutine != null)
        {
            StopCoroutine(rewardCueRoutine);
            rewardCueRoutine = null;
        }

        if (rewardCueText != null)
        {
            rewardCueText.text = string.Empty;
            rewardCueText.gameObject.SetActive(false);
        }
    }

    public void SetOutcomePresentation(bool active)
    {
        if (HudLayer != null)
        {
            HudLayer.gameObject.SetActive(!active);
        }

        if (OverlayLayer != null)
        {
            OverlayLayer.gameObject.SetActive(!active);
        }

        if (OutcomeLayer != null)
        {
            OutcomeLayer.gameObject.SetActive(active);
        }
    }

    private IEnumerator ProcessRewardCueQueue()
    {
        while (rewardCueQueue.Count > 0)
        {
            RewardCueNotice notice = rewardCueQueue.Dequeue();
            rewardCueText.text = notice.Message;
            rewardCueText.color = notice.Highlighted
                ? new Color(1f, 0.72f, 0.18f, 1f)
                : new Color(0.38f, 0.92f, 0.86f, 1f);
            rewardCueText.gameObject.SetActive(true);
            yield return new WaitForSecondsRealtime(2.4f);
        }

        if (rewardCueText != null)
        {
            rewardCueText.gameObject.SetActive(false);
        }

        rewardCueRoutine = null;
    }

    public void Bind(GameObject playerRoot)
    {
        Unbind();
        health = playerRoot.GetComponent<Health>();
        combat = playerRoot.GetComponent<PlayerCombatController>();
        ammoPresenter = playerRoot.GetComponent<AmmoHudPresenter>();
        vitalsPresenter = playerRoot.GetComponent<PlayerVitalsHudPresenter>();
        crosshairPresenter =
            playerRoot.GetComponent<PlayerCrosshairPresenter>();
        feedbackPresenter =
            playerRoot.GetComponent<PlayerCombatFeedbackController>();
        runtimeStats = playerRoot.GetComponent<PlayerRuntimeCombatStats>();
        lowHealthFireRateEffect =
            playerRoot.GetComponent<PlayerLowHealthFireRateEffectController>();
        PlayerRunProgression progression =
            playerRoot.GetComponent<PlayerRunProgression>();

        if (health == null || combat == null ||
            ammoPresenter == null || vitalsPresenter == null ||
            crosshairPresenter == null || feedbackPresenter == null)
        {
            Debug.LogError(
                "Unified HUD could not bind all player presentation sources.");
            return;
        }

        combat.EquippedWeaponChanged += BindWeapon;
        ammoPresenter.ViewChanged += RefreshWeapon;
        vitalsPresenter.ViewChanged += RefreshVitals;
        crosshairPresenter.ViewChanged += RefreshCrosshair;
        feedbackPresenter.ViewChanged += RefreshDamage;
        if (runtimeStats != null)
        {
            runtimeStats.ModifiersChanged += HandleRuntimeModifiersChanged;
        }
        if (lowHealthFireRateEffect != null)
        {
            lowHealthFireRateEffect.StateChanged += RefreshWeapon;
        }
        ammoPresenter.SetLegacyPresentation(false);
        vitalsPresenter.SetLegacyPresentation(false);
        crosshairPresenter.SetLegacyPresentation(false);
        feedbackPresenter.SetLegacyPresentation(false);
        BindWeapon(combat.EquippedWeapon);
        RefreshVitals();
        RefreshCrosshair();
        RefreshDamage();
        BindProgression(progression);
        IsBound = true;

        if (WaveDirector.Active != null)
        {
            BindWave(WaveDirector.Active);
        }
    }

    public void BindProgression(IRunProgressionSource source)
    {
        UnbindProgression();
        progressionSource = source;

        if (progressionSource == null)
        {
            progressionHudRoot?.gameObject.SetActive(false);
            return;
        }

        progressionSource.ProgressChanged += RefreshProgression;
        progressionHudRoot.gameObject.SetActive(true);
        RefreshProgression(progressionSource.CurrentProgress);
    }

    public void UnbindProgression()
    {
        if (progressionSource != null)
        {
            progressionSource.ProgressChanged -= RefreshProgression;
        }

        progressionSource = null;
        progressionHudRoot?.gameObject.SetActive(false);
    }

    public void BindWave(IWaveProgressSource source)
    {
        UnbindWave();
        waveSource = source;

        if (waveSource == null)
        {
            waveHudRoot?.gameObject.SetActive(false);
            return;
        }

        waveSource.ProgressChanged += RefreshWave;
        waveHudRoot.gameObject.SetActive(true);
        RefreshWave(waveSource.CurrentProgress);
    }

    public void UnbindWave()
    {
        if (waveSource != null)
        {
            waveSource.ProgressChanged -= RefreshWave;
        }

        waveSource = null;
        waveHudRoot?.gameObject.SetActive(false);
        waveCountdownText?.gameObject.SetActive(false);
        waveCueText?.gameObject.SetActive(false);
    }

    private void Unbind()
    {
        if (combat != null)
        {
            combat.EquippedWeaponChanged -= BindWeapon;
        }

        if (ammoPresenter != null)
        {
            ammoPresenter.ViewChanged -= RefreshWeapon;
        }

        if (vitalsPresenter != null)
        {
            vitalsPresenter.ViewChanged -= RefreshVitals;
        }

        if (crosshairPresenter != null)
        {
            crosshairPresenter.ViewChanged -= RefreshCrosshair;
        }

        if (feedbackPresenter != null)
        {
            feedbackPresenter.ViewChanged -= RefreshDamage;
        }

        if (runtimeStats != null)
        {
            runtimeStats.ModifiersChanged -= HandleRuntimeModifiersChanged;
        }
        if (lowHealthFireRateEffect != null)
        {
            lowHealthFireRateEffect.StateChanged -= RefreshWeapon;
        }

        UnbindProgression();

        health = null;
        runtimeStats = null;
        lowHealthFireRateEffect = null;
        combat = null;
        weapon = null;
        IsBound = false;
    }

    private void RefreshProgression(RunExperienceSnapshot progress)
    {
        levelText.text = $"LV {progress.Level:00}";
        experienceText.text = progress.IsMaxLevel
            ? $"{progress.TotalExperience} XP  ·  MAX"
            : $"{progress.CurrentExperience} / " +
              $"{progress.ExperienceToNextLevel} XP";
        RectTransform rect = experienceFill.rectTransform;
        Vector2 dimensions = rect.sizeDelta;
        dimensions.x = ExperienceBarWidth * progress.ProgressNormalized;
        rect.sizeDelta = dimensions;
        ProgressionRefreshCount++;
    }

    private void BindWeapon(WeaponController nextWeapon)
    {
        weapon = nextWeapon;
        RefreshWeapon();
    }

    private void HandleRuntimeModifiersChanged()
    {
        RefreshVitals();
        RefreshWeapon();
    }

    private void RefreshWave(WaveProgressSnapshot progress)
    {
        waveTitleText.text = progress.CurrentWave > 0
            ? $"WAVE {progress.CurrentWave}/{progress.TotalWaves}"
            : $"WAVE READY  ·  {progress.TotalWaves}";
        waveSpawnedText.text =
            $"SPAWNED {progress.SpawnedCount}/{progress.TotalCount}";
        waveRemainingText.text =
            $"REMAINING {progress.RemainingCount}";
        wavePhaseText.text = progress.Phase switch
        {
            WaveRunPhase.Idle => "READY",
            WaveRunPhase.Spawning => "SPAWNING",
            WaveRunPhase.Fighting => "FIGHTING",
            WaveRunPhase.Intermission => "INTERMISSION",
            WaveRunPhase.Completed => "COMPLETED",
            WaveRunPhase.Stopped => "STOPPED",
            _ => string.Empty
        };
        bool showCountdown =
            progress.Phase == WaveRunPhase.Intermission;
        waveCountdownText.gameObject.SetActive(showCountdown);
        waveCountdownText.text = showCountdown
            ? $"NEXT WAVE  {Mathf.CeilToInt(progress.IntermissionRemaining)}"
            : string.Empty;
        bool showCue =
            progress.PresentationCue != WavePresentationCue.None;
        waveCueText.gameObject.SetActive(showCue);
        int cueWave = progress.PresentationWave > 0
            ? progress.PresentationWave
            : progress.CurrentWave;
        waveCueText.text = progress.PresentationCue switch
        {
            WavePresentationCue.WaveStarted =>
                $"WAVE {cueWave} START",
            WavePresentationCue.WaveCleared =>
                $"WAVE {cueWave} CLEARED",
            WavePresentationCue.RunCompleted =>
                "ALL WAVES CLEARED",
            _ => string.Empty
        };
        WaveRefreshCount++;
    }

    private void RefreshVitals()
    {
        if (health == null || vitalsPresenter == null)
        {
            return;
        }

        SetBarWidth(healthFill, vitalsPresenter.HealthNormalized);
        SetBarWidth(
            healthTrail,
            vitalsPresenter.TrailingHealthNormalized);
        SetBarWidth(armorFill, vitalsPresenter.ArmorNormalized);
        SetBarWidth(
            armorTrail,
            vitalsPresenter.TrailingArmorNormalized);
        healthText.SetText(
            "HEALTH  {0:000} / {1:000}",
            health.CurrentHealth,
            health.MaxHealth);
        armorText.SetText(
            "ARMOR   {0:000} / {1:000}",
            health.CurrentArmor,
            health.MaxArmor);
        if (movementText != null)
        {
            float multiplier = runtimeStats != null
                ? runtimeStats.SurvivalModifiers.MovementSpeedMultiplier
                : 1f;
            movementText.SetText("MOVE  {0:0}%", multiplier * 100f);
        }
        VitalsRefreshCount++;
    }

    private void RefreshWeapon()
    {
        if (weapon == null)
        {
            weaponText.text = string.Empty;
            fireModeText.text = string.Empty;
            ammoText.text = "0";
            reserveAmmoText.text = "0";
            ammoSeparatorText.text = "/";
            weaponSlotText.text = string.Empty;
            weaponStatusText.text = string.Empty;
            weaponIcon.sprite = iconCatalog.Get(HudIconId.Rifle);
            return;
        }

        weaponText.text = weapon.WeaponName;
        float fireRateMultiplier = runtimeStats != null
            ? runtimeStats.FireRateMultiplier
            : 1f;
        fireModeText.text =
            $"FIRE · {weapon.FireModeName} · " +
            $"{Mathf.RoundToInt(fireRateMultiplier * 100f)}%";
        ammoText.text = CachedNumber(weapon.CurrentAmmo);
        reserveAmmoText.text = CachedNumber(weapon.ReserveAmmo);
        ammoSeparatorText.text = "/";
        if (combat != null)
        {
            weaponSlotText.SetText(
                "SLOT {0:00}",
                combat.EquippedWeaponIndex + 1);
        }
        else
        {
            weaponSlotText.text = string.Empty;
        }
        string weaponState = ammoPresenter != null
            ? ammoPresenter.StatusText
            : weapon.IsReloading ? "RELOADING" : string.Empty;
        weaponStatusText.text = !string.IsNullOrEmpty(weaponState)
            ? weaponState
            : lowHealthFireRateEffect != null
                ? lowHealthFireRateEffect.StatusText
                : string.Empty;
        weaponIcon.sprite = iconCatalog.GetWeaponIcon(weapon.WeaponName);
        ammoText.color = weapon.CurrentAmmo == 0
            ? new Color(1f, 0.25f, 0.2f, 1f)
            : Color.white;
        WeaponRefreshCount++;
    }

    private static string CachedNumber(int value)
    {
        return value >= 0 && value < CachedIntegerText.Length
            ? CachedIntegerText[value]
            : value.ToString();
    }

    private static string[] BuildCachedIntegerText(int count)
    {
        var values = new string[count];

        for (int index = 0; index < values.Length; index++)
        {
            values[index] = index.ToString();
        }

        return values;
    }

    private void RefreshCrosshair()
    {
        if (crosshairPresenter == null)
        {
            return;
        }

        bool visible = crosshairPresenter.IsVisible;
        crosshairLeft.gameObject.SetActive(visible);
        crosshairRight.gameObject.SetActive(visible);
        crosshairTop.gameObject.SetActive(visible);
        crosshairBottom.gameObject.SetActive(visible);
        float gap = crosshairPresenter.CurrentGap;
        float length = Mathf.Lerp(
            10f,
            4f,
            Mathf.SmoothStep(0f, 1f, crosshairPresenter.AimBlend));
        SetCrosshairLine(crosshairLeft.rectTransform,
            new Vector2(-(gap + length * 0.5f), 0f),
            new Vector2(length, 2f));
        SetCrosshairLine(crosshairRight.rectTransform,
            new Vector2(gap + length * 0.5f, 0f),
            new Vector2(length, 2f));
        SetCrosshairLine(crosshairTop.rectTransform,
            new Vector2(0f, gap + length * 0.5f),
            new Vector2(2f, length));
        SetCrosshairLine(crosshairBottom.rectTransform,
            new Vector2(0f, -(gap + length * 0.5f)),
            new Vector2(2f, length));
        crosshairDot.gameObject.SetActive(
            visible && crosshairPresenter.AimBlend >= 0.5f);
        HitFeedbackKind feedback =
            crosshairPresenter.CurrentHitFeedback;
        hitFeedbackText.gameObject.SetActive(
            feedback != HitFeedbackKind.None);
        hitFeedbackText.text = feedback switch
        {
            HitFeedbackKind.Headshot => "HEADSHOT",
            HitFeedbackKind.Kill => "KILL",
            HitFeedbackKind.Normal => "HIT",
            _ => string.Empty
        };
        hitFeedbackText.color = feedback switch
        {
            HitFeedbackKind.Headshot =>
                new Color(1f, 0.75f, 0.1f, 1f),
            HitFeedbackKind.Kill =>
                new Color(1f, 0.15f, 0.1f, 1f),
            _ => Color.white
        };
        CrosshairRefreshCount++;
    }

    private void RefreshDamage()
    {
        if (feedbackPresenter == null)
        {
            return;
        }

        float alpha = feedbackPresenter.DamageFlashAlpha;
        damageFlash.color = new Color(0.9f, 0.02f, 0.01f, alpha * 0.12f);
        damageFlash.gameObject.SetActive(alpha > 0f);
        damageIndicator.color = new Color(
            1f,
            0.05f,
            0.02f,
            alpha * 0.78f);
        damageIndicator.gameObject.SetActive(alpha > 0f);
        PositionDamageIndicator(
            damageIndicator.rectTransform,
            feedbackPresenter.LastDamageSide);
        DamageRefreshCount++;
    }

    private void BuildHierarchy()
    {
        RectTransform canvasRect = (RectTransform)transform;
        SafeArea = CreateRect("SafeArea", canvasRect);
        Stretch(SafeArea);
        SafeArea.gameObject.AddComponent<SafeAreaFitter>();
        HudLayer = CreateRect("HUDLayer", SafeArea);
        Stretch(HudLayer);
        ModalLayer = CreateRect("ModalLayer", canvasRect);
        Stretch(ModalLayer);
        OverlayLayer = CreateRect("OverlayLayer", canvasRect);
        Stretch(OverlayLayer);
        OutcomeLayer = CreateRect("OutcomeLayer", canvasRect);
        Stretch(OutcomeLayer);
        OutcomeLayer.gameObject.SetActive(false);
        BuildVitalsHud();
        BuildProgressionHud();
        BuildWeaponHud();
        BuildWaveHud();
        BuildRewardCue();
        BuildCombatWarning();
        BuildEncounterHud();
        BuildCrosshair();
        BuildDamageOverlay();
    }

    private void BuildProgressionHud()
    {
        Color panel = profile != null
            ? profile.PanelColor
            : new Color(0.02f, 0.03f, 0.04f, 0.82f);
        panel.a = Mathf.Min(panel.a, 0.76f);
        Color accent = new Color(0.38f, 0.92f, 0.86f, 1f);
        progressionHudRoot = CreatePanel(
            "ProgressionHud",
            HudLayer,
            panel,
            Vector2.zero,
            Vector2.zero,
            Vector2.zero,
            new Vector2(360f, 58f),
            new Vector2(28f, 180f));
        Image accentStrip = CreateImage(
            "ProgressionAccent",
            progressionHudRoot,
            accent);
        SetBottomLeftRect(
            accentStrip.rectTransform,
            Vector2.zero,
            new Vector2(4f, 58f));
        levelText = CreateText(
            "LevelText",
            progressionHudRoot,
            18f,
            TextAlignmentOptions.Center,
            new Vector2(12f, 13f),
            new Vector2(76f, 32f));
        levelText.color = accent;
        levelText.fontStyle = FontStyles.Bold;
        CreateBarBackground(
            progressionHudRoot,
            new Vector2(102f, 28f),
            new Vector2(ExperienceBarWidth, 10f));
        experienceFill = CreateFilledBar(
            "ExperienceFill",
            progressionHudRoot,
            accent,
            new Vector2(102f, 28f),
            new Vector2(0f, 10f));
        experienceText = CreateText(
            "ExperienceText",
            progressionHudRoot,
            11f,
            TextAlignmentOptions.MidlineRight,
            new Vector2(102f, 6f),
            new Vector2(238f, 20f));
        experienceText.color =
            new Color(0.8f, 0.86f, 0.88f, 1f);
        progressionHudRoot.gameObject.SetActive(false);
    }

    private void BuildWaveHud()
    {
        Color panel = profile != null
            ? profile.PanelColor
            : new Color(0.02f, 0.03f, 0.04f, 0.82f);
        panel.a = Mathf.Min(panel.a, 0.72f);
        Color accent = new Color(0.38f, 0.92f, 0.86f, 1f);
        waveHudRoot = CreatePanel(
            "WaveHud",
            HudLayer,
            panel,
            new Vector2(0.5f, 1f),
            new Vector2(0.5f, 1f),
            new Vector2(0.5f, 1f),
            new Vector2(360f, 106f),
            new Vector2(0f, -24f));
        Image accentStrip = CreateImage(
            "WaveAccent", waveHudRoot, accent);
        RectTransform accentRect = accentStrip.rectTransform;
        accentRect.anchorMin = new Vector2(0f, 1f);
        accentRect.anchorMax = new Vector2(1f, 1f);
        accentRect.pivot = new Vector2(0.5f, 1f);
        accentRect.anchoredPosition = Vector2.zero;
        accentRect.sizeDelta = new Vector2(0f, 3f);
        waveTitleText = CreateText(
            "WaveTitle", waveHudRoot, 20f,
            TextAlignmentOptions.Center,
            new Vector2(16f, 70f),
            new Vector2(328f, 28f));
        waveTitleText.color = accent;
        waveTitleText.fontStyle = FontStyles.Bold;
        waveSpawnedText = CreateText(
            "WaveSpawned", waveHudRoot, 13f,
            TextAlignmentOptions.MidlineLeft,
            new Vector2(18f, 40f),
            new Vector2(174f, 24f));
        waveRemainingText = CreateText(
            "WaveRemaining", waveHudRoot, 13f,
            TextAlignmentOptions.MidlineRight,
            new Vector2(190f, 40f),
            new Vector2(152f, 24f));
        wavePhaseText = CreateText(
            "WavePhase", waveHudRoot, 11f,
            TextAlignmentOptions.MidlineLeft,
            new Vector2(18f, 10f),
            new Vector2(140f, 22f));
        wavePhaseText.color = new Color(0.74f, 0.8f, 0.82f, 1f);
        waveCountdownText = CreateText(
            "WaveCountdown", waveHudRoot, 13f,
            TextAlignmentOptions.MidlineRight,
            new Vector2(158f, 10f),
            new Vector2(184f, 22f));
        waveCountdownText.color = accent;
        waveCountdownText.gameObject.SetActive(false);
        waveCueText = CreateText(
            "WaveCue", OverlayLayer, 28f,
            TextAlignmentOptions.Center,
            Vector2.zero,
            new Vector2(520f, 44f));
        RectTransform cueRect = waveCueText.rectTransform;
        cueRect.anchorMin = new Vector2(0.5f, 1f);
        cueRect.anchorMax = new Vector2(0.5f, 1f);
        cueRect.pivot = new Vector2(0.5f, 1f);
        cueRect.anchoredPosition = new Vector2(0f, -145f);
        waveCueText.color = accent;
        waveCueText.fontStyle = FontStyles.Bold;
        waveCueText.gameObject.SetActive(false);
        waveHudRoot.gameObject.SetActive(false);
    }

    private void BuildRewardCue()
    {
        rewardCueText = CreateText(
            "RewardCue", OverlayLayer, 22f,
            TextAlignmentOptions.Center,
            Vector2.zero,
            new Vector2(720f, 40f));
        RectTransform rect = rewardCueText.rectTransform;
        rect.anchorMin = new Vector2(0.5f, 1f);
        rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(0f, -194f);
        runtimeRewardFontAsset = CreateRewardFontAsset();

        if (runtimeRewardFontAsset != null)
        {
            rewardCueText.font = runtimeRewardFontAsset;
        }

        rewardCueText.fontStyle = FontStyles.Bold;
        rewardCueText.gameObject.SetActive(false);
    }

    private void BuildCombatWarning()
    {
        combatWarningText = CreateText(
            "CombatDirectorWarning",
            OverlayLayer,
            22f,
            TextAlignmentOptions.Center,
            Vector2.zero,
            new Vector2(760f, 42f));
        RectTransform rect = combatWarningText.rectTransform;
        rect.anchorMin = new Vector2(0.5f, 1f);
        rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(0f, -244f);
        if (runtimeRewardFontAsset != null)
            combatWarningText.font = runtimeRewardFontAsset;
        combatWarningText.fontStyle = FontStyles.Bold;
        combatWarningText.color = new Color(1f, 0.48f, 0.16f, 1f);
        combatWarningText.gameObject.SetActive(false);
    }

    private void BuildEncounterHud()
    {
        Color panel = profile != null
            ? profile.PanelColor
            : new Color(0.02f, 0.03f, 0.04f, 0.82f);
        panel.a = Mathf.Min(panel.a, 0.78f);
        Color accent = new Color(1f, 0.48f, 0.16f, 1f);
        encounterHudRoot = CreatePanel(
            "EncounterHud",
            HudLayer,
            panel,
            Vector2.one,
            Vector2.one,
            Vector2.one,
            new Vector2(320f, 112f),
            new Vector2(-28f, -152f));
        Image strip = CreateImage("EncounterAccent", encounterHudRoot, accent);
        SetBottomLeftRect(
            strip.rectTransform,
            new Vector2(0f, 109f),
            new Vector2(320f, 3f));
        encounterTitleText = CreateText(
            "EncounterTitle",
            encounterHudRoot,
            17f,
            TextAlignmentOptions.MidlineLeft,
            new Vector2(18f, 72f),
            new Vector2(284f, 28f));
        encounterTitleText.color = accent;
        encounterTitleText.fontStyle = FontStyles.Bold;
        encounterObjectiveText = CreateText(
            "EncounterObjective",
            encounterHudRoot,
            12f,
            TextAlignmentOptions.MidlineLeft,
            new Vector2(18f, 45f),
            new Vector2(284f, 24f));
        CreateBarBackground(
            encounterHudRoot,
            new Vector2(24f, 29f),
            new Vector2(272f, 7f));
        encounterProgressFill = CreateFilledBar(
            "EncounterProgressFill",
            encounterHudRoot,
            accent,
            new Vector2(24f, 29f),
            new Vector2(0f, 7f));
        encounterProgressText = CreateText(
            "EncounterProgress",
            encounterHudRoot,
            10f,
            TextAlignmentOptions.MidlineRight,
            new Vector2(18f, 6f),
            new Vector2(284f, 18f));
        encounterProgressText.color = new Color(0.8f, 0.86f, 0.88f, 1f);
        if (runtimeRewardFontAsset != null)
        {
            encounterTitleText.font = runtimeRewardFontAsset;
            encounterObjectiveText.font = runtimeRewardFontAsset;
            encounterProgressText.font = runtimeRewardFontAsset;
        }
        encounterHudRoot.gameObject.SetActive(false);
    }

    private static TMP_FontAsset CreateRewardFontAsset()
    {
        string[] fonts =
        {
            "PingFang SC", "Microsoft YaHei", "Noto Sans CJK SC",
            "Source Han Sans SC", "Arial Unicode MS"
        };

        for (int index = 0; index < fonts.Length; index++)
        {
            TMP_FontAsset asset = TMP_FontAsset.CreateFontAsset(
                fonts[index], "Regular", 48);

            if (asset != null && asset.HasCharacter('奖', false, true))
            {
                asset.name = "奖励提示中文动态字体";
                return asset;
            }

            if (asset != null)
            {
                Destroy(asset);
            }
        }

        return null;
    }

    private readonly struct RewardCueNotice
    {
        public RewardCueNotice(string message, bool highlighted)
        {
            Message = message;
            Highlighted = highlighted;
        }

        public string Message { get; }
        public bool Highlighted { get; }
    }

    private void BuildVitalsHud()
    {
        Color panel = profile != null
            ? profile.PanelColor
            : new Color(0.02f, 0.03f, 0.04f, 0.82f);
        Color healthColor = profile != null
            ? profile.HealthColor
            : new Color(0.92f, 0.24f, 0.22f, 1f);
        Color armorColor = profile != null
            ? profile.ArmorColor
            : new Color(0.18f, 0.68f, 0.95f, 1f);
        Color trail = profile != null
            ? profile.DamageTrailColor
            : new Color(1f, 0.85f, 0.5f, 0.8f);
        RectTransform root = CreatePanel(
            "VitalsHud",
            HudLayer,
            panel,
            new Vector2(0f, 0f),
            new Vector2(0f, 0f),
            new Vector2(0f, 0f),
            new Vector2(360f, 142f),
            new Vector2(28f, 28f));
        CreateIcon("ArmorIcon", root, iconCatalog.Get(HudIconId.Armor),
            armorColor, new Vector2(14f, 92f), new Vector2(28f, 28f));
        CreateBarBackground(root, new Vector2(52f, 96f),
            new Vector2(290f, 20f));
        armorTrail = CreateFilledBar(
            "ArmorTrail", root, trail, new Vector2(52f, 96f),
            new Vector2(290f, 20f));
        armorFill = CreateFilledBar(
            "ArmorFill", root, armorColor, new Vector2(52f, 96f),
            new Vector2(290f, 20f));
        armorText = CreateText(
            "ArmorText", root, 15f, TextAlignmentOptions.MidlineLeft,
            new Vector2(58f, 94f), new Vector2(278f, 24f));
        CreateIcon("HealthIcon", root, iconCatalog.Get(HudIconId.Health),
            healthColor, new Vector2(14f, 46f), new Vector2(30f, 30f));
        CreateBarBackground(root, new Vector2(52f, 48f),
            new Vector2(290f, 28f));
        healthTrail = CreateFilledBar(
            "HealthTrail", root, trail, new Vector2(52f, 48f),
            new Vector2(290f, 28f));
        healthFill = CreateFilledBar(
            "HealthFill", root, healthColor, new Vector2(52f, 48f),
            new Vector2(290f, 28f));
        healthText = CreateText(
            "HealthText", root, 17f, TextAlignmentOptions.MidlineLeft,
            new Vector2(58f, 47f), new Vector2(278f, 30f));
        movementText = CreateText(
            "MovementText", root, 13f, TextAlignmentOptions.MidlineRight,
            new Vector2(52f, 10f), new Vector2(290f, 24f));
        movementText.color = new Color(0.38f, 0.92f, 0.86f, 1f);
    }

    private void BuildWeaponHud()
    {
        Color panel = profile != null
            ? profile.PanelColor
            : new Color(0.02f, 0.03f, 0.04f, 0.82f);
        panel.a = Mathf.Min(panel.a, 0.76f);
        Color accent = new Color(0.38f, 0.92f, 0.86f, 1f);
        Color muted = new Color(0.72f, 0.78f, 0.8f, 0.9f);
        RectTransform root = CreatePanel(
            "WeaponHud",
            HudLayer,
            panel,
            new Vector2(1f, 0f),
            new Vector2(1f, 0f),
            new Vector2(1f, 0f),
            new Vector2(374f, 148f),
            new Vector2(-28f, 28f));
        Outline outline = root.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(1f, 1f, 1f, 0.14f);
        outline.effectDistance = new Vector2(1f, -1f);
        Image accentStrip = CreateImage("AccentStrip", root, accent);
        SetBottomLeftRect(
            accentStrip.rectTransform,
            new Vector2(0f, 0f),
            new Vector2(4f, 148f));
        weaponIcon = CreateIcon(
            "WeaponIcon", root, iconCatalog.Get(HudIconId.Rifle),
            accent, new Vector2(18f, 58f), new Vector2(246f, 62f));
        weaponText = CreateText(
            "WeaponName", root, 17f, TextAlignmentOptions.MidlineLeft,
            new Vector2(18f, 120f), new Vector2(210f, 24f));
        weaponText.fontStyle = FontStyles.Bold;
        weaponSlotText = CreateText(
            "WeaponSlot", root, 11f, TextAlignmentOptions.MidlineRight,
            new Vector2(274f, 121f), new Vector2(82f, 20f));
        weaponSlotText.color = muted;
        fireModeText = CreateText(
            "FireMode", root, 12f, TextAlignmentOptions.MidlineRight,
            new Vector2(190f, 92f), new Vector2(166f, 22f));
        fireModeText.color = accent;
        ammoText = CreateAmmoText(
            "CurrentAmmo", root, 38, FontStyle.Bold,
            TextAnchor.MiddleRight,
            new Vector2(190f, 2f), new Vector2(88f, 40f));
        ammoSeparatorText = CreateAmmoText(
            "AmmoSeparator", root, 20, FontStyle.Normal,
            TextAnchor.MiddleCenter,
            new Vector2(281f, 5f), new Vector2(20f, 32f));
        ammoSeparatorText.color = muted;
        reserveAmmoText = CreateAmmoText(
            "ReserveAmmo", root, 22, FontStyle.Normal,
            TextAnchor.MiddleRight,
            new Vector2(302f, 5f), new Vector2(54f, 32f));
        reserveAmmoText.color = muted;
        weaponStatusText = CreateText(
            "WeaponStatus", root, 13f, TextAlignmentOptions.MidlineLeft,
            new Vector2(18f, 8f), new Vector2(156f, 26f));
        weaponStatusText.color = new Color(1f, 0.72f, 0.2f, 1f);
    }

    private void BuildCrosshair()
    {
        RectTransform root = CreateRect("Crosshair", HudLayer);
        Stretch(root);
        crosshairLeft = CreateCenteredLine("Left", root);
        crosshairRight = CreateCenteredLine("Right", root);
        crosshairTop = CreateCenteredLine("Top", root);
        crosshairBottom = CreateCenteredLine("Bottom", root);
        crosshairDot = CreateCenteredLine("Dot", root);
        crosshairDot.rectTransform.sizeDelta = new Vector2(2f, 2f);
        hitFeedbackText = CreateText(
            "HitFeedback", root, 16f, TextAlignmentOptions.Center,
            Vector2.zero, new Vector2(160f, 26f));
        hitFeedbackText.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
        hitFeedbackText.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        hitFeedbackText.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        hitFeedbackText.rectTransform.anchoredPosition = new Vector2(0f, -42f);
    }

    private void BuildDamageOverlay()
    {
        damageFlash = CreateImage(
            "DamageFlash", OverlayLayer, Color.clear);
        Stretch(damageFlash.rectTransform);
        damageIndicator = CreateImage(
            "DamageIndicator", OverlayLayer, Color.clear);
        damageFlash.raycastTarget = false;
        damageIndicator.raycastTarget = false;
        damageFlash.gameObject.SetActive(false);
        damageIndicator.gameObject.SetActive(false);
    }

    private TMP_Text CreateText(
        string objectName,
        Transform parent,
        float size,
        TextAlignmentOptions alignment,
        Vector2 position,
        Vector2 dimensions)
    {
        GameObject textObject = new GameObject(
            objectName,
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(TextMeshProUGUI));
        textObject.transform.SetParent(parent, false);
        TextMeshProUGUI text = textObject.GetComponent<TextMeshProUGUI>();
        text.fontSize = size;
        text.alignment = alignment;
        text.color = Color.white;
        text.raycastTarget = false;
        text.textWrappingMode = TextWrappingModes.NoWrap;

        if (fontAsset != null)
        {
            text.font = fontAsset;
        }

        RectTransform rect = text.rectTransform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.zero;
        rect.pivot = Vector2.zero;
        rect.anchoredPosition = position;
        rect.sizeDelta = dimensions;
        return text;
    }

    private Text CreateAmmoText(
        string objectName,
        Transform parent,
        int size,
        FontStyle style,
        TextAnchor alignment,
        Vector2 position,
        Vector2 dimensions)
    {
        GameObject textObject = new GameObject(
            objectName,
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Text));
        textObject.transform.SetParent(parent, false);
        Text text = textObject.GetComponent<Text>();
        text.font = profile != null && profile.Font != null
            ? profile.Font
            : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = size;
        text.fontStyle = style;
        text.alignment = alignment;
        text.color = Color.white;
        text.raycastTarget = false;
        text.supportRichText = false;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        RectTransform rect = text.rectTransform;
        SetBottomLeftRect(rect, position, dimensions);
        return text;
    }

    private static RectTransform CreatePanel(
        string objectName,
        Transform parent,
        Color color,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 pivot,
        Vector2 dimensions,
        Vector2 position)
    {
        Image image = CreateImage(objectName, parent, color);
        RectTransform rect = image.rectTransform;
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot;
        rect.sizeDelta = dimensions;
        rect.anchoredPosition = position;
        return rect;
    }

    private static Image CreateIcon(
        string objectName,
        Transform parent,
        Sprite sprite,
        Color color,
        Vector2 position,
        Vector2 dimensions)
    {
        Image image = CreateImage(objectName, parent, color);
        image.sprite = sprite;
        image.preserveAspect = true;
        RectTransform rect = image.rectTransform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.zero;
        rect.pivot = Vector2.zero;
        rect.anchoredPosition = position;
        rect.sizeDelta = dimensions;
        return image;
    }

    private static Image CreateFilledBar(
        string objectName,
        Transform parent,
        Color color,
        Vector2 position,
        Vector2 dimensions)
    {
        Image fill = CreateImage(objectName, parent, color);
        RectTransform fillRect = fill.rectTransform;
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = Vector2.zero;
        fillRect.pivot = Vector2.zero;
        fillRect.anchoredPosition = position;
        fillRect.sizeDelta = dimensions;
        return fill;
    }

    private static void SetBarWidth(Image bar, float normalizedValue)
    {
        RectTransform rect = bar.rectTransform;
        Vector2 dimensions = rect.sizeDelta;
        dimensions.x = VitalsBarWidth * Mathf.Clamp01(normalizedValue);
        rect.sizeDelta = dimensions;
    }

    private static void CreateBarBackground(
        Transform parent,
        Vector2 position,
        Vector2 dimensions)
    {
        Image background = CreateImage(
            "BarBackground", parent, new Color(0f, 0f, 0f, 0.72f));
        RectTransform rect = background.rectTransform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.zero;
        rect.pivot = Vector2.zero;
        rect.anchoredPosition = position;
        rect.sizeDelta = dimensions;
    }

    private static Image CreateCenteredLine(
        string objectName,
        Transform parent)
    {
        Image image = CreateImage(objectName, parent, Color.white);
        RectTransform rect = image.rectTransform;
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(2f, 2f);
        return image;
    }

    private static Image CreateImage(
        string objectName,
        Transform parent,
        Color color)
    {
        GameObject imageObject = new GameObject(
            objectName,
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image));
        imageObject.transform.SetParent(parent, false);
        Image image = imageObject.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private static RectTransform CreateRect(
        string objectName,
        Transform parent)
    {
        GameObject child = new GameObject(
            objectName,
            typeof(RectTransform));
        child.transform.SetParent(parent, false);
        return child.GetComponent<RectTransform>();
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static void SetCrosshairLine(
        RectTransform rect,
        Vector2 position,
        Vector2 dimensions)
    {
        rect.anchoredPosition = position;
        rect.sizeDelta = dimensions;
    }

    private static void SetBottomLeftRect(
        RectTransform rect,
        Vector2 position,
        Vector2 dimensions)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.zero;
        rect.pivot = Vector2.zero;
        rect.anchoredPosition = position;
        rect.sizeDelta = dimensions;
    }

    private static void PositionDamageIndicator(
        RectTransform rect,
        DamageIndicatorSide side)
    {
        switch (side)
        {
            case DamageIndicatorSide.Left:
                rect.anchorMin = new Vector2(0f, 0.25f);
                rect.anchorMax = new Vector2(0f, 0.75f);
                rect.pivot = new Vector2(0f, 0.5f);
                rect.sizeDelta = new Vector2(20f, 0f);
                rect.anchoredPosition = Vector2.zero;
                break;
            case DamageIndicatorSide.Right:
                rect.anchorMin = new Vector2(1f, 0.25f);
                rect.anchorMax = new Vector2(1f, 0.75f);
                rect.pivot = new Vector2(1f, 0.5f);
                rect.sizeDelta = new Vector2(20f, 0f);
                rect.anchoredPosition = Vector2.zero;
                break;
            case DamageIndicatorSide.Back:
                rect.anchorMin = new Vector2(0.25f, 0f);
                rect.anchorMax = new Vector2(0.75f, 0f);
                rect.pivot = new Vector2(0.5f, 0f);
                rect.sizeDelta = new Vector2(0f, 20f);
                rect.anchoredPosition = Vector2.zero;
                break;
            default:
                rect.anchorMin = new Vector2(0.25f, 1f);
                rect.anchorMax = new Vector2(0.75f, 1f);
                rect.pivot = new Vector2(0.5f, 1f);
                rect.sizeDelta = new Vector2(0f, 20f);
                rect.anchoredPosition = Vector2.zero;
                break;
        }
    }
}
