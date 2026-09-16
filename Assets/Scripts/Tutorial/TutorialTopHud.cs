using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class TutorialTopHud : MonoBehaviour
{
    private static readonly Color PanelColor =
        new(0.018f, 0.035f, 0.05f, 0.92f);
    private static readonly Color AccentColor =
        new(0.16f, 0.92f, 0.82f, 1f);
    private static readonly Color WarningColor =
        new(1f, 0.72f, 0.24f, 1f);

    private TutorialProgressionStateMachine progression;
    private TMP_FontAsset font;
    private bool ownsFont;
    private Coroutine feedbackRoutine;

    public Canvas RootCanvas { get; private set; }
    public RectTransform SafeArea { get; private set; }
    public RectTransform Panel { get; private set; }
    public TMP_Text ChapterText { get; private set; }
    public TMP_Text StepText { get; private set; }
    public TMP_Text InstructionText { get; private set; }
    public TMP_Text ProgressText { get; private set; }
    public TMP_Text FeedbackText { get; private set; }
    public bool IsBound => progression != null;

    public static TutorialTopHud Create(Transform owner)
    {
        GameObject canvasObject = new(
            "Tutorial Guide Canvas",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(CanvasGroup),
            typeof(TutorialTopHud));
        canvasObject.transform.SetParent(owner, false);
        TutorialTopHud hud = canvasObject.GetComponent<TutorialTopHud>();
        hud.Build();
        return hud;
    }

    public void Bind(TutorialProgressionStateMachine target)
    {
        Unbind();
        progression = target;
        if (progression == null)
        {
            return;
        }

        progression.ProgressChanged += Refresh;
        progression.StepCompleted += HandleStepCompleted;
        progression.SequenceCompleted += HandleSequenceCompleted;
        Refresh(progression.Snapshot);
    }

    public void ShowActivityHint(string message)
    {
        if (feedbackRoutine != null || string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        FeedbackText.color = WarningColor;
        FeedbackText.text = message;
        FeedbackText.gameObject.SetActive(true);
    }

    public void ClearActivityHint()
    {
        if (feedbackRoutine == null && FeedbackText != null)
        {
            FeedbackText.gameObject.SetActive(false);
        }
    }

    private void Build()
    {
        RootCanvas = GetComponent<Canvas>();
        RootCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        RootCanvas.sortingOrder = 175;

        CanvasScaler scaler = GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode =
            CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        CanvasGroup group = GetComponent<CanvasGroup>();
        group.interactable = false;
        group.blocksRaycasts = false;
        group.ignoreParentGroups = true;

        font = ModeUiFactory.CreateChineseFont(out ownsFont);
        SafeArea = ModeUiFactory.CreateRect("Safe Area", transform);
        ModeUiFactory.Stretch(SafeArea);
        SafeArea.gameObject.AddComponent<SafeAreaFitter>();

        Image panelImage = ModeUiFactory.CreateImage(
            "Top Guide Panel",
            SafeArea,
            PanelColor,
            false);
        Panel = panelImage.rectTransform;
        ModeUiFactory.SetRect(
            Panel,
            new Vector2(0.5f, 1f),
            new Vector2(0.5f, 1f),
            new Vector2(0.5f, 1f),
            new Vector2(820f, 126f),
            new Vector2(0f, -18f));

        Image accent = ModeUiFactory.CreateImage(
            "Accent",
            Panel,
            AccentColor,
            false);
        ModeUiFactory.SetRect(
            accent.rectTransform,
            new Vector2(0f, 0f),
            new Vector2(0f, 1f),
            new Vector2(0f, 0.5f),
            new Vector2(5f, 0f),
            Vector2.zero);

        ChapterText = CreateText(
            "Chapter",
            Panel,
            20f,
            FontStyles.Bold,
            TextAlignmentOptions.Left,
            new Vector2(26f, -12f),
            new Vector2(520f, 28f),
            AccentColor);
        ProgressText = CreateText(
            "Progress",
            Panel,
            18f,
            FontStyles.Normal,
            TextAlignmentOptions.Right,
            new Vector2(-24f, -13f),
            new Vector2(240f, 26f),
            new Color(0.76f, 0.84f, 0.88f, 1f),
            true);
        StepText = CreateText(
            "Step Title",
            Panel,
            28f,
            FontStyles.Bold,
            TextAlignmentOptions.Left,
            new Vector2(26f, -43f),
            new Vector2(768f, 34f),
            Color.white);
        InstructionText = CreateText(
            "Instruction",
            Panel,
            20f,
            FontStyles.Normal,
            TextAlignmentOptions.Left,
            new Vector2(26f, -82f),
            new Vector2(768f, 28f),
            new Color(0.86f, 0.9f, 0.92f, 1f));
        FeedbackText = CreateText(
            "Completion Feedback",
            Panel,
            20f,
            FontStyles.Bold,
            TextAlignmentOptions.Center,
            new Vector2(0f, -8f),
            new Vector2(760f, 28f),
            AccentColor,
            false,
            true);
        FeedbackText.gameObject.SetActive(false);
    }

    private TMP_Text CreateText(
        string name,
        Transform parent,
        float size,
        FontStyles style,
        TextAlignmentOptions alignment,
        Vector2 position,
        Vector2 dimensions,
        Color color,
        bool anchorRight = false,
        bool anchorBelow = false)
    {
        TMP_Text text = ModeUiFactory.CreateText(
            name,
            parent,
            string.Empty,
            size,
            alignment,
            font,
            color);
        text.fontStyle = style;
        Vector2 anchor = anchorBelow
            ? new Vector2(0.5f, 0f)
            : anchorRight
                ? new Vector2(1f, 1f)
                : new Vector2(0f, 1f);
        Vector2 pivot = anchorBelow
            ? new Vector2(0.5f, 1f)
            : anchorRight
                ? new Vector2(1f, 1f)
                : new Vector2(0f, 1f);
        ModeUiFactory.SetRect(
            text.rectTransform,
            anchor,
            anchor,
            pivot,
            dimensions,
            position);
        return text;
    }

    private void Refresh(TutorialProgressSnapshot snapshot)
    {
        if (snapshot.IsComplete)
        {
            ChapterText.text = "新手训练";
            StepText.text = "全部教学步骤已完成";
            InstructionText.text = "你已经掌握本关的基础操作。";
            ProgressText.text = $"步骤 {snapshot.TotalSteps}/{snapshot.TotalSteps}";
            return;
        }

        TutorialStepDefinition step = snapshot.CurrentStep;
        ChapterText.text = step.Chapter;
        StepText.text = step.Title;
        InstructionText.text = step.Instruction;
        ProgressText.text =
            $"步骤 {snapshot.CurrentStepIndex + 1}/{snapshot.TotalSteps}  " +
            $"{FormatProgress(snapshot.CurrentValue)}/{step.TargetValue}";
    }

    private void HandleStepCompleted(TutorialProgressSnapshot snapshot)
    {
        string message = snapshot.CurrentStep?.CompletionFeedback ??
                         $"已完成：{snapshot.LastCompletedTitle}";
        ShowFeedback(message);
    }

    private void HandleSequenceCompleted(TutorialProgressSnapshot snapshot)
    {
        Refresh(snapshot);
        ShowFeedback("新手训练全部完成");
    }

    private void ShowFeedback(string message)
    {
        if (feedbackRoutine != null)
        {
            StopCoroutine(feedbackRoutine);
        }

        FeedbackText.color = AccentColor;
        feedbackRoutine = StartCoroutine(ShowFeedbackRoutine(message));
    }

    private IEnumerator ShowFeedbackRoutine(string message)
    {
        FeedbackText.text = message;
        FeedbackText.gameObject.SetActive(true);
        yield return new WaitForSecondsRealtime(1.35f);
        FeedbackText.gameObject.SetActive(false);
        feedbackRoutine = null;
    }

    private static string FormatProgress(float value)
    {
        return Mathf.Approximately(value, Mathf.Round(value))
            ? Mathf.RoundToInt(value).ToString()
            : value.ToString("0.0");
    }

    private void Unbind()
    {
        if (progression == null)
        {
            return;
        }

        progression.ProgressChanged -= Refresh;
        progression.StepCompleted -= HandleStepCompleted;
        progression.SequenceCompleted -= HandleSequenceCompleted;
        progression = null;
    }

    private void OnDestroy()
    {
        Unbind();
        if (ownsFont && font != null)
        {
            Destroy(font);
        }
    }
}
