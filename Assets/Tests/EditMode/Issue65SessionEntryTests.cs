using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using FPS.Networking.Netcode;
using FPS.Networking.Session;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FPS.Tests.Architecture
{
    public sealed class Issue65SessionEntryTests
    {
        [TestCase(" ab12cd ", "AB12CD")]
        [TestCase("XYZ789", "XYZ789")]
        [TestCase("", "")]
        [TestCase(null, "")]
        public void JoinCodeIsNormalizedForPlayerEntry(
            string source,
            string expected)
        {
            Assert.That(
                CoopSessionController.NormalizeJoinCode(source),
                Is.EqualTo(expected));
        }

        [Test]
        public void VerticalSliceRemainsExplicitlyTwoPlayer()
        {
            Assert.That(CoopSessionController.MaximumPlayers, Is.EqualTo(2));
        }
    }

    /// <summary>All gateway responses are local promises; no transport is started.</summary>
    public sealed class Issue65PendingRoomExitTests
    {
        private GameObject sessionObject;
        private GameObject networkObject;
        private CoopSessionController session;
        private DelayedRoomGateway gateway;
        private readonly List<Task> operations = new();
        private string previousToken;
        private string previousAccount;
        private float previousTimeScale;
        private CursorLockMode previousCursorLock;
        private bool previousCursorVisible;
        private bool previousPauseVisible;
        private bool previousEconomyVisible;

        [SetUp]
        public void SetUp()
        {
            previousToken = SelfHostedAuthenticationGateway.AccessToken;
            previousAccount = SelfHostedAuthenticationGateway.AccountId;
            previousTimeScale = Time.timeScale;
            previousCursorLock = Cursor.lockState;
            previousCursorVisible = Cursor.visible;
            previousPauseVisible = CoopUiInputGate.PauseMenuVisible;
            previousEconomyVisible = CoopUiInputGate.EconomyModalVisible;
            SetIdentity("local-test-token", "local-test-player");

            sessionObject = new GameObject("Local Pending Room Exit Test");
            sessionObject.SetActive(false);
            session = sessionObject.AddComponent<CoopSessionController>();
            networkObject = new GameObject("Inactive Room Exit Transport");
            networkObject.SetActive(false);
            OptionalNetworkBootstrap bootstrap =
                networkObject.AddComponent<OptionalNetworkBootstrap>();
            bootstrap.Configure(NetworkEndpointSettings.Localhost);
            typeof(CoopSessionController).GetField("networkBootstrap",
                BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(session, bootstrap);
            gateway = new DelayedRoomGateway();
            session.ConfigureRoomGatewayForTests(gateway.EnterAsync,
                gateway.LeaveAsync);
        }

        [TearDown]
        public async Task TearDown()
        {
            // Release local promises even after an assertion failure so no
            // continuation survives this fixture or touches a later test.
            gateway?.Response.TrySetCanceled();
            gateway?.LeaveCompletion.TrySetResult(true);
            try
            {
                await AwaitLocal(Task.WhenAll(operations));
            }
            finally
            {
                operations.Clear();
                if (sessionObject != null) Object.DestroyImmediate(sessionObject);
                if (networkObject != null) Object.DestroyImmediate(networkObject);
                SetIdentity(previousToken, previousAccount);
                Time.timeScale = previousTimeScale;
                Cursor.lockState = previousCursorLock;
                Cursor.visible = previousCursorVisible;
                CoopUiInputGate.PauseMenuVisible = previousPauseVisible;
                CoopUiInputGate.EconomyModalVisible = previousEconomyVisible;
            }
        }

        [TestCase("host")]
        [TestCase("code")]
        [TestCase("public")]
        public async Task ModeExitWaitsForLateRoomResponseAndItsSingleLeave(string entry)
        {
            Task<bool> entering = StartEntry(entry);
            Assert.That(gateway.EntryCalls, Is.EqualTo(1));
            Assert.That(entering.IsCompleted, Is.False);
            Task exiting = Track(session.ShutdownForModeExitAsync());

            Assert.That(exiting.IsCompleted, Is.False,
                "尚未返回房间 ID 的创建/加入请求也是退出清理的一部分。");
            Assert.That(session.HasActiveSession, Is.False);
            Assert.That(await session.HostAsync(), Is.False,
                "退出期间不得放宽并发创建/加入保护。");
            Assert.That(gateway.EntryCalls, Is.EqualTo(1));

            gateway.Response.SetResult(new SelfHostedRoomSnapshot
            {
                id = "late-local-room",
                hostId = "local-test-player"
            });
            await AwaitLocal(gateway.LeaveRequested.Task);
            Assert.That(gateway.LeftRoomIds, Is.EqualTo(new[] { "late-local-room" }));
            Assert.That(exiting.IsCompleted, Is.False,
                "收到迟到的房间后仍须等离房响应，不能只等待创建/加入响应。");
            Assert.That(entering.IsCompleted, Is.False);

            gateway.LeaveCompletion.SetResult(true);
            await AwaitLocal(Task.WhenAll(entering, exiting));
            Assert.That(entering.Result, Is.False);
            Assert.That(session.State, Is.EqualTo(CoopSessionState.Offline));
            Assert.That(session.HasActiveSession, Is.False);
            Assert.That(gateway.LeftRoomIds, Has.Count.EqualTo(1),
                "迟到响应只由原操作离房，退出流程不能再发送一次离房。");
        }

        [TestCase("host")]
        [TestCase("code")]
        [TestCase("public")]
        public async Task RejectedPendingEntryAlsoReleasesModeExit(string entry)
        {
            Task<bool> entering = StartEntry(entry);
            Task exiting = Track(session.ShutdownForModeExitAsync());
            Assert.That(exiting.IsCompleted, Is.False);

            gateway.Response.SetException(new InvalidOperationException(
                "Local simulated room rejection"));
            await AwaitLocal(Task.WhenAll(entering, exiting));

            Assert.That(entering.Result, Is.False);
            Assert.That(exiting.IsCompletedSuccessfully, Is.True);
            Assert.That(session.State, Is.EqualTo(CoopSessionState.Offline));
            Assert.That(gateway.LeftRoomIds, Is.Empty,
                "服务端未返回房间时不能凭空构造离房请求。");
        }

        [TestCase("host")]
        [TestCase("code")]
        [TestCase("public")]
        public async Task RequestSubstitutesDoNotBypassAuthentication(string entry)
        {
            SetIdentity(string.Empty, string.Empty);
            Task<bool> entering = StartEntry(entry);
            await AwaitLocal(entering);
            Assert.That(entering.Result, Is.False);
            Assert.That(session.LastFailure, Does.Contain("请先"));
            Assert.That(gateway.EntryCalls, Is.Zero);
            await AwaitLocal(Track(session.ShutdownForModeExitAsync()));
            Assert.That(session.State, Is.EqualTo(CoopSessionState.Offline));
        }

        private Task<bool> StartEntry(string kind)
        {
            Task<bool> operation = kind switch
            {
                "host" => session.HostAsync(roomName: "Local fake room"),
                "code" => session.JoinAsync("ABCD2345"),
                "public" => session.JoinPublicRoomAsync("local-public-room"),
                _ => throw new ArgumentOutOfRangeException(nameof(kind))
            };
            operations.Add(operation);
            return operation;
        }

        private Task Track(Task operation)
        {
            operations.Add(operation);
            return operation;
        }

        private static async Task AwaitLocal(Task operation)
        {
            Task completed = await Task.WhenAny(operation, Task.Delay(3000));
            Assert.That(completed, Is.SameAs(operation),
                "本地已释放的请求不能让退出任务永远挂起。");
            await operation;
        }

        private static void SetIdentity(string token, string account)
        {
            const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
            typeof(SelfHostedAuthenticationGateway).GetField("currentAccessToken", flags)!
                .SetValue(null, token);
            typeof(SelfHostedAuthenticationGateway).GetField("currentAccountId", flags)!
                .SetValue(null, account);
        }

        private sealed class DelayedRoomGateway
        {
            public readonly TaskCompletionSource<SelfHostedRoomSnapshot> Response = new();
            public readonly TaskCompletionSource<bool> LeaveRequested = new();
            public readonly TaskCompletionSource<bool> LeaveCompletion = new();
            public readonly List<string> LeftRoomIds = new();
            public int EntryCalls { get; private set; }

            public Task<SelfHostedRoomSnapshot> EnterAsync()
            {
                EntryCalls++;
                return Response.Task;
            }

            public Task LeaveAsync(string roomId)
            {
                LeftRoomIds.Add(roomId);
                LeaveRequested.TrySetResult(true);
                return LeaveCompletion.Task;
            }
        }
    }
}
