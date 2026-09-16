using System.Text;
using FPS.Networking.Netcode;
using NUnit.Framework;

namespace FPS.Tests.Architecture
{
    public sealed class Issue99ReconnectAdmissionTests
    {
        private static readonly byte[] Key = Encoding.UTF8.GetBytes(
            "issue99-reconnect-key-is-at-least-thirty-two-bytes");
        private long now = 1_900_000_000;

        [Test]
        public void FreshTicketReclaimsReservedAuthoritativeSlot()
        {
            CoopConnectionTicketCodec codec = new(Key);
            CoopConnectionAdmissionService service = Service(codec, 2);
            CoopAdmissionDecision first = service.Approve(11,
                Ticket(codec, "account-a", "first"));
            service.Release(11);

            CoopAdmissionDecision resumed = service.Approve(27,
                Ticket(codec, "account-a", "resume"));

            Assert.That(resumed.Approved, Is.True);
            Assert.That(resumed.Identity.IsReconnection, Is.True);
            Assert.That(resumed.Identity.SimulationPlayerId,
                Is.EqualTo(first.Identity.SimulationPlayerId));
            Assert.That(resumed.Identity.ConnectionGeneration,
                Is.GreaterThan(first.Identity.ConnectionGeneration));
            Assert.That(service.ReservedCount, Is.Zero);
        }

        [Test]
        public void ReservedSeatCannotBeStolenByAnotherAccount()
        {
            CoopConnectionTicketCodec codec = new(Key);
            CoopConnectionAdmissionService service = Service(codec, 1);
            Assert.That(service.Approve(1,
                Ticket(codec, "owner", "owner-first")).Approved, Is.True);
            service.Release(1);

            CoopAdmissionDecision intruder = service.Approve(2,
                Ticket(codec, "intruder", "intruder-first"));

            Assert.That(intruder.Failure,
                Is.EqualTo(CoopAdmissionFailure.ServerFull));
            Assert.That(service.TryGetReservation("owner", out var lease),
                Is.True);
            Assert.That(lease.SimulationPlayerId, Is.EqualTo(1));
        }

        [Test]
        public void ExpiredLeaseIsReportedAndCannotBeReclaimedLate()
        {
            CoopConnectionTicketCodec codec = new(Key);
            CoopConnectionAdmissionService service = Service(codec, 1,
                reconnectSeconds: 10);
            Assert.That(service.Approve(1,
                Ticket(codec, "owner", "first")).Approved, Is.True);
            service.Release(1);
            now += 11;

            Assert.That(service.TryDequeueExpiredReservation(out var expired),
                Is.True);
            Assert.That(expired.AccountPlayerId, Is.EqualTo("owner"));
            CoopAdmissionDecision late = service.Approve(2,
                Ticket(codec, "owner", "late"));
            Assert.That(late.Failure,
                Is.EqualTo(CoopAdmissionFailure.ReconnectWindowExpired));
        }

        [Test]
        public void TicketMustBelongToCurrentMatch()
        {
            CoopConnectionTicketCodec codec = new(Key);
            CoopConnectionAdmissionService service = Service(codec, 2,
                matchId: "match-99");

            CoopAdmissionDecision wrong = service.Approve(1,
                Ticket(codec, "account-a", "wrong-match", "match-else"));
            CoopAdmissionDecision right = service.Approve(2,
                Ticket(codec, "account-a", "right-match", "match-99"));

            Assert.That(wrong.Failure,
                Is.EqualTo(CoopAdmissionFailure.SessionMismatch));
            Assert.That(right.Approved, Is.True);
        }

        [Test]
        public void OriginalOneTimeTicketRemainsInvalidAfterReservation()
        {
            CoopConnectionTicketCodec codec = new(Key);
            CoopConnectionAdmissionService service = Service(codec, 2);
            byte[] credential = Ticket(codec, "account-a", "single-use");
            Assert.That(service.Approve(1, credential).Approved, Is.True);
            service.Release(1);

            CoopAdmissionDecision replay = service.Approve(2, credential);

            Assert.That(replay.Failure,
                Is.EqualTo(CoopAdmissionFailure.CredentialReplayed));
        }

        private CoopConnectionAdmissionService Service(
            CoopConnectionTicketCodec codec,
            int players,
            int reconnectSeconds = 30,
            string matchId = "match-99") => new(
            codec, "1.0.0", players, () => now, reconnectSeconds, matchId);

        private byte[] Ticket(CoopConnectionTicketCodec codec,
            string account,
            string nonce,
            string matchId = "match-99") => Encoding.UTF8.GetBytes(
            codec.Issue(account, "1.0.0", now, 120, nonce, matchId));
    }
}
