using System;
using UnityEngine;

public enum ItemType
{
    Consumable
}

public enum ItemEffectType
{
    RestoreHealth
}

[CreateAssetMenu(
    fileName = "ItemDefinition",
    menuName = "FPS/Inventory/Item Definition")]
public sealed class ItemDefinition : ScriptableObject
{
    [SerializeField] private string stableId;
    [SerializeField] private string displayName;
    [SerializeField, TextArea] private string description;
    [SerializeField] private Sprite icon;
    [SerializeField] private ItemType itemType = ItemType.Consumable;
    [SerializeField, Min(1)] private int maximumStack = 1;
    [SerializeField] private ItemEffectType effectType =
        ItemEffectType.RestoreHealth;
    [SerializeField, Min(0f)] private float effectAmount;

    public string StableId => stableId;
    public string DisplayName => displayName;
    public string Description => description;
    public Sprite Icon => icon;
    public ItemType ItemType => itemType;
    public int MaximumStack => Mathf.Max(1, maximumStack);
    public ItemEffectType EffectType => effectType;
    public float EffectAmount => Mathf.Max(0f, effectAmount);

    public InventoryItemSpec ToSpec()
    {
        return new InventoryItemSpec(StableId, MaximumStack);
    }

    public void Configure(
        string id,
        string itemName,
        string itemDescription,
        Sprite itemIcon,
        ItemType type,
        int maxStack,
        ItemEffectType configuredEffect,
        float configuredAmount)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException(
                "An item requires a stable ID.",
                nameof(id));
        }

        stableId = id.Trim();
        displayName = itemName ?? string.Empty;
        description = itemDescription ?? string.Empty;
        icon = itemIcon;
        itemType = type;
        maximumStack = Mathf.Max(1, maxStack);
        effectType = configuredEffect;
        effectAmount = Mathf.Max(0f, configuredAmount);
    }
}
