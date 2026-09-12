using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Produces one immutable read-only fact snapshot for the Utility AI decision
/// loop. It never issues movement, combat, or support commands.
/// </summary>
public sealed class EnemyUtilityWorldFactCollector
{
    private readonly List<EnemyController> neighbors = new(16);

    public EnemyUtilityWorldFacts Capture(
        EnemyController owner,
        Transform target,
        bool hasLineOfSight,
        EnemyUtilityProfileDefinition profile,
        IEnemyNeighborQuery neighborQuery)
    {
        Vector3 ownerPosition = owner != null
            ? owner.transform.position
            : Vector3.zero;
        Vector3 targetPosition = target != null
            ? target.position
            : ownerPosition;
        float distance = Vector3.Distance(ownerPosition, targetPosition);
        Health health = owner != null ? owner.GetComponent<Health>() : null;
        float healthRatio = health != null && health.MaxHealth > Mathf.Epsilon
            ? health.CurrentHealth / health.MaxHealth
            : 1f;
        int raiders = 0;
        int suppressors = 0;
        int supporters = 0;
        bool supportCoverage = owner?.SupportEffects != null &&
            owner.SupportEffects.ActiveSourceCount > 0;
        neighbors.Clear();

        if (owner != null && neighborQuery != null && profile != null)
        {
            neighborQuery.CollectAliveNeighbors(
                owner,
                profile.FriendlyScanRadius,
                "enemy",
                neighbors);

            for (int index = 0; index < neighbors.Count; index++)
            {
                EnemyController neighbor = neighbors[index];
                EnemyAbilityController abilities = neighbor != null
                    ? neighbor.AbilityController
                    : null;

                if (abilities == null)
                {
                    continue;
                }

                if (abilities.IsRaider)
                {
                    raiders++;
                }

                if (abilities.IsSuppressor)
                {
                    suppressors++;
                }

                if (!abilities.IsSupport)
                {
                    continue;
                }

                supporters++;
                Vector3 supportOffset = neighbor.transform.position -
                    owner.transform.position;
                supportOffset.y = 0f;
                supportCoverage |= supportOffset.sqrMagnitude <=
                    abilities.SupportRadius * abilities.SupportRadius;
            }
        }

        return new EnemyUtilityWorldFacts(
            distance,
            hasLineOfSight,
            target != null && !hasLineOfSight,
            healthRatio,
            raiders,
            suppressors,
            supporters,
            supportCoverage);
    }
}
