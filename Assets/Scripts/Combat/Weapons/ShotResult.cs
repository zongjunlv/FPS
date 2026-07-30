using UnityEngine;

public enum SurfaceType
{
    Concrete,
    Metal
}

public enum HitFeedbackKind
{
    None,
    Normal,
    Headshot,
    Kill
}

public readonly struct ShotResult
{
    public ShotResult(
        bool didHit,
        Vector3 point,
        Vector3 normal,
        SurfaceType surface,
        DamageResult damage)
        : this(
            didHit,
            point,
            normal,
            surface,
            damage,
            null)
    {
    }

    public ShotResult(
        bool didHit,
        Vector3 point,
        Vector3 normal,
        SurfaceType surface,
        DamageResult damage,
        GameObject damageTarget)
    {
        DidHit = didHit;
        Point = point;
        Normal = normal;
        Surface = surface;
        Damage = damage;
        DamageTarget = damageTarget;
    }

    public bool DidHit { get; }
    public Vector3 Point { get; }
    public Vector3 Normal { get; }
    public SurfaceType Surface { get; }
    public DamageResult Damage { get; }
    public GameObject DamageTarget { get; }

    public HitFeedbackKind FeedbackKind
    {
        get
        {
            if (!Damage.WasApplied)
            {
                return HitFeedbackKind.None;
            }

            if (Damage.WasKilled)
            {
                return HitFeedbackKind.Kill;
            }

            return Damage.Region == HitRegion.Head
                ? HitFeedbackKind.Headshot
                : HitFeedbackKind.Normal;
        }
    }

    public static ShotResult Miss =>
        new ShotResult(
            false,
            Vector3.zero,
            Vector3.zero,
            SurfaceType.Concrete,
            DamageResult.None);
}
