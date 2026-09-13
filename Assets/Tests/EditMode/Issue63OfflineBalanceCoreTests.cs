using System.Linq;
using FPS.GameplayEffects;
using FPS.Simulation;
using FPS.Simulation.Offline;
using NUnit.Framework;
using UnityEngine;

namespace FPS.Tests.Architecture
{
    public sealed class Issue63OfflineBalanceCoreTests
    {
        [Test]
        public void SameSeedDrivesAuthoritativeSystemsAndProducesStableResult()
        {
            OfflineBalanceScenario scenario = CreateHealthyScenario();

            OfflineRunResult first = OfflineBalanceSimulator.Run(
                scenario,
                63001);
            OfflineRunResult second = OfflineBalanceSimulator.Run(
                scenario,
                63001);

            Assert.That(first.Outcome, Is.EqualTo(OfflineRunOutcome.Victory));
            Assert.That(second.Digest, Is.EqualTo(first.Digest));
            Assert.That(second.DurationTicks, Is.EqualTo(first.DurationTicks));
            Assert.That(second.AmmoConsumed, Is.EqualTo(first.AmmoConsumed));
            Assert.That(first.Commands.Select(command => command.Type),
                Does.Contain(SimulationCommandType.StartRun));
            Assert.That(first.Commands.Select(command => command.Type),
                Does.Contain(SimulationCommandType.EnemySpawned));
            Assert.That(first.Commands.Select(command => command.Type),
                Does.Contain(SimulationCommandType.EnemySettled));
            Assert.That(first.DirectorEvents, Is.Not.Empty);
            Assert.That(first.EncounterEvents, Is.Not.Empty);
            Assert.That(first.CombatRuleEvents, Is.Not.Empty);
            Assert.That(first.CombatRuleTriggerDistribution, Is.Not.Empty);
            Assert.That(first.CombatRuleEffectsExpanded, Is.False);
            Assert.That(first.CombatRuleAbstractionNote, Is.Not.Empty);
            Assert.That(first.RoleDistribution.Sum(entry => entry.Count),
                Is.EqualTo(first.WaveDistribution.Sum(entry => entry.Count)));
            Assert.That(OfflineRunReplay.Verify(scenario, first).IsMatch,
                Is.True);
        }

        [Test]
        public void InputOrderAndParallelismDoNotChangePerSeedResults()
        {
            OfflineBalanceScenario scenario = CreateHealthyScenario();
            long[] ascending = Enumerable.Range(0, 64)
                .Select(index => 64000L + index)
                .ToArray();
            long[] descending = ascending.Reverse().ToArray();

            var sequential = OfflineBalanceBatchRunner.Run(
                scenario,
                ascending,
                1);
            var parallel = OfflineBalanceBatchRunner.Run(
                scenario,
                descending,
                4);

            Assert.That(sequential.Select(result => result.Seed),
                Is.Ordered.Ascending);
            Assert.That(parallel.Select(result => result.Seed),
                Is.EqualTo(sequential.Select(result => result.Seed)));
            Assert.That(parallel.Select(result => result.Digest),
                Is.EqualTo(sequential.Select(result => result.Digest)));
        }

        [Test]
        public void BatchSupportsAtLeastOneThousandSeedsInCanonicalOrder()
        {
            OfflineBalanceScenario scenario = CreateMinimalScenario();
            long[] seeds = Enumerable.Range(0, 1000)
                .Select(index => 73000L - index)
                .ToArray();

            var results = OfflineBalanceBatchRunner.Run(
                scenario,
                seeds,
                4);

            Assert.That(results, Has.Count.EqualTo(1000));
            Assert.That(results.Select(result => result.Seed),
                Is.Ordered.Ascending);
            Assert.That(results.All(result =>
                !string.IsNullOrWhiteSpace(result.Digest)), Is.True);
        }

        [Test]
        public void AmmunitionExhaustionIsReportedSeparatelyFromPlayerDefeat()
        {
            OfflineBalanceScenario scenario = CreateScenario(
                player: new OfflinePlayerSpec(
                    100f,
                    50f,
                    10f,
                    1,
                    1),
                enemies: new[]
                {
                    new OfflineEnemyArchetype(
                        "tank",
                        "elite",
                        5,
                        100f,
                        0f,
                        30)
                },
                waves: new[]
                {
                    new OfflineWaveSpec(
                        1,
                        0,
                        new[] { new OfflineWaveEntry("tank", 1) })
                });

            OfflineRunResult result = OfflineBalanceSimulator.Run(
                scenario,
                63004);

            Assert.That(result.Outcome, Is.EqualTo(OfflineRunOutcome.Defeat));
            Assert.That(result.FailureReason,
                Is.EqualTo(OfflineFailureReason.ResourceDepleted));
            Assert.That(result.AmmoConsumed, Is.EqualTo(1));
            Assert.That(result.HealthLost, Is.EqualTo(0f));
        }

        [Test]
        public void MissingOrInvulnerableRosterIsReportedAsUnsolvable()
        {
            OfflineBalanceScenario missingRoster = CreateScenario(
                enemies: new[]
                {
                    new OfflineEnemyArchetype(
                        "grunt",
                        "assault",
                        1,
                        10f,
                        0f,
                        30)
                },
                waves: new[]
                {
                    new OfflineWaveSpec(
                        1,
                        0,
                        new[] { new OfflineWaveEntry("unknown", 1) })
                });
            OfflineBalanceScenario noDamage = CreateScenario(
                player: new OfflinePlayerSpec(
                    100f,
                    0f,
                    0f,
                    1,
                    100),
                enemies: new[]
                {
                    new OfflineEnemyArchetype(
                        "grunt",
                        "assault",
                        1,
                        10f,
                        0f,
                        30)
                },
                waves: new[]
                {
                    new OfflineWaveSpec(
                        1,
                        0,
                        new[] { new OfflineWaveEntry("grunt", 1) })
                });

            Assert.That(OfflineBalanceSimulator.Run(missingRoster, 1)
                    .FailureReason,
                Is.EqualTo(OfflineFailureReason.UnsolvableRoster));
            Assert.That(OfflineBalanceSimulator.Run(noDamage, 1)
                    .FailureReason,
                Is.EqualTo(OfflineFailureReason.UnsolvableRoster));
        }

        [Test]
        public void AttackOpportunitySuppressesWindowsWithoutScalingDamage()
        {
            var player = new OfflinePlayerSpec(
                100f,
                0f,
                100f,
                1,
                10);
            var enemies = new[]
            {
                new OfflineEnemyArchetype(
                    "attacker",
                    "assault",
                    1,
                    1f,
                    25f,
                    30)
            };
            var waves = new[]
            {
                new OfflineWaveSpec(
                    1,
                    0,
                    new[] { new OfflineWaveEntry("attacker", 1) })
            };
            OfflineRunResult always = OfflineBalanceSimulator.Run(
                CreateScenario(
                    player,
                    enemies,
                    waves,
                    worldModel: new OfflineWorldModelSpec(10000)),
                63063);
            OfflineRunResult never = OfflineBalanceSimulator.Run(
                CreateScenario(
                    player,
                    enemies,
                    waves,
                    worldModel: new OfflineWorldModelSpec(0)),
                63063);

            Assert.That(always.HealthLost, Is.EqualTo(25f),
                "成功攻击窗口必须结算完整正式伤害。" );
            Assert.That(never.HealthLost, Is.EqualTo(0f));
            Assert.That(always.Digest, Is.Not.EqualTo(never.Digest));
            Assert.That(always.EnemyAttackOpportunityBasisPoints,
                Is.EqualTo(10000));
            Assert.That(never.WorldModelVersion,
                Is.EqualTo(OfflineWorldModelSpec.CurrentVersion));
        }

        [Test]
        public void LiveEngineAndPureKernelMakeTheSameRuleDecisions()
        {
            var effect = ScriptableObject.CreateInstance<
                CombatRuleEffectDefinition>();
            var rule = ScriptableObject.CreateInstance<
                CombatRuleDefinition>();
            var build = ScriptableObject.CreateInstance<
                CombatBuildDefinition>();
            effect.Configure(
                "effect.shared-decision",
                CombatRuleEffectKind.ShowHudMessage,
                CombatRuleTarget.EventSource,
                null,
                string.Empty,
                0f,
                0,
                "共享判定");
            rule.Configure(
                "rule.shared-decision",
                CombatTriggerType.Hit,
                new[] { "entity.player" },
                new[] { "entity.enemy" },
                new[] { "status.immune" },
                0.2f,
                0.8f,
                3,
                4300,
                effect);
            build.Configure(
                "build.shared-decision",
                "共享构筑",
                true,
                rule);

            try
            {
                var live = new CombatRuleEngine(63059, new[] { build });
                Assert.That(live.Install(build), Is.True);
                var pure = new CombatRuleDecisionKernel(
                    63059,
                    new[]
                    {
                        new CombatRuleDecisionSpec(
                            "rule.shared-decision",
                            CombatRuleDecisionTrigger.Hit,
                            new[] { "entity.player" },
                            new[] { "entity.enemy" },
                            new[] { "status.immune" },
                            0.2f,
                            0.8f,
                            3,
                            4300)
                    });

                int triggered = 0;
                for (int index = 1; index <= 40; index++)
                {
                    long tick = index - 1;
                    float health = index % 5 == 0 ? 0.1f : 0.5f;
                    string[] targetTags = index % 7 == 0
                        ? new[] { "entity.enemy", "status.immune" }
                        : new[] { "entity.enemy" };
                    CombatTriggerType liveTrigger = index % 11 == 0
                        ? CombatTriggerType.Kill
                        : CombatTriggerType.Hit;
                    var liveContext = new CombatTriggerContext(
                        index,
                        tick,
                        liveTrigger,
                        "player",
                        "enemy",
                        new[] { "entity.player" },
                        targetTags,
                        health);
                    var pureContext = new CombatRuleDecisionContext(
                        index,
                        tick,
                        (CombatRuleDecisionTrigger)liveTrigger,
                        new[] { "entity.player" },
                        targetTags,
                        health);

                    string[] liveDecisions = live.Process(liveContext)
                        .Select(value => value.RuleId)
                        .Distinct()
                        .ToArray();
                    string[] pureDecisions = pure.Process(pureContext)
                        .Select(value => value.RuleId)
                        .ToArray();
                    triggered += liveDecisions.Length;
                    Assert.That(liveDecisions,
                        Is.EqualTo(pureDecisions),
                        "event " + index);
                    Assert.That(live.Process(liveContext), Is.Empty,
                        "正式引擎必须复用共享事件去重。" );
                    Assert.That(pure.Process(pureContext), Is.Empty,
                        "纯核心必须采用相同事件去重。" );
                }
                Assert.That(triggered, Is.GreaterThan(0),
                    "概率规则必须存在实际触发样本。" );
                Assert.That(triggered, Is.LessThan(40),
                    "标签、阈值、概率与冷却必须过滤部分事件。" );
            }
            finally
            {
                Object.DestroyImmediate(build);
                Object.DestroyImmediate(rule);
                Object.DestroyImmediate(effect);
            }
        }

        private static OfflineBalanceScenario CreateHealthyScenario()
        {
            return CreateScenario(
                waves: new[]
                {
                    new OfflineWaveSpec(
                        2,
                        2,
                        new[]
                        {
                            new OfflineWaveEntry("grunt", 2),
                            new OfflineWaveEntry("support", 1)
                        }),
                    new OfflineWaveSpec(
                        2,
                        0,
                        new[]
                        {
                            new OfflineWaveEntry("grunt", 1),
                            new OfflineWaveEntry("support", 1)
                        })
                },
                encounters: new[]
                {
                    new EncounterDefinitionSpec(
                        "offline.ambush",
                        1,
                        EncounterKind.Ambush,
                        "伏击",
                        "击杀一名敌人",
                        EncounterTrigger.Wave(1),
                        EncounterObjective.Eliminate(1),
                        EncounterMainFlowPolicy.Parallel,
                        0,
                        300)
                },
                director: new CombatDirectorConfiguration(
                    0,
                    2,
                    1,
                    3,
                    1,
                    1,
                    0.3f),
                combatBuilds: new[]
                {
                    new OfflineCombatBuildSpec(
                        "build.offline-hit",
                        new[]
                        {
                            new CombatRuleDecisionSpec(
                                "rule.offline-hit",
                                CombatRuleDecisionTrigger.Hit,
                                new[] { "entity.player" },
                                new[] { "entity.enemy" },
                                new string[0],
                                0f,
                                1f,
                                1,
                                10000)
                        })
                },
                worldModel: new OfflineWorldModelSpec(4000));
        }

        private static OfflineBalanceScenario CreateMinimalScenario()
        {
            return CreateScenario(
                player: new OfflinePlayerSpec(
                    100f,
                    0f,
                    100f,
                    1,
                    1),
                enemies: new[]
                {
                    new OfflineEnemyArchetype(
                        "target",
                        "assault",
                        1,
                        1f,
                        0f,
                        60)
                },
                waves: new[]
                {
                    new OfflineWaveSpec(
                        1,
                        0,
                        new[] { new OfflineWaveEntry("target", 1) })
                });
        }

        private static OfflineBalanceScenario CreateScenario(
            OfflinePlayerSpec player = null,
            OfflineEnemyArchetype[] enemies = null,
            OfflineWaveSpec[] waves = null,
            EncounterDefinitionSpec[] encounters = null,
            CombatDirectorConfiguration director = null,
            OfflineCombatBuildSpec[] combatBuilds = null,
            OfflineWorldModelSpec worldModel = null)
        {
            return new OfflineBalanceScenario(
                "tests.issue63",
                1,
                30,
                waves ?? new[]
                {
                    new OfflineWaveSpec(
                        2,
                        0,
                        new[] { new OfflineWaveEntry("grunt", 2) })
                },
                player ?? new OfflinePlayerSpec(
                    100f,
                    50f,
                    25f,
                    2,
                    100,
                    1f),
                enemies ?? new[]
                {
                    new OfflineEnemyArchetype(
                        "grunt",
                        "assault",
                        1,
                        40f,
                        1f,
                        15,
                        10,
                        1),
                    new OfflineEnemyArchetype(
                        "support",
                        "support",
                        2,
                        50f,
                        1f,
                        20,
                        12,
                        2)
                },
                new[]
                {
                    new OfflineUpgradeSpec(
                        "damage.up",
                        1.1f),
                    new OfflineUpgradeSpec(
                        "armor.up",
                        armorBonus: 10f)
                },
                encounters ?? new EncounterDefinitionSpec[0],
                director,
                3000,
                combatBuilds: combatBuilds,
                worldModel: worldModel);
        }
    }
}
