using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FPS.Tests.PlayMode
{
    public sealed class Issue57UtilityAiLifecycleTests
    {
        [UnityTest]
        public IEnumerator RaiderReachesTrueFlankBeforeChargingOrAttacking()
        {
            GameObject target = new("Issue57 Target");
            target.transform.position = Vector3.forward * 12f;
            GameObject enemyObject = new("Issue57 Raider");
            EnemyController enemy = enemyObject.AddComponent<EnemyController>();
            EnemyUtilityProfileDefinition profile = CreateProfile();
            RaiderApproachAbilityDefinition raider = CreateRaider(profile);
            EnemyAbilitySetDefinition set = CreateSet(raider);
            yield return null;

            try
            {
                enemy.ResetForSpawn(target.transform);
                EnemyAbilityController abilities = enemy.AbilityController;
                EnemyNavigationController navigation =
                    enemy.GetComponent<EnemyNavigationController>();
                Assert.That(abilities.ApplyAbilitySet(set, target.transform),
                    Is.True);

                EnemyMovementDirective first = abilities.ResolveMovement(
                    target.transform,
                    target.transform.position,
                    true,
                    12f,
                    0.016f);
                Assert.That(first.Kind, Is.EqualTo(
                    EnemyMovementDirectiveKind.Move));
                Assert.That(abilities.LastUtilityDecision.SelectedAction.Kind,
                    Is.EqualTo(EnemyUtilityActionKind.Flank));
                Vector3 flank = first.Destination;
                Vector3 targetToFlank = flank - target.transform.position;
                Assert.That(Vector3.Dot(
                    targetToFlank,
                    target.transform.forward), Is.LessThan(-0.5f));
                Assert.That(Mathf.Abs(Vector3.Dot(
                    targetToFlank,
                    target.transform.right)), Is.GreaterThan(2f));

                navigation.SetDestination(flank);
                enemyObject.transform.position =
                    target.transform.position - target.transform.forward * 1.5f;
                EnemyMovementDirective nearPlayer = abilities.ResolveMovement(
                    target.transform,
                    target.transform.position,
                    true,
                    1.5f,
                    0.1f);
                Assert.That(nearPlayer.Kind,
                    Is.EqualTo(EnemyMovementDirectiveKind.Move),
                    "尚未抵达侧后方时不能因靠近玩家而提前攻击。");
                Assert.That(nearPlayer.Destination, Is.EqualTo(flank));

                enemyObject.transform.position = flank;
                EnemyMovementDirective charge = abilities.ResolveMovement(
                    target.transform,
                    target.transform.position,
                    true,
                    Vector3.Distance(flank, target.transform.position),
                    0.1f);
                Assert.That(charge.Kind,
                    Is.EqualTo(EnemyMovementDirectiveKind.Move));
                Assert.That(charge.Destination,
                    Is.EqualTo(target.transform.position));
                Assert.That(abilities.Phase,
                    Is.EqualTo(RaiderTacticsPhase.Charging));

                abilities.ClearForPool();
                Assert.That(abilities.UsesUtilityAi, Is.False);
                Assert.That(abilities.LastUtilityDecision, Is.Null);
            }
            finally
            {
                Object.Destroy(enemyObject);
                Object.Destroy(target);
                Object.Destroy(set);
                Object.Destroy(raider);
                Object.Destroy(profile);
            }

            yield return null;
        }

        private static EnemyUtilityProfileDefinition CreateProfile()
        {
            EnemyUtilityProfileDefinition profile =
                ScriptableObject.CreateInstance<EnemyUtilityProfileDefinition>();
            profile.Configure(
                "test.raider.utility",
                "raider.chase",
                12f,
                new[]
                {
                    new EnemyUtilityActionDefinition(
                        "raider.chase",
                        "追击",
                        EnemyUtilityActionKind.Chase,
                        0.5f,
                        0.05f,
                        0f,
                        0.25f,
                        0.1f,
                        0f,
                        1f,
                        "raider.chase",
                        new EnemyUtilityConsiderationDefinition[0]),
                    new EnemyUtilityActionDefinition(
                        "raider.flank",
                        "侧翼突袭",
                        EnemyUtilityActionKind.Flank,
                        1f,
                        0.1f,
                        3f,
                        2.5f,
                        0.15f,
                        5f,
                        1.35f,
                        "raider.chase",
                        new[]
                        {
                            new EnemyUtilityConsiderationDefinition(
                                EnemyUtilityFactId.HasLineOfSight,
                                EnemyUtilityResponseCurve.BooleanTrue),
                            new EnemyUtilityConsiderationDefinition(
                                EnemyUtilityFactId.TargetDistance,
                                EnemyUtilityResponseCurve.Rising,
                                3f,
                                12f,
                                1f,
                                0.8f)
                        }),
                    new EnemyUtilityActionDefinition(
                        "raider.retreat",
                        "战术撤退",
                        EnemyUtilityActionKind.Retreat,
                        1.1f,
                        0.1f,
                        3f,
                        1f,
                        0.1f,
                        6f,
                        1.4f,
                        "raider.chase",
                        new[]
                        {
                            new EnemyUtilityConsiderationDefinition(
                                EnemyUtilityFactId.HealthRatio,
                                EnemyUtilityResponseCurve.Falling,
                                0.2f,
                                0.6f)
                        })
                });
            return profile;
        }

        private static RaiderApproachAbilityDefinition CreateRaider(
            EnemyUtilityProfileDefinition profile)
        {
            RaiderApproachAbilityDefinition raider =
                ScriptableObject.CreateInstance<
                    RaiderApproachAbilityDefinition>();
            raider.Configure(
                "test.raider",
                5f,
                2f,
                2f,
                4.5f,
                1.35f,
                1.75f,
                1f,
                0.5f,
                2.3f,
                0.2f,
                0.8f,
                0.8f);
            raider.ConfigureUtilityProfile(profile);
            return raider;
        }

        private static EnemyAbilitySetDefinition CreateSet(
            RaiderApproachAbilityDefinition raider)
        {
            EnemyAbilitySetDefinition set =
                ScriptableObject.CreateInstance<EnemyAbilitySetDefinition>();
            set.Configure(
                "test.raider.set",
                "RAIDER",
                Color.cyan,
                new EnemyAbilityDefinition[] { raider });
            return set;
        }
    }
}
