using System;
using System.Collections.Generic;
using FPS.Networking.Domain;
using FPS.Networking.Netcode;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Composition adapter: reuse game views, never the local economy controllers.</summary>
public sealed class CoopEconomyPresentationAdapter : MonoBehaviour,
    ICoopEconomyPresentation
{
    private readonly Dictionary<string, ItemDefinition> items = new(StringComparer.Ordinal);
    private readonly Dictionary<string, UpgradeDefinition> upgrades = new(StringComparer.Ordinal);
    private readonly List<WorldPickupListEntry> pickupEntries = new();
    private readonly ServerProgressionProjection progress = new();
    private UnifiedGameHud hud;
    private InventoryView inventoryView;
    private UpgradeChoiceView upgradeView;
    private WorldPickupListHud pickupView;
    private InventoryState inventory;
    private PlayerInputReader input;
    private Func<CoopEconomyIntent, bool> submit;
    private Action close;
    private CoopEconomyPresentationFrame lastFrame;
    private CanvasGroup upgradeCanvas;
    private CanvasGroup inventoryCanvas;
    private TMP_Text upgradeStatus;
    private GameObject pickupOwner;
    private GameObject[] unsupportedBindings = Array.Empty<GameObject>();
    private int presentedChoiceGeneration = -1;
    private int presentedRunGeneration = -1;
    private bool inventoryWasVisible;
    private bool disposed;

    public bool IsReady => EnsureViews();
    public bool PickupPressed => input != null ? input.PickupPressed
        : Keyboard.current != null && Keyboard.current.fKey.wasPressedThisFrame;
    public InventoryView InventoryView => inventoryView;
    public UpgradeChoiceView UpgradeView => upgradeView;
    public WorldPickupListHud PickupView => pickupView;
    public InventoryState InventoryProjection => inventory;

    public void Configure(Func<CoopEconomyIntent, bool> commandSubmitter,
        Action closeHandler)
    {
        submit = commandSubmitter;
        close = closeHandler;
        LoadDefinitions();
    }

    public int ConsumePickupScroll()
    {
        ResolveInput();
        return input != null ? input.ConsumeWeaponCycleDirection() : 0;
    }

    public void Present(CoopEconomyPresentationFrame frame)
    {
        if (disposed || frame == null || !EnsureViews()) return;
        lastFrame = frame;
        progress.Set(frame.Progression);
        MirrorInventory(frame);
        if (presentedRunGeneration != frame.RunGeneration)
        {
            presentedRunGeneration = frame.RunGeneration;
            presentedChoiceGeneration = -1;
            upgradeView.Hide();
            inventoryView.Hide();
            inventoryWasVisible = false;
        }

        bool choosing = frame.Progression.PendingUpgradeChoices > 0;
        bool showInventory = frame.InventoryVisible && !choosing;
        if (showInventory && !inventoryWasVisible)
        {
            inventoryView.Show(inventory, ResolveItem, Availability,
                slot => Send(new CoopEconomyIntent(AuthoritativeEconomyCommandKind.Use,
                    sourceSlot: slot)),
                (source, destination) => Operation(
                    new CoopEconomyIntent(AuthoritativeEconomyCommandKind.Transfer,
                        sourceSlot: source, destinationSlot: destination),
                    InventoryOperationKind.Move),
                () => Operation(new CoopEconomyIntent(AuthoritativeEconomyCommandKind.Compact),
                    InventoryOperationKind.Compact),
                (source, destination, quantity) => Operation(
                    new CoopEconomyIntent(AuthoritativeEconomyCommandKind.Split,
                        sourceSlot: source, destinationSlot: destination, quantity: quantity),
                    InventoryOperationKind.Split),
                (source, quantity) => Send(new CoopEconomyIntent(
                    AuthoritativeEconomyCommandKind.Drop, sourceSlot: source, quantity: quantity)),
                // Quick-slot bindings are not part of the server economy protocol.
                // Disable the operation instead of silently writing local state.
                (_, _) => false, () => close?.Invoke());
            inventoryCanvas = hud.ModalLayer.Find("InventoryPanel")?.GetComponent<CanvasGroup>();
        }
        else if (!showInventory && inventoryWasVisible)
            inventoryView.Hide();
        inventoryWasVisible = showInventory;

        if (!choosing)
        {
            upgradeView.Hide();
            presentedChoiceGeneration = -1;
        }
        else if (!upgradeView.IsVisible || frame.RetryAfterRejection ||
                 presentedChoiceGeneration != frame.Progression.ChoiceGeneration)
            ShowServerCandidates(frame);

        // SetSuspended also changes EventSystem focus. Calling it every frame
        // would continually select the first card/slot and defeat navigation.
        if (upgradeView.IsSuspended != frame.Suspended)
            upgradeView.SetSuspended(frame.Suspended);
        if (inventoryView.IsSuspended != frame.Suspended)
            inventoryView.SetSuspended(frame.Suspended);
        ApplyPendingPresentation(frame);
        pickupEntries.Clear();
        if (!frame.Suspended && !choosing && !showInventory && frame.NearbyDrops != null)
        {
            foreach (NetcodeWorldDropState drop in frame.NearbyDrops)
            {
                string id = drop.ItemId.ToString();
                pickupEntries.Add(new WorldPickupListEntry(
                    ResolveItem(id)?.DisplayName ?? id, drop.Quantity));
            }
        }
        pickupView.RefreshEntries(pickupEntries, frame.SelectedDropIndex);
    }

    private void LateUpdate()
    {
        // InventoryView's single-player callback messages run during EventSystem
        // update. Replace optimistic wording with the actual network request state.
        if (lastFrame != null && !disposed) ApplyPendingPresentation(lastFrame);
    }

    private void ApplyPendingPresentation(CoopEconomyPresentationFrame frame)
    {
        bool interactable = !frame.Pending && !frame.Suspended;
        if (inventoryCanvas != null) inventoryCanvas.interactable = interactable;
        if (upgradeCanvas != null) upgradeCanvas.interactable = interactable;
        if (inventoryWasVisible) inventoryView.ShowMessage(frame.Status);
        if (upgradeStatus != null)
        {
            upgradeStatus.text = frame.Status;
            upgradeStatus.gameObject.SetActive(upgradeView.IsVisible && !frame.Suspended &&
                !string.IsNullOrWhiteSpace(frame.Status));
        }
    }

    private void ShowServerCandidates(CoopEconomyPresentationFrame frame)
    {
        string[] ids = { frame.Progression.Candidate0.ToString(),
            frame.Progression.Candidate1.ToString(), frame.Progression.Candidate2.ToString() };
        var candidates = new List<UpgradeDefinition>();
        var indices = new List<int>();
        for (int index = 0; index < ids.Length; index++)
        {
            if (string.IsNullOrEmpty(ids[index])) continue;
            if (!upgrades.TryGetValue(ids[index], out UpgradeDefinition definition))
            {
                upgradeView.Hide();
                Debug.LogError($"联机升级显示资源缺失：{ids[index]}", this);
                return;
            }
            candidates.Add(definition);
            indices.Add(index);
        }
        if (candidates.Count == 0) return;
        // This detached state is used only for card level labels. It never applies
        // GameplayEffects or modifies the player; the input bridge projects stats.
        var displayState = new RunUpgradeState();
        if (frame.Upgrades != null)
        {
            foreach (NetcodeUpgradeStackState stack in frame.Upgrades)
            {
                if (!upgrades.TryGetValue(stack.UpgradeId.ToString(), out UpgradeDefinition definition))
                    continue;
                for (int level = 0; level < Math.Min(stack.Level, definition.MaximumLevel); level++)
                    displayState.TryApply(definition);
            }
        }
        presentedChoiceGeneration = frame.Progression.ChoiceGeneration;
        upgradeView.Show(candidates, displayState,
            selected => Send(new CoopEconomyIntent(
                AuthoritativeEconomyCommandKind.SelectUpgrade,
                candidateIndex: indices[selected])));
        upgradeCanvas = hud.ModalLayer.Find("UpgradeChoicePanel")?.GetComponent<CanvasGroup>();
    }

    private void MirrorInventory(CoopEconomyPresentationFrame frame)
    {
        int capacity = frame.Inventory != null && frame.Inventory.Count > 0 ? 1 : 12;
        if (frame.Inventory != null)
            foreach (NetcodeInventorySlotState slot in frame.Inventory)
                capacity = Math.Max(capacity, slot.SlotIndex + 1);
        if (inventory == null || inventory.Capacity != capacity)
        {
            inventoryView.Hide();
            inventoryWasVisible = false;
            inventory = new InventoryState(capacity);
        }
        var slots = new InventorySlot[capacity];
        if (frame.Inventory != null)
        {
            foreach (NetcodeInventorySlotState slot in frame.Inventory)
            {
                if (slot.SlotIndex < 0 || slot.SlotIndex >= capacity || slot.Quantity <= 0) continue;
                slots[slot.SlotIndex] = new InventorySlot(slot.ItemId.ToString(),
                    Math.Max(slot.MaximumStack, slot.Quantity), slot.Quantity);
            }
        }
        inventory.RestorePrepared(slots);
        if (inventoryWasVisible) inventoryView.RefreshRuntimeState();
    }

    private ItemUseResult Availability(int slot)
    {
        if (lastFrame == null || lastFrame.Pending)
            return ItemUseResult.Failure(ItemUseFailureReason.CooldownActive);
        return inventory.GetSlot(slot).IsEmpty ?
            ItemUseResult.Failure(ItemUseFailureReason.InvalidItem) : ItemUseResult.Available;
    }

    private bool Send(CoopEconomyIntent intent)
    {
        if (disposed || submit == null || lastFrame?.Pending == true || !submit(intent))
            return false;
        if (lastFrame != null)
        {
            lastFrame.Pending = true;
            lastFrame.Status = "正在等待服务器确认……";
            ApplyPendingPresentation(lastFrame);
        }
        return true;
    }

    private InventoryOperationResult Operation(CoopEconomyIntent intent, InventoryOperationKind kind) =>
        Send(intent) ? InventoryOperationResult.Success(kind)
            : InventoryOperationResult.Failed(InventoryOperationFailure.NoChange);

    private ItemDefinition ResolveItem(string id) =>
        items.TryGetValue(id, out ItemDefinition definition) ? definition : null;

    private void LoadDefinitions()
    {
        items.Clear();
        upgrades.Clear();
        foreach (ItemDefinition definition in Resources.LoadAll<ItemDefinition>("Content/CityNew/Items"))
            if (definition != null) items[definition.StableId] = definition;
        foreach (UpgradeDefinition definition in Resources.LoadAll<UpgradeDefinition>("Content/CityNew/Upgrades"))
            if (definition != null) upgrades[definition.StableId] = definition;
    }

    private bool EnsureViews()
    {
        if (disposed) return false;
        if (hud != null && hud.ModalLayer != null && inventoryView != null &&
            upgradeView != null && pickupView != null) return true;
        hud = FindFirstObjectByType<UnifiedGameHud>();
        if (hud == null || hud.ModalLayer == null || hud.HudLayer == null) return false;
        ReleaseAuxiliaryViews();
        inventoryView = hud.GetComponent<InventoryView>() ?? hud.gameObject.AddComponent<InventoryView>();
        upgradeView = hud.GetComponent<UpgradeChoiceView>() ?? hud.gameObject.AddComponent<UpgradeChoiceView>();
        inventoryView.Initialize(hud.ModalLayer);
        upgradeView.Initialize(hud.ModalLayer);
        CacheUnsupportedBindings();
        pickupOwner = new GameObject("Coop Pickup Presentation");
        pickupOwner.transform.SetParent(hud.transform, false);
        pickupView = pickupOwner.AddComponent<WorldPickupListHud>();
        pickupView.Initialize(hud.HudLayer);
        Transform upgradePanel = hud.ModalLayer.Find("UpgradeChoicePanel");
        TMP_FontAsset statusFont = upgradePanel.GetComponentInChildren<TMP_Text>(true)?.font;
        upgradeStatus = ModeUiFactory.CreateText("Coop Upgrade Confirmation", upgradePanel,
            string.Empty, 18f, TextAlignmentOptions.Center, statusFont, TacticalUiTheme.Cyan);
        ModeUiFactory.SetRect(upgradeStatus.rectTransform, new Vector2(0.5f, 0f),
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(1000f, 48f),
            new Vector2(0f, 70f));
        upgradeStatus.gameObject.SetActive(false);
        hud.BindProgression(progress);
        ResolveInput();
        return true;
    }

    private void CacheUnsupportedBindings()
    {
        Transform panel = hud.ModalLayer.Find("InventoryPanel");
        var bindings = new List<GameObject>();
        if (panel != null)
        {
            foreach (UnityEngine.UI.Button button in
                     panel.GetComponentsInChildren<UnityEngine.UI.Button>(true))
                if (button.name == "绑定 [4]" || button.name == "绑定 [5]")
                {
                    bindings.Add(button.gameObject);
                    button.gameObject.SetActive(false);
                }
        }
        unsupportedBindings = bindings.ToArray();
    }

    private void ReleaseAuxiliaryViews()
    {
        pickupView?.Hide();
        if (upgradeStatus != null) upgradeStatus.gameObject.SetActive(false);
        if (pickupOwner != null) Destroy(pickupOwner);
        if (upgradeStatus != null) Destroy(upgradeStatus.gameObject);
        pickupOwner = null;
        pickupView = null;
        upgradeStatus = null;
    }

    private void ResolveInput()
    {
        if (input == null) input = FindFirstObjectByType<PlayerInputReader>();
    }

    public void Hide()
    {
        lastFrame = null;
        inventoryView?.Hide();
        upgradeView?.Hide();
        pickupView?.Hide();
        if (upgradeStatus != null) upgradeStatus.gameObject.SetActive(false);
        inventoryWasVisible = false;
        presentedChoiceGeneration = -1;
    }

    public void Dispose()
    {
        if (disposed) return;
        Hide();
        disposed = true;
        if (hud != null) hud.UnbindProgression();
        ReleaseAuxiliaryViews();
        RestoreBindingButtons();
        submit = null;
        close = null;
        Destroy(this);
    }

    private void OnDestroy()
    {
        if (disposed) return;
        Hide();
        ReleaseAuxiliaryViews();
        RestoreBindingButtons();
        if (hud != null) hud.UnbindProgression();
    }

    private void RestoreBindingButtons()
    {
        foreach (GameObject binding in unsupportedBindings)
            if (binding != null) binding.SetActive(true);
        unsupportedBindings = Array.Empty<GameObject>();
    }

    private sealed class ServerProgressionProjection : IRunProgressionSource
    {
        public event Action<RunExperienceSnapshot> ProgressChanged;
        public RunExperienceSnapshot CurrentProgress { get; private set; } =
            new(1, 0, 100, 0, 0);

        public void Set(NetcodeProgressionState value)
        {
            if (CurrentProgress.Level == value.Level &&
                CurrentProgress.TotalExperience == value.TotalExperience &&
                CurrentProgress.CurrentExperience == value.CurrentExperience &&
                CurrentProgress.ExperienceToNextLevel == value.ExperienceToNextLevel) return;
            CurrentProgress = new RunExperienceSnapshot(value.Level, value.CurrentExperience,
                value.ExperienceToNextLevel, value.TotalExperience, Math.Max(0, value.Level - 1));
            ProgressChanged?.Invoke(CurrentProgress);
        }
    }
}

public static class CoopEconomyPresentationRegistration
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    public static void Register()
    {
        CoopEconomyPresentationRegistry.Register((owner, submit, close) =>
        {
            var adapter = owner.GetComponent<CoopEconomyPresentationAdapter>() ??
                owner.AddComponent<CoopEconomyPresentationAdapter>();
            adapter.Configure(submit, close);
            return adapter;
        }, (parent, itemId) =>
        {
            var root = new GameObject("Replicated Item Package");
            root.transform.SetParent(parent, false);
            var visual = root.AddComponent<ReplicatedWorldItemVisual>();
            ItemEffectType effect = itemId switch
            {
                "armor_pack" or "armor_plate" => ItemEffectType.RestoreArmor,
                "rifle_ammo" => ItemEffectType.AddRifleAmmo,
                "handgun_ammo" => ItemEffectType.AddHandgunAmmo,
                _ => ItemEffectType.RestoreHealth
            };
            visual.Build(effect);
            return root;
        });
    }
}
