using System;
using System.IO;
using FPS.Balance;
using FPS.Simulation;
using FPS.Simulation.Offline;
using NUnit.Framework;

namespace FPS.Tests.PlayMode
{
    public sealed class Issue63BalanceReproRuntimeTests
    {
        [Test]
        public void ExportedScenarioRoundTripsAndStableReplayHasNoDivergence()
        {
            OfflineBalanceScenario scenario = CreateScenario();
            OfflineRunResult result = OfflineBalanceSimulator.Run(
                scenario,
                63001);
            OfflineBalanceReproBundle exported =
                OfflineBalanceReproBundle.Create(
                    scenario,
                    result,
                    "test-policy.v1",
                    "content-fingerprint",
                    new[] { "duration-p99" });

            string json = OfflineBalanceReproBundleCodec.Serialize(exported);
            OfflineBalanceReproBundle loaded =
                OfflineBalanceReproBundleCodec.Deserialize(json);
            OfflineBalanceReproVerification verification =
                OfflineBalanceReproRuntime.Verify(loaded, "memory://test");

            Assert.That(loaded.SchemaVersion, Is.EqualTo(
                OfflineBalanceReproBundle.CurrentSchemaVersion));
            Assert.That(loaded.Scenario.Waves, Has.Length.EqualTo(2));
            Assert.That(loaded.Scenario.Enemies, Has.Length.EqualTo(2));
            Assert.That(loaded.Scenario.CombatBuilds,
                Has.Length.EqualTo(1));
            Assert.That(loaded.Scenario.CombatBuilds[0].Rules,
                Has.Length.EqualTo(1));
            Assert.That(loaded.Scenario.Player.SourceTags,
                Does.Contain("weapon.rifle"));
            Assert.That(loaded.Scenario.Enemies[0].TargetTags,
                Does.Contain("enemy"));
            Assert.That(
                loaded.Scenario.WorldModel
                    .EnemyAttackOpportunityBasisPoints,
                Is.EqualTo(4000));
            Assert.That(loaded.WorldModelVersion,
                Is.EqualTo(OfflineWorldModelSpec.CurrentVersion));
            Assert.That(loaded.EnemyAttackOpportunityBasisPoints,
                Is.EqualTo(4000));
            Assert.That(verification.IsMatch, Is.True);
            Assert.That(verification.ActualDigest, Is.EqualTo(result.Digest));
            Assert.That(verification.FirstDivergence, Is.Null,
                "稳定重跑必须允许首个分歧为空，不能伪造偏差。");
        }

        [Test]
        public void ChangedCommandStreamReportsFirstRealCommandDivergence()
        {
            OfflineBalanceScenario scenario = CreateScenario();
            OfflineRunResult result = OfflineBalanceSimulator.Run(
                scenario,
                63002);
            OfflineBalanceReproBundle bundle =
                OfflineBalanceReproBundle.Create(
                    scenario,
                    result,
                    "test-policy.v1",
                    "fingerprint",
                    new[] { "forced-test-anomaly" });
            var changed = new SimulationCommand[result.Commands.Count];
            for (int index = 0; index < changed.Length; index++)
                changed[index] = result.Commands[index];
            SimulationCommand first = changed[0];
            changed[0] = SimulationCommand.Create(
                first.Tick,
                SimulationCommandType.Pause,
                first.EntityId,
                first.PrimaryValue,
                first.SecondaryValue);
            bundle.AuthoritativeCommandStream =
                SimulationCommandCodec.Serialize(changed);
            bundle.ExpectedDigest = "deliberately-different";

            OfflineBalanceReproVerification verification =
                OfflineBalanceReproRuntime.Verify(bundle);

            Assert.That(verification.IsMatch, Is.False);
            Assert.That(verification.FirstDivergence, Is.Not.Null);
            Assert.That(verification.FirstDivergence.CommandIndex,
                Is.EqualTo(0));
            Assert.That(verification.FirstDivergence.Tick,
                Is.EqualTo(first.Tick));
            Assert.That(verification.FirstDivergence.Reason,
                Does.Contain("权威命令"));
        }

        [Test]
        public void RuntimeLatestPathIsIsolatedFromRunSaveFiles()
        {
            string normalized = OfflineBalanceReproStorage.LatestPath
                .Replace('\\', '/');

            Assert.That(normalized,
                Does.EndWith("/Issue63Balance/latest-reproduction.json"));
            Assert.That(Path.GetFileName(normalized),
                Is.Not.EqualTo("run-snapshot.json"));
        }

        private static OfflineBalanceScenario CreateScenario()
        {
            return new OfflineBalanceScenario(
                "tests.issue63.runtime-repro",
                3,
                30,
                new[]
                {
                    new OfflineWaveSpec(
                        2,
                        2,
                        new[]
                        {
                            new OfflineWaveEntry("grunt", 2),
                            new OfflineWaveEntry("elite", 1)
                        }),
                    new OfflineWaveSpec(
                        1,
                        0,
                        new[] { new OfflineWaveEntry("grunt", 1) })
                },
                new OfflinePlayerSpec(
                    100f,
                    50f,
                    25f,
                    2,
                    100,
                    1f,
                    new[] { "weapon.rifle" }),
                new[]
                {
                    new OfflineEnemyArchetype(
                        "grunt", "assault", 1, 40f, 1f, 15, 10, 1,
                        false, new[] { "enemy" }),
                    new OfflineEnemyArchetype(
                        "elite", "elite", 3, 80f, 2f, 20, 12, 2, true,
                        new[] { "enemy" })
                },
                new[]
                {
                    new OfflineUpgradeSpec("damage.up", 1.1f),
                    new OfflineUpgradeSpec("armor.up", armorBonus: 10f)
                },
                new[]
                {
                    new EncounterDefinitionSpec(
                        "runtime-repro.encounter",
                        1,
                        EncounterKind.Ambush,
                        "伏击",
                        "消灭敌人",
                        EncounterTrigger.Wave(1),
                        EncounterObjective.Eliminate(1),
                        EncounterMainFlowPolicy.Parallel,
                        0,
                        300)
                },
                new CombatDirectorConfiguration(0, 2, 1, 3, 1, 1, 0.3f),
                3000,
                1,
                new[]
                {
                    new OfflineCombatBuildSpec(
                        "build.test",
                        new[]
                        {
                            new CombatRuleDecisionSpec(
                                "rule.test",
                                CombatRuleDecisionTrigger.Hit,
                                new[] { "weapon.rifle" },
                                new[] { "enemy" },
                                Array.Empty<string>(),
                                0f,
                                1f,
                                5,
                                10000)
                        })
                },
                new OfflineWorldModelSpec(4000));
        }
    }
}
