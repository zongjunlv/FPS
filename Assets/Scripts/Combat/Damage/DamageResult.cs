public enum HitRegion
{
    Generic,
    Body,
    Head
}

public readonly struct DamageResult
{
    public DamageResult(
        bool wasApplied,
        bool wasKilled,
        float appliedAmount,
        HitRegion region)
    {
        WasApplied = wasApplied;
        WasKilled = wasKilled;
        AppliedAmount = appliedAmount;
        Region = region;
    }

    public bool WasApplied { get; }
    public bool WasKilled { get; }
    public float AppliedAmount { get; }
    public HitRegion Region { get; }

    public static DamageResult None =>
        new DamageResult(false, false, 0f, HitRegion.Generic);

    public DamageResult WithRegion(HitRegion region)
    {
        return new DamageResult(
            WasApplied,
            WasKilled,
            AppliedAmount,
            region);
    }
}
