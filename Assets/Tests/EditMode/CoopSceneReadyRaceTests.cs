using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using FPS.Networking.Netcode;
using FPS.Networking.Session;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FPS.Tests.Architecture
{
    /// <summary>
    /// Focused lifetime guards supplement the real, offline HTTPS call-pattern
    /// acceptance in scripts/editor/CoopSceneReadyRaceAcceptance.cs. Reflection
    /// is fixture setup only; assertions exercise ReportSceneReadyAsync.
    /// </summary>
    public sealed class CoopSceneReadyRaceTests
    {
        private const string LocalPlayer = "scene-ready-test-player";
        private const string RoomId = "scene-ready-test-room";
        private const string Epoch = "scene-ready-test-epoch";
        private const BindingFlags InstancePrivate = BindingFlags.Instance |
            BindingFlags.NonPublic;
        private const BindingFlags StaticPrivate = BindingFlags.Static |
            BindingFlags.NonPublic;

        private GameObject sessionObject;
        private CoopSessionController session;
        private SelfHostedRoomSnapshot initialRoom;
        private string previousAccount;
        private float previousTimeScale;
        private CursorLockMode previousCursorLock;
        private bool previousCursorVisible;
        private bool previousPauseVisible;
        private bool previousEconomyVisible;
        private readonly List<Task<bool>> operations = new();
        private readonly List<TaskCompletionSource<SelfHostedRoomSnapshot>> responses = new();
        private readonly List<(string Room, string Epoch)> reports = new();

        [SetUp]
        public void SetUp()
        {
            previousAccount = SelfHostedAuthenticationGateway.AccountId;
            previousTimeScale = Time.timeScale;
            previousCursorLock = Cursor.lockState;
            previousCursorVisible = Cursor.visible;
            previousPauseVisible = CoopUiInputGate.PauseMenuVisible;
            previousEconomyVisible = CoopUiInputGate.EconomyModalVisible;
            typeof(SelfHostedAuthenticationGateway).GetField("currentAccountId",
                    StaticPrivate)!.SetValue(null, LocalPlayer);
            sessionObject = new GameObject("Scene Ready Lifetime Test");
            sessionObject.SetActive(false);
            session = sessionObject.AddComponent<CoopSessionController>();
            initialRoom = CreateRoom();
            Set("activeRoom", initialRoom);
            Set("<State>k__BackingField", CoopSessionState.Connected);
            session.ConfigureSceneReadyRequestForTests((room, epoch) =>
            {
                reports.Add((room, epoch));
                var response = new TaskCompletionSource<SelfHostedRoomSnapshot>();
                responses.Add(response);
                return response.Task;
            });
        }

        [TearDown]
        public async Task TearDown()
        {
            foreach (TaskCompletionSource<SelfHostedRoomSnapshot> response in responses)
                response.TrySetCanceled();
            try
            {
                await Task.WhenAll(operations);
            }
            finally
            {
                operations.Clear();
                reports.Clear();
                responses.Clear();
                if (sessionObject != null) Object.DestroyImmediate(sessionObject);
                typeof(SelfHostedAuthenticationGateway).GetField("currentAccountId",
                        StaticPrivate)!.SetValue(null, previousAccount);
                Time.timeScale = previousTimeScale;
                Cursor.lockState = previousCursorLock;
                Cursor.visible = previousCursorVisible;
                CoopUiInputGate.PauseMenuVisible = previousPauseVisible;
                CoopUiInputGate.EconomyModalVisible = previousEconomyVisible;
            }
        }

        [Test]
        public async Task SceneReadyCanBeSentWhileAllocationRoomRefreshIsBusy()
        {
            // The integration script reaches this state via a real held GET,
            // not by setting the flag. Here isolate the lifecycle entry guard.
            Set("lobbyOperationInProgress", true);
            Task<bool> reporting = Track(session.ReportSceneReadyAsync(Epoch));

            Assert.That(reports, Is.EqualTo(new[] { (RoomId, Epoch) }),
                "场景就绪是战局生命周期回报，不得被普通房间操作忙碌门控吞掉。");
            Assert.That(reporting.IsCompleted, Is.False);
            responses[0].SetResult(CreateRoom(readyEpoch: Epoch, revision: 2));
            Assert.That(await reporting, Is.True);
            Assert.That(session.ActiveSession.players.Single(player =>
                player.accountId == LocalPlayer).readyEpoch, Is.EqualTo(Epoch));
            Assert.That(session.IsLobbyBusy, Is.True,
                "就绪回报不得提前释放仍在运行的分配请求锁。");
        }

        [Test]
        public async Task OrdinaryLobbyMutationStillHonorsBusyGuard()
        {
            Set("lobbyOperationInProgress", true);

            Assert.That(await session.SetLobbyReadyAsync(false), Is.False);
            Assert.That(reports, Is.Empty);
            Assert.That(session.ActiveSession, Is.SameAs(initialRoom));
            Assert.That(session.IsLobbyBusy, Is.True);
        }

        [TestCase("leave")]
        [TestCase("mode-exit")]
        public async Task ExitClearsInvalidatedLobbyBusyBeforeLateReadyResponse(string exitKind)
        {
            // A held allocation/room refresh owns this busy flag. Its captured
            // generation is invalidated by exit, so its eventual continuation
            // must not be responsible for clearing a new generation's lock.
            Set("lobbyOperationInProgress", true);
            session.SetSceneReadyRetryState(RoomId, Epoch, true, "old-ready-error");
            Task<bool> pendingReport = Track(session.ReportSceneReadyAsync(Epoch));
            var leaveCompletion = new TaskCompletionSource<bool>();
            var leftRooms = new List<string>();
            session.ConfigureRoomGatewayForTests(() => Task.FromResult(initialRoom), room =>
            {
                leftRooms.Add(room);
                return leaveCompletion.Task;
            });
            Task exiting = exitKind == "leave"
                ? session.LeaveAsync()
                : session.ShutdownForModeExitAsync();
            try
            {
                Assert.That(leftRooms, Is.EqualTo(new[] { RoomId }));
                Assert.That(exiting.IsCompleted, Is.False);
                Assert.That(session.HasActiveSession, Is.False);
                leaveCompletion.SetResult(true);
                await exiting;

                Assert.That(session.State, Is.EqualTo(CoopSessionState.Offline));
                Assert.That(session.IsLobbyBusy, Is.False,
                    "退出时旧generation不会再清busy，ResetLobbyState必须主动释放旧锁。");
                Assert.That(session.IsSceneReadyRetrying, Is.False);
                Assert.That(session.SceneLoadFailure, Is.Empty);
                Assert.That(pendingReport.IsCompleted, Is.False,
                    "解除旧房间busy不能依赖尚未返回的旧HTTP请求。");

                responses[0].SetResult(CreateRoom(readyEpoch: Epoch, revision: 2));
                Assert.That(await pendingReport, Is.False);
                Assert.That(session.HasActiveSession, Is.False);
                Assert.That(session.IsLobbyBusy, Is.False);
            }
            finally
            {
                leaveCompletion.TrySetResult(true);
                await exiting;
            }
        }

        [Test]
        public void NewLoadEpochClearsPreviousReadyRetryAndError()
        {
            session.SetSceneReadyRetryState(RoomId, Epoch, true, "old-ready-error");
            var newRound = CreateRoom(epoch: "new-epoch", revision: 2);

            typeof(CoopSessionController).GetMethod("ApplyRoom", InstancePrivate)!
                .Invoke(session, new object[] { newRound, false });

            Assert.That(session.ActiveSession, Is.SameAs(newRound));
            Assert.That(session.IsSceneReadyRetrying, Is.False);
            Assert.That(session.SceneLoadFailure, Is.Empty);
            session.SetSceneReadyRetryState(RoomId, Epoch, true, "late-old-ready-error");
            Assert.That(session.IsSceneReadyRetrying, Is.False);
            Assert.That(session.SceneLoadFailure, Is.Empty);
        }

        [TestCase("old-room", Epoch)]
        [TestCase(RoomId, "old-epoch")]
        public void ObsoleteRetryUiUpdateCannotOverwriteCurrentRound(string room, string epoch)
        {
            session.SetSceneReadyRetryState(RoomId, Epoch, true, "current-ready-error");

            session.SetSceneReadyRetryState(room, epoch, false, "obsolete-ready-error");

            Assert.That(session.IsSceneReadyRetrying, Is.True);
            Assert.That(session.SceneLoadFailure, Is.EqualTo("current-ready-error"));
        }

        [Test]
        public async Task WrongEpochNeverSendsReadyForCurrentRound()
        {
            Assert.That(await Track(session.ReportSceneReadyAsync("old-epoch")), Is.False);
            Assert.That(reports, Is.Empty);
            Assert.That(session.ActiveSession, Is.SameAs(initialRoom));
        }

        [Test]
        public async Task SuccessfulHttpResponseWithoutLocalReadyEpochIsNotAcknowledgement()
        {
            Task<bool> reporting = Track(session.ReportSceneReadyAsync(Epoch));
            responses[0].SetResult(CreateRoom(revision: 2));

            Assert.That(await reporting, Is.False);
            Assert.That(session.ActiveSession.players.Single(player =>
                player.accountId == LocalPlayer).readyEpoch, Is.Empty);
            Assert.That(session.SceneLoadFailure, Is.Not.Empty,
                "HTTP成功不能替代服务器确认当前玩家已完成这一轮场景加载。");
        }

        [Test]
        public async Task AcknowledgedReadyClearsPreviousTransientFailureAndRetryState()
        {
            session.SetSceneReadyRetryState(RoomId, Epoch, true, "temporary-ready-error");
            Task<bool> reporting = Track(session.ReportSceneReadyAsync(Epoch));
            responses[0].SetResult(CreateRoom(readyEpoch: Epoch, revision: 2));

            Assert.That(await reporting, Is.True);
            Assert.That(session.SceneLoadFailure, Is.Empty);
            Assert.That(session.IsSceneReadyRetrying, Is.False);
        }

        [TestCase("epoch")]
        [TestCase("generation")]
        [TestCase("room")]
        [TestCase("cancellation-in-progress")]
        [TestCase("cancelled-phase")]
        [TestCase("mode-exit")]
        [TestCase("offline")]
        [TestCase("failed")]
        public async Task ObsoleteReadyResponseCannotReplaceCurrentRoom(string invalidation)
        {
            Task<bool> reporting = Track(session.ReportSceneReadyAsync(Epoch));
            Assert.That(reports, Has.Count.EqualTo(1));
            SelfHostedRoomSnapshot currentRoom = initialRoom;
            switch (invalidation)
            {
                case "epoch":
                    currentRoom = CreateRoom(epoch: "new-epoch", revision: 4);
                    Set("activeRoom", currentRoom);
                    break;
                case "generation":
                    Set("operationGeneration", 1);
                    break;
                case "room":
                    currentRoom = CreateRoom(id: "replacement-room", revision: 4);
                    Set("activeRoom", currentRoom);
                    break;
                case "cancellation-in-progress":
                    Set("sceneCancellationInProgress", true);
                    break;
                case "cancelled-phase":
                    currentRoom = CreateRoom(phase: CoopSessionController.PhaseCancelled,
                        revision: 4);
                    Set("activeRoom", currentRoom);
                    break;
                case "mode-exit":
                    Set("modeExitRequested", true);
                    break;
                case "offline":
                    Set("<State>k__BackingField", CoopSessionState.Offline);
                    break;
                case "failed":
                    Set("<State>k__BackingField", CoopSessionState.Failed);
                    break;
            }

            responses[0].SetResult(CreateRoom(readyEpoch: Epoch, revision: 5));

            Assert.That(await reporting, Is.False);
            Assert.That(session.ActiveSession, Is.SameAs(currentRoom),
                "迟到的场景就绪回包不得将取消状态或新战局替换成旧加载状态。");
            Assert.That(currentRoom.players.Single(player =>
                player.accountId == LocalPlayer).readyEpoch, Is.Empty);
        }

        [TestCase("new-epoch")]
        [TestCase("cancellation-in-progress")]
        public async Task ObsoleteReadyExceptionCannotOverwriteCurrentLoadFailure(string invalidation)
        {
            Task<bool> reporting = Track(session.ReportSceneReadyAsync(Epoch));
            SelfHostedRoomSnapshot currentRoom = initialRoom;
            if (invalidation == "new-epoch")
            {
                currentRoom = CreateRoom(epoch: "new-epoch", revision: 4);
                Set("activeRoom", currentRoom);
            }
            else Set("sceneCancellationInProgress", true);
            Set("sceneLoadFailure", "current-round-failure");
            responses[0].SetException(new InvalidOperationException("old-request-failure"));

            Assert.That(await reporting, Is.False);
            Assert.That(session.SceneLoadFailure, Is.EqualTo("current-round-failure"));
            Assert.That(session.ActiveSession, Is.SameAs(currentRoom));
        }

        [TestCase("throw")]
        [TestCase("canceled")]
        public async Task CoordinatorRetriesTransientReadyFailureThenStopsAfterAcknowledgement(
            string firstFailure)
        {
            CoopSceneLoadCoordinator coordinator = CreateCoordinator();
            session.ConfigureSceneReadyRequestForTests((room, epoch) =>
            {
                reports.Add((room, epoch));
                if (reports.Count != 1)
                    return Task.FromResult(CreateRoom(readyEpoch: Epoch, revision: 2));
                return firstFailure == "throw"
                    ? Task.FromException<SelfHostedRoomSnapshot>(
                        new InvalidOperationException("temporary-ready-request-failure"))
                    : Task.FromCanceled<SelfHostedRoomSnapshot>(new CancellationToken(true));
            });
            IEnumerator retry = ReadyRetry(coordinator);
            bool retryVisible = false;

            await Drive(retry, () => retryVisible |= session.IsSceneReadyRetrying);

            Assert.That(reports, Is.EqualTo(new[] { (RoomId, Epoch), (RoomId, Epoch) }));
            Assert.That(retryVisible, Is.True, "重试期间应公开真实状态，而非只有等待队友的黑幕。");
            Assert.That(coordinator.IsRetryingSceneReady, Is.False);
            Assert.That(session.IsSceneReadyRetrying, Is.False);
            Assert.That(session.SceneLoadFailure, Is.Empty);
            Assert.That(session.ActiveSession.players.Single(player =>
                player.accountId == LocalPlayer).readyEpoch, Is.EqualTo(Epoch));
        }

        [Test]
        public void CoordinatorDoesNotResendOldReadyAfterEpochChangesDuringBackoff()
        {
            CoopSceneLoadCoordinator coordinator = CreateCoordinator();
            session.ConfigureSceneReadyRequestForTests((room, epoch) =>
            {
                reports.Add((room, epoch));
                return Task.FromException<SelfHostedRoomSnapshot>(
                    new InvalidOperationException("temporary-ready-request-failure"));
            });
            IEnumerator retry = ReadyRetry(coordinator);
            Assert.That(retry.MoveNext(), Is.True);
            Assert.That(reports, Has.Count.EqualTo(1));
            var newRound = CreateRoom(epoch: "new-epoch", revision: 4);
            Set("activeRoom", newRound);

            Assert.That(retry.MoveNext(), Is.False);
            Assert.That(reports, Has.Count.EqualTo(1));
            Assert.That(session.ActiveSession, Is.SameAs(newRound));
        }

        [Test]
        public void CancelledCoordinatorPendingReportCannotAcknowledgeNewRound()
        {
            CoopSceneLoadCoordinator coordinator = CreateCoordinator();
            IEnumerator retry = ReadyRetry(coordinator);
            Assert.That(retry.MoveNext(), Is.True);
            Assert.That(reports, Has.Count.EqualTo(1));
            Set("sceneCancellationInProgress", true);
            var newRound = CreateRoom(epoch: "new-epoch", revision: 4);
            Set("activeRoom", newRound);
            typeof(CoopSceneLoadCoordinator).GetMethod("CancelPendingLoad", InstancePrivate)!
                .Invoke(coordinator, null);
            responses[0].SetResult(CreateRoom(readyEpoch: Epoch, revision: 5));

            Assert.That(retry.MoveNext(), Is.False);
            Assert.That(reports, Has.Count.EqualTo(1));
            Assert.That(session.ActiveSession, Is.SameAs(newRound));
            Assert.That(newRound.players.Single(player =>
                player.accountId == LocalPlayer).readyEpoch, Is.Empty);
        }

        private CoopSceneLoadCoordinator CreateCoordinator()
        {
            sessionObject.SetActive(true);
            CoopSceneLoadCoordinator coordinator = sessionObject.AddComponent<CoopSceneLoadCoordinator>();
            SetCoordinator(coordinator, "session", session);
            SetCoordinator(coordinator, "loadingRoomId", RoomId);
            SetCoordinator(coordinator, "loadingEpoch", Epoch);
            // Manually drive only the ready helper: do not schedule scene loads,
            // a live network manager, or a foreground Editor coroutine.
            foreach (string eventName in new[] { "LobbyStartRequested", "BattleSceneReady",
                         "SceneLoadCancelled", "ReturnedToLobby", "StateChanged" })
                typeof(CoopSessionController).GetField(eventName, InstancePrivate)!
                    .SetValue(session, null);
            return coordinator;
        }

        private static IEnumerator ReadyRetry(CoopSceneLoadCoordinator coordinator) =>
            (IEnumerator)typeof(CoopSceneLoadCoordinator).GetMethod(
                "ReportReadyUntilAcknowledged", InstancePrivate)!
                .Invoke(coordinator, new object[] { RoomId, Epoch, 0 });

        private static void SetCoordinator(CoopSceneLoadCoordinator coordinator,
            string name, object value) => typeof(CoopSceneLoadCoordinator)
            .GetField(name, InstancePrivate)!.SetValue(coordinator, value);

        private static async Task Drive(IEnumerator routine, Action observe)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(5);
            while (routine.MoveNext())
            {
                observe();
                if (DateTime.UtcNow >= deadline)
                    Assert.Fail("场景就绪确认重试未在本地测试期限内完成。");
                await Task.Delay(5);
            }
        }

        private Task<bool> Track(Task<bool> operation)
        {
            operations.Add(operation);
            return operation;
        }

        private void Set(string name, object value) =>
            typeof(CoopSessionController).GetField(name, InstancePrivate)!
                .SetValue(session, value);

        private static SelfHostedRoomSnapshot CreateRoom(string id = RoomId,
            string epoch = Epoch, string phase = CoopSessionController.PhaseLoading,
            string readyEpoch = "", long revision = 1) => new()
        {
            id = id,
            hostId = "scene-ready-test-host",
            phase = phase,
            mapId = CoopSessionController.DefaultMapId,
            seed = 18018,
            loadEpoch = epoch,
            revision = revision,
            players = new[]
            {
                new SelfHostedRoomPlayer
                {
                    accountId = LocalPlayer, appearanceId = "operative-alpha",
                    connected = true, ready = true, readyEpoch = readyEpoch
                },
                new SelfHostedRoomPlayer
                {
                    accountId = "scene-ready-test-host", appearanceId = "operative-alpha",
                    connected = true, ready = true
                }
            }
        };
    }
}
