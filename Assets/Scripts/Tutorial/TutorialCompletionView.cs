using FPS.Core.GameModes;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class TutorialCompletionView : MonoBehaviour
{
    private static readonly Color PanelColor =
        new(0.018f, 0.035f, 0.05f, 0.97f);
    private static readonly Color AccentColor =
        new(0.16f, 0.92f, 0.82f, 1f);

    private TutorialFlowController tutorial;
    private GameModeFlowController modeFlow;
    private TMP_FontAsset font;
    private bool ownsFont;
    private TMP_Text statusText;
    private CanvasGroup canvasGroup;

    public Canvas RootCanvas { get; private set; }
    public RectTransform Panel { get; private set; }
    public Button EnterBattleButton { get; private set; }
    public Button RestartButton { get; private set; }
    public Button ReturnButton { get; private set; }
    public bool IsVisible => gameObject.activeSelf;
    public bool IsBound => tutorial != null && modeFlow != null;
    public string VisibleStatus => statusText != null
        ? statusText.text
        : string.Empty;

    public static TutorialCompletionView Create(
        Transform owner,
        TutorialFlowController configuredTutorial)
    {
        GameObject canvasObject = new(
            "Tutorial Completion Canvas",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster),
            typeof(CanvasGroup),
            typeof(TutorialCompletionView));
        canvasObject.transform.SetParent(owner, false);
        TutorialCompletionView view =
            canvasObject.GetComponent<TutorialCompletionView>();
        view.Build(configuredTutorial);
        view.SetVisible(false);
        return view;
    }

    public void SetVisible(bool visible)
    {
        if (gameObject.activeSelf != visible)
        {
            gameObject.SetActive(visible);
        }

        if (!visible)
        {
            return;
        }

        SetInteractionEnabled(true);
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        ModeUiFactory.EnsureEventSystem();
        EventSystem.current?.SetSelectedGameObject(
            EnterBattleButton.gameObject);
        Refresh();
    }

    public void SetInteractionEnabled(bool enabled)
    {
        if (canvasGroup != null)
        {
            canvasGroup.interactable = enabled;
            canvasGroup.blocksRaycasts = enabled;
        }

        if (EnterBattleButton != null)
        {
            EnterBattleButton.interactable = enabled;
            RestartButton.interactable = enabled;
            ReturnButton.interactable = enabled;
        }
    }

    public void Unbind()
    {
        if (modeFlow != null)
        {
            modeFlow.StateChanged -= Refresh;
        }

        modeFlow = null;
    }

    private void Build(TutorialFlowController configuredTutorial)
    {
        tutorial = configuredTutorial;
        modeFlow = GameModeFlowController.Instance ??
                   GameModeFlowController.Ensure(
                       GameModeCatalog.LoadDefault());
        if (modeFlow != null)
        {
            modeFlow.StateChanged += Refresh;
        }

        RootCanvas = GetComponent<Canvas>();
        RootCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        RootCanvas.sortingOrder = 220;
        CanvasScaler scaler = GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode =
            CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        canvasGroup = GetComponent<CanvasGroup>();
        font = ModeUiFactory.CreateChineseFont(out ownsFont);

        RectTransform safeArea = ModeUiFactory.CreateRect(
            "Safe Area",
            transform);
        ModeUiFactory.Stretch(safeArea);
        safeArea.gameObject.AddComponent<SafeAreaFitter>();

        Image panelImage = ModeUiFactory.CreateImage(
            "Completion Panel",
            safeArea,
            PanelColor,
            true);
        Panel = panelImage.rectTransform;
        ModeUiFactory.SetRect(
            Panel,
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(720f, 430f),
            Vector2.zero);

        TMP_Text eyebrow = CreateText(
            "Eyebrow",
            "TRAINING COMPLETE",
            18f,
            new Vector2(0f, 162f),
            new Vector2(640f, 30f),
            AccentColor);
        eyebrow.fontStyle = FontStyles.Bold;
        TMP_Text title = CreateText(
            "Title",
            "新手训练完成",
            42f,
            new Vector2(0f, 112f),
            new Vector2(640f, 62f),
            Color.white);
        title.fontStyle = FontStyles.Bold;
        CreateText(
            "Description",
            "基础移动、射击、武器操作与部位伤害训练已完成",
            21f,
            new Vector2(0f, 59f),
            new Vector2(640f, 40f),
            new Color(0.78f, 0.84f, 0.87f, 1f));

        EnterBattleButton = CreateButton(
            "进入战斗模式",
            new Vector2(0f, -7f),
            new Color(0.04f, 0.42f, 0.37f, 1f),
            () => tutorial?.TryEnterBattleMode());
        RestartButton = CreateButton(
            "重新开始教学",
            new Vector2(0f, -80f),
            new Color(0.05f, 0.25f, 0.25f, 1f),
            () => tutorial?.TryRestartTutorial());
        ReturnButton = CreateButton(
            "返回模式入口",
            new Vector2(0f, -153f),
            new Color(0.05f, 0.16f, 0.19f, 1f),
            () => tutorial?.TryReturnToModeEntry());

        statusText = CreateText(
            "Status",
            "选择下一步行动",
            16f,
            new Vector2(0f, -199f),
            new Vector2(640f, 26f),
            new Color(0.58f, 0.72f, 0.75f, 1f));
    }

    private TMP_Text CreateText(
        string objectName,
        string value,
        float size,
        Vector2 position,
        Vector2 dimensions,
        Color color)
    {
        TMP_Text text = ModeUiFactory.CreateText(
            objectName,
            Panel,
            value,
            size,
            TextAlignmentOptions.Center,
            font,
            color);
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
        buttonObject.transform.SetParent(Panel, false);
        RectTransform rect = (RectTransform)buttonObject.transform;
        ModeUiFactory.SetRect(
            rect,
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(430f, 58f),
            position);
        Image image = buttonObject.GetComponent<Image>();
        image.color = color;
        Button button = buttonObject.GetComponent<Button>();
        button.onClick.AddListener(action);
        TMP_Text text = ModeUiFactory.CreateText(
            "Label",
            rect,
            label,
            21f,
            TextAlignmentOptions.Center,
            font,
            Color.white);
        text.fontStyle = FontStyles.Bold;
        ModeUiFactory.Stretch(text.rectTransform);
        return button;
    }

    private void Refresh()
    {
        if (modeFlow == null || statusText == null)
        {
            return;
        }

        bool available = !modeFlow.IsLoading &&
                         tutorial != null &&
                         !tutorial.IsLeavingTutorial;
        SetInteractionEnabled(available);
        statusText.text = !string.IsNullOrWhiteSpace(
                modeFlow.FailureMessage)
            ? modeFlow.FailureMessage
            : modeFlow.IsLoading
                ? $"{modeFlow.StatusMessage}  {modeFlow.LoadingProgress:P0}"
                : "选择下一步行动";
    }

    private void OnDestroy()
    {
        Unbind();
        tutorial = null;
        if (ownsFont && font != null)
        {
            Destroy(font);
        }
    }
}
