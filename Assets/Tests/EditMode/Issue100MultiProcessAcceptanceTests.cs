using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FPS.Networking.Diagnostics;
using FPS.Networking.Netcode;
using NUnit.Framework;
using Unity.Netcode;

namespace FPS.Tests.Architecture
{
    public sealed class Issue100MultiProcessAcceptanceTests
    {
        [Test]
        public void CompleteThreeProcessMatrixPassesFormalGate()
        {
            Issue100ScenarioEvidence[] evidence = BuildCompleteEvidence();

            Issue100AcceptanceDecision decision =
                Issue100AcceptanceGate.Evaluate(evidence);

            Assert.That(decision.Passed, Is.True,
                string.Join("\n", decision.Failures));
        }

        [Test]
        public void DuplicateProcessIdCannotMasqueradeAsThreeProcesses()
        {
            Issue100ScenarioEvidence[] evidence = BuildCompleteEvidence();
            Issue100ScenarioEvidence original = evidence[0];
            Issue100ProcessEvidence first = original.Processes[0];
            Issue100ProcessEvidence second = original.Processes[1];
            Issue100ProcessEvidence third = original.Processes[2];
            evidence[0] = new Issue100ScenarioEvidence(original.StableId,
                new[]
                {
                    first,
                    new Issue100ProcessEvidence(second.Role, first.ProcessId,
                        second.StartedUnixMilliseconds,
                        second.EndedUnixMilliseconds, second.LogPath,
                        second.SnapshotPath),
                    third
                }, original.Metrics, original.Steps, original.TimelinePath);

            Issue100AcceptanceDecision decision =
                Issue100AcceptanceGate.Evaluate(evidence);

            Assert.That(decision.Passed, Is.False);
            Assert.That(decision.Failures, Does.Contain(
                original.StableId + ":process-ids-must-be-distinct"));
        }

        [Test]
        public void NonOverlappingProcessesAreRejected()
        {
            Issue100ScenarioEvidence[] evidence = BuildCompleteEvidence();
            Issue100ScenarioEvidence original = evidence[1];
            Issue100ProcessEvidence[] processes = original.Processes.ToArray();
            processes[2] = new Issue100ProcessEvidence(
                Issue100ProcessRole.ClientB, 3002, 3000, 4000,
                "client-b.log", "client-b-snapshot.json");
            evidence[1] = new Issue100ScenarioEvidence(original.StableId,
                processes, original.Metrics, original.Steps,
                original.TimelinePath);

            Issue100AcceptanceDecision decision =
                Issue100AcceptanceGate.Evaluate(evidence);

            Assert.That(decision.Failures, Does.Contain(
                original.StableId + ":process-lifetimes-do-not-overlap"));
        }

        [Test]
        public void MissingGameplayStepFailsEvenWhenMetricsPass()
        {
            Issue100ScenarioEvidence[] evidence = BuildCompleteEvidence();
            Issue100ScenarioEvidence normal = evidence.Single(value =>
                value.StableId == "rtt-000-loss-00");
            Issue100StepEvidence[] steps = normal.Steps.Where(value =>
                    value.StepId != Issue100AcceptanceSteps.ReconnectRestore)
                .ToArray();
            int index = Array.IndexOf(evidence, normal);
            evidence[index] = new Issue100ScenarioEvidence(normal.StableId,
                normal.Processes, normal.Metrics, steps,
                normal.TimelinePath);

            Issue100AcceptanceDecision decision =
                Issue100AcceptanceGate.Evaluate(evidence);

            Assert.That(decision.Passed, Is.False);
            Assert.That(decision.Failures, Does.Contain(
                "flow:missing-" +
                Issue100AcceptanceSteps.ReconnectRestore));
        }

        [Test]
        public void ScenarioMatrixCannotOmitPacketLossRun()
        {
            Issue100ScenarioEvidence[] incomplete = BuildCompleteEvidence()
                .Where(value => value.StableId != "rtt-080-loss-05")
                .ToArray();

            Issue100AcceptanceDecision decision =
                Issue100AcceptanceGate.Evaluate(incomplete);

            Assert.That(decision.Failures, Does.Contain(
                "matrix:requires-exact-four-network-scenarios"));
        }

        [Test]
        public void DedicatedServerTargetsProvideProgressionAndHeadHitboxes()
        {
            MethodInfo buildTargets = typeof(DedicatedServerRuntime)
                .GetMethod("BuildTargets", BindingFlags.Static |
                    BindingFlags.NonPublic);

            Assert.That(buildTargets, Is.Not.Null);
            var targets = (CoopTargetSpawnDefinition[])buildTargets.Invoke(
                null, new object[] { 18018 });

            Assert.That(targets, Has.Length.EqualTo(6));
            Assert.That(targets, Has.All.Matches<CoopTargetSpawnDefinition>(
                target => target.RewardExperience >= 100 &&
                    target.HeadRadius > 0f && target.HeadOffset.y > 0f));
        }

        [Test]
        public void PlayerInputCommandsShareOneOrderedReliableChannel()
        {
            string[] methods =
            {
                nameof(NetworkCoopSessionAuthority.SubmitInputRpc),
                nameof(NetworkCoopSessionAuthority.SubmitActionInputRpc),
                nameof(NetworkCoopSessionAuthority.SubmitShotRpc)
            };

            foreach (string methodName in methods)
            {
                MethodInfo method = typeof(NetworkCoopSessionAuthority)
                    .GetMethod(methodName);
                Assert.That(method, Is.Not.Null, methodName);
                RpcAttribute attribute =
                    method.GetCustomAttribute<RpcAttribute>();
                Assert.That(attribute, Is.Not.Null, methodName);
                Assert.That(attribute.Delivery,
                    Is.EqualTo(RpcDelivery.Reliable), methodName);
            }
        }

        private static Issue100ScenarioEvidence[] BuildCompleteEvidence()
        {
            var result = new List<Issue100ScenarioEvidence>();
            for (int index = 0;
                 index < Issue65NetworkConditionMatrix.Required.Count;
                 index++)
            {
                NetworkConditionScenario scenario =
                    Issue65NetworkConditionMatrix.Required[index];
                Issue100StepEvidence[] steps = index == 0
                    ? Issue100AcceptanceSteps.Required.Select((step, stepIndex) =>
                        new Issue100StepEvidence(step,
                            stepIndex % 2 == 0
                                ? Issue100ProcessRole.ClientA
                                : Issue100ProcessRole.ClientB,
                            true, stepIndex + 1)).ToArray()
                    : Array.Empty<Issue100StepEvidence>();
                result.Add(new Issue100ScenarioEvidence(scenario.StableId,
                    BuildProcesses(index), BuildMetrics(scenario, index), steps,
                    scenario.StableId + "/timeline.ndjson"));
            }
            return result.ToArray();
        }

        private static Issue100ProcessEvidence[] BuildProcesses(int index)
        {
            long start = 1000 + index * 10000;
            long end = start + 9000;
            return new[]
            {
                new Issue100ProcessEvidence(
                    Issue100ProcessRole.DedicatedServer, 1000 + index,
                    start, end, "server.log", "server-snapshot.json"),
                new Issue100ProcessEvidence(
                    Issue100ProcessRole.ClientA, 2000 + index,
                    start + 100, end - 100, "client-a.log",
                    "client-a-snapshot.json"),
                new Issue100ProcessEvidence(
                    Issue100ProcessRole.ClientB, 3000 + index,
                    start + 200, end - 200, "client-b.log",
                    "client-b-snapshot.json")
            };
        }

        private static NetworkDiagnosticScenarioResult BuildMetrics(
            NetworkConditionScenario scenario, int index)
        {
            IReadOnlyList<NetworkRejectionCount> rejection = index == 0
                ? new[]
                {
                    new NetworkRejectionCount(
                        NetworkCommandRejectionReason.TooOld, 1),
                    new NetworkRejectionCount(
                        NetworkCommandRejectionReason.Duplicate, 1),
                    new NetworkRejectionCount(
                        NetworkCommandRejectionReason.IllegalState, 1)
                }
                : Array.Empty<NetworkRejectionCount>();
            int rejected = rejection.Sum(value => value.Count);
            int dropped = scenario.HasControlledLoss ? 5 : 0;
            return new NetworkDiagnosticScenarioResult(
                scenario,
                60d,
                24,
                scenario.RoundTripLatencyMilliseconds + 10d,
                scenario.RoundTripLatencyMilliseconds + 20d,
                scenario.RoundTripLatencyMilliseconds + 30d,
                2,
                2d,
                0.05d,
                0.08d,
                0.12d,
                16000,
                64000,
                266.7d,
                1066.7d,
                600,
                2,
                2d / 600d,
                0.1d,
                50d,
                100,
                100 - dropped - rejected,
                dropped,
                rejection);
        }
    }
}
