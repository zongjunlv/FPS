using System;
using System.Collections.Generic;
using System.Linq;
using FPS.Networking.Diagnostics;
using DomainRejectionReason =
    FPS.Networking.Domain.CommandRejectionReason;
using NUnit.Framework;

namespace FPS.Tests.Architecture
{
    public sealed class Issue65NetworkDiagnosticsTests
    {
        [Test]
        public void RequiredMatrixContainsZeroEightyOneFiftyAndControlledLoss()
        {
            IReadOnlyList<NetworkConditionScenario> scenarios =
                Issue65NetworkConditionMatrix.Required;

            Assert.That(scenarios.Count, Is.EqualTo(4));
            Assert.That(
                scenarios.Select(value =>
                    value.RoundTripLatencyMilliseconds),
                Is.EquivalentTo(new[] { 0, 80, 150, 80 }));
            Assert.That(
                scenarios.Count(value => value.HasControlledLoss),
                Is.EqualTo(1));
            Assert.That(
                scenarios.Single(value => value.HasControlledLoss)
                    .PacketLossBasisPoints,
                Is.EqualTo(500));
        }

        [Test]
        public void FixturePassesMetricThresholdsButCannotPassAcceptance()
        {
            NetworkDiagnosticReport report =
                Issue65NetworkDiagnosticFixture.Run(FixtureMetadata());

            Assert.That(
                report.Gate.Outcome,
                Is.EqualTo(NetworkDiagnosticsGateOutcome.FixtureOnly));
            Assert.That(report.Gate.MetricThresholdsPassed, Is.True);
            Assert.That(report.Gate.AcceptanceEligible, Is.False);
            Assert.That(
                report.Gate.Reasons,
                Does.Contain(
                    "evidence:fixture-is-not-real-multi-process-proof"));
        }

        [Test]
        public void FixtureRejectsMetadataThatClaimsMultiProcessEvidence()
        {
            Assert.Throws<ArgumentException>(() =>
                Issue65NetworkDiagnosticFixture.Run(MultiProcessMetadata()));
        }

        [Test]
        public void StableJsonIsByteIdenticalAndScenariosAreSorted()
        {
            NetworkDiagnosticReport report =
                Issue65NetworkDiagnosticFixture.Run(FixtureMetadata());

            string first = NetworkDiagnosticsReportWriter.ToStableJson(report);
            string second = NetworkDiagnosticsReportWriter.ToStableJson(report);

            Assert.That(second, Is.EqualTo(first));
            Assert.That(first, Does.Contain(
                "\"schemaVersion\": \"issue65-network-diagnostics-v1\""));
            Assert.That(first, Does.Contain(
                "\"isRealMultiProcess\": false"));
            Assert.That(first, Does.Contain(
                "\"acceptanceEligible\": false"));
            Assert.That(
                first.IndexOf("rtt-000-loss-00", StringComparison.Ordinal),
                Is.LessThan(first.IndexOf(
                    "rtt-080-loss-00",
                    StringComparison.Ordinal)));
            Assert.That(
                first.IndexOf("rtt-080-loss-00", StringComparison.Ordinal),
                Is.LessThan(first.IndexOf(
                    "rtt-080-loss-05",
                    StringComparison.Ordinal)));
        }

        [Test]
        public void MarkdownExplicitlyDisclaimsFixtureAsAcceptanceEvidence()
        {
            NetworkDiagnosticReport report =
                Issue65NetworkDiagnosticFixture.Run(FixtureMetadata());

            string markdown = NetworkDiagnosticsReportWriter.ToMarkdown(report);

            Assert.That(markdown, Does.Contain("确定性进程内诊断 Fixture"));
            Assert.That(markdown, Does.Contain("不能冒充真实多进程验收证据"));
            Assert.That(markdown, Does.Contain("命中反馈 P95"));
            Assert.That(markdown, Does.Contain("命令拒绝原因"));
        }

        [Test]
        public void RealMultiProcessEvidencePassesOnlyWhenAllHardGatesPass()
        {
            IReadOnlyList<NetworkDiagnosticScenarioResult> results =
                PassingResults();

            NetworkDiagnosticsGateDecision decision =
                NetworkDiagnosticsGate.Evaluate(
                    MultiProcessMetadata(),
                    results);

            Assert.That(
                decision.Outcome,
                Is.EqualTo(NetworkDiagnosticsGateOutcome.Pass));
            Assert.That(decision.AcceptanceEligible, Is.True);
            Assert.That(decision.Reasons, Is.Empty);
        }

        [Test]
        public void UnknownRejectionReasonFailsHardGate()
        {
            IReadOnlyList<NetworkDiagnosticScenarioResult> results =
                PassingResults(true);

            NetworkDiagnosticsGateDecision decision =
                NetworkDiagnosticsGate.Evaluate(
                    MultiProcessMetadata(),
                    results);

            Assert.That(
                decision.Outcome,
                Is.EqualTo(NetworkDiagnosticsGateOutcome.Fail));
            Assert.That(decision.AcceptanceEligible, Is.False);
            Assert.That(
                decision.Reasons,
                Does.Contain("rtt-000-loss-00:unknown-rejection-reason"));
        }

        [Test]
        public void MissingRequiredRejectionCoverageFailsHardGate()
        {
            IReadOnlyList<NetworkDiagnosticScenarioResult> results =
                PassingResults(includeCoverage: false);

            NetworkDiagnosticsGateDecision decision =
                NetworkDiagnosticsGate.Evaluate(
                    MultiProcessMetadata(),
                    results);

            Assert.That(decision.Outcome,
                Is.EqualTo(NetworkDiagnosticsGateOutcome.Fail));
            Assert.That(
                decision.Reasons,
                Does.Contain("rejection-coverage:missing-TooOld"));
            Assert.That(
                decision.Reasons,
                Does.Contain("rejection-coverage:missing-Duplicate"));
            Assert.That(
                decision.Reasons,
                Does.Contain("rejection-coverage:missing-IllegalState"));
        }

        [Test]
        public void DomainRejectionsMapToStableDiagnosticCategories()
        {
            Assert.That(
                NetworkDiagnosticsDomainMapper.MapRejection(
                    DomainRejectionReason.TimestampTooOld),
                Is.EqualTo(NetworkCommandRejectionReason.TooOld));
            Assert.That(
                NetworkDiagnosticsDomainMapper.MapRejection(
                    DomainRejectionReason.DuplicateNonce),
                Is.EqualTo(NetworkCommandRejectionReason.Duplicate));
            Assert.That(
                NetworkDiagnosticsDomainMapper.MapRejection(
                    DomainRejectionReason.ImpossibleDisplacement),
                Is.EqualTo(NetworkCommandRejectionReason.IllegalState));
            Assert.That(
                NetworkDiagnosticsDomainMapper.MapRejection(
                    DomainRejectionReason.FireRateExceeded),
                Is.EqualTo(NetworkCommandRejectionReason.RateLimited));
        }

        private static NetworkDiagnosticRunMetadata FixtureMetadata()
        {
            return new NetworkDiagnosticRunMetadata(
                "fixture-test",
                "test",
                "6000.5",
                "none-fixture",
                NetworkDiagnosticEvidenceKind.DeterministicFixture,
                "single-process-in-memory",
                1,
                "Editor",
                "Editor",
                "test-content");
        }

        private static NetworkDiagnosticRunMetadata MultiProcessMetadata()
        {
            return new NetworkDiagnosticRunMetadata(
                "mpp-test",
                "test",
                "6000.5",
                "UnityTransport",
                NetworkDiagnosticEvidenceKind.MultiProcessPlayer,
                "dedicated-server-plus-client",
                2,
                "macOS",
                "macOS",
                "test-content");
        }

        private static IReadOnlyList<NetworkDiagnosticScenarioResult>
            PassingResults(
                bool includeUnknown = false,
                bool includeCoverage = true)
        {
            var results = new List<NetworkDiagnosticScenarioResult>();
            for (int scenarioIndex = 0;
                 scenarioIndex < Issue65NetworkConditionMatrix.Required.Count;
                 scenarioIndex++)
            {
                NetworkConditionScenario scenario =
                    Issue65NetworkConditionMatrix.Required[scenarioIndex];
                var accumulator = new NetworkDiagnosticsAccumulator(scenario);
                accumulator.Advance(10d);
                for (int sample = 0; sample < 30; sample++)
                {
                    accumulator.RecordHitFeedback(
                        scenario.RoundTripLatencyMilliseconds + 16d);
                    accumulator.RecordCommandSent();
                    accumulator.RecordCommandAccepted();
                    accumulator.RecordStateComparison(false);
                    accumulator.RecordTraffic(64, 128);
                }

                if (scenario.HasControlledLoss)
                {
                    accumulator.RecordCommandSent();
                    accumulator.RecordCommandDroppedByCondition();
                }

                if (scenarioIndex == 0 && includeCoverage)
                {
                    RecordRejected(accumulator,
                        NetworkCommandRejectionReason.TooOld);
                    RecordRejected(accumulator,
                        NetworkCommandRejectionReason.Duplicate);
                    RecordRejected(accumulator,
                        NetworkCommandRejectionReason.IllegalState);
                }

                if (scenarioIndex == 0 && includeUnknown)
                {
                    RecordRejected(accumulator,
                        NetworkCommandRejectionReason.Unknown);
                }

                results.Add(accumulator.Complete());
            }

            return results;
        }

        private static void RecordRejected(
            NetworkDiagnosticsAccumulator accumulator,
            NetworkCommandRejectionReason reason)
        {
            accumulator.RecordCommandSent();
            accumulator.RecordCommandRejected(reason);
        }
    }
}
