using System;
using System.Collections.Generic;
using System.Linq;
using FPS.Core.GameModes;
using FPS.Networking.Session;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

[DisallowMultipleComponent]
public sealed class CoopAccountView : MonoBehaviour
{
    private GameModeFlowController flow;
    private TMP_FontAsset font;
    private bool ownsFont;
    private TMP_Text statusText;
    private CoopSessionController session;
    private PlayerAppearanceCatalog appearanceCatalog;
    private GameObject lobbyPanel;
    private TMP_Text lobbySummaryText;
    private TMP_Text lobbyMembersText;
    private TMP_Text lobbyStatusText;
    private TMP_Text lobbyAppearanceText;
    private int lobbyAppearanceIndex;

    public CoopAccountController Controller { get; private set; }
    public TMP_InputField UsernameInput { get; private set; }
    public TMP_InputField PasswordInput { get; private set; }
    public TMP_InputField ConfirmationInput { get; private set; }
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
            configuredSession, configuredAppearances);
        return view;
    }

    private void Build(GameModeFlowController configuredFlow,
        ICoopAuthenticationGateway gateway, bool allowDevelopmentAnonymous,
        CoopSessionController configuredSession,
        PlayerAppearanceCatalog configuredAppearances)
    {
        flow = configuredFlow;
        session = configuredSession;
        appearanceCatalog = configuredAppearances;
        Controller = new CoopAccountController(gateway,
            allowDevelopmentAnonymous);
        Controller.Changed += Refresh;
        font = ModeUiFactory.CreateChineseFont(out ownsFont);
        RectTransform canvas = (RectTransform)transform;
        ModeUiFactory.CreateImage("Background", canvas,
            new Color(0.012f, 0.027f, 0.043f, 1f), true);

        RectTransform panel = ModeUiFactory.CreateRect("Account Panel", canvas);
        ModeUiFactory.SetRect(panel, new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(900f, 880f), Vector2.zero);
        UnityEngine.UI.Image panelImage =
            panel.gameObject.AddComponent<UnityEngine.UI.Image>();
        panelImage.color = new Color(0.025f, 0.065f, 0.085f, 0.98f);
        panelImage.raycastTarget = false;

        TMP_Text eyebrow = ModeUiFactory.CreateText("Eyebrow", panel,
            "COOPERATIVE ACCOUNT", 17f, TextAlignmentOptions.Center, font,
            new Color(0.3f, 0.9f, 0.84f));
        ModeUiFactory.SetRect(eyebrow.rectTransform, new Vector2(0.5f, 1f),
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(760f, 34f), new Vector2(0f, -38f));

        TMP_Text title = ModeUiFactory.CreateText("Title", panel,
            "多人合作账号", 44f, TextAlignmentOptions.Center, font, Color.white);
        title.fontStyle = FontStyles.Bold;
        ModeUiFactory.SetRect(title.rectTransform, new Vector2(0.5f, 1f),
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(760f, 64f), new Vector2(0f, -82f));

        TMP_Text description = ModeUiFactory.CreateText("Description", panel,
            "登录后才能创建或加入合作战局。账号密码由 Unity Authentication 安全处理。",
            18f, TextAlignmentOptions.Center, font,
            new Color(0.65f, 0.75f, 0.79f));
        description.enableWordWrapping = true;
        ModeUiFactory.SetRect(description.rectTransform,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0.5f, 1f), new Vector2(760f, 60f),
            new Vector2(0f, -145f));

        UsernameInput = CreateInput(panel, "Username", "用户名（3—20 位）",
            new Vector2(0f, 190f), TMP_InputField.ContentType.Standard, 20);
        PasswordInput = CreateInput(panel, "Password", "密码",
            new Vector2(0f, 105f), TMP_InputField.ContentType.Password, 30);
        ConfirmationInput = CreateInput(panel, "Password Confirmation",
            "再次输入密码（注册时使用）", new Vector2(0f, 20f),
            TMP_InputField.ContentType.Password, 30);

        LoginButton = CreateButton(panel, "Login", "登录",
            new Vector2(-195f, -72f), new Color(0.05f, 0.42f, 0.38f),
            Login);
        RegisterButton = CreateButton(panel, "Register", "注册新账号",
            new Vector2(195f, -72f), new Color(0.08f, 0.29f, 0.4f),
            Register);
        LogoutButton = CreateButton(panel, "Logout", "注销并清除会话",
            new Vector2(0f, -72f), new Color(0.36f, 0.12f, 0.12f),
            Logout);
        DevelopmentAnonymousButton = CreateButton(panel,
            "Development Anonymous", "开发调试：匿名身份",
            new Vector2(0f, -152f), new Color(0.23f, 0.18f, 0.08f),
            DevelopmentAnonymous);
        ReturnButton = CreateButton(panel, "Return", "返回模式选择",
            new Vector2(0f, -345f), new Color(0.06f, 0.22f, 0.23f),
            ReturnToModes);

        if (session != null && appearanceCatalog != null &&
            appearanceCatalog.TryValidate(out _))
        {
            ConfigureLobbyModel();
            BuildLobbyPanel(panel);
            session.LobbyChanged += Refresh;
            session.StateChanged += HandleSessionStateChanged;
        }

        RectTransform status = ModeUiFactory.CreateRect("Status Panel", panel);
        ModeUiFactory.SetRect(status, new Vector2(0.5f, 0f),
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(760f, 76f), new Vector2(0f, 28f));
        UnityEngine.UI.Image statusImage =
            status.gameObject.AddComponent<UnityEngine.UI.Image>();
        statusImage.color = new Color(0.025f, 0.12f, 0.14f, 0.9f);
        statusImage.raycastTarget = false;
        statusText = ModeUiFactory.CreateText("Status", status,
            Controller.StatusMessage, 17f, TextAlignmentOptions.Center, font,
            Color.white);
        statusText.enableWordWrapping = true;
        ModeUiFactory.Stretch(statusText.rectTransform, 16f, 6f);

        ModeUiFactory.EnsureEventSystem();
        EventSystem.current.SetSelectedGameObject(UsernameInput.gameObject);
        Refresh();
        RestoreSession();
    }

    private TMP_InputField CreateInput(Transform parent, string objectName,
        string placeholderValue, Vector2 position,
        TMP_InputField.ContentType contentType, int characterLimit)
    {
        GameObject inputObject = new(objectName, typeof(RectTransform),
            typeof(CanvasRenderer), typeof(UnityEngine.UI.Image),
            typeof(TMP_InputField));
        inputObject.transform.SetParent(parent, false);
        RectTransform rect = (RectTransform)inputObject.transform;
        ModeUiFactory.SetRect(rect, new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(660f, 64f), position);
        UnityEngine.UI.Image background =
            inputObject.GetComponent<UnityEngine.UI.Image>();
        background.color = new Color(0.015f, 0.035f, 0.05f, 1f);

        TMP_Text value = ModeUiFactory.CreateText("Value", inputObject.transform,
            string.Empty, 20f, TextAlignmentOptions.MidlineLeft, font, Color.white);
        ModeUiFactory.Stretch(value.rectTransform, 20f, 8f);
        TMP_Text placeholder = ModeUiFactory.CreateText("Placeholder",
            inputObject.transform, placeholderValue, 18f,
            TextAlignmentOptions.MidlineLeft, font,
            new Color(0.43f, 0.53f, 0.58f));
        ModeUiFactory.Stretch(placeholder.rectTransform, 20f, 8f);

        TMP_InputField input = inputObject.GetComponent<TMP_InputField>();
        input.textViewport = rect;
        input.textComponent = value;
        input.placeholder = placeholder;
        input.contentType = contentType;
        input.lineType = TMP_InputField.LineType.SingleLine;
        input.characterLimit = characterLimit;
        input.caretColor = new Color(0.3f, 0.9f, 0.84f);
        input.selectionColor = new Color(0.2f, 0.55f, 0.55f, 0.65f);
        return input;
    }

    private UnityEngine.UI.Button CreateButton(Transform parent,
        string objectName, string label, Vector2 position, Color color,
        UnityEngine.Events.UnityAction action, Vector2? size = null)
    {
        GameObject buttonObject = new(objectName, typeof(RectTransform),
            typeof(CanvasRenderer), typeof(UnityEngine.UI.Image),
            typeof(UnityEngine.UI.Button));
        buttonObject.transform.SetParent(parent, false);
        ModeUiFactory.SetRect((RectTransform)buttonObject.transform,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f), size ?? new Vector2(350f, 60f), position);
        buttonObject.GetComponent<UnityEngine.UI.Image>().color = color;
        UnityEngine.UI.Button button =
            buttonObject.GetComponent<UnityEngine.UI.Button>();
        button.onClick.AddListener(action);
        TMP_Text text = ModeUiFactory.CreateText("Label", buttonObject.transform,
            label, 19f, TextAlignmentOptions.Center, font, Color.white);
        ModeUiFactory.Stretch(text.rectTransform, 8f, 4f);
        return button;
    }

    private async void RestoreSession()
    {
        await Controller.RestoreAsync();
    }

    private async void Login()
    {
        string password = PasswordInput.text;
        await Controller.SignInAsync(UsernameInput.text, password);
        ClearSensitiveInputs();
    }

    private async void Register()
    {
        string password = PasswordInput.text;
        string confirmation = ConfirmationInput.text;
        await Controller.RegisterAsync(UsernameInput.text, password, confirmation);
        ClearSensitiveInputs();
    }

    private async void DevelopmentAnonymous()
    {
        await Controller.SignInAnonymouslyForDevelopmentAsync();
        ClearSensitiveInputs();
    }

    private void Logout()
    {
        Controller.SignOut();
        ClearSensitiveInputs();
        UsernameInput.text = string.Empty;
    }

    private void ReturnToModes()
    {
        if (!Controller.IsBusy)
            flow.TryReturnToEntry();
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

    private void BuildLobbyPanel(Transform parent)
    {
        RectTransform root = ModeUiFactory.CreateRect("Coop Lobby", parent);
        ModeUiFactory.SetRect(root, new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(800f, 610f), new Vector2(0f, -30f));
        lobbyPanel = root.gameObject;

        lobbySummaryText = ModeUiFactory.CreateText("Room Summary", root,
            string.Empty, 19f, TextAlignmentOptions.Center, font,
            new Color(0.46f, 0.92f, 0.86f));
        ModeUiFactory.SetRect(lobbySummaryText.rectTransform,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f), new Vector2(740f, 54f),
            new Vector2(0f, 245f));

        JoinCodeInput = CreateInput(root, "Room Join Code", "输入 6 位加入码",
            new Vector2(0f, 165f), TMP_InputField.ContentType.Alphanumeric, 12);
        JoinCodeInput.onValueChanged.AddListener(_ => Refresh());
        ModeUiFactory.SetRect((RectTransform)JoinCodeInput.transform,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f), new Vector2(500f, 58f),
            new Vector2(0f, 165f));
        CreateRoomButton = CreateButton(root, "Create Room", "创建双人房间",
            new Vector2(-190f, 92f), new Color(0.05f, 0.42f, 0.38f),
            CreateRoom, new Vector2(340f, 56f));
        JoinRoomButton = CreateButton(root, "Join Room", "通过加入码加入",
            new Vector2(190f, 92f), new Color(0.08f, 0.29f, 0.4f),
            JoinRoom, new Vector2(340f, 56f));

        PreviousAppearanceButton = CreateButton(root,
            "Previous Appearance", "◀ 上一个角色",
            new Vector2(-255f, 18f), new Color(0.05f, 0.22f, 0.24f),
            PreviousLobbyAppearance, new Vector2(200f, 50f));
        lobbyAppearanceText = ModeUiFactory.CreateText("Selected Appearance",
            root, string.Empty, 19f, TextAlignmentOptions.Center, font,
            Color.white);
        ModeUiFactory.SetRect(lobbyAppearanceText.rectTransform,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f), new Vector2(285f, 50f),
            new Vector2(0f, 18f));
        NextAppearanceButton = CreateButton(root,
            "Next Appearance", "下一个角色 ▶",
            new Vector2(255f, 18f), new Color(0.05f, 0.22f, 0.24f),
            NextLobbyAppearance, new Vector2(200f, 50f));

        ReadyButton = CreateButton(root, "Ready", "确认角色并准备",
            new Vector2(-245f, -58f), new Color(0.12f, 0.36f, 0.22f),
            ToggleReady, new Vector2(215f, 52f));
        LeaveRoomButton = CreateButton(root, "Leave Room", "离开房间",
            new Vector2(0f, -58f), new Color(0.34f, 0.15f, 0.13f),
            LeaveRoom, new Vector2(215f, 52f));
        StartRoomButton = CreateButton(root, "Start Match", "房主开始战局",
            new Vector2(245f, -58f), new Color(0.1f, 0.34f, 0.42f),
            StartRoom, new Vector2(215f, 52f));

        lobbyMembersText = ModeUiFactory.CreateText("Room Members", root,
            string.Empty, 17f, TextAlignmentOptions.TopLeft, font,
            new Color(0.82f, 0.87f, 0.89f));
        lobbyMembersText.enableWordWrapping = true;
        ModeUiFactory.SetRect(lobbyMembersText.rectTransform,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f), new Vector2(700f, 130f),
            new Vector2(0f, -155f));

        lobbyStatusText = ModeUiFactory.CreateText("Room Status", root,
            string.Empty, 16f, TextAlignmentOptions.Center, font,
            new Color(0.55f, 0.9f, 0.84f));
        lobbyStatusText.enableWordWrapping = true;
        ModeUiFactory.SetRect(lobbyStatusText.rectTransform,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f), new Vector2(700f, 70f),
            new Vector2(0f, -255f));
    }

    private async void CreateRoom()
    {
        if (session != null) await session.HostAsync();
        Refresh();
    }

    private async void JoinRoom()
    {
        if (session != null) await session.JoinAsync(JoinCodeInput?.text);
        Refresh();
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
        UsernameInput.gameObject.SetActive(!signedIn);
        PasswordInput.gameObject.SetActive(!signedIn);
        ConfirmationInput.gameObject.SetActive(!signedIn);
        LoginButton.gameObject.SetActive(!signedIn);
        RegisterButton.gameObject.SetActive(!signedIn);
        LoginButton.interactable = inputsEnabled;
        RegisterButton.interactable = inputsEnabled;
        LogoutButton.gameObject.SetActive(signedIn);
        if (session != null)
            LogoutButton.gameObject.SetActive(signedIn && !session.IsConnected);
        LogoutButton.interactable = signedIn && !Controller.IsBusy;
        DevelopmentAnonymousButton.gameObject.SetActive(!signedIn &&
            Controller.AllowDevelopmentAnonymous);
        DevelopmentAnonymousButton.interactable = inputsEnabled;
        ReturnButton.interactable = !Controller.IsBusy &&
                                    flow != null && !flow.IsLoading;
        statusText.text = Controller.StatusMessage;
        statusText.color = string.IsNullOrWhiteSpace(Controller.LastFailure)
            ? new Color(0.55f, 0.9f, 0.84f)
            : new Color(1f, 0.48f, 0.4f);
        RefreshLobby(signedIn);
    }

    private void RefreshLobby(bool signedIn)
    {
        if (lobbyPanel == null || session == null || appearanceCatalog == null)
            return;
        lobbyPanel.SetActive(signedIn);
        if (!signedIn) return;

        bool connected = session.IsConnected;
        bool busy = session.IsLobbyBusy ||
                    session.State == CoopSessionState.Initializing ||
                    session.State == CoopSessionState.Hosting ||
                    session.State == CoopSessionState.Joining ||
                    session.State == CoopSessionState.Leaving;
        busy |= session.IsLobbyStarting;
        JoinCodeInput.gameObject.SetActive(!connected);
        CreateRoomButton.gameObject.SetActive(!connected);
        JoinRoomButton.gameObject.SetActive(!connected);
        CreateRoomButton.interactable = !busy;
        JoinRoomButton.interactable = !busy &&
                                      !string.IsNullOrWhiteSpace(
                                          JoinCodeInput.text);
        ReadyButton.gameObject.SetActive(connected);
        LeaveRoomButton.gameObject.SetActive(connected);
        StartRoomButton.gameObject.SetActive(connected && session.IsHost);
        ReadyButton.interactable = connected && !busy;
        LeaveRoomButton.interactable = connected && !busy;
        PreviousAppearanceButton.interactable = !busy;
        NextAppearanceButton.interactable = !busy;
        StartRoomButton.interactable = connected && !busy &&
                                       session.CanHostStart;

        PlayerAppearanceDefinition selected = appearanceCatalog.Resolve(
            session.PendingAppearanceId, out _);
        lobbyAppearanceText.text = selected != null
            ? $"当前角色：{selected.DisplayName}"
            : "当前角色：未选择";
        lobbySummaryText.text = connected
            ? $"地图：{session.LobbyMapId}    加入码：{session.JoinCode}    " +
              $"玩家：{session.PlayerCount} / {CoopSessionController.MaximumPlayers}"
            : "创建房间，或输入好友提供的加入码";

        if (session.LobbyMembers.Count == 0)
        {
            lobbyMembersText.text = connected ? "正在同步房间成员……" : string.Empty;
        }
        else
        {
            var lines = new List<string>();
            for (int index = 0; index < session.LobbyMembers.Count; index++)
            {
                CoopLobbyMemberSnapshot member = session.LobbyMembers[index];
                PlayerAppearanceDefinition definition = appearanceCatalog.Resolve(
                    member.AppearanceId, out _);
                string identity = member.AccountPlayerId.Length > 12
                    ? member.AccountPlayerId.Substring(0, 12) + "…"
                    : member.AccountPlayerId;
                lines.Add($"{(member.IsHost ? "房主" : "成员")}  {identity}  " +
                          $"角色：{definition?.DisplayName ?? member.AppearanceId}  " +
                          $"连接：{ConnectionLabel(member.ConnectionState)}  " +
                          $"{(member.IsReady ? "已准备" : "未准备")}");
            }
            lobbyMembersText.text = string.Join("\n", lines);
        }

        TMP_Text readyLabel = ReadyButton.GetComponentInChildren<TMP_Text>();
        if (readyLabel != null)
            readyLabel.text = session.IsLocalReady ? "取消准备" : "确认角色并准备";
        string failure = !string.IsNullOrWhiteSpace(session.LobbyFailureMessage)
            ? session.LobbyFailureMessage
            : session.LastFailure;
        lobbyStatusText.text = !string.IsNullOrWhiteSpace(failure)
            ? failure
            : session.IsLobbyStarting
                ? "准备完成，等待服务器统一加载 CityNew……"
                : connected
                    ? session.CanHostStart
                        ? "双方已准备，房主可以开始战局。"
                        : "选择角色并准备；双方准备后由房主开始。"
                    : "首版房间限制为两名玩家。";
        lobbyStatusText.color = string.IsNullOrWhiteSpace(failure)
            ? new Color(0.55f, 0.9f, 0.84f)
            : new Color(1f, 0.48f, 0.4f);
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
        }
        ClearSensitiveInputs();
        if (ownsFont && font != null) Destroy(font);
    }
}
