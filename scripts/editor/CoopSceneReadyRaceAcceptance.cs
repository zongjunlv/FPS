using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FPS.Core.GameModes;
using FPS.Networking.Netcode;
using FPS.Networking.Session;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// Offline acceptance: run through the Unity CLI Pipeline run_script command.
// --file scripts/editor/CoopSceneReadyRaceAcceptance.cs
// --entry CoopSceneReadyRaceAcceptance.Run --args '["/tmp/loopback.pfx","/tmp/scene-ready.json"]'
// Create a disposable self-signed legacy PKCS#12 file with password repro-only;
// it is pinned by this script and never used with any non-loopback host.
// Accept only expectedBehaviorPassed=true. No actual multiplayer server is used.
// Diagnostic only. All HTTP traffic terminates in this process at 127.0.0.1.
// Real StartLobbyGameAsync/GetAsync/PollRoomAsync/LoadCityNew/ReportSceneReadyAsync
// methods run unmodified. Reflection binds local gateways and invokes private
// lifecycle entry points; it does not directly set lobbyOperationInProgress.
public static class CoopSceneReadyRaceAcceptance
{
    private static string Output;
    private static string CertificatePath;
    private static bool running;
    private const string LocalPlayer = "offline-repro-host";
    private const string RoomId = "offline-repro-room";
    private const string Epoch = "offline-repro-epoch";
    private const BindingFlags InstancePrivate = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags StaticPrivate = BindingFlags.Static | BindingFlags.NonPublic;

    public static async Task<string> Run(string certificatePath, string outputPath)
    {
        if (running) throw new InvalidOperationException("Another acceptance run is active.");
        if (!File.Exists(certificatePath)) throw new FileNotFoundException("Provide a loopback-only legacy PFX (password: repro-only).", certificatePath);
        CertificatePath = Path.GetFullPath(certificatePath);
        Output = Path.GetFullPath(outputPath);
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            throw new InvalidOperationException("Diagnostic must run in idle, headless EditMode.");
        if (!Application.isBatchMode)
            throw new InvalidOperationException("Refusing to run in a user's interactive Editor.");
        if (!string.IsNullOrEmpty(SelfHostedAuthenticationGateway.AccountId))
            throw new InvalidOperationException("Refusing to replace an existing signed-in account.");
        for (int index = 0; index < SceneManager.sceneCount; index++)
            if (SceneManager.GetSceneAt(index).isDirty)
                throw new InvalidOperationException("Refusing to discard a dirty scene.");

        running = true;
        var setup = EditorSceneManager.GetSceneManagerSetup();
        var context = typeof(GameModeContext).GetFields(StaticPrivate)
            .Where(field => !field.IsLiteral && !field.IsInitOnly)
            .ToDictionary(field => field, field => field.GetValue(null));
        var results = new List<CaseResult>();
        try
        {
            // No PlayMode, camera, rendered window, gameplay AI, or audio is run.
            EditorSceneManager.OpenScene(DedicatedServerConfiguration.CityNewScenePath,
                OpenSceneMode.Single);
            SetIdentity("offline-local-test-token", LocalPlayer);
            GameModeContext.ResetForTests();
            GameModeContext.BeginTransition(GameModeId.Coop, GameModeStage.CoopBattle);
            GameModeContext.TryActivate(GameModeId.Coop, GameModeStage.CoopBattle, out _);
            results.Add(await RunCase("race_get_held_until_after_scene_ready", true));
            results.Add(await RunCase("control_get_completes_before_scene_ready", false));
            results.Add(await RunCase("late_start_get_observes_already_started_battle", true, true));
            bool expectedBehaviorPassed = results.All(result =>
                result.readyEpochPostsAfterBusyCleared == 1 &&
                result.localReadyEpoch == Epoch &&
                result.startSucceeded &&
                result.finalPhase == (result.advanceBattleBeforeStartGetReturns ? "battle" : "loading"));
            Write(new { state = "completed", productionCodeModified = false,
                requestsUseLoopbackOnly = true, expectedBehaviorPassed,
                assertions = results.Select(result => new
                {
                    result.name,
                    sceneReadyAccepted = result.readyEpochPostsAfterBusyCleared == 1 &&
                        result.localReadyEpoch == Epoch,
                    lateStartResponseDidNotFail = result.startSucceeded,
                    phaseDidNotReturnToLobby = result.finalPhase != "lobby"
                }).ToArray(), cases = results });
        }
        catch (Exception exception)
        {
            Write(new { state = "failed", failure = exception.ToString(), cases = results });
            throw;
        }
        finally
        {
            // Only synthetic in-memory credentials are used; no session file
            // is opened and no real authentication token is read or printed.
            SetIdentity(string.Empty, string.Empty);
            foreach (var pair in context) pair.Key.SetValue(null, pair.Value);
            if (setup.Length == 0) EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,
                NewSceneMode.Single);
            else EditorSceneManager.RestoreSceneManagerSetup(setup);
            running = false;
        }
        return Output;
    }

    private static async Task<CaseResult> RunCase(string name, bool blockStartGet,
        bool advanceBattleBeforeStartGetReturns = false)
    {
        using var broker = new LocalBroker(blockStartGet);
        var host = new GameObject("Offline Loading Ready Diagnostic " + name)
        {
            hideFlags = HideFlags.HideAndDontSave
        };
        var session = host.AddComponent<CoopSessionController>();
        var coordinator = host.AddComponent<CoopSceneLoadCoordinator>();
        Task<bool> starting = null;
        try
        {
            // Suppress Unity's coroutine scheduling in EditMode. We drive the
            // unchanged LoadCityNew IEnumerator explicitly below, including
            // its actual scene-ready report/retry helper.
            Set(coordinator, "session", session);
            Set(coordinator, "loadingRoomId", RoomId);
            Set(coordinator, "loadingEpoch", Epoch);
            foreach (string eventName in new[] { "LobbyStartRequested", "BattleSceneReady",
                         "SceneLoadCancelled", "ReturnedToLobby", "StateChanged" })
                typeof(CoopSessionController).GetField(eventName, InstancePrivate)
                    ?.SetValue(session, null);
            Set(session, "roomGateway", new SelfHostedRoomGateway(broker.Settings));
            Set(session, "dedicatedServerGateway", new CoopDedicatedServerGateway(broker.Settings));
            Set(session, "<State>k__BackingField", CoopSessionState.Connected);
            Invoke(session, "BindRoom", broker.InitialRoom());

            // A late battle-phase room response must be accepted, but this
            // control-plane test must not create a real NGO client socket.
            if (advanceBattleBeforeStartGetReturns)
                Set(session, "dedicatedTransportOperationInProgress", true);
            starting = session.StartLobbyGameAsync();
            await Await(broker.FirstStartGetReached.Task, "allocation then first GetAsync");
            if (blockStartGet)
            {
                // The real polling request observes backend loading while
                // StartLobbyGameAsync is still awaiting its own room GET.
                Task polling = (Task)Invoke(session, "PollRoomAsync", RoomId, 0);
                await Await(polling, "parallel production PollRoomAsync");
            }
            else await Await(starting, "normal start request completion");

            if (session.LobbyPhase != "loading")
                throw new InvalidOperationException("Mock room did not reach production loading phase.");
            bool busyAtReady = session.IsLobbyBusy;
            IEnumerator sceneLoad = (IEnumerator)Invoke(coordinator, "LoadCityNew", Epoch, 0);
            int sceneSteps = await DriveCoroutine(sceneLoad,
                DateTime.UtcNow.AddSeconds(15));
            int beforeRelease = broker.ReadyEpochPosts;
            string rejectedMessage = session.LobbyFailureMessage;
            if (advanceBattleBeforeStartGetReturns) broker.AdvanceToBattle();
            broker.ReleaseFirstStartGet.TrySetResult(true);
            await Await(starting, "release in-flight StartLobbyGameAsync GET");
            if (session.IsLobbyBusy)
                throw new InvalidOperationException("Start did not release its busy flag.");

            // Pump the same coordinator Update and real room polling after
            // busy cleared. An acknowledged report must not be duplicated.
            for (int index = 0; index < 12; index++)
            {
                Invoke(coordinator, "Update");
                await Task.Delay(2);
            }
            Task refresh = (Task)Invoke(session, "PollRoomAsync", RoomId, 0);
            await Await(refresh, "post-busy room poll");
            int afterClear = broker.ReadyEpochPosts;
            string localReady = session.ActiveSession.players.First(player =>
                player.accountId == LocalPlayer).readyEpoch;

            return new CaseResult
            {
                name = name, blockStartGet = blockStartGet,
                advanceBattleBeforeStartGetReturns = advanceBattleBeforeStartGetReturns,
                startSucceeded = starting.Result,
                startWasBusyAtSceneReady = busyAtReady,
                sceneEnumeratorSteps = sceneSteps,
                readyEpochPostsBeforeRelease = beforeRelease,
                readyEpochPostsAfterBusyCleared = afterClear,
                rejectionMessage = rejectedMessage,
                coordinatorLastFailureAfterReport = coordinator.LastFailure,
                localReadyEpoch = localReady ?? string.Empty,
                finalPhase = session.LobbyPhase,
                phaseTransitions = broker.PhaseTransitions.ToArray(),
                releaseCalls = broker.ReleaseCalls,
                requests = broker.Requests.ToArray(),
                timeoutWasAdvancedDeterministically = false
            };
        }
        finally
        {
            broker.ReleaseFirstStartGet.TrySetResult(true);
            if (starting != null && !starting.IsCompleted)
            {
                try { await Await(starting, "cleanup pending start"); } catch { }
            }
            Object.DestroyImmediate(host);
        }
    }

    private static void SetIdentity(string token, string account)
    {
        typeof(SelfHostedAuthenticationGateway).GetField("currentAccessToken", StaticPrivate)
            .SetValue(null, token);
        typeof(SelfHostedAuthenticationGateway).GetField("currentAccountId", StaticPrivate)
            .SetValue(null, account);
    }
    private static void Set(object target, string name, object value) =>
        target.GetType().GetField(name, InstancePrivate).SetValue(target, value);
    private static object Invoke(object target, string name, params object[] args) =>
        target.GetType().GetMethod(name, InstancePrivate).Invoke(target, args);
    private static async Task Await(Task task, string stage)
    {
        if (await Task.WhenAny(task, Task.Delay(10000)) != task)
            throw new TimeoutException(stage);
        await task;
    }
    private static async Task Until(Func<bool> predicate, string stage)
    {
        DateTime end = DateTime.UtcNow.AddSeconds(10);
        while (!predicate())
        {
            if (DateTime.UtcNow > end) throw new TimeoutException(stage);
            await Task.Delay(1);
        }
    }
    private static async Task<int> DriveCoroutine(IEnumerator routine, DateTime deadline)
    {
        int steps = 0;
        while (routine.MoveNext())
        {
            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException("LoadCityNew/scene-ready coroutine did not complete.");
            steps++;
            // Match Unity's nested IEnumerator semantics. Moving only the
            // outer enumerator would skip ReportReadyUntilAcknowledged.
            if (routine.Current is IEnumerator nested)
                steps += await DriveCoroutine(nested, deadline);
            else
                await Task.Delay(1);
        }
        return steps;
    }
    private static void Write(object result) =>
        File.WriteAllText(Output, JsonConvert.SerializeObject(result, Formatting.Indented));

    private sealed class CaseResult
    {
        public string name;
        public bool blockStartGet;
        public bool advanceBattleBeforeStartGetReturns;
        public bool startSucceeded;
        public bool startWasBusyAtSceneReady;
        public int sceneEnumeratorSteps;
        public int readyEpochPostsBeforeRelease;
        public int readyEpochPostsAfterBusyCleared;
        public string rejectionMessage;
        public string coordinatorLastFailureAfterReport;
        public string localReadyEpoch;
        public string finalPhase;
        public string[] phaseTransitions;
        public int releaseCalls;
        public string[] requests;
        public bool timeoutWasAdvancedDeterministically;
    }

    private sealed class LocalBroker : IDisposable
    {
        private readonly TcpListener listener;
        private readonly X509Certificate2 certificate;
        private readonly bool blockFirstGet;
        private readonly object sync = new object();
        private readonly CancellationTokenSource cancel = new CancellationTokenSource();
        private readonly List<TcpClient> clients = new List<TcpClient>();
        private string phase = "lobby";
        private string readyEpoch = string.Empty;
        private long revision = 1;
        private int getCalls;
        private int readyPosts;
        private int releaseCalls;
        public readonly TaskCompletionSource<bool> FirstStartGetReached =
            new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource<bool> ReleaseFirstStartGet =
            new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly List<string> Requests = new List<string>();
        public readonly List<string> PhaseTransitions = new List<string>();
        public CoopDedicatedServerSettings Settings { get; }
        public int ReadyEpochPosts => Volatile.Read(ref readyPosts);
        public int ReleaseCalls => Volatile.Read(ref releaseCalls);

        public LocalBroker(bool block)
        {
            blockFirstGet = block;
            certificate = new X509Certificate2(CertificatePath, "repro-only",
                X509KeyStorageFlags.Exportable);
            listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            using SHA256 sha = SHA256.Create();
            string pin = BitConverter.ToString(sha.ComputeHash(certificate.RawData))
                .Replace("-", string.Empty).ToLowerInvariant();
            Settings = new CoopDedicatedServerSettings
            {
                brokerUrl = "https://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port,
                pinnedCertificateSha256 = pin, requestTimeoutSeconds = 10,
                applicationVersion = "0.1.0", protocolVersion = "2", contentVersion = "citynew-v1"
            };
            _ = Task.Run(AcceptLoop);
        }

        public void AdvanceToBattle()
        {
            lock (sync) { phase = "battle"; revision++; }
        }

        public SelfHostedRoomSnapshot InitialRoom() => new SelfHostedRoomSnapshot
        {
            id = RoomId, hostId = LocalPlayer, mapId = "CityNew", phase = "lobby",
            seed = 18018, revision = 1,
            players = new[]
            {
                new SelfHostedRoomPlayer { accountId = LocalPlayer,
                    appearanceId = "operative-alpha", ready = true, connected = true },
                new SelfHostedRoomPlayer { accountId = "offline-repro-guest",
                    appearanceId = "operative-alpha", ready = true, connected = true }
            }
        };

        private async Task AcceptLoop()
        {
            while (!cancel.IsCancellationRequested)
            {
                TcpClient client;
                try { client = await listener.AcceptTcpClientAsync(); }
                catch { return; }
                lock (sync) clients.Add(client);
                _ = Task.Run(() => Handle(client));
            }
        }

        private async Task Handle(TcpClient client)
        {
            try
            {
                using (client)
                using (var tls = new SslStream(client.GetStream(), false))
                {
                    await tls.AuthenticateAsServerAsync(certificate, false,
                        SslProtocols.Tls12, false);
                    var head = new List<byte>();
                    var one = new byte[1];
                    while (head.Count < 32768)
                    {
                        int count = await tls.ReadAsync(one, 0, 1);
                        if (count == 0) return;
                        head.Add(one[0]);
                        int n = head.Count;
                        if (n >= 4 && head[n - 4] == 13 && head[n - 3] == 10 &&
                            head[n - 2] == 13 && head[n - 1] == 10) break;
                    }
                    string[] lines = Encoding.ASCII.GetString(head.ToArray()).Split(
                        new[] { "\r\n" }, StringSplitOptions.None);
                    string[] request = lines[0].Split(' ');
                    string method = request[0];
                    string path = request[1];
                    int length = 0;
                    foreach (string line in lines)
                        if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                            length = int.Parse(line.Substring(line.IndexOf(':') + 1).Trim());
                    // Unity/curl may send Expect: 100-continue for POST.
                    if (lines.Any(line => line.Equals("Expect: 100-continue",
                            StringComparison.OrdinalIgnoreCase)))
                    {
                        byte[] interim = Encoding.ASCII.GetBytes("HTTP/1.1 100 Continue\r\n\r\n");
                        await tls.WriteAsync(interim, 0, interim.Length);
                        await tls.FlushAsync();
                    }
                    var payload = new byte[length];
                    for (int offset = 0; offset < length;)
                    {
                        int count = await tls.ReadAsync(payload, offset, length - offset);
                        if (count == 0) return;
                        offset += count;
                    }
                    string body = Encoding.UTF8.GetString(payload);
                    lock (sync) Requests.Add(method + " " + path);
                    string response;
                    if (path == "/v1/matches/allocate")
                    {
                        lock (sync) { phase = "loading"; revision++; }
                        response = AllocationJson();
                    }
                    else if (method == "GET" && path == "/v1/rooms/" + RoomId)
                    {
                        bool first = Interlocked.Increment(ref getCalls) == 1;
                        if (first)
                        {
                            FirstStartGetReached.TrySetResult(true);
                            if (blockFirstGet) await ReleaseFirstStartGet.Task;
                        }
                        lock (sync) response = RoomJson();
                    }
                    else if (path == "/v1/rooms/" + RoomId + "/player")
                    {
                        lock (sync)
                        {
                            if (body.Contains("\"readyEpoch\""))
                            {
                                Interlocked.Increment(ref readyPosts);
                                readyEpoch = Epoch;
                            }
                            revision++;
                            response = RoomJson();
                        }
                    }
                    else if (path == "/v1/rooms/" + RoomId + "/phase")
                    {
                        lock (sync)
                        {
                            phase = body.Contains("cancelled") ? "cancelled" :
                                body.Contains("battle") ? "battle" : "lobby";
                            PhaseTransitions.Add(phase);
                            revision++;
                            response = RoomJson();
                        }
                    }
                    else if (path == "/v1/matches/release")
                    {
                        Interlocked.Increment(ref releaseCalls);
                        response = "{}";
                    }
                    else throw new InvalidOperationException("Unexpected local mock request: " + path);
                    byte[] data = Encoding.UTF8.GetBytes(response);
                    byte[] header = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\n" +
                        "Content-Type: application/json\r\nContent-Length: " + data.Length +
                        "\r\nConnection: close\r\n\r\n");
                    await tls.WriteAsync(header, 0, header.Length);
                    await tls.WriteAsync(data, 0, data.Length);
                    await tls.FlushAsync();
                }
            }
            catch (Exception exception)
            {
                if (!cancel.IsCancellationRequested)
                    lock (sync) Requests.Add("MOCK_ERROR " + exception.GetType().Name + ": " + exception.Message);
            }
        }

        private static string AllocationJson() => "{\"connection\":" + ConnectionJson() +
            ",\"connectionTicket\":\"offline-local-ticket\"}";
        private static string ConnectionJson() => "{\"schemaVersion\":\"fps-remote-match-v1\"," +
            "\"host\":\"127.0.0.1\",\"port\":17777,\"matchId\":\"offline-local-match\"," +
            "\"applicationVersion\":\"0.1.0\",\"protocolVersion\":\"2\"," +
            "\"contentVersion\":\"citynew-v1\",\"maximumPlayers\":2}";
        private string RoomJson() => "{\"id\":\"" + RoomId + "\",\"hostId\":\"" +
            LocalPlayer + "\",\"mapId\":\"CityNew\",\"phase\":\"" + phase +
            "\",\"seed\":18018,\"loadEpoch\":\"" + Epoch + "\",\"revision\":" +
            revision + ",\"serverAllocation\":" + ConnectionJson() + ",\"players\":[" +
            "{\"accountId\":\"" + LocalPlayer + "\",\"appearanceId\":\"operative-alpha\"," +
            "\"ready\":true,\"readyEpoch\":\"" + readyEpoch + "\",\"connected\":true}," +
            "{\"accountId\":\"offline-repro-guest\",\"appearanceId\":\"operative-alpha\"," +
            "\"ready\":true,\"readyEpoch\":\"\",\"connected\":true}]}";
        public void Dispose()
        {
            cancel.Cancel();
            ReleaseFirstStartGet.TrySetResult(true);
            listener.Stop();
            lock (sync) foreach (TcpClient client in clients) client.Dispose();
            certificate.Dispose();
            cancel.Dispose();
        }
    }
}
