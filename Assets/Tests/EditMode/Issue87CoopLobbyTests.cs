using System.Linq;
using FPS.Networking.Session;
using NUnit.Framework;
using Unity.Services.Multiplayer;

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
        public void SoloHostCanStartButJoinedGuestMustBeReady()
        {
            var room = new CoopLobbyRoster(Appearances);
            room.TryJoin("host", true, Appearances[0], out _);
            room.TrySetReady("host", true, out _);

            Assert.That(room.CanStart("host", out CoopLobbyFailure solo),
                Is.True);
            Assert.That(solo, Is.EqualTo(CoopLobbyFailure.None));

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

        [Test]
        public void PublicRoomBrowserOnlyQueriesJoinableCityNewLobbies()
        {
            QuerySessionsOptions options =
                CoopSessionController.CreatePublicRoomQueryOptions();

            Assert.That(options.Count, Is.EqualTo(20));
            Assert.That(options.FilterOptions.Any(value =>
                value.Field == FilterField.AvailableSlots &&
                value.Operation == FilterOperation.Greater &&
                value.Value == "0"), Is.True);
            Assert.That(options.FilterOptions.Any(value =>
                value.Field == FilterField.IsLocked &&
                value.Operation == FilterOperation.Equal &&
                value.Value == "false"), Is.True);
            Assert.That(options.FilterOptions.Any(value =>
                value.Field == FilterField.StringIndex1 &&
                value.Value == CoopSessionController.DefaultMapId), Is.True);
            Assert.That(options.FilterOptions.Any(value =>
                value.Field == FilterField.StringIndex2 &&
                value.Value == CoopSessionController.PhaseLobby), Is.True);
            Assert.That(options.SortOptions.Single().Field,
                Is.EqualTo(SortField.LastUpdated));
            Assert.That(options.SortOptions.Single().Order,
                Is.EqualTo(SortOrder.Descending));
        }

        [Test]
        public void ReconnectGateIgnoresDeferredLobbyButBlocksGameplayRestore()
        {
            Assert.That(CoopReconnectPresentationGate.ShouldBlockForState(
                CoopSessionState.Connected,
                true,
                CoopSessionController.PhaseLobby,
                true,
                true,
                false,
                false), Is.False,
                "大厅会延迟生成角色，零副本不能被当成重连。");
            Assert.That(CoopReconnectPresentationGate.ShouldBlockForState(
                CoopSessionState.Connected,
                true,
                CoopSessionController.PhaseLoading,
                true,
                true,
                false,
                false), Is.True,
                "进入战斗加载后应等待本地权威副本。");
            Assert.That(CoopReconnectPresentationGate.ShouldBlockForState(
                CoopSessionState.Connected,
                true,
                CoopSessionController.PhaseLoading,
                false,
                false,
                false,
                false), Is.True,
                "场景已加载但数据通道尚未启动时仍应保持遮罩。");
            Assert.That(CoopReconnectPresentationGate.ShouldBlockForState(
                CoopSessionState.Connected,
                true,
                CoopSessionController.PhaseBattle,
                true,
                true,
                true,
                false), Is.True,
                "战斗副本消费首份服务端状态前应继续遮挡。");
            Assert.That(CoopReconnectPresentationGate.ShouldBlockForState(
                CoopSessionState.Connected,
                true,
                CoopSessionController.PhaseBattle,
                true,
                true,
                true,
                true), Is.False,
                "权威状态恢复完成后必须解除遮罩。");
            Assert.That(CoopReconnectPresentationGate.ShouldBlockForState(
                CoopSessionState.Reconnecting,
                true,
                CoopSessionController.PhaseBattle,
                false,
                false,
                false,
                false), Is.True,
                "真正重连期间仍应保持遮罩。");
            Assert.That(CoopReconnectPresentationGate.ShouldBlockForState(
                CoopSessionState.Connected,
                true,
                CoopSessionController.PhaseBattle,
                false,
                false,
                false,
                false), Is.True,
                "大厅已连接但专用服务器尚未握手时，不能暴露本地占位玩法。");
            Assert.That(CoopReconnectPresentationGate.ShouldBlockForState(
                CoopSessionState.Failed,
                true,
                CoopSessionController.PhaseBattle,
                false,
                false,
                false,
                false), Is.True,
                "专用服务器连接失败后必须保持遮罩并引导玩家退出，不能回退单机。");
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
