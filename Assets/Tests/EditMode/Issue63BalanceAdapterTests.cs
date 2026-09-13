using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using FPS.Simulation.Offline;
using NUnit.Framework;

namespace FPS.Tests.Architecture
{
    public sealed class Issue63BalanceAdapterTests
    {
        [Test]
        public void CityNewProjectionFreezesValidPureScenariosPerSeed()
        {
            object batch = Prepare(new[] { 630001L, 630002L });
            object[] entries = ReadEnumerable(batch, "Entries").ToArray();

            Assert.That(entries, Has.Length.EqualTo(2));
            Assert.That(Read<string>(batch, "ContentFingerprint"),
                Has.Length.EqualTo(64));
            foreach (object entry in entries)
            {
                long seed = Read<long>(entry, "Seed");
                var scenario = Read<OfflineBalanceScenario>(entry, "Scenario");
                Assert.That(scenario.Waves, Is.Not.Empty);
                Assert.That(scenario.Enemies, Is.Not.Empty);
                Assert.That(scenario.Encounters, Is.Not.Empty);
                Assert.That(scenario.CombatBuilds, Is.Not.Empty);
                Assert.That(scenario.CombatBuilds.SelectMany(build => build.Rules),
                    Is.Not.Empty);
                Assert.That(
                    scenario.WorldModel.EnemyAttackOpportunityBasisPoints,
                    Is.EqualTo(3333));
                Assert.That(scenario.Waves.SelectMany(wave => wave.Roster)
                    .All(roster => scenario.Enemies.Any(enemy =>
                        enemy.StableId == roster.EnemyTypeId)), Is.True);
                OfflineRunResult result =
                    OfflineBalanceSimulator.Run(scenario, seed);
                Assert.That(result.Digest, Is.Not.Empty);
                Assert.That(result.CombatRuleEvents, Is.Not.Empty);
            }
        }

        [Test]
        public void ReportMatchesMachineGateSchemaAndUsesRealTtkSamples()
        {
            string directory = Path.Combine(
                Path.GetTempPath(),
                "fps-issue63-adapter-" + Guid.NewGuid().ToString("N"));
            string reportPath = Path.Combine(directory, "report.json");
            string reproPath = Path.Combine(directory, "reproductions");
            try
            {
                object run = RunService(630100L, 8, 2, reportPath, reproPath);
                object report = Read<object>(run, "Report");
                object[] results = ReadEnumerable(report, "Results").ToArray();
                string json = File.ReadAllText(reportPath);

                Assert.That(results, Has.Length.EqualTo(8));
                Assert.That(json, Does.Contain("\"schemaVersion\""));
                Assert.That(json, Does.Contain("\"rulesVersion\""));
                Assert.That(json, Does.Contain("\"policyVersion\""));
                Assert.That(json, Does.Contain("\"contentFingerprint\""));
                Assert.That(json, Does.Contain("\"environment\""));
                Assert.That(json, Does.Contain("\"fixedTickRate\""));
                Assert.That(json, Does.Contain("\"parallelism\""));
                Assert.That(json, Does.Contain("\"runtimeVersion\""));
                Assert.That(json, Does.Contain("\"gitCommit\""));
                Assert.That(json, Does.Contain("\"worldModelVersion\""));
                Assert.That(json,
                    Does.Contain("\"enemyAttackOpportunityBasisPoints\":3333"));
                Assert.That(json, Does.Contain("\"enemyTtkTicks\""));
                Assert.That(json, Does.Contain("\"roleCounts\""));
                Assert.That(json, Does.Contain("\"encounterCounts\""));
                Assert.That(json, Does.Contain("\"upgradeSelections\""));
                Assert.That(json, Does.Contain("\"combatRuleCounts\""));
                Assert.That(json, Does.Contain("\"combatRuleEvents\""));
                Assert.That(json, Does.Contain("\"deterministicDigest\""));

                foreach (object result in results)
                {
                    Assert.That(ReadEnumerable(result, "EnemyTtkTicks"),
                        Is.Not.Empty);
                    Assert.That(Read<string>(result, "DeterministicDigest"),
                        Is.Not.Empty);
                }
            }
            finally
            {
                if (Directory.Exists(directory))
                    Directory.Delete(directory, true);
            }
        }

        [Test]
        public void ParallelismDoesNotChangePreparedBatchDigest()
        {
            string directory = Path.Combine(
                Path.GetTempPath(),
                "fps-issue63-order-" + Guid.NewGuid().ToString("N"));
            try
            {
                object sequential = RunService(
                    630200L,
                    16,
                    1,
                    Path.Combine(directory, "sequential.json"),
                    Path.Combine(directory, "repro-sequential"));
                object parallel = RunService(
                    630200L,
                    16,
                    4,
                    Path.Combine(directory, "parallel.json"),
                    Path.Combine(directory, "repro-parallel"));
                object sequentialReport = Read<object>(sequential, "Report");
                object parallelReport = Read<object>(parallel, "Report");

                Assert.That(Read<string>(parallelReport, "BatchDigest"),
                    Is.EqualTo(Read<string>(sequentialReport, "BatchDigest")));
            }
            finally
            {
                if (Directory.Exists(directory))
                    Directory.Delete(directory, true);
            }
        }

        private static object Prepare(IReadOnlyList<long> seeds)
        {
            Type type = RequireType(
                "FPS.Editor.Balance.CityNewOfflineBalanceContentAdapter");
            MethodInfo method = type.GetMethod(
                "PrepareDefault",
                BindingFlags.Public | BindingFlags.Static);
            Assert.That(method, Is.Not.Null);
            return method.Invoke(null, new object[] { seeds });
        }

        private static object RunService(
            long seedStart,
            int seedCount,
            int parallelism,
            string reportPath,
            string reproductionDirectory)
        {
            Type type = RequireType(
                "FPS.Editor.Balance.Issue63BalanceSimulationService");
            MethodInfo method = type.GetMethod(
                "Run",
                BindingFlags.Public | BindingFlags.Static);
            Assert.That(method, Is.Not.Null);
            return method.Invoke(null, new object[]
            {
                seedStart,
                seedCount,
                parallelism,
                reportPath,
                reproductionDirectory
            });
        }

        private static Type RequireType(string fullName)
        {
            Type type = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType(fullName, false))
                .FirstOrDefault(candidate => candidate != null);
            Assert.That(type, Is.Not.Null,
                "Editor balance adapter type was not compiled.");
            return type;
        }

        private static IEnumerable<object> ReadEnumerable(
            object target,
            string property)
        {
            var enumerable = Read<IEnumerable>(target, property);
            foreach (object value in enumerable) yield return value;
        }

        private static T Read<T>(object target, string property)
        {
            PropertyInfo info = target.GetType().GetProperty(
                property,
                BindingFlags.Public | BindingFlags.Instance);
            Assert.That(info, Is.Not.Null,
                "Missing property " + property + " on " + target.GetType());
            return (T)info.GetValue(target);
        }
    }
}
