using FPS.Networking.Netcode;
using NUnit.Framework;

namespace FPS.Tests.Architecture
{
    public sealed class Issue85DedicatedServerConfigurationTests
    {
        [Test]
        public void DefaultsAreDeterministicAndProductionSafe()
        {
            Assert.That(DedicatedServerConfiguration.TryParse(
                new[] { "game", "-fps-server" }, "1.2.3", "/tmp/fps-server",
                out DedicatedServerConfiguration configuration,
                out string error), Is.True, error);
            Assert.That(configuration.MapScenePath,
                Is.EqualTo(DedicatedServerConfiguration.CityNewScenePath));
            Assert.That(configuration.Port,
                Is.EqualTo(NetworkEndpointSettings.DefaultPort));
            Assert.That(configuration.MaximumPlayers, Is.EqualTo(2));
            Assert.That(configuration.TickRate, Is.EqualTo(60));
            Assert.That(configuration.Seed, Is.EqualTo(18018));
            Assert.That(configuration.Version, Is.EqualTo("1.2.3"));
            Assert.That(configuration.ProtocolVersion, Is.EqualTo("1"));
            Assert.That(configuration.ContentVersion,
                Is.EqualTo("citynew-v1"));
            Assert.That(configuration.IdleTimeoutSeconds, Is.EqualTo(120));
            Assert.That(configuration.DiagnosticsPath,
                Does.EndWith("server-local-match-diagnostics.json"));
        }

        [Test]
        public void EveryServerParameterCanBeOverridden()
        {
            string[] arguments =
            {
                "game", "-fps-server",
                "-server-map", "CityNew",
                "-server-port", "19085",
                "-server-match", "interview.demo_85",
                "-server-max-players", "6",
                "-server-seed", "85001",
                "-server-version", "2.4.0-server",
                "-server-protocol-version", "net-7",
                "-server-content-version", "citynew-2026.09",
                "-server-tick-rate", "30",
                "-server-idle-timeout", "300",
                "-server-diagnostics", "/tmp/issue85.json"
            };

            Assert.That(DedicatedServerConfiguration.TryParse(arguments,
                "fallback", "/tmp", out DedicatedServerConfiguration result,
                out string error), Is.True, error);
            Assert.That(result.Port, Is.EqualTo(19085));
            Assert.That(result.MatchId, Is.EqualTo("interview.demo_85"));
            Assert.That(result.MaximumPlayers, Is.EqualTo(6));
            Assert.That(result.Seed, Is.EqualTo(85001));
            Assert.That(result.Version, Is.EqualTo("2.4.0-server"));
            Assert.That(result.ProtocolVersion, Is.EqualTo("net-7"));
            Assert.That(result.ContentVersion,
                Is.EqualTo("citynew-2026.09"));
            Assert.That(result.TickRate, Is.EqualTo(30));
            Assert.That(result.IdleTimeoutSeconds, Is.EqualTo(300));
            Assert.That(result.DiagnosticsPath,
                Is.EqualTo("/tmp/issue85.json"));
        }

        [TestCase("-server-port", "0", "端口")]
        [TestCase("-server-max-players", "17", "最大人数")]
        [TestCase("-server-tick-rate", "9", "Tick Rate")]
        [TestCase("-server-match", "bad match", "战局 ID")]
        [TestCase("-server-map", "MissingMap", "地图")]
        [TestCase("-server-version", "bad version", "服务器版本")]
        [TestCase("-server-protocol-version", "bad version", "协议版本")]
        [TestCase("-server-content-version", "bad/content", "内容版本")]
        [TestCase("-server-idle-timeout", "14", "空闲回收")]
        public void InvalidArgumentsFailWithChineseGuidance(string key,
            string value, string expected)
        {
            Assert.That(DedicatedServerConfiguration.TryParse(
                new[] { "game", "-fps-server", key, value }, "1", "/tmp",
                out _, out string error), Is.False);
            Assert.That(error, Does.Contain(expected));
        }

        [Test]
        public void ServerActivationRequiresExplicitFlag()
        {
            Assert.That(DedicatedServerConfiguration.IsRequested(
                new[] { "game", "-fps-server" }), Is.True);
            Assert.That(DedicatedServerConfiguration.IsRequested(
                new[] { "game", "-batchmode" }), Is.False);
        }
    }
}
