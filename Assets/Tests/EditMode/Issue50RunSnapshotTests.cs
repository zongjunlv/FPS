using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using FPS.SaveGame;
using NUnit.Framework;

namespace FPS.Tests.Architecture
{
    public sealed class Issue50RunSnapshotTests
    {
        private string directory;

        [SetUp]
        public void SetUp() => directory = Path.Combine(Path.GetTempPath(), "fps-snapshot-tests-" + Guid.NewGuid().ToString("N"));

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }

        [Test]
        public void SerializeRoundTripPreservesEverySnapshotFieldAndChecksum()
        {
            var original = Example();
            var json = RunSnapshotCodec.Serialize(original);
            Assert.That(RunSnapshotCodec.TryDeserialize(json, out var loaded, out var error), Is.True, error);
            Assert.That(loaded.SchemaVersion, Is.EqualTo(RunSnapshot.CurrentSchemaVersion));
            Assert.That(loaded.Seed, Is.EqualTo(4101));
            Assert.That(loaded.Health, Is.EqualTo(73.5f));
            Assert.That(loaded.Armor, Is.EqualTo(12.25f));
            Assert.That(loaded.CurrentWeaponId, Is.EqualTo("rifle"));
            Assert.That(loaded.Weapons[0].WeaponId, Is.EqualTo("rifle"));
            Assert.That(loaded.Weapons[0].Magazine, Is.EqualTo(21));
            Assert.That(loaded.Weapons[0].Reserve, Is.EqualTo(130));
            Assert.That(loaded.Weapons[1].WeaponId, Is.EqualTo("handgun"));
            Assert.That(loaded.Upgrades[0].UpgradeId, Is.EqualTo("vitality"));
            Assert.That(loaded.Upgrades[0].Level, Is.EqualTo(2));
            Assert.That(loaded.UpgradeSelectionHistory, Is.EqualTo(original.UpgradeSelectionHistory));
            Assert.That(loaded.Checksum, Is.EqualTo(original.Checksum));
            Assert.That(SnapshotChecksum.Compute(loaded), Is.EqualTo(original.Checksum));
        }

        [Test]
        public void ChecksumDoesNotDependOnCurrentCultureOrChecksumField()
        {
            var snapshot = Example();
            var previous = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
                var checksum = SnapshotChecksum.Compute(snapshot);
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
                snapshot.Checksum = "ignored";
                Assert.That(SnapshotChecksum.Compute(snapshot), Is.EqualTo(checksum));
                snapshot.Weapons[0].Magazine--;
                Assert.That(SnapshotChecksum.Compute(snapshot), Is.Not.EqualTo(checksum));
            }
            finally { CultureInfo.CurrentCulture = previous; }
        }

        [Test]
        public void TamperedContentIsRejectedWithoutReturningPartialSnapshot()
        {
            var json = RunSnapshotCodec.Serialize(Example()).Replace("73.5", "72.5");
            Assert.That(RunSnapshotCodec.TryDeserialize(json, out var loaded, out var error), Is.False);
            Assert.That(loaded, Is.Null);
            Assert.That(error, Does.Contain("校验"));
        }

        [Test]
        public void UnknownSchemaIsRejectedExplicitly()
        {
            var json = RunSnapshotCodec.Serialize(Example()).Replace(
                "\"SchemaVersion\":" + RunSnapshot.CurrentSchemaVersion,
                "\"SchemaVersion\":999");
            Assert.That(RunSnapshotCodec.TryDeserialize(json, out _, out var error), Is.False);
            Assert.That(error, Does.Contain("版本"));
        }

        [TestCase("{")]
        [TestCase("{}")]
        [TestCase("null")]
        [TestCase("")]
        public void MalformedOrIncompleteDataIsRejected(string json)
        {
            Assert.That(RunSnapshotCodec.TryDeserialize(json, out var snapshot, out var error), Is.False);
            Assert.That(snapshot, Is.Null);
            Assert.That(error, Is.Not.Empty);
        }

        [Test]
        public void DuplicateIdsAndInconsistentSelectionHistoryAreRejected()
        {
            var snapshot = Example();
            snapshot.Weapons.Add(snapshot.Weapons[0]);
            Assert.That(SnapshotValidation.TryValidate(snapshot, out _), Is.False);
            snapshot.Weapons.RemoveAt(2);
            snapshot.UpgradeSelectionHistory.RemoveAt(0);
            Assert.That(SnapshotValidation.TryValidate(snapshot, out var error), Is.False);
            Assert.That(error, Does.Contain("历史"));
        }

        [Test]
        public void MissingFileIsReportedSeparatelyFromCorruptData()
        {
            var store = new RunSnapshotStore(Path.Combine(directory, "run-snapshot.json"));
            Assert.That(store.Load().Status, Is.EqualTo(SnapshotStatus.MissingFile));
            Directory.CreateDirectory(directory);
            File.WriteAllText(store.FilePath, "broken");
            Assert.That(store.Load().Status, Is.EqualTo(SnapshotStatus.InvalidData));
        }

        [Test]
        public void InvalidSaveCannotOverwritePreviouslySavedSnapshot()
        {
            var store = new RunSnapshotStore(Path.Combine(directory, "run-snapshot.json"));
            Assert.That(store.Save(Example()).Success, Is.True);
            var saved = File.ReadAllText(store.FilePath);
            var invalid = Example();
            invalid.Health = float.NaN;
            Assert.That(store.Save(invalid).Status, Is.EqualTo(SnapshotStatus.InvalidData));
            Assert.That(File.ReadAllText(store.FilePath), Is.EqualTo(saved));
            var loaded = store.Load();
            Assert.That(loaded.Success, Is.True, loaded.Message);
            Assert.That(loaded.Snapshot.Health, Is.EqualTo(73.5f));
        }

        [Test]
        public void PureSnapshotAssemblyDoesNotReferenceUnity()
        {
            foreach (var reference in typeof(RunSnapshot).Assembly.GetReferencedAssemblies())
                Assert.That(reference.Name, Does.Not.StartWith("Unity"));
        }

        private static RunSnapshot Example() => new RunSnapshot
        {
            Seed = 4101, Health = 73.5f, Armor = 12.25f, CurrentWeaponId = "rifle",
            Weapons = new List<WeaponAmmoSnapshot>
            {
                new WeaponAmmoSnapshot { WeaponId = "rifle", Magazine = 21, Reserve = 130 },
                new WeaponAmmoSnapshot { WeaponId = "handgun", Magazine = 8, Reserve = 20 }
            },
            Upgrades = new List<UpgradeLevelSnapshot> { new UpgradeLevelSnapshot { UpgradeId = "vitality", Level = 2 } },
            UpgradeSelectionHistory = new List<string> { "vitality", "vitality" }
        };
    }
}
