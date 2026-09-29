using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using FPS.Core.GameModes;
using FPS.Networking.Session;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
public sealed class CoopAccountView : MonoBehaviour
{
    private static readonly Color Background =
        TacticalUiTheme.Background;
    private static readonly Color Panel =
        TacticalUiTheme.Surface;
    private static readonly Color RaisedPanel =
        TacticalUiTheme.SurfaceRaised;
    private static readonly Color Field =
        new(0.075f, 0.09f, 0.10f, 1f);
    private static readonly Color Cyan =
        TacticalUiTheme.Cyan;
    private static readonly Color TextPrimary =
        TacticalUiTheme.TextPrimary;
    private static readonly Color TextSecondary =
        TacticalUiTheme.TextSecondary;
    private static readonly Color Error =
        TacticalUiTheme.Red;

    private readonly Dictionary<TMP_InputField, InputVisual> inputVisuals =
        new();
    private GameModeFlowController flow;
    private TMP_FontAsset font;
    private bool ownsFont;
    private TMP_Text statusText;
    private UnityEngine.UI.Image statusBackground;
    private CoopSessionController session;
    private PlayerAppearanceCatalog appearanceCatalog;
    private GameObject accountPanel;
    private GameObject lobbyPanel;
    private GameObject lobbyEntryPanel;
    private GameObject lobbyRoomPanel;
    private GameObject lobbyEntryActionsPanel;
    private GameObject lobbyRosterPanel;
    private TMP_Text authModeTitle;
    private TMP_Text authModeDescription;
    private TMP_Text loginTabLabel;
    private TMP_Text registerTabLabel;
    private UnityEngine.UI.Image loginTabImage;
    private UnityEngine.UI.Image registerTabImage;
    private TMP_Text lobbyAccountText;
    private TMP_Text lobbyConnectionText;
    private TMP_Text lobbySummaryText;
    private TMP_Text lobbyMembersText;
    private TMP_Text lobbyStatusText;
    private TMP_Text lobbyAppearanceText;
    private TMP_Text lobbyJoinCodeText;
    private RectTransform publicRoomsContent;
    private TMP_Text publicRoomsEmptyText;
    private readonly List<UnityEngine.UI.Button> publicRoomButtons = new();
    private string selectedPublicRoomId = string.Empty;
    private bool authenticationGateOnly;
    private bool authenticationCompletionRaised;
    private Action authenticationCompleted;
    private bool automaticallyRefreshPublicRooms;
    private bool requestedInitialPublicRooms;
    private int lobbyAppearanceIndex;
    private bool registerMode;
    private bool passwordVisible;
    private bool confirmationVisible;

    public CoopAccountController Controller { get; private set; }
    public GameObject AuthenticationPanel => accountPanel;
    public GameObject LobbyPanel => lobbyPanel;
    public bool IsRegisterMode => registerMode;
    public TMP_InputField UsernameInput { get; private set; }
    public TMP_InputField PasswordInput { get; private set; }
    public TMP_InputField ConfirmationInput { get; private set; }
    public UnityEngine.UI.Button LoginTabButton { get; private set; }
    public UnityEngine.UI.Button RegisterTabButton { get; private set; }
    public UnityEngine.UI.Button PasswordVisibilityButton { get; private set; }
    public UnityEngine.UI.Button ConfirmationVisibilityButton { get; private set; }
    public UnityEngine.UI.Button LoginButton { get; private set; }
    public UnityEngine.UI.Button RegisterButton { get; private set; }
    public UnityEngine.UI.Button LogoutButton { get; private set; }
    public UnityEngine.UI.Button DevelopmentAnonymousButton { get; private set; }
    public UnityEngine.UI.Button ReturnButton { get; private set; }
    public TMP_InputField JoinCodeInput { get; private set; }
    public UnityEngine.UI.Button CreateRoomButton { get; private set; }
    public UnityEngine.UI.Button JoinRoomButton { get; private set; }
    public UnityEngine.UI.Button PreviousAppearanceButton { get; private set; }
    public UnityEngine.UI.Button NextAppearanceButton { get; private set; }
    public UnityEngine.UI.Button ReadyButton { get; private set; }
    public UnityEngine.UI.Button LeaveRoomButton { get; private set; }
    public UnityEngine.UI.Button StartRoomButton { get; private set; }
    public UnityEngine.UI.Button RefreshRoomsButton { get; private set; }
    public UnityEngine.UI.Button JoinSelectedRoomButton { get; private set; }
    public RectTransform PublicRoomsContent => publicRoomsContent;
    public string VisibleStatus => statusText != null
        ? statusText.text
        : string.Empty;

    public static CoopAccountView Create(GameModeFlowController configuredFlow,
        ICoopAuthenticationGateway gateway, bool allowDevelopmentAnonymous,
        CoopSessionController configuredSession = null,
        PlayerAppearanceCatalog configuredAppearances = null)
    {
        GameObject canvasObject = ModeUiFactory.CreateCanvas(
            "Coop Account Canvas", 210);
        CoopAccountView view = canvasObject.AddComponent<CoopAccountView>();
        view.Build(configuredFlow, gateway, allowDevelopmentAnonymous,
            configuredSession, configuredAppearances, false, null);
        return view;
    }

    public static CoopAccountView CreateAuthenticationGate(
        GameModeFlowController configuredFlow,
        ICoopAuthenticationGateway gateway,
        Action onAuthenticated)
    {
        GameObject canvasObject = ModeUiFactory.CreateCanvas(
            "Account Gate Canvas", 220);
        CoopAccountView view = canvasObject.AddComponent<CoopAccountView>();
        view.Build(configuredFlow, gateway, false, null, null, true,
            onAuthenticated);
        return view;
    }

    public bool SignOutToAuthentication()
    {
        if (!authenticationGateOnly || Controller == null ||
            !Controller.IsSignedIn || Controller.IsBusy)
            return false;
        authenticationCompletionRaised = false;
        gameObject.SetActive(true);
        Logout();
        StartCoroutine(FocusAuthenticationAfterFrame());
        return !Controller.IsSignedIn;
    }

    private void Build(GameModeFlowController configuredFlow,
        ICoopAuthenticationGateway gateway, bool allowDevelopmentAnonymous,
        CoopSessionController configuredSession,
        PlayerAppearanceCatalog configuredAppearances,
        bool configuredAuthenticationGateOnly,
        Action configuredAuthenticationCompleted)
    {
        flow = configuredFlow;
        session = configuredSession;
        appearanceCatalog = configuredAppearances;
        authenticationGateOnly = configuredAuthenticationGateOnly;
        authenticationCompleted = configuredAuthenticationCompleted;
        automaticallyRefreshPublicRooms =
            gateway is SelfHostedAuthenticationGateway;
        Controller = new CoopAccountController(gateway,
            allowDevelopmentAnonymous);
        Controller.Changed += Refresh;
        font = ModeUiFactory.CreateChineseFont(out ownsFont);

        RectTransform canvas = (RectTransform)transform;
        UnityEngine.UI.Image backdrop = ModeUiFactory.CreateImage(
            "整备室背景", canvas, Background, true);
        TacticalUiTheme.ApplyMenuArt(backdrop, "lobby-backdrop",
            new Color(0.74f, 0.81f, 0.85f, 1f), false);
        UnityEngine.UI.Image dimmer = ModeUiFactory.CreateImage(
            "背景调暗", canvas,
            new Color(0.008f, 0.017f, 0.024f, 0.32f), true);
        dimmer.raycastTarget = false;
        BuildBackdrop(canvas);
        BuildNavigation(canvas);
        BuildAuthenticationPanel(canvas);
        if (!authenticationGateOnly)
            BuildLobbyPanel(canvas);

        if (session != null && appearanceCatalog != null &&
            appearanceCatalog.TryValidate(out _))
        {
            ConfigureLobbyModel();
            session.LobbyChanged += Refresh;
            session.StateChanged += HandleSessionStateChanged;
            session.PublicRoomsChanged += HandlePublicRoomsChanged;
        }

        ModeUiFactory.EnsureEventSystem();
        if (authenticationGateOnly)
        {
            ReturnButton.gameObject.SetActive(false);
            LogoutButton.gameObject.SetActive(false);
        }
        registerMode = false;
        Refresh();
        FocusInput(UsernameInput);
        RestoreSession();
    }

    private void BuildBackdrop(RectTransform canvas)
    {
        UnityEngine.UI.Image leftField = ModeUiFactory.CreateImage(
            "哑光侧背景", canvas,
            new Color(0.055f, 0.075f, 0.088f, 0.72f), false);
        ModeUiFactory.SetRect(leftField.rectTransform, Vector2.zero,
            new Vector2(0.54f, 1f), new Vector2(0f, 0.5f), Vector2.zero,
            Vector2.zero);

        RectTransform divider = ModeUiFactory.CreateRect("背景分界", canvas);
        ModeUiFactory.SetRect(divider, new Vector2(0.54f, 0f),
            new Vector2(0.54f, 1f), new Vector2(0.5f, 0.5f),
            new Vector2(1f, 0f), Vector2.zero);
        UnityEngine.UI.Image dividerImage =
            divider.gameObject.AddComponent<UnityEngine.UI.Image>();
        dividerImage.color = new Color(0.38f, 0.48f, 0.5f, 0.18f);
        dividerImage.raycastTarget = false;

        RectTransform topAccent = ModeUiFactory.CreateRect("顶部高亮线", canvas);
        ModeUiFactory.SetRect(topAccent, new Vector2(0f, 1f),
            new Vector2(1f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, 2f), Vector2.zero);
        UnityEngine.UI.Image accent =
            topAccent.gameObject.AddComponent<UnityEngine.UI.Image>();
        accent.color = new Color(Cyan.r, Cyan.g, Cyan.b, 0.72f);
        accent.raycastTarget = false;
    }

    private void BuildNavigation(RectTransform canvas)
    {
        ReturnButton = CreateButton(canvas, "返回模式选择", "返回模式选择",
            new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(0f, 1f), new Vector2(220f, 52f),
            new Vector2(52f, -38f), ButtonTone.Ghost, ReturnToModes, 18f);

        LogoutButton = CreateButton(canvas, "退出账号", "退出当前账号",
            new Vector2(1f, 1f), new Vector2(1f, 1f),
            new Vector2(1f, 1f), new Vector2(220f, 52f),
            new Vector2(-52f, -38f), ButtonTone.Danger, Logout, 18f);
    }

    private void BuildAuthenticationPanel(RectTransform canvas)
    {
        RectTransform root = ModeUiFactory.CreateRect("账号认证界面", canvas);
        ModeUiFactory.Stretch(root, 80f, 90f);
        accountPanel = root.gameObject;

        BuildAuthenticationHero(root);
        BuildAuthenticationCard(root);
    }

    private void BuildAuthenticationHero(RectTransform root)
    {
        RectTransform hero = CreatePanel(root, "合作行动介绍",
            new Vector2(820f, 800f), new Vector2(470f, -5f),
            new Color(0f, 0f, 0f, 0f));

        TMP_Text badge = CreateText(hero, "模式标签", "FPS / TACTICAL OPERATIONS",
            17f, TextAlignmentOptions.MidlineLeft, Cyan);
        ModeUiFactory.SetRect(badge.rectTransform, new Vector2(0f, 1f),
            new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(580f, 36f), new Vector2(54f, -58f));

        TMP_Text title = CreateText(hero, "模式标题",
            "整备就绪。\n进入战区。", 52f, TextAlignmentOptions.TopLeft,
            TextPrimary);
        title.fontStyle = FontStyles.Bold;
        title.lineSpacing = -10f;
        ModeUiFactory.SetRect(title.rectTransform, new Vector2(0f, 1f),
            new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(680f, 170f), new Vector2(54f, -118f));

        TMP_Text description = CreateText(hero, "模式说明",
            "一个账号，三种作战方式。完成训练、挑战单人战局，\n或与队友组成双人小队。",
            20f, TextAlignmentOptions.TopLeft, TextSecondary);
        description.enableWordWrapping = true;
        description.lineSpacing = 5f;
        ModeUiFactory.SetRect(description.rectTransform, new Vector2(0f, 1f),
            new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(680f, 90f), new Vector2(54f, -296f));

        CreateFeature(hero, "01", "新手教学",
            "学习移动、射击、换弹与不同部位伤害。", -410f);
        CreateFeature(hero, "02", "单人战斗",
            "体验波次、任务、背包、成长与肉鸽升级。", -520f);
        CreateFeature(hero, "03", "联机 PVE",
            "浏览公开房间或创建小队，与队友协同通关。", -630f);

        TMP_Text footer = CreateText(hero, "安全说明",
            "01 / 身份验证     02 / 选择行动     03 / 进入战区", 16f,
            TextAlignmentOptions.MidlineLeft,
            new Color(0.46f, 0.62f, 0.65f));
        ModeUiFactory.SetRect(footer.rectTransform, new Vector2(0f, 0f),
            new Vector2(0f, 0f), new Vector2(0f, 0f),
            new Vector2(720f, 36f), new Vector2(54f, 36f));
    }

    private void CreateFeature(Transform parent, string index, string title,
        string description, float y)
    {
        RectTransform marker = ModeUiFactory.CreateRect(
            $"功能 {index}", parent);
        ModeUiFactory.SetRect(marker, new Vector2(0f, 1f),
            new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(52f, 52f), new Vector2(54f, y));
        UnityEngine.UI.Image markerImage =
            marker.gameObject.AddComponent<UnityEngine.UI.Image>();
        markerImage.color = new Color(0.08f, 0.3f, 0.31f, 0.95f);
        markerImage.raycastTarget = false;
        TMP_Text number = CreateText(marker, "编号", index, 17f,
            TextAlignmentOptions.Center, Cyan);
        number.fontStyle = FontStyles.Bold;
        ModeUiFactory.Stretch(number.rectTransform);

        TMP_Text heading = CreateText(parent, $"功能标题 {index}", title,
            20f, TextAlignmentOptions.MidlineLeft, TextPrimary);
        heading.fontStyle = FontStyles.Bold;
        ModeUiFactory.SetRect(heading.rectTransform, new Vector2(0f, 1f),
            new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(470f, 28f), new Vector2(120f, y + 14f));

        TMP_Text body = CreateText(parent, $"功能说明 {index}", description,
            16f, TextAlignmentOptions.TopLeft, TextSecondary);
        body.enableWordWrapping = true;
        ModeUiFactory.SetRect(body.rectTransform, new Vector2(0f, 1f),
            new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(470f, 42f), new Vector2(120f, y - 14f));
    }

    private void BuildAuthenticationCard(RectTransform root)
    {
        RectTransform card = CreatePanel(root, "账号操作卡片",
            new Vector2(700f, 800f), new Vector2(-430f, -5f), Panel);
        TacticalUiTheme.ApplyMenuArt(card.GetComponent<UnityEngine.UI.Image>(),
            "nescia-panel-wide", Color.white);
        TacticalUiTheme.AddSurfaceChrome(card, Cyan);

        TacticalUiTheme.CreateIcon("账号安全图标", card, "locked", Cyan,
            new Vector2(1f, 1f), new Vector2(1f, 1f),
            new Vector2(1f, 1f), new Vector2(44f, 44f),
            new Vector2(-58f, -44f));

        TMP_Text section = CreateText(card, "页面标签", "玩家账号",
            16f, TextAlignmentOptions.MidlineLeft, Cyan);
        ModeUiFactory.SetRect(section.rectTransform, new Vector2(0f, 1f),
            new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(580f, 28f), new Vector2(58f, -42f));

        authModeTitle = CreateText(card, "账号标题", string.Empty,
            34f, TextAlignmentOptions.MidlineLeft, TextPrimary);
        authModeTitle.fontStyle = FontStyles.Bold;
        ModeUiFactory.SetRect(authModeTitle.rectTransform,
            new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(0f, 1f), new Vector2(580f, 52f),
            new Vector2(58f, -82f));

        authModeDescription = CreateText(card, "账号说明", string.Empty,
            17f, TextAlignmentOptions.TopLeft, TextSecondary);
        authModeDescription.enableWordWrapping = true;
        ModeUiFactory.SetRect(authModeDescription.rectTransform,
            new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(0f, 1f), new Vector2(580f, 48f),
            new Vector2(58f, -137f));

        RectTransform tabs = CreatePanel(card, "登录注册切换",
            new Vector2(584f, 58f), new Vector2(0f, 203f),
            new Color(0.075f, 0.09f, 0.10f, 1f));
        LoginTabButton = CreateButton(tabs, "登录页签", "登录",
            new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, 0.5f), new Vector2(-3f, 50f),
            new Vector2(4f, 0f), ButtonTone.Tab, ShowLoginMode, 18f);
        RegisterTabButton = CreateButton(tabs, "注册页签", "注册",
            new Vector2(0.5f, 0.5f), new Vector2(1f, 0.5f),
            new Vector2(0f, 0.5f), new Vector2(-3f, 50f),
            new Vector2(3f, 0f), ButtonTone.Tab, ShowRegisterMode, 18f);
        loginTabImage = LoginTabButton.GetComponent<UnityEngine.UI.Image>();
        registerTabImage =
            RegisterTabButton.GetComponent<UnityEngine.UI.Image>();
        loginTabLabel = LoginTabButton.GetComponentInChildren<TMP_Text>();
        registerTabLabel =
            RegisterTabButton.GetComponentInChildren<TMP_Text>();

        UsernameInput = CreateInput(card, "Username", "用户名",
            "输入 3—20 位用户名", new Vector2(0f, 102f),
            TMP_InputField.ContentType.Standard, 20, false,
            out _, out _);
        PasswordInput = CreateInput(card, "Password", "密码",
            "8—30 位，至少包含字母和数字", new Vector2(0f, -15f),
            TMP_InputField.ContentType.Password, 30, true,
            out UnityEngine.UI.Button passwordVisibility,
            out _);
        PasswordVisibilityButton = passwordVisibility;
        ConfirmationInput = CreateInput(card, "Password Confirmation",
            "确认密码", "再次输入相同密码", new Vector2(0f, -132f),
            TMP_InputField.ContentType.Password, 30, true,
            out UnityEngine.UI.Button confirmationVisibility,
            out _);
        ConfirmationVisibilityButton = confirmationVisibility;
        UsernameInput.onValueChanged.AddListener(_ => RefreshCredentialSubmission());
        PasswordInput.onValueChanged.AddListener(_ => RefreshCredentialSubmission());
        ConfirmationInput.onValueChanged.AddListener(_ => RefreshCredentialSubmission());

        LoginButton = CreateButton(card, "登录", "登录并进入模式大厅",
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f), new Vector2(584f, 62f),
            new Vector2(0f, -155f), ButtonTone.Primary, Login, 20f);
        RegisterButton = CreateButton(card, "注册", "创建账号并进入模式大厅",
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f), new Vector2(584f, 56f),
            new Vector2(0f, -215f), ButtonTone.Primary, Register, 20f);

        DevelopmentAnonymousButton = CreateButton(card,
            "开发调试身份", "使用开发调试身份",
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(0.5f, 0f), new Vector2(280f, 46f),
            new Vector2(0f, 102f), ButtonTone.Ghost,
            DevelopmentAnonymous, 16f);

        RectTransform status = CreatePanel(card, "账号状态",
            new Vector2(584f, 72f), new Vector2(0f, -341f),
            new Color(0.10f, 0.14f, 0.15f, 0.96f));
        statusBackground = status.GetComponent<UnityEngine.UI.Image>();
        statusText = CreateText(status, "状态文字",
            Controller.StatusMessage, 16f, TextAlignmentOptions.Center,
            TextPrimary);
        statusText.enableWordWrapping = true;
        ModeUiFactory.Stretch(statusText.rectTransform, 18f, 8f);
    }

    private void BuildLobbyPanel(RectTransform canvas)
    {
        RectTransform root = ModeUiFactory.CreateRect("合作房间大厅", canvas);
        ModeUiFactory.Stretch(root, 78f, 88f);
        lobbyPanel = root.gameObject;

        TMP_Text title = CreateText(root, "大厅标题", "行动整备大厅",
            38f, TextAlignmentOptions.MidlineLeft, TextPrimary);
        title.fontStyle = FontStyles.Bold;
        ModeUiFactory.SetRect(title.rectTransform, new Vector2(0f, 1f),
            new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(620f, 60f), new Vector2(0f, -10f));

        lobbyAccountText = CreateText(root, "当前账号", string.Empty,
            16f, TextAlignmentOptions.MidlineRight, TextSecondary);
        ModeUiFactory.SetRect(lobbyAccountText.rectTransform,
            new Vector2(1f, 1f), new Vector2(1f, 1f),
            new Vector2(1f, 1f), new Vector2(520f, 30f),
            new Vector2(0f, -3f));
        lobbyConnectionText = CreateText(root, "连接状态", string.Empty,
            18f, TextAlignmentOptions.MidlineRight, Cyan);
        lobbyConnectionText.fontStyle = FontStyles.Bold;
        ModeUiFactory.SetRect(lobbyConnectionText.rectTransform,
            new Vector2(1f, 1f), new Vector2(1f, 1f),
            new Vector2(1f, 1f), new Vector2(520f, 36f),
            new Vector2(0f, -38f));

        RectTransform summary = CreatePanel(root, "房间摘要",
            new Vector2(1764f, 72f), new Vector2(0f, 312f), RaisedPanel);
        TacticalUiTheme.ApplyMenuArt(summary.GetComponent<UnityEngine.UI.Image>(),
            "nescia-panel-strip", Color.white);
        TacticalUiTheme.AddSurfaceChrome(summary, Cyan, false);
        TacticalUiTheme.CreateIcon("摘要网络图标", summary, "signal3",
            TacticalUiTheme.Green, new Vector2(0f, 0.5f),
            new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
            new Vector2(30f, 30f), new Vector2(22f, 0f));
        lobbySummaryText = CreateText(summary, "房间摘要文字", string.Empty,
            18f, TextAlignmentOptions.MidlineLeft, TextPrimary);
        lobbySummaryText.fontStyle = FontStyles.Bold;
        ModeUiFactory.Stretch(lobbySummaryText.rectTransform, 66f, 8f);

        RectTransform left = CreatePanel(root, "房间操作区域",
            new Vector2(1080f, 620f), new Vector2(-340f, -40f), Panel);
        TacticalUiTheme.ApplyMenuArt(left.GetComponent<UnityEngine.UI.Image>(),
            "nescia-panel-wide", Color.white);
        TacticalUiTheme.AddSurfaceChrome(left, Cyan);
        BuildLobbyEntryPanel(left);
        BuildLobbyRoomPanel(left);

        RectTransform right = CreatePanel(root, "队伍与角色区域",
            new Vector2(650f, 620f), new Vector2(555f, -40f), Panel);
        TacticalUiTheme.ApplyMenuArt(right.GetComponent<UnityEngine.UI.Image>(),
            "nescia-panel-wide", Color.white);
        TacticalUiTheme.AddSurfaceChrome(right, TacticalUiTheme.Blue);
        BuildLobbyEntryActions(right);
        BuildRosterPanel(right);

        RectTransform status = CreatePanel(root, "大厅状态",
            new Vector2(1764f, 58f), new Vector2(0f, -388f),
            new Color(0.10f, 0.14f, 0.15f, 0.96f));
        lobbyStatusText = CreateText(status, "大厅状态文字", string.Empty,
            16f, TextAlignmentOptions.Center, Cyan);
        lobbyStatusText.enableWordWrapping = true;
        ModeUiFactory.Stretch(lobbyStatusText.rectTransform, 20f, 7f);
    }

    private void BuildLobbyEntryPanel(RectTransform parent)
    {
        RectTransform root = ModeUiFactory.CreateRect("创建或加入房间", parent);
        ModeUiFactory.Stretch(root);
        lobbyEntryPanel = root.gameObject;

        TMP_Text title = CreateText(root, "进入房间标题", "公开房间",
            30f, TextAlignmentOptions.MidlineLeft, TextPrimary);
        title.fontStyle = FontStyles.Bold;
        ModeUiFactory.SetRect(title.rectTransform, new Vector2(0f, 1f),
            new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(700f, 45f), new Vector2(46f, -42f));

        TacticalUiTheme.CreateIcon("大厅图标", root, "multiplayer", Cyan,
            new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(0f, 1f), new Vector2(38f, 38f),
            new Vector2(0f, -44f));

        RefreshRoomsButton = CreateButton(root, "刷新公开房间", "刷新列表",
            new Vector2(1f, 1f), new Vector2(1f, 1f),
            new Vector2(1f, 1f), new Vector2(170f, 44f),
            new Vector2(-46f, -39f), ButtonTone.Ghost,
            RefreshPublicRooms, 16f);

        TMP_Text description = CreateText(root, "进入房间说明",
            "浏览进行中的整备小队，选择有空位的房间加入。",
            17f, TextAlignmentOptions.TopLeft, TextSecondary);
        ModeUiFactory.SetRect(description.rectTransform,
            new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(0f, 1f), new Vector2(900f, 50f),
            new Vector2(46f, -95f));

        BuildPublicRoomScrollView(root);

        TMP_Text listHint = CreateText(root, "房间列表提示",
            "选择一个有空位的房间，再在右侧确认加入。", 16f,
            TextAlignmentOptions.MidlineLeft, TextSecondary);
        ModeUiFactory.SetRect(listHint.rectTransform,
            new Vector2(0f, 0f), new Vector2(0f, 0f),
            new Vector2(0f, 0f), new Vector2(920f, 36f),
            new Vector2(46f, 36f));
    }

    private void BuildLobbyEntryActions(RectTransform parent)
    {
        RectTransform root = ModeUiFactory.CreateRect("房间操作", parent);
        ModeUiFactory.Stretch(root);
        lobbyEntryActionsPanel = root.gameObject;

        TMP_Text kicker = CreateText(root, "操作标签", "小队 / 部署",
            16f, TextAlignmentOptions.MidlineLeft, Cyan);
        ModeUiFactory.SetRect(kicker.rectTransform,
            new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(0f, 1f), new Vector2(540f, 30f),
            new Vector2(42f, -34f));
        TMP_Text title = CreateText(root, "操作标题", "选择加入方式",
            26f, TextAlignmentOptions.MidlineLeft, TextPrimary);
        title.fontStyle = FontStyles.Bold;
        ModeUiFactory.SetRect(title.rectTransform,
            new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(0f, 1f), new Vector2(560f, 44f),
            new Vector2(42f, -70f));

        TMP_Text publicLabel = CreateText(root, "公开房间操作标签",
            "公开匹配", 16f, TextAlignmentOptions.MidlineLeft,
            TextSecondary);
        ModeUiFactory.SetRect(publicLabel.rectTransform,
            new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(0f, 1f), new Vector2(560f, 28f),
            new Vector2(42f, -154f));

        CreateRoomButton = CreateButton(root, "创建房间", "创建公开房间",
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0.5f, 1f), new Vector2(566f, 58f),
            new Vector2(0f, -194f), ButtonTone.Primary, CreateRoom, 18f);
        JoinSelectedRoomButton = CreateButton(root, "加入所选房间",
            "加入所选房间", new Vector2(0.5f, 1f),
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(566f, 58f), new Vector2(0f, -264f),
            ButtonTone.Secondary, JoinSelectedPublicRoom, 18f);

        TMP_Text inviteLabel = CreateText(root, "邀请码操作标签",
            "好友邀请", 16f, TextAlignmentOptions.MidlineLeft,
            TextSecondary);
        ModeUiFactory.SetRect(inviteLabel.rectTransform,
            new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(0f, 1f), new Vector2(560f, 28f),
            new Vector2(42f, -358f));
        JoinCodeInput = CreateInput(root, "Room Join Code", "邀请代码（可选）",
            "输入好友分享的代码", new Vector2(0f, -140f),
            TMP_InputField.ContentType.Alphanumeric, 12, false,
            out _, out _);
        RectTransform codeRoot = JoinCodeInput.transform.parent as RectTransform;
        if (codeRoot != null) codeRoot.sizeDelta = new Vector2(566f, 94f);
        JoinCodeInput.onValueChanged.AddListener(_ => Refresh());

        JoinRoomButton = CreateButton(root, "加入房间", "使用代码进入",
            new Vector2(1f, 0f), new Vector2(1f, 0f),
            new Vector2(1f, 0f), new Vector2(190f, 50f),
            new Vector2(-42f, 34f), ButtonTone.Ghost, JoinRoom, 16f);

        TMP_Text rule = CreateText(root, "房间规则",
            "最多 2 人  /  房主发起战局",
            15f, TextAlignmentOptions.MidlineLeft, TextSecondary);
        ModeUiFactory.SetRect(rule.rectTransform, new Vector2(0f, 0f),
            new Vector2(0f, 0f), new Vector2(0f, 0f),
            new Vector2(340f, 54f), new Vector2(42f, 34f));
    }

    private void BuildPublicRoomScrollView(RectTransform parent)
    {
        GameObject scrollObject = new("公开房间列表",
            typeof(RectTransform), typeof(CanvasRenderer),
            typeof(UnityEngine.UI.Image), typeof(UnityEngine.UI.ScrollRect));
        scrollObject.transform.SetParent(parent, false);
        RectTransform scrollRect = (RectTransform)scrollObject.transform;
        ModeUiFactory.SetRect(scrollRect, new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(984f, 370f), new Vector2(0f, -45f));
        UnityEngine.UI.Image background =
            scrollObject.GetComponent<UnityEngine.UI.Image>();
        TacticalUiTheme.ApplyMenuArt(background,
            "nescia-panel-wide", Color.white);
        background.raycastTarget = true;

        RectTransform viewport = ModeUiFactory.CreateRect("Viewport", scrollRect);
        ModeUiFactory.Stretch(viewport, 8f, 8f);
        UnityEngine.UI.Image viewportImage =
            viewport.gameObject.AddComponent<UnityEngine.UI.Image>();
        viewportImage.color = new Color(0f, 0f, 0f, 0.01f);
        viewport.gameObject.AddComponent<UnityEngine.UI.Mask>()
            .showMaskGraphic = false;

        publicRoomsContent = ModeUiFactory.CreateRect("Content", viewport);
        publicRoomsContent.anchorMin = new Vector2(0f, 1f);
        publicRoomsContent.anchorMax = new Vector2(1f, 1f);
        publicRoomsContent.pivot = new Vector2(0.5f, 1f);
        publicRoomsContent.anchoredPosition = Vector2.zero;
        publicRoomsContent.sizeDelta = Vector2.zero;
        UnityEngine.UI.VerticalLayoutGroup layout =
            publicRoomsContent.gameObject.AddComponent<
                UnityEngine.UI.VerticalLayoutGroup>();
        layout.spacing = 8f;
        layout.padding = new RectOffset(4, 4, 4, 4);
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        UnityEngine.UI.ContentSizeFitter fitter =
            publicRoomsContent.gameObject.AddComponent<
                UnityEngine.UI.ContentSizeFitter>();
        fitter.verticalFit =
            UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;

        UnityEngine.UI.ScrollRect scroll =
            scrollObject.GetComponent<UnityEngine.UI.ScrollRect>();
        scroll.viewport = viewport;
        scroll.content = publicRoomsContent;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = UnityEngine.UI.ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 28f;

        publicRoomsEmptyText = CreateText(viewport, "空房间提示",
            "正在读取公开房间……", 17f, TextAlignmentOptions.Center,
            TextSecondary);
        ModeUiFactory.Stretch(publicRoomsEmptyText.rectTransform, 18f, 18f);
    }

    private void BuildLobbyRoomPanel(RectTransform parent)
    {
        RectTransform root = ModeUiFactory.CreateRect("当前房间信息", parent);
        ModeUiFactory.Stretch(root);
        lobbyRoomPanel = root.gameObject;

        TMP_Text label = CreateText(root, "加入码标签", "邀请队友使用加入码",
            16f, TextAlignmentOptions.MidlineLeft, TextSecondary);
        ModeUiFactory.SetRect(label.rectTransform, new Vector2(0f, 1f),
            new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(880f, 30f), new Vector2(46f, -44f));
        lobbyJoinCodeText = CreateText(root, "当前加入码", "------",
            38f, TextAlignmentOptions.MidlineLeft, Cyan);
        lobbyJoinCodeText.fontStyle = FontStyles.Bold;
        lobbyJoinCodeText.characterSpacing = 8f;
        ModeUiFactory.SetRect(lobbyJoinCodeText.rectTransform,
            new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(0f, 1f), new Vector2(580f, 62f),
            new Vector2(46f, -79f));

        TMP_Text roomHeading = CreateText(root, "房间整备标题",
            "小队整备", 28f, TextAlignmentOptions.MidlineLeft,
            TextPrimary);
        roomHeading.fontStyle = FontStyles.Bold;
        ModeUiFactory.SetRect(roomHeading.rectTransform,
            new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(0f, 1f), new Vector2(880f, 48f),
            new Vector2(46f, -158f));

        TMP_Text flowText = CreateText(root, "房间流程",
            "选择角色  /  完成准备  /  房主开始战局",
            17f, TextAlignmentOptions.TopLeft, TextSecondary);
        flowText.lineSpacing = 10f;
        ModeUiFactory.SetRect(flowText.rectTransform,
            new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(0f, 1f), new Vector2(900f, 48f),
            new Vector2(46f, -219f));

        ReadyButton = CreateButton(root, "准备状态", "确认角色并准备",
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f), new Vector2(956f, 60f),
            new Vector2(0f, -30f), ButtonTone.Primary, ToggleReady, 19f);
        StartRoomButton = CreateButton(root, "开始战局", "房主开始战局",
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f), new Vector2(956f, 58f),
            new Vector2(0f, -104f), ButtonTone.Secondary, StartRoom, 18f);
        LeaveRoomButton = CreateButton(root, "离开房间", "离开当前房间",
            new Vector2(1f, 0f), new Vector2(1f, 0f),
            new Vector2(1f, 0f), new Vector2(250f, 48f),
            new Vector2(-46f, 35f), ButtonTone.Danger, LeaveRoom, 16f);
    }

    private void BuildRosterPanel(RectTransform parent)
    {
        RectTransform root = ModeUiFactory.CreateRect("小队整备", parent);
        ModeUiFactory.Stretch(root);
        lobbyRosterPanel = root.gameObject;

        TMP_Text rosterTitle = CreateText(root, "小队标题", "当前小队",
            25f, TextAlignmentOptions.MidlineLeft, TextPrimary);
        rosterTitle.fontStyle = FontStyles.Bold;
        ModeUiFactory.SetRect(rosterTitle.rectTransform,
            new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(0f, 1f), new Vector2(520f, 44f),
            new Vector2(42f, -36f));

        TacticalUiTheme.CreateIcon("小队图标", root, "multiplayer",
            TacticalUiTheme.Blue, new Vector2(0f, 1f),
            new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(34f, 34f), new Vector2(0f, -41f));

        RectTransform members = CreatePanel(root, "玩家席位",
            new Vector2(566f, 210f), new Vector2(0f, 110f), RaisedPanel);
        AddOutline(members, new Color(0.15f, 0.34f, 0.43f, 0.55f), 1f);
        lobbyMembersText = CreateText(members, "房间成员", string.Empty,
            16f, TextAlignmentOptions.TopLeft, TextPrimary);
        lobbyMembersText.enableWordWrapping = true;
        lobbyMembersText.lineSpacing = 8f;
        ModeUiFactory.Stretch(lobbyMembersText.rectTransform, 68f, 12f);
        TacticalUiTheme.CreateIcon("玩家席位图标 1", members,
            "singleplayer", Cyan, new Vector2(0f, 0.5f),
            new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
            new Vector2(30f, 30f), new Vector2(20f, 48f));
        TacticalUiTheme.CreateIcon("玩家席位图标 2", members,
            "singleplayer", TextSecondary, new Vector2(0f, 0.5f),
            new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
            new Vector2(30f, 30f), new Vector2(20f, -48f));

        TMP_Text appearanceTitle = CreateText(root, "角色选择标题",
            "出战角色", 18f, TextAlignmentOptions.MidlineLeft, TextSecondary);
        ModeUiFactory.SetRect(appearanceTitle.rectTransform,
            new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
            new Vector2(0f, 0.5f), new Vector2(480f, 32f),
            new Vector2(42f, -24f));

        lobbyAppearanceText = CreateText(root, "已选角色", string.Empty,
            28f, TextAlignmentOptions.MidlineLeft, Cyan);
        lobbyAppearanceText.fontStyle = FontStyles.Bold;
        ModeUiFactory.SetRect(lobbyAppearanceText.rectTransform,
            new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
            new Vector2(0f, 0.5f), new Vector2(540f, 50f),
            new Vector2(42f, -75f));

        PreviousAppearanceButton = CreateButton(root, "上一个角色",
            "上一个角色", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
            new Vector2(0f, 0.5f), new Vector2(270f, 54f),
            new Vector2(42f, -132f), ButtonTone.Ghost,
            PreviousLobbyAppearance, 17f);
        NextAppearanceButton = CreateButton(root, "下一个角色",
            "下一个角色", new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
            new Vector2(1f, 0.5f), new Vector2(270f, 54f),
            new Vector2(-42f, -132f), ButtonTone.Ghost,
            NextLobbyAppearance, 17f);

        TMP_Text tip = CreateText(root, "角色提示",
            "角色外观将在进入战区后同步给所有队员。\n准备后仍可取消准备并重新选择。",
            16f, TextAlignmentOptions.TopLeft, TextSecondary);
        tip.enableWordWrapping = true;
        tip.lineSpacing = 6f;
        ModeUiFactory.SetRect(tip.rectTransform,
            new Vector2(0f, 0f), new Vector2(0f, 0f),
            new Vector2(0f, 0f), new Vector2(560f, 72f),
            new Vector2(42f, 38f));
    }

    private TMP_InputField CreateInput(Transform parent, string objectName,
        string fieldLabel, string placeholderValue, Vector2 position,
        TMP_InputField.ContentType contentType, int characterLimit,
        bool includesVisibilityToggle,
        out UnityEngine.UI.Button visibilityButton,
        out TMP_Text visibilityLabel)
    {
        RectTransform fieldRoot = ModeUiFactory.CreateRect(
            $"{objectName} Field", parent);
        ModeUiFactory.SetRect(fieldRoot, new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(584f, 102f), position);

        TMP_Text label = CreateText(fieldRoot, "字段名称", fieldLabel,
            15f, TextAlignmentOptions.MidlineLeft, TextSecondary);
        label.fontStyle = FontStyles.Bold;
        ModeUiFactory.SetRect(label.rectTransform, new Vector2(0f, 1f),
            new Vector2(1f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, 25f), Vector2.zero);

        GameObject inputObject = new(objectName, typeof(RectTransform),
            typeof(CanvasRenderer), typeof(UnityEngine.UI.Image),
            typeof(UnityEngine.UI.Outline), typeof(TMP_InputField));
        inputObject.transform.SetParent(fieldRoot, false);
        RectTransform rect = (RectTransform)inputObject.transform;
        ModeUiFactory.SetRect(rect, new Vector2(0f, 0f),
            new Vector2(1f, 0f), new Vector2(0.5f, 0f),
            new Vector2(0f, 62f), Vector2.zero);
        UnityEngine.UI.Image background =
            inputObject.GetComponent<UnityEngine.UI.Image>();
        background.color = Field;
        UnityEngine.UI.Outline outline =
            inputObject.GetComponent<UnityEngine.UI.Outline>();
        outline.effectColor = new Color(0.24f, 0.30f, 0.32f, 0.78f);
        outline.effectDistance = new Vector2(1f, -1f);

        string inputIconName = objectName == "Username"
            ? "singleplayer"
            : "locked";
        TacticalUiTheme.CreateIcon("字段图标", rect, inputIconName,
            TextSecondary, new Vector2(0f, 0.5f),
            new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
            new Vector2(28f, 28f), new Vector2(18f, 0f));

        RectTransform viewport = ModeUiFactory.CreateRect("文字视口", rect);
        viewport.gameObject.AddComponent<UnityEngine.UI.RectMask2D>();
        viewport.anchorMin = Vector2.zero;
        viewport.anchorMax = Vector2.one;
        viewport.offsetMin = new Vector2(62f, 6f);
        viewport.offsetMax = new Vector2(
            includesVisibilityToggle ? -94f : -18f, -6f);

        TMP_Text value = CreateText(viewport, "输入内容", string.Empty,
            19f, TextAlignmentOptions.MidlineLeft, TextPrimary);
        ModeUiFactory.Stretch(value.rectTransform);
        TMP_Text placeholder = CreateText(viewport, "输入提示",
            placeholderValue, 17f, TextAlignmentOptions.MidlineLeft,
            new Color(0.39f, 0.49f, 0.54f));
        ModeUiFactory.Stretch(placeholder.rectTransform);

        TMP_InputField input = inputObject.GetComponent<TMP_InputField>();
        input.targetGraphic = background;
        input.textViewport = viewport;
        input.textComponent = value;
        input.placeholder = placeholder;
        input.contentType = contentType;
        input.lineType = TMP_InputField.LineType.SingleLine;
        input.characterLimit = characterLimit;
        input.customCaretColor = true;
        input.caretColor = Cyan;
        input.caretWidth = 3;
        input.caretBlinkRate = 0.75f;
        input.selectionColor = new Color(0.2f, 0.68f, 0.65f, 0.55f);
        input.onSelect.AddListener(_ => SetInputFocused(input, true));
        input.onDeselect.AddListener(_ => SetInputFocused(input, false));

        RectTransform focusLine = ModeUiFactory.CreateRect("焦点高亮线", rect);
        ModeUiFactory.SetRect(focusLine, new Vector2(0f, 0f),
            new Vector2(1f, 0f), new Vector2(0.5f, 0f),
            new Vector2(0f, 3f), Vector2.zero);
        UnityEngine.UI.Image focusLineImage =
            focusLine.gameObject.AddComponent<UnityEngine.UI.Image>();
        focusLineImage.color = new Color(Cyan.r, Cyan.g, Cyan.b, 0f);
        focusLineImage.raycastTarget = false;

        visibilityButton = null;
        visibilityLabel = null;
        if (includesVisibilityToggle)
        {
            visibilityButton = CreateButton(rect, "密码可见性", "显示",
                new Vector2(1f, 0f), new Vector2(1f, 1f),
                new Vector2(1f, 0.5f), new Vector2(78f, 0f),
                Vector2.zero, ButtonTone.Inline, () =>
                    TogglePasswordVisibility(input), 15f);
            visibilityLabel =
                visibilityButton.GetComponentInChildren<TMP_Text>();
        }

        inputVisuals[input] = new InputVisual(fieldRoot.gameObject, label,
            background, outline, focusLineImage, visibilityLabel);
        return input;
    }

    private UnityEngine.UI.Button CreateButton(Transform parent,
        string objectName, string label,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
        Vector2 size, Vector2 position, ButtonTone tone,
        UnityEngine.Events.UnityAction action, float fontSize)
    {
        GameObject buttonObject = new(objectName, typeof(RectTransform),
            typeof(CanvasRenderer), typeof(UnityEngine.UI.Image),
            typeof(UnityEngine.UI.Button));
        buttonObject.transform.SetParent(parent, false);
        ModeUiFactory.SetRect((RectTransform)buttonObject.transform,
            anchorMin, anchorMax, pivot, size, position);
        UnityEngine.UI.Image image =
            buttonObject.GetComponent<UnityEngine.UI.Image>();
        UnityEngine.UI.Button button =
            buttonObject.GetComponent<UnityEngine.UI.Button>();
        ConfigureButton(button, image, tone);
        if (tone == ButtonTone.Ghost || tone == ButtonTone.Secondary)
        {
            TacticalUiTheme.ApplyMenuArt(image, "nescia-button", Color.white);
            UnityEngine.UI.ColorBlock artColors = button.colors;
            artColors.normalColor = Color.white;
            artColors.highlightedColor = new Color(0.79f, 1f, 0.97f, 1f);
            artColors.selectedColor = artColors.highlightedColor;
            artColors.pressedColor = new Color(0.63f, 0.79f, 0.78f, 1f);
            artColors.disabledColor = new Color(0.38f, 0.42f, 0.43f, 0.72f);
            button.colors = artColors;
        }
        button.onClick.AddListener(action);
        TMP_Text text = CreateText(buttonObject.transform, "按钮文字", label,
            fontSize, TextAlignmentOptions.Center,
            tone == ButtonTone.Primary ? Background : TextPrimary);
        text.fontStyle = FontStyles.Bold;
        ModeUiFactory.Stretch(text.rectTransform, 8f, 4f);
        string iconName = ButtonIconFor(objectName);
        if (!string.IsNullOrEmpty(iconName))
        {
            Color iconColor = tone == ButtonTone.Primary
                ? Background
                : tone == ButtonTone.Danger
                    ? new Color(1f, 0.72f, 0.69f, 1f)
                    : Cyan;
            TacticalUiTheme.CreateIcon("按钮图标", buttonObject.transform,
                iconName, iconColor, new Vector2(0f, 0.5f),
                new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(24f, 24f), new Vector2(16f, 0f));
        }
        return button;
    }

    private static string ButtonIconFor(string objectName)
    {
        return objectName switch
        {
            "返回模式选择" => "return",
            "退出账号" => "exit",
            "登录" => "unlocked",
            "注册" => "plus",
            "创建房间" => "plus",
            "加入所选房间" => "multiplayer",
            "加入房间" => "signal3",
            "准备状态" => "checkmark",
            "开始战局" => "target",
            "离开房间" => "exit",
            "上一个角色" => "arrowLeft",
            "下一个角色" => "arrowRight",
            _ => string.Empty
        };
    }

    private static void ConfigureButton(UnityEngine.UI.Button button,
        UnityEngine.UI.Image image, ButtonTone tone)
    {
        Color normal = tone switch
        {
            ButtonTone.Primary => Cyan,
            ButtonTone.Secondary => new Color(0.18f, 0.28f, 0.30f, 1f),
            ButtonTone.Danger => new Color(0.27f, 0.13f, 0.13f, 1f),
            ButtonTone.Inline => new Color(0.09f, 0.12f, 0.13f, 1f),
            ButtonTone.Tab => new Color(0.09f, 0.12f, 0.13f, 1f),
            _ => new Color(0.10f, 0.14f, 0.15f, 1f)
        };
        image.color = normal;
        UnityEngine.UI.ColorBlock colors = button.colors;
        colors.normalColor = normal;
        colors.highlightedColor = tone == ButtonTone.Primary
            ? new Color(0.56f, 0.90f, 0.84f, 1f)
            : new Color(Mathf.Min(1f, normal.r + 0.04f),
                Mathf.Min(1f, normal.g + 0.07f),
                Mathf.Min(1f, normal.b + 0.07f), normal.a);
        colors.selectedColor = colors.highlightedColor;
        colors.pressedColor = new Color(
            normal.r * 0.78f, normal.g * 0.78f, normal.b * 0.78f, normal.a);
        colors.disabledColor = new Color(0.13f, 0.18f, 0.2f, 0.68f);
        colors.colorMultiplier = 1f;
        colors.fadeDuration = 0.08f;
        button.colors = colors;
    }

    private RectTransform CreatePanel(Transform parent, string objectName,
        Vector2 size, Vector2 position, Color color)
    {
        RectTransform rect = ModeUiFactory.CreateRect(objectName, parent);
        ModeUiFactory.SetRect(rect, new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            size, position);
        UnityEngine.UI.Image image =
            rect.gameObject.AddComponent<UnityEngine.UI.Image>();
        image.color = color;
        image.raycastTarget = false;
        return rect;
    }

    private TMP_Text CreateText(Transform parent, string objectName,
        string value, float size, TextAlignmentOptions alignment, Color color)
    {
        return ModeUiFactory.CreateText(objectName, parent, value, size,
            alignment, font, color);
    }

    private static void AddOutline(RectTransform rect, Color color,
        float distance)
    {
        UnityEngine.UI.Outline outline =
            rect.gameObject.AddComponent<UnityEngine.UI.Outline>();
        outline.effectColor = color;
        outline.effectDistance = new Vector2(distance, -distance);
    }

    private async void RestoreSession()
    {
        await Controller.RestoreAsync();
        RefreshRoomsAfterAuthentication();
        if (this != null && !Controller.IsSignedIn)
            StartCoroutine(FocusAuthenticationAfterFrame());
    }

    private IEnumerator FocusAuthenticationAfterFrame()
    {
        yield return null;
        if (Controller == null || Controller.IsBusy ||
            Controller.IsSignedIn || accountPanel == null ||
            !accountPanel.activeInHierarchy ||
            UsernameInput.isFocused || PasswordInput.isFocused ||
            ConfirmationInput.isFocused)
            yield break;
        FocusInput(UsernameInput);
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        if (hasFocus && Controller != null && !Controller.IsSignedIn &&
            accountPanel != null && accountPanel.activeInHierarchy)
            StartCoroutine(FocusAuthenticationAfterFrame());
    }

    private async void Login()
    {
        string password = PasswordInput.text;
        await Controller.SignInAsync(UsernameInput.text, password);
        ClearSensitiveInputs();
        RefreshRoomsAfterAuthentication();
    }

    private async void Register()
    {
        string password = PasswordInput.text;
        string confirmation = ConfirmationInput.text;
        await Controller.RegisterAsync(UsernameInput.text, password,
            confirmation);
        ClearSensitiveInputs();
        RefreshRoomsAfterAuthentication();
    }

    private async void DevelopmentAnonymous()
    {
        await Controller.SignInAnonymouslyForDevelopmentAsync();
        ClearSensitiveInputs();
        RefreshRoomsAfterAuthentication();
    }

    private void Logout()
    {
        Controller.SignOut();
        ClearSensitiveInputs();
        UsernameInput.text = string.Empty;
        ShowLoginMode();
    }

    private void ReturnToModes()
    {
        if (!Controller.IsBusy)
            flow.TryReturnToEntry();
    }

    private void ShowLoginMode()
    {
        if (Controller.IsBusy || Controller.IsSignedIn) return;
        registerMode = false;
        ConfirmationInput.text = string.Empty;
        Refresh();
        FocusInput(string.IsNullOrWhiteSpace(UsernameInput.text)
            ? UsernameInput
            : PasswordInput);
    }

    private void ShowRegisterMode()
    {
        if (Controller.IsBusy || Controller.IsSignedIn) return;
        registerMode = true;
        Refresh();
        FocusInput(string.IsNullOrWhiteSpace(UsernameInput.text)
            ? UsernameInput
            : PasswordInput);
    }

    private void TogglePasswordVisibility(TMP_InputField input)
    {
        if (input == null) return;
        bool visible;
        if (input == PasswordInput)
        {
            passwordVisible = !passwordVisible;
            visible = passwordVisible;
        }
        else
        {
            confirmationVisible = !confirmationVisible;
            visible = confirmationVisible;
        }

        input.contentType = visible
            ? TMP_InputField.ContentType.Standard
            : TMP_InputField.ContentType.Password;
        input.ForceLabelUpdate();
        if (inputVisuals.TryGetValue(input, out InputVisual visual) &&
            visual.VisibilityLabel != null)
        {
            visual.VisibilityLabel.text = visible ? "隐藏" : "显示";
        }
        FocusInput(input);
    }

    private void FocusInput(TMP_InputField input)
    {
        if (input == null || !input.gameObject.activeInHierarchy ||
            EventSystem.current == null) return;
        EventSystem.current.SetSelectedGameObject(input.gameObject);
        input.Select();
        input.ActivateInputField();
        SetInputFocused(input, true);
    }

    private void LateUpdate()
    {
        if (ClientQuitDialog.IsBlockingInput || ClientQuitDialog.EscapeHandledThisFrame) return;
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null || !keyboard.tabKey.wasPressedThisFrame ||
            Controller == null || Controller.IsBusy || Controller.IsSignedIn ||
            accountPanel == null || !accountPanel.activeInHierarchy)
            return;

        TMP_InputField[] fields = registerMode
            ? new[] { UsernameInput, PasswordInput, ConfirmationInput }
            : new[] { UsernameInput, PasswordInput };
        GameObject selected = EventSystem.current != null
            ? EventSystem.current.currentSelectedGameObject
            : null;
        int current = Array.FindIndex(fields,
            field => field != null && field.gameObject == selected);
        bool reverse = keyboard.leftShiftKey.isPressed ||
                       keyboard.rightShiftKey.isPressed;
        int next = current < 0 ? 0 :
            (current + (reverse ? fields.Length - 1 : 1)) % fields.Length;
        FocusInput(fields[next]);
    }

    private void SetInputFocused(TMP_InputField input, bool focused)
    {
        if (input == null ||
            !inputVisuals.TryGetValue(input, out InputVisual visual)) return;
        visual.Background.color = focused
            ? new Color(0.018f, 0.075f, 0.09f, 1f)
            : Field;
        visual.Outline.effectColor = focused
            ? Cyan
            : new Color(0.15f, 0.28f, 0.33f, 1f);
        visual.Outline.effectDistance = focused
            ? new Vector2(2f, -2f)
            : new Vector2(1f, -1f);
        visual.FocusLine.color = new Color(
            Cyan.r, Cyan.g, Cyan.b, focused ? 1f : 0f);
        visual.Label.color = focused ? Cyan : TextSecondary;
    }

    private void ConfigureLobbyModel()
    {
        IReadOnlyList<PlayerAppearanceDefinition> definitions =
            appearanceCatalog.Definitions;
        PlayerAppearanceDefinition selected =
            PlayerAppearanceSelection.LoadOrDefault(appearanceCatalog, out _);
        lobbyAppearanceIndex = 0;
        for (int index = 0; index < definitions.Count; index++)
        {
            if (definitions[index] != selected) continue;
            lobbyAppearanceIndex = index;
            break;
        }
        if (!session.HasActiveSession)
        {
            session.ConfigureLobby(definitions.Select(value => value.StableId),
                definitions[lobbyAppearanceIndex].StableId,
                CoopSessionController.DefaultMapId);
        }
    }

    private async void CreateRoom()
    {
        if (session != null)
        {
            string owner = string.IsNullOrWhiteSpace(Controller.Username)
                ? "玩家"
                : Controller.Username.Trim();
            await session.HostAsync(roomName: $"{owner}的小队");
        }
        Refresh();
    }

    private async void JoinRoom()
    {
        if (session != null) await session.JoinAsync(JoinCodeInput?.text);
        Refresh();
    }

    private async void JoinSelectedPublicRoom()
    {
        if (session == null || string.IsNullOrWhiteSpace(selectedPublicRoomId))
            return;
        await session.JoinPublicRoomAsync(selectedPublicRoomId);
        Refresh();
    }

    private async void RefreshPublicRooms()
    {
        if (session == null || !Controller.IsSignedIn) return;
        requestedInitialPublicRooms = true;
        await session.RefreshPublicRoomsAsync();
        if (!session.PublicRooms.Any(value =>
                value.SessionId == selectedPublicRoomId))
            selectedPublicRoomId = string.Empty;
        RebuildPublicRoomList();
        Refresh();
    }

    private void RefreshRoomsAfterAuthentication()
    {
        if (automaticallyRefreshPublicRooms && !authenticationGateOnly &&
            Controller.IsSignedIn &&
            session != null && !requestedInitialPublicRooms)
            RefreshPublicRooms();
    }

    private void HandlePublicRoomsChanged()
    {
        RebuildPublicRoomList();
        Refresh();
    }

    private void SelectPublicRoom(string sessionId)
    {
        selectedPublicRoomId = sessionId ?? string.Empty;
        RebuildPublicRoomList();
        Refresh();
    }

    private void RebuildPublicRoomList()
    {
        if (publicRoomsContent == null || session == null) return;
        for (int index = 0; index < publicRoomButtons.Count; index++)
        {
            if (publicRoomButtons[index] != null)
                Destroy(publicRoomButtons[index].gameObject);
        }
        publicRoomButtons.Clear();

        bool hasRooms = session.PublicRooms.Count > 0;
        if (publicRoomsEmptyText != null)
        {
            publicRoomsEmptyText.gameObject.SetActive(!hasRooms);
            publicRoomsEmptyText.text = session.IsPublicRoomQueryInProgress
                ? "正在刷新公开房间……"
                : !string.IsNullOrWhiteSpace(session.PublicRoomBrowserFailure)
                    ? "房间列表暂时不可用，请点击刷新重试。"
                    : "暂无可加入房间，你可以创建一个公开小队。";
        }

        for (int index = 0; index < session.PublicRooms.Count; index++)
        {
            CoopPublicRoomSnapshot room = session.PublicRooms[index];
            UnityEngine.UI.Button button = CreateButton(publicRoomsContent,
                $"公开房间 {index + 1}", room.DisplayName,
                Vector2.zero, Vector2.one,
                new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero,
                ButtonTone.Ghost, () => SelectPublicRoom(room.SessionId), 16f);
            UnityEngine.UI.LayoutElement layout =
                button.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
            layout.preferredHeight = 62f;
            layout.minHeight = 62f;
            TMP_Text roomName = button.GetComponentInChildren<TMP_Text>();
            roomName.alignment = TextAlignmentOptions.MidlineLeft;
            ModeUiFactory.Stretch(roomName.rectTransform, 58f, 5f);
            roomName.rectTransform.offsetMax = new Vector2(-210f, -5f);

            TacticalUiTheme.CreateIcon("房间网络状态", button.transform,
                "signal3", TacticalUiTheme.Green,
                new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(0f, 0.5f), new Vector2(24f, 24f),
                new Vector2(18f, 0f));

            TMP_Text map = CreateText(button.transform, "地图名称",
                room.MapId, 13f, TextAlignmentOptions.MidlineRight,
                TextSecondary);
            ModeUiFactory.SetRect(map.rectTransform,
                new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(1f, 0.5f), new Vector2(105f, 38f),
                new Vector2(-114f, 0f));

            RectTransform population = TacticalUiTheme.CreatePill(
                button.transform, "房间人数", new Vector2(74f, 34f),
                Vector2.zero, new Color(0.04f, 0.18f, 0.19f, 1f));
            ModeUiFactory.SetRect(population,
                new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(1f, 0.5f), new Vector2(74f, 34f),
                new Vector2(-18f, 0f));
            TMP_Text populationText = CreateText(population, "人数文字",
                $"{room.PlayerCount}/{room.MaximumPlayers}", 14f,
                TextAlignmentOptions.Center, Cyan);
            ModeUiFactory.Stretch(populationText.rectTransform, 4f, 2f);

            bool selected = room.SessionId == selectedPublicRoomId;
            if (selected)
            {
                UnityEngine.UI.Image image =
                    button.GetComponent<UnityEngine.UI.Image>();
                Color selectedColor = new(0.18f, 0.28f, 0.30f, 1f);
                image.color = selectedColor;
                UnityEngine.UI.ColorBlock selectedColors = button.colors;
                selectedColors.normalColor = selectedColor;
                selectedColors.selectedColor = selectedColor;
                button.colors = selectedColors;
                RectTransform marker = ModeUiFactory.CreateRect(
                    "所选房间标记", button.transform);
                ModeUiFactory.SetRect(marker, Vector2.zero,
                    new Vector2(0f, 1f), new Vector2(0f, 0.5f),
                    new Vector2(4f, 0f), Vector2.zero);
                UnityEngine.UI.Image markerImage =
                    marker.gameObject.AddComponent<UnityEngine.UI.Image>();
                markerImage.color = Cyan;
                markerImage.raycastTarget = false;
            }
            publicRoomButtons.Add(button);
        }
    }

    private void PreviousLobbyAppearance()
    {
        ChangeLobbyAppearance(-1);
    }

    private void NextLobbyAppearance()
    {
        ChangeLobbyAppearance(1);
    }

    private async void ChangeLobbyAppearance(int direction)
    {
        if (appearanceCatalog == null ||
            appearanceCatalog.Definitions.Count == 0 || session == null) return;
        lobbyAppearanceIndex = (lobbyAppearanceIndex + direction +
                                appearanceCatalog.Definitions.Count) %
                               appearanceCatalog.Definitions.Count;
        string id = appearanceCatalog.Definitions[lobbyAppearanceIndex].StableId;
        if (session.IsConnected)
            await session.SelectLobbyCharacterAsync(id);
        else
            session.SetPendingLobbyAppearance(id);
        Refresh();
    }

    private async void ToggleReady()
    {
        if (session != null)
            await session.SetLobbyReadyAsync(!session.IsLocalReady);
        Refresh();
    }

    private async void LeaveRoom()
    {
        if (session != null) await session.LeaveAsync();
        selectedPublicRoomId = string.Empty;
        requestedInitialPublicRooms = false;
        RefreshRoomsAfterAuthentication();
        Refresh();
    }

    private async void StartRoom()
    {
        if (session != null) await session.StartLobbyGameAsync();
        Refresh();
    }

    private void HandleSessionStateChanged(CoopSessionState _)
    {
        Refresh();
    }

    private void ClearSensitiveInputs()
    {
        if (PasswordInput != null) PasswordInput.text = string.Empty;
        if (ConfirmationInput != null) ConfirmationInput.text = string.Empty;
    }

    private void Refresh()
    {
        if (Controller == null || statusText == null) return;
        bool signedIn = Controller.IsSignedIn;
        bool inputsEnabled = !Controller.IsBusy && !signedIn;

        accountPanel.SetActive(!signedIn);
        if (lobbyPanel != null) lobbyPanel.SetActive(signedIn);
        LogoutButton.gameObject.SetActive(false);
        ReturnButton.gameObject.SetActive(!authenticationGateOnly && signedIn);
        ReturnButton.interactable = signedIn && !Controller.IsBusy && flow != null &&
                                    !flow.IsLoading;

        SetInputActive(UsernameInput, !signedIn);
        SetInputActive(PasswordInput, !signedIn);
        SetInputActive(ConfirmationInput, !signedIn && registerMode);
        LoginButton.gameObject.SetActive(!signedIn && !registerMode);
        RegisterButton.gameObject.SetActive(!signedIn && registerMode);
        RefreshCredentialSubmission();
        DevelopmentAnonymousButton.gameObject.SetActive(!signedIn &&
            Controller.AllowDevelopmentAnonymous);
        DevelopmentAnonymousButton.interactable = inputsEnabled;

        statusText.text = Controller.StatusMessage;
        bool failed = !string.IsNullOrWhiteSpace(Controller.LastFailure);
        statusText.color = failed ? Error : Cyan;
        statusBackground.color = failed
            ? new Color(0.25f, 0.055f, 0.052f, 0.94f)
            : new Color(0.10f, 0.14f, 0.15f, 0.96f);
        RefreshAuthenticationMode(inputsEnabled);
        if (authenticationGateOnly)
        {
            if (signedIn && !authenticationCompletionRaised)
            {
                authenticationCompletionRaised = true;
                authenticationCompleted?.Invoke();
                gameObject.SetActive(false);
            }
            return;
        }
        RefreshLobby(signedIn);
    }

    private void RefreshCredentialSubmission()
    {
        if (Controller == null || LoginButton == null || RegisterButton == null)
            return;
        bool credentialsPresent = !Controller.IsBusy &&
            !Controller.IsSignedIn &&
            !string.IsNullOrWhiteSpace(UsernameInput.text) &&
            !string.IsNullOrWhiteSpace(PasswordInput.text);
        LoginButton.interactable = credentialsPresent;
        RegisterButton.interactable = credentialsPresent &&
            !string.IsNullOrWhiteSpace(ConfirmationInput.text);
        RefreshSubmissionVisual(LoginButton);
        RefreshSubmissionVisual(RegisterButton);
    }

    private static void RefreshSubmissionVisual(UnityEngine.UI.Button button)
    {
        Color contentColor = button.interactable ? Background : TextSecondary;
        TMP_Text label = button.GetComponentInChildren<TMP_Text>(true);
        if (label != null)
            label.color = contentColor;
        Transform icon = button.transform.Find("按钮图标");
        if (icon != null && icon.TryGetComponent(out UnityEngine.UI.Image image))
            image.color = contentColor;
    }

    private void RefreshAuthenticationMode(bool inputsEnabled)
    {
        authModeTitle.text = registerMode ? "创建玩家账号" : "欢迎归队";
        authModeDescription.text = registerMode
            ? "创建新账号后将自动登录，并进入模式选择大厅。"
            : "使用已有账号登录，进入教学、单人或联机模式。";
        ApplyTabState(LoginTabButton, loginTabImage, loginTabLabel,
            !registerMode, inputsEnabled);
        ApplyTabState(RegisterTabButton, registerTabImage, registerTabLabel,
            registerMode, inputsEnabled);
    }

    private static void ApplyTabState(UnityEngine.UI.Button button,
        UnityEngine.UI.Image image, TMP_Text label, bool active,
        bool inputsEnabled)
    {
        Color inactive = new(0.09f, 0.12f, 0.13f, 1f);
        Color selected = new(0.18f, 0.28f, 0.30f, 1f);
        UnityEngine.UI.ColorBlock colors = button.colors;
        colors.normalColor = inactive;
        colors.disabledColor = active ? selected : inactive;
        button.colors = colors;
        button.interactable = inputsEnabled && !active;
        image.color = active ? selected : inactive;
        label.color = active ? Cyan : TextSecondary;
    }

    private void SetInputActive(TMP_InputField input, bool active)
    {
        if (input == null) return;
        if (inputVisuals.TryGetValue(input, out InputVisual visual))
        {
            visual.Root.SetActive(active);
            input.gameObject.SetActive(active);
        }
        else
            input.gameObject.SetActive(active);
    }

    private void RefreshLobby(bool signedIn)
    {
        if (!signedIn) return;
        string account = string.IsNullOrWhiteSpace(Controller.Username)
            ? Controller.PlayerId
            : Controller.Username;
        lobbyAccountText.text = string.IsNullOrWhiteSpace(account)
            ? "当前账号：已登录"
            : $"当前账号：{account}";

        if (session == null || appearanceCatalog == null)
        {
            lobbyConnectionText.text = "联机服务未就绪";
            lobbyConnectionText.color = Error;
            lobbySummaryText.text = "无法读取房间服务，请返回模式选择后重试。";
            lobbyEntryPanel.SetActive(true);
            lobbyRoomPanel.SetActive(false);
            lobbyEntryActionsPanel.SetActive(true);
            lobbyRosterPanel.SetActive(false);
            SetLobbyControlsInteractable(false);
            lobbyMembersText.text = "玩家席位 1  等待服务\n\n玩家席位 2  等待服务";
            lobbyAppearanceText.text = "角色服务未就绪";
            lobbyStatusText.text = "联机服务初始化失败。";
            lobbyStatusText.color = Error;
            return;
        }

        bool connected = session.IsConnected;
        bool busy = session.IsLobbyBusy ||
                    session.IsPublicRoomQueryInProgress ||
                    session.State == CoopSessionState.Initializing ||
                    session.State == CoopSessionState.Hosting ||
                    session.State == CoopSessionState.Joining ||
                    session.State == CoopSessionState.Leaving ||
                    session.IsLobbyStarting;

        lobbyConnectionText.text = connected ? "已连接至房间" :
            busy ? "正在建立连接" : "等待加入房间";
        lobbyConnectionText.color = busy ? new Color(1f, 0.77f, 0.3f) : Cyan;
        lobbyEntryPanel.SetActive(!connected);
        lobbyRoomPanel.SetActive(connected);
        lobbyEntryActionsPanel.SetActive(!connected);
        lobbyRosterPanel.SetActive(connected);
        JoinCodeInput.gameObject.SetActive(!connected);
        CreateRoomButton.gameObject.SetActive(!connected);
        JoinRoomButton.gameObject.SetActive(!connected);
        RefreshRoomsButton.gameObject.SetActive(!connected);
        JoinSelectedRoomButton.gameObject.SetActive(!connected);
        ReadyButton.gameObject.SetActive(connected);
        LeaveRoomButton.gameObject.SetActive(connected);
        CreateRoomButton.interactable = !busy;
        RefreshRoomsButton.interactable = !busy;
        JoinSelectedRoomButton.interactable = !busy &&
                                              !string.IsNullOrWhiteSpace(
                                                  selectedPublicRoomId);
        JoinRoomButton.interactable = !busy &&
                                      !string.IsNullOrWhiteSpace(
                                          JoinCodeInput.text);
        PreviousAppearanceButton.interactable = !busy;
        NextAppearanceButton.interactable = !busy;
        ReadyButton.interactable = connected && !busy;
        LeaveRoomButton.interactable = connected && !busy;
        StartRoomButton.gameObject.SetActive(connected && session.IsHost);
        StartRoomButton.interactable = connected && !busy &&
                                       session.CanHostStart;

        PlayerAppearanceDefinition selected = appearanceCatalog.Resolve(
            session.PendingAppearanceId, out _);
        lobbyAppearanceText.text = selected != null
            ? selected.DisplayName
            : "尚未选择角色";
        lobbySummaryText.text = connected
            ? $"作战地图  {session.LobbyMapId}     " +
              $"小队人数  {session.PlayerCount} / " +
              $"{CoopSessionController.MaximumPlayers}     " +
              $"房间权限  公开大厅"
            : "浏览公开小队或创建房间，然后选择角色并完成战前准备。";
        lobbyJoinCodeText.text = string.IsNullOrWhiteSpace(session.JoinCode)
            ? "------"
            : session.JoinCode.ToUpperInvariant();

        if (session.LobbyMembers.Count == 0)
        {
            lobbyMembersText.text = connected
                ? "玩家席位 1  正在同步……\n\n玩家席位 2  等待队友加入"
                : "玩家席位 1  等待创建房间\n\n玩家席位 2  等待队友加入";
        }
        else
        {
            var lines = new List<string>();
            for (int index = 0;
                 index < CoopSessionController.MaximumPlayers; index++)
            {
                if (index >= session.LobbyMembers.Count)
                {
                    lines.Add($"玩家席位 {index + 1}    等待队友加入");
                    continue;
                }

                CoopLobbyMemberSnapshot member = session.LobbyMembers[index];
                PlayerAppearanceDefinition definition =
                    appearanceCatalog.Resolve(member.AppearanceId, out _);
                string identity = member.AccountPlayerId.Length > 16
                    ? member.AccountPlayerId.Substring(0, 16) + "…"
                    : member.AccountPlayerId;
                string role = member.IsHost ? "房主" : "队员";
                string ready = member.IsReady ? "已准备" : "未准备";
                lines.Add($"玩家席位 {index + 1}    {identity}\n" +
                          $"    {role} · " +
                          $"{definition?.DisplayName ?? member.AppearanceId} · " +
                          $"{ConnectionLabel(member.ConnectionState)} · {ready}");
            }
            lobbyMembersText.text = string.Join("\n\n", lines);
        }

        TMP_Text readyLabel = ReadyButton.GetComponentInChildren<TMP_Text>();
        if (readyLabel != null)
            readyLabel.text = session.IsLocalReady
                ? "取消准备"
                : "确认角色并准备";
        string failure = !string.IsNullOrWhiteSpace(
            session.LobbyFailureMessage)
            ? session.LobbyFailureMessage
            : !string.IsNullOrWhiteSpace(session.LastFailure)
                ? session.LastFailure
                : session.PublicRoomBrowserFailure;
        lobbyStatusText.text = !string.IsNullOrWhiteSpace(failure)
            ? failure
            : session.IsLobbyStarting
                ? "房主已开始战局，正在由服务器统一加载战区……"
                : connected
                    ? session.CanHostStart
                        ? session.PlayerCount == 1
                            ? "你已准备，可以开始单人联机测试。"
                            : "全员均已准备，房主现在可以开始战局。"
                        : "选择角色并准备；已加入玩家全部准备后由房主开始。"
                    : "选择公开房间、创建小队，或使用好友邀请代码。";
        lobbyStatusText.color = string.IsNullOrWhiteSpace(failure)
            ? Cyan
            : Error;
    }

    private void SetLobbyControlsInteractable(bool value)
    {
        CreateRoomButton.interactable = value;
        JoinRoomButton.interactable = value;
        RefreshRoomsButton.interactable = value;
        JoinSelectedRoomButton.interactable = value;
        PreviousAppearanceButton.interactable = value;
        NextAppearanceButton.interactable = value;
        ReadyButton.interactable = value;
        LeaveRoomButton.interactable = value;
        StartRoomButton.interactable = value;
    }

    private static string ConnectionLabel(CoopLobbyConnectionState state)
    {
        return state switch
        {
            CoopLobbyConnectionState.Reconnecting => "重连中",
            CoopLobbyConnectionState.Disconnected => "已断开",
            _ => "已连接"
        };
    }

    private void OnDestroy()
    {
        if (Controller != null) Controller.Changed -= Refresh;
        if (session != null)
        {
            session.LobbyChanged -= Refresh;
            session.StateChanged -= HandleSessionStateChanged;
            session.PublicRoomsChanged -= HandlePublicRoomsChanged;
        }
        ClearSensitiveInputs();
        if (ownsFont && font != null) Destroy(font);
    }

    private sealed class InputVisual
    {
        public InputVisual(GameObject root, TMP_Text label,
            UnityEngine.UI.Image background, UnityEngine.UI.Outline outline,
            UnityEngine.UI.Image focusLine, TMP_Text visibilityLabel)
        {
            Root = root;
            Label = label;
            Background = background;
            Outline = outline;
            FocusLine = focusLine;
            VisibilityLabel = visibilityLabel;
        }

        public GameObject Root { get; }
        public TMP_Text Label { get; }
        public UnityEngine.UI.Image Background { get; }
        public UnityEngine.UI.Outline Outline { get; }
        public UnityEngine.UI.Image FocusLine { get; }
        public TMP_Text VisibilityLabel { get; }
    }

    private enum ButtonTone
    {
        Primary,
        Secondary,
        Ghost,
        Danger,
        Tab,
        Inline
    }
}
