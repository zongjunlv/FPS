using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using FPS.SaveGame;
using NUnit.Framework;

namespace FPS.Tests.Architecture
{
    public sealed class Issue52SnapshotDurabilityTests
    {
        // Produced by the shipped schema-v1 codec and kept literal so the test cannot
        // accidentally regenerate history with today's model or checksum algorithm.
        private const string VersionOneFixture =
            "{\"SchemaVersion\":1,\"Seed\":4101,\"Health\":73.5,\"Armor\":12.25," +
            "\"CurrentWeaponId\":\"rifle\",\"Weapons\":[{\"WeaponId\":\"rifle\",\"Magazine\":21,\"Reserve\":130}," +
            "{\"WeaponId\":\"handgun\",\"Magazine\":8,\"Reserve\":20}]," +
            "\"Upgrades\":[{\"UpgradeId\":\"vitality\",\"Level\":2}]," +
            "\"UpgradeSelectionHistory\":[\"vitality\",\"vitality\"]," +
            "\"Checksum\":\"596f9def1da5663a14a9255a6e7d4e3c411cfabf01e4415ba418d21c69a4c73e\"}";

        private string directory;

        [SetUp]
        public void SetUp() => directory = Path.Combine(Path.GetTempPath(), "fps-issue52-" + Guid.NewGuid().ToString("N"));

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }

        [Test]
        public void ShippedVersionOneFixtureMigratesToCurrentSchemaWithStructuredResult()
        {
            SnapshotDecodeResult decoded = RunSnapshotCodec.Decode(VersionOneFixture);

            Assert.That(decoded.Success, Is.True, decoded.Error);
            Assert.That(decoded.OriginalSchemaVersion, Is.EqualTo(1));
            Assert.That(decoded.WasMigrated, Is.True);
            Assert.That(decoded.Snapshot.SchemaVersion, Is.EqualTo(RunSnapshot.CurrentSchemaVersion));
            Assert.That(decoded.Snapshot.Seed, Is.EqualTo(4101));
            Assert.That(decoded.Snapshot.Weapons[1].WeaponId, Is.EqualTo("handgun"));
            Assert.That(decoded.Snapshot.InventorySlots, Is.Empty);
            Assert.That(decoded.Snapshot.Wave.Phase, Is.Zero);
            Assert.That(decoded.Snapshot.Checksum, Is.EqualTo(SnapshotChecksum.Compute(decoded.Snapshot)));
        }

        [Test]
        public void VersionOneFixtureMustPassItsHistoricalChecksumBeforeMigration()
        {
            string tampered = VersionOneFixture.Replace("\"Magazine\":21", "\"Magazine\":20");

            SnapshotDecodeResult decoded = RunSnapshotCodec.Decode(tampered);

            Assert.That(decoded.Success, Is.False);
            Assert.That(decoded.OriginalSchemaVersion, Is.EqualTo(1));
            Assert.That(decoded.Error, Does.Contain("校验"));
        }

        [Test]
        public void SaveUsesAtomicTemporaryAndKeepsThePreviousCompleteSnapshotAsBackup()
        {
            RunSnapshotStore store = Store();
            Assert.That(store.Save(Example(10)).Success, Is.True);
            Assert.That(store.Save(Example(20)).Success, Is.True);

            Assert.That(File.Exists(store.TemporaryPath), Is.False);
            Assert.That(RunSnapshotCodec.Decode(File.ReadAllText(store.FilePath)).Snapshot.Seed, Is.EqualTo(20));
            Assert.That(RunSnapshotCodec.Decode(File.ReadAllText(store.BackupPath)).Snapshot.Seed, Is.EqualTo(10));
        }

        [Test]
        public void PowerLossTemporaryIsIgnoredAndCannotReplaceLastCommittedSnapshot()
        {
            RunSnapshotStore store = Store();
            Assert.That(store.Save(Example(10)).Success, Is.True);
            Directory.CreateDirectory(directory);
            File.WriteAllText(store.TemporaryPath, "{\"SchemaVersion\":2", Encoding.UTF8);

            SnapshotResult loaded = store.Load();

            Assert.That(loaded.Success, Is.True, loaded.Message);
            Assert.That(loaded.Snapshot.Seed, Is.EqualTo(10));
            Assert.That(loaded.Source, Is.EqualTo(SnapshotLoadSource.Primary));
            Assert.That(loaded.LoadDisposition, Is.EqualTo(SnapshotLoadDisposition.Current));
        }

        [Test]
        public void ChecksumFailureRecoversMostRecentValidBackupAndPreservesCorruptBytes()
        {
            RunSnapshotStore store = Store();
            Assert.That(store.Save(Example(10)).Success, Is.True);
            Assert.That(store.Save(Example(20)).Success, Is.True);
            string corrupt = File.ReadAllText(store.FilePath).Replace("\"Seed\":20", "\"Seed\":21");
            File.WriteAllText(store.FilePath, corrupt, new UTF8Encoding(false));

            SnapshotResult recovered = store.Load();

            Assert.That(recovered.Success, Is.True, recovered.Message);
            Assert.That(recovered.Source, Is.EqualTo(SnapshotLoadSource.Backup));
            Assert.That(recovered.LoadDisposition, Is.EqualTo(SnapshotLoadDisposition.RecoveredFromBackup));
            Assert.That(recovered.RecoveredFromBackup, Is.True);
            Assert.That(recovered.Snapshot.Seed, Is.EqualTo(10));
            Assert.That(recovered.CorruptFilePath, Is.Not.Null);
            Assert.That(File.ReadAllText(recovered.CorruptFilePath), Is.EqualTo(corrupt));
            Assert.That(store.Load().Snapshot.Seed, Is.EqualTo(10), "恢复后主存档也应完成自愈");
        }

        [Test]
        public void SavingOverCorruptPrimaryPreservesTheLastKnownGoodBackup()
        {
            RunSnapshotStore store = Store();
            Assert.That(store.Save(Example(10)).Success, Is.True);
            Assert.That(store.Save(Example(20)).Success, Is.True);
            string validBackup = File.ReadAllText(store.BackupPath);
            const string corrupt = "{broken-primary";
            File.WriteAllText(store.FilePath, corrupt);

            SnapshotResult saved = store.Save(Example(30));

            Assert.That(saved.Success, Is.True, saved.Message);
            Assert.That(RunSnapshotCodec.Decode(File.ReadAllText(store.FilePath)).Snapshot.Seed, Is.EqualTo(30));
            Assert.That(File.ReadAllText(store.BackupPath), Is.EqualTo(validBackup));
            Assert.That(saved.CorruptFilePath, Is.Not.Null);
            Assert.That(File.ReadAllText(saved.CorruptFilePath), Is.EqualTo(corrupt));
        }

        [Test]
        public void UnrecoverablePrimaryIsQuarantinedAndReportedWithoutParsingMessageText()
        {
            RunSnapshotStore store = Store();
            Directory.CreateDirectory(directory);
            const string corrupt = "{broken";
            File.WriteAllText(store.FilePath, corrupt);

            SnapshotResult result = store.Load();

            Assert.That(result.Status, Is.EqualTo(SnapshotStatus.InvalidData));
            Assert.That(result.LoadDisposition, Is.EqualTo(SnapshotLoadDisposition.None));
            Assert.That(result.Source, Is.EqualTo(SnapshotLoadSource.None));
            Assert.That(result.CorruptFilePath, Is.Not.Null);
            Assert.That(File.ReadAllText(result.CorruptFilePath), Is.EqualTo(corrupt));
            Assert.That(File.Exists(store.FilePath), Is.False);
        }

        [Test]
        public void BackupRecoveryCanAlsoReportThatAnOldSchemaWasMigrated()
        {
            RunSnapshotStore store = Store();
            Directory.CreateDirectory(directory);
            File.WriteAllText(store.FilePath, "{broken");
            File.WriteAllText(store.BackupPath, VersionOneFixture, new UTF8Encoding(false));

            SnapshotResult result = store.Load();

            Assert.That(result.Success, Is.True, result.Message);
            Assert.That(result.Source, Is.EqualTo(SnapshotLoadSource.Backup));
            Assert.That(result.LoadDisposition, Is.EqualTo(SnapshotLoadDisposition.RecoveredFromBackupAndMigrated));
            Assert.That(result.OriginalSchemaVersion, Is.EqualTo(1));
            Assert.That(result.WasMigrated, Is.True);
        }

        private RunSnapshotStore Store() => new RunSnapshotStore(Path.Combine(directory, "run-snapshot.json"));

        private static RunSnapshot Example(int seed) => new RunSnapshot
        {
            Seed = seed,
            Health = 80f,
            Armor = 12f,
            CurrentWeaponId = "rifle",
            Weapons = new List<WeaponAmmoSnapshot>
            {
                new WeaponAmmoSnapshot { WeaponId = "rifle", Magazine = 20, Reserve = 100 }
            },
            Upgrades = new List<UpgradeLevelSnapshot>(),
            UpgradeSelectionHistory = new List<string>()
        };
    }
}
