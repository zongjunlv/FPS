using FPS.Determinism;
using NUnit.Framework;
using UnityEngine;

public sealed class Issue54RuntimeReplayTests
{
    [Test]
    public void RecordedInputPayloadDecodesToSameLogicalSample()
    {
        string payload = StableEventPayload.Create(
            RunPayloadField.Number("flags", (1 << 0) | (1 << 6) | (1 << 14)),
            RunPayloadField.Number("lookX", -250),
            RunPayloadField.Number("lookY", 750),
            RunPayloadField.Number("moveX", 1000),
            RunPayloadField.Number("moveY", -500),
            RunPayloadField.Number("quickUse", 1),
            RunPayloadField.Number("weaponCycle", -1),
            RunPayloadField.Number("weaponSlot", 0));

        bool decoded = RunReplayRuntimeAdapter.TryDecodeInput(
            payload, out PlayerInputSample sample, out string error);

        Assert.That(decoded, Is.True, error);
        Assert.That(sample.Move, Is.EqualTo(new Vector2(1f, -0.5f)));
        Assert.That(sample.Look, Is.EqualTo(new Vector2(-0.25f, 0.75f)));
        Assert.That(sample.JumpPressed, Is.True);
        Assert.That(sample.AttackHeld, Is.True);
        Assert.That(sample.LookUsesPointerDelta, Is.True);
        Assert.That(sample.WeaponSelection, Is.Zero);
        Assert.That(sample.WeaponCycleDirection, Is.EqualTo(-1));
        Assert.That(sample.QuickUseSelection, Is.EqualTo(1));
    }

    [Test]
    public void RecorderExportsPeriodicStateCheckpointContract()
    {
        GameObject root = new GameObject("Issue54 Recorder");
        try
        {
            RunDeterminismRecorder recorder = root.AddComponent<RunDeterminismRecorder>();
            recorder.Configure(54, null, null, null, null, null);
            recorder.RecordInput(default);
            recorder.RecordCheckpoint(ReplayStateSnapshot.Create(
                ReplayStateField.Number("player/progression/level", 3),
                ReplayStateField.Number("wave/current", 2)));

            RunRecord imported = RunRecordCodec.Import(recorder.ExportJson());

            Assert.That(imported.Checkpoints.Count, Is.EqualTo(1));
            Assert.That(imported.Checkpoints[0].Tick, Is.EqualTo(1));
            Assert.That(imported.Checkpoints[0].State.Fields.Count, Is.EqualTo(2));
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void ReplayInputDecoderRejectsMissingFieldsWithoutPartialSample()
    {
        string incomplete = StableEventPayload.Create(
            RunPayloadField.Number("flags", 0),
            RunPayloadField.Number("moveX", 1000));

        bool decoded = RunReplayRuntimeAdapter.TryDecodeInput(
            incomplete, out PlayerInputSample sample, out string error);

        Assert.That(decoded, Is.False);
        Assert.That(error, Does.Contain("缺少字段"));
        Assert.That(sample.Move, Is.EqualTo(Vector2.zero));
    }
}
