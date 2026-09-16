using System;
using FPS.Networking.Netcode;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkPlayerReplica))]
public sealed class NetworkPlayerAppearancePresenter : MonoBehaviour
{
    [SerializeField] private Transform visualRoot;
    [SerializeField] private ThirdPersonWeaponCatalog weaponCatalog;
    [SerializeField] private string defaultWeaponId = "weapon.lpsp.ar";
    [SerializeField] private bool ownerBodyCastsShadows = true;

    private NetworkPlayerReplica replica;
    private PlayerAppearanceCatalog catalog;
    private GameObject appearanceInstance;
    private ThirdPersonWeaponRig weaponInstance;
    private ThirdPersonWeaponDefinition weaponDefinition;
    private Transform weaponSocket;
    private Animator animator;
    private NetworkThirdPersonAnimator animationDriver;

    public Transform VisualRoot => visualRoot;
    public GameObject CurrentAppearance => appearanceInstance;
    public GameObject ThirdPersonWeapon =>
        weaponInstance != null ? weaponInstance.gameObject : null;
    public GameObject ThirdPersonWeaponPrefab => ResolveWeaponDefinition()
        ?.CalibratedPrefab;
    public ThirdPersonWeaponCatalog WeaponCatalog => weaponCatalog;
    public ThirdPersonWeaponDefinition CurrentWeaponDefinition =>
        weaponDefinition;
    public ThirdPersonWeaponRig CurrentWeaponRig => weaponInstance;
    public Transform WeaponSocket => weaponSocket;
    public bool OwnerBodyCastsShadows => ownerBodyCastsShadows;
    public NetworkThirdPersonAnimator AnimationDriver => animationDriver;
    public bool IsOwnerRepresentation =>
        replica != null && replica.IsLocallyControlled;

    public void Configure(
        Transform presentationRoot,
        ThirdPersonWeaponCatalog catalog,
        string weaponId,
        bool castOwnerShadows)
    {
        visualRoot = presentationRoot;
        weaponCatalog = catalog;
        defaultWeaponId = weaponId?.Trim() ?? string.Empty;
        ownerBodyCastsShadows = castOwnerShadows;
    }

    private void Awake()
    {
        replica = GetComponent<NetworkPlayerReplica>();
        animationDriver = GetComponent<NetworkThirdPersonAnimator>();
        EnsureVisualRoot();
    }

    private void OnEnable()
    {
        replica ??= GetComponent<NetworkPlayerReplica>();
        replica.AppearanceChanged += HandleAppearanceChanged;
        replica.WeaponChanged += HandleWeaponChanged;
        replica.PosePresented += HandlePosePresented;
    }

    private void Start()
    {
        RefreshRepresentation();
    }

    private void OnDisable()
    {
        if (replica == null) return;
        replica.AppearanceChanged -= HandleAppearanceChanged;
        replica.WeaponChanged -= HandleWeaponChanged;
        replica.PosePresented -= HandlePosePresented;
    }

    public void RefreshRepresentation()
    {
        replica ??= GetComponent<NetworkPlayerReplica>();
        EnsureVisualRoot();
        if (IsDedicatedServer())
        {
            visualRoot.gameObject.SetActive(false);
            return;
        }
        visualRoot.gameObject.SetActive(true);
        catalog ??= Resources.Load<PlayerAppearanceCatalog>(
            PlayerAppearanceCatalog.ResourcesPath);
        if (catalog == null)
        {
            throw new InvalidOperationException(
                "联机人物表现缺少 PlayerAppearanceCatalog。");
        }

        string requestedId = replica.AppearanceId;
        if (string.IsNullOrWhiteSpace(requestedId) &&
            replica.IsLocallyControlled)
        {
            requestedId = PlayerAppearanceSelection.CurrentAppearanceId;
        }

        appearanceInstance = PlayerAppearanceFactory.Create(
            catalog,
            requestedId,
            visualRoot,
            out _,
            out _);
        animator = appearanceInstance.GetComponent<Animator>();
        defaultWeaponId = NetworkPresentationIds.ResolveWeaponOrDefault(
            replica.WeaponId);
        animationDriver ??= GetComponent<NetworkThirdPersonAnimator>();
        animationDriver?.BindAnimator(animator);
        CreateThirdPersonWeapon();
        ApplyVisibilityPolicy();
    }

    public void SetThirdPersonWeapon(string weaponId)
    {
        defaultWeaponId = weaponId?.Trim() ?? string.Empty;
        if (animator != null && isActiveAndEnabled)
        {
            CreateThirdPersonWeapon();
            ApplyVisibilityPolicy();
        }
    }

    private void HandleAppearanceChanged(string _)
    {
        if (!isActiveAndEnabled) return;
        RefreshRepresentation();
    }

    private void HandleWeaponChanged(string weaponId)
    {
        if (!isActiveAndEnabled) return;
        string safe = NetworkPresentationIds.ResolveWeaponOrDefault(weaponId);
        if (string.Equals(defaultWeaponId, safe,
                StringComparison.Ordinal) && weaponInstance != null) return;
        SetThirdPersonWeapon(safe);
    }

    private void HandlePosePresented(
        Vector3 _,
        float __,
        float ___,
        bool ____,
        bool _____)
    {
        ApplyVisibilityPolicy();
    }

    private void ApplyVisibilityPolicy()
    {
        if (visualRoot == null || replica == null) return;
        bool alive = replica.PresentedAlive;
        bool owner = replica.IsLocallyControlled;
        bool ready = replica.IsPresentationReady;
        Renderer[] renderers = visualRoot.GetComponentsInChildren<Renderer>(
            true);
        for (int index = 0; index < renderers.Length; index++)
        {
            Renderer renderer = renderers[index];
            if (!ready || !alive)
            {
                renderer.enabled = false;
                continue;
            }

            if (!owner)
            {
                renderer.enabled = true;
                renderer.shadowCastingMode = ShadowCastingMode.On;
                renderer.receiveShadows = true;
                continue;
            }

            renderer.enabled = ownerBodyCastsShadows;
            if (ownerBodyCastsShadows)
            {
                renderer.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
                renderer.receiveShadows = false;
            }
        }
    }

    private void CreateThirdPersonWeapon()
    {
        DisposeWeapon();
        if (animator == null) return;
        weaponDefinition = ResolveWeaponDefinition();
        if (weaponDefinition == null)
            throw new InvalidOperationException(
                "联机人物表现缺少有效第三人称武器定义。");
        weaponInstance = ThirdPersonWeaponFactory.Create(
            weaponDefinition, animator, out weaponSocket);
        animationDriver?.BindWeaponRig(weaponInstance);
    }

    private void DisposeWeapon()
    {
        animationDriver?.BindWeaponRig(null);
        if (weaponSocket != null)
        {
            weaponSocket.gameObject.SetActive(false);
            if (Application.isPlaying) Destroy(weaponSocket.gameObject);
            else DestroyImmediate(weaponSocket.gameObject);
        }
        weaponInstance = null;
        weaponDefinition = null;
        weaponSocket = null;
    }

    private void EnsureVisualRoot()
    {
        if (visualRoot != null) return;
        Transform existing = transform.Find("ThirdPersonVisualRoot");
        if (existing != null)
        {
            visualRoot = existing;
            return;
        }

        var root = new GameObject("ThirdPersonVisualRoot");
        visualRoot = root.transform;
        visualRoot.SetParent(transform, false);
    }

    private ThirdPersonWeaponDefinition ResolveWeaponDefinition()
    {
        weaponCatalog ??= Resources.Load<ThirdPersonWeaponCatalog>(
            ThirdPersonWeaponCatalog.ResourcesPath);
        if (weaponCatalog == null) return null;
        return weaponCatalog.Resolve(defaultWeaponId, out _);
    }

    private static bool IsDedicatedServer()
    {
#if UNITY_SERVER
        return true;
#else
        NetworkManager manager = NetworkManager.Singleton;
        return manager != null && manager.IsServer && !manager.IsClient;
#endif
    }
}
