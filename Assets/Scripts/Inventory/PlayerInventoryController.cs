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
    private WeaponLoadoutController loadout;
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
        loadout = GetComponent<WeaponLoadoutController>();
        gameplayLocks = GetComponent<GameplayLockCoordinator>();
        Inventory = new InventoryState(capacity);
    }

    private void Start()
    {
        if (health != null)
        {
            health.VitalsChanged += HandleRuntimeStateChanged;
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
                weapon.AmmoChanged += HandleRuntimeStateChanged;
            }
        }
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

    private void OnDestroy()
    {
        if (health != null)
        {
            health.VitalsChanged -= HandleRuntimeStateChanged;
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
            (existing.MaximumStack != definition.MaximumStack ||
             existing.EffectType != definition.EffectType))
        {
            throw new InvalidOperationException(
                $"Item ID '{definition.StableId}' has conflicting definitions.");
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

        return effects.Evaluate(
            definition,
            new ItemUseContext(health, loadout));
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
            ItemUseResult result = effects.Apply(
                definition,
                new ItemUseContext(health, loadout));

            if (!result.Succeeded)
            {
                view?.ShowMessage(
                    ItemUsePresentation.GetFailureText(
                        result.FailureReason));
                return false;
            }

            bool removed = Inventory.TryRemoveAt(slotIndex, 1);

            if (removed)
            {
                view?.ShowMessage(
                    GetSuccessMessage(definition.EffectType));
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
        view.Show(
            Inventory,
            ResolveDefinition,
            GetUseAvailability,
            TryUse,
            Close);
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
