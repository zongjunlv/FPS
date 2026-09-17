using System.IO;
using FPS.Networking.Acceptance;
using FPS.Networking.Diagnostics;
using NUnit.Framework;

namespace FPS.Tests.Architecture
{
    public sealed class Issue100RuntimeArgumentsTests
    {
        [Test]
        public void ClientArgumentsBindFixedScenarioAndIdentity()
        {
            string[] arguments =
            {
                "player",
                "-issue100-acceptance",
                "-issue100-role", "client-b",
                "-issue100-run-id", "run-100",
                "-issue100-scenario", "rtt-080-loss-05",
                "-issue100-output", "/tmp/issue100/client-b",
                "-issue86-account", "acceptance-b",
                "-issue99-match", "match-100",
                "-issue100-reconnect"
            };

            bool parsed = Issue100RuntimeArguments.TryParse(arguments,
                out Issue100RuntimeArguments options, out string error);

            Assert.That(parsed, Is.True, error);
            Assert.That(options.Role, Is.EqualTo(Issue100ProcessRole.ClientB));
            Assert.That(options.Scenario.PacketLossBasisPoints,
                Is.EqualTo(500));
            Assert.That(options.AccountId, Is.EqualTo("acceptance-b"));
            Assert.That(options.MatchId, Is.EqualTo("match-100"));
            Assert.That(options.Reconnect, Is.True);
        }

        [Test]
        public void UnknownNetworkConditionIsRejected()
        {
            string[] arguments =
            {
                "player",
                "-issue100-acceptance",
                "-issue100-role", "server",
                "-issue100-run-id", "run-100",
                "-issue100-scenario", "rtt-999-loss-99",
                "-issue100-output", "/tmp/issue100/server",
                "-issue99-match", "match-100"
            };

            bool parsed = Issue100RuntimeArguments.TryParse(arguments,
                out _, out string error);

            Assert.That(parsed, Is.False);
            Assert.That(error, Does.Contain("固定四档"));
        }

        [Test]
        public void ClientCannotRunWithoutAccountOrMatchBinding()
        {
            string[] arguments =
            {
                "player",
                "-issue100-acceptance",
                "-issue100-role", "client-a",
                "-issue100-run-id", "run-100",
                "-issue100-scenario", "rtt-000-loss-00",
                "-issue100-output", "/tmp/issue100/client-a",
                "-issue99-match", ""
            };

            bool parsed = Issue100RuntimeArguments.TryParse(arguments,
                out _, out string error);

            Assert.That(parsed, Is.False);
            Assert.That(error, Does.Contain("格式有效"));
        }

        [Test]
        public void ClientACanOwnContinuousVideoPipe()
        {
            string[] arguments =
            {
                "player",
                "-issue100-acceptance",
                "-issue100-role", "client-a",
                "-issue100-run-id", "run-100",
                "-issue100-scenario", "rtt-000-loss-00",
                "-issue100-output", "/tmp/issue100/client-a",
                "-issue86-account", "acceptance-a",
                "-issue99-match", "match-100",
                "-issue100-record-video",
                "-issue100-video-pipe", "/tmp/issue100/video.raw"
            };

            bool parsed = Issue100RuntimeArguments.TryParse(arguments,
                out Issue100RuntimeArguments options, out string error);

            Assert.That(parsed, Is.True, error);
            Assert.That(options.RecordVideo, Is.True);
            Assert.That(options.VideoPipePath,
                Is.EqualTo("/tmp/issue100/video.raw"));
        }

        [Test]
        public void VideoCaptureRejectsWrongRoleOrMissingPipe()
        {
            string[] wrongRole =
            {
                "player", "-issue100-acceptance",
                "-issue100-role", "client-b",
                "-issue100-run-id", "run-100",
                "-issue100-scenario", "rtt-000-loss-00",
                "-issue100-output", "/tmp/issue100/client-b",
                "-issue86-account", "acceptance-b",
                "-issue99-match", "match-100",
                "-issue100-record-video",
                "-issue100-video-pipe", "/tmp/issue100/video.raw"
            };
            string[] missingPipe =
            {
                "player", "-issue100-acceptance",
                "-issue100-role", "client-a",
                "-issue100-run-id", "run-100",
                "-issue100-scenario", "rtt-000-loss-00",
                "-issue100-output", "/tmp/issue100/client-a",
                "-issue86-account", "acceptance-a",
                "-issue99-match", "match-100",
                "-issue100-record-video"
            };

            Assert.That(Issue100RuntimeArguments.TryParse(wrongRole,
                out _, out string roleError), Is.False);
            Assert.That(roleError, Does.Contain("client-a"));
            Assert.That(Issue100RuntimeArguments.TryParse(missingPipe,
                out _, out string pipeError), Is.False);
            Assert.That(pipeError, Does.Contain("video-pipe"));
        }

        [Test]
        public void ReconnectCredentialCanBeReadFromPrivateRuntimeFile()
        {
            string path = Path.Combine(Path.GetTempPath(),
                "issue101-reconnect.ticket");
            File.WriteAllText(path, "short-lived-ticket");
            try
            {
                string[] arguments =
                {
                    "player", "-issue100-acceptance",
                    "-issue100-role", "client-b",
                    "-issue100-run-id", "run-101",
                    "-issue100-scenario", "rtt-000-loss-00",
                    "-issue100-output", "/tmp/issue101/client-b",
                    "-issue86-account", "acceptance-b",
                    "-issue99-match", "match-101",
                    "-issue101-reconnect-ticket-file", path
                };

                bool parsed = Issue100RuntimeArguments.TryParse(arguments,
                    out Issue100RuntimeArguments options, out string error);

                Assert.That(parsed, Is.True, error);
                Assert.That(options.ReconnectCredential,
                    Is.EqualTo("short-lived-ticket"));
            }
            finally
            {
                File.Delete(path);
            }
        }
    }
}
