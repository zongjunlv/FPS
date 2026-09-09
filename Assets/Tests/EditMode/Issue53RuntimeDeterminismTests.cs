using System.Linq;
using FPS.Determinism;
using NUnit.Framework;
using UnityEngine;

public sealed class Issue53RuntimeDeterminismTests
{
    [Test]
    public void DynamicWaveUsesRunSeedAndRemainsRepeatable()
    {
        WaveDefinition first = CreateWave();
        WaveDefinition second = CreateWave();
        try
        {
            first.PrepareForRun(5301);
            second.PrepareForRun(5301);

            Assert.That(
                first.ResolvedEntries.Select(entry => entry.EnemyTypeId),
                Is.EqualTo(second.ResolvedEntries.Select(entry => entry.EnemyTypeId)));
            Assert.That(first.TotalEnemyCount, Is.EqualTo(second.TotalEnemyCount));
        }
        finally
        {
            Object.DestroyImmediate(first);
            Object.DestroyImmediate(second);
        }
    }

    [Test]
    public void RecorderCapturesVersionedInputUpgradeSpawnAndLootEvents()
    {
        GameObject root = new GameObject("Run Recorder Test");
        WaveDefinition wave = CreateWave();
        WaveSequenceDefinition sequence =
            ScriptableObject.CreateInstance<WaveSequenceDefinition>();
        sequence.ConfigureWithStableId(
            "issue53-sequence",
            new[] { new WaveStageDefinition(wave, 0f) });
        try
        {
            wave.PrepareForRun(53);
            RunDeterminismRecorder recorder =
                root.AddComponent<RunDeterminismRecorder>();
            recorder.Configure(53, null, null, null, null, sequence);
            recorder.RecordInput(new PlayerInputSample(
                Vector2.up, Vector2.right, true,
                true, false, false, false, false,
                true, true, false, false, false,
                false, false, false, false, 0, 1, -1));
            recorder.RecordUpgrade(new PlayerUpgradeSelectionEvent(
                1, "rapid-cycling", 1,
                new[] { "vitality", "rapid-cycling", "armor" }));
            WaveEnemyEntry elite = new WaveEnemyEntry(
                null, 1, LootRewardTier.Elite, "elite", null, null, 2, "elite");
            var request = new EnemySpawnRequest(
                7, 1, elite, new Vector3(1f, 2f, 3f), Quaternion.identity, null);
            recorder.RecordEnemySpawn(new EnemySpawnedEvent(request, default));
            recorder.RecordLoot(new LootRewardSettlement(
                LootRewardTier.Elite, 1, 7,
                new[] { new LootDropStack("medkit", 2) }, 1));

            RunRecord imported = RunRecordCodec.Import(recorder.ExportJson());
            Assert.That(imported.SchemaVersion,
                Is.EqualTo(RunRecord.CurrentSchemaVersion));
            Assert.That(imported.RunSeed, Is.EqualTo(53));
            Assert.That(imported.Events.Select(value => value.Type),
                Does.Contain(RunEventType.InputSampled));
            Assert.That(imported.Events.Select(value => value.Type),
                Does.Contain(RunEventType.UpgradeSelected));
            Assert.That(imported.Events.Select(value => value.Type),
                Does.Contain(RunEventType.EnemySpawned));
            Assert.That(imported.Events.Select(value => value.Type),
                Does.Contain(RunEventType.EliteGenerated));
            Assert.That(imported.Events.Select(value => value.Type),
                Does.Contain(RunEventType.LootGenerated));
        }
        finally
        {
            Object.DestroyImmediate(root);
            Object.DestroyImmediate(sequence);
            Object.DestroyImmediate(wave);
        }
    }

    private static WaveDefinition CreateWave()
    {
        WaveDefinition wave = ScriptableObject.CreateInstance<WaveDefinition>();
        wave.ConfigureIdentity("issue53-wave");
        wave.ConfigureThreatBudget(
            8,
            4101,
            3,
            0f,
            new[]
            {
                new WaveEnemyEntry(null, 4, LootRewardTier.Normal,
                    "assault", null, null, 1, "assault"),
                new WaveEnemyEntry(null, 2, LootRewardTier.Normal,
                    "support", null, null, 2, "support"),
                new WaveEnemyEntry(null, 1, LootRewardTier.Elite,
                    "elite", null, null, 3, "elite")
            },
            null,
            0.5f);
        return wave;
    }
}
