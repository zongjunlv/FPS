using FPS.Networking.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkPlayerReplica))]
public sealed class NetworkThirdPersonAnimator : MonoBehaviour
{
    [SerializeField, Min(0f)] private float movementDamping = 0.12f;
    [SerializeField, Min(0f)] private float aimDamping = 0.1f;
    [SerializeField, Min(0.1f)] private float fallbackSprintSpeed = 6f;

    private NetworkPlayerReplica replica;
    private Animator animator;
    private ThirdPersonWeaponIkController weaponIk;
    private bool aiming = true;

    public Animator BoundAnimator => animator;
    public float NormalizedSpeed { get; private set; }
    public Vector2 LocalMovement { get; private set; }
    public float NormalizedAimPitch { get; private set; }
    public ThirdPersonCombatAction LastCombatAction { get; private set; }
    public int CombatActionCount { get; private set; }
    public ThirdPersonWeaponIkController WeaponIk => weaponIk;
    public Ray CurrentWeaponFeedbackRay => weaponIk != null
        ? weaponIk.FeedbackRay
        : new Ray(transform.position, transform.forward);

    private void Awake()
    {
        replica = GetComponent<NetworkPlayerReplica>();
    }

    private void OnEnable()
    {
        replica ??= GetComponent<NetworkPlayerReplica>();
        replica.PosePresented += HandlePosePresented;
        replica.PresentationActionReceived += HandlePresentationAction;
    }

    private void OnDisable()
    {
        if (replica != null)
        {
            replica.PosePresented -= HandlePosePresented;
            replica.PresentationActionReceived -= HandlePresentationAction;
        }
    }

    public void BindAnimator(Animator target)
    {
        animator = target;
        if (animator == null) return;
        if (replica != null) aiming = replica.PresentedAiming;
        animator.applyRootMotion = false;
        animator.updateMode = AnimatorUpdateMode.Normal;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        weaponIk = animator.GetComponent<ThirdPersonWeaponIkController>();
        if (weaponIk == null)
            weaponIk = animator.gameObject.AddComponent<
                ThirdPersonWeaponIkController>();
        weaponIk.Configure(animator, null, transform);
        weaponIk.SetAiming(aiming);
        int upperBodyLayer = animator.GetLayerIndex(
            ThirdPersonAnimationParameters.UpperBodyLayerName);
        if (upperBodyLayer >= 0) animator.SetLayerWeight(upperBodyLayer, 1f);
        animator.SetBool(ThirdPersonAnimationParameters.Aiming, aiming);
        if (replica != null)
        {
            ApplyPresentation(
                replica.PresentedVelocity,
                replica.PresentedAimPitch,
                replica.PresentedCrouching,
                replica.PresentedGrounded,
                0f,
                immediate: true);
        }
    }

    public void SetAiming(bool value)
    {
        aiming = value;
        weaponIk?.SetAiming(aiming);
        if (animator != null)
            animator.SetBool(ThirdPersonAnimationParameters.Aiming, aiming);
    }

    public void PlayCombatAction(ThirdPersonCombatAction action)
    {
        if (action is < ThirdPersonCombatAction.Shoot or
            > ThirdPersonCombatAction.SwitchWeapon)
        {
            return;
        }
        LastCombatAction = action;
        CombatActionCount++;
        weaponIk?.BeginCombatAction(action);
        if (animator == null) return;
        int trigger = action switch
        {
            ThirdPersonCombatAction.Shoot =>
                ThirdPersonAnimationParameters.Shoot,
            ThirdPersonCombatAction.Reload =>
                ThirdPersonAnimationParameters.Reload,
            _ => ThirdPersonAnimationParameters.SwitchWeapon
        };
        animator.SetTrigger(trigger);
    }

    public void ApplyPresentation(
        Vector3 worldVelocity,
        float aimPitchDegrees,
        bool crouching,
        bool grounded,
        float deltaTime,
        bool immediate = false)
    {
        Vector3 horizontal = worldVelocity;
        horizontal.y = 0f;
        float sprintSpeed = fallbackSprintSpeed;
        if (replica != null)
            sprintSpeed = Mathf.Max(0.1f,
                replica.PresentationSprintSpeed);
        NormalizedSpeed = Mathf.Clamp01(horizontal.magnitude / sprintSpeed);
        Vector3 local = transform.InverseTransformDirection(horizontal);
        Vector2 direction = new(local.x, local.z);
        if (direction.sqrMagnitude > 0.0001f)
            direction.Normalize();
        LocalMovement = direction * NormalizedSpeed;
        NormalizedAimPitch = Mathf.Clamp(aimPitchDegrees / 89f, -1f, 1f);
        weaponIk?.SetAimPitch(aimPitchDegrees);

        if (animator == null) return;
        float movementBlend = immediate ? 0f : movementDamping;
        float aimBlend = immediate ? 0f : aimDamping;
        float safeDelta = Mathf.Max(0f, deltaTime);
        animator.SetFloat(ThirdPersonAnimationParameters.MoveX,
            LocalMovement.x, movementBlend, safeDelta);
        animator.SetFloat(ThirdPersonAnimationParameters.MoveY,
            LocalMovement.y, movementBlend, safeDelta);
        animator.SetFloat(ThirdPersonAnimationParameters.Speed,
            NormalizedSpeed, movementBlend, safeDelta);
        animator.SetFloat(ThirdPersonAnimationParameters.VerticalSpeed,
            worldVelocity.y, movementBlend, safeDelta);
        animator.SetFloat(ThirdPersonAnimationParameters.AimPitch,
            ThirdPersonWeaponIkController.ResolvePresentationAimParameter(
                aimPitchDegrees), aimBlend, safeDelta);
        animator.SetBool(ThirdPersonAnimationParameters.Crouching, crouching);
        animator.SetBool(ThirdPersonAnimationParameters.Grounded, grounded);
        animator.SetBool(ThirdPersonAnimationParameters.Aiming, aiming);
    }

    public void BindWeaponRig(ThirdPersonWeaponRig rig)
    {
        if (animator != null && weaponIk == null)
        {
            weaponIk = animator.GetComponent<ThirdPersonWeaponIkController>();
            if (weaponIk == null)
                weaponIk = animator.gameObject.AddComponent<
                    ThirdPersonWeaponIkController>();
            weaponIk.Configure(animator, rig, transform);
        }
        else
        {
            weaponIk?.BindWeaponRig(rig);
        }
        weaponIk?.SetAiming(aiming);
        weaponIk?.SetAimPitch(replica != null
            ? replica.PresentedAimPitch
            : NormalizedAimPitch * 89f);
    }

    private void HandlePosePresented(
        Vector3 _,
        float __,
        float aimPitch,
        bool crouching,
        bool grounded)
    {
        if (replica == null) return;
        SetAiming(replica.PresentedAiming);
        ApplyPresentation(replica.PresentedVelocity, aimPitch,
            crouching, grounded, Time.unscaledDeltaTime);
    }

    private void HandlePresentationAction(NetworkPresentationAction action)
    {
        switch (action)
        {
            case NetworkPresentationAction.Shoot:
                PlayCombatAction(ThirdPersonCombatAction.Shoot);
                break;
            case NetworkPresentationAction.Reload:
                PlayCombatAction(ThirdPersonCombatAction.Reload);
                break;
            case NetworkPresentationAction.SwitchWeapon:
                PlayCombatAction(ThirdPersonCombatAction.SwitchWeapon);
                break;
        }
    }
}
