using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(100)]
[RequireComponent(typeof(PlayerInputReader), typeof(PlayerController))]
public sealed class PlayerWorldPickupController : MonoBehaviour
{
    private const float ScrollGestureReleaseDelay = 0.14f;
    [SerializeField, Min(0.5f)] private float pickupRange = 3f;
    [SerializeField] private LayerMask pickupMask = ~0;

    private readonly RaycastHit[] visibilityHits = new RaycastHit[40];
    private readonly List<WorldItemPickup> activePickups = new();
    private readonly List<WorldItemPickup> nearbyPickups = new();
    private PlayerInputReader input;
    private PlayerController player;
    private PlayerInteractionController interactions;
    private WorldPickupListHud view;
    private WorldItemPickup selectedPickup;
    private int selectedIndex = -1;
    private bool scrollInputOwned;
    private float lastScrollSignalTime;

    public int NearbyPickupCount => nearbyPickups.Count;
    public int SelectedIndex => selectedIndex;
    public WorldItemPickup SelectedPickup => selectedPickup;
    public WorldPickupListHud View => view;
    public IReadOnlyList<WorldItemPickup> NearbyPickups => nearbyPickups;
    public int ScrollSelectionCount { get; private set; }

    private void Awake()
    {
        input = GetComponent<PlayerInputReader>();
        player = GetComponent<PlayerController>();
        interactions = GetComponent<PlayerInteractionController>();
    }

    private IEnumerator Start()
    {
        while (view == null && isActiveAndEnabled)
        {
            UnifiedGameHud hud =
                Object.FindAnyObjectByType<UnifiedGameHud>();

            if (hud != null && hud.HudLayer != null)
            {
                view = hud.GetComponent<WorldPickupListHud>();
                view ??= hud.gameObject.AddComponent<WorldPickupListHud>();
                view.Initialize(hud.HudLayer);
                break;
            }

            yield return null;
        }
    }

    private void Update()
    {
        if (!CanProcessPickupInput())
        {
            ClearNearbyPickups();
            return;
        }

        RefreshNearbyPickups();

        if (scrollInputOwned)
        {
            int ownedDirection = input.ConsumeWeaponCycleDirection();
            ProcessScrollSelectionInput(
                ownedDirection,
                Time.unscaledTime);

            if (nearbyPickups.Count == 0)
            {
                return;
            }
        }

        if (nearbyPickups.Count == 0)
        {
            return;
        }

        if (!scrollInputOwned)
        {
            int direction = input.ConsumeWeaponCycleDirection();
            ProcessScrollSelectionInput(direction, Time.unscaledTime);
        }

        if (input.PickupPressed)
        {
            TryPickupSelected();
        }
    }

    public void ConfigureRange(float configuredRange)
    {
        pickupRange = Mathf.Max(0.5f, configuredRange);
    }

    public void RefreshNearbyPickups()
    {
        WorldItemPickup.CollectActive(activePickups);
        nearbyPickups.Clear();

        for (int index = 0; index < activePickups.Count; index++)
        {
            WorldItemPickup pickup = activePickups[index];

            if (!IsPickupReachable(pickup))
            {
                continue;
            }

            nearbyPickups.Add(pickup);
        }

        nearbyPickups.Sort(ComparePickups);
        RestoreSelection();
        view?.Refresh(nearbyPickups, selectedIndex);
    }

    public bool SelectNext(int direction)
    {
        if (nearbyPickups.Count == 0 || direction == 0)
        {
            return false;
        }

        int step = direction > 0 ? -1 : 1;
        selectedIndex =
            (selectedIndex + step + nearbyPickups.Count) %
            nearbyPickups.Count;
        selectedPickup = nearbyPickups[selectedIndex];
        view?.Refresh(nearbyPickups, selectedIndex);
        return true;
    }

    public bool ProcessScrollSelectionInput(
        int direction,
        float unscaledTime)
    {
        if (direction == 0)
        {
            if (scrollInputOwned &&
                unscaledTime - lastScrollSignalTime >=
                ScrollGestureReleaseDelay)
            {
                scrollInputOwned = false;
            }

            return false;
        }

        lastScrollSignalTime = unscaledTime;
        scrollInputOwned = true;

        if (!SelectNext(direction))
        {
            return false;
        }

        ScrollSelectionCount++;
        return true;
    }

    public bool TryPickupSelected()
    {
        WorldItemPickup pickup = selectedPickup;

        if (pickup == null || !IsPickupReachable(pickup) ||
            !pickup.TryBegin(gameObject))
        {
            RefreshNearbyPickups();
            return false;
        }

        bool resolved = pickup.Advance(gameObject, 0f);
        RefreshNearbyPickups();
        return resolved;
    }

    public bool IsPickupReachable(WorldItemPickup pickup)
    {
        if (pickup == null || !pickup.gameObject.activeInHierarchy ||
            pickup.gameObject.scene != gameObject.scene ||
            !pickup.View.IsAvailable)
        {
            return false;
        }

        Vector3 origin = player != null && player.AimCamera != null
            ? player.AimCamera.transform.position
            : transform.position + Vector3.up * 1.2f;
        Collider pickupCollider = pickup.GetComponent<Collider>();
        Vector3 target = pickupCollider != null
            ? pickupCollider.bounds.center
            : pickup.transform.position;
        float distance = Vector3.Distance(transform.position, target);

        if (distance > pickupRange)
        {
            return false;
        }

        Vector3 direction = target - origin;
        float sightDistance = direction.magnitude;

        if (sightDistance <= 0.01f)
        {
            return true;
        }

        int hitCount = Physics.RaycastNonAlloc(
            origin,
            direction / sightDistance,
            visibilityHits,
            sightDistance + 0.08f,
            pickupMask,
            QueryTriggerInteraction.Ignore);
        float nearestDistance = float.PositiveInfinity;
        Collider nearestCollider = null;

        for (int index = 0; index < hitCount; index++)
        {
            Collider hit = visibilityHits[index].collider;

            if (hit == null || hit.transform == transform ||
                hit.transform.IsChildOf(transform))
            {
                continue;
            }

            WorldItemPickup hitPickup =
                hit.GetComponentInParent<WorldItemPickup>();

            if (hitPickup != null && hitPickup != pickup)
            {
                continue;
            }

            if (visibilityHits[index].distance < nearestDistance)
            {
                nearestDistance = visibilityHits[index].distance;
                nearestCollider = hit;
            }
        }

        return nearestCollider == null ||
               nearestCollider.GetComponentInParent<WorldItemPickup>() ==
               pickup;
    }

    private bool CanProcessPickupInput()
    {
        return input != null && player != null &&
               player.GameplayInputEnabled && !player.IsPaused &&
               Time.timeScale > 0f &&
               (interactions == null || !interactions.IsInteracting);
    }

    private void RestoreSelection()
    {
        if (nearbyPickups.Count == 0)
        {
            selectedPickup = null;
            selectedIndex = -1;
            return;
        }

        int preservedIndex = selectedPickup != null
            ? nearbyPickups.IndexOf(selectedPickup)
            : -1;
        selectedIndex = preservedIndex >= 0
            ? preservedIndex
            : Mathf.Clamp(selectedIndex, 0, nearbyPickups.Count - 1);
        selectedPickup = nearbyPickups[selectedIndex];
    }

    private int ComparePickups(
        WorldItemPickup left,
        WorldItemPickup right)
    {
        float leftDistance =
            (left.transform.position - transform.position).sqrMagnitude;
        float rightDistance =
            (right.transform.position - transform.position).sqrMagnitude;
        int distanceComparison = leftDistance.CompareTo(rightDistance);

        if (distanceComparison != 0)
        {
            return distanceComparison;
        }

        int spawnComparison = left.SpawnId.CompareTo(right.SpawnId);
        return spawnComparison != 0
            ? spawnComparison
            : string.CompareOrdinal(
                left.GetEntityId().ToString(),
                right.GetEntityId().ToString());
    }

    private void ClearNearbyPickups()
    {
        nearbyPickups.Clear();
        activePickups.Clear();
        selectedPickup = null;
        selectedIndex = -1;
        scrollInputOwned = false;
        lastScrollSignalTime = 0f;
        view?.Hide();
    }

    private void OnDisable()
    {
        ClearNearbyPickups();
    }
}
