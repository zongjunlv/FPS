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

    public CoopAccountController Controller { get; private set; }
    public TMP_InputField UsernameInput { get; private set; }
    public TMP_InputField PasswordInput { get; private set; }
    public TMP_InputField ConfirmationInput { get; private set; }
    public UnityEngine.UI.Button LoginButton { get; private set; }
    public UnityEngine.UI.Button RegisterButton { get; private set; }
    public UnityEngine.UI.Button LogoutButton { get; private set; }
    public UnityEngine.UI.Button DevelopmentAnonymousButton { get; private set; }
    public UnityEngine.UI.Button ReturnButton { get; private set; }
    public string VisibleStatus => statusText != null
        ? statusText.text
        : string.Empty;

    public static CoopAccountView Create(GameModeFlowController configuredFlow,
        ICoopAuthenticationGateway gateway, bool allowDevelopmentAnonymous)
    {
        GameObject canvasObject = ModeUiFactory.CreateCanvas(
            "Coop Account Canvas", 210);
        CoopAccountView view = canvasObject.AddComponent<CoopAccountView>();
        view.Build(configuredFlow, gateway, allowDevelopmentAnonymous);
        return view;
    }

    private void Build(GameModeFlowController configuredFlow,
        ICoopAuthenticationGateway gateway, bool allowDevelopmentAnonymous)
    {
        flow = configuredFlow;
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
            new Vector2(0f, -232f), new Color(0.06f, 0.22f, 0.23f),
            ReturnToModes);

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
        UnityEngine.Events.UnityAction action)
    {
        GameObject buttonObject = new(objectName, typeof(RectTransform),
            typeof(CanvasRenderer), typeof(UnityEngine.UI.Image),
            typeof(UnityEngine.UI.Button));
        buttonObject.transform.SetParent(parent, false);
        ModeUiFactory.SetRect((RectTransform)buttonObject.transform,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f), new Vector2(350f, 60f), position);
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
    }

    private void OnDestroy()
    {
        if (Controller != null) Controller.Changed -= Refresh;
        ClearSensitiveInputs();
        if (ownsFont && font != null) Destroy(font);
    }
}
