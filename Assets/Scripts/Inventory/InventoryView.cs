using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed class InventoryView : MonoBehaviour, ICancelHandler
{
    private enum InteractionMode
    {
        None,
        SplitTarget,
        SplitQuantity,
        DropQuantity
    }

    private readonly List<Button> slotButtons = new();
    private readonly List<Image> slotIcons = new();
    private readonly List<TMP_Text> slotCounts = new();
    private readonly List<Outline> slotOutlines = new();
    private HudIconCatalog icons;
    private RectTransform root;
    private RectTransform inventoryWindow;
    private RectTransform gridRoot;
    private RectTransform quantityPanel;
    private RectTransform dragGhost;
    private CanvasGroup rootCanvasGroup;
    private TMP_FontAsset fontAsset;
    private TMP_FontAsset runtimeChineseFontAsset;
    private TMP_Text capacityText;
    private TMP_Text itemNameText;
    private TMP_Text itemTypeText;
    private TMP_Text descriptionText;
    private TMP_Text effectText;
    private TMP_Text heldText;
    private TMP_Text availabilityText;
    private TMP_Text messageText;
    private TMP_Text quantityTitleText;
    private TMP_Text quantityValueText;
    private Image detailIcon;
    private Image dragGhostIcon;
    private TMP_Text dragGhostCount;
    private Button useButton;
    private Button organizeButton;
    private Button splitButton;
    private Button dropButton;
    private Button bindQuickSlot1Button;
    private Button bindQuickSlot2Button;
    private Button quantityConfirmButton;
    private TMP_Text useButtonText;
    private InventoryState inventory;
    private Func<string, ItemDefinition> resolveDefinition;
    private Func<int, ItemUseResult> resolveAvailability;
    private Func<int, bool> onUse;
    private Func<int, int, InventoryOperationResult> onTransfer;
    private Func<InventoryOperationResult> onCompact;
    private Func<int, int, int, InventoryOperationResult> onSplit;
    private Func<int, int, bool> onDrop;
    private Func<int, int, bool> onBindQuickSlot;
    private Action onClose;
    private int selectedIndex = -1;
    private int operationSourceIndex = -1;
    private int operationTargetIndex = -1;
    private int operationQuantity = 1;
    private InteractionMode interactionMode;
    private bool isSuspended;
    private int dragSourceIndex = -1;
    private bool dragEndedInsideInventory;

    public bool IsVisible => root != null && root.gameObject.activeSelf;
    public bool IsSuspended => isSuspended;
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
    public string OperationMessage => messageText != null
        ? messageText.text
        : string.Empty;
    public bool IsQuantityDialogVisible =>
        quantityPanel != null && quantityPanel.gameObject.activeSelf;

    public void Initialize(RectTransform modalLayer)
    {
        if (root != null || modalLayer == null)
        {
            return;
        }

        fontAsset = CreateChineseFontAsset();
        icons = new HudIconCatalog();
        root = CreateRect("InventoryPanel", modalLayer);
        Stretch(root);
        rootCanvasGroup = root.gameObject.AddComponent<CanvasGroup>();
        Image blocker = root.gameObject.AddComponent<Image>();
        blocker.color = new Color(0.005f, 0.008f, 0.012f, 0.9f);
        blocker.raycastTarget = true;
        inventoryWindow = CreatePanel(
            "InventoryWindow",
            root,
            new Vector2(1040f, 650f),
            new Color(0.025f, 0.035f, 0.045f, 0.98f));
        RectTransform panel = inventoryWindow;
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
        BuildDragGhost();
        root.gameObject.SetActive(false);
    }

    public void Show(
        InventoryState source,
        Func<string, ItemDefinition> definitionResolver,
        Func<int, ItemUseResult> availabilityResolver,
        Func<int, bool> useHandler,
        Func<int, int, InventoryOperationResult> transferHandler,
        Func<InventoryOperationResult> compactHandler,
        Func<int, int, int, InventoryOperationResult> splitHandler,
        Func<int, int, bool> dropHandler,
        Func<int, int, bool> bindQuickSlotHandler,
        Action closeHandler)
    {
        Unbind();
        inventory = source;
        resolveDefinition = definitionResolver;
        resolveAvailability = availabilityResolver;
        onUse = useHandler;
        onTransfer = transferHandler;
        onCompact = compactHandler;
        onSplit = splitHandler;
        onDrop = dropHandler;
        onBindQuickSlot = bindQuickSlotHandler;
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
        SetSuspended(false);
        CancelOperation(false);
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

        CancelSlotDrag();
        root.gameObject.SetActive(false);
        SetSuspended(false);
        CancelOperation(false);
        Unbind();
    }

    public void SetSuspended(bool suspended)
    {
        isSuspended = suspended;

        if (suspended)
        {
            CancelSlotDrag();
        }

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
        else if (!suspended && IsVisible && EventSystem.current != null)
        {
            GameObject focus = IsQuantityDialogVisible &&
                               quantityConfirmButton != null
                ? quantityConfirmButton.gameObject
                : selectedIndex >= 0 && selectedIndex < slotButtons.Count
                    ? slotButtons[selectedIndex].gameObject
                    : null;

            if (focus != null)
            {
                EventSystem.current.SetSelectedGameObject(focus);
            }
        }
    }

    public void OnCancel(BaseEventData eventData)
    {
        if (!IsVisible || isSuspended)
        {
            return;
        }

        if (interactionMode != InteractionMode.None)
        {
            CancelOperation(true);
            return;
        }

        onClose?.Invoke();
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
        icons?.Dispose();
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
            InventoryCancelRelay relay =
                slotObject.AddComponent<InventoryCancelRelay>();
            relay.Initialize(this);
            InventorySlotDragRelay dragRelay =
                slotObject.AddComponent<InventorySlotDragRelay>();
            dragRelay.Initialize(this, captured);
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
        SetRect(detailIcon.rectTransform, new Vector2(132f, 360f),
            new Vector2(96f, 96f));
        detailIcon.preserveAspect = true;
        itemNameText = CreateText("Name", details, "空物品格", 25f,
            TextAlignmentOptions.Center);
        SetRect(itemNameText.rectTransform, new Vector2(24f, 318f),
            new Vector2(312f, 40f));
        itemNameText.fontStyle = FontStyles.Bold;
        itemTypeText = CreateText("Type", details, string.Empty, 14f,
            TextAlignmentOptions.Center);
        SetRect(itemTypeText.rectTransform, new Vector2(24f, 290f),
            new Vector2(312f, 26f));
        itemTypeText.color = Accent;
        descriptionText = CreateText("Description", details, string.Empty,
            16f, TextAlignmentOptions.TopLeft);
        SetRect(descriptionText.rectTransform, new Vector2(24f, 230f),
            new Vector2(312f, 54f));
        descriptionText.color = Muted;
        effectText = CreateText("Effect", details, string.Empty, 16f,
            TextAlignmentOptions.MidlineLeft);
        SetRect(effectText.rectTransform, new Vector2(24f, 198f),
            new Vector2(312f, 28f));
        effectText.color = Accent;
        heldText = CreateText("Held", details, string.Empty, 15f,
            TextAlignmentOptions.MidlineLeft);
        SetRect(heldText.rectTransform, new Vector2(24f, 170f),
            new Vector2(312f, 26f));
        availabilityText = CreateText(
            "Availability", details, string.Empty, 13f,
            TextAlignmentOptions.MidlineLeft);
        SetRect(availabilityText.rectTransform, new Vector2(24f, 145f),
            new Vector2(312f, 24f));
        availabilityText.color = new Color(1f, 0.72f, 0.2f, 1f);

        organizeButton = CreateButton(details, "整理", new Vector2(24f, 101f),
            new Vector2(96f, 36f), AutoCompact, out _);
        splitButton = CreateButton(details, "拆分", new Vector2(132f, 101f),
            new Vector2(96f, 36f), BeginSplit, out _);
        dropButton = CreateButton(details, "丢弃", new Vector2(240f, 101f),
            new Vector2(96f, 36f), BeginDrop, out _);

        useButton = CreateButton(details, "使用", new Vector2(24f, 50f),
            new Vector2(96f, 40f), UseSelected, out useButtonText);
        bindQuickSlot1Button = CreateButton(
            details, "绑定 [4]", new Vector2(132f, 50f),
            new Vector2(96f, 40f), () => BindSelected(0), out _);
        bindQuickSlot2Button = CreateButton(
            details, "绑定 [5]", new Vector2(240f, 50f),
            new Vector2(96f, 40f), () => BindSelected(1), out _);
        CreateButton(details, "×", new Vector2(316f, 432f),
            new Vector2(32f, 28f), () => onClose?.Invoke(), out _);

        BuildQuantityPanel(details);
        messageText = CreateText("Message", panel, string.Empty, 14f,
            TextAlignmentOptions.Center);
        SetRect(messageText.rectTransform, new Vector2(640f, 32f),
            new Vector2(360f, 28f));
        messageText.color = new Color(1f, 0.72f, 0.2f, 1f);
    }

    private void BuildQuantityPanel(RectTransform details)
    {
        quantityPanel = CreatePanel(
            "QuantityDialog", details, new Vector2(312f, 176f),
            new Color(0.018f, 0.028f, 0.035f, 0.99f));
        quantityPanel.anchorMin = quantityPanel.anchorMax =
            quantityPanel.pivot = new Vector2(0.5f, 0.5f);
        quantityPanel.anchoredPosition = Vector2.zero;
        Outline outline = quantityPanel.gameObject.AddComponent<Outline>();
        outline.effectColor = Accent;
        outline.effectDistance = new Vector2(2f, -2f);
        quantityTitleText = CreateText(
            "Title", quantityPanel, "选择数量", 20f,
            TextAlignmentOptions.Center);
        SetRect(quantityTitleText.rectTransform, new Vector2(20f, 126f),
            new Vector2(272f, 32f));
        quantityValueText = CreateText(
            "Value", quantityPanel, "1", 28f,
            TextAlignmentOptions.Center);
        SetRect(quantityValueText.rectTransform, new Vector2(106f, 78f),
            new Vector2(100f, 38f));
        CreateButton(quantityPanel, "−", new Vector2(48f, 80f),
            new Vector2(48f, 36f), () => AdjustQuantity(-1), out _);
        CreateButton(quantityPanel, "+", new Vector2(216f, 80f),
            new Vector2(48f, 36f), () => AdjustQuantity(1), out _);
        quantityConfirmButton = CreateButton(
            quantityPanel, "确认", new Vector2(48f, 24f),
            new Vector2(98f, 40f), ConfirmQuantity, out _);
        CreateButton(quantityPanel, "取消", new Vector2(166f, 24f),
            new Vector2(98f, 40f), () => CancelOperation(true), out _);
        quantityPanel.gameObject.SetActive(false);
    }

    private void BuildDragGhost()
    {
        dragGhost = CreatePanel(
            "DragGhost", root, new Vector2(92f, 92f),
            new Color(0.05f, 0.08f, 0.09f, 0.92f));
        dragGhost.gameObject.GetComponent<Image>().raycastTarget = false;
        Outline outline = dragGhost.gameObject.AddComponent<Outline>();
        outline.effectColor = Accent;
        outline.effectDistance = new Vector2(2f, -2f);
        dragGhostIcon = CreateImage("Icon", dragGhost, Color.white);
        SetRect(dragGhostIcon.rectTransform, new Vector2(8f, 8f),
            new Vector2(76f, 76f));
        dragGhostIcon.preserveAspect = true;
        dragGhostCount = CreateText(
            "Count", dragGhost, string.Empty, 15f,
            TextAlignmentOptions.BottomRight);
        SetRect(dragGhostCount.rectTransform, new Vector2(48f, 4f),
            new Vector2(38f, 24f));
        dragGhost.gameObject.SetActive(false);
    }

    private void Select(int index)
    {
        if (isSuspended || inventory == null ||
            index < 0 || index >= inventory.Capacity)
        {
            return;
        }

        if (interactionMode == InteractionMode.SplitTarget)
        {
            SelectSplitTarget(index);
            return;
        }

        if (interactionMode == InteractionMode.SplitQuantity ||
            interactionMode == InteractionMode.DropQuantity)
        {
            return;
        }

        selectedIndex = index;
        Refresh();
    }

    private void UseSelected()
    {
        if (isSuspended || interactionMode != InteractionMode.None)
        {
            return;
        }

        if (onUse?.Invoke(selectedIndex) == true &&
            inventory.GetSlot(selectedIndex).IsEmpty)
        {
            selectedIndex = FindFirstOccupiedSlot();
        }

        Refresh();
    }

    private void AutoCompact()
    {
        int nextSelectedIndex = GetCompactedIndex(selectedIndex);
        InventoryOperationResult result = onCompact != null
            ? onCompact()
            : InventoryOperationResult.Failed(
                InventoryOperationFailure.NoChange);

        if (!result.Succeeded)
        {
            ShowMessage("物品已经排列整齐");
            return;
        }

        selectedIndex = nextSelectedIndex >= 0
            ? nextSelectedIndex
            : FindFirstOccupiedSlot();
        ShowMessage($"整理完成，已填补 {result.TransferredQuantity} 个空位");
        Refresh();
    }

    public void BeginSlotDrag(int index, PointerEventData eventData)
    {
        if (isSuspended || interactionMode != InteractionMode.None ||
            inventory == null || eventData == null)
        {
            return;
        }

        InventorySlot source = inventory.GetSlot(index);
        ItemDefinition definition = source.IsEmpty
            ? null
            : resolveDefinition?.Invoke(source.StableId);

        if (definition == null)
        {
            return;
        }

        selectedIndex = index;
        dragSourceIndex = index;
        dragEndedInsideInventory = false;
        dragGhostIcon.sprite = ResolveIcon(definition);
        dragGhostCount.text = source.Quantity > 1
            ? $"×{source.Quantity}"
            : string.Empty;
        dragGhost.position = eventData.position;
        dragGhost.gameObject.SetActive(true);
        dragGhost.SetAsLastSibling();
        Refresh();
    }

    public void DragSlot(PointerEventData eventData)
    {
        if (dragSourceIndex >= 0 && dragGhost != null && eventData != null)
        {
            dragGhost.position = eventData.position;
        }
    }

    public void DropSlotOn(int destinationIndex)
    {
        if (dragSourceIndex < 0 || inventory == null)
        {
            return;
        }

        dragEndedInsideInventory = true;

        if (destinationIndex == dragSourceIndex)
        {
            ShowMessage("物品位置未改变");
            return;
        }

        InventoryOperationResult result = onTransfer != null
            ? onTransfer(dragSourceIndex, destinationIndex)
            : InventoryOperationResult.Failed(
                InventoryOperationFailure.NoChange);

        if (!result.Succeeded)
        {
            ShowMessage(result.Failure == InventoryOperationFailure.ItemMismatch
                ? "只能拖到空格或同类物品上"
                : GetOperationFailureText(result.Failure));
            return;
        }

        selectedIndex = destinationIndex;
        ShowMessage(result.Kind switch
        {
            InventoryOperationKind.Merge =>
                $"已合并 {result.TransferredQuantity} 个物品",
            InventoryOperationKind.Swap => "物品顺序已交换",
            _ => "物品已移动"
        });
    }

    public void EndSlotDrag(PointerEventData eventData)
    {
        if (dragSourceIndex < 0)
        {
            return;
        }

        int sourceIndex = dragSourceIndex;
        bool pointerInsideWindow = eventData != null &&
            inventoryWindow != null &&
            RectTransformUtility.RectangleContainsScreenPoint(
                inventoryWindow,
                eventData.position,
                eventData.pressEventCamera);

        if (!dragEndedInsideInventory && !pointerInsideWindow)
        {
            InventorySlot source = inventory.GetSlot(sourceIndex);

            if (!source.IsEmpty &&
                onDrop?.Invoke(sourceIndex, source.Quantity) == true)
            {
                ShowMessage($"已丢弃 {source.Quantity} 个物品");

                if (inventory.GetSlot(sourceIndex).IsEmpty)
                {
                    selectedIndex = FindFirstOccupiedSlot();
                    selectedIndex = selectedIndex < 0
                        ? sourceIndex
                        : selectedIndex;
                }
            }
            else
            {
                ShowMessage("附近没有安全落点，物品未被扣除");
            }
        }

        CancelSlotDrag();
        Refresh();
    }

    private void CancelSlotDrag()
    {
        dragSourceIndex = -1;
        dragEndedInsideInventory = false;
        dragGhost?.gameObject.SetActive(false);
    }

    private void BeginSplit()
    {
        InventorySlot source = inventory?.GetSlot(selectedIndex) ?? default;

        if (source.IsEmpty || source.Quantity <= 1)
        {
            ShowMessage("该物品数量不足，无法拆分");
            return;
        }

        operationSourceIndex = selectedIndex;
        interactionMode = InteractionMode.SplitTarget;
        ShowMessage("请选择一个空格作为拆分目标");
        FocusFirstTarget(true);
    }

    private void SelectSplitTarget(int destinationIndex)
    {
        if (destinationIndex == operationSourceIndex ||
            !inventory.GetSlot(destinationIndex).IsEmpty)
        {
            ShowMessage("拆分目标必须是其他空格");
            return;
        }

        operationTargetIndex = destinationIndex;
        operationQuantity = 1;
        interactionMode = InteractionMode.SplitQuantity;
        quantityTitleText.text = "拆分数量";
        RefreshQuantityDialog();
    }

    private void BeginDrop()
    {
        InventorySlot source = inventory?.GetSlot(selectedIndex) ?? default;

        if (source.IsEmpty)
        {
            ShowMessage("请先选择要丢弃的物品");
            return;
        }

        operationSourceIndex = selectedIndex;
        operationTargetIndex = -1;
        operationQuantity = 1;
        interactionMode = InteractionMode.DropQuantity;
        quantityTitleText.text = "丢弃数量";
        RefreshQuantityDialog();
    }

    private void AdjustQuantity(int delta)
    {
        InventorySlot source = inventory?.GetSlot(operationSourceIndex) ??
                               default;
        int maximum = interactionMode == InteractionMode.SplitQuantity
            ? Mathf.Max(1, source.Quantity - 1)
            : Mathf.Max(1, source.Quantity);
        operationQuantity = Mathf.Clamp(
            operationQuantity + delta,
            1,
            maximum);
        RefreshQuantityDialog();
    }

    private void ConfirmQuantity()
    {
        if (interactionMode == InteractionMode.SplitQuantity)
        {
            InventoryOperationResult result = onSplit != null
                ? onSplit(
                    operationSourceIndex,
                    operationTargetIndex,
                    operationQuantity)
                : InventoryOperationResult.Failed(
                    InventoryOperationFailure.NoChange);

            if (!result.Succeeded)
            {
                ShowMessage(GetOperationFailureText(result.Failure));
                return;
            }

            selectedIndex = operationTargetIndex;
            int amount = result.TransferredQuantity;
            CancelOperation(false);
            ShowMessage($"已拆分 {amount} 个物品");
            Refresh();
            return;
        }

        if (interactionMode != InteractionMode.DropQuantity)
        {
            return;
        }

        int sourceIndex = operationSourceIndex;
        int amountToDrop = operationQuantity;

        if (onDrop?.Invoke(sourceIndex, amountToDrop) != true)
        {
            ShowMessage("附近没有安全落点，物品未被扣除");
            return;
        }

        if (inventory.GetSlot(sourceIndex).IsEmpty)
        {
            selectedIndex = FindFirstOccupiedSlot();
            selectedIndex = selectedIndex < 0 ? sourceIndex : selectedIndex;
        }

        CancelOperation(false);
        ShowMessage($"已丢弃 {amountToDrop} 个物品");
        Refresh();
    }

    private void BindSelected(int quickSlotIndex)
    {
        if (onBindQuickSlot?.Invoke(quickSlotIndex, selectedIndex) == true)
        {
            ShowMessage($"已绑定到快捷键 [{quickSlotIndex + 4}]");
        }
        else
        {
            ShowMessage("当前物品无法绑定");
        }
    }

    private void FocusFirstTarget(bool requireEmpty)
    {
        if (EventSystem.current == null || inventory == null)
        {
            return;
        }

        for (int offset = 1; offset < inventory.Capacity; offset++)
        {
            int index = (operationSourceIndex + offset) % inventory.Capacity;

            if (requireEmpty && !inventory.GetSlot(index).IsEmpty)
            {
                continue;
            }

            EventSystem.current.SetSelectedGameObject(
                slotButtons[index].gameObject);
            return;
        }
    }

    private void RefreshQuantityDialog()
    {
        if (quantityPanel == null)
        {
            return;
        }

        bool visible = interactionMode == InteractionMode.SplitQuantity ||
                       interactionMode == InteractionMode.DropQuantity;
        quantityPanel.gameObject.SetActive(visible);

        if (visible)
        {
            quantityValueText.text = operationQuantity.ToString();
            quantityPanel.SetAsLastSibling();

            if (EventSystem.current != null &&
                quantityConfirmButton != null)
            {
                EventSystem.current.SetSelectedGameObject(
                    quantityConfirmButton.gameObject);
            }
        }
    }

    private void CancelOperation(bool showMessage)
    {
        interactionMode = InteractionMode.None;
        operationSourceIndex = -1;
        operationTargetIndex = -1;
        operationQuantity = 1;
        quantityPanel?.gameObject.SetActive(false);

        if (showMessage)
        {
            ShowMessage("已取消当前操作");
        }

        if (IsVisible && !isSuspended && selectedIndex >= 0 &&
            selectedIndex < slotButtons.Count && EventSystem.current != null)
        {
            EventSystem.current.SetSelectedGameObject(
                slotButtons[selectedIndex].gameObject);
        }
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
        useButton.interactable = hasItem && availability.Succeeded &&
                                 interactionMode == InteractionMode.None;
        useButtonText.text = hasItem && availability.Succeeded
            ? "使用"
            : "无法使用";
        availabilityText.text = hasItem
            ? availability.Succeeded
                ? "可以使用"
                : UseFailureReason
            : string.Empty;
        bool canOperate = hasItem && interactionMode == InteractionMode.None;
        organizeButton.interactable =
            interactionMode == InteractionMode.None && CanCompact();
        splitButton.interactable = canOperate && selected.Quantity > 1;
        dropButton.interactable = canOperate;
        bindQuickSlot1Button.interactable = canOperate;
        bindQuickSlot2Button.interactable = canOperate;
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
        onTransfer = null;
        onCompact = null;
        onSplit = null;
        onDrop = null;
        onBindQuickSlot = null;
        onClose = null;
    }

    private bool CanCompact()
    {
        bool foundEmpty = false;

        for (int index = 0; index < inventory.Capacity; index++)
        {
            if (inventory.GetSlot(index).IsEmpty)
            {
                foundEmpty = true;
            }
            else if (foundEmpty)
            {
                return true;
            }
        }

        return false;
    }

    private int GetCompactedIndex(int sourceIndex)
    {
        if (inventory == null || sourceIndex < 0 ||
            inventory.GetSlot(sourceIndex).IsEmpty)
        {
            return -1;
        }

        int compactedIndex = 0;

        for (int index = 0; index < sourceIndex; index++)
        {
            if (!inventory.GetSlot(index).IsEmpty)
            {
                compactedIndex++;
            }
        }

        return compactedIndex;
    }

    private static string GetOperationFailureText(
        InventoryOperationFailure failure)
    {
        return failure switch
        {
            InventoryOperationFailure.InvalidIndex => "物品格无效",
            InventoryOperationFailure.SameSlot => "不能选择同一个物品格",
            InventoryOperationFailure.EmptySource => "来源物品格为空",
            InventoryOperationFailure.EmptyDestination => "目标物品格为空",
            InventoryOperationFailure.DestinationNotEmpty => "目标物品格必须为空",
            InventoryOperationFailure.ItemMismatch => "只能合并同类物品",
            InventoryOperationFailure.DestinationFull => "目标堆叠已满",
            InventoryOperationFailure.InvalidQuantity => "数量必须大于零",
            InventoryOperationFailure.QuantityExceedsSource => "拆分数量必须小于当前数量",
            _ => "当前操作无法完成"
        };
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
        InventoryCancelRelay relay =
            buttonObject.AddComponent<InventoryCancelRelay>();
        relay.Initialize(this);
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

public sealed class InventoryCancelRelay : MonoBehaviour, ICancelHandler
{
    private InventoryView owner;

    public void Initialize(InventoryView inventoryView)
    {
        owner = inventoryView;
    }

    public void OnCancel(BaseEventData eventData)
    {
        owner?.OnCancel(eventData);
    }
}

public sealed class InventorySlotDragRelay : MonoBehaviour,
    IBeginDragHandler, IDragHandler, IEndDragHandler, IDropHandler
{
    private InventoryView owner;
    private int slotIndex;

    public void Initialize(InventoryView inventoryView, int index)
    {
        owner = inventoryView;
        slotIndex = index;
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        owner?.BeginSlotDrag(slotIndex, eventData);
    }

    public void OnDrag(PointerEventData eventData)
    {
        owner?.DragSlot(eventData);
    }

    public void OnDrop(PointerEventData eventData)
    {
        owner?.DropSlotOn(slotIndex);
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        owner?.EndSlotDrag(eventData);
    }
}
