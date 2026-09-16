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
    [SerializeField] private GameObject thirdPersonWeaponPrefab;
    [SerializeField] private bool ownerBodyCastsShadows = true;
    [SerializeField] private Vector3 weaponLocalPosition =
        new(0.02f, 0.04f, 0.02f);
    [SerializeField] private Vector3 weaponLocalEuler =
        new(0f, 90f, 90f);
    [SerializeField] private Vector3 weaponLocalScale = Vector3.one;

    private NetworkPlayerReplica replica;
    private PlayerAppearanceCatalog catalog;
    private GameObject appearanceInstance;
    private GameObject weaponInstance;
    private Animator animator;
    private NetworkThirdPersonAnimator animationDriver;

    public Transform VisualRoot => visualRoot;
    public GameObject CurrentAppearance => appearanceInstance;
    public GameObject ThirdPersonWeapon => weaponInstance;
    public GameObject ThirdPersonWeaponPrefab => thirdPersonWeaponPrefab;
    public bool OwnerBodyCastsShadows => ownerBodyCastsShadows;
    public NetworkThirdPersonAnimator AnimationDriver => animationDriver;
    public bool IsOwnerRepresentation =>
        replica != null && replica.IsLocallyControlled;

    public void Configure(
        Transform presentationRoot,
        GameObject weaponPrefab,
        bool castOwnerShadows,
        Vector3 weaponPosition,
        Vector3 weaponEuler,
        Vector3 weaponScale)
    {
        visualRoot = presentationRoot;
        thirdPersonWeaponPrefab = weaponPrefab;
        ownerBodyCastsShadows = castOwnerShadows;
        weaponLocalPosition = weaponPosition;
        weaponLocalEuler = weaponEuler;
        weaponLocalScale = weaponScale;
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
        animationDriver ??= GetComponent<NetworkThirdPersonAnimator>();
        animationDriver?.BindAnimator(animator);
        CreateThirdPersonWeapon();
        ApplyVisibilityPolicy();
    }

    private void HandleAppearanceChanged(string _)
    {
        if (!isActiveAndEnabled) return;
        RefreshRepresentation();
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
        Renderer[] renderers = visualRoot.GetComponentsInChildren<Renderer>(
            true);
        for (int index = 0; index < renderers.Length; index++)
        {
            Renderer renderer = renderers[index];
            if (!alive)
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
        if (animator == null || thirdPersonWeaponPrefab == null) return;
        Transform rightHand = animator.GetBoneTransform(
            HumanBodyBones.RightHand);
        if (rightHand == null) return;

        weaponInstance = Instantiate(
            thirdPersonWeaponPrefab, rightHand, false);
        weaponInstance.name = "ThirdPersonWeapon";
        weaponInstance.transform.localPosition = weaponLocalPosition;
        weaponInstance.transform.localRotation =
            Quaternion.Euler(weaponLocalEuler);
        weaponInstance.transform.localScale = weaponLocalScale;
        DisableWeaponGameplayComponents(weaponInstance);
    }

    private void DisposeWeapon()
    {
        if (weaponInstance == null) return;
        weaponInstance.SetActive(false);
        if (Application.isPlaying) Destroy(weaponInstance);
        else DestroyImmediate(weaponInstance);
        weaponInstance = null;
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

    private static void DisableWeaponGameplayComponents(GameObject weapon)
    {
        foreach (MonoBehaviour behaviour in weapon
                     .GetComponentsInChildren<MonoBehaviour>(true))
            behaviour.enabled = false;
        foreach (Animator weaponAnimator in weapon
                     .GetComponentsInChildren<Animator>(true))
            weaponAnimator.enabled = false;
        foreach (Collider collider in weapon
                     .GetComponentsInChildren<Collider>(true))
            collider.enabled = false;
        foreach (Rigidbody body in weapon
                     .GetComponentsInChildren<Rigidbody>(true))
        {
            body.isKinematic = true;
            body.detectCollisions = false;
        }
        foreach (AudioSource source in weapon
                     .GetComponentsInChildren<AudioSource>(true))
            source.enabled = false;
        foreach (ParticleSystem particles in weapon
                     .GetComponentsInChildren<ParticleSystem>(true))
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
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
