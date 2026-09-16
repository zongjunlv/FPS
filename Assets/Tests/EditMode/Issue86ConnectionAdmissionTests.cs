using System;
using System.Text;
using FPS.Networking.Domain;
using FPS.Networking.Netcode;
using NUnit.Framework;
using UnityEngine;

namespace FPS.Tests.Architecture
{
    public sealed class Issue86ConnectionAdmissionTests
    {
        private const long Now = 1_800_000_000;
        private static readonly byte[] Key = Encoding.UTF8.GetBytes(
            "issue86-test-key-is-at-least-thirty-two-bytes-long");

        [Test]
        public void ValidTicketBindsAccountClientAndAuthoritativePlayer()
        {
            var codec = new CoopConnectionTicketCodec(Key);
            var service = NewService(codec, 2);
            byte[] payload = Ticket(codec, "account-a", "nonce-a");

            CoopAdmissionDecision decision = service.Approve(41, payload);

            Assert.That(decision.Approved, Is.True);
            Assert.That(decision.Identity.ClientId, Is.EqualTo(41));
            Assert.That(decision.Identity.AccountPlayerId,
                Is.EqualTo("account-a"));
            Assert.That(decision.Identity.SimulationPlayerId, Is.EqualTo(1));
            Assert.That(service.TryGetIdentity(41, out var bound), Is.True);
            Assert.That(bound.AccountPlayerId, Is.EqualTo("account-a"));
        }

        [Test]
        public void MissingLoginCredentialIsRejectedWithReadableReason()
        {
            var service = NewService(new CoopConnectionTicketCodec(Key), 2);

            CoopAdmissionDecision decision = service.Approve(1,
                Array.Empty<byte>());

            Assert.That(decision.Approved, Is.False);
            Assert.That(decision.Failure,
                Is.EqualTo(CoopAdmissionFailure.LoginRequired));
            Assert.That(decision.Reason, Does.Contain("登录"));
        }

        [Test]
        public void UsedTicketCannotBeReplayedAfterDisconnect()
        {
            var codec = new CoopConnectionTicketCodec(Key);
            var service = NewService(codec, 2);
            byte[] payload = Ticket(codec, "account-a", "single-use");
            Assert.That(service.Approve(1, payload).Approved, Is.True);
            service.Release(1);

            CoopAdmissionDecision replay = service.Approve(2, payload);

            Assert.That(replay.Failure,
                Is.EqualTo(CoopAdmissionFailure.CredentialReplayed));
        }

        [Test]
        public void SameAccountCannotLoginTwiceWithDifferentTickets()
        {
            var codec = new CoopConnectionTicketCodec(Key);
            var service = NewService(codec, 2);
            Assert.That(service.Approve(1,
                Ticket(codec, "account-a", "nonce-1")).Approved, Is.True);

            CoopAdmissionDecision duplicate = service.Approve(2,
                Ticket(codec, "account-a", "nonce-2"));

            Assert.That(duplicate.Failure,
                Is.EqualTo(CoopAdmissionFailure.DuplicateAccount));
        }

        [Test]
        public void FullServerRejectsAdditionalAccount()
        {
            var codec = new CoopConnectionTicketCodec(Key);
            var service = NewService(codec, 1);
            Assert.That(service.Approve(1,
                Ticket(codec, "account-a", "nonce-1")).Approved, Is.True);

            CoopAdmissionDecision full = service.Approve(2,
                Ticket(codec, "account-b", "nonce-2"));

            Assert.That(full.Failure,
                Is.EqualTo(CoopAdmissionFailure.ServerFull));
        }

        [Test]
        public void VersionMismatchIsRejectedBeforeIdentityBinding()
        {
            var codec = new CoopConnectionTicketCodec(Key);
            var service = NewService(codec, 2);
            string ticket = codec.Issue("account-a", "2.0.0", Now, 120,
                "version-nonce");

            CoopAdmissionDecision mismatch = service.Approve(1,
                Encoding.UTF8.GetBytes(ticket));

            Assert.That(mismatch.Failure,
                Is.EqualTo(CoopAdmissionFailure.VersionMismatch));
            Assert.That(service.ApprovedCount, Is.Zero);
        }

        [Test]
        public void ExpiredAndTamperedCredentialsAreRejected()
        {
            var codec = new CoopConnectionTicketCodec(Key);
            var service = NewService(codec, 2);
            string expired = codec.Issue("account-a", "1.0.0", Now - 200,
                60, "expired-nonce");
            CoopAdmissionDecision expiredDecision = service.Approve(1,
                Encoding.UTF8.GetBytes(expired));
            Assert.That(expiredDecision.Failure,
                Is.EqualTo(CoopAdmissionFailure.CredentialExpired));

            string valid = codec.Issue("account-b", "1.0.0", Now, 60,
                "tampered-nonce");
            char replacement = valid[^1] == 'A' ? 'B' : 'A';
            string tampered = valid.Substring(0, valid.Length - 1) + replacement;
            CoopAdmissionDecision tamperedDecision = service.Approve(2,
                Encoding.UTF8.GetBytes(tampered));
            Assert.That(tamperedDecision.Failure,
                Is.EqualTo(CoopAdmissionFailure.InvalidCredential));
        }

        [Test]
        public void RejectionReasonNeverContainsCredentialOrSigningKey()
        {
            var codec = new CoopConnectionTicketCodec(Key);
            var service = NewService(codec, 1);
            string credential = codec.Issue("private-player-id", "2.0.0",
                Now, 120, "private-nonce");

            CoopAdmissionDecision decision = service.Approve(1,
                Encoding.UTF8.GetBytes(credential));

            Assert.That(decision.Reason, Does.Not.Contain(credential));
            Assert.That(decision.Reason, Does.Not.Contain("private-player-id"));
            Assert.That(decision.Reason, Does.Not.Contain(
                Encoding.UTF8.GetString(Key)));
        }

        [Test]
        public void AuthoritativeCommandsUseServerBoundIdentityNotClientClaim()
        {
            var codec = new CoopConnectionTicketCodec(Key);
            var service = NewService(codec, 2);
            CoopAdmissionDecision admitted = service.Approve(73,
                Ticket(codec, "account-authoritative", "identity-nonce"));
            var gameObject = new GameObject("Issue86 Authority");
            try
            {
                NetworkCoopSessionAuthority authority =
                    gameObject.AddComponent<NetworkCoopSessionAuthority>();
                authority.EnableServerTestHook();
                authority.ConfigureServer(new CoopServerRules(), new[]
                {
                    new CoopPlayerSpawn(1, new NetVector3(0d, 0d, 0d)),
                    new CoopPlayerSpawn(2, new NetVector3(2d, 0d, 0d))
                }, new[]
                {
                    new CoopTargetSpawn(1, new NetVector3(0d, 0d, 5d),
                        1d, 68d, "medkit")
                });
                authority.RegisterPlayerClient(admitted.Identity.ClientId,
                    admitted.Identity.SimulationPlayerId);
                var spoofed = new NetcodePlayerCommand { PlayerId = 2 };

                Assert.That(authority.TryQueueCommand(73, spoofed, false),
                    Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void ServerSigningSecretMustComeFromEnvironmentAndStayRedacted()
        {
            bool missing = CoopAdmissionEnvironment.TryCreateCodec(_ => null,
                out _, out string error);
            Assert.That(missing, Is.False);
            Assert.That(error, Does.Contain(
                CoopAdmissionEnvironment.SigningSecretVariable));

            string secret = Encoding.UTF8.GetString(Key);
            bool available = CoopAdmissionEnvironment.TryCreateCodec(
                name => name == CoopAdmissionEnvironment.SigningSecretVariable
                    ? secret
                    : null,
                out CoopConnectionTicketCodec codec, out string successError);
            Assert.That(available, Is.True);
            Assert.That(codec, Is.Not.Null);
            Assert.That(successError, Is.Empty);
            Assert.That(error, Does.Not.Contain(secret));
        }

        private static CoopConnectionAdmissionService NewService(
            CoopConnectionTicketCodec codec, int maximumPlayers) =>
            new(codec, "1.0.0", maximumPlayers, () => Now);

        private static byte[] Ticket(CoopConnectionTicketCodec codec,
            string account, string nonce) => Encoding.UTF8.GetBytes(
            codec.Issue(account, "1.0.0", Now, 120, nonce));
    }
}
