using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class ConsumableQuickSlotHud : MonoBehaviour
{
    private const int DisplayedSlotCount = 2;

    private readonly List<Image> slotBackgrounds = new();
    private readonly List<Image> slotIcons = new();
    private readonly List<TMP_Text> slotQuantityTexts = new();
    private readonly List<TMP_Text> slotBindingTexts = new();
    private readonly List<bool> slotUsableStates = new();
    private HudIconCatalog icons;

    private RectTransform root;
    private TMP_Text messageText;
    private TMP_FontAsset fontAsset;
    private TMP_FontAsset runtimeChineseFontAsset;
    private InventoryState inventory;
    private QuickSlotState quickSlots;
    private Func<string, ItemDefinition> resolveDefinition;
    private Func<int, ItemUseResult> resolveAvailability;

    public int QuickSlotCount => slotIcons.Count;
    public int RefreshCount { get; private set; }

    public void Initialize(RectTransform hudLayer)
    {
        if (root != null || hudLayer == null)
        {
            return;
        }

        fontAsset = CreateChineseFontAsset();
        icons = new HudIconCatalog();
        root = CreateRect("ConsumableQuickSlotHud", hudLayer);
        root.anchorMin = new Vector2(0.5f, 0f);
        root.anchorMax = new Vector2(0.5f, 0f);
        root.pivot = new Vector2(0.5f, 0f);
        root.anchoredPosition = new Vector2(0f, 24f);
        root.sizeDelta = new Vector2(242f, 108f);

        Image panel = root.gameObject.AddComponent<Image>();
        panel.color = new Color(0.02f, 0.03f, 0.04f, 0.76f);
        panel.raycastTarget = false;

        BuildSlots();
        messageText = CreateText(
            "QuickSlotMessage",
            root,
            string.Empty,
            13f,
            TextAlignmentOptions.Center);
        SetBottomLeftRect(
            messageText.rectTransform,
            new Vector2(8f, 82f),
            new Vector2(226f, 22f));
        messageText.color = new Color(1f, 0.72f, 0.2f, 1f);
        Refresh();
    }

    public void Bind(
        InventoryState source,
        QuickSlotState bindings,
        Func<string, ItemDefinition> definitionResolver,
        Func<int, ItemUseResult> availabilityResolver)
    {
        Unbind();
        inventory = source;
        quickSlots = bindings;
        resolveDefinition = definitionResolver;
        resolveAvailability = availabilityResolver;

        if (inventory != null)
        {
            inventory.Changed += Refresh;
        }

        if (quickSlots != null)
        {
            quickSlots.Changed += Refresh;
        }

        Refresh();
    }

    public void RefreshRuntimeState()
    {
        Refresh();
    }

    public void ShowMessage(string message)
    {
        if (messageText != null)
        {
            messageText.text = message ?? string.Empty;
        }
    }

    public string GetBoundId(int slotIndex)
    {
        return IsDisplayedSlot(slotIndex) && quickSlots != null
            ? quickSlots.GetBoundId(slotIndex) ?? string.Empty
            : string.Empty;
    }

    public string GetQuantityText(int slotIndex)
    {
        return IsDisplayedSlot(slotIndex) &&
               slotIndex < slotQuantityTexts.Count
            ? slotQuantityTexts[slotIndex].text
            : string.Empty;
    }

    public Sprite GetIcon(int slotIndex)
    {
        return IsDisplayedSlot(slotIndex) && slotIndex < slotIcons.Count
            ? slotIcons[slotIndex].sprite
            : null;
    }

    public bool IsUsable(int slotIndex)
    {
        return IsDisplayedSlot(slotIndex) &&
               slotIndex < slotUsableStates.Count &&
               slotUsableStates[slotIndex];
    }

    private void OnDestroy()
    {
        Unbind();

        if (runtimeChineseFontAsset != null)
        {
            Destroy(runtimeChineseFontAsset);
        }
    }

    private void BuildSlots()
    {
        slotBackgrounds.Clear();
        slotIcons.Clear();
        slotQuantityTexts.Clear();
        slotBindingTexts.Clear();
        slotUsableStates.Clear();

        for (int index = 0; index < DisplayedSlotCount; index++)
        {
            RectTransform slot = CreateRect(
                $"QuickSlot{index + 1}",
                root);
            SetBottomLeftRect(
                slot,
                new Vector2(8f + index * 113f, 8f),
                new Vector2(105f, 70f));
            Image background = slot.gameObject.AddComponent<Image>();
            background.color = EmptySlotColor;
            background.raycastTarget = false;
            Outline outline = slot.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(1f, 1f, 1f, 0.16f);
            outline.effectDistance = new Vector2(1f, -1f);

            TMP_Text keyText = CreateText(
                "Shortcut",
                slot,
                $"[{index + 4}]",
                13f,
                TextAlignmentOptions.TopLeft);
            SetBottomLeftRect(
                keyText.rectTransform,
                new Vector2(6f, 42f),
                new Vector2(32f, 22f));
            keyText.color = Accent;
            keyText.fontStyle = FontStyles.Bold;

            Image icon = CreateImage("Icon", slot, Color.white);
            SetBottomLeftRect(
                icon.rectTransform,
                new Vector2(28f, 20f),
                new Vector2(50f, 44f));
            icon.preserveAspect = true;

            TMP_Text quantity = CreateText(
                "Quantity",
                slot,
                "×0",
                14f,
                TextAlignmentOptions.BottomRight);
            SetBottomLeftRect(
                quantity.rectTransform,
                new Vector2(68f, 4f),
                new Vector2(31f, 22f));
            quantity.fontStyle = FontStyles.Bold;

            TMP_Text binding = CreateText(
                "Binding",
                slot,
                "未绑定",
                11f,
                TextAlignmentOptions.BottomLeft);
            SetBottomLeftRect(
                binding.rectTransform,
                new Vector2(6f, 3f),
                new Vector2(62f, 20f));
            binding.color = Muted;

            slotBackgrounds.Add(background);
            slotIcons.Add(icon);
            slotQuantityTexts.Add(quantity);
            slotBindingTexts.Add(binding);
            slotUsableStates.Add(false);
        }
    }

    private void Refresh()
    {
        if (root == null)
        {
            return;
        }

        for (int index = 0; index < DisplayedSlotCount; index++)
        {
            string stableId = GetBoundId(index);
            ItemDefinition definition =
                !string.IsNullOrEmpty(stableId)
                    ? resolveDefinition?.Invoke(stableId)
                    : null;
            int quantity = inventory != null &&
                           !string.IsNullOrEmpty(stableId)
                ? inventory.GetQuantity(stableId)
                : 0;
            bool hasBinding = definition != null;
            ItemUseResult availability = hasBinding && quantity > 0 &&
                                         resolveAvailability != null
                ? resolveAvailability(index)
                : ItemUseResult.Failure(
                    ItemUseFailureReason.InvalidItem);
            bool usable = hasBinding && quantity > 0 &&
                          availability.Succeeded;

            slotIcons[index].gameObject.SetActive(hasBinding);
            slotIcons[index].sprite = hasBinding
                ? ResolveIcon(definition)
                : null;
            slotIcons[index].color = usable
                ? Color.white
                : new Color(0.55f, 0.58f, 0.6f, 0.58f);
            slotQuantityTexts[index].text = $"×{quantity}";
            slotQuantityTexts[index].color = usable
                ? Color.white
                : Muted;
            slotBindingTexts[index].text = hasBinding
                ? definition.DisplayName
                : "未绑定";
            slotBackgrounds[index].color = usable
                ? UsableSlotColor
                : EmptySlotColor;
            slotUsableStates[index] = usable;
        }

        RefreshCount++;
    }

    private void Unbind()
    {
        if (inventory != null)
        {
            inventory.Changed -= Refresh;
        }

        if (quickSlots != null)
        {
            quickSlots.Changed -= Refresh;
        }

        inventory = null;
        quickSlots = null;
        resolveDefinition = null;
        resolveAvailability = null;
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
                runtimeChineseFontAsset.HasCharacter(
                    '中',
                    false,
                    true))
            {
                runtimeChineseFontAsset.name = "快捷栏中文动态字体";
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
        GameObject textObject = new GameObject(
            objectName,
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(TextMeshProUGUI));
        textObject.transform.SetParent(parent, false);
        TMP_Text text = textObject.GetComponent<TMP_Text>();
        text.text = content;
        text.fontSize = size;
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
        GameObject rectObject = new GameObject(
            objectName,
            typeof(RectTransform));
        rectObject.transform.SetParent(parent, false);
        return (RectTransform)rectObject.transform;
    }

    private static void SetBottomLeftRect(
        RectTransform rect,
        Vector2 position,
        Vector2 size)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.zero;
        rect.pivot = Vector2.zero;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    private static bool IsDisplayedSlot(int slotIndex)
    {
        return slotIndex >= 0 && slotIndex < DisplayedSlotCount;
    }

    private static readonly Color Accent =
        new(0.38f, 0.92f, 0.86f, 1f);
    private static readonly Color Muted =
        new(0.68f, 0.73f, 0.75f, 1f);
    private static readonly Color EmptySlotColor =
        new(0.055f, 0.07f, 0.08f, 0.94f);
    private static readonly Color UsableSlotColor =
        new(0.06f, 0.14f, 0.14f, 0.96f);
}
