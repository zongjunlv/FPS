using FPS.Core.GameModes;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

[DisallowMultipleComponent]
public sealed class ModeDestinationView : MonoBehaviour
{
    private GameModeFlowController flow;
    private TMP_FontAsset font;
    private bool ownsFont;
    private TMP_Text statusText;

    public GameModeId Mode { get; private set; }
    public GameModeStage Stage { get; private set; }
    public UnityEngine.UI.Button ReturnButton { get; private set; }
    public string VisibleStatus => statusText != null
        ? statusText.text
        : string.Empty;

    public static ModeDestinationView Create(
        GameModeFlowController configuredFlow,
        GameModeId mode,
        GameModeStage stage)
    {
        GameObject canvasObject = ModeUiFactory.CreateCanvas(
            "Mode Destination Canvas",
            200);
        ModeDestinationView view =
            canvasObject.AddComponent<ModeDestinationView>();
        view.Build(configuredFlow, mode, stage);
        return view;
    }

    private void Build(
        GameModeFlowController configuredFlow,
        GameModeId mode,
        GameModeStage stage)
    {
        flow = configuredFlow;
        Mode = mode;
        Stage = stage;
        font = ModeUiFactory.CreateChineseFont(out ownsFont);
        RectTransform canvas = (RectTransform)transform;
        ModeUiFactory.CreateImage(
            "Background",
            canvas,
            new Color(0.018f, 0.035f, 0.05f, 1f),
            true);

        RectTransform panel = ModeUiFactory.CreateRect("Flow Panel", canvas);
        ModeUiFactory.SetRect(
            panel,
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(920f, 500f),
            Vector2.zero);
        UnityEngine.UI.Image panelImage =
            panel.gameObject.AddComponent<UnityEngine.UI.Image>();
        panelImage.color = new Color(0.035f, 0.075f, 0.095f, 0.98f);
        panelImage.raycastTarget = false;

        string titleValue = TitleFor(stage);
        TMP_Text eyebrow = ModeUiFactory.CreateText(
            "Flow Type",
            panel,
            "MODE FLOW READY",
            17f,
            TextAlignmentOptions.Center,
            font,
            new Color(0.28f, 0.86f, 0.82f, 1f));
        ModeUiFactory.SetRect(
            eyebrow.rectTransform,
            new Vector2(0.5f, 1f),
            new Vector2(0.5f, 1f),
            new Vector2(0.5f, 1f),
            new Vector2(760f, 34f),
            new Vector2(0f, -55f));

        TMP_Text title = ModeUiFactory.CreateText(
            "Flow Title",
            panel,
            titleValue,
            46f,
            TextAlignmentOptions.Center,
            font,
            Color.white);
        title.fontStyle = FontStyles.Bold;
        ModeUiFactory.SetRect(
            title.rectTransform,
            new Vector2(0.5f, 1f),
            new Vector2(0.5f, 1f),
            new Vector2(0.5f, 1f),
            new Vector2(800f, 68f),
            new Vector2(0f, -105f));

        TMP_Text description = ModeUiFactory.CreateText(
            "Flow Description",
            panel,
            DescriptionFor(stage),
            21f,
            TextAlignmentOptions.Center,
            font,
            new Color(0.68f, 0.76f, 0.79f, 1f));
        description.enableWordWrapping = true;
        ModeUiFactory.SetRect(
            description.rectTransform,
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(760f, 110f),
            new Vector2(0f, 22f));

        ReturnButton = CreateReturnButton(panel);
        statusText = ModeUiFactory.CreateText(
            "Status",
            panel,
            "流程入口已就绪",
            16f,
            TextAlignmentOptions.Center,
            font,
            new Color(0.48f, 0.9f, 0.84f, 1f));
        ModeUiFactory.SetRect(
            statusText.rectTransform,
            new Vector2(0.5f, 0f),
            new Vector2(0.5f, 0f),
            new Vector2(0.5f, 0f),
            new Vector2(760f, 36f),
            new Vector2(0f, 34f));

        ModeUiFactory.EnsureEventSystem();
        EventSystem.current.SetSelectedGameObject(ReturnButton.gameObject);
        flow.StateChanged += Refresh;
        Refresh();
    }

    private UnityEngine.UI.Button CreateReturnButton(RectTransform parent)
    {
        GameObject buttonObject = new GameObject(
            "返回模式选择",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(UnityEngine.UI.Image),
            typeof(UnityEngine.UI.Button));
        buttonObject.transform.SetParent(parent, false);
        RectTransform rect = (RectTransform)buttonObject.transform;
        ModeUiFactory.SetRect(
            rect,
            new Vector2(0.5f, 0f),
            new Vector2(0.5f, 0f),
            new Vector2(0.5f, 0f),
            new Vector2(360f, 62f),
            new Vector2(0f, 94f));
        UnityEngine.UI.Image image =
            buttonObject.GetComponent<UnityEngine.UI.Image>();
        image.color = new Color(0.06f, 0.25f, 0.25f, 1f);
        image.raycastTarget = true;
        UnityEngine.UI.Button button =
            buttonObject.GetComponent<UnityEngine.UI.Button>();
        button.onClick.AddListener(() => flow.TryReturnToEntry());
        TMP_Text label = ModeUiFactory.CreateText(
            "Label",
            buttonObject.transform,
            "返回模式选择",
            21f,
            TextAlignmentOptions.Center,
            font,
            Color.white);
        ModeUiFactory.Stretch(label.rectTransform);
        return button;
    }

    private void Refresh()
    {
        if (flow == null || statusText == null || ReturnButton == null)
        {
            return;
        }

        ReturnButton.interactable = !flow.IsLoading;
        statusText.text = !string.IsNullOrWhiteSpace(flow.FailureMessage)
            ? flow.FailureMessage
            : flow.IsLoading
                ? $"{flow.StatusMessage}  {flow.LoadingProgress:P0}"
                : "流程入口已就绪";
        statusText.color = string.IsNullOrWhiteSpace(flow.FailureMessage)
            ? new Color(0.48f, 0.9f, 0.84f, 1f)
            : new Color(1f, 0.48f, 0.4f, 1f);
    }

    private static string TitleFor(GameModeStage stage)
    {
        return stage switch
        {
            GameModeStage.Tutorial => "新手教学",
            GameModeStage.BattlePreparation => "战斗准备",
            GameModeStage.CoopLogin => "多人合作登录",
            _ => "模式流程"
        };
    }

    private static string DescriptionFor(GameModeStage stage)
    {
        return stage switch
        {
            GameModeStage.Tutorial =>
                "已进入独立教学流程。后续移动、射击与伤害训练将在此场景逐步接入。",
            GameModeStage.BattlePreparation =>
                "正式战斗开始前的准备流程。后续将在此完成角色选择与战局确认。",
            GameModeStage.CoopLogin =>
                "多人合作的账号入口。当前不会自动建立网络连接，登录后再进入合作大厅。",
            _ => "当前模式流程已加载。"
        };
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
