using FPS.Networking.Domain;
using FPS.Networking.Netcode;
using FPS.Networking.Session;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class NetworkCombatFeedbackPresenter : MonoBehaviour
{
    private static ShotTracerPool sharedTracers;
    private static CombatEffectPool sharedImpacts;
    private static NetworkWeaponFeedbackPool sharedWeaponEffects;

    private NetworkPlayerReplica replica;
    private PlayerCombatController localCombat;
    private CoopNetworkWorldPresenter worldPresenter;

    public int PresentedShotCount { get; private set; }
    public int PresentedHitCount { get; private set; }
    public NetcodeShotFeedbackEvent LastEvent { get; private set; }

    public void Configure(
        NetworkPlayerReplica source,
        PlayerCombatController combat = null)
    {
        if (replica != null)
            replica.ShotFeedbackReceived -= Present;
        replica = source;
        localCombat = combat;
        if (replica != null)
            replica.ShotFeedbackReceived += Present;
        EnsureSharedPools();
    }

    private void OnDestroy()
    {
        if (replica != null)
            replica.ShotFeedbackReceived -= Present;
    }

    public void Present(NetcodeShotFeedbackEvent value)
    {
        PresentedShotCount++;
        LastEvent = value;
        GameObject target = ResolveTarget(value.TargetId);
        bool killed = value.Kind == ShotResolutionKind.Killed;
        HitRegion region = value.HitRegion == AuthoritativeHitRegion.Head
            ? HitRegion.Head
            : value.HitRegion == AuthoritativeHitRegion.Body
                ? HitRegion.Body
                : HitRegion.Generic;
        var damage = value.DidHit
            ? new DamageResult(true, killed, value.AppliedDamage, region)
            : DamageResult.None;
        SurfaceType surface = value.Surface == AuthoritativeSurface.Metal
            ? SurfaceType.Metal
            : SurfaceType.Concrete;
        var result = value.DidImpact
            ? new ShotResult(true, value.EndPoint, value.Normal, surface,
                damage, target, target)
            : ShotResult.Miss;
        Vector3 shotDirection = value.EndPoint - value.Origin;

        if (localCombat != null && replica != null &&
            replica.IsLocallyControlled &&
            localCombat.PresentAuthoritativeNetworkShot(
                value.WeaponId.ToString(), value.Origin, value.EndPoint,
                result))
        {
            sharedWeaponEffects?.EjectCasing(
                value.Origin, shotDirection);
            if (value.DidHit) PresentedHitCount++;
            return;
        }

        sharedWeaponEffects?.PlayMuzzle(value.Origin, shotDirection);
        sharedWeaponEffects?.EjectCasing(value.Origin, shotDirection);
        sharedTracers?.Play(value.Origin, value.EndPoint, 280f);
        if (value.DidHit)
        {
            PresentedHitCount++;
            sharedImpacts?.PresentImpact(result);
        }
    }

    private GameObject ResolveTarget(int targetId)
    {
        if (targetId <= 0) return null;
        if (worldPresenter == null)
            worldPresenter = FindFirstObjectByType<CoopNetworkWorldPresenter>();
        return worldPresenter != null &&
               worldPresenter.TryGetTargetView(targetId, out GameObject view)
            ? view
            : null;
    }

    private static void EnsureSharedPools()
    {
        if (sharedTracers != null && sharedImpacts != null &&
            sharedWeaponEffects != null) return;
        GameObject host = GameObject.Find("Network Combat Feedback Pools");
        if (host == null)
        {
            host = new GameObject("Network Combat Feedback Pools");
            Object.DontDestroyOnLoad(host);
        }
        sharedTracers ??= host.GetComponent<ShotTracerPool>() ??
            host.AddComponent<ShotTracerPool>();
        sharedImpacts ??= CombatEffectPool.Ensure(host.transform, null);
        sharedWeaponEffects ??=
            host.GetComponent<NetworkWeaponFeedbackPool>() ??
            host.AddComponent<NetworkWeaponFeedbackPool>();
    }
}
