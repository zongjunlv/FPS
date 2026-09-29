using System;
using System.Collections.Generic;
using FPS.Core.GameModes;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

[DisallowMultipleComponent]
public sealed class GameModeEntryButton : MonoBehaviour
{
    [SerializeField] private GameModeId mode;

    public GameModeId Mode => mode;

    public void Configure(GameModeId configuredMode)
    {
        mode = configuredMode;
    }
}

[DisallowMultipleComponent]
public sealed class ModeEntryView : MonoBehaviour
{
    private readonly List<UnityEngine.UI.Button> buttons = new();
    private GameModeFlowController flow;
    private TMP_FontAsset font;
    private bool ownsFont;
    private TMP_Text statusText;
    private UnityEngine.UI.Image statusBackground;
    private TMP_Text authenticationText;
    private UnityEngine.UI.Image authenticationIcon;
    private UnityEngine.UI.Button logoutButton;
    private Action onAccountSignOut;
    private bool authenticationUnlocked = true;

    public IReadOnlyList<UnityEngine.UI.Button> Buttons => buttons;
    public string VisibleStatus => statusText != null
        ? statusText.text
        : string.Empty;

    public static ModeEntryView Create(
        GameModeFlowController configuredFlow,
        GameModeCatalog catalog)
    {
        GameObject canvasObject = ModeUiFactory.CreateCanvas(
            "Mode Entry Canvas",
            200);
        ModeEntryView view = canvasObject.AddComponent<ModeEntryView>();
        view.Build(configuredFlow, catalog);
        return view;
    }

    public UnityEngine.UI.Button GetButton(GameModeId mode)
    {
        for (int index = 0; index < buttons.Count; index++)
        {
            GameModeEntryButton tag =
                buttons[index].GetComponent<GameModeEntryButton>();
            if (tag != null && tag.Mode == mode)
            {
                return buttons[index];
            }
        }

        return null;
    }

    public void SetAuthenticationUnlocked(bool unlocked)
    {
        authenticationUnlocked = unlocked;
        Refresh();
        if (!unlocked || buttons.Count == 0 || EventSystem.current == null)
            return;
        EventSystem.current.SetSelectedGameObject(buttons[0].gameObject);
    }

    public void ConfigureAccountSignOut(Action callback)
    {
        onAccountSignOut = callback;
        Refresh();
    }

    private void Build(
        GameModeFlowController configuredFlow,
        GameModeCatalog catalog)
    {
        flow = configuredFlow;
        font = ModeUiFactory.CreateChineseFont(out ownsFont);
        RectTransform canvas = (RectTransform)transform;
        UnityEngine.UI.Image background = ModeUiFactory.CreateImage(
            "Background",
            canvas,
            TacticalUiTheme.Background,
            true);
        TacticalUiTheme.ApplyMenuArt(background, "lobby-backdrop",
            new Color(0.72f, 0.79f, 0.84f, 1f), false);
        // The operator is the focal point, kept clear of the mode list.
        background.rectTransform.offsetMin = new Vector2(210f, -118f);
        background.rectTransform.offsetMax = new Vector2(500f, 118f);
        UnityEngine.UI.Image shade = ModeUiFactory.CreateImage(
            "Backdrop Vignette", canvas,
            new Color(0.017f, 0.025f, 0.035f, 0.27f), true);
        shade.raycastTarget = false;
        UnityEngine.UI.Image leftShade = ModeUiFactory.CreateImage(
            "Operation List Scrim", canvas,
            new Color(0.018f, 0.029f, 0.040f, 0.79f), false);
        ModeUiFactory.SetRect(leftShade.rectTransform, Vector2.zero,
            new Vector2(0.57f, 1f), new Vector2(0f, 0.5f),
            Vector2.zero, Vector2.zero);
        CreateAmbientAccent(canvas);

        RectTransform panel = ModeUiFactory.CreateRect(
            "Mode Selection",
            canvas);
        panel.anchorMin = panel.anchorMax = panel.pivot =
            new Vector2(0.5f, 0.5f);
        panel.sizeDelta = new Vector2(1660f, 900f);
        panel.anchoredPosition = Vector2.zero;

        TMP_Text eyebrow = ModeUiFactory.CreateText(
            "Eyebrow",
            panel,
            "FPS  //  OPERATION HUB",
            17f,
            TextAlignmentOptions.MidlineLeft,
            font,
            TacticalUiTheme.Cyan);
        ModeUiFactory.SetRect(
            eyebrow.rectTransform,
            new Vector2(0f, 1f),
            new Vector2(0f, 1f),
            new Vector2(0f, 1f),
            new Vector2(900f, 34f),
            new Vector2(0f, -28f));

        TMP_Text title = ModeUiFactory.CreateText(
            "Title",
            panel,
            "选择行动",
            52f,
            TextAlignmentOptions.MidlineLeft,
            font,
            TacticalUiTheme.TextPrimary);
        title.fontStyle = FontStyles.Bold;
        ModeUiFactory.SetRect(
            title.rectTransform,
            new Vector2(0f, 1f),
            new Vector2(0f, 1f),
            new Vector2(0f, 1f),
            new Vector2(980f, 80f),
            new Vector2(0f, -68f));

        TMP_Text subtitle = ModeUiFactory.CreateText(
            "Subtitle",
            panel,
            "选择训练、单人行动或双人协同作战。",
            21f,
            TextAlignmentOptions.MidlineLeft,
            font,
            TacticalUiTheme.TextSecondary);
        ModeUiFactory.SetRect(
            subtitle.rectTransform,
            new Vector2(0f, 1f),
            new Vector2(0f, 1f),
            new Vector2(0f, 1f),
            new Vector2(980f, 42f),
            new Vector2(0f, -145f));

        RectTransform verified = TacticalUiTheme.CreatePill(panel,
            "账号验证状态", new Vector2(350f, 50f),
            new Vector2(650f, 374f),
            new Color(0.035f, 0.068f, 0.079f, 0.92f));
        TacticalUiTheme.ApplyMenuArt(verified.GetComponent<UnityEngine.UI.Image>(),
            "nescia-panel-strip", Color.white);
        authenticationIcon = TacticalUiTheme.CreateIcon(
            "安全图标", verified, "checkmark",
            TacticalUiTheme.Cyan, new Vector2(0f, 0.5f),
            new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
            new Vector2(24f, 24f), new Vector2(20f, 0f));
        authenticationText = ModeUiFactory.CreateText(
            "安全状态文字", verified, string.Empty, 16f,
            TextAlignmentOptions.MidlineLeft, font,
            TacticalUiTheme.TextPrimary);
        ModeUiFactory.Stretch(authenticationText.rectTransform, 56f, 4f);

        RectTransform logout = TacticalUiTheme.CreatePill(panel,
            "退出当前账号", new Vector2(240f, 50f),
            new Vector2(285f, 374f),
            new Color(0.035f, 0.068f, 0.079f, 0.92f));
        UnityEngine.UI.Image logoutImage =
            logout.GetComponent<UnityEngine.UI.Image>();
        TacticalUiTheme.ApplyMenuArt(logoutImage,
            "nescia-panel-strip", Color.white);
        logoutImage.raycastTarget = true;
        logoutButton = logout.gameObject.AddComponent<UnityEngine.UI.Button>();
        logoutButton.targetGraphic = logoutImage;
        logoutButton.onClick.AddListener(() => onAccountSignOut?.Invoke());
        TacticalUiTheme.CreateIcon("退出图标", logout, "exit",
            TacticalUiTheme.Cyan, new Vector2(0f, 0.5f),
            new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
            new Vector2(24f, 24f), new Vector2(18f, 0f));
        TMP_Text logoutLabel = ModeUiFactory.CreateText(
            "退出文字", logout, "退出当前账号", 16f,
            TextAlignmentOptions.Center, font,
            TacticalUiTheme.TextPrimary);
        ModeUiFactory.Stretch(logoutLabel.rectTransform, 40f, 4f);

        RectTransform list = ModeUiFactory.CreateRect(
            "Mode List",
            panel);
        ModeUiFactory.SetRect(list,
            new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(0f, 1f), new Vector2(770f, 548f),
            new Vector2(0f, -233f));
        UnityEngine.UI.VerticalLayoutGroup layout =
            list.gameObject.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
        layout.spacing = 13f;
        layout.padding = new RectOffset(0, 0, 0, 0);
        layout.childAlignment = TextAnchor.UpperLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        for (int index = 0; index < catalog.Modes.Count; index++)
        {
            GameModeDefinition definition = catalog.Modes[index];
            UnityEngine.UI.Button button = CreateModeButton(list, definition);
            buttons.Add(button);
        }

        LinkNavigation();

        TMP_Text help = ModeUiFactory.CreateText(
            "Input Help",
            panel,
            "W / S 或方向键选择    Enter 确认    鼠标点击",
            16f,
            TextAlignmentOptions.Center,
            font,
            TacticalUiTheme.TextSecondary);
        ModeUiFactory.SetRect(
            help.rectTransform,
            new Vector2(0f, 0f), new Vector2(0f, 0f),
            new Vector2(0f, 0f), new Vector2(770f, 40f),
            new Vector2(0f, 79f));

        RectTransform status = ModeUiFactory.CreateRect(
            "Transition Status",
            panel);
        ModeUiFactory.SetRect(
            status,
            new Vector2(0f, 0f), new Vector2(0f, 0f),
            new Vector2(0f, 0f), new Vector2(770f, 54f),
            new Vector2(0f, 14f));
        statusBackground = status.gameObject.AddComponent<
            UnityEngine.UI.Image>();
        statusBackground.color = new Color(0.035f, 0.068f, 0.079f, 0.96f);
        TacticalUiTheme.ApplyMenuArt(statusBackground,
            "nescia-panel-strip", Color.white);
        statusBackground.raycastTarget = false;
        statusText = ModeUiFactory.CreateText(
            "Message",
            status,
            string.Empty,
            18f,
            TextAlignmentOptions.Center,
            font,
            TacticalUiTheme.TextPrimary);
        ModeUiFactory.Stretch(statusText.rectTransform, 14f, 0f);

        ModeUiFactory.EnsureEventSystem();
        if (buttons.Count > 0)
        {
            EventSystem.current.SetSelectedGameObject(buttons[0].gameObject);
        }

        flow.StateChanged += Refresh;
        Refresh();
    }

    private UnityEngine.UI.Button CreateModeButton(
        RectTransform parent,
        GameModeDefinition definition)
    {
        GameObject buttonObject = new GameObject(
            definition.DisplayName,
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(UnityEngine.UI.Image),
            typeof(UnityEngine.UI.Button),
            typeof(UnityEngine.UI.LayoutElement),
            typeof(GameModeEntryButton));
        buttonObject.transform.SetParent(parent, false);
        UnityEngine.UI.LayoutElement element =
            buttonObject.GetComponent<UnityEngine.UI.LayoutElement>();
        element.preferredWidth = 770f;
        element.minWidth = 770f;
        element.preferredHeight = 174f;
        element.minHeight = 174f;
        UnityEngine.UI.Image image =
            buttonObject.GetComponent<UnityEngine.UI.Image>();
        Color accentColor = TacticalUiTheme.Cyan;
        TacticalUiTheme.ApplyMenuArt(image, "nescia-panel-strip", Color.white);
        image.raycastTarget = true;
        UnityEngine.UI.Button button =
            buttonObject.GetComponent<UnityEngine.UI.Button>();
        UnityEngine.UI.ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(0.76f, 1f, 0.98f, 1f);
        colors.selectedColor = new Color(0.76f, 1f, 0.98f, 1f);
        colors.pressedColor = new Color(0.54f, 0.74f, 0.76f, 1f);
        colors.disabledColor = new Color(0.35f, 0.39f, 0.4f, 0.75f);
        colors.colorMultiplier = 1f;
        colors.fadeDuration = 0.1f;
        button.colors = colors;
        button.transition = UnityEngine.UI.Selectable.Transition.ColorTint;
        button.onClick.AddListener(() => flow.TryEnterMode(definition.Mode));
        buttonObject.GetComponent<GameModeEntryButton>()
            .Configure(definition.Mode);

        RectTransform accent = ModeUiFactory.CreateRect(
            "Accent",
            buttonObject.transform);
        ModeUiFactory.SetRect(
            accent,
            new Vector2(0f, 0f), new Vector2(0f, 1f),
            new Vector2(0f, 0.5f), new Vector2(4f, -16f),
            new Vector2(7f, 0f));
        UnityEngine.UI.Image accentImage =
            accent.gameObject.AddComponent<UnityEngine.UI.Image>();
        accentImage.color = accentColor;
        accentImage.raycastTarget = false;

        RectTransform iconPlate = ModeUiFactory.CreateRect(
            "模式图标底座", buttonObject.transform);
        ModeUiFactory.SetRect(iconPlate, new Vector2(0f, 0.5f),
            new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
            new Vector2(68f, 68f), new Vector2(53f, 0f));
        UnityEngine.UI.Image iconPlateImage =
            iconPlate.gameObject.AddComponent<UnityEngine.UI.Image>();
        iconPlateImage.color = new Color(0.07f, 0.22f, 0.25f, 0.82f);
        iconPlateImage.raycastTarget = false;
        TacticalUiTheme.CreateIcon("模式图标", iconPlate,
            IconFor(definition.Mode), accentColor,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f), new Vector2(39f, 39f), Vector2.zero);

        TMP_Text protocol = ModeUiFactory.CreateText(
            "Mode Protocol", buttonObject.transform,
            ProtocolFor(definition.Mode), 14f,
            TextAlignmentOptions.MidlineRight, font,
            TacticalUiTheme.TextSecondary);
        protocol.fontStyle = FontStyles.Bold;
        ModeUiFactory.SetRect(protocol.rectTransform,
            new Vector2(1f, 1f), new Vector2(1f, 1f),
            new Vector2(1f, 1f), new Vector2(230f, 32f),
            new Vector2(-30f, -20f));

        TMP_Text name = ModeUiFactory.CreateText(
            "Mode Name",
            buttonObject.transform,
            definition.DisplayName,
            30f,
            TextAlignmentOptions.Left,
            font,
            TacticalUiTheme.TextPrimary);
        name.fontStyle = FontStyles.Bold;
        ModeUiFactory.SetRect(
            name.rectTransform,
            new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
            new Vector2(0f, 0.5f), new Vector2(400f, 48f),
            new Vector2(145f, 19f));

        TMP_Text description = ModeUiFactory.CreateText(
            "Description",
            buttonObject.transform,
            definition.Mode == GameModeId.Coop
                ? "进入公开房间，与队友协同完成同一场战局。"
                : definition.Description,
            18f,
            TextAlignmentOptions.TopLeft,
            font,
            TacticalUiTheme.TextSecondary);
        description.enableWordWrapping = true;
        description.lineSpacing = 5f;
        ModeUiFactory.SetRect(
            description.rectTransform,
            new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
            new Vector2(0f, 0.5f), new Vector2(400f, 54f),
            new Vector2(145f, -28f));

        TMP_Text actionLabel = ModeUiFactory.CreateText(
            "Action Label", buttonObject.transform,
            "进入行动", 16f, TextAlignmentOptions.MidlineRight,
            font, accentColor);
        actionLabel.fontStyle = FontStyles.Bold;
        ModeUiFactory.SetRect(actionLabel.rectTransform,
            new Vector2(1f, 0f), new Vector2(1f, 0f),
            new Vector2(1f, 0f), new Vector2(140f, 36f),
            new Vector2(-69f, 18f));

        TMP_Text arrow = ModeUiFactory.CreateText(
            "Arrow",
            buttonObject.transform,
            "›",
            34f,
            TextAlignmentOptions.Center,
            font,
            accentColor);
        ModeUiFactory.SetRect(
            arrow.rectTransform,
            new Vector2(1f, 0f), new Vector2(1f, 0f),
            new Vector2(1f, 0f), new Vector2(38f, 38f),
            new Vector2(-24f, 18f));
        return button;
    }

    private static string IconFor(GameModeId mode)
    {
        return mode switch
        {
            GameModeId.Tutorial => "target",
            GameModeId.SoloBattle => "singleplayer",
            GameModeId.Coop => "multiplayer",
            _ => "menuGrid"
        };
    }

    private static string ProtocolFor(GameModeId mode)
    {
        return mode switch
        {
            GameModeId.Tutorial => "01 / TRAINING",
            GameModeId.SoloBattle => "02 / SOLO",
            GameModeId.Coop => "03 / CO-OP",
            _ => "OPERATION"
        };
    }

    private static string FeaturesFor(GameModeId mode)
    {
        return mode switch
        {
            GameModeId.Tutorial => "• 基础移动与射击训练\n• 固定目标与部位伤害教学",
            GameModeId.SoloBattle => "• 波次任务与肉鸽成长\n• 背包、敌群与撤离流程",
            GameModeId.Coop => "• 双人服务器权威战局\n• 公开房间、角色与准备系统",
            _ => string.Empty
        };
    }

    private void LinkNavigation()
    {
        for (int index = 0; index < buttons.Count; index++)
        {
            UnityEngine.UI.Navigation navigation =
                new UnityEngine.UI.Navigation
                {
                    mode = UnityEngine.UI.Navigation.Mode.Explicit,
                    selectOnUp = buttons[(index - 1 + buttons.Count) % buttons.Count],
                    selectOnDown = buttons[(index + 1) % buttons.Count]
                };
            buttons[index].navigation = navigation;
        }
    }

    private void Refresh()
    {
        if (flow == null || statusText == null)
        {
            return;
        }

        for (int index = 0; index < buttons.Count; index++)
        {
            buttons[index].interactable = authenticationUnlocked &&
                                          !flow.IsLoading;
        }

        string status = !string.IsNullOrWhiteSpace(flow.FailureMessage)
            ? flow.FailureMessage
            : flow.IsLoading
                ? $"{flow.StatusMessage}  {flow.LoadingProgress:P0}"
                : authenticationUnlocked
                    ? "等待选择"
                    : "请先完成账号登录";
        statusText.text = status;
        bool failed = !string.IsNullOrWhiteSpace(flow.FailureMessage);
        statusText.color = failed
            ? TacticalUiTheme.Red
            : TacticalUiTheme.TextPrimary;
        statusBackground.color = failed
            ? new Color(0.21f, 0.09f, 0.09f, 0.98f)
            : Color.white;

        if (authenticationText != null)
        {
            authenticationText.text = authenticationUnlocked
                ? "账号已验证  ·  模式大厅在线"
                : "请先登录账号";
            authenticationText.color = authenticationUnlocked
                ? TacticalUiTheme.TextPrimary
                : TacticalUiTheme.TextSecondary;
        }

        if (authenticationIcon != null)
        {
            authenticationIcon.sprite = TacticalUiTheme.LoadIcon(
                authenticationUnlocked ? "checkmark" : "locked");
            authenticationIcon.enabled = authenticationIcon.sprite != null;
            authenticationIcon.color = authenticationUnlocked
                ? TacticalUiTheme.Cyan
                : TacticalUiTheme.TextSecondary;
        }

        if (logoutButton != null)
        {
            logoutButton.gameObject.SetActive(authenticationUnlocked);
            logoutButton.interactable = authenticationUnlocked &&
                                        onAccountSignOut != null &&
                                        !flow.IsLoading;
        }
    }

    private static void CreateAmbientAccent(RectTransform canvas)
    {
        RectTransform topLine = ModeUiFactory.CreateRect("Top Accent", canvas);
        ModeUiFactory.SetRect(
            topLine,
            new Vector2(0f, 1f),
            new Vector2(1f, 1f),
            new Vector2(0.5f, 1f),
            new Vector2(0f, 2f),
            Vector2.zero);
        UnityEngine.UI.Image line =
            topLine.gameObject.AddComponent<UnityEngine.UI.Image>();
        line.color = new Color(0.30f, 0.42f, 0.44f, 0.7f);
        line.raycastTarget = false;
    }

    private void OnDestroy()
    {
        if (flow != null)
        {
            flow.StateChanged -= Refresh;
        }

        if (ownsFont && font != null)
        {
            Destroy(font);
        }
    }
}

internal static class ModeUiFactory
{
    public static GameObject CreateCanvas(string objectName, int sortingOrder)
    {
        GameObject canvasObject = new GameObject(
            objectName,
            typeof(RectTransform),
            typeof(Canvas),
            typeof(UnityEngine.UI.CanvasScaler),
            typeof(UnityEngine.UI.GraphicRaycaster));
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;
        UnityEngine.UI.CanvasScaler scaler =
            canvasObject.GetComponent<UnityEngine.UI.CanvasScaler>();
        scaler.uiScaleMode =
            UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode =
            UnityEngine.UI.CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        return canvasObject;
    }

    public static void EnsureEventSystem()
    {
        EventSystem eventSystem = EventSystem.current;
        if (eventSystem == null)
        {
            GameObject eventSystemObject = new GameObject(
                "EventSystem",
                typeof(EventSystem),
                typeof(InputSystemUIInputModule));
            eventSystem = eventSystemObject.GetComponent<EventSystem>();
        }

        InputSystemUIInputModule module =
            eventSystem.GetComponent<InputSystemUIInputModule>();
        module ??= eventSystem.gameObject.AddComponent<InputSystemUIInputModule>();
        if (module.actionsAsset == null)
        {
            module.AssignDefaultActions();
        }
    }

    public static TMP_FontAsset CreateChineseFont(out bool owned)
    {
        Font bundled = Resources.Load<Font>("UI/TacticalMenu/NotoSansSC");
        if (bundled != null)
        {
            TMP_FontAsset uiFont = TMP_FontAsset.CreateFontAsset(bundled);
            if (uiFont != null)
            {
                uiFont.name = "Noto Sans SC UI Dynamic";
                owned = true;
                return uiFont;
            }
        }

        string[] fonts =
        {
            "PingFang SC",
            "Microsoft YaHei",
            "Noto Sans CJK SC",
            "Source Han Sans SC",
            "Arial Unicode MS"
        };

        for (int index = 0; index < fonts.Length; index++)
        {
            TMP_FontAsset candidate = TMP_FontAsset.CreateFontAsset(
                fonts[index],
                "Regular",
                48);
            if (candidate != null && candidate.HasCharacter('中', false, true))
            {
                candidate.name = "模式入口中文动态字体";
                owned = true;
                return candidate;
            }

            if (candidate != null)
            {
                UnityEngine.Object.Destroy(candidate);
            }
        }

        owned = false;
        return Resources.Load<TMP_FontAsset>(
            "Fonts & Materials/LiberationSans SDF");
    }

    public static RectTransform CreateRect(string name, Transform parent)
    {
        GameObject gameObject = new GameObject(name, typeof(RectTransform));
        gameObject.transform.SetParent(parent, false);
        return (RectTransform)gameObject.transform;
    }

    public static UnityEngine.UI.Image CreateImage(
        string name,
        Transform parent,
        Color color,
        bool stretch)
    {
        RectTransform rect = CreateRect(name, parent);
        UnityEngine.UI.Image image =
            rect.gameObject.AddComponent<UnityEngine.UI.Image>();
        image.color = color;
        image.raycastTarget = false;
        if (stretch)
        {
            Stretch(rect);
        }

        return image;
    }

    public static TMP_Text CreateText(
        string name,
        Transform parent,
        string value,
        float size,
        TextAlignmentOptions alignment,
        TMP_FontAsset font,
        Color color)
    {
        GameObject textObject = new GameObject(
            name,
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(TextMeshProUGUI));
        textObject.transform.SetParent(parent, false);
        TMP_Text text = textObject.GetComponent<TMP_Text>();
        text.text = value;
        text.fontSize = size;
        text.alignment = alignment;
        text.color = color;
        text.raycastTarget = false;
        text.enableWordWrapping = false;
        if (font != null)
        {
            text.font = font;
        }

        return text;
    }

    public static void SetRect(
        RectTransform rect,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 pivot,
        Vector2 size,
        Vector2 position)
    {
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot;
        rect.sizeDelta = size;
        rect.anchoredPosition = position;
    }

    public static void Stretch(
        RectTransform rect,
        float horizontalPadding = 0f,
        float verticalPadding = 0f)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(horizontalPadding, verticalPadding);
        rect.offsetMax = new Vector2(-horizontalPadding, -verticalPadding);
    }
}
