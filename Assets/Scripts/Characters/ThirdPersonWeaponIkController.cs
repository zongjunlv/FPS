using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Animator))]
public sealed class ThirdPersonWeaponIkController : MonoBehaviour
{
    public const float MaximumAimPitchDegrees = 55f;
    public const float MaximumPresentationPitchDegrees = 25f;

    [SerializeField] private Animator animator;
    [SerializeField] private ThirdPersonWeaponRig weaponRig;
    [SerializeField] private Transform aimReference;
    [SerializeField, Min(0.1f)] private float releaseSpeed = 8f;
    [SerializeField, Min(0.1f)] private float recoverySpeed = 5f;
    [SerializeField, Range(0f, 1f)] private float handRotationWeight = 0.45f;
    [SerializeField, Range(0f, 1f)] private float elbowHintWeight = 1f;

    private int upperBodyLayerIndex = -1;
    private float requestedAimPitch;
    private bool aiming = true;
    private float actionSecondsRemaining;
    private float actionLeftHandTarget = 1f;
    private float actionAimTarget = 1f;
    private bool capturedGripRotation;
    private Quaternion gripRotationOffset = Quaternion.identity;

    public Animator Animator => animator;
    public ThirdPersonWeaponRig WeaponRig => weaponRig;
    public float RequestedAimPitch => requestedAimPitch;
    public float ConstrainedAimPitch { get; private set; }
    public float PresentationAimParameter =>
        ResolvePresentationAimParameter(ConstrainedAimPitch);
    public float LeftHandWeight { get; private set; } = 1f;
    public float AimConstraintWeight { get; private set; } = 1f;
    public float ActionSecondsRemaining => actionSecondsRemaining;
    public Vector3 AimDirection => ResolveAimDirection(
        aimReference != null ? aimReference : transform,
        ConstrainedAimPitch);
    public Ray FeedbackRay => new(
        weaponRig != null && weaponRig.MuzzlePoint != null
            ? weaponRig.MuzzlePoint.position
            : transform.position,
        AimDirection);

    public void Configure(
        Animator targetAnimator,
        ThirdPersonWeaponRig rig,
        Transform characterAimReference)
    {
        animator = targetAnimator != null
            ? targetAnimator
            : GetComponent<Animator>();
        weaponRig = rig;
        aimReference = characterAimReference != null
            ? characterAimReference
            : transform;
        upperBodyLayerIndex = animator != null
            ? animator.GetLayerIndex(
                ThirdPersonAnimationParameters.UpperBodyLayerName)
            : -1;
        capturedGripRotation = false;
        actionSecondsRemaining = 0f;
        actionLeftHandTarget = 1f;
        actionAimTarget = 1f;
        LeftHandWeight = 1f;
        AimConstraintWeight = aiming ? 1f : 0f;
        UpdateAimMarkers();
    }

    public void BindWeaponRig(ThirdPersonWeaponRig rig)
    {
        weaponRig = rig;
        capturedGripRotation = false;
        UpdateAimMarkers();
    }

    public void SetAiming(bool value)
    {
        aiming = value;
    }

    public void SetAimPitch(float pitchDegrees)
    {
        requestedAimPitch = float.IsFinite(pitchDegrees)
            ? pitchDegrees
            : 0f;
        ConstrainedAimPitch = Mathf.Clamp(
            requestedAimPitch,
            -MaximumAimPitchDegrees,
            MaximumAimPitchDegrees);
        UpdateAimMarkers();
    }

    public void BeginCombatAction(ThirdPersonCombatAction action)
    {
        switch (action)
        {
            case ThirdPersonCombatAction.Shoot:
                actionSecondsRemaining = 0.1f;
                actionLeftHandTarget = 0.9f;
                actionAimTarget = 0.9f;
                break;
            case ThirdPersonCombatAction.Reload:
                actionSecondsRemaining = 0.75f;
                actionLeftHandTarget = 0.2f;
                actionAimTarget = 0.35f;
                break;
            case ThirdPersonCombatAction.SwitchWeapon:
                actionSecondsRemaining = 0.4f;
                actionLeftHandTarget = 0f;
                actionAimTarget = 0.2f;
                break;
        }
    }

    public void Tick(float deltaTime)
    {
        float safeDelta = Mathf.Max(0f, deltaTime);
        if (actionSecondsRemaining > 0f)
            actionSecondsRemaining = Mathf.Max(
                0f, actionSecondsRemaining - safeDelta);
        float handTarget = actionSecondsRemaining > 0f
            ? actionLeftHandTarget
            : weaponRig != null ? 1f : 0f;
        float aimTarget = actionSecondsRemaining > 0f
            ? actionAimTarget
            : aiming ? 1f : 0f;
        float handSpeed = handTarget < LeftHandWeight
            ? releaseSpeed
            : recoverySpeed;
        float aimSpeed = aimTarget < AimConstraintWeight
            ? releaseSpeed
            : recoverySpeed;
        LeftHandWeight = Mathf.MoveTowards(
            LeftHandWeight, handTarget, handSpeed * safeDelta);
        AimConstraintWeight = Mathf.MoveTowards(
            AimConstraintWeight, aimTarget, aimSpeed * safeDelta);
        UpdateAimMarkers();
    }

    private void Update()
    {
        Tick(Time.unscaledDeltaTime);
    }

    private void LateUpdate()
    {
        UpdateAimMarkers();
    }

    private void OnAnimatorIK(int layerIndex)
    {
        if (animator == null || weaponRig == null ||
            layerIndex != upperBodyLayerIndex)
        {
            return;
        }

        ApplyAimConstraint();
        ApplyLeftHandIk();
    }

    private void ApplyAimConstraint()
    {
        Transform chest = animator.GetBoneTransform(
                              HumanBodyBones.UpperChest) ??
                          animator.GetBoneTransform(HumanBodyBones.Chest) ??
                          animator.GetBoneTransform(HumanBodyBones.Spine);
        Vector3 origin = chest != null
            ? chest.position
            : transform.position + Vector3.up * 1.35f;
        animator.SetLookAtWeight(
            AimConstraintWeight,
            bodyWeight: 0.18f,
            headWeight: 0.08f,
            eyesWeight: 0f,
            clampWeight: 0.72f);
        animator.SetLookAtPosition(origin + AimDirection * 12f);
    }

    private void ApplyLeftHandIk()
    {
        Transform grip = weaponRig.LeftHandGrip;
        Transform hand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
        if (grip == null || hand == null) return;
        if (!capturedGripRotation)
        {
            gripRotationOffset = Quaternion.Inverse(grip.rotation) *
                                 hand.rotation;
            capturedGripRotation = true;
        }

        animator.SetIKPositionWeight(
            AvatarIKGoal.LeftHand, LeftHandWeight);
        animator.SetIKRotationWeight(
            AvatarIKGoal.LeftHand,
            LeftHandWeight * handRotationWeight);
        animator.SetIKPosition(AvatarIKGoal.LeftHand, grip.position);
        animator.SetIKRotation(
            AvatarIKGoal.LeftHand,
            grip.rotation * gripRotationOffset);

        Transform upperArm = animator.GetBoneTransform(
            HumanBodyBones.LeftUpperArm);
        if (upperArm == null || aimReference == null) return;
        Vector3 elbowHint = upperArm.position - aimReference.right * 0.5f +
                            aimReference.forward * 0.08f -
                            aimReference.up * 0.08f;
        animator.SetIKHintPositionWeight(
            AvatarIKHint.LeftElbow,
            LeftHandWeight * elbowHintWeight);
        animator.SetIKHintPosition(AvatarIKHint.LeftElbow, elbowHint);
    }

    private void UpdateAimMarkers()
    {
        if (weaponRig == null) return;
        Vector3 direction = AimDirection;
        Vector3 up = aimReference != null ? aimReference.up : Vector3.up;
        Quaternion rotation = Quaternion.LookRotation(direction, up);
        if (weaponRig.MuzzlePoint != null)
            weaponRig.MuzzlePoint.rotation = rotation;
        if (weaponRig.AimPoint != null)
            weaponRig.AimPoint.rotation = rotation;
    }

    public static Vector3 ResolveAimDirection(
        Transform reference,
        float pitchDegrees)
    {
        if (reference == null) return Vector3.forward;
        float constrained = Mathf.Clamp(
            float.IsFinite(pitchDegrees) ? pitchDegrees : 0f,
            -MaximumAimPitchDegrees,
            MaximumAimPitchDegrees);
        return (Quaternion.AngleAxis(constrained, reference.right) *
                reference.forward).normalized;
    }

    public static float ResolvePresentationAimParameter(float pitchDegrees)
    {
        float presentationPitch = Mathf.Clamp(
            float.IsFinite(pitchDegrees) ? pitchDegrees : 0f,
            -MaximumPresentationPitchDegrees,
            MaximumPresentationPitchDegrees);
        return presentationPitch / 89f;
    }
}
