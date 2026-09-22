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

    private void Build(
        GameModeFlowController configuredFlow,
        GameModeCatalog catalog)
    {
        flow = configuredFlow;
        font = ModeUiFactory.CreateChineseFont(out ownsFont);
        RectTransform canvas = (RectTransform)transform;
        ModeUiFactory.CreateImage(
            "Background",
            canvas,
            TacticalUiTheme.Background,
            true);
        TacticalUiTheme.AddScanLines(canvas, 14);
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
            "TACTICAL SURVIVAL PROTOCOL",
            18f,
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
            "FPS 生存行动",
            54f,
            TextAlignmentOptions.MidlineLeft,
            font,
            Color.white);
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
            "选择行动模式",
            23f,
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
            "账号验证状态", new Vector2(330f, 52f),
            new Vector2(665f, 376f),
            new Color(0.035f, 0.14f, 0.15f, 0.95f));
        TacticalUiTheme.CreateIcon("安全图标", verified, "checkmark",
            TacticalUiTheme.Green, new Vector2(0f, 0.5f),
            new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
            new Vector2(28f, 28f), new Vector2(18f, 0f));
        TMP_Text verifiedText = ModeUiFactory.CreateText(
            "安全状态文字", verified, "账号已验证  ·  模式大厅在线", 16f,
            TextAlignmentOptions.MidlineLeft, font,
            TacticalUiTheme.TextPrimary);
        ModeUiFactory.Stretch(verifiedText.rectTransform, 58f, 4f);

        RectTransform list = ModeUiFactory.CreateRect(
            "Mode List",
            panel);
        ModeUiFactory.SetRect(
            list,
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(1660f, 508f),
            new Vector2(0f, -2f));
        UnityEngine.UI.HorizontalLayoutGroup layout =
            list.gameObject.AddComponent<UnityEngine.UI.HorizontalLayoutGroup>();
        layout.spacing = 24f;
        layout.padding = new RectOffset(0, 0, 0, 0);
        layout.childAlignment = TextAnchor.MiddleCenter;
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
            17f,
            TextAlignmentOptions.Center,
            font,
            new Color(0.55f, 0.64f, 0.68f, 1f));
        ModeUiFactory.SetRect(
            help.rectTransform,
            new Vector2(0.5f, 0f),
            new Vector2(0.5f, 0f),
            new Vector2(0.5f, 0f),
            new Vector2(1050f, 40f),
            new Vector2(0f, 80f));

        RectTransform status = ModeUiFactory.CreateRect(
            "Transition Status",
            panel);
        ModeUiFactory.SetRect(
            status,
            new Vector2(0.5f, 0f),
            new Vector2(0.5f, 0f),
            new Vector2(0.5f, 0f),
            new Vector2(1040f, 54f),
            new Vector2(0f, 14f));
        statusBackground = status.gameObject.AddComponent<
            UnityEngine.UI.Image>();
        statusBackground.color = new Color(0.04f, 0.12f, 0.15f, 0.92f);
        statusBackground.raycastTarget = false;
        statusText = ModeUiFactory.CreateText(
            "Message",
            status,
            string.Empty,
            18f,
            TextAlignmentOptions.Center,
            font,
            Color.white);
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
        element.preferredWidth = 530f;
        element.minWidth = 500f;
        element.preferredHeight = 508f;
        element.minHeight = 508f;
        UnityEngine.UI.Image image =
            buttonObject.GetComponent<UnityEngine.UI.Image>();
        Color accentColor = AccentFor(definition.Mode);
        image.color = TacticalUiTheme.Surface;
        image.raycastTarget = true;
        TacticalUiTheme.AddSurfaceChrome(
            (RectTransform)buttonObject.transform, accentColor);
        UnityEngine.UI.Button button =
            buttonObject.GetComponent<UnityEngine.UI.Button>();
        UnityEngine.UI.ColorBlock colors = button.colors;
        colors.normalColor = TacticalUiTheme.Surface;
        colors.highlightedColor = new Color(
            accentColor.r * 0.18f, accentColor.g * 0.18f,
            accentColor.b * 0.18f, 1f);
        colors.selectedColor = new Color(
            accentColor.r * 0.24f, accentColor.g * 0.24f,
            accentColor.b * 0.24f, 1f);
        colors.pressedColor = new Color(
            accentColor.r * 0.32f, accentColor.g * 0.32f,
            accentColor.b * 0.32f, 1f);
        colors.disabledColor = new Color(0.035f, 0.05f, 0.06f, 0.8f);
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
            new Vector2(0f, 0f),
            new Vector2(0f, 1f),
            new Vector2(0f, 0.5f),
            new Vector2(5f, 0f),
            Vector2.zero);
        UnityEngine.UI.Image accentImage =
            accent.gameObject.AddComponent<UnityEngine.UI.Image>();
        accentImage.color = accentColor;
        accentImage.raycastTarget = false;

        RectTransform iconPlate = TacticalUiTheme.CreatePill(
            buttonObject.transform, "模式图标底座", new Vector2(112f, 112f),
            new Vector2(-177f, 156f),
            new Color(accentColor.r, accentColor.g, accentColor.b, 0.12f));
        TacticalUiTheme.CreateIcon("模式图标", iconPlate,
            IconFor(definition.Mode), accentColor,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f), new Vector2(68f, 68f), Vector2.zero);

        TMP_Text protocol = ModeUiFactory.CreateText(
            "Mode Protocol", buttonObject.transform,
            ProtocolFor(definition.Mode), 15f,
            TextAlignmentOptions.MidlineRight, font, accentColor);
        protocol.fontStyle = FontStyles.Bold;
        ModeUiFactory.SetRect(protocol.rectTransform,
            new Vector2(1f, 1f), new Vector2(1f, 1f),
            new Vector2(1f, 1f), new Vector2(260f, 32f),
            new Vector2(-30f, -36f));

        TMP_Text name = ModeUiFactory.CreateText(
            "Mode Name",
            buttonObject.transform,
            definition.DisplayName,
            32f,
            TextAlignmentOptions.Left,
            font,
            Color.white);
        name.fontStyle = FontStyles.Bold;
        ModeUiFactory.SetRect(
            name.rectTransform,
            new Vector2(0f, 0.5f),
            new Vector2(0f, 0.5f),
            new Vector2(0f, 0.5f),
            new Vector2(440f, 48f),
            new Vector2(30f, 62f));

        TMP_Text description = ModeUiFactory.CreateText(
            "Description",
            buttonObject.transform,
            definition.Description,
            18f,
            TextAlignmentOptions.TopLeft,
            font,
            TacticalUiTheme.TextSecondary);
        description.enableWordWrapping = true;
        description.lineSpacing = 5f;
        ModeUiFactory.SetRect(
            description.rectTransform,
            new Vector2(0f, 0.5f),
            new Vector2(0f, 0.5f),
            new Vector2(0f, 0.5f),
            new Vector2(450f, 100f),
            new Vector2(30f, -12f));

        TMP_Text features = ModeUiFactory.CreateText(
            "Mode Features", buttonObject.transform,
            FeaturesFor(definition.Mode), 16f,
            TextAlignmentOptions.TopLeft, font, TacticalUiTheme.TextPrimary);
        features.enableWordWrapping = true;
        features.lineSpacing = 12f;
        ModeUiFactory.SetRect(features.rectTransform,
            new Vector2(0f, 0f), new Vector2(0f, 0f),
            new Vector2(0f, 0f), new Vector2(410f, 92f),
            new Vector2(30f, 84f));

        TMP_Text actionLabel = ModeUiFactory.CreateText(
            "Action Label", buttonObject.transform,
            "进入模式", 17f, TextAlignmentOptions.MidlineLeft,
            font, accentColor);
        actionLabel.fontStyle = FontStyles.Bold;
        ModeUiFactory.SetRect(actionLabel.rectTransform,
            new Vector2(0f, 0f), new Vector2(0f, 0f),
            new Vector2(0f, 0f), new Vector2(260f, 44f),
            new Vector2(30f, 24f));

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
            new Vector2(1f, 0f),
            new Vector2(1f, 0f),
            new Vector2(1f, 0f),
            new Vector2(44f, 44f),
            new Vector2(-28f, 24f));
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
            GameModeId.Tutorial => "TRAINING / 01",
            GameModeId.SoloBattle => "SOLO OP / 02",
            GameModeId.Coop => "CO-OP PVE / 03",
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

    private static Color AccentFor(GameModeId mode)
    {
        return mode switch
        {
            GameModeId.Tutorial => TacticalUiTheme.Cyan,
            GameModeId.SoloBattle => TacticalUiTheme.Amber,
            GameModeId.Coop => TacticalUiTheme.Green,
            _ => TacticalUiTheme.Blue
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
            ? new Color(1f, 0.48f, 0.4f, 1f)
            : Color.white;
        statusBackground.color = failed
            ? new Color(0.24f, 0.055f, 0.055f, 0.94f)
            : new Color(0.04f, 0.12f, 0.15f, 0.92f);
    }

    private static void CreateAmbientAccent(RectTransform canvas)
    {
        RectTransform topLine = ModeUiFactory.CreateRect("Top Accent", canvas);
        ModeUiFactory.SetRect(
            topLine,
            new Vector2(0f, 1f),
            new Vector2(1f, 1f),
            new Vector2(0.5f, 1f),
            new Vector2(0f, 3f),
            Vector2.zero);
        UnityEngine.UI.Image line =
            topLine.gameObject.AddComponent<UnityEngine.UI.Image>();
        line.color = new Color(0.22f, 0.84f, 0.8f, 0.8f);
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
