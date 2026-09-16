using System;
using UnityEngine;

[DisallowMultipleComponent]
[DefaultExecutionOrder(120)]
public sealed class TutorialShootingTarget : MonoBehaviour
{
    private const float PointTolerance = 0.015f;

    [SerializeField] private PlayerCombatController combat;
    [SerializeField] private Collider wallCollider;
    [SerializeField] private BoxCollider hitRegion;

    private bool subscribed;

    public PlayerCombatController Combat => combat;
    public Collider WallCollider => wallCollider;
    public BoxCollider HitRegion => hitRegion;
    public int EvaluatedShotCount { get; private set; }
    public int ValidHitCount { get; private set; }
    public int RejectedHitCount { get; private set; }
    public ShotResult LastShot { get; private set; }
    public bool LastShotWasValid { get; private set; }

    public event Action<ShotResult, bool> ShotEvaluated;
    public event Action<ShotResult> ValidHitRecorded;

    public void Configure(
        PlayerCombatController configuredCombat,
        Collider configuredWallCollider,
        BoxCollider configuredHitRegion)
    {
        Unsubscribe();
        combat = configuredCombat;
        wallCollider = configuredWallCollider;
        hitRegion = configuredHitRegion;

        if (Application.isPlaying && isActiveAndEnabled)
        {
            Subscribe();
        }
    }

    public bool TryValidate(out string error)
    {
        if (combat == null || wallCollider == null || hitRegion == null)
        {
            error = "教学射击靶缺少玩家战斗组件、墙体碰撞体或命中区域。";
            return false;
        }

        if (wallCollider.isTrigger)
        {
            error = "教学射击墙必须使用实体碰撞体。";
            return false;
        }

        if (!hitRegion.isTrigger ||
            hitRegion.gameObject.layer != LayerMask.NameToLayer(
                "Ignore Raycast"))
        {
            error = "教学命中区域必须是位于 Ignore Raycast 层的触发器。";
            return false;
        }

        if (!hitRegion.transform.IsChildOf(transform) ||
            hitRegion.size.x <= 0f || hitRegion.size.y <= 0f ||
            hitRegion.size.z <= 0f)
        {
            error = "教学命中区域的层级或尺寸无效。";
            return false;
        }

        error = string.Empty;
        return true;
    }

    public bool IsValidHit(ShotResult result)
    {
        if (!result.DidHit || result.HitObject == null ||
            wallCollider == null || hitRegion == null)
        {
            return false;
        }

        Transform hitTransform = result.HitObject.transform;
        Transform wallTransform = wallCollider.transform;
        bool hitTeachingWall = hitTransform == wallTransform ||
                               hitTransform.IsChildOf(wallTransform);
        return hitTeachingWall && ContainsPoint(result.Point);
    }

    public bool EvaluateShot(ShotResult result)
    {
        bool valid = IsValidHit(result);
        EvaluatedShotCount++;
        LastShot = result;
        LastShotWasValid = valid;

        if (valid)
        {
            ValidHitCount++;
            ValidHitRecorded?.Invoke(result);
        }
        else
        {
            RejectedHitCount++;
        }

        ShotEvaluated?.Invoke(result, valid);
        return valid;
    }

    public void ResetEvidence()
    {
        EvaluatedShotCount = 0;
        ValidHitCount = 0;
        RejectedHitCount = 0;
        LastShot = ShotResult.Miss;
        LastShotWasValid = false;
    }

    private bool ContainsPoint(Vector3 worldPoint)
    {
        Vector3 localPoint = hitRegion.transform.InverseTransformPoint(
            worldPoint) - hitRegion.center;
        Vector3 halfSize = hitRegion.size * 0.5f;
        return Mathf.Abs(localPoint.x) <= halfSize.x + PointTolerance &&
               Mathf.Abs(localPoint.y) <= halfSize.y + PointTolerance &&
               Mathf.Abs(localPoint.z) <= halfSize.z + PointTolerance;
    }

    private void Start()
    {
        if (!TryValidate(out string error))
        {
            Debug.LogError(
                $"[{nameof(TutorialShootingTarget)}] {error}",
                this);
            enabled = false;
            return;
        }

        Subscribe();
    }

    private void OnEnable()
    {
        if (Application.isPlaying)
        {
            Subscribe();
        }
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    private void Subscribe()
    {
        if (subscribed || combat == null)
        {
            return;
        }

        combat.ShotResolved += HandleShotResolved;
        subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!subscribed || combat == null)
        {
            subscribed = false;
            return;
        }

        combat.ShotResolved -= HandleShotResolved;
        subscribed = false;
    }

    private void HandleShotResolved(ShotResult result)
    {
        EvaluateShot(result);
    }

    private void OnDestroy()
    {
        Unsubscribe();
    }
}
