using System.Linq;
using FPS.Determinism;
using NUnit.Framework;
using UnityEngine;

public sealed class Issue55RuntimeRecordingTests
{
    [Test]
    public void RecorderEmitsExplicitShotAndKillAuditEvents()
    {
        var root = new GameObject("Issue55 Recorder");
        try
        {
            RunDeterminismRecorder recorder = root.AddComponent<RunDeterminismRecorder>();
            recorder.Configure(55, null, null, null, null, null);
            recorder.RecordShot(new ShotResult(
                true,
                new Vector3(1f, 2f, 3f),
                Vector3.back,
                SurfaceType.Metal,
                new DamageResult(true, true, 25f, HitRegion.Head)));
            recorder.RecordEnemyDeath(new EnemyDeathEvent(
                null,
                7,
                2,
                new DamageInfo(25f, Vector3.one, Vector3.forward, root, DamageType.Hitscan),
                30,
                Vector3.one,
                "spider",
                LootRewardTier.Elite));
            recorder.RecordWaveTransition("started", 2);

            Assert.That(recorder.Record.Events.Select(value => value.Type), Is.EqualTo(new[]
            {
                RunEventType.ShotFired,
                RunEventType.EnemyKilled,
                RunEventType.WaveTransition
            }));
            Assert.That(StableEventPayload.Parse(recorder.Record.Events[0].Payload)["weapon"], Is.Empty);
            Assert.That(StableEventPayload.Parse(recorder.Record.Events[1].Payload)["spawnId"], Is.EqualTo("7"));
            Assert.That(StableEventPayload.Parse(recorder.Record.Events[2].Payload)["phase"], Is.EqualTo("started"));
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }
}
