using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class CoopBattlePauseView : MonoBehaviour
{
    private static readonly Color PanelColor =
        new(0.018f, 0.035f, 0.05f, 0.98f);
    private static readonly Color AccentColor =
        new(0.16f, 0.92f, 0.82f, 1f);

    private CoopBattlePauseController controller;
    private TMP_FontAsset font;
    private bool ownsFont;
    private CanvasGroup canvasGroup;
    private TMP_Text status;

    public Button ContinueButton { get; private set; }
    public Button ReturnButton { get; private set; }
    public bool IsVisible => gameObject.activeSelf;

    public static CoopBattlePauseView Create(
        Transform owner,
        CoopBattlePauseController configuredController)
    {
        GameObject canvasObject = new(
            "Coop Battle Pause Canvas",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster),
            typeof(CanvasGroup),
            typeof(CoopBattlePauseView));
        canvasObject.transform.SetParent(owner, false);
        CoopBattlePauseView view =
            canvasObject.GetComponent<CoopBattlePauseView>();
        view.Build(configuredController);
        view.SetVisible(false);
        return view;
    }

    public void SetVisible(bool visible)
    {
        if (gameObject.activeSelf != visible) gameObject.SetActive(visible);
        if (!visible) return;
        SetInteractionEnabled(true);
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        ModeUiFactory.EnsureEventSystem();
        EventSystem.current?.SetSelectedGameObject(
            ContinueButton.gameObject);
    }

    public void SetInteractionEnabled(bool enabled)
    {
        if (canvasGroup != null)
        {
            canvasGroup.interactable = enabled;
            canvasGroup.blocksRaycasts = enabled;
        }
        if (ContinueButton != null) ContinueButton.interactable = enabled;
        if (ReturnButton != null) ReturnButton.interactable = enabled;
    }

    public void SetStatus(string value)
    {
        if (status != null) status.text = value ?? string.Empty;
    }

    private void Build(CoopBattlePauseController configuredController)
    {
        controller = configuredController;
        Canvas canvas = GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 220;
        CanvasScaler scaler = GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode =
            CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        canvasGroup = GetComponent<CanvasGroup>();
        font = ModeUiFactory.CreateChineseFont(out ownsFont);

        RectTransform safeArea = ModeUiFactory.CreateRect(
            "Safe Area", transform);
        ModeUiFactory.Stretch(safeArea);
        safeArea.gameObject.AddComponent<SafeAreaFitter>();

        Image dim = ModeUiFactory.CreateImage(
            "Background Dim",
            safeArea,
            new Color(0.005f, 0.01f, 0.015f, 0.82f),
            true);
        ModeUiFactory.Stretch(dim.rectTransform);

        Image panelImage = ModeUiFactory.CreateImage(
            "Pause Panel", safeArea, PanelColor, true);
        RectTransform panel = panelImage.rectTransform;
        ModeUiFactory.SetRect(
            panel,
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(650f, 390f),
            Vector2.zero);

        TMP_Text eyebrow = CreateText(
            "Eyebrow", panel, "CO-OP SESSION", 17f,
            new Vector2(0f, 143f), new Vector2(550f, 28f), AccentColor);
        eyebrow.fontStyle = FontStyles.Bold;
        TMP_Text title = CreateText(
            "Title", panel, "联机战斗菜单", 40f,
            new Vector2(0f, 94f), new Vector2(550f, 56f), Color.white);
        title.fontStyle = FontStyles.Bold;
        CreateText(
            "Description", panel,
            "菜单只停止本机操作，服务器战局与队友仍会继续运行",
            18f, new Vector2(0f, 48f), new Vector2(560f, 34f),
            new Color(0.76f, 0.84f, 0.87f, 1f));

        ContinueButton = CreateButton(
            panel, "继续联机战斗", new Vector2(0f, -24f),
            new Color(0.04f, 0.42f, 0.37f, 1f),
            () => controller?.TryContinueBattle());
        ReturnButton = CreateButton(
            panel, "退出战局并返回模式大厅", new Vector2(0f, -98f),
            new Color(0.05f, 0.16f, 0.19f, 1f),
            () => controller?.TryReturnToModeEntry());
        status = CreateText(
            "Status", panel, string.Empty, 15f,
            new Vector2(0f, -145f), new Vector2(560f, 36f),
            new Color(1f, 0.55f, 0.42f, 1f));
        CreateText(
            "Hint", panel, "ESC  继续联机战斗", 15f,
            new Vector2(0f, -174f), new Vector2(520f, 24f),
            new Color(0.54f, 0.66f, 0.7f, 1f));
    }

    private TMP_Text CreateText(
        string objectName,
        Transform parent,
        string value,
        float size,
        Vector2 position,
        Vector2 dimensions,
        Color color)
    {
        TMP_Text text = ModeUiFactory.CreateText(
            objectName, parent, value, size,
            TextAlignmentOptions.Center, font, color);
        ModeUiFactory.SetRect(
            text.rectTransform,
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            dimensions,
            position);
        return text;
    }

    private Button CreateButton(
        Transform parent,
        string label,
        Vector2 position,
        Color color,
        UnityEngine.Events.UnityAction action)
    {
        GameObject buttonObject = new(
            label,
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image),
            typeof(Button));
        buttonObject.transform.SetParent(parent, false);
        RectTransform rect = (RectTransform)buttonObject.transform;
        ModeUiFactory.SetRect(
            rect,
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(450f, 58f),
            position);
        Image image = buttonObject.GetComponent<Image>();
        image.color = color;
        Button button = buttonObject.GetComponent<Button>();
        button.onClick.AddListener(action);
        TMP_Text text = ModeUiFactory.CreateText(
            "Label", rect, label, 21f,
            TextAlignmentOptions.Center, font, Color.white);
        text.fontStyle = FontStyles.Bold;
        ModeUiFactory.Stretch(text.rectTransform);
        return button;
    }

    private void OnDestroy()
    {
        controller = null;
        if (ownsFont && font != null) Destroy(font);
    }
}
