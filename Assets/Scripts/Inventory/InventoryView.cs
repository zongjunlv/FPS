using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed class InventoryView : MonoBehaviour
{
    private readonly List<Button> slotButtons = new();
    private readonly List<Image> slotIcons = new();
    private readonly List<TMP_Text> slotCounts = new();
    private readonly List<Outline> slotOutlines = new();
    private readonly HudIconCatalog icons = new();
    private RectTransform root;
    private RectTransform gridRoot;
    private TMP_FontAsset fontAsset;
    private TMP_FontAsset runtimeChineseFontAsset;
    private TMP_Text capacityText;
    private TMP_Text itemNameText;
    private TMP_Text itemTypeText;
    private TMP_Text descriptionText;
    private TMP_Text effectText;
    private TMP_Text heldText;
    private TMP_Text messageText;
    private Image detailIcon;
    private Button useButton;
    private TMP_Text useButtonText;
    private InventoryState inventory;
    private Func<string, ItemDefinition> resolveDefinition;
    private Func<int, ItemUseResult> resolveAvailability;
    private Func<int, bool> onUse;
    private Action onClose;
    private int selectedIndex = -1;

    public bool IsVisible => root != null && root.gameObject.activeSelf;
    public int SlotCount => slotButtons.Count;
    public int SelectedIndex => selectedIndex;
    public string DetailName => itemNameText != null
        ? itemNameText.text
        : string.Empty;
    public string DetailDescription => descriptionText != null
        ? descriptionText.text
        : string.Empty;
    public string DetailQuantity => heldText != null
        ? heldText.text
        : string.Empty;
    public bool DetailIconVisible =>
        detailIcon != null && detailIcon.gameObject.activeSelf;
    public string DetailEffect => effectText != null
        ? effectText.text
        : string.Empty;
    public string UseFailureReason { get; private set; } = string.Empty;
    public bool UseButtonInteractable =>
        useButton != null && useButton.interactable;

    public void Initialize(RectTransform modalLayer)
    {
        if (root != null || modalLayer == null)
        {
            return;
        }

        fontAsset = CreateChineseFontAsset();
        root = CreateRect("InventoryPanel", modalLayer);
        Stretch(root);
        Image blocker = root.gameObject.AddComponent<Image>();
        blocker.color = new Color(0.005f, 0.008f, 0.012f, 0.9f);
        blocker.raycastTarget = true;
        RectTransform panel = CreatePanel(
            "InventoryWindow",
            root,
            new Vector2(1040f, 650f),
            new Color(0.025f, 0.035f, 0.045f, 0.98f));
        TMP_Text title = CreateText(
            "Title", panel, "背包", 32f,
            TextAlignmentOptions.MidlineLeft);
        SetRect(title.rectTransform, new Vector2(36f, 574f),
            new Vector2(250f, 48f));
        title.fontStyle = FontStyles.Bold;
        title.color = Accent;
        capacityText = CreateText(
            "Capacity", panel, "0 / 12", 16f,
            TextAlignmentOptions.MidlineRight);
        SetRect(capacityText.rectTransform, new Vector2(730f, 582f),
            new Vector2(180f, 32f));
        TMP_Text closeHint = CreateText(
            "CloseHint", panel, "TAB 关闭", 14f,
            TextAlignmentOptions.MidlineRight);
        SetRect(closeHint.rectTransform, new Vector2(920f, 582f),
            new Vector2(90f, 32f));
        closeHint.color = Muted;
        gridRoot = CreateRect("SlotGrid", panel);
        SetRect(gridRoot, new Vector2(36f, 72f),
            new Vector2(600f, 470f));
        BuildDetails(panel);
        root.gameObject.SetActive(false);
    }

    public void Show(
        InventoryState source,
        Func<string, ItemDefinition> definitionResolver,
        Func<int, ItemUseResult> availabilityResolver,
        Func<int, bool> useHandler,
        Action closeHandler)
    {
        Unbind();
        inventory = source;
        resolveDefinition = definitionResolver;
        resolveAvailability = availabilityResolver;
        onUse = useHandler;
        onClose = closeHandler;

        if (inventory == null)
        {
            return;
        }

        BuildSlots(inventory.Capacity);
        inventory.Changed += Refresh;
        selectedIndex = FindFirstOccupiedSlot();

        if (selectedIndex < 0 && inventory.Capacity > 0)
        {
            selectedIndex = 0;
        }

        root.gameObject.SetActive(true);
        root.SetAsLastSibling();
        Refresh();

        if (selectedIndex >= 0 && EventSystem.current != null)
        {
            EventSystem.current.SetSelectedGameObject(
                slotButtons[selectedIndex].gameObject);
        }
    }

    public void Hide()
    {
        if (root == null)
        {
            return;
        }

        root.gameObject.SetActive(false);
        Unbind();
    }

    public void ShowMessage(string message)
    {
        if (messageText != null)
        {
            messageText.text = message ?? string.Empty;
        }
    }

    public void RefreshRuntimeState()
    {
        Refresh();
    }

    public void SelectSlot(int index)
    {
        Select(index);
    }

    private void OnDestroy()
    {
        Unbind();

        if (runtimeChineseFontAsset != null)
        {
            Destroy(runtimeChineseFontAsset);
        }
    }

    private void BuildSlots(int count)
    {
        for (int index = gridRoot.childCount - 1; index >= 0; index--)
        {
            Destroy(gridRoot.GetChild(index).gameObject);
        }

        slotButtons.Clear();
        slotIcons.Clear();
        slotCounts.Clear();
        slotOutlines.Clear();
        const float size = 124f;
        const float gap = 14f;

        for (int index = 0; index < count; index++)
        {
            int captured = index;
            GameObject slotObject = new GameObject(
                $"InventorySlot{index + 1}",
                typeof(RectTransform), typeof(CanvasRenderer),
                typeof(Image), typeof(Button), typeof(Outline));
            slotObject.transform.SetParent(gridRoot, false);
            RectTransform rect = (RectTransform)slotObject.transform;
            int column = index % 4;
            int row = index / 4;
            rect.anchorMin = rect.anchorMax = rect.pivot =
                new Vector2(0f, 1f);
            rect.sizeDelta = new Vector2(size, size);
            rect.anchoredPosition = new Vector2(
                column * (size + gap),
                -row * (size + gap));
            Image background = slotObject.GetComponent<Image>();
            background.color = new Color(0.07f, 0.085f, 0.1f, 1f);
            Button button = slotObject.GetComponent<Button>();
            button.onClick.AddListener(() => Select(captured));
            Outline outline = slotObject.GetComponent<Outline>();
            outline.effectDistance = new Vector2(2f, -2f);
            Image icon = CreateImage("Icon", rect, Color.white);
            SetRect(icon.rectTransform, new Vector2(17f, 17f),
                new Vector2(90f, 90f));
            icon.preserveAspect = true;
            TMP_Text countText = CreateText(
                "Count", rect, string.Empty, 16f,
                TextAlignmentOptions.BottomRight);
            SetRect(countText.rectTransform, new Vector2(72f, 4f),
                new Vector2(44f, 28f));
            countText.fontStyle = FontStyles.Bold;
            slotButtons.Add(button);
            slotIcons.Add(icon);
            slotCounts.Add(countText);
            slotOutlines.Add(outline);
        }
    }

    private void BuildDetails(RectTransform panel)
    {
        RectTransform details = CreatePanel(
            "ItemDetails", panel, new Vector2(360f, 470f),
            new Color(0.05f, 0.065f, 0.075f, 1f));
        details.anchorMin = details.anchorMax = details.pivot =
            new Vector2(1f, 0f);
        details.anchoredPosition = new Vector2(-36f, 72f);
        detailIcon = CreateImage("Icon", details, Color.white);
        SetRect(detailIcon.rectTransform, new Vector2(120f, 326f),
            new Vector2(120f, 120f));
        detailIcon.preserveAspect = true;
        itemNameText = CreateText("Name", details, "空物品格", 25f,
            TextAlignmentOptions.Center);
        SetRect(itemNameText.rectTransform, new Vector2(24f, 284f),
            new Vector2(312f, 40f));
        itemNameText.fontStyle = FontStyles.Bold;
        itemTypeText = CreateText("Type", details, string.Empty, 14f,
            TextAlignmentOptions.Center);
        SetRect(itemTypeText.rectTransform, new Vector2(24f, 254f),
            new Vector2(312f, 26f));
        itemTypeText.color = Accent;
        descriptionText = CreateText("Description", details, string.Empty,
            16f, TextAlignmentOptions.TopLeft);
        SetRect(descriptionText.rectTransform, new Vector2(32f, 172f),
            new Vector2(296f, 70f));
        descriptionText.color = Muted;
        effectText = CreateText("Effect", details, string.Empty, 16f,
            TextAlignmentOptions.MidlineLeft);
        SetRect(effectText.rectTransform, new Vector2(32f, 130f),
            new Vector2(296f, 30f));
        effectText.color = Accent;
        heldText = CreateText("Held", details, string.Empty, 15f,
            TextAlignmentOptions.MidlineLeft);
        SetRect(heldText.rectTransform, new Vector2(32f, 92f),
            new Vector2(296f, 28f));
        useButton = CreateButton(details, "使用", new Vector2(32f, 34f),
            new Vector2(142f, 46f), UseSelected, out useButtonText);
        CreateButton(details, "关闭", new Vector2(186f, 34f),
            new Vector2(142f, 46f), () => onClose?.Invoke(), out _);
        messageText = CreateText("Message", panel, string.Empty, 14f,
            TextAlignmentOptions.Center);
        SetRect(messageText.rectTransform, new Vector2(640f, 32f),
            new Vector2(360f, 28f));
        messageText.color = new Color(1f, 0.72f, 0.2f, 1f);
    }

    private void Select(int index)
    {
        if (inventory == null || index < 0 || index >= inventory.Capacity)
        {
            return;
        }

        selectedIndex = index;
        messageText.text = string.Empty;
        Refresh();
    }

    private void UseSelected()
    {
        if (onUse?.Invoke(selectedIndex) == true &&
            inventory.GetSlot(selectedIndex).IsEmpty)
        {
            selectedIndex = FindFirstOccupiedSlot();
        }

        Refresh();
    }

    private void Refresh()
    {
        if (inventory == null)
        {
            return;
        }

        capacityText.text =
            $"{inventory.OccupiedSlotCount} / {inventory.Capacity}";

        for (int index = 0; index < slotButtons.Count; index++)
        {
            InventorySlot slot = inventory.GetSlot(index);
            ItemDefinition definition = !slot.IsEmpty
                ? resolveDefinition?.Invoke(slot.StableId)
                : null;
            slotIcons[index].gameObject.SetActive(definition != null);
            slotIcons[index].sprite = definition != null
                ? ResolveIcon(definition)
                : null;
            slotCounts[index].text = slot.Quantity > 1
                ? $"×{slot.Quantity}"
                : string.Empty;
            slotOutlines[index].effectColor = index == selectedIndex
                ? Accent
                : new Color(0.2f, 0.24f, 0.28f, 1f);
        }

        InventorySlot selected = inventory.GetSlot(selectedIndex);
        ItemDefinition item = !selected.IsEmpty
            ? resolveDefinition?.Invoke(selected.StableId)
            : null;
        bool hasItem = item != null;
        detailIcon.gameObject.SetActive(hasItem);
        detailIcon.sprite = hasItem
            ? ResolveIcon(item)
            : null;
        itemNameText.text = hasItem ? item.DisplayName : "空物品格";
        itemTypeText.text = hasItem ? "消耗品" : string.Empty;
        descriptionText.text = hasItem ? item.Description : "选择一个物品查看详情。";
        effectText.text = hasItem
            ? ItemUsePresentation.GetEffectText(item)
            : string.Empty;
        heldText.text = hasItem ? $"持有数量：{selected.Quantity}" : string.Empty;
        ItemUseResult availability = hasItem && resolveAvailability != null
            ? resolveAvailability(selectedIndex)
            : ItemUseResult.Failure(ItemUseFailureReason.InvalidItem);
        UseFailureReason = hasItem && !availability.Succeeded
            ? ItemUsePresentation.GetFailureText(availability.FailureReason)
            : string.Empty;
        useButton.interactable = hasItem && availability.Succeeded;
        useButtonText.text = hasItem && availability.Succeeded
            ? "使用"
            : "无法使用";
        messageText.text = UseFailureReason;
    }

    private int FindFirstOccupiedSlot()
    {
        for (int index = 0; index < inventory.Capacity; index++)
        {
            if (!inventory.GetSlot(index).IsEmpty)
            {
                return index;
            }
        }

        return -1;
    }

    private void Unbind()
    {
        if (inventory != null)
        {
            inventory.Changed -= Refresh;
        }

        inventory = null;
        resolveDefinition = null;
        resolveAvailability = null;
        onUse = null;
        onClose = null;
    }

    private Sprite ResolveIcon(ItemDefinition definition)
    {
        if (definition.Icon != null)
        {
            return definition.Icon;
        }

        return definition.EffectType switch
        {
            ItemEffectType.RestoreArmor => icons.Get(HudIconId.Armor),
            ItemEffectType.AddRifleAmmo => icons.Get(HudIconId.Rifle),
            ItemEffectType.AddHandgunAmmo => icons.Get(HudIconId.Handgun),
            _ => icons.Get(HudIconId.Health)
        };
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
            runtimeChineseFontAsset = TMP_FontAsset.CreateFontAsset(
                fonts[index], "Regular", 48);

            if (runtimeChineseFontAsset != null &&
                runtimeChineseFontAsset.HasCharacter('中', false, true))
            {
                runtimeChineseFontAsset.name = "背包中文动态字体";
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
        string objectName, Transform parent, string content,
        float size, TextAlignmentOptions alignment)
    {
        GameObject textObject = new GameObject(
            objectName, typeof(RectTransform), typeof(CanvasRenderer),
            typeof(TextMeshProUGUI));
        textObject.transform.SetParent(parent, false);
        TMP_Text text = textObject.GetComponent<TMP_Text>();
        text.text = content;
        text.fontSize = size;
        text.alignment = alignment;
        text.color = Color.white;
        text.raycastTarget = false;
        if (fontAsset != null) text.font = fontAsset;
        return text;
    }

    private Button CreateButton(
        Transform parent, string label, Vector2 position, Vector2 size,
        Action callback, out TMP_Text labelText)
    {
        GameObject buttonObject = new GameObject(
            label, typeof(RectTransform), typeof(CanvasRenderer),
            typeof(Image), typeof(Button));
        buttonObject.transform.SetParent(parent, false);
        RectTransform rect = (RectTransform)buttonObject.transform;
        SetRect(rect, position, size);
        Image image = buttonObject.GetComponent<Image>();
        image.color = new Color(0.08f, 0.2f, 0.2f, 1f);
        Button button = buttonObject.GetComponent<Button>();
        button.onClick.AddListener(() => callback?.Invoke());
        labelText = CreateText("Label", rect, label, 16f,
            TextAlignmentOptions.Center);
        Stretch(labelText.rectTransform);
        return button;
    }

    private static Image CreateImage(
        string name, Transform parent, Color color)
    {
        GameObject go = new GameObject(
            name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);
        Image image = go.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private static RectTransform CreatePanel(
        string name, Transform parent, Vector2 size, Color color)
    {
        RectTransform rect = CreateRect(name, parent);
        rect.anchorMin = rect.anchorMax = rect.pivot =
            new Vector2(0.5f, 0.5f);
        rect.sizeDelta = size;
        Image image = rect.gameObject.AddComponent<Image>();
        image.color = color;
        return rect;
    }

    private static RectTransform CreateRect(string name, Transform parent)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static void SetRect(
        RectTransform rect, Vector2 position, Vector2 size)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot =
            new Vector2(0f, 0f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    private static readonly Color Accent =
        new(0.38f, 0.92f, 0.86f, 1f);
    private static readonly Color Muted =
        new(0.72f, 0.78f, 0.8f, 1f);
}
