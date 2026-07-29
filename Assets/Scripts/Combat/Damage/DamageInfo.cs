using UnityEngine;

public readonly struct DamageInfo
{
    public DamageInfo(
        float amount,
        Vector3 hitPoint,
        Vector3 hitDirection,
        GameObject source)
    {
        Amount = Mathf.Max(0f, amount);
        HitPoint = hitPoint;
        HitDirection = hitDirection.sqrMagnitude > 0.0001f
            ? hitDirection.normalized
            : Vector3.zero;
        Source = source;
    }

    public float Amount { get; }
    public Vector3 HitPoint { get; }
    public Vector3 HitDirection { get; }
    public GameObject Source { get; }

    public DamageInfo WithAmount(float amount)
    {
        return new DamageInfo(
            amount,
            HitPoint,
            HitDirection,
            Source);
    }
}
