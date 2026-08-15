using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class WorldPickupListHud : MonoBehaviour
{
    private const int MaximumVisibleRows = 6;
    private readonly List<Image> rowBackgrounds = new();
    private readonly List<TMP_Text> rowTexts = new();
    private RectTransform root;
    private TMP_FontAsset fontAsset;
    private TMP_FontAsset runtimeChineseFontAsset;

    public bool IsVisible => root != null && root.gameObject.activeSelf;
    public int SelectedIndex { get; private set; } = -1;
    public int VisibleRowCount { get; private set; }
    public int RefreshCount { get; private set; }
    public bool HasOuterPanel =>
        root != null && root.GetComponent<Image>() != null;
    public bool HasHeaderOrFooter =>
        root != null &&
        (root.Find("Header") != null || root.Find("Footer") != null);

    public void Initialize(RectTransform hudLayer)
    {
        if (root != null || hudLayer == null)
        {
            return;
        }

        fontAsset = CreateChineseFontAsset();
        root = CreateRect("WorldPickupListHud", hudLayer);
        root.anchorMin = new Vector2(0.5f, 0.5f);
        root.anchorMax = new Vector2(0.5f, 0.5f);
        root.pivot = new Vector2(0f, 0.5f);
        root.anchoredPosition = new Vector2(210f, -20f);
        root.sizeDelta = new Vector2(346f, 252f);

        for (int index = 0; index < MaximumVisibleRows; index++)
        {
            RectTransform row = CreateRect($"PickupRow{index + 1}", root);
            SetTopRect(row, 0f, index * 42f, 346f, 36f);
            Image background = row.gameObject.AddComponent<Image>();
            background.raycastTarget = false;
            TMP_Text text = CreateText(
                "ItemName",
                row,
                string.Empty,
                17f,
                TextAlignmentOptions.MidlineLeft);
            Stretch(text.rectTransform, 12f, 8f);
            rowBackgrounds.Add(background);
            rowTexts.Add(text);
        }
        root.gameObject.SetActive(false);
    }

    public void Refresh(
        IReadOnlyList<WorldItemPickup> pickups,
        int selectedIndex)
    {
        RefreshCount++;
        int count = pickups?.Count ?? 0;

        if (root == null || count == 0)
        {
            Hide();
            return;
        }

        SelectedIndex = Mathf.Clamp(selectedIndex, 0, count - 1);
        VisibleRowCount = Mathf.Min(count, MaximumVisibleRows);
        int first = Mathf.Clamp(
            SelectedIndex - MaximumVisibleRows / 2,
            0,
            Mathf.Max(0, count - MaximumVisibleRows));
        root.gameObject.SetActive(true);

        for (int rowIndex = 0; rowIndex < MaximumVisibleRows; rowIndex++)
        {
            bool visible = rowIndex < VisibleRowCount;
            rowBackgrounds[rowIndex].gameObject.SetActive(visible);

            if (!visible)
            {
                continue;
            }

            int itemIndex = first + rowIndex;
            WorldItemPickup pickup = pickups[itemIndex];
            bool selected = itemIndex == SelectedIndex;
            string itemName = pickup != null && pickup.Definition != null
                ? pickup.Definition.DisplayName
                : "未知物品";
            int quantity = pickup != null ? pickup.RemainingQuantity : 0;
            rowTexts[rowIndex].text =
                $"{(selected ? "▶" : "  ")} {itemName}    ×{quantity}";
            rowTexts[rowIndex].color = selected
                ? new Color(0.32f, 1f, 0.9f, 1f)
                : Color.white;
            rowBackgrounds[rowIndex].color = selected
                ? new Color(0.05f, 0.34f, 0.34f, 0.92f)
                : Color.clear;
        }
    }

    public string GetRowText(int rowIndex)
    {
        return rowIndex >= 0 && rowIndex < rowTexts.Count &&
               rowTexts[rowIndex].gameObject.activeSelf
            ? rowTexts[rowIndex].text
            : string.Empty;
    }

    public void Hide()
    {
        SelectedIndex = -1;
        VisibleRowCount = 0;

        if (root != null)
        {
            root.gameObject.SetActive(false);
        }
    }

    private void OnDestroy()
    {
        if (runtimeChineseFontAsset != null)
        {
            Destroy(runtimeChineseFontAsset);
        }
    }

    private TMP_FontAsset CreateChineseFontAsset()
    {
        string[] preferredFonts =
        {
            "PingFang SC",
            "Microsoft YaHei",
            "Noto Sans CJK SC",
            "Source Han Sans SC",
            "Arial Unicode MS"
        };

        for (int index = 0; index < preferredFonts.Length; index++)
        {
            runtimeChineseFontAsset = TMP_FontAsset.CreateFontAsset(
                preferredFonts[index],
                "Regular",
                48);

            if (runtimeChineseFontAsset != null &&
                runtimeChineseFontAsset.HasCharacter('中', false, true))
            {
                runtimeChineseFontAsset.name = "拾取列表中文动态字体";
                return runtimeChineseFontAsset;
            }

            if (runtimeChineseFontAsset != null)
            {
                Destroy(runtimeChineseFontAsset);
                runtimeChineseFontAsset = null;
            }
        }

        return Resources.Load<TMP_FontAsset>(
            "Fonts & Materials/LiberationSans SDF");
    }

    private TMP_Text CreateText(
        string objectName,
        Transform parent,
        string content,
        float size,
        TextAlignmentOptions alignment)
    {
        GameObject textObject = new(
            objectName,
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(TextMeshProUGUI));
        textObject.transform.SetParent(parent, false);
        TMP_Text text = textObject.GetComponent<TMP_Text>();
        text.font = fontAsset;
        text.text = content;
        text.fontSize = size;
        text.alignment = alignment;
        text.enableWordWrapping = false;
        text.raycastTarget = false;
        return text;
    }

    private static RectTransform CreateRect(
        string objectName,
        Transform parent)
    {
        GameObject value = new(objectName, typeof(RectTransform));
        value.transform.SetParent(parent, false);
        return value.GetComponent<RectTransform>();
    }

    private static void SetTopRect(
        RectTransform rect,
        float left,
        float top,
        float width,
        float height)
    {
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(left, -top);
        rect.sizeDelta = new Vector2(width, height);
    }

    private static void Stretch(
        RectTransform rect,
        float horizontal,
        float vertical)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(horizontal, vertical);
        rect.offsetMax = new Vector2(-horizontal, -vertical);
    }
}
