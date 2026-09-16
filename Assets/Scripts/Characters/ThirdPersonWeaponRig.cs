using UnityEngine;

[DisallowMultipleComponent]
public sealed class ThirdPersonWeaponRig : MonoBehaviour
{
    public const string CalibrationRootName = "WeaponCalibrationRoot";
    public const string RightHandReferenceName = "RightHandReference";
    public const string LeftHandGripName = "LeftHandGrip";
    public const string AimPointName = "AimPoint";
    public const string MuzzlePointName = "MuzzlePoint";
    public const string CasingEjectionPointName = "CasingEjectionPoint";

    [SerializeField] private Transform calibrationRoot;
    [SerializeField] private Transform modelRoot;
    [SerializeField] private Transform rightHandReference;
    [SerializeField] private Transform leftHandGrip;
    [SerializeField] private Transform aimPoint;
    [SerializeField] private Transform muzzlePoint;
    [SerializeField] private Transform casingEjectionPoint;

    public Transform CalibrationRoot => calibrationRoot;
    public Transform ModelRoot => modelRoot;
    public Transform RightHandReference => rightHandReference;
    public Transform LeftHandGrip => leftHandGrip;
    public Transform AimPoint => aimPoint;
    public Transform MuzzlePoint => muzzlePoint;
    public Transform CasingEjectionPoint => casingEjectionPoint;

    public void Configure(
        Transform calibratedRoot,
        Transform model,
        Transform rightHand,
        Transform leftHand,
        Transform aim,
        Transform muzzle,
        Transform casingEjection)
    {
        calibrationRoot = calibratedRoot;
        modelRoot = model;
        rightHandReference = rightHand;
        leftHandGrip = leftHand;
        aimPoint = aim;
        muzzlePoint = muzzle;
        casingEjectionPoint = casingEjection;
    }

    public bool TryValidate(out string error)
    {
        if (calibrationRoot == null || modelRoot == null ||
            rightHandReference == null || leftHandGrip == null ||
            aimPoint == null || muzzlePoint == null)
        {
            error = "缺少校准根、模型、右手基准、左手握点、瞄准点或枪口点。";
            return false;
        }
        if (!calibrationRoot.IsChildOf(transform) ||
            !modelRoot.IsChildOf(calibrationRoot) ||
            !leftHandGrip.IsChildOf(calibrationRoot) ||
            !aimPoint.IsChildOf(calibrationRoot) ||
            !muzzlePoint.IsChildOf(calibrationRoot) ||
            !rightHandReference.IsChildOf(transform))
        {
            error = "校准点不在规定的 WeaponCalibrationRoot 层级中。";
            return false;
        }
        Vector3 scale = calibrationRoot.localScale;
        float minimum = Mathf.Min(scale.x, Mathf.Min(scale.y, scale.z));
        float maximum = Mathf.Max(scale.x, Mathf.Max(scale.y, scale.z));
        if (!IsFinite(scale) || minimum < 0.1f || maximum > 4f ||
            maximum / minimum > 1.05f)
        {
            error = "校准根缩放异常，必须为 0.1–4 范围内的近似等比正缩放。";
            return false;
        }
        if (transform.InverseTransformPoint(rightHandReference.position)
                .sqrMagnitude > 0.0001f)
        {
            error = "右手基准必须与 WeaponSocket 原点重合。";
            return false;
        }
        Vector3 muzzle = calibrationRoot.InverseTransformPoint(
            muzzlePoint.position);
        Vector3 aim = calibrationRoot.InverseTransformPoint(aimPoint.position);
        Vector3 leftGrip = calibrationRoot.InverseTransformPoint(
            leftHandGrip.position);
        if (muzzle.z <= aim.z + 0.05f || muzzle.z <= leftGrip.z)
        {
            error = "枪口必须位于瞄准点和左手握点前方。";
            return false;
        }
        if (Vector3.Dot(muzzlePoint.forward, calibrationRoot.forward) < 0.9f ||
            Vector3.Dot(aimPoint.forward, calibrationRoot.forward) < 0.9f)
        {
            error = "枪口或瞄准点方向与校准根 +Z 前向不一致。";
            return false;
        }
        if (Mathf.Abs(aim.x - muzzle.x) > 0.2f ||
            Mathf.Abs(aim.y - muzzle.y) > 0.25f)
        {
            error = "瞄准点与枪口偏离过大。";
            return false;
        }
        if (modelRoot.GetComponentsInChildren<Renderer>(true).Length == 0)
        {
            error = "武器模型没有 Renderer。";
            return false;
        }
        error = string.Empty;
        return true;
    }

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.x) && float.IsFinite(value.y) &&
        float.IsFinite(value.z);
}
