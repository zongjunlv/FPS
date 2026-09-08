using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public sealed class UpgradeChoiceView : MonoBehaviour
{
    private readonly List<Button> cardButtons = new();
    private RectTransform root;
    private RectTransform cardsRoot;
    private CanvasGroup rootCanvasGroup;
    private TMP_FontAsset fontAsset;
    private TMP_FontAsset runtimeChineseFontAsset;
    private Func<int, bool> onSelected;
    private bool selectionCommitted;
    private HudIconCatalog fallbackIcons;

    public bool IsVisible => root != null && root.gameObject.activeSelf;
    public bool IsSuspended { get; private set; }
    public int CardCount => cardButtons.Count;
    public int SubmitCount { get; private set; }

    public void Initialize(RectTransform modalLayer)
    {
        if (root != null || modalLayer == null)
        {
            return;
        }

        fontAsset = CreateChineseFontAsset();
        root = CreateRect("UpgradeChoicePanel", modalLayer);
        Stretch(root);
        rootCanvasGroup = root.gameObject.AddComponent<CanvasGroup>();
        Image blocker = root.gameObject.AddComponent<Image>();
        blocker.color = new Color(0.005f, 0.008f, 0.012f, 0.9f);
        blocker.raycastTarget = true;
        TMP_Text heading = CreateText(
            "UpgradeHeading",
            root,
            "选择一项升级",
            34f,
            TextAlignmentOptions.Center);
        SetRect(
            heading.rectTransform,
            new Vector2(0.5f, 1f),
            new Vector2(0.5f, 1f),
            new Vector2(0.5f, 1f),
            new Vector2(760f, 54f),
            new Vector2(0f, -86f));
        heading.color = new Color(0.38f, 0.92f, 0.86f, 1f);
        heading.fontStyle = FontStyles.Bold;
        TMP_Text instruction = CreateText(
            "UpgradeInstruction",
            root,
            "鼠标选择  ·  方向键或摇杆切换  ·  确认键选择",
            15f,
            TextAlignmentOptions.Center);
        SetRect(
            instruction.rectTransform,
            new Vector2(0.5f, 1f),
            new Vector2(0.5f, 1f),
            new Vector2(0.5f, 1f),
            new Vector2(840f, 32f),
            new Vector2(0f, -142f));
        instruction.color = new Color(0.72f, 0.78f, 0.8f, 1f);
        cardsRoot = CreateRect("UpgradeCards", root);
        SetRect(
            cardsRoot,
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(1120f, 440f),
            new Vector2(0f, -10f));
        root.gameObject.SetActive(false);
    }

    public void SetSuspended(bool suspended)
    {
        IsSuspended = suspended;

        if (rootCanvasGroup == null)
        {
            return;
        }

        rootCanvasGroup.alpha = suspended ? 0f : 1f;
        rootCanvasGroup.interactable = !suspended;
        rootCanvasGroup.blocksRaycasts = !suspended;

        if (suspended && EventSystem.current != null &&
            EventSystem.current.currentSelectedGameObject != null &&
            EventSystem.current.currentSelectedGameObject.transform
                .IsChildOf(root))
        {
            EventSystem.current.SetSelectedGameObject(null);
        }
        else if (!suspended && IsVisible && cardButtons.Count > 0 &&
                 EventSystem.current != null)
        {
            EventSystem.current.SetSelectedGameObject(
                cardButtons[0].gameObject);
        }
    }

    public void Show(
        IReadOnlyList<UpgradeDefinition> candidates,
        RunUpgradeState state,
        Func<int, bool> selectionHandler)
    {
        if (root == null)
        {
            return;
        }

        ClearCards();
        onSelected = selectionHandler;
        selectionCommitted = false;
        int count = candidates != null ? candidates.Count : 0;
        float cardWidth = 340f;
        float spacing = 30f;
        float contentWidth = count * cardWidth +
            Mathf.Max(0, count - 1) * spacing;

        for (int index = 0; index < count; index++)
        {
            float x = -contentWidth * 0.5f +
                cardWidth * 0.5f + index * (cardWidth + spacing);
            Button button = CreateCard(
                candidates[index],
                state.GetLevel(candidates[index].StableId),
                index,
                x,
                cardWidth);
            cardButtons.Add(button);
        }

        ConfigureNavigation();
        root.gameObject.SetActive(true);
        root.SetAsLastSibling();

        if (cardButtons.Count > 0 && EventSystem.current != null)
        {
            EventSystem.current.SetSelectedGameObject(
                cardButtons[0].gameObject);
        }
    }

    public void Hide()
    {
        if (root == null)
        {
            return;
        }

        if (EventSystem.current != null &&
            EventSystem.current.currentSelectedGameObject != null &&
            EventSystem.current.currentSelectedGameObject.transform
                .IsChildOf(root))
        {
            EventSystem.current.SetSelectedGameObject(null);
        }

        root.gameObject.SetActive(false);
        onSelected = null;
        selectionCommitted = false;
    }

    private void OnDestroy()
    {
        fallbackIcons?.Dispose();
        if (runtimeChineseFontAsset != null)
        {
            Destroy(runtimeChineseFontAsset);
        }

    }

    private void Update()
    {
        if (!IsVisible || IsSuspended || selectionCommitted ||
            Keyboard.current == null)
        {
            return;
        }

        if (Keyboard.current.digit1Key.wasPressedThisFrame)
        {
            Commit(0);
        }
        else if (Keyboard.current.digit2Key.wasPressedThisFrame)
        {
            Commit(1);
        }
        else if (Keyboard.current.digit3Key.wasPressedThisFrame)
        {
            Commit(2);
        }
    }

    private Button CreateCard(
        UpgradeDefinition definition,
        int currentLevel,
        int index,
        float x,
        float width)
    {
        GameObject cardObject = new GameObject(
            $"UpgradeCard{index + 1}",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image),
            typeof(Button),
            typeof(Outline));
        cardObject.transform.SetParent(cardsRoot, false);
        RectTransform cardRect = (RectTransform)cardObject.transform;
        SetRect(
            cardRect,
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(width, 410f),
            new Vector2(x, 0f));
        Image background = cardObject.GetComponent<Image>();
        background.color = new Color(0.035f, 0.045f, 0.055f, 0.97f);
        background.raycastTarget = true;
        Outline outline = cardObject.GetComponent<Outline>();
        Color rarityColor = GetRarityColor(definition.Rarity);
        outline.effectColor = rarityColor;
        outline.effectDistance = new Vector2(2f, -2f);
        Button button = cardObject.GetComponent<Button>();
        ColorBlock colors = button.colors;
        colors.normalColor = background.color;
        colors.highlightedColor = new Color(0.08f, 0.13f, 0.15f, 1f);
        colors.selectedColor = colors.highlightedColor;
        colors.pressedColor = new Color(0.12f, 0.22f, 0.22f, 1f);
        button.colors = colors;
        int capturedIndex = index;
        button.onClick.AddListener(() => Commit(capturedIndex));

        Image icon = CreateImage("Icon", cardRect, Color.white);
        icon.sprite = definition.Icon != null
            ? definition.Icon
            : (fallbackIcons ??= new HudIconCatalog()).Get(HudIconId.Ammo);
        icon.preserveAspect = true;
        SetRect(
            icon.rectTransform,
            new Vector2(0.5f, 1f),
            new Vector2(0.5f, 1f),
            new Vector2(0.5f, 1f),
            new Vector2(112f, 112f),
            new Vector2(0f, -32f));
        TMP_Text rarity = CreateText(
            "Rarity",
            cardRect,
            GetRarityText(definition.Rarity),
            14f,
            TextAlignmentOptions.Center);
        SetRect(
            rarity.rectTransform,
            new Vector2(0.5f, 1f),
            new Vector2(0.5f, 1f),
            new Vector2(0.5f, 1f),
            new Vector2(260f, 24f),
            new Vector2(0f, -154f));
        rarity.color = rarityColor;
        rarity.fontStyle = FontStyles.Bold;
        TMP_Text title = CreateText(
            "Title",
            cardRect,
            definition.Title,
            21f,
            TextAlignmentOptions.Center);
        SetRect(
            title.rectTransform,
            new Vector2(0.5f, 1f),
            new Vector2(0.5f, 1f),
            new Vector2(0.5f, 1f),
            new Vector2(292f, 56f),
            new Vector2(0f, -188f));
        title.fontStyle = FontStyles.Bold;
        TMP_Text description = CreateText(
            "Description",
            cardRect,
            definition.Description,
            16f,
            TextAlignmentOptions.Center);
        SetRect(
            description.rectTransform,
            new Vector2(0.5f, 1f),
            new Vector2(0.5f, 1f),
            new Vector2(0.5f, 1f),
            new Vector2(292f, 72f),
            new Vector2(0f, -254f));
        description.color = new Color(0.82f, 0.86f, 0.88f, 1f);
        TMP_Text effect = CreateText(
            "Effect",
            cardRect,
            definition.EffectValueText,
            17f,
            TextAlignmentOptions.Center);
        SetRect(
            effect.rectTransform,
            new Vector2(0.5f, 0f),
            new Vector2(0.5f, 0f),
            new Vector2(0.5f, 0f),
            new Vector2(292f, 28f),
            new Vector2(0f, 68f));
        effect.color = rarityColor;
        effect.fontStyle = FontStyles.Bold;
        TMP_Text stack = CreateText(
            "Stack",
            cardRect,
            definition.GetLevelText(currentLevel),
            14f,
            TextAlignmentOptions.Center);
        SetRect(
            stack.rectTransform,
            new Vector2(0.5f, 0f),
            new Vector2(0.5f, 0f),
            new Vector2(0.5f, 0f),
            new Vector2(292f, 28f),
            new Vector2(0f, 30f));
        stack.color = rarityColor;
        return button;
    }

    private void Commit(int index)
    {
        if (IsSuspended || selectionCommitted ||
            index < 0 ||
            index >= cardButtons.Count ||
            onSelected == null)
        {
            return;
        }

        selectionCommitted = true;

        if (onSelected(index))
        {
            SubmitCount++;
            return;
        }

        selectionCommitted = false;
    }

    private void ConfigureNavigation()
    {
        for (int index = 0; index < cardButtons.Count; index++)
        {
            Navigation navigation = new Navigation
            {
                mode = Navigation.Mode.Explicit,
                selectOnLeft = cardButtons[
                    Mathf.Max(0, index - 1)],
                selectOnRight = cardButtons[
                    Mathf.Min(cardButtons.Count - 1, index + 1)],
                selectOnUp = cardButtons[index],
                selectOnDown = cardButtons[index]
            };
            cardButtons[index].navigation = navigation;
        }
    }

    private void ClearCards()
    {
        for (int index = cardsRoot.childCount - 1; index >= 0; index--)
        {
            Destroy(cardsRoot.GetChild(index).gameObject);
        }

        cardButtons.Clear();
    }

    private TMP_Text CreateText(
        string objectName,
        Transform parent,
        string content,
        float fontSize,
        TextAlignmentOptions alignment)
    {
        GameObject textObject = new GameObject(
            objectName,
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(TextMeshProUGUI));
        textObject.transform.SetParent(parent, false);
        TMP_Text text = textObject.GetComponent<TMP_Text>();
        text.text = content;
        text.fontSize = fontSize;
        text.alignment = alignment;
        text.color = Color.white;
        text.raycastTarget = false;

        if (fontAsset != null)
        {
            text.font = fontAsset;
        }

        return text;
    }

    private static Image CreateImage(
        string objectName,
        Transform parent,
        Color color)
    {
        GameObject imageObject = new GameObject(
            objectName,
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image));
        imageObject.transform.SetParent(parent, false);
        Image image = imageObject.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private static RectTransform CreateRect(
        string objectName,
        Transform parent)
    {
        GameObject child = new GameObject(objectName, typeof(RectTransform));
        child.transform.SetParent(parent, false);
        return (RectTransform)child.transform;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static void SetRect(
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

    private static Color GetRarityColor(UpgradeRarity rarity)
    {
        return rarity switch
        {
            UpgradeRarity.Rare => new Color(0.2f, 0.58f, 1f, 1f),
            UpgradeRarity.Epic => new Color(0.76f, 0.34f, 1f, 1f),
            _ => new Color(0.38f, 0.92f, 0.86f, 1f)
        };
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
                runtimeChineseFontAsset.name = "升级卡中文动态字体";
                return runtimeChineseFontAsset;
            }

            if (runtimeChineseFontAsset != null)
            {
                Destroy(runtimeChineseFontAsset);
                runtimeChineseFontAsset = null;
            }
        }

        Debug.LogWarning(
            "未找到可用的中文系统字体，升级卡将使用默认字体。");
        return Resources.Load<TMP_FontAsset>(
            "Fonts & Materials/LiberationSans SDF");
    }

    private static string GetRarityText(UpgradeRarity rarity)
    {
        return rarity switch
        {
            UpgradeRarity.Rare => "稀有",
            UpgradeRarity.Epic => "史诗",
            _ => "普通"
        };
    }
}
