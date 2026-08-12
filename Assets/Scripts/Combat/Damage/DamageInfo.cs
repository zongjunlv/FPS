using UnityEngine;

public enum DamageType
{
    Unknown,
    Hitscan,
    Projectile,
    Melee,
    Environment
}

public readonly struct DamageInfo
{
    public DamageInfo(
        float amount,
        Vector3 hitPoint,
        Vector3 hitDirection,
        GameObject source)
        : this(
            amount,
            hitPoint,
            hitDirection,
            source,
            DamageType.Unknown)
    {
    }

    public DamageInfo(
        float amount,
        Vector3 hitPoint,
        Vector3 hitDirection,
        GameObject source,
        DamageType damageType)
    {
        Amount = Mathf.Max(0f, amount);
        HitPoint = hitPoint;
        HitDirection = hitDirection.sqrMagnitude > 0.0001f
            ? hitDirection.normalized
            : Vector3.zero;
        Source = source;
        Type = damageType;
    }

    public float Amount { get; }
    public Vector3 HitPoint { get; }
    public Vector3 HitDirection { get; }
    public GameObject Source { get; }
    public DamageType Type { get; }

    public DamageInfo WithAmount(float amount)
    {
        return new DamageInfo(
            amount,
            HitPoint,
            HitDirection,
            Source,
            Type);
    }
}
