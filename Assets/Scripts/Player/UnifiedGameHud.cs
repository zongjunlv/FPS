using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class UnifiedGameHud : MonoBehaviour
{
    private const float VitalsBarWidth = 290f;

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
    private IWaveProgressSource waveSource;

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

    public Canvas RootCanvas { get; private set; }
    public RectTransform SafeArea { get; private set; }
    public RectTransform HudLayer { get; private set; }
    public RectTransform ModalLayer { get; private set; }
    public RectTransform OverlayLayer { get; private set; }
    public bool IsBound { get; private set; }
    public int VitalsRefreshCount { get; private set; }
    public int WeaponRefreshCount { get; private set; }
    public int CrosshairRefreshCount { get; private set; }
    public int DamageRefreshCount { get; private set; }
    public int WaveRefreshCount { get; private set; }
    public string HealthText => healthText != null ? healthText.text : string.Empty;
    public string ArmorText => armorText != null ? armorText.text : string.Empty;
    public string AmmoText => ammoText != null && reserveAmmoText != null
        ? $"{ammoText.text} / {reserveAmmoText.text}"
        : string.Empty;
    public string WaveText =>
        waveTitleText != null ? waveTitleText.text : string.Empty;
    public string SpawnedText =>
        waveSpawnedText != null ? waveSpawnedText.text : string.Empty;
    public string RemainingEnemiesText =>
        waveRemainingText != null ? waveRemainingText.text : string.Empty;
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
        UnbindWave();
        Unbind();
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
        ammoPresenter.SetLegacyPresentation(false);
        vitalsPresenter.SetLegacyPresentation(false);
        crosshairPresenter.SetLegacyPresentation(false);
        feedbackPresenter.SetLegacyPresentation(false);
        BindWeapon(combat.EquippedWeapon);
        RefreshVitals();
        RefreshCrosshair();
        RefreshDamage();
        IsBound = true;

        if (WaveDirector.Active != null)
        {
            BindWave(WaveDirector.Active);
        }
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

        health = null;
        combat = null;
        weapon = null;
        IsBound = false;
    }

    private void BindWeapon(WeaponController nextWeapon)
    {
        weapon = nextWeapon;
        RefreshWeapon();
    }

    private void RefreshWave(WaveProgressSnapshot progress)
    {
        waveTitleText.text =
            $"WAVE {progress.CurrentWave}/{progress.TotalWaves}";
        waveSpawnedText.text =
            $"SPAWNED {progress.SpawnedCount}/{progress.TotalCount}";
        waveRemainingText.text =
            $"REMAINING {progress.RemainingCount}";
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
        healthText.text =
            $"HEALTH  {Mathf.CeilToInt(health.CurrentHealth):000}";
        armorText.text =
            $"ARMOR   {Mathf.CeilToInt(health.CurrentArmor):000}";
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
        fireModeText.text = $"FIRE  ·  {weapon.FireModeName}";
        ammoText.text = weapon.CurrentAmmo.ToString();
        reserveAmmoText.text = weapon.ReserveAmmo.ToString();
        ammoSeparatorText.text = "/";
        weaponSlotText.text = combat != null
            ? $"SLOT {combat.EquippedWeaponIndex + 1:00}"
            : string.Empty;
        weaponStatusText.text = ammoPresenter != null
            ? ammoPresenter.StatusText
            : weapon.IsReloading ? "RELOADING" : string.Empty;
        weaponIcon.sprite = iconCatalog.GetWeaponIcon(weapon.WeaponName);
        ammoText.color = weapon.CurrentAmmo == 0
            ? new Color(1f, 0.25f, 0.2f, 1f)
            : Color.white;
        WeaponRefreshCount++;
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
        BuildVitalsHud();
        BuildWeaponHud();
        BuildWaveHud();
        BuildCrosshair();
        BuildDamageOverlay();
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
            new Vector2(332f, 76f),
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
            new Vector2(16f, 41f),
            new Vector2(300f, 28f));
        waveTitleText.color = accent;
        waveTitleText.fontStyle = FontStyles.Bold;
        waveSpawnedText = CreateText(
            "WaveSpawned", waveHudRoot, 13f,
            TextAlignmentOptions.MidlineLeft,
            new Vector2(18f, 12f),
            new Vector2(160f, 24f));
        waveRemainingText = CreateText(
            "WaveRemaining", waveHudRoot, 13f,
            TextAlignmentOptions.MidlineRight,
            new Vector2(172f, 12f),
            new Vector2(142f, 24f));
        waveHudRoot.gameObject.SetActive(false);
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
            new Vector2(360f, 116f),
            new Vector2(28f, 28f));
        CreateIcon("ArmorIcon", root, iconCatalog.Get(HudIconId.Armor),
            armorColor, new Vector2(14f, 68f), new Vector2(28f, 28f));
        CreateBarBackground(root, new Vector2(52f, 72f),
            new Vector2(290f, 20f));
        armorTrail = CreateFilledBar(
            "ArmorTrail", root, trail, new Vector2(52f, 72f),
            new Vector2(290f, 20f));
        armorFill = CreateFilledBar(
            "ArmorFill", root, armorColor, new Vector2(52f, 72f),
            new Vector2(290f, 20f));
        armorText = CreateText(
            "ArmorText", root, 15f, TextAlignmentOptions.MidlineLeft,
            new Vector2(58f, 70f), new Vector2(278f, 24f));
        CreateIcon("HealthIcon", root, iconCatalog.Get(HudIconId.Health),
            healthColor, new Vector2(14f, 22f), new Vector2(30f, 30f));
        CreateBarBackground(root, new Vector2(52f, 24f),
            new Vector2(290f, 28f));
        healthTrail = CreateFilledBar(
            "HealthTrail", root, trail, new Vector2(52f, 24f),
            new Vector2(290f, 28f));
        healthFill = CreateFilledBar(
            "HealthFill", root, healthColor, new Vector2(52f, 24f),
            new Vector2(290f, 28f));
        healthText = CreateText(
            "HealthText", root, 17f, TextAlignmentOptions.MidlineLeft,
            new Vector2(58f, 23f), new Vector2(278f, 30f));
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
            new Vector2(250f, 92f), new Vector2(106f, 22f));
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
