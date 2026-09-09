using NUnit.Framework;

namespace FPS.Tests.Architecture
{
    public sealed class Issue51WaveRestoreTests
    {
        [Test]
        public void SingleWaveRestoreIsAtomicAndSilent()
        {
            var source = new SingleWaveState(3, 2);
            Assert.That(source.TryRegisterSpawn(11), Is.True);
            Assert.That(source.TryRegisterSpawn(12), Is.True);
            Assert.That(source.TrySettle(11), Is.True);

            var restored = new SingleWaveState(3, 2);
            int completedEvents = 0;
            restored.Completed += () => completedEvents++;
            Assert.That(restored.TryRestore(source.CaptureState(), out string error), Is.True, error);
            Assert.That(completedEvents, Is.Zero);
            Assert.That(restored.SpawnedCount, Is.EqualTo(2));
            Assert.That(restored.AliveCount, Is.EqualTo(1));
            Assert.That(restored.SettledCount, Is.EqualTo(1));

            SingleWaveStateSnapshot before = restored.CaptureState();
            var invalid = new SingleWaveStateSnapshot(3, 2, new[] { 11 }, new[] { 99 }, new int[0]);
            Assert.That(restored.TryRestore(invalid, out _), Is.False);
            Assert.That(restored.CaptureState().SpawnedIds, Is.EqualTo(before.SpawnedIds));
            Assert.That(restored.CaptureState().ActiveIds, Is.EqualTo(before.ActiveIds));

            Assert.That(restored.TrySettle(12), Is.True);
            Assert.That(restored.TryRegisterSpawn(13), Is.True);
            Assert.That(restored.TrySettle(13), Is.True);
            Assert.That(completedEvents, Is.EqualTo(1));
        }

        [Test]
        public void MultiWaveRestoresCurrentPhaseIdsAndIntermissionWithoutEvents()
        {
            var rules = new[]
            {
                new WaveStageRules(2, 2, 4f),
                new WaveStageRules(1, 1, 0f)
            };
            var source = new MultiWaveFlowState(rules);
            source.StartRun();
            source.TryRegisterSpawn(21);
            source.TryRegisterSpawn(22);
            source.TrySettle(21);
            source.TrySettle(22);
            source.Tick(1.5f);

            var target = new MultiWaveFlowState(rules);
            Assert.That(target.TryRestore(source.CaptureState(), out string error), Is.True, error);
            Assert.That(target.CurrentWave, Is.EqualTo(1));
            Assert.That(target.Phase, Is.EqualTo(WaveRunPhase.Intermission));
            Assert.That(target.SpawnedCount, Is.EqualTo(2));
            Assert.That(target.SettledCount, Is.EqualTo(2));
            Assert.That(target.IntermissionRemaining, Is.EqualTo(2.5f).Within(0.001f));

            Assert.That(target.Tick(2.5f), Is.True);
            Assert.That(target.CurrentWave, Is.EqualTo(2));
            Assert.That(target.Phase, Is.EqualTo(WaveRunPhase.Spawning));
        }

        [Test]
        public void MultiWaveRejectsPhaseMismatchWithoutChangingLiveState()
        {
            var target = new MultiWaveFlowState(new[] { new WaveStageRules(2, 1, 0f) });
            target.StartRun();
            target.TryRegisterSpawn(1);
            MultiWaveFlowStateSnapshot before = target.CaptureState();
            var invalid = new MultiWaveFlowStateSnapshot(
                1,
                WaveRunPhase.Completed,
                before.CurrentWaveState,
                0f);

            Assert.That(target.TryRestore(invalid, out _), Is.False);
            Assert.That(target.Phase, Is.EqualTo(before.Phase));
            Assert.That(target.SpawnedCount, Is.EqualTo(1));
            Assert.That(target.AliveCount, Is.EqualTo(1));
        }
    }
}
