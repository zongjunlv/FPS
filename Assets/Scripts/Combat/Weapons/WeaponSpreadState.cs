using UnityEngine;

public sealed class WeaponSpreadState
{
    private float hipSpread;
    private float adsSpread;
    private float movementBonus;
    private float sprintBonus;
    private float bloomPerShot;
    private float maxBloom;
    private float recoverySpeed;
    private float aimBlend;
    private float moveAmount;
    private bool sprinting;
    private float bloom;

    public float CurrentSpreadDegrees =>
        Mathf.Lerp(hipSpread, adsSpread, aimBlend) +
        movementBonus * moveAmount +
        (sprinting ? sprintBonus : 0f) +
        bloom;

    public float CurrentBloom => bloom;

    public void Configure(
        float hip,
        float ads,
        float movement,
        float sprint,
        float perShot,
        float maximumBloom,
        float recovery)
    {
        hipSpread = Mathf.Max(0f, hip);
        adsSpread = Mathf.Max(0f, ads);
        movementBonus = Mathf.Max(0f, movement);
        sprintBonus = Mathf.Max(0f, sprint);
        bloomPerShot = Mathf.Max(0f, perShot);
        maxBloom = Mathf.Max(0f, maximumBloom);
        recoverySpeed = Mathf.Max(0f, recovery);
    }

    public void SetContext(
        float adsBlend,
        float movementAmount,
        bool isSprinting)
    {
        aimBlend = Mathf.Clamp01(adsBlend);
        moveAmount = Mathf.Clamp01(movementAmount);
        sprinting = isSprinting;
    }

    public void RegisterShot()
    {
        bloom = Mathf.Min(maxBloom, bloom + bloomPerShot);
    }

    public void Tick(float deltaTime)
    {
        bloom = Mathf.MoveTowards(
            bloom,
            0f,
            recoverySpeed * Mathf.Max(0f, deltaTime));
    }

    public static Vector3 ApplySpread(
        Vector3 forward,
        Vector3 right,
        Vector3 up,
        float spreadDegrees,
        Vector2 diskSample)
    {
        Vector2 sample = Vector2.ClampMagnitude(diskSample, 1f);
        float tangent = Mathf.Tan(
            Mathf.Deg2Rad * Mathf.Max(0f, spreadDegrees));
        return (
            forward.normalized +
            right.normalized * sample.x * tangent +
            up.normalized * sample.y * tangent).normalized;
    }
}
