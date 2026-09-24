using FPS.Networking.Session;
using NUnit.Framework;
using UnityEngine;

namespace FPS.Tests.Networking
{
    public sealed class SelfHostedRoomGatewayTests
    {
        [Test]
        public void RoomJsonPreservesMemberAndAllocatedServerIdentity()
        {
            const string json = "{\"id\":\"room-1\",\"joinCode\":\"ABCD2345\","
                + "\"displayName\":\"测试房间\",\"mapId\":\"CityNew\","
                + "\"hostId\":\"player-1\",\"phase\":\"loading\","
                + "\"seed\":18018,\"loadEpoch\":\"epoch-1\","
                + "\"serverAllocation\":{\"host\":\"game.example.test\","
                + "\"port\":17777,\"matchId\":\"match-1\"},"
                + "\"players\":[{\"accountId\":\"player-1\","
                + "\"appearanceId\":\"operative-alpha\",\"ready\":true,"
                + "\"readyEpoch\":\"epoch-1\",\"connected\":true}],"
                + "\"maximumPlayers\":2,\"revision\":8}";

            SelfHostedRoomSnapshot room =
                JsonUtility.FromJson<SelfHostedRoomSnapshot>(json);

            Assert.That(room.HasPlayer("player-1"), Is.True);
            Assert.That(room.HasPlayer("other"), Is.False);
            Assert.That(room.serverAllocation.matchId, Is.EqualTo("match-1"));
            Assert.That(room.players[0].readyEpoch, Is.EqualTo("epoch-1"));
            Assert.That(room.revision, Is.EqualTo(8));
        }

        [Test]
        public void PublicRoomBrowserHidesStartedAndFullRooms()
        {
            var room = new SelfHostedRoomSnapshot
            {
                id = "room-1",
                displayName = "测试房间",
                mapId = CoopSessionController.DefaultMapId,
                phase = CoopSessionController.PhaseLobby,
                maximumPlayers = 2,
                players = new[] { new SelfHostedRoomPlayer
                {
                    accountId = "player-1"
                } }
            };

            Assert.That(CoopSessionController.TryCreatePublicRoomSnapshot(
                room, out CoopPublicRoomSnapshot available), Is.True);
            Assert.That(available.PlayerCount, Is.EqualTo(1));
            room.phase = CoopSessionController.PhaseLoading;
            Assert.That(CoopSessionController.TryCreatePublicRoomSnapshot(
                room, out _), Is.False);
            room.phase = CoopSessionController.PhaseLobby;
            room.players = new[]
            {
                new SelfHostedRoomPlayer { accountId = "player-1" },
                new SelfHostedRoomPlayer { accountId = "player-2" }
            };
            Assert.That(CoopSessionController.TryCreatePublicRoomSnapshot(
                room, out _), Is.False);
        }

        [Test]
        public void PublicRoomSummaryUsesCountWithoutPrivateRoster()
        {
            const string json = "{\"id\":\"room-public\","
                + "\"displayName\":\"公开小队\",\"mapId\":\"CityNew\","
                + "\"phase\":\"lobby\",\"playerCount\":1,"
                + "\"maximumPlayers\":2}";
            SelfHostedRoomSnapshot room =
                JsonUtility.FromJson<SelfHostedRoomSnapshot>(json);

            Assert.That(room.players, Is.Null);
            Assert.That(CoopSessionController.TryCreatePublicRoomSnapshot(
                room, out CoopPublicRoomSnapshot snapshot), Is.True);
            Assert.That(snapshot.PlayerCount, Is.EqualTo(1));
        }

        [Test]
        public void CurrentRoomNoContentAllowsNewRoomFlow()
        {
            Assert.That(SelfHostedRoomGateway.ParseOptionalRoomResponse(
                string.Empty), Is.Null);
            Assert.That(SelfHostedRoomGateway.ParseOptionalRoomResponse(
                "  "), Is.Null);
            const string room = "{\"id\":\"room-restored\","
                + "\"hostId\":\"player-1\",\"phase\":\"battle\","
                + "\"loadEpoch\":\"load-1\"}";
            Assert.That(SelfHostedRoomGateway.ParseOptionalRoomResponse(
                room).loadEpoch, Is.EqualTo("load-1"));
        }
    }
}
