using UnityEngine;

public sealed class WorldItemPickup : MonoBehaviour, IInteractable
{
    [SerializeField] private ItemDefinition definition;
    [SerializeField, Min(1)] private int quantity = 1;

    private GameObject activeActor;
    private bool attempted;

    public bool IsClaimed { get; private set; }
    public int SettlementCount { get; private set; }
    public int RemainingQuantity => quantity;
    public int TotalAccepted { get; private set; }
    public ItemDefinition Definition => definition;
    public InteractionView View => new(
        IsClaimed
            ? "物品已拾取"
            : $"[E] 拾取 {definition?.DisplayName ?? "物品"}",
        IsClaimed ? 1f : 0f,
        !IsClaimed,
        IsClaimed);

    public void Configure(ItemDefinition configuredDefinition, int count)
    {
        definition = configuredDefinition;
        quantity = Mathf.Max(1, count);
        activeActor = null;
        attempted = false;
        IsClaimed = false;
        SettlementCount = 0;
        TotalAccepted = 0;
    }

    public bool TryBegin(GameObject actor)
    {
        if (actor == null || definition == null || IsClaimed ||
            activeActor != null)
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
        InventoryAddResult result = inventory != null
            ? inventory.Add(definition, quantity)
            : new InventoryAddResult(quantity, 0);
        quantity = result.Remaining;
        activeActor = null;

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
