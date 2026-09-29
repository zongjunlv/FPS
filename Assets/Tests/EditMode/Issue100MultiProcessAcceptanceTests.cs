using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using FPS.Networking;
using FPS.Networking.Diagnostics;
using FPS.Networking.Domain;
using FPS.Networking.Netcode;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;

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
        public void DedicatedServerConsumesCompleteCityNewScenario()
        {
            CoopScenarioConfiguration scenario =
                CityNewAuthoritativeScenarioAdapter.Build(18018, 2);

            Assert.That(scenario.Players, Has.Length.EqualTo(2));
            Assert.That(scenario.Waves.Length, Is.GreaterThan(1));
            Assert.That(scenario.Targets.Length, Is.GreaterThan(6));
            Assert.That(scenario.Targets,
                Has.All.Matches<CoopTargetSpawnDefinition>(
                target => target.RewardExperience > 0 &&
                    target.HeadRadius > 0f && target.HeadOffset.y > 0f &&
                    !string.IsNullOrWhiteSpace(target.ArchetypeId) &&
                    !string.IsNullOrWhiteSpace(
                        target.PresentationAddress)));
        }

        [Test]
        public void AcceptanceReconnectTicketMatchesCurrentServerWireProtocol()
        {
            // This is a source-binding regression plus the real admission path;
            // it is not a substitute for the three-process UDP reconnect run.
            string source = File.ReadAllText(Path.Combine(
                Application.dataPath, "Scripts/Networking/Acceptance/" +
                "Issue100ClientScenarioDriver.cs"));
            Match binding = Regex.Match(source,
                @"ticket\s*=\s*codec\.Issue\(options\.AccountId,\s*" +
                @"new\s+CoopBuildCompatibility\(""local-dev"",\s*" +
                @"(?<protocol>""[^""]+""|CoopWireProtocol\.CompatibilityId)\s*,");
            Assert.That(binding.Success, Is.True,
                "The local acceptance reconnect ticket binding must remain identifiable.");
            string expression = binding.Groups["protocol"].Value;
            string reconnectProtocol = expression ==
                "CoopWireProtocol.CompatibilityId"
                    ? CoopWireProtocol.CompatibilityId
                    : expression.Trim('"');

            const long now = 1_800_000_000;
            const string matchId = "issue100-current-wire-reconnect";
            var codec = new CoopConnectionTicketCodec(Encoding.UTF8.GetBytes(
                "issue100-test-only-signing-key-at-least-thirty-two-bytes"));
            var current = new CoopBuildCompatibility("local-dev",
                CoopWireProtocol.CompatibilityId, "citynew-v1");
            var admission = new CoopConnectionAdmissionService(codec,
                current, 2, () => now, matchId: matchId);
            CoopAdmissionDecision initial = admission.Approve(11,
                Encoding.UTF8.GetBytes(codec.Issue("client-b", current,
                    now, nonce: "initial", matchId: matchId)));
            Assert.That(initial.Approved, Is.True);
            admission.Release(11);

            string ticket = codec.Issue("client-b",
                new CoopBuildCompatibility("local-dev", reconnectProtocol,
                    "citynew-v1"), now, nonce: "reconnect", matchId: matchId);
            CoopAdmissionDecision restored = admission.Approve(12,
                Encoding.UTF8.GetBytes(ticket));

            Assert.That(restored.Approved, Is.True,
                $"Acceptance reconnect ticket was rejected: {restored.Failure}; " +
                $"ticketProtocol={reconnectProtocol}; " +
                $"serverProtocol={CoopWireProtocol.CompatibilityId}");
            Assert.That(restored.Identity.IsReconnection, Is.True);
            Assert.That(restored.Identity.SimulationPlayerId,
                Is.EqualTo(initial.Identity.SimulationPlayerId));
            Assert.That(restored.Identity.ConnectionGeneration,
                Is.EqualTo(initial.Identity.ConnectionGeneration + 1));
            Assert.That(expression,
                Is.EqualTo("CoopWireProtocol.CompatibilityId"),
                "Acceptance ticket must use the central wire version, not a copied literal.");
        }

        [Test]
        public void RejectedConsumableAcknowledgementCannotPassAcceptance()
        {
            var simulation = new AuthoritativeCoopSimulation(
                new CoopServerRules(), new[] { new CoopPlayerSpawn(1, default) },
                new[] { new CoopTargetSpawn(1, new NetVector3(0d, 0d, 30d),
                    0.5d, 100d, moveSpeed: 0d, attackDamage: 0d) });
            int dropId = simulation.SpawnServerWorldDrop("medical_kit", 2, default);
            AuthoritativeWorldSnapshot before = simulation.Step(
                Array.Empty<PlayerInputCommand>(), new[]
                {
                    new AuthoritativeEconomyCommand(1, 1, 101,
                        AuthoritativeEconomyCommandKind.Pickup, entityId: dropId)
                }).Snapshot;
            AuthoritativeTickResult rejected = simulation.Step(
                Array.Empty<PlayerInputCommand>(), new[]
                {
                    new AuthoritativeEconomyCommand(1, 2, 102,
                        AuthoritativeEconomyCommandKind.Use, sourceSlot: 0)
                });
            Assert.That(rejected.EconomyCommands.Single().Rejection,
                Is.EqualTo(AuthoritativeEconomyRejection.EffectUnavailable));
            Assert.That(rejected.Snapshot.Economy.Player(1)
                .AcknowledgedEconomySequence, Is.EqualTo(2));

            bool passed = EvaluateAcceptanceSettlement("HasConfirmedItemUse", 2,
                NetcodeProgressionState.FromDomain(before.Economy.Player(1)),
                NetcodeProgressionState.FromDomain(rejected.Snapshot.Economy.Player(1)),
                before.Economy.InventorySlots.Sum(slot => slot.Quantity),
                rejected.Snapshot.Economy.InventorySlots.Sum(slot => slot.Quantity));

            Assert.That(passed, Is.False,
                "A processed but rejected use command cannot be accepted merely because its ACK advances.");
        }

        [Test]
        public void UnacknowledgedItemUseCannotPassAcceptance()
        {
            var before = new NetcodeProgressionState
            {
                PlayerId = 1, AcknowledgedEconomySequence = 7,
                InventoryRevision = 3
            };
            Assert.That(EvaluateAcceptanceSettlement("HasConfirmedItemUse", 8,
                before, before, 2, 2), Is.False);
        }

        [Test]
        public void ExistingUpgradeWithoutCurrentSettlementCannotPassAcceptance()
        {
            var before = new NetcodeProgressionState
            {
                PlayerId = 1, AcknowledgedEconomySequence = 7,
                PendingUpgradeChoices = 1, ChoiceGeneration = 4
            };
            NetcodeProgressionState rejected = before;
            rejected.AcknowledgedEconomySequence = 8;

            Assert.That(EvaluateAcceptanceSettlement("HasConfirmedUpgradeSelection", 8,
                before, rejected, 2, 2), Is.False,
                "An existing upgrade cannot prove that the current select command was applied.");
            Assert.That(EvaluateAcceptanceSettlement("HasConfirmedUpgradeSelection", 8,
                before, before, 2, 2), Is.False,
                "An unacknowledged selection must remain unconfirmed.");
        }

        [Test]
        public void AcceptedItemUseRequiresItsAckRevisionAndOneConsumedItem()
        {
            var before = new NetcodeProgressionState
            {
                PlayerId = 1, AcknowledgedEconomySequence = 7,
                InventoryRevision = 3
            };
            NetcodeProgressionState after = before;
            after.AcknowledgedEconomySequence = 8;
            after.InventoryRevision = 4;
            Assert.That(EvaluateAcceptanceSettlement("HasConfirmedItemUse", 8,
                before, after, 2, 1), Is.True);
            Assert.That(EvaluateAcceptanceSettlement("HasConfirmedItemUse", 8,
                before, after, 2, 2), Is.False);
            after.InventoryRevision = 3;
            Assert.That(EvaluateAcceptanceSettlement("HasConfirmedItemUse", 8,
                before, after, 2, 1), Is.False);
            after.InventoryRevision = 4;
            after.AcknowledgedEconomySequence = 7;
            Assert.That(EvaluateAcceptanceSettlement("HasConfirmedItemUse", 8,
                before, after, 2, 1), Is.False);
        }

        [Test]
        public void AcceptedUpgradeRequiresThisRequestAndCurrentLevelOrPendingDelta()
        {
            var before = new NetcodeProgressionState
            {
                PlayerId = 1, AcknowledgedEconomySequence = 7,
                PendingUpgradeChoices = 2, ChoiceGeneration = 4
            };
            NetcodeProgressionState after = before;
            after.AcknowledgedEconomySequence = 8;
            Assert.That(EvaluateAcceptanceSettlement("HasConfirmedUpgradeSelection", 8,
                before, after, 1, 2), Is.True);
            after.PendingUpgradeChoices = 1;
            Assert.That(EvaluateAcceptanceSettlement("HasConfirmedUpgradeSelection", 8,
                before, after, 1, 1), Is.True);
            after.AcknowledgedEconomySequence = 7;
            Assert.That(EvaluateAcceptanceSettlement("HasConfirmedUpgradeSelection", 8,
                before, after, 1, 2), Is.False);
            Assert.That(EvaluateAcceptanceSettlement("HasConfirmedUpgradeSelection", 0,
                before, after, 1, 2), Is.False);
        }

        private static bool EvaluateAcceptanceSettlement(string methodName,
            uint sequence, NetcodeProgressionState before,
            NetcodeProgressionState after, int amountBefore, int amountAfter)
        {
            Type driver = Type.GetType(
                "FPS.Networking.Acceptance.Issue100ClientScenarioDriver, " +
                "FPS.Networking.Acceptance");
            Assert.That(driver, Is.Not.Null);
            MethodInfo method = driver.GetMethod(methodName,
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null);
            return (bool)method.Invoke(null, new object[]
            {
                sequence, before, after, amountBefore, amountAfter
            });
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

        [Test]
        public void NavigationAdvancesPastOvershotCornerWithoutReversing()
        {
            Vector3[] corners =
            {
                Vector3.zero,
                new Vector3(1f, 0f, 0f),
                new Vector3(1f, 0f, 10f)
            };

            int next = AdvanceNavigationWaypoint(
                new Vector3(2f, 0f, 0f), corners, 1, 0.8f);

            Assert.That(next, Is.EqualTo(2));
        }

        [Test]
        public void NavigationDoesNotSkipCornerWhenFarOffRoute()
        {
            Vector3[] corners =
            {
                Vector3.zero,
                new Vector3(1f, 0f, 0f),
                new Vector3(1f, 0f, 10f)
            };

            int next = AdvanceNavigationWaypoint(
                new Vector3(2f, 0f, 3f), corners, 1, 0.8f);

            Assert.That(next, Is.EqualTo(1));
        }

        private static int AdvanceNavigationWaypoint(Vector3 current,
            Vector3[] corners, int next, float arrivalRadius)
        {
            Type driver = Type.GetType(
                "FPS.Networking.Acceptance.Issue100ClientScenarioDriver, " +
                "FPS.Networking.Acceptance");
            Assert.That(driver, Is.Not.Null);
            MethodInfo advance = driver.GetMethod("AdvanceWaypointIndex",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(advance, Is.Not.Null);
            return (int)advance.Invoke(null,
                new object[] { current, corners, next, arrivalRadius });
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
