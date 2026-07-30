using UnityEngine;

public sealed class WeaponImpactFeedbackController : MonoBehaviour
{
    private WeaponController weapon;
    private CombatEffectPool effectPool;

    public int FeedbackCount { get; private set; }
    public SurfaceType LastSurface { get; private set; }
    public CombatEffectPool EffectPool => effectPool;

    public void Configure(
        WeaponController sourceWeapon,
        GameObject effectPrefab)
    {
        if (weapon != null)
        {
            weapon.ShotResolved -= Present;
        }

        weapon = sourceWeapon;
        PlayerCombatController player = sourceWeapon != null
            ? sourceWeapon.GetComponentInParent<PlayerCombatController>()
            : null;
        Transform poolOwner = player != null
            ? player.transform
            : sourceWeapon != null
                ? sourceWeapon.transform.root
                : transform;
        effectPool = CombatEffectPool.Ensure(
            poolOwner,
            effectPrefab);

        if (weapon != null)
        {
            weapon.ShotResolved += Present;
        }
    }

    private void OnDestroy()
    {
        if (weapon != null)
        {
            weapon.ShotResolved -= Present;
        }
    }

    public void Present(ShotResult result)
    {
        if (!result.DidHit)
        {
            return;
        }

        FeedbackCount++;
        LastSurface = result.Surface;
        effectPool?.PresentImpact(result);
    }
}
