using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

[Serializable]
public struct PlayerCollisionReference
{
    [SerializeField, Min(0.1f)] private float height;
    [SerializeField, Min(0.01f)] private float radius;
    [SerializeField] private Vector3 center;

    public float Height => height;
    public float Radius => radius;
    public Vector3 Center => center;

    public PlayerCollisionReference(float referenceHeight, float referenceRadius,
        Vector3 referenceCenter)
    {
        height = Mathf.Max(0.1f, referenceHeight);
        radius = Mathf.Clamp(referenceRadius, 0.01f, height * 0.5f);
        center = referenceCenter;
    }

    public bool IsValid => height > 0f && radius > 0f &&
                           radius <= height * 0.5f && IsFinite(center);

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.x) && float.IsFinite(value.y) &&
        float.IsFinite(value.z);
}

[Serializable]
public struct PlayerAppearanceLodConfiguration
{
    [SerializeField, Range(0.01f, 1f)] private float visibleHeight;
    [SerializeField] private LODFadeMode fadeMode;
    [SerializeField, Min(0f)] private float crossFadeWidth;

    public float VisibleHeight => visibleHeight;
    public LODFadeMode FadeMode => fadeMode;
    public float CrossFadeWidth => crossFadeWidth;

    public PlayerAppearanceLodConfiguration(float screenRelativeHeight,
        LODFadeMode mode, float fadeWidth)
    {
        visibleHeight = Mathf.Clamp(screenRelativeHeight, 0.01f, 1f);
        fadeMode = mode;
        crossFadeWidth = Mathf.Max(0f, fadeWidth);
    }

    public bool IsValid => visibleHeight is > 0f and <= 1f &&
                           crossFadeWidth >= 0f;
}

[CreateAssetMenu(fileName = "PlayerAppearance",
    menuName = "FPS/Characters/Player Appearance Definition")]
public sealed class PlayerAppearanceDefinition : ScriptableObject
{
    [SerializeField] private string stableId;
    [SerializeField] private string displayName;
    [SerializeField] private GameObject visualPrefab;
    [SerializeField] private Avatar avatar;
    [SerializeField] private RuntimeAnimatorController animatorController;
    [SerializeField] private Material[] materials = Array.Empty<Material>();
    [SerializeField] private PlayerCollisionReference collisionReference;
    [SerializeField] private Vector3 viewOffset = new(0f, 1.65f, 0f);
    [SerializeField] private PlayerAppearanceLodConfiguration lodConfiguration;

    public string StableId => stableId;
    public string DisplayName => displayName;
    public GameObject VisualPrefab => visualPrefab;
    public Avatar Avatar => avatar;
    public RuntimeAnimatorController AnimatorController => animatorController;
    public IReadOnlyList<Material> Materials => materials ?? Array.Empty<Material>();
    public PlayerCollisionReference CollisionReference => collisionReference;
    public Vector3 ViewOffset => viewOffset;
    public PlayerAppearanceLodConfiguration LodConfiguration => lodConfiguration;

    public void Configure(string id, string name, GameObject prefab,
        Avatar humanoidAvatar, RuntimeAnimatorController controller,
        IEnumerable<Material> appearanceMaterials,
        PlayerCollisionReference referenceCollision, Vector3 cameraViewOffset,
        PlayerAppearanceLodConfiguration lod)
    {
        stableId = id?.Trim() ?? string.Empty;
        displayName = name?.Trim() ?? string.Empty;
        visualPrefab = prefab;
        avatar = humanoidAvatar;
        animatorController = controller;
        materials = appearanceMaterials?.Where(value => value != null)
            .Distinct().ToArray() ?? Array.Empty<Material>();
        collisionReference = referenceCollision;
        viewOffset = cameraViewOffset;
        lodConfiguration = lod;
    }

    public bool TryValidate(out string error)
    {
        if (string.IsNullOrWhiteSpace(stableId) ||
            string.IsNullOrWhiteSpace(displayName))
        {
            error = "角色外观缺少稳定 ID 或显示名称。";
            return false;
        }
        if (visualPrefab == null)
        {
            error = $"角色外观 '{stableId}' 缺少模型 Prefab。";
            return false;
        }
        if (avatar == null || !avatar.isValid || !avatar.isHuman)
        {
            error = $"角色外观 '{stableId}' 缺少有效 Humanoid Avatar。";
            return false;
        }
        if (animatorController == null)
        {
            error = $"角色外观 '{stableId}' 缺少 Animator Controller。";
            return false;
        }
        Animator animator = visualPrefab.GetComponent<Animator>();
        if (animator == null || animator.avatar != avatar ||
            animator.runtimeAnimatorController != animatorController)
        {
            error = $"角色外观 '{stableId}' 的模型、Avatar 与动画图不一致。";
            return false;
        }
        if (materials == null || materials.Length == 0 ||
            materials.Any(value => value == null))
        {
            error = $"角色外观 '{stableId}' 缺少有效材质。";
            return false;
        }
        if (!collisionReference.IsValid || !lodConfiguration.IsValid ||
            !IsFinite(viewOffset))
        {
            error = $"角色外观 '{stableId}' 的碰撞、视线或 LOD 参考无效。";
            return false;
        }
        if (visualPrefab.GetComponentInChildren<CharacterController>(true) != null ||
            visualPrefab.GetComponentInChildren<PlayerController>(true) != null)
        {
            error = $"角色外观 '{stableId}' 不得携带权威移动或碰撞逻辑。";
            return false;
        }
        error = string.Empty;
        return true;
    }

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.x) && float.IsFinite(value.y) &&
        float.IsFinite(value.z);
}
