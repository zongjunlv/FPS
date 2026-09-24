using FPS.Networking.Session;
using NUnit.Framework;

namespace FPS.Tests.Architecture
{
    public sealed class SelfHostedAccountGatewayTests
    {
        private const string Bundled =
            "{\"brokerUrl\":\"https://old.example.test\"," +
            "\"pinnedCertificateSha256\":\"\"," +
            "\"applicationVersion\":\"0.1.0\"," +
            "\"protocolVersion\":\"1\"," +
            "\"contentVersion\":\"citynew-v1\"," +
            "\"requestTimeoutSeconds\":10}";

        [Test]
        public void ServerMigrationOverridesEndpointWithoutChangingBuildCompatibility()
        {
            Assert.That(CoopDedicatedServerSettings.TryParse(Bundled,
                "{\"brokerUrl\":\"https://new.example.test\"}",
                out CoopDedicatedServerSettings settings, out string error),
                Is.True, error);
            Assert.That(settings.brokerUrl,
                Is.EqualTo("https://new.example.test"));
            Assert.That(settings.applicationVersion, Is.EqualTo("0.1.0"));
            Assert.That(settings.protocolVersion, Is.EqualTo("1"));
            Assert.That(settings.pinnedCertificateSha256, Is.Empty);
        }

        [Test]
        public void ExternalConfigurationCannotDowngradeToPlainHttp()
        {
            Assert.That(CoopDedicatedServerSettings.TryParse(Bundled,
                "{\"brokerUrl\":\"http://new.example.test\"}",
                out _, out string error), Is.False);
            Assert.That(error, Does.Contain("HTTPS"));
        }

        [Test]
        public void InvalidCertificatePinIsRejected()
        {
            Assert.That(CoopDedicatedServerSettings.TryParse(Bundled,
                "{\"pinnedCertificateSha256\":\"abc\"}",
                out _, out string error), Is.False);
            Assert.That(error, Does.Contain("证书指纹"));
        }

        [Test]
        public void RegistrationConflictAndRecoveryExpirationHaveDistinctErrors()
        {
            Assert.That(SelfHostedAuthenticationGateway.ClassifyFailure(
                    new SelfHostedHttpException(409, "username_taken", "")),
                Is.EqualTo(CoopAuthenticationFailure.AccountAlreadyExists));
            Assert.That(SelfHostedAuthenticationGateway.ClassifyFailure(
                    new SelfHostedHttpException(401, "unauthorized", ""),
                    restoring: true),
                Is.EqualTo(CoopAuthenticationFailure.SessionExpired));
            Assert.That(SelfHostedAuthenticationGateway.ClassifyFailure(
                    new SelfHostedHttpException(0, "", "")),
                Is.EqualTo(CoopAuthenticationFailure.NetworkUnavailable));
        }

        [Test]
        public void MissingOrPastExpirationCannotRestoreCachedSession()
        {
            Assert.That(SelfHostedAuthenticationGateway.IsExpired(null),
                Is.True);
            Assert.That(SelfHostedAuthenticationGateway.IsExpired(
                "2020-01-01T00:00:00Z"), Is.True);
            Assert.That(SelfHostedAuthenticationGateway.IsExpired(
                "2999-01-01T00:00:00Z"), Is.False);
        }
    }
}
