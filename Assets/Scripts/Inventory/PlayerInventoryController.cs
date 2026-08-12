using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(-300)]
public sealed class PlayerInventoryController : MonoBehaviour
{
    [SerializeField, Min(1)] private int capacity = 12;

    private readonly Dictionary<string, ItemDefinition> catalog =
        new(StringComparer.Ordinal);
    private readonly ItemEffectRegistry effects = new();
    private PlayerInputReader input;
    private Health health;
    private GameplayLockCoordinator gameplayLocks;
    private GameplayLockLease inventoryLock;
    private InventoryView view;
    private bool waitingForHud;
    private bool usingItem;

    public InventoryState Inventory { get; private set; }
    public bool IsOpen => view != null && view.IsVisible;

    private void Awake()
    {
        input = GetComponent<PlayerInputReader>();
        health = GetComponent<Health>();
        gameplayLocks = GetComponent<GameplayLockCoordinator>();
        Inventory = new InventoryState(capacity);
    }

    private void Update()
    {
        if (input == null || !input.InventoryPressed)
        {
            return;
        }

        if (IsOpen)
        {
            Close();
        }
        else
        {
            Open();
        }
    }

    private void OnDisable()
    {
        Close();
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

    public void RegisterItem(ItemDefinition definition)
    {
        if (definition == null ||
            string.IsNullOrWhiteSpace(definition.StableId))
        {
            return;
        }

        catalog[definition.StableId] = definition;
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

    public bool TryUse(int slotIndex)
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
            if (!effects.TryApply(definition, health))
            {
                view?.ShowMessage("当前无法使用该物品");
                return false;
            }

            bool removed = Inventory.TryRemoveAt(slotIndex, 1);

            if (removed)
            {
                view?.ShowMessage("医疗包使用成功");
            }

            return removed;
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
        view.Show(Inventory, ResolveDefinition, TryUse, Close);
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
