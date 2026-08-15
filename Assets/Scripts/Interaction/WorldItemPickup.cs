using System;
using UnityEngine;

public sealed class WorldItemPickup : MonoBehaviour, IInteractable
{
    private static readonly System.Collections.Generic.HashSet<WorldItemPickup>
        ActivePickups = new();
    [SerializeField] private ItemDefinition definition;
    [SerializeField, Min(1)] private int quantity = 1;

    private GameObject activeActor;
    private bool attempted;
    private string settlementFeedback;

    public bool IsClaimed { get; private set; }
    public int SettlementCount { get; private set; }
    public int InitialQuantity { get; private set; }
    public int RemainingQuantity => quantity;
    public int TotalAccepted { get; private set; }
    public int SpawnId { get; private set; }
    public WorldItemSource Source { get; private set; }
    public ItemDefinition Definition => definition;

    [RuntimeInitializeOnLoadMethod(
        RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetActiveRegistry()
    {
        ActivePickups.Clear();
    }

    public static void CollectActive(
        System.Collections.Generic.List<WorldItemPickup> destination)
    {
        if (destination == null)
        {
            throw new ArgumentNullException(nameof(destination));
        }

        destination.Clear();

        foreach (WorldItemPickup pickup in ActivePickups)
        {
            if (pickup != null && pickup.gameObject.activeInHierarchy)
            {
                destination.Add(pickup);
            }
        }
    }
    public InteractionView View
    {
        get
        {
            bool isAvailable =
                !IsClaimed && definition != null && quantity > 0;
            return new InteractionView(
                isAvailable
                    ? !string.IsNullOrEmpty(settlementFeedback)
                        ? settlementFeedback
                        : $"[F] 拾取 {definition.DisplayName} ×{quantity}"
                    : "物品已拾取",
                IsClaimed ? 1f : 0f,
                isAvailable,
                IsClaimed);
        }
    }

    public void Configure(ItemDefinition configuredDefinition, int count)
    {
        if (configuredDefinition == null)
        {
            throw new ArgumentNullException(nameof(configuredDefinition));
        }

        if (count <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        definition = configuredDefinition;
        quantity = count;
        InitialQuantity = count;
        activeActor = null;
        attempted = false;
        IsClaimed = false;
        SettlementCount = 0;
        TotalAccepted = 0;
        settlementFeedback = string.Empty;
    }

    public void ConfigureSpawnMetadata(
        int spawnId,
        WorldItemSource source)
    {
        SpawnId = Mathf.Max(0, spawnId);
        Source = source;
    }

    public bool TryBegin(GameObject actor)
    {
        if (actor == null || definition == null || quantity <= 0 ||
            IsClaimed || activeActor != null)
        {
            return false;
        }

        activeActor = actor;
        attempted = false;
        return true;
    }

    public bool Advance(GameObject actor, float deltaTime)
    {
        if (actor == null || actor != activeActor || attempted)
        {
            return false;
        }

        attempted = true;
        PlayerInventoryController inventory =
            actor.GetComponent<PlayerInventoryController>();
        InventoryAddResult result;

        try
        {
            result = inventory != null
                ? inventory.Add(definition, quantity)
                : new InventoryAddResult(quantity, 0);
        }
        finally
        {
            activeActor = null;
        }

        quantity = result.Remaining;

        if (result.Accepted <= 0 && quantity > 0)
        {
            settlementFeedback =
                $"背包空间不足，{definition.DisplayName} ×{quantity}仍在地面";
        }
        else if (quantity > 0)
        {
            settlementFeedback =
                $"已拾取 {result.Accepted}，剩余 {definition.DisplayName} ×{quantity}";
        }
        else
        {
            settlementFeedback = string.Empty;
        }

        if (result.Accepted > 0)
        {
            SettlementCount++;
            TotalAccepted += result.Accepted;
        }

        if (quantity <= 0)
        {
            IsClaimed = true;
            Collider pickupCollider = GetComponent<Collider>();

            if (pickupCollider != null)
            {
                pickupCollider.enabled = false;
            }

            gameObject.SetActive(false);
        }

        return true;
    }

    private void OnDisable()
    {
        ActivePickups.Remove(this);
        activeActor = null;
        attempted = false;
    }

    private void OnEnable()
    {
        ActivePickups.Add(this);
    }

    public bool Cancel(GameObject actor, InteractionCancelReason reason)
    {
        if (actor == null || actor != activeActor)
        {
            return false;
        }

        activeActor = null;
        attempted = false;
        return true;
    }
}
