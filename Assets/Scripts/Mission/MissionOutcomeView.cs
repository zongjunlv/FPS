using System;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class MissionOutcomeView : MonoBehaviour
{
    private TMP_FontAsset fontAsset;
    private TMP_FontAsset runtimeFontAsset;
    private RectTransform root;
    private TMP_Text titleText;
    private TMP_Text summaryText;
    private TMP_Text upgradeText;
    private Button restartButton;
    private Action restartAction;

    public bool IsVisible => root != null && root.gameObject.activeSelf;
    public string TitleText => titleText != null ? titleText.text : string.Empty;
    public string SummaryText => summaryText != null ? summaryText.text : string.Empty;
    public string UpgradeText => upgradeText != null ? upgradeText.text : string.Empty;
    public int ShowCount { get; private set; }

    public void Initialize(RectTransform parent)
    {
        if (root != null || parent == null)
        {
            return;
        }

        fontAsset = CreateChineseFontAsset();
        root = CreateRect("MissionOutcomePanel", parent);
        Stretch(root);
        Image blocker = root.gameObject.AddComponent<Image>();
        blocker.color = new Color(0.005f, 0.008f, 0.012f, 0.94f);
        blocker.raycastTarget = true;

        RectTransform panel = CreateRect("ResultCard", root);
        panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0.5f, 0.5f);
        panel.sizeDelta = new Vector2(820f, 610f);
        Image panelImage = panel.gameObject.AddComponent<Image>();
        panelImage.color = new Color(0.025f, 0.035f, 0.045f, 0.98f);

        Image accent = CreateImage("Accent", panel, new Color(0.2f, 0.95f, 0.78f, 1f));
        SetRect(accent.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
            new Vector2(0f, 0.5f), new Vector2(5f, 610f), Vector2.zero);

        titleText = CreateText("OutcomeTitle", panel, 38f,
            TextAlignmentOptions.Center, new Vector2(40f, -40f), new Vector2(740f, 58f));
        titleText.fontStyle = FontStyles.Bold;

        TMP_Text statsHeader = CreateText("StatsHeader", panel, 16f,
            TextAlignmentOptions.Left, new Vector2(54f, -120f), new Vector2(330f, 28f));
        statsHeader.text = "本局统计";
        statsHeader.color = new Color(0.38f, 0.92f, 0.86f, 1f);

        TMP_Text upgradeHeader = CreateText("UpgradeHeader", panel, 16f,
            TextAlignmentOptions.Left, new Vector2(438f, -120f), new Vector2(325f, 28f));
        upgradeHeader.text = "局内强化";
        upgradeHeader.color = new Color(1f, 0.72f, 0.18f, 1f);

        summaryText = CreateText("Summary", panel, 20f,
            TextAlignmentOptions.TopLeft, new Vector2(54f, -158f), new Vector2(330f, 300f));
        summaryText.lineSpacing = 13f;
        upgradeText = CreateText("Upgrades", panel, 18f,
            TextAlignmentOptions.TopLeft, new Vector2(438f, -158f), new Vector2(325f, 300f));
        upgradeText.lineSpacing = 10f;
        upgradeText.textWrappingMode = TextWrappingModes.Normal;

        restartButton = CreateButton(panel, "重新开始  [R]", new Vector2(0f, 42f));
        restartButton.onClick.AddListener(HandleRestart);
        root.gameObject.SetActive(false);
    }

    public void Show(MissionRunSummary summary, Action onRestart)
    {
        if (root == null || !summary.IsValid)
        {
            return;
        }

        restartAction = onRestart;
        bool victory = summary.Outcome == MissionFlowState.Victory;
        titleText.text = victory ? "任务完成" : "任务失败";
        titleText.color = victory
            ? new Color(0.2f, 1f, 0.72f, 1f)
            : new Color(1f, 0.24f, 0.2f, 1f);
        summaryText.text = BuildSummary(summary);
        upgradeText.text = BuildUpgrades(summary);
        root.gameObject.SetActive(true);
        restartButton.interactable = true;
        ShowCount++;
    }

    public void Hide()
    {
        restartAction = null;
        if (root != null)
        {
            root.gameObject.SetActive(false);
        }
    }

    private void OnDestroy()
    {
        if (runtimeFontAsset != null)
        {
            Destroy(runtimeFontAsset);
        }
    }

    private void HandleRestart()
    {
        restartButton.interactable = false;
        restartAction?.Invoke();
    }

    private static string BuildSummary(MissionRunSummary summary)
    {
        int seconds = Mathf.FloorToInt(summary.ElapsedSeconds);
        return
            $"完成波次     {summary.CompletedWaves} / {summary.TotalWaves}\n" +
            $"击杀数量     {summary.Kills}\n" +
            $"开火数量     {summary.ShotsFired}\n" +
            $"命中数量     {summary.Hits}\n" +
            $"命中率       {summary.Accuracy * 100f:0.0}%\n" +
            $"本局用时     {seconds / 60:00}:{seconds % 60:00}\n" +
            $"承受伤害     {summary.DamageTakenAmount:0.#}  ({summary.DamageTakenCount} 次)\n" +
            $"最终等级     {summary.FinalLevel}";
    }

    private static string BuildUpgrades(MissionRunSummary summary)
    {
        if (summary.SelectedUpgrades == null || summary.SelectedUpgrades.Count == 0)
        {
            return "本局未选择强化";
        }

        var builder = new StringBuilder();
        for (int index = 0; index < summary.SelectedUpgrades.Count; index++)
        {
            if (index > 0)
            {
                builder.Append('\n');
            }
            builder.Append("• ").Append(summary.SelectedUpgrades[index]);
        }
        return builder.ToString();
    }

    private TMP_FontAsset CreateChineseFontAsset()
    {
        string[] fonts =
        {
            "PingFang SC", "Microsoft YaHei", "Noto Sans CJK SC",
            "Source Han Sans SC", "Arial Unicode MS"
        };

        for (int index = 0; index < fonts.Length; index++)
        {
            runtimeFontAsset = TMP_FontAsset.CreateFontAsset(fonts[index], "Regular", 48);
            if (runtimeFontAsset != null && runtimeFontAsset.HasCharacter('中', false, true))
            {
                runtimeFontAsset.name = "结算中文动态字体";
                return runtimeFontAsset;
            }

            if (runtimeFontAsset != null)
            {
                Destroy(runtimeFontAsset);
                runtimeFontAsset = null;
            }
        }

        return Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
    }

    private TMP_Text CreateText(string name, Transform parent, float size,
        TextAlignmentOptions alignment, Vector2 position, Vector2 dimensions)
    {
        RectTransform rect = CreateRect(name, parent);
        TextMeshProUGUI text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        text.font = fontAsset;
        text.fontSize = size;
        text.alignment = alignment;
        text.color = Color.white;
        text.raycastTarget = false;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        SetRect(rect, new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(0f, 1f), dimensions, position);
        return text;
    }

    private Button CreateButton(Transform parent, string label, Vector2 position)
    {
        RectTransform rect = CreateRect("RestartButton", parent);
        SetRect(rect, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(0.5f, 0f), new Vector2(300f, 54f), position);
        Image image = rect.gameObject.AddComponent<Image>();
        image.color = new Color(0.06f, 0.5f, 0.45f, 1f);
        Button button = rect.gameObject.AddComponent<Button>();
        TMP_Text text = CreateText("Label", rect, 20f, TextAlignmentOptions.Center,
            Vector2.zero, rect.sizeDelta);
        Stretch(text.rectTransform);
        text.text = label;
        text.fontStyle = FontStyles.Bold;
        return button;
    }

    private static RectTransform CreateRect(string name, Transform parent)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    private static Image CreateImage(string name, Transform parent, Color color)
    {
        RectTransform rect = CreateRect(name, parent);
        Image image = rect.gameObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private static void SetRect(RectTransform rect, Vector2 min, Vector2 max,
        Vector2 pivot, Vector2 size, Vector2 position)
    {
        rect.anchorMin = min;
        rect.anchorMax = max;
        rect.pivot = pivot;
        rect.sizeDelta = size;
        rect.anchoredPosition = position;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }
}
