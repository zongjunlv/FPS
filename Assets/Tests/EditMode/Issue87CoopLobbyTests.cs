using FPS.Networking.Session;
using NUnit.Framework;

namespace FPS.Tests.Architecture
{
    public sealed class Issue87CoopLobbyTests
    {
        private static readonly string[] Appearances =
        {
            "operative-alpha", "operative-bravo", "operative-charlie"
        };

        [Test]
        public void TwoPlayersSelectConfirmReadyAndHostStarts()
        {
            var room = new CoopLobbyRoster(Appearances);
            Assert.That(room.TryJoin("host", true, Appearances[0], out _),
                Is.True);
            Assert.That(room.TryJoin("guest", false, Appearances[1], out _),
                Is.True);
            Assert.That(room.TrySetReady("host", true, out _), Is.True);
            Assert.That(room.TrySetReady("guest", true, out _), Is.True);

            Assert.That(room.CanStart("host", out _), Is.True);
            Assert.That(room.TryStart("host", out _), Is.True);
            Assert.That(room.IsStarting, Is.True);
        }

        [Test]
        public void GuestCannotStartEvenWhenEveryoneIsReady()
        {
            var room = ReadyRoom();

            Assert.That(room.TryStart("guest", out CoopLobbyFailure failure),
                Is.False);
            Assert.That(failure, Is.EqualTo(CoopLobbyFailure.HostOnly));
        }

        [Test]
        public void RoomRequiresExactlyTwoConnectedReadyPlayers()
        {
            var room = new CoopLobbyRoster(Appearances);
            room.TryJoin("host", true, Appearances[0], out _);
            room.TrySetReady("host", true, out _);

            Assert.That(room.CanStart("host", out CoopLobbyFailure solo),
                Is.False);
            Assert.That(solo,
                Is.EqualTo(CoopLobbyFailure.WaitingForSecondPlayer));

            room.TryJoin("guest", false, Appearances[1], out _);
            Assert.That(room.CanStart("host", out CoopLobbyFailure unready),
                Is.False);
            Assert.That(unready, Is.EqualTo(CoopLobbyFailure.PlayersNotReady));
        }

        [Test]
        public void AppearanceChangeCancelsReadyState()
        {
            var room = ReadyRoom();

            room.TrySelectAppearance("guest", Appearances[2], out _);

            Assert.That(room.TryGet("guest", out var guest), Is.True);
            Assert.That(guest.AppearanceId, Is.EqualTo(Appearances[2]));
            Assert.That(guest.IsReady, Is.False);
            Assert.That(room.CanStart("host", out CoopLobbyFailure failure),
                Is.False);
            Assert.That(failure, Is.EqualTo(CoopLobbyFailure.PlayersNotReady));
        }

        [Test]
        public void DuplicateAccountInvalidAppearanceAndFullRoomAreRejected()
        {
            var room = new CoopLobbyRoster(Appearances);
            room.TryJoin("host", true, Appearances[0], out _);
            Assert.That(room.TryJoin("host", false, Appearances[1],
                out CoopLobbyFailure duplicate), Is.False);
            Assert.That(duplicate, Is.EqualTo(CoopLobbyFailure.DuplicateAccount));
            Assert.That(room.TryJoin("invalid", false, "unknown-role",
                out CoopLobbyFailure invalid), Is.False);
            Assert.That(invalid, Is.EqualTo(CoopLobbyFailure.InvalidAppearance));
            room.TryJoin("guest", false, Appearances[1], out _);
            Assert.That(room.TryJoin("third", false, Appearances[2],
                out CoopLobbyFailure full), Is.False);
            Assert.That(full, Is.EqualTo(CoopLobbyFailure.RoomFull));
        }

        [Test]
        public void LeaveAndRejoinRestoreSlotWithoutDuplicatingMember()
        {
            var room = ReadyRoom();
            Assert.That(room.TryLeave("guest", out _), Is.True);
            Assert.That(room.Count, Is.EqualTo(1));
            Assert.That(room.TryJoin("guest", false, Appearances[2], out _),
                Is.True);
            Assert.That(room.Count, Is.EqualTo(2));
            Assert.That(room.TryGet("guest", out var guest), Is.True);
            Assert.That(guest.IsReady, Is.False);
        }

        [Test]
        public void DisconnectedMemberLosesReadyAndBlocksStart()
        {
            var room = ReadyRoom();
            room.TrySetConnection("guest",
                CoopLobbyConnectionState.Disconnected, out _);

            Assert.That(room.TryGet("guest", out var guest), Is.True);
            Assert.That(guest.IsReady, Is.False);
            Assert.That(room.CanStart("host", out CoopLobbyFailure failure),
                Is.False);
            Assert.That(failure, Is.EqualTo(CoopLobbyFailure.PlayersNotReady));
        }

        private static CoopLobbyRoster ReadyRoom()
        {
            var room = new CoopLobbyRoster(Appearances);
            room.TryJoin("host", true, Appearances[0], out _);
            room.TryJoin("guest", false, Appearances[1], out _);
            room.TrySetReady("host", true, out _);
            room.TrySetReady("guest", true, out _);
            return room;
        }
    }
}
