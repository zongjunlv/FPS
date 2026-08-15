using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(-300)]
public sealed class PlayerInventoryController : MonoBehaviour
{
    [SerializeField, Min(1)] private int capacity = 12;
    [SerializeField, Min(0f)] private float itemUseCooldown;

    private readonly Dictionary<string, ItemDefinition> catalog =
        new(StringComparer.Ordinal);
    private readonly ItemEffectRegistry effects = new();
    private PlayerInputReader input;
    private Health health;
    private WeaponLoadoutController loadout;
    private GameplayLockCoordinator gameplayLocks;
    private WorldItemFactory worldItemFactory;
    private GameplayLockLease inventoryLock;
    private InventoryView view;
    private ConsumableQuickSlotHud quickSlotHud;
    private bool waitingForHud;
    private bool usingItem;
    private bool droppingItem;
    private float nextUseTime;
    private bool cooldownWasActive;

    public InventoryState Inventory { get; private set; }
    public QuickSlotState QuickSlots { get; private set; }
    public bool IsOpen => view != null && view.IsVisible;
    public bool IsUseCoolingDown =>
        itemUseCooldown > 0f && Time.unscaledTime < nextUseTime;
    public float UseCooldownRemaining => Mathf.Max(
        0f,
        nextUseTime - Time.unscaledTime);

    private void Awake()
    {
        input = GetComponent<PlayerInputReader>();
        health = GetComponent<Health>();
        loadout = GetComponent<WeaponLoadoutController>();
        gameplayLocks = GetComponent<GameplayLockCoordinator>();
        worldItemFactory = GetComponent<WorldItemFactory>();
        worldItemFactory ??= gameObject.AddComponent<WorldItemFactory>();
        Inventory = new InventoryState(capacity);
        QuickSlots = new QuickSlotState(2);
    }

    private void Start()
    {
        if (health != null)
        {
            health.VitalsChanged += HandleRuntimeStateChanged;
        }

        if (gameplayLocks != null)
        {
            gameplayLocks.ModalStateChanged += HandleModalStateChanged;
        }

        StartCoroutine(WaitForQuickSlotHud());

        if (loadout == null)
        {
            return;
        }

        for (int index = 0; index < loadout.WeaponCount; index++)
        {
            WeaponController weapon = loadout.GetWeapon(index);

            if (weapon != null)
            {
                weapon.AmmoChanged += HandleRuntimeStateChanged;
            }
        }
    }

    private void Update()
    {
        if (input == null)
        {
            return;
        }

        if (input.InventoryPressed)
        {
            if (IsOpen)
            {
                if (gameplayLocks == null ||
                    gameplayLocks.State.IsTopmost(
                        GameplayLockReason.Inventory))
                {
                    Close();
                }
            }
            else
            {
                Open();
            }

            return;
        }

        int quickSlot = input.ConsumeQuickUse();

        if (quickSlot >= 0)
        {
            TryUseQuickSlot(quickSlot);
        }

        if (cooldownWasActive && !IsUseCoolingDown)
        {
            cooldownWasActive = false;
            HandleRuntimeStateChanged();
        }
    }

    private void OnDisable()
    {
        Close();
    }

    private void OnDestroy()
    {
        if (health != null)
        {
            health.VitalsChanged -= HandleRuntimeStateChanged;
        }

        if (gameplayLocks != null)
        {
            gameplayLocks.ModalStateChanged -= HandleModalStateChanged;
        }

        if (loadout == null)
        {
            return;
        }

        for (int index = 0; index < loadout.WeaponCount; index++)
        {
            WeaponController weapon = loadout.GetWeapon(index);

            if (weapon != null)
            {
                weapon.AmmoChanged -= HandleRuntimeStateChanged;
            }
        }
    }

    public void ConfigureInventory(int configuredCapacity)
    {
        if (Inventory != null && Inventory.OccupiedSlotCount > 0)
        {
            throw new InvalidOperationException(
                "A non-empty inventory cannot be reconfigured.");
        }

        capacity = Mathf.Max(1, configuredCapacity);
        Inventory = new InventoryState(capacity);
    }

    public void ConfigureUseCooldown(float seconds)
    {
        itemUseCooldown = Mathf.Max(0f, seconds);
        nextUseTime = 0f;
        cooldownWasActive = false;
        HandleRuntimeStateChanged();
    }

    public void RegisterItem(ItemDefinition definition)
    {
        if (definition == null ||
            string.IsNullOrWhiteSpace(definition.StableId))
        {
            return;
        }

        if (catalog.TryGetValue(
                definition.StableId,
                out ItemDefinition existing) &&
            existing != null &&
            (existing.ItemType != definition.ItemType ||
             existing.MaximumStack != definition.MaximumStack ||
             existing.EffectType != definition.EffectType ||
             !Mathf.Approximately(
                 existing.EffectAmount,
                 definition.EffectAmount)))
        {
            throw new InvalidOperationException(
                $"Item ID '{definition.StableId}' has conflicting definitions.");
        }

        catalog[definition.StableId] = definition;
    }

    public bool TryGetDefinition(
        string stableId,
        out ItemDefinition definition)
    {
        definition = null;

        if (string.IsNullOrWhiteSpace(stableId) ||
            !catalog.TryGetValue(stableId, out ItemDefinition resolved) ||
            resolved == null)
        {
            return false;
        }

        definition = resolved;
        return true;
    }

    public bool TryAdd(ItemDefinition definition, int quantity = 1)
    {
        if (definition == null ||
            string.IsNullOrWhiteSpace(definition.StableId))
        {
            return false;
        }

        RegisterItem(definition);
        return Inventory.TryAdd(definition.ToSpec(), quantity);
    }

    public InventoryAddResult Add(
        ItemDefinition definition,
        int quantity = 1)
    {
        if (definition == null ||
            string.IsNullOrWhiteSpace(definition.StableId))
        {
            return new InventoryAddResult(quantity, 0);
        }

        RegisterItem(definition);
        return Inventory.Add(definition.ToSpec(), quantity);
    }

    public ItemUseResult GetUseAvailability(int slotIndex)
    {
        InventorySlot slot = Inventory.GetSlot(slotIndex);

        if (slot.IsEmpty ||
            !catalog.TryGetValue(slot.StableId, out ItemDefinition definition))
        {
            return ItemUseResult.Failure(
                ItemUseFailureReason.InvalidItem);
        }

        if (IsUseCoolingDown)
        {
            return ItemUseResult.Failure(
                ItemUseFailureReason.CooldownActive);
        }

        return effects.Evaluate(
            definition,
            new ItemUseContext(health, loadout));
    }

    public bool TryUse(int slotIndex)
    {
        return TryUseCore(slotIndex);
    }

    public bool TryUseQuickSlot(int quickSlotIndex)
    {
        if (gameplayLocks != null && gameplayLocks.IsLocked)
        {
            return false;
        }

        string stableId = QuickSlots.GetBoundId(quickSlotIndex);
        int inventorySlot = FindFirstSlot(stableId);
        return inventorySlot >= 0 && TryUseCore(inventorySlot);
    }

    public bool BindQuickSlot(int quickSlotIndex, string stableId)
    {
        if (string.IsNullOrWhiteSpace(stableId) ||
            !catalog.ContainsKey(stableId))
        {
            return false;
        }

        if (string.Equals(
                QuickSlots.GetBoundId(quickSlotIndex),
                stableId,
                StringComparison.Ordinal))
        {
            return true;
        }

        return QuickSlots.Bind(quickSlotIndex, stableId);
    }

    public InventoryOperationResult Transfer(
        int sourceIndex,
        int destinationIndex)
    {
        return Inventory.Transfer(sourceIndex, destinationIndex);
    }

    public InventoryOperationResult DragTransfer(
        int sourceIndex,
        int destinationIndex)
    {
        InventorySlot destination = Inventory.GetSlot(destinationIndex);

        if (destination.IsEmpty)
        {
            return Inventory.Move(sourceIndex, destinationIndex);
        }

        InventorySlot source = Inventory.GetSlot(sourceIndex);
        return !source.IsEmpty && string.Equals(
                   source.StableId,
                   destination.StableId,
                   StringComparison.Ordinal) &&
               source.MaximumStack == destination.MaximumStack
            ? Inventory.Merge(sourceIndex, destinationIndex)
            : Inventory.Swap(sourceIndex, destinationIndex);
    }

    public InventoryOperationResult Compact()
    {
        return Inventory.Compact();
    }

    public InventoryOperationResult Split(
        int sourceIndex,
        int destinationIndex,
        int quantity)
    {
        return Inventory.Split(sourceIndex, destinationIndex, quantity);
    }

    public bool TryDrop(int slotIndex, int quantity)
    {
        if (droppingItem || quantity <= 0)
        {
            return false;
        }

        InventorySlot slot = Inventory.GetSlot(slotIndex);

        if (slot.IsEmpty || slot.Quantity < quantity ||
            !catalog.TryGetValue(slot.StableId, out ItemDefinition definition) ||
            worldItemFactory == null ||
            !worldItemFactory.TryPrepareDrop(
                definition,
                quantity,
                out WorldItemDropHandle handle))
        {
            return false;
        }

        droppingItem = true;

        try
        {
            using (handle)
            {
                WorldItemPickup committedPickup = null;
                bool committed = Inventory.TryConsumeAt(
                    slotIndex,
                    quantity,
                    () =>
                    {
                        committedPickup = handle.Commit();
                        return committedPickup != null;
                    });

                if (!committed)
                {
                    return false;
                }

                view?.ShowMessage($"已丢弃 {definition.DisplayName} ×{quantity}");
                return true;
            }
        }
        finally
        {
            droppingItem = false;
        }
    }

    private bool TryUseCore(int slotIndex)
    {
        if (usingItem)
        {
            return false;
        }

        InventorySlot slot = Inventory.GetSlot(slotIndex);

        if (slot.IsEmpty ||
            !catalog.TryGetValue(slot.StableId, out ItemDefinition definition))
        {
            return false;
        }

        usingItem = true;

        try
        {
            ItemUseResult availability = GetUseAvailability(slotIndex);

            if (!availability.Succeeded)
            {
                view?.ShowMessage(
                    ItemUsePresentation.GetFailureText(
                        availability.FailureReason));
                return false;
            }

            ItemUseResult result = ItemUseResult.Failure(
                ItemUseFailureReason.InvalidItem);
            bool consumed = Inventory.TryConsumeAt(
                slotIndex,
                1,
                () =>
                {
                    result = effects.Apply(
                        definition,
                        new ItemUseContext(health, loadout));

                    if (!result.Succeeded)
                    {
                        return false;
                    }

                    nextUseTime = Time.unscaledTime + itemUseCooldown;
                    cooldownWasActive = itemUseCooldown > 0f;
                    return true;
                });

            if (!consumed)
            {
                view?.ShowMessage(
                    ItemUsePresentation.GetFailureText(
                        result.FailureReason));
                return false;
            }

            if (consumed)
            {
                view?.ShowMessage(
                    GetSuccessMessage(definition.EffectType));
                quickSlotHud?.ShowMessage(
                    GetSuccessMessage(definition.EffectType));
                HandleRuntimeStateChanged();
            }

            return true;
        }
        finally
        {
            usingItem = false;
        }
    }

    public bool Open()
    {
        if (IsOpen || !CanOpen())
        {
            return false;
        }

        if (!EnsureView())
        {
            if (!waitingForHud)
            {
                StartCoroutine(WaitForHud());
            }

            return false;
        }

        inventoryLock ??= gameplayLocks?.Acquire(
            GameplayLockReason.Inventory);
        view.Show(
            Inventory,
            ResolveDefinition,
            GetUseAvailability,
            TryUse,
            DragTransfer,
            Compact,
            Split,
            TryDrop,
            BindQuickSlotFromInventory,
            Close);
        HandleModalStateChanged(gameplayLocks?.TopReason);
        return true;
    }

    public void Close()
    {
        view?.Hide();
        inventoryLock?.Dispose();
        inventoryLock = null;
    }

    private bool CanOpen()
    {
        if (gameplayLocks == null)
        {
            return true;
        }

        GameplayLockState state = gameplayLocks.State;
        return !state.IsReasonActive(GameplayLockReason.UpgradeChoice) &&
               !state.IsReasonActive(GameplayLockReason.PauseMenu) &&
               !state.IsReasonActive(GameplayLockReason.Victory) &&
               !state.IsReasonActive(GameplayLockReason.Defeat);
    }

    private ItemDefinition ResolveDefinition(string stableId)
    {
        return !string.IsNullOrEmpty(stableId) &&
               catalog.TryGetValue(stableId, out ItemDefinition definition)
            ? definition
            : null;
    }

    private void HandleRuntimeStateChanged()
    {
        view?.RefreshRuntimeState();
        quickSlotHud?.RefreshRuntimeState();
    }

    private void HandleModalStateChanged(GameplayLockReason? topReason)
    {
        view?.SetSuspended(
            topReason.HasValue &&
            topReason.Value != GameplayLockReason.Inventory);
    }

    private bool BindQuickSlotFromInventory(
        int quickSlotIndex,
        int inventorySlotIndex)
    {
        InventorySlot slot = Inventory.GetSlot(inventorySlotIndex);
        return !slot.IsEmpty &&
               BindQuickSlot(quickSlotIndex, slot.StableId);
    }

    private int FindFirstSlot(string stableId)
    {
        if (string.IsNullOrEmpty(stableId))
        {
            return -1;
        }

        for (int index = 0; index < Inventory.Capacity; index++)
        {
            InventorySlot slot = Inventory.GetSlot(index);

            if (!slot.IsEmpty && string.Equals(
                    slot.StableId,
                    stableId,
                    StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }

    private static string GetSuccessMessage(ItemEffectType effectType)
    {
        return effectType switch
        {
            ItemEffectType.RestoreHealth => "医疗包使用成功",
            ItemEffectType.RestoreArmor => "护甲包使用成功",
            ItemEffectType.AddRifleAmmo => "已补充步枪备弹",
            ItemEffectType.AddHandgunAmmo => "已补充手枪备弹",
            _ => "物品使用成功"
        };
    }

    private bool EnsureView()
    {
        if (view != null)
        {
            return true;
        }

        UnifiedGameHud hud = FindAnyObjectByType<UnifiedGameHud>();

        if (hud == null || hud.ModalLayer == null)
        {
            return false;
        }

        view = hud.GetComponent<InventoryView>();
        view ??= hud.gameObject.AddComponent<InventoryView>();
        view.Initialize(hud.ModalLayer);
        return true;
    }

    private bool EnsureQuickSlotHud()
    {
        if (quickSlotHud != null)
        {
            return true;
        }

        UnifiedGameHud hud = FindAnyObjectByType<UnifiedGameHud>();

        if (hud == null || hud.HudLayer == null)
        {
            return false;
        }

        quickSlotHud = hud.GetComponent<ConsumableQuickSlotHud>();
        quickSlotHud ??= hud.gameObject.AddComponent<ConsumableQuickSlotHud>();
        quickSlotHud.Initialize(hud.HudLayer);
        quickSlotHud.Bind(
            Inventory,
            QuickSlots,
            ResolveDefinition,
            GetQuickSlotAvailability);
        return true;
    }

    private ItemUseResult GetQuickSlotAvailability(int quickSlotIndex)
    {
        int inventorySlot = FindFirstSlot(
            QuickSlots.GetBoundId(quickSlotIndex));
        return inventorySlot >= 0
            ? GetUseAvailability(inventorySlot)
            : ItemUseResult.Failure(ItemUseFailureReason.InvalidItem);
    }

    private IEnumerator WaitForQuickSlotHud()
    {
        for (int frame = 0; frame < 120 && isActiveAndEnabled; frame++)
        {
            if (EnsureQuickSlotHud())
            {
                yield break;
            }

            yield return null;
        }
    }

    private IEnumerator WaitForHud()
    {
        waitingForHud = true;

        for (int frame = 0; frame < 120 && isActiveAndEnabled; frame++)
        {
            if (EnsureView())
            {
                break;
            }

            yield return null;
        }

        waitingForHud = false;

        if (view != null)
        {
            Open();
        }
    }
}
