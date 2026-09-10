using System.Linq;
using NUnit.Framework;

namespace FPS.Tests.Architecture
{
    public sealed class Issue56CombatRuntimeDiagnosticsTests
    {
        [Test]
        public void ClosedRefreshGateNeverRequestsSampling()
        {
            var gate = new RuntimeDiagnosticsRefreshGate(0.25f);

            Assert.That(gate.TryConsume(0f), Is.False);
            Assert.That(gate.TryConsume(100f), Is.False);
        }

        [Test]
        public void VisibleRefreshGateSamplesImmediatelyThenAtBoundedInterval()
        {
            var gate = new RuntimeDiagnosticsRefreshGate(0.25f);
            gate.SetVisible(true, 10f);

            Assert.That(gate.TryConsume(10f), Is.True);
            Assert.That(gate.TryConsume(10.1f), Is.False);
            Assert.That(gate.TryConsume(10.25f), Is.True);
            gate.SetVisible(false, 10.3f);
            Assert.That(gate.TryConsume(20f), Is.False);
        }

        [Test]
        public void DiagnosticsValuesClampInvalidRuntimeCounters()
        {
            var perception = new PerceptionRuntimeDiagnostics(
                -1, -2, -3, -4, -5, -6, -7f);
            var pool = new EnemyPoolRuntimeDiagnostics(
                "spider", -1, -2, -3, -4, -5);

            Assert.That(perception.BudgetPerFrame, Is.Zero);
            Assert.That(perception.AverageLatencyFrames, Is.Zero);
            Assert.That(pool.Capacity, Is.Zero);
            Assert.That(pool.Expansion, Is.Zero);
        }

        [Test]
        public void SnapshotExposesAllAcceptanceSections()
        {
            var states = new[] { new RuntimeDiagnosticCount("Alert", 2) };
            var roles = new[] { new RuntimeDiagnosticCount("raider", 1) };
            var lod = new[] { new RuntimeDiagnosticCount("Near", 2) };
            var perception = new PerceptionRuntimeDiagnostics(4, 2, 2, 0, 1, 8, 4f);
            var wave = new WaveRuntimeDiagnostics(2, 3, "Spawning", 12, 11,
                new[] { new RuntimeDiagnosticCount("spider", 3) },
                new[] { new RuntimeDiagnosticCount("spider", 1) });
            var pools = new[]
            {
                new EnemyPoolRuntimeDiagnostics("spider", 8, 2, 6, 5, 1)
            };

            var snapshot = new RuntimeCombatDiagnosticsSnapshot(
                states, roles, lod, perception, wave, pools);

            Assert.That(snapshot.AwarenessStates.Single().Label, Is.EqualTo("Alert"));
            Assert.That(snapshot.Roles.Single().Label, Is.EqualTo("raider"));
            Assert.That(snapshot.LodTiers.Single().Label, Is.EqualTo("Near"));
            Assert.That(snapshot.Perception.QueueLength, Is.Zero);
            Assert.That(snapshot.Wave.SpawnQueue.Single().Count, Is.EqualTo(1));
            Assert.That(snapshot.Pools.Single().Reuse, Is.EqualTo(5));
        }
    }
}
