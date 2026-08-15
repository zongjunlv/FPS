using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public enum WorldItemSource
{
    InitialScene,
    PlayerDrop,
    EnemyDrop
}

public sealed class WorldItemDropHandle : IDisposable
{
    private WorldItemFactory owner;
    private GameObject stagedObject;

    internal WorldItemDropHandle(
        WorldItemFactory factory,
        GameObject worldObject,
        WorldItemPickup pickup)
    {
        owner = factory;
        stagedObject = worldObject;
        Pickup = pickup;
    }

    public WorldItemPickup Pickup { get; }
    public bool IsCommitted { get; private set; }

    public WorldItemPickup Commit()
    {
        if (IsCommitted || stagedObject == null || owner == null)
        {
            return null;
        }

        IsCommitted = true;
        stagedObject.SetActive(true);
        owner.RegisterCommitted(Pickup);
        stagedObject = null;
        owner = null;
        return Pickup;
    }

    public void Dispose()
    {
        if (IsCommitted || stagedObject == null)
        {
            return;
        }

        UnityEngine.Object.Destroy(stagedObject);
        stagedObject = null;
        owner = null;
    }
}

public sealed class WorldItemFactory : MonoBehaviour
{
    private const float DropDistance = 1.65f;
    [SerializeField] private LayerMask groundMask = ~0;
    [SerializeField] private LayerMask obstacleMask = ~0;
    private static readonly float[] CandidateAngles =
    {
        0f, -42f, 42f, -78f, 78f, 180f
    };
    private static readonly float[] RewardCandidateRadii =
    {
        0.85f, 1.35f, 1.9f, 2.4f
    };

    private readonly Dictionary<ItemEffectType, Material> bodyMaterials =
        new();
    private readonly Dictionary<ItemEffectType, Material> markMaterials =
        new();
    private readonly List<WorldItemPickup> committedPickups = new();
    private readonly RaycastHit[] groundHits = new RaycastHit[24];
    private readonly Collider[] overlapHits = new Collider[24];
    private int nextSpawnId;

    public WorldItemPickup LastSpawnedPickup { get; private set; }
    public int ActivePickupCount
    {
        get
        {
            int count = 0;

            for (int index = committedPickups.Count - 1; index >= 0; index--)
            {
                WorldItemPickup pickup = committedPickups[index];

                if (pickup == null)
                {
                    committedPickups.RemoveAt(index);
                }
                else if (!pickup.IsClaimed && pickup.gameObject.activeSelf)
                {
                    count++;
                }
            }

            return count;
        }
    }

    public bool TryPrepareDrop(
        ItemDefinition definition,
        int quantity,
        out WorldItemDropHandle handle)
    {
        handle = null;

        if (definition == null || quantity <= 0 ||
            !TryFindDropPosition(out Vector3 position))
        {
            return false;
        }

        GameObject worldObject = CreateWorldObject(
            definition,
            quantity,
            position,
            WorldItemSource.PlayerDrop,
            ++nextSpawnId);
        WorldItemPickup pickup = worldObject.GetComponent<WorldItemPickup>();
        handle = new WorldItemDropHandle(this, worldObject, pickup);
        return true;
    }

    public bool TrySpawnRewardDrop(
        ItemDefinition definition,
        int quantity,
        Vector3 desiredOrigin,
        Transform ignoredRoot,
        out WorldItemPickup pickup)
    {
        pickup = null;

        if (definition == null || quantity <= 0 ||
            !TryFindRewardDropPosition(
                desiredOrigin,
                ignoredRoot,
                out Vector3 position))
        {
            return false;
        }

        GameObject worldObject = CreateWorldObject(
            definition,
            quantity,
            position,
            WorldItemSource.EnemyDrop,
            ++nextSpawnId);
        pickup = worldObject.GetComponent<WorldItemPickup>();
        worldObject.SetActive(true);
        RegisterCommitted(pickup);
        return true;
    }

    internal void RegisterCommitted(WorldItemPickup pickup)
    {
        if (pickup == null)
        {
            return;
        }

        committedPickups.Add(pickup);
        LastSpawnedPickup = pickup;
    }

    private void OnDestroy()
    {
        foreach (Material material in bodyMaterials.Values)
        {
            if (material != null)
            {
                Destroy(material);
            }
        }

        foreach (Material material in markMaterials.Values)
        {
            if (material != null)
            {
                Destroy(material);
            }
        }
    }

    private bool TryFindDropPosition(out Vector3 position)
    {
        Vector3 forward = ResolveHorizontalForward();

        for (int index = 0; index < CandidateAngles.Length; index++)
        {
            Vector3 direction = Quaternion.AngleAxis(
                CandidateAngles[index],
                Vector3.up) * forward;
            Vector3 candidate = transform.position + direction * DropDistance;

            if (!TryFindGround(candidate, out RaycastHit ground) ||
                Vector3.Dot(ground.normal, Vector3.up) < 0.7f ||
                Mathf.Abs(ground.point.y - transform.position.y) > 1.25f)
            {
                continue;
            }

            Vector3 resolved = ground.point + Vector3.up * 0.24f;

            if (IsDropPathClear(resolved) &&
                IsDropSpaceClear(resolved, direction))
            {
                position = resolved;
                return true;
            }
        }

        position = default;
        return false;
    }

    private bool TryFindRewardDropPosition(
        Vector3 origin,
        Transform ignoredRoot,
        out Vector3 position)
    {
        Vector3 forward = ResolveHorizontalForward();

        for (int radiusIndex = 0;
             radiusIndex < RewardCandidateRadii.Length;
             radiusIndex++)
        {
            float radius = RewardCandidateRadii[radiusIndex];

            for (int angleIndex = 0;
                 angleIndex < CandidateAngles.Length;
                 angleIndex++)
            {
                Vector3 direction = Quaternion.AngleAxis(
                    CandidateAngles[angleIndex],
                    Vector3.up) * forward;
                Vector3 candidate = origin + direction * radius;

                if (!TryFindGround(
                        candidate,
                        ignoredRoot,
                        out RaycastHit ground) ||
                    Vector3.Dot(ground.normal, Vector3.up) < 0.7f ||
                    Mathf.Abs(ground.point.y - origin.y) > 2.5f ||
                    !IsNavMeshReachable(ground.point))
                {
                    continue;
                }

                Vector3 resolved = ground.point + Vector3.up * 0.24f;

                if (IsDropSpaceClear(resolved, direction, ignoredRoot))
                {
                    position = resolved;
                    return true;
                }
            }
        }

        position = default;
        return false;
    }

    private Vector3 ResolveHorizontalForward()
    {
        PlayerController player = GetComponent<PlayerController>();
        Vector3 forward = player != null && player.AimCamera != null
            ? player.AimCamera.transform.forward
            : transform.forward;
        forward = Vector3.ProjectOnPlane(forward, Vector3.up);

        if (forward.sqrMagnitude <= 0.001f)
        {
            forward = Vector3.ProjectOnPlane(
                transform.forward,
                Vector3.up);
        }

        return forward.sqrMagnitude > 0.001f
            ? forward.normalized
            : Vector3.forward;
    }

    private bool TryFindGround(Vector3 candidate, out RaycastHit ground)
    {
        return TryFindGround(candidate, null, out ground);
    }

    private bool TryFindGround(
        Vector3 candidate,
        Transform ignoredRoot,
        out RaycastHit ground)
    {
        int count = Physics.RaycastNonAlloc(
            candidate + Vector3.up * 2.5f,
            Vector3.down,
            groundHits,
            6f,
            groundMask,
            QueryTriggerInteraction.Ignore);
        float nearest = float.PositiveInfinity;
        ground = default;

        for (int index = 0; index < count; index++)
        {
            RaycastHit hit = groundHits[index];

            if (hit.collider == null ||
                hit.collider.transform.IsChildOf(transform) ||
                IsWithin(hit.collider.transform, ignoredRoot) ||
                hit.collider.GetComponentInParent<WorldItemPickup>() != null ||
                !IsStableGround(hit.collider) ||
                hit.distance >= nearest)
            {
                continue;
            }

            nearest = hit.distance;
            ground = hit;
        }

        return nearest < float.PositiveInfinity;
    }

    private bool IsDropPathClear(Vector3 center)
    {
        Vector3 origin = transform.position + Vector3.up * 0.65f;
        Vector3 target = center + Vector3.up * 0.34f;
        Vector3 direction = target - origin;
        float distance = direction.magnitude;

        if (distance <= 0.001f)
        {
            return true;
        }

        int count = Physics.RaycastNonAlloc(
            origin,
            direction / distance,
            groundHits,
            distance,
            obstacleMask,
            QueryTriggerInteraction.Ignore);

        for (int index = 0; index < count; index++)
        {
            Collider collider = groundHits[index].collider;

            if (collider == null ||
                collider.transform.IsChildOf(transform))
            {
                continue;
            }

            return false;
        }

        return count < groundHits.Length;
    }

    private bool IsDropSpaceClear(Vector3 center, Vector3 forward)
    {
        return IsDropSpaceClear(center, forward, null);
    }

    private bool IsDropSpaceClear(
        Vector3 center,
        Vector3 forward,
        Transform ignoredRoot)
    {
        Quaternion rotation = Quaternion.LookRotation(-forward, Vector3.up);
        int count = Physics.OverlapBoxNonAlloc(
            center,
            new Vector3(0.37f, 0.23f, 0.27f),
            overlapHits,
            rotation,
            obstacleMask,
            QueryTriggerInteraction.Ignore);

        for (int index = 0; index < count; index++)
        {
            Collider collider = overlapHits[index];

            if (collider == null ||
                collider.transform.IsChildOf(transform) ||
                IsWithin(collider.transform, ignoredRoot))
            {
                continue;
            }

            return false;
        }

        return count < overlapHits.Length;
    }

    private bool IsNavMeshReachable(Vector3 destination)
    {
        if (!NavMesh.SamplePosition(
                transform.position,
                out NavMeshHit start,
                2.5f,
                NavMesh.AllAreas) ||
            !NavMesh.SamplePosition(
                destination,
                out NavMeshHit end,
                1.5f,
                NavMesh.AllAreas))
        {
            return false;
        }

        var path = new NavMeshPath();
        return NavMesh.CalculatePath(
                   start.position,
                   end.position,
                   NavMesh.AllAreas,
                   path) &&
               path.status == NavMeshPathStatus.PathComplete;
    }

    private static bool IsWithin(Transform candidate, Transform root)
    {
        return candidate != null && root != null &&
               (candidate == root || candidate.IsChildOf(root));
    }

    private static bool IsStableGround(Collider collider)
    {
        if (collider == null ||
            collider.GetComponentInParent<CharacterController>() != null ||
            collider.GetComponentInParent<EnemyController>() != null)
        {
            return false;
        }

        return collider.attachedRigidbody == null;
    }

    private GameObject CreateWorldObject(
        ItemDefinition definition,
        int quantity,
        Vector3 position,
        WorldItemSource source,
        int spawnId)
    {
        GameObject worldObject = GameObject.CreatePrimitive(
            PrimitiveType.Cube);
        worldObject.SetActive(false);
        worldObject.name = $"Dropped_{definition.StableId}_{spawnId}";
        worldObject.transform.position = position;
        worldObject.transform.rotation = Quaternion.LookRotation(
            -ResolveHorizontalForward(),
            Vector3.up);
        worldObject.transform.localScale = new Vector3(0.7f, 0.42f, 0.5f);
        Renderer body = worldObject.GetComponent<Renderer>();
        body.sharedMaterial = GetBodyMaterial(definition.EffectType);
        CreateMark(worldObject.transform, Vector3.forward * 0.52f,
            definition.EffectType);
        CreateMark(worldObject.transform, Vector3.back * 0.52f,
            definition.EffectType);
        WorldItemPickup pickup = worldObject.AddComponent<WorldItemPickup>();
        pickup.Configure(definition, quantity);
        pickup.ConfigureSpawnMetadata(spawnId, source);
        return worldObject;
    }

    private void CreateMark(
        Transform parent,
        Vector3 position,
        ItemEffectType effectType)
    {
        CreateMarkBar(parent, position,
            new Vector3(0.28f, 0.08f, 0.035f), effectType);
        CreateMarkBar(parent, position,
            new Vector3(0.08f, 0.28f, 0.035f), effectType);
    }

    private void CreateMarkBar(
        Transform parent,
        Vector3 position,
        Vector3 scale,
        ItemEffectType effectType)
    {
        GameObject bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
        bar.name = "ItemMark";
        bar.transform.SetParent(parent, false);
        bar.transform.localPosition = position;
        bar.transform.localScale = scale;
        Collider collider = bar.GetComponent<Collider>();

        if (collider != null)
        {
            Destroy(collider);
        }

        bar.GetComponent<Renderer>().sharedMaterial =
            GetMarkMaterial(effectType);
    }

    private Material GetBodyMaterial(ItemEffectType effectType)
    {
        if (bodyMaterials.TryGetValue(effectType, out Material material))
        {
            return material;
        }

        material = CreateMaterial(
            "Universal Render Pipeline/Lit",
            GetBodyColor(effectType));
        bodyMaterials.Add(effectType, material);
        return material;
    }

    private Material GetMarkMaterial(ItemEffectType effectType)
    {
        if (markMaterials.TryGetValue(effectType, out Material material))
        {
            return material;
        }

        material = CreateMaterial(
            "Universal Render Pipeline/Unlit",
            GetMarkColor(effectType));
        markMaterials.Add(effectType, material);
        return material;
    }

    private static Material CreateMaterial(string shaderName, Color color)
    {
        Shader shader = Shader.Find(shaderName);
        shader ??= Shader.Find(
            shaderName.EndsWith("Unlit", StringComparison.Ordinal)
                ? "Unlit/Color"
                : "Standard");
        return shader != null
            ? new Material(shader) { color = color }
            : null;
    }

    private static Color GetBodyColor(ItemEffectType effectType)
    {
        return effectType switch
        {
            ItemEffectType.RestoreArmor =>
                new Color(0.12f, 0.24f, 0.35f, 1f),
            ItemEffectType.AddRifleAmmo =>
                new Color(0.25f, 0.28f, 0.18f, 1f),
            ItemEffectType.AddHandgunAmmo =>
                new Color(0.32f, 0.24f, 0.14f, 1f),
            _ => new Color(0.86f, 0.89f, 0.88f, 1f)
        };
    }

    private static Color GetMarkColor(ItemEffectType effectType)
    {
        return effectType switch
        {
            ItemEffectType.RestoreArmor =>
                new Color(0.28f, 0.62f, 1f, 1f),
            ItemEffectType.AddRifleAmmo =>
                new Color(0.75f, 0.9f, 0.3f, 1f),
            ItemEffectType.AddHandgunAmmo =>
                new Color(1f, 0.7f, 0.22f, 1f),
            _ => new Color(0.86f, 0.12f, 0.12f, 1f)
        };
    }
}
