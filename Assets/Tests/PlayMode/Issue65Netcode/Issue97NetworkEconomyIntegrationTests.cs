using System.Collections;
using System.Collections.Generic;
using System.Linq;
using FPS.Networking.Domain;
using FPS.Networking.Netcode;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FPS.Tests.PlayMode.Issue65
{
    public sealed class Issue97NetworkEconomyIntegrationTests
    {
        private readonly List<GameObject> created = new();

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            for (int index = created.Count - 1; index >= 0; index--)
                if (created[index] != null) Object.Destroy(created[index]);
            created.Clear();
            yield return null;
            yield return null;
        }

        [Test]
        public void TwoAuthenticatedClientsRaceAndOnlyOneReceivesDrop()
        {
            NetworkCoopSessionAuthority authority = Authority(twoPlayers: true);
            authority.RegisterPlayerClient(1001, 1);
            authority.RegisterPlayerClient(1002, 2);
            int drop = authority.SpawnServerWorldDrop(
                "medical_kit", 2, Vector3.zero);

            Assert.That(authority.TryQueueEconomyCommand(1002,
                Payload(2, 1, 201, AuthoritativeEconomyCommandKind.Pickup,
                    entityId: drop, expectedDropRevision: 1)), Is.True);
            Assert.That(authority.TryQueueEconomyCommand(1001,
                Payload(1, 1, 101, AuthoritativeEconomyCommandKind.Pickup,
                    entityId: drop, expectedDropRevision: 1)), Is.True);
            AuthoritativeTickResult result = authority.ServerStep();

            Assert.That(result.EconomyCommands.Count(value => value.Accepted),
                Is.EqualTo(1));
            Assert.That(result.EconomyCommands.Single(value => value.Accepted)
                .Command.PlayerId, Is.EqualTo(1));
            Assert.That(authority.GetReplicatedWorldDrop(0).Available,
                Is.False);
            Assert.That(authority.ReplicatedInventorySlotCount,
                Is.EqualTo(24));
        }

        [Test]
        public void ConsumableUseChangesServerHealthAndReplicatedInventory()
        {
            NetworkCoopSessionAuthority authority = Authority();
            authority.RegisterPlayerClient(1001, 1);
            int drop = authority.SpawnServerWorldDrop(
                "medical_kit", 2, Vector3.zero);
            authority.TryQueueEconomyCommand(1001,
                Payload(1, 1, 101, AuthoritativeEconomyCommandKind.Pickup,
                    entityId: drop));
            authority.ServerStep();
            authority.ApplyServerDamageToPlayer(1, 60d);

            authority.TryQueueEconomyCommand(1001,
                Payload(1, 2, 102, AuthoritativeEconomyCommandKind.Use,
                    sourceSlot: 0, expectedInventoryRevision: 2));
            authority.ServerStep();

            Assert.That(authority.LastAuthoritativeSnapshot.Player(1).Health,
                Is.EqualTo(75d));
            NetcodeInventorySlotState slot =
                authority.GetReplicatedInventorySlot(0);
            Assert.That(slot.ItemId.ToString(), Is.EqualTo("medical_kit"));
            Assert.That(slot.Quantity, Is.EqualTo(1));
            Assert.That(authority.TryGetPlayerState(1,
                out NetcodePlayerState player), Is.True);
            Assert.That(player.Health, Is.EqualTo(75f));
        }

        [Test]
        public void LateSnapshotContainsExperienceCandidatesAndAppliedBuild()
        {
            NetworkCoopSessionAuthority authority = Authority();
            authority.RegisterPlayerClient(1001, 1);
            authority.ApplyServerDamageToPlayer(1, 20d);
            authority.GrantServerExperience(1, 100);
            Assert.That(authority.TryGetProgression(1,
                out NetcodeProgressionState offer), Is.True);
            Assert.That(offer.Level, Is.EqualTo(2));
            Assert.That(offer.PendingUpgradeChoices, Is.EqualTo(1));
            Assert.That(offer.Candidate0.IsEmpty, Is.False);

            authority.TryQueueEconomyCommand(1001,
                Payload(1, 1, 101,
                    AuthoritativeEconomyCommandKind.SelectUpgrade,
                    candidateIndex: 0,
                    choiceGeneration: offer.ChoiceGeneration));
            authority.ServerStep();

            Assert.That(authority.TryGetProgression(1,
                out NetcodeProgressionState rebuilt), Is.True);
            Assert.That(rebuilt.PendingUpgradeChoices, Is.Zero);
            Assert.That(authority.ReplicatedUpgradeCount, Is.EqualTo(1));
            Assert.That(authority.GetReplicatedUpgrade(0).Level,
                Is.EqualTo(1));
            Assert.That(rebuilt.BuildTags.IsEmpty, Is.False);
        }

        private NetworkCoopSessionAuthority Authority(bool twoPlayers = false)
        {
            GameObject host = Track(new GameObject("Issue97 Authority"));
            NetworkCoopSessionAuthority authority = host.AddComponent<
                NetworkCoopSessionAuthority>();
            authority.EnableServerTestHook();
            CoopPlayerSpawn[] players = twoPlayers
                ? new[]
                {
                    new CoopPlayerSpawn(1, default),
                    new CoopPlayerSpawn(2, default)
                }
                : new[] { new CoopPlayerSpawn(1, default) };
            authority.ConfigureServer(
                new CoopServerRules(tickRate: 60),
                players,
                new[]
                {
                    new CoopTargetSpawn(1,
                        new NetVector3(0d, 0d, 30d), 0.5d, 100d,
                        moveSpeed: 0d, attackDamage: 0d)
                });
            return authority;
        }

        private GameObject Track(GameObject value)
        {
            created.Add(value);
            return value;
        }

        private static NetcodeEconomyCommand Payload(
            int playerId,
            uint sequence,
            ulong nonce,
            AuthoritativeEconomyCommandKind kind,
            int entityId = 0,
            int sourceSlot = -1,
            int destinationSlot = -1,
            int quantity = 0,
            int candidateIndex = -1,
            int expectedInventoryRevision = 0,
            int expectedDropRevision = 0,
            int choiceGeneration = 0) =>
            NetcodeEconomyCommand.FromDomain(new AuthoritativeEconomyCommand(
                playerId, sequence, nonce, kind, entityId, sourceSlot,
                destinationSlot, quantity, candidateIndex,
                expectedInventoryRevision, expectedDropRevision,
                choiceGeneration));
    }
}
