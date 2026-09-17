using System.Text;
using FPS.Networking.Domain;
using FPS.Networking.Netcode;
using NUnit.Framework;

namespace FPS.Tests.Architecture
{
    public sealed class Issue101RemoteDedicatedServerTests
    {
        private static readonly byte[] Key = Encoding.UTF8.GetBytes(
            "issue101-compatibility-test-key-over-thirty-two-bytes");

        [Test]
        public void CompatibilityIdentityRoundTripsThroughSignedTicket()
        {
            var compatibility = new CoopBuildCompatibility(
                "1.4.2", "net-7", "citynew-2026.09");
            var codec = new CoopConnectionTicketCodec(Key);
            string ticket = codec.Issue("player-a", compatibility,
                2_000_000_000, 120, "issue101-nonce", "match-101");

            Assert.That(codec.TryValidate(ticket, 2_000_000_010,
                out CoopConnectionClaims claims, out var failure), Is.True);
            Assert.That(failure, Is.EqualTo(CoopAdmissionFailure.None));
            Assert.That(CoopBuildCompatibility.TryParseTicketValue(
                claims.Version, out var restored), Is.True);
            Assert.That(restored, Is.EqualTo(compatibility));
        }

        [TestCase("2.0.0", "net-7", "citynew-1",
            CoopAdmissionFailure.VersionMismatch)]
        [TestCase("1.0.0", "net-8", "citynew-1",
            CoopAdmissionFailure.ProtocolMismatch)]
        [TestCase("1.0.0", "net-7", "citynew-2",
            CoopAdmissionFailure.ContentMismatch)]
        public void AdmissionExplainsExactCompatibilityLayer(
            string application, string protocol, string content,
            CoopAdmissionFailure expected)
        {
            var codec = new CoopConnectionTicketCodec(Key);
            var service = new CoopConnectionAdmissionService(codec,
                new CoopBuildCompatibility("1.0.0", "net-7", "citynew-1"),
                2, () => 2_000_000_000, matchId: "match-101");
            string ticket = codec.Issue("player-a",
                new CoopBuildCompatibility(application, protocol, content),
                2_000_000_000, 120, "nonce-" + expected, "match-101");

            CoopAdmissionDecision decision = service.Approve(10,
                Encoding.UTF8.GetBytes(ticket));

            Assert.That(decision.Approved, Is.False);
            Assert.That(decision.Failure, Is.EqualTo(expected));
            Assert.That(decision.Reason, Is.Not.Empty);
        }

        [Test]
        public void ClientRejectsIncompatibleAllocationBeforeTransportStart()
        {
            var local = new CoopBuildCompatibility(
                "1.0.0", "net-7", "citynew-1");
            var remote = new RemoteMatchConnectionInfo
            {
                host = "203.0.113.10",
                port = 17777,
                matchId = "match-101",
                applicationVersion = "1.0.0",
                protocolVersion = "net-8",
                contentVersion = "citynew-1",
                maximumPlayers = 2
            };

            CoopCompatibilityDecision decision =
                RemoteMatchCompatibilityValidator.Validate(local, remote);

            Assert.That(decision.Compatible, Is.False);
            Assert.That(decision.Failure,
                Is.EqualTo(CoopCompatibilityFailure.ProtocolVersionMismatch));
            Assert.That(decision.Message, Does.Contain("协议"));
            Assert.That(decision.Message, Does.Contain("net-7"));
            Assert.That(decision.Message, Does.Contain("net-8"));
        }

        [Test]
        public void IdleLeaseRefreshesWhilePlayersAreConnectedThenRecycles()
        {
            var policy = new DedicatedServerIdlePolicy(120d, 10d);

            Assert.That(policy.ShouldRecycle(100d, 0), Is.False);
            Assert.That(policy.ShouldRecycle(119d, 2), Is.False);
            Assert.That(policy.ShouldRecycle(238.9d, 0), Is.False);
            Assert.That(policy.ShouldRecycle(239d, 0), Is.True);
        }

        [Test]
        public void AllocationMetadataMustBeComplete()
        {
            var local = new CoopBuildCompatibility(
                "1.0.0", "net-7", "citynew-1");
            var remote = new RemoteMatchConnectionInfo
            {
                host = "",
                port = 17777,
                matchId = "match-101",
                applicationVersion = "1.0.0",
                protocolVersion = "net-7",
                contentVersion = "citynew-1"
            };

            CoopCompatibilityDecision decision =
                RemoteMatchCompatibilityValidator.Validate(local, remote);

            Assert.That(decision.Compatible, Is.False);
            Assert.That(decision.Failure,
                Is.EqualTo(CoopCompatibilityFailure.InvalidServerMetadata));
            Assert.That(decision.Message, Does.Contain("连接信息"));
        }

        [Test]
        public void EmptyRemoteLobbyDoesNotTakeDamageBeforeClientsJoin()
        {
            var simulation = new AuthoritativeCoopSimulation(
                new CoopServerRules(tickRate: 60),
                new[]
                {
                    new CoopPlayerSpawn(1, new NetVector3(0d, 0d, 0d), 100d),
                    new CoopPlayerSpawn(2, new NetVector3(2d, 0d, 0d), 100d)
                },
                new[]
                {
                    new CoopTargetSpawn(1, new NetVector3(0d, 0d, 1d),
                        0.5d, 20d, attackRange: 2d, attackDamage: 100d,
                        attackIntervalTicks: 1)
                });
            simulation.InitializePlayerConnections(System.Array.Empty<int>());

            for (int index = 0; index < 600; index++)
                simulation.Step(System.Array.Empty<PlayerInputCommand>());
            AuthoritativeWorldSnapshot snapshot = simulation.CaptureSnapshot();

            Assert.That(snapshot.Player(1).IsConnected, Is.False);
            Assert.That(snapshot.Player(2).IsConnected, Is.False);
            Assert.That(snapshot.Player(1).Health, Is.EqualTo(100d));
            Assert.That(snapshot.Player(2).Health, Is.EqualTo(100d));
            Assert.That(snapshot.Mission.Phase,
                Is.EqualTo(AuthoritativeMissionPhase.ClearEnemies));
        }
    }
}
