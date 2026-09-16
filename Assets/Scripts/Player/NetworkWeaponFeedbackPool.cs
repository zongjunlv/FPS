using UnityEngine;

[DisallowMultipleComponent]
public sealed class NetworkWeaponFeedbackPool : MonoBehaviour
{
    private const int MuzzleCapacity = 16;
    private const int CasingCapacity = 32;

    private PooledNetworkWeaponVisual[] muzzleFlashes;
    private PooledNetworkWeaponVisual[] casings;
    private Material muzzleMaterial;
    private Material casingMaterial;
    private int muzzleReuseIndex;
    private int casingReuseIndex;

    public int MuzzleFlashCapacity => muzzleFlashes?.Length ?? 0;
    public int CasingCapacityValue => casings?.Length ?? 0;
    public int ActiveMuzzleFlashCount => CountActive(muzzleFlashes);
    public int ActiveCasingCount => CountActive(casings);

    private void Awake()
    {
        Prewarm();
    }

    public void PlayMuzzle(Vector3 origin, Vector3 shotDirection)
    {
        Prewarm();
        Vector3 forward = SafeDirection(shotDirection);
        PooledNetworkWeaponVisual visual = Acquire(
            muzzleFlashes, ref muzzleReuseIndex);
        visual.Activate(origin, Quaternion.LookRotation(forward),
            Vector3.zero, Vector3.zero, 0.055f);
    }

    public void EjectCasing(Vector3 origin, Vector3 shotDirection)
    {
        Prewarm();
        Vector3 forward = SafeDirection(shotDirection);
        Vector3 right = Vector3.Cross(Vector3.up, forward);
        if (right.sqrMagnitude < 0.001f)
            right = Vector3.right;
        else
            right.Normalize();
        Vector3 velocity = right * 1.65f + Vector3.up * 1.2f -
                           forward * 0.18f;
        Vector3 spin = new Vector3(720f, 1040f, 560f);
        PooledNetworkWeaponVisual visual = Acquire(
            casings, ref casingReuseIndex);
        visual.Activate(origin + right * 0.035f,
            Quaternion.LookRotation(forward), velocity, spin, 1.1f);
    }

    public void ReturnAll()
    {
        DeactivateAll(muzzleFlashes);
        DeactivateAll(casings);
    }

    private void Prewarm()
    {
        if (muzzleFlashes != null && casings != null) return;
        CreateMaterials();
        muzzleFlashes = new PooledNetworkWeaponVisual[MuzzleCapacity];
        casings = new PooledNetworkWeaponVisual[CasingCapacity];
        for (int index = 0; index < muzzleFlashes.Length; index++)
        {
            GameObject effect = GameObject.CreatePrimitive(
                PrimitiveType.Sphere);
            effect.name = $"Network Muzzle Flash {index + 1:00}";
            effect.transform.SetParent(transform, false);
            effect.transform.localScale = new Vector3(0.055f, 0.055f, 0.16f);
            DisableCollider(effect);
            effect.GetComponent<Renderer>().sharedMaterial = muzzleMaterial;
            muzzleFlashes[index] = effect.AddComponent<
                PooledNetworkWeaponVisual>();
            muzzleFlashes[index].Prepare(gravity: false);
        }
        for (int index = 0; index < casings.Length; index++)
        {
            GameObject effect = GameObject.CreatePrimitive(
                PrimitiveType.Cylinder);
            effect.name = $"Network Casing {index + 1:00}";
            effect.transform.SetParent(transform, false);
            effect.transform.localScale = new Vector3(0.012f, 0.026f, 0.012f);
            DisableCollider(effect);
            effect.GetComponent<Renderer>().sharedMaterial = casingMaterial;
            casings[index] = effect.AddComponent<
                PooledNetworkWeaponVisual>();
            casings[index].Prepare(gravity: true);
        }
    }

    private void CreateMaterials()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ??
                        Shader.Find("Unlit/Color") ??
                        Shader.Find("Sprites/Default");
        muzzleMaterial = new Material(shader)
        {
            name = "Network Muzzle Flash Material",
            color = new Color(1f, 0.45f, 0.05f, 1f),
            hideFlags = HideFlags.HideAndDontSave
        };
        casingMaterial = new Material(shader)
        {
            name = "Network Casing Material",
            color = new Color(0.72f, 0.48f, 0.12f, 1f),
            hideFlags = HideFlags.HideAndDontSave
        };
    }

    private void OnDestroy()
    {
        if (muzzleMaterial != null) Destroy(muzzleMaterial);
        if (casingMaterial != null) Destroy(casingMaterial);
    }

    private static PooledNetworkWeaponVisual Acquire(
        PooledNetworkWeaponVisual[] pool,
        ref int reuseIndex)
    {
        foreach (PooledNetworkWeaponVisual visual in pool)
        {
            if (!visual.gameObject.activeSelf) return visual;
        }
        PooledNetworkWeaponVisual reused = pool[reuseIndex];
        reuseIndex = (reuseIndex + 1) % pool.Length;
        reused.Deactivate();
        return reused;
    }

    private static int CountActive(PooledNetworkWeaponVisual[] pool)
    {
        if (pool == null) return 0;
        int count = 0;
        foreach (PooledNetworkWeaponVisual visual in pool)
        {
            if (visual != null && visual.gameObject.activeSelf) count++;
        }
        return count;
    }

    private static void DeactivateAll(PooledNetworkWeaponVisual[] pool)
    {
        if (pool == null) return;
        foreach (PooledNetworkWeaponVisual visual in pool)
            visual?.Deactivate();
    }

    private static void DisableCollider(GameObject effect)
    {
        Collider collider = effect.GetComponent<Collider>();
        if (collider != null) collider.enabled = false;
    }

    private static Vector3 SafeDirection(Vector3 direction) =>
        direction.sqrMagnitude > 0.0001f
            ? direction.normalized
            : Vector3.forward;
}

public sealed class PooledNetworkWeaponVisual : MonoBehaviour
{
    private bool useGravity;
    private float remainingLifetime;
    private Vector3 velocity;
    private Vector3 angularVelocity;

    public void Prepare(bool gravity)
    {
        useGravity = gravity;
        Deactivate();
    }

    public void Activate(
        Vector3 position,
        Quaternion rotation,
        Vector3 initialVelocity,
        Vector3 spinDegrees,
        float lifetime)
    {
        transform.SetPositionAndRotation(position, rotation);
        velocity = initialVelocity;
        angularVelocity = spinDegrees;
        remainingLifetime = Mathf.Max(0.01f, lifetime);
        gameObject.SetActive(true);
    }

    public void Deactivate()
    {
        remainingLifetime = 0f;
        velocity = Vector3.zero;
        angularVelocity = Vector3.zero;
        gameObject.SetActive(false);
    }

    private void Update()
    {
        float deltaTime = Time.deltaTime;
        remainingLifetime -= deltaTime;
        if (remainingLifetime <= 0f)
        {
            Deactivate();
            return;
        }
        if (useGravity) velocity += Physics.gravity * deltaTime;
        transform.position += velocity * deltaTime;
        transform.Rotate(angularVelocity * deltaTime, Space.Self);
    }
}
