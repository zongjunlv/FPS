using System;
using System.Collections;
using System.Reflection;
using System.Threading.Tasks;
using FPS.Networking.Netcode;
using FPS.Networking.Session;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace FPS.Tests.PlayMode.Issue65
{
    public sealed class Issue106SessionTransportLifecycleTests
    {
        private GameObject sessionObject;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            foreach (NetworkManager manager in Object.FindObjectsByType<NetworkManager>(
                         FindObjectsInactive.Include))
            {
                if (manager.IsListening && !manager.ShutdownInProgress)
                    manager.Shutdown(discardMessageQueue: true);
                Object.Destroy(manager.gameObject);
            }
            yield return null;
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (sessionObject != null) Object.Destroy(sessionObject);
            foreach (OptionalNetworkBootstrap bootstrap in
                     Object.FindObjectsByType<OptionalNetworkBootstrap>(
                         FindObjectsInactive.Include))
            {
                bootstrap.ResetForTests();
                Object.Destroy(bootstrap.gameObject);
            }
            CoopUiInputGate.PauseMenuVisible = false;
            CoopUiInputGate.EconomyModalVisible = false;
            Time.timeScale = 1f;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            yield return null;
            yield return null;
        }

        [UnityTest]
        public IEnumerator TenImmediateReentriesWaitForPreviousTransportShutdown()
        {
            sessionObject = new GameObject("Issue106 Reentry Session");
            CoopSessionController session = sessionObject.AddComponent<CoopSessionController>();
            NetworkEndpointSettings endpoint = NetworkEndpointSettings.Localhost;
            endpoint.Port = 18066;

            for (int iteration = 0; iteration < 10; iteration++)
            {
                Assert.That(session.StartDirect(true, endpoint), Is.True,
                    $"第 {iteration + 1} 次进入：{session.LastFailure}");
                for (int frame = 0; frame < 5; frame++) yield return null;
                OptionalNetworkBootstrap bootstrap =
                    Object.FindFirstObjectByType<OptionalNetworkBootstrap>();
                Assert.That(bootstrap, Is.Not.Null);

                Task leave = session.LeaveAsync();
                float deadline = Time.realtimeSinceStartup + 10f;
                while (!leave.IsCompleted && Time.realtimeSinceStartup < deadline)
                    yield return null;
                Assert.That(leave.IsCompletedSuccessfully, Is.True);
                Assert.That(bootstrap.NetworkManager.ShutdownInProgress, Is.False,
                    "退出任务完成意味着 NGO 已停止，而不是仅提交异步关闭请求。");
                Assert.That(bootstrap.IsListening, Is.False,
                    "重入前必须真正关闭旧监听，不能绕过玩家生成屏障的启动前保护。");
                // Deliberately no intervening frame: the next iteration is the
                // same-frame exit/reenter path that previously hit the guard.
            }
        }

        [UnityTest]
        public IEnumerator ModeExitCompletesShutdownAndRestoresLobbyInteraction()
        {
            sessionObject = new GameObject("Issue106 Mode Exit Session");
            CoopSessionController session = sessionObject.AddComponent<CoopSessionController>();
            NetworkEndpointSettings endpoint = NetworkEndpointSettings.Localhost;
            endpoint.Port = 18067;
            Assert.That(session.StartDirect(true, endpoint), Is.True, session.LastFailure);
            for (int frame = 0; frame < 5; frame++) yield return null;
            OptionalNetworkBootstrap bootstrap =
                Object.FindFirstObjectByType<OptionalNetworkBootstrap>();
            CoopUiInputGate.PauseMenuVisible = true;
            CoopUiInputGate.EconomyModalVisible = true;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            Time.timeScale = 0f;

            Task exit = session.ShutdownForModeExitAsync();
            float deadline = Time.realtimeSinceStartup + 10f;
            while (!exit.IsCompleted && Time.realtimeSinceStartup < deadline)
                yield return null;

            Assert.That(exit.IsCompletedSuccessfully, Is.True);
            Assert.That(bootstrap.IsListening, Is.False);
            Assert.That(bootstrap.NetworkManager.ShutdownInProgress, Is.False);
            Assert.That(session.State, Is.EqualTo(CoopSessionState.Offline));
            Assert.That(CoopUiInputGate.GameplayInputSuppressed, Is.False);
            Assert.That(Time.timeScale, Is.EqualTo(1f));
            Assert.That(Cursor.lockState, Is.EqualTo(CursorLockMode.None));
            Assert.That(Cursor.visible, Is.True);
        }

        [UnityTest]
        public IEnumerator FailedShutdownOnLeaveStillReturnsOfflineWithUsableCursor()
        {
            return VerifyFailedShutdownRestoresInteraction(modeExit: false, port: 18068);
        }

        [UnityTest]
        public IEnumerator FailedShutdownOnModeExitStillReturnsOfflineWithUsableCursor()
        {
            return VerifyFailedShutdownRestoresInteraction(modeExit: true, port: 18069);
        }

        [UnityTest]
        public IEnumerator CompletedFailedShutdownPromiseDoesNotPoisonNextHostPreparation()
        {
            sessionObject = new GameObject("Issue106 Failed Promise Recovery");
            CoopSessionController session = sessionObject.AddComponent<CoopSessionController>();
            SetShutdownPromise(session, Task.FromException(new TimeoutException(
                "模拟旧关闭任务失败")));
            // 此测试必须显式隔离认证状态；初始化另一个网关已不再清除
            // 正在使用的进程会话，否则旧场景可能误伤新登录的账号。
            MethodInfo resetSession = typeof(SelfHostedAuthenticationGateway)
                .GetMethod("ResetForNewPlaySession",
                    BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(resetSession, Is.Not.Null);
            resetSession.Invoke(null, null);
            // Initialization is local-only. Leave the account deliberately
            // unauthenticated so this regression cannot issue a real room call.
            Task initialize = new SelfHostedAuthenticationGateway().InitializeAsync();
            while (!initialize.IsCompleted) yield return null;

            Task<bool> host = session.HostAsync();
            float deadline = Time.realtimeSinceStartup + 5f;
            while (!host.IsCompleted && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.That(host.IsCompletedSuccessfully, Is.True);
            Assert.That(host.Result, Is.False);
            Assert.That(session.LastFailure, Does.Contain("请先"),
                "网络已空闲后应进入正常身份校验，不能永久复用旧关闭异常。");
            Assert.That(session.LastFailure, Does.Not.Contain("模拟旧关闭任务失败"));
        }

        private IEnumerator VerifyFailedShutdownRestoresInteraction(bool modeExit, ushort port)
        {
            sessionObject = new GameObject("Issue106 Shutdown Failure Session");
            CoopSessionController session = sessionObject.AddComponent<CoopSessionController>();
            NetworkEndpointSettings endpoint = NetworkEndpointSettings.Localhost;
            endpoint.Port = port;
            Assert.That(session.StartDirect(true, endpoint), Is.True, session.LastFailure);
            for (int frame = 0; frame < 5; frame++) yield return null;
            var shutdown = new TaskCompletionSource<bool>();
            // Inject a failed underlying shutdown promise without modifying
            // NGO, waiting five real seconds, or depending on remote networking.
            SetShutdownPromise(session, shutdown.Task);
            CoopUiInputGate.PauseMenuVisible = true;
            CoopUiInputGate.EconomyModalVisible = true;
            Time.timeScale = 0f;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            Task exit = modeExit ? session.ShutdownForModeExitAsync() : session.LeaveAsync();
            shutdown.SetException(new TimeoutException("模拟关闭超时"));
            float deadline = Time.realtimeSinceStartup + 5f;
            while (!exit.IsCompleted && Time.realtimeSinceStartup < deadline)
                yield return null;

            Assert.That(exit.IsCompletedSuccessfully, Is.True,
                "关闭异常应被观察并转换为可用大厅状态，不能再次 await 故障任务后泄漏异常。");
            Assert.That(session.State, Is.EqualTo(CoopSessionState.Offline));
            Assert.That(session.LastFailure, Does.Contain("模拟关闭超时"));
            Assert.That(CoopUiInputGate.GameplayInputSuppressed, Is.False);
            Assert.That(Time.timeScale, Is.EqualTo(1f));
            Assert.That(Cursor.lockState, Is.EqualTo(CursorLockMode.None));
            Assert.That(Cursor.visible, Is.True);
            Assert.That(session.StartDirect(true, endpoint), Is.False,
                "模拟底层仍监听时必须继续拒绝重入，异常恢复不得绕开启动前保护。");
        }

        private static void SetShutdownPromise(CoopSessionController session, Task promise)
        {
            typeof(CoopSessionController).GetField("transportShutdownTask",
                BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(session, promise);
        }
    }
}
