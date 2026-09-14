using FPS.Networking.Session;
using NUnit.Framework;

namespace FPS.Tests.Architecture
{
    public sealed class Issue65SessionEntryTests
    {
        [TestCase(" ab12cd ", "AB12CD")]
        [TestCase("XYZ789", "XYZ789")]
        [TestCase("", "")]
        [TestCase(null, "")]
        public void JoinCodeIsNormalizedForPlayerEntry(
            string source,
            string expected)
        {
            Assert.That(
                CoopSessionController.NormalizeJoinCode(source),
                Is.EqualTo(expected));
        }

        [Test]
        public void VerticalSliceRemainsExplicitlyTwoPlayer()
        {
            Assert.That(CoopSessionController.MaximumPlayers, Is.EqualTo(2));
        }
    }
}
