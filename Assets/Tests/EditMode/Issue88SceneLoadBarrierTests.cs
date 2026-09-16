using FPS.Networking.Session;
using NUnit.Framework;

namespace FPS.Tests.Architecture
{
    public sealed class Issue88SceneLoadBarrierTests
    {
        [Test]
        public void TwoIndependentPlayersCompleteOneServerEpoch()
        {
            var barrier = Begin();

            Assert.That(barrier.ReportReady("host", "epoch-1"),
                Is.EqualTo(CoopSceneReadyResult.Accepted));
            Assert.That(barrier.ReportReady("guest", "epoch-1"),
                Is.EqualTo(CoopSceneReadyResult.Completed));
            Assert.That(barrier.State, Is.EqualTo(CoopSceneLoadState.Ready));
        }

        [Test]
        public void DuplicateAndOldEpochReportsCannotAdvanceBarrier()
        {
            var barrier = Begin();
            barrier.ReportReady("host", "epoch-1");

            Assert.That(barrier.ReportReady("host", "epoch-1"),
                Is.EqualTo(CoopSceneReadyResult.Duplicate));
            Assert.That(barrier.ReportReady("guest", "old-epoch"),
                Is.EqualTo(CoopSceneReadyResult.WrongEpoch));
            Assert.That(barrier.ReadyCount, Is.EqualTo(1));
            Assert.That(barrier.State, Is.EqualTo(CoopSceneLoadState.Loading));
        }

        [Test]
        public void UnexpectedLatePlayerCannotJoinActiveBarrier()
        {
            var barrier = Begin();

            Assert.That(barrier.ReportReady("late-player", "epoch-1"),
                Is.EqualTo(CoopSceneReadyResult.UnexpectedPlayer));
            Assert.That(barrier.ReadyCount, Is.Zero);
        }

        [Test]
        public void TimeoutStopsSpawnBarrier()
        {
            var barrier = Begin();
            barrier.ReportReady("host", "epoch-1");

            Assert.That(barrier.Tick(29.9d), Is.False);
            Assert.That(barrier.Tick(30d), Is.True);
            Assert.That(barrier.State,
                Is.EqualTo(CoopSceneLoadState.TimedOut));
        }

        [Test]
        public void MemberLeavingCancelsInsteadOfStartingShortHanded()
        {
            var barrier = Begin();
            barrier.ReportReady("host", "epoch-1");

            Assert.That(barrier.RemovePlayer("guest"), Is.True);
            Assert.That(barrier.State,
                Is.EqualTo(CoopSceneLoadState.Cancelled));
            Assert.That(barrier.ReportReady("host", "epoch-1"),
                Is.EqualTo(CoopSceneReadyResult.NotLoading));
        }

        private static CoopSceneLoadBarrier Begin()
        {
            var barrier = new CoopSceneLoadBarrier();
            barrier.Begin("epoch-1", "CityNew", 18018,
                new[] { "host", "guest" }, 0d, 30d);
            return barrier;
        }
    }
}
