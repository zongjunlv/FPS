using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

[Serializable]
public sealed class HumanoidCharacterEntry
{
    [SerializeField] private string stableId;
    [SerializeField] private string displayName;
    [SerializeField] private GameObject prefab;
    [SerializeField] private float expectedHeight = 1.8f;

    public string StableId => stableId;
    public string DisplayName => displayName;
    public GameObject Prefab => prefab;
    public float ExpectedHeight => expectedHeight;

    public void Configure(
        string id,
        string name,
        GameObject characterPrefab,
        float height)
    {
        stableId = id?.Trim() ?? string.Empty;
        displayName = name?.Trim() ?? string.Empty;
        prefab = characterPrefab;
        expectedHeight = Mathf.Max(0.1f, height);
    }
}

[Serializable]
public sealed class HumanoidAnimationSet
{
    [SerializeField] private AnimationClip idle;
    [SerializeField] private AnimationClip walk;
    [SerializeField] private AnimationClip sprint;
    [SerializeField] private AnimationClip crouch;
    [SerializeField] private AnimationClip jumpStart;
    [SerializeField] private AnimationClip jumpLoop;
    [SerializeField] private AnimationClip land;
    [SerializeField] private AnimationClip aim;
    [SerializeField] private AnimationClip shoot;
    [SerializeField] private AnimationClip reload;

    public AnimationClip Idle => idle;
    public AnimationClip Walk => walk;
    public AnimationClip Sprint => sprint;
    public AnimationClip Crouch => crouch;
    public AnimationClip JumpStart => jumpStart;
    public AnimationClip JumpLoop => jumpLoop;
    public AnimationClip Land => land;
    public AnimationClip Aim => aim;
    public AnimationClip Shoot => shoot;
    public AnimationClip Reload => reload;
    public IReadOnlyList<AnimationClip> RequiredClips => new[]
    {
        idle, walk, sprint, crouch, jumpStart, jumpLoop, land, aim, shoot, reload
    };

    public void Configure(
        AnimationClip idleClip,
        AnimationClip walkClip,
        AnimationClip sprintClip,
        AnimationClip crouchClip,
        AnimationClip jumpStartClip,
        AnimationClip jumpLoopClip,
        AnimationClip landClip,
        AnimationClip aimClip,
        AnimationClip shootClip,
        AnimationClip reloadClip)
    {
        idle = idleClip;
        walk = walkClip;
        sprint = sprintClip;
        crouch = crouchClip;
        jumpStart = jumpStartClip;
        jumpLoop = jumpLoopClip;
        land = landClip;
        aim = aimClip;
        shoot = shootClip;
        reload = reloadClip;
    }
}

[CreateAssetMenu(
    fileName = "HumanoidCharacterStandard",
    menuName = "FPS/Characters/Humanoid Character Standard")]
public sealed class HumanoidCharacterStandard : ScriptableObject
{
    public const string DefaultAssetPath =
        "Assets/Resources/Content/Characters/Humanoid/HumanoidCharacterStandard.asset";
    public const string PreviewScenePath =
        "Assets/Scenes/CharacterCalibration/HumanoidCharacterPreview.unity";
    public const float StandardHeight = 1.8f;

    [SerializeField] private string stableId = "characters.humanoid.standard";
    [SerializeField, Min(1)] private int version = 1;
    [SerializeField] private float standardHeight = StandardHeight;
    [SerializeField] private Vector3 forwardAxis = Vector3.forward;
    [SerializeField] private Vector3 upAxis = Vector3.up;
    [SerializeField] private RuntimeAnimatorController sharedAnimatorController;
    [SerializeField] private HumanoidAnimationSet animationSet = new();
    [SerializeField] private HumanoidCharacterEntry[] characters =
        Array.Empty<HumanoidCharacterEntry>();

    public string StableId => stableId;
    public int Version => version;
    public float TargetHeight => standardHeight;
    public Vector3 ForwardAxis => forwardAxis;
    public Vector3 UpAxis => upAxis;
    public RuntimeAnimatorController SharedAnimatorController => sharedAnimatorController;
    public HumanoidAnimationSet AnimationSet => animationSet;
    public IReadOnlyList<HumanoidCharacterEntry> Characters =>
        characters ?? Array.Empty<HumanoidCharacterEntry>();

    public void Configure(
        string id,
        int contentVersion,
        RuntimeAnimatorController controller,
        HumanoidAnimationSet motions,
        IEnumerable<HumanoidCharacterEntry> entries)
    {
        stableId = id?.Trim() ?? string.Empty;
        version = Mathf.Max(1, contentVersion);
        standardHeight = StandardHeight;
        forwardAxis = Vector3.forward;
        upAxis = Vector3.up;
        sharedAnimatorController = controller;
        animationSet = motions ?? new HumanoidAnimationSet();
        characters = entries?.Where(entry => entry != null).ToArray() ??
                     Array.Empty<HumanoidCharacterEntry>();
    }

    public bool TryValidate(out string error)
    {
        if (string.IsNullOrWhiteSpace(stableId))
        {
            error = "Humanoid 标准缺少 stableId。";
            return false;
        }
        if (Mathf.Abs(standardHeight - StandardHeight) > 0.001f ||
            forwardAxis != Vector3.forward || upAxis != Vector3.up)
        {
            error = "Humanoid 标准必须使用 1.8 米、Z 轴向前、Y 轴向上。";
            return false;
        }
        if (sharedAnimatorController == null)
        {
            error = "Humanoid 标准缺少共享 Animator Controller。";
            return false;
        }
        if (animationSet == null ||
            animationSet.RequiredClips.Any(clip => clip == null || !clip.isHumanMotion))
        {
            error = "Humanoid 标准缺少有效的人形移动或战斗动画。";
            return false;
        }
        if (characters == null || characters.Length < 3)
        {
            error = "Humanoid 标准至少需要三个可切换角色。";
            return false;
        }
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (HumanoidCharacterEntry entry in characters)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.StableId) ||
                !ids.Add(entry.StableId) || entry.Prefab == null)
            {
                error = "Humanoid 角色记录为空、ID 重复或缺少 Prefab。";
                return false;
            }
            Animator animator = entry.Prefab.GetComponent<Animator>();
            if (animator == null || animator.avatar == null ||
                !animator.avatar.isValid || !animator.avatar.isHuman ||
                animator.runtimeAnimatorController != sharedAnimatorController)
            {
                error = $"角色 '{entry.StableId}' 未使用有效 Humanoid Avatar 或共享动画图。";
                return false;
            }
        }
        error = string.Empty;
        return true;
    }
}
