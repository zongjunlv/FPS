using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FPS.Networking.Diagnostics;
using FPS.Networking.Domain;
using FPS.Networking.Netcode;
using FPS.Networking.Session;
using Unity.Netcode;
using Unity.Networking.Transport;
using Unity.Networking.Transport.Analytics;
using UnityEngine;
using UnityEngine.AI;

namespace FPS.Networking.Acceptance
{
    [DisallowMultipleComponent]
    internal sealed class Issue100ClientScenarioDriver : MonoBehaviour
    {
        private const string AcceptancePassword = "Issue100!Pass";
        private const float PredictedShotOriginHeight = 1.25f;
        private const double WaveClearTimeoutSeconds = 95d;
        private readonly Dictionary<uint, double> pendingShots = new();
        private readonly List<double> rttSamples = new();
        private readonly RaycastHit[] combatLineHits = new RaycastHit[32];
        private readonly CoopPlayerMovementCollision combatMovementCollision = new();
        private const float CombatProbeDistance = 0.75f;
        private Vector3[] combatCorners = Array.Empty<Vector3>();
        private int combatWaypointIndex;
        private Vector3 combatRouteTarget;
        private Vector3 combatProgressPosition;
        private double combatNextRepathAt;
        private double combatProgressCheckedAt = -1d;
        private int combatStallCount;
        private double combatRecoveryUntil;
        private Vector3 combatRecoveryDirection;
        private bool combatJumpRequested;
        private NavMeshPath navigationPath;
        private Issue100AcceptanceRuntime runtime;
        private Issue100RuntimeArguments options;
        private Issue100EvidenceStore evidence;
        private NetworkPlayerReplica replica;
        private NetworkVerticalSliceInputDriver input;
        private NetworkCoopSessionAuthority authority;
        private OptionalNetworkBootstrap network;
        private NetworkDiagnosticsAccumulator metrics;
        private DriverStatistics initialTraffic;
        private uint highestAcknowledgedSequence;
        private int sentCommands;
        private int acceptedCommands;
        private int hitFeedbacks;
        private int missedFeedbacks;
        private int blockedFeedbacks;
        private int lockedCombatTargetId;
        private string lastNavigationTrace = string.Empty;
        private int combatDecisionRunGeneration = -1;
        private int combatDecisionSampleCount;
        private double nextCombatDecisionAt;
        private double divergentSince = -1d;
        private bool initialized;
        private bool metricsBound;

        public void Initialize(Issue100AcceptanceRuntime owner)
        {
            runtime = owner != null
                ? owner
                : throw new ArgumentNullException(nameof(owner));
            options = runtime.Options;
            evidence = runtime.Evidence;
            metrics = new NetworkDiagnosticsAccumulator(options.Scenario);
            initialized = true;
            StartCoroutine(Run());
        }

        private IEnumerator Run()
        {
            if (!options.Reconnect)
            {
                yield return RunLocalAuthentication();
                if (!initialized) yield break;
            }

            evidence.Started(Issue100AcceptanceSteps.RoomJoin,
                detail: $"match={options.MatchId};dataPlane=UnityTransport");
            yield return WaitForConnection(45d);
            if (replica == null)
            {
                Abort(Issue100AcceptanceSteps.RoomJoin,
                    "客户端没有在时限内获得本地权威角色副本。");
                yield break;
            }
            evidence.Passed(Issue100AcceptanceSteps.RoomJoin,
                Tick(), $"player={replica.PlayerId}");
            BindMetrics();

            yield return PresentStep(Issue100AcceptanceSteps.CharacterSelect);
            string appearance = NetworkPresentationIds.ResolveAppearance(
                options.AppearanceId);
            if (!string.Equals(replica.AppearanceId, appearance,
                    StringComparison.Ordinal))
            {
                Abort(Issue100AcceptanceSteps.CharacterSelect,
                    $"角色外观未同步：expected={appearance};" +
                    $"actual={replica.AppearanceId}");
                yield break;
            }
            evidence.Passed(Issue100AcceptanceSteps.CharacterSelect,
                Tick(), "appearance=" + appearance);
            yield return PresentStep(Issue100AcceptanceSteps.LobbyReady);
            evidence.Passed(Issue100AcceptanceSteps.LobbyReady,
                Tick(), "direct-ready-barrier=connected-and-snapshot-complete");

            if (options.Reconnect)
            {
                evidence.Passed(Issue100AcceptanceSteps.ReconnectRestore,
                    Tick(), $"player={replica.PlayerId};tick={Tick()}");
                yield return ExerciseInputs(includeTraversalAssertions: false);
                yield return FireSamples(24, targetEnemies: false);
                yield return FinalizeMetrics();
                runtime.FinishClient(true);
                yield break;
            }

            yield return ExerciseInputs(includeTraversalAssertions: true);
            if (!initialized) yield break;
            yield return FireSamples(24,
                targetEnemies: options.Scenario.StableId ==
                    "rtt-000-loss-00" &&
                    options.Role == Issue100ProcessRole.ClientA);
            if (!initialized) yield break;

            bool standard = string.Equals(options.Scenario.StableId,
                "rtt-000-loss-00", StringComparison.Ordinal);
            if (!standard)
            {
                yield return FinalizeMetrics();
                runtime.FinishClient(true);
                yield break;
            }

            if (options.Role == Issue100ProcessRole.ClientB)
            {
                yield return ExerciseReconnect();
                if (!initialized) yield break;
                yield return AssistWave();
                if (!initialized) yield break;
                yield return RallyForExtraction();
                if (!initialized) yield break;
                yield return WaitUntil(() => authority.WorldState.MissionPhase ==
                    AuthoritativeMissionPhase.Victory, 90d);
                if (authority.WorldState.MissionPhase !=
                    AuthoritativeMissionPhase.Victory)
                {
                    Abort(Issue100AcceptanceSteps.MatchSettlement,
                        "Client B 没有观察到最终胜利结算。");
                    yield break;
                }
                yield return FinalizeMetrics();
                runtime.FinishClient(true);
                yield break;
            }

            yield return CompleteWave();
            if (!initialized) yield break;
            yield return ExerciseEconomy();
            if (!initialized) yield break;
            yield return CompleteMission();
            if (!initialized) yield break;
            yield return FinalizeMetrics();
            runtime.FinishClient(true);
        }

        private IEnumerator RunLocalAuthentication()
        {
            string username = SafeUsername(options.AccountId);
            var gateway = new Issue100LocalAuthenticationGateway();
            var account = new CoopAccountController(gateway);
            evidence.Started(Issue100AcceptanceSteps.AccountRegister,
                detail: "controlPlane=local-acceptance");
            if (options.RecordVideo)
                yield return new WaitForSecondsRealtime(0.9f);
            Task<bool> register = account.RegisterAsync(username,
                AcceptancePassword, AcceptancePassword);
            yield return WaitTask(register);
            if (!register.IsCompletedSuccessfully || !register.Result)
            {
                Abort(Issue100AcceptanceSteps.AccountRegister,
                    account.LastFailure);
                yield break;
            }
            evidence.Passed(Issue100AcceptanceSteps.AccountRegister,
                detail: "identity=" + account.PlayerId);
            account.SignOut();

            evidence.Started(Issue100AcceptanceSteps.AccountLogin,
                detail: "controlPlane=local-acceptance");
            if (options.RecordVideo)
                yield return new WaitForSecondsRealtime(0.9f);
            Task<bool> login = account.SignInAsync(username,
                AcceptancePassword);
            yield return WaitTask(login);
            if (!login.IsCompletedSuccessfully || !login.Result)
            {
                Abort(Issue100AcceptanceSteps.AccountLogin,
                    account.LastFailure);
                yield break;
            }
            evidence.Passed(Issue100AcceptanceSteps.AccountLogin,
                detail: "identity=" + account.PlayerId);
        }

        private IEnumerator WaitForConnection(double timeoutSeconds)
        {
            double deadline = Time.realtimeSinceStartupAsDouble +
                timeoutSeconds;
            while (Time.realtimeSinceStartupAsDouble < deadline)
            {
                network = FindAnyObjectByType<OptionalNetworkBootstrap>();
                authority = FindAnyObjectByType<NetworkCoopSessionAuthority>();
                replica = FindObjectsByType<NetworkPlayerReplica>(
                        FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                    .FirstOrDefault(value => value.IsSpawned &&
                        value.IsLocallyControlled &&
                        value.HasConsumedServerState &&
                        value.IsPresentationReady);
                if (network?.NetworkManager != null &&
                    network.NetworkManager.IsConnectedClient &&
                    authority != null && authority.IsReplicatedSnapshotComplete &&
                    replica != null)
                {
                    input = replica.GetComponent<
                        NetworkVerticalSliceInputDriver>();
                    if (input != null &&
                        input.TryAcquireExclusiveInput(this))
                        yield break;
                }
                yield return null;
            }
        }

        private void BindMetrics()
        {
            highestAcknowledgedSequence =
                replica.PresentedAcknowledgedSequence;
            input.CommandSubmitted += HandleCommandSubmitted;
            replica.ShotFeedbackReceived += HandleShotFeedback;
            replica.PredictionReconciled += HandlePredictionReconciled;
            if (network?.Transport != null)
            {
                ref NetworkDriver driver = ref network.Transport
                    .GetNetworkDriver();
                if (driver.IsCreated) initialTraffic = driver.GetStatistics();
            }
            metricsBound = true;
        }

        private IEnumerator ExerciseReconnect()
        {
            CoopSessionController controller = FindAnyObjectByType<
                CoopSessionController>();
            if (controller == null || network == null)
            {
                Abort(Issue100AcceptanceSteps.ReconnectRestore,
                    "缺少合作会话控制器或网络引导器。");
                yield break;
            }
            int expectedPlayerId = replica.PlayerId;
            long disconnectedAtTick = Tick();
            Vector3 expectedPosition = replica.PresentedPosition;
            float expectedHealth = replica.PresentedHealth;
            int expectedMagazine = replica.PresentedMagazineAmmo;
            bool hadServerState = authority.TryGetPlayerState(
                expectedPlayerId, out NetcodePlayerState beforeState);
            evidence.Passed("reconnect.ready-to-disconnect",
                disconnectedAtTick,
                $"player={expectedPlayerId};tick={disconnectedAtTick};" +
                $"clientMagazine={expectedMagazine};" +
                $"serverMagazine={(hadServerState ? beforeState.MagazineAmmo : -1)}");

            UnbindMetrics();
            network.Shutdown();
            yield return WaitUntil(() => !network.IsListening, 5d);
            yield return new WaitForSecondsRealtime(0.35f);

            string ticket = options.ReconnectCredential;
            if (string.IsNullOrWhiteSpace(ticket))
            {
                if (!CoopAdmissionEnvironment.TryCreateCodec(
                        out CoopConnectionTicketCodec codec,
                        out string error))
                {
                    Abort(Issue100AcceptanceSteps.ReconnectRestore, error);
                    yield break;
                }
                ticket = codec.Issue(options.AccountId,
                    new CoopBuildCompatibility("local-dev", CoopWireProtocol.CompatibilityId,
                        "citynew-v1"),
                    DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    matchId: options.MatchId);
            }
            if (!controller.ReconnectWithCredential(ticket))
            {
                Abort(Issue100AcceptanceSteps.ReconnectRestore,
                    controller.LastFailure);
                yield break;
            }

            replica = null;
            input = null;
            authority = null;
            yield return WaitForConnection(12d);
            if (replica == null || replica.PlayerId != expectedPlayerId)
            {
                Abort(Issue100AcceptanceSteps.ReconnectRestore,
                    "重连没有恢复原 simulationPlayerId。");
                yield break;
            }
            if (Tick() <= disconnectedAtTick)
            {
                Abort(Issue100AcceptanceSteps.ReconnectRestore,
                    "重连期间服务器 Tick 没有继续推进。");
                yield break;
            }
            if (Mathf.Abs(replica.PresentedHealth - expectedHealth) > 0.01f ||
                replica.PresentedMagazineAmmo != expectedMagazine ||
                Vector3.Distance(replica.PresentedPosition,
                    expectedPosition) > 1.5f)
            {
                bool hasServerState = authority.TryGetPlayerState(
                    expectedPlayerId, out NetcodePlayerState afterState);
                Abort(Issue100AcceptanceSteps.ReconnectRestore,
                    "重连后的生命、弹药或位置没有恢复权威状态。" +
                    $" before=({expectedHealth:0.#},{expectedMagazine}," +
                    $"{expectedPosition}) after=({replica.PresentedHealth:0.#}," +
                    $"{replica.PresentedMagazineAmmo},{replica.PresentedPosition})" +
                    $" authority=({(hasServerState ? afterState.Health : -1f):0.#}," +
                    $"{(hasServerState ? afterState.MagazineAmmo : -1)}," +
                    $"{(hasServerState ? afterState.Position : Vector3.zero)})");
                yield break;
            }
            BindMetrics();
            evidence.Passed(Issue100AcceptanceSteps.ReconnectRestore, Tick(),
                $"player={replica.PlayerId};serverTick={Tick()}");
        }

        private IEnumerator ExerciseInputs(bool includeTraversalAssertions)
        {
            yield return PresentStep(Issue100AcceptanceSteps.Walk);
            Vector3 origin = replica.PresentedPosition;
            yield return DriveFor(new Vector2(0f, 1f), 0.65f,
                sprint: false, crouch: false, aiming: false);
            // CityNew spawn points can face a nearby solid surface. Probe
            // the other cardinal directions before treating a blocked path
            // as a broken authoritative movement pipeline.
            if (includeTraversalAssertions &&
                Vector3.Distance(origin, replica.PresentedPosition) < 0.15f)
            {
                foreach (Vector2 direction in new[]
                {
                    new Vector2(0f, -1f), new Vector2(1f, 0f),
                    new Vector2(-1f, 0f)
                })
                {
                    yield return DriveFor(direction, 0.65f,
                        sprint: false, crouch: false, aiming: false);
                    if (Vector3.Distance(origin, replica.PresentedPosition) >=
                        0.15f)
                        break;
                }
            }
            if (includeTraversalAssertions &&
                Vector3.Distance(origin, replica.PresentedPosition) < 0.15f)
            {
                Abort(Issue100AcceptanceSteps.Walk,
                    "移动命令没有改变权威位置。");
                yield break;
            }
            evidence.Passed(Issue100AcceptanceSteps.Walk, Tick(),
                "distance=" + Vector3.Distance(origin,
                    replica.PresentedPosition).ToString("0.###"));

            yield return PresentStep(Issue100AcceptanceSteps.Sprint);
            yield return DriveFor(new Vector2(0f, 1f), 0.55f,
                sprint: true, crouch: false, aiming: false);
            evidence.Passed(Issue100AcceptanceSteps.Sprint, Tick(),
                "authoritative-input=sprint");

            yield return PresentStep(Issue100AcceptanceSteps.Crouch);
            SetInputFrame(Vector2.zero, replica.PresentedAimYaw,
                replica.PresentedAimPitch, false, false, false, true, false);
            if (includeTraversalAssertions)
                yield return WaitUntil(IsAuthoritativelyCrouching, 6d);
            else
                yield return new WaitForSecondsRealtime(0.4f);
            if (includeTraversalAssertions && !IsAuthoritativelyCrouching())
            {
                Abort(Issue100AcceptanceSteps.Crouch,
                    "权威姿态没有进入下蹲。");
                yield break;
            }
            evidence.Passed(Issue100AcceptanceSteps.Crouch, Tick(),
                "stance=crouching");
            ReleaseInput();
            yield return new WaitForSecondsRealtime(0.1f);

            yield return PresentStep(Issue100AcceptanceSteps.Jump);
            SetInputFrame(Vector2.zero, replica.PresentedAimYaw,
                replica.PresentedAimPitch, false, true, false, false, false);
            yield return new WaitForSecondsRealtime(0.25f);
            evidence.Passed(Issue100AcceptanceSteps.Jump, Tick(),
                "authoritative-input=jump");

            yield return PresentStep(Issue100AcceptanceSteps.Aim);
            SetInputFrame(Vector2.zero, replica.PresentedAimYaw,
                replica.PresentedAimPitch, false, false, false, false, true);
            if (includeTraversalAssertions)
            {
                double aimDeadline = Time.realtimeSinceStartupAsDouble + 8d;
                while (!IsAuthoritativelyAiming() &&
                       Time.realtimeSinceStartupAsDouble < aimDeadline)
                {
                    SetInputFrame(Vector2.zero, replica.PresentedAimYaw,
                        replica.PresentedAimPitch, false, false, false,
                        false, true);
                    input.TrySubmitCurrentFrame(out _);
                    yield return null;
                }
            }
            else
                yield return new WaitForSecondsRealtime(0.3f);
            if (includeTraversalAssertions && !IsAuthoritativelyAiming())
            {
                Abort(Issue100AcceptanceSteps.Aim,
                    "权威表现没有进入瞄准姿态。");
                yield break;
            }
            evidence.Passed(Issue100AcceptanceSteps.Aim, Tick(),
                "aiming=true");
            ReleaseInput();
            yield return new WaitForSecondsRealtime(0.1f);
        }

        private IEnumerator FireSamples(int count, bool targetEnemies)
        {
            yield return PresentStep(Issue100AcceptanceSteps.Fire);
            int feedbackBefore = metrics.Complete().HitFeedbackSampleCount;
            int hitsBefore = hitFeedbacks;
            // In the live CityNew scenario enemies attack while this initial
            // transport sample is collected. Eight acknowledged shots are
            // enough to prove aim/fire/feedback before both clients proceed
            // to the longer cooperative wave-clear stage, which contributes
            // the remaining combat samples without keeping either player
            // stationary until they are downed.
            int requiredFeedback = Math.Min(targetEnemies ? 8 : 20, count);
            int attempts = 0;
            double fireDeadline = Time.realtimeSinceStartupAsDouble +
                                  (options.RecordVideo ? 45d : 22d);
            while (metrics.Complete().HitFeedbackSampleCount - feedbackBefore <
                       requiredFeedback &&
                   attempts < count * 4 &&
                   Time.realtimeSinceStartupAsDouble < fireDeadline)
            {
                if (replica.PresentedLifeState !=
                    AuthoritativePlayerLifeState.Alive)
                    break;
                if (replica.PresentedMagazineAmmo <= 1)
                {
                    input.SubmitPresentationAction(
                        NetworkPresentationAction.Reload);
                    yield return new WaitForSecondsRealtime(1.4f);
                    continue;
                }
                Vector3 target = targetEnemies
                    ? ResolveTargetPoint(lockTarget: true)
                    : replica.PresentedPosition + Vector3.forward * 30f +
                      Vector3.up * 1.2f;
                if (targetEnemies && !CanFireAt(target, 2f))
                {
                    AimAt(target, fire: false, approach: true);
                    yield return null;
                    continue;
                }
                int feedbackAtShot = metrics.Complete()
                    .HitFeedbackSampleCount;
                AimAt(target, fire: true, approach: targetEnemies);
                attempts++;
                double feedbackDeadline =
                    Time.realtimeSinceStartupAsDouble + 1.25d;
                while (metrics.Complete().HitFeedbackSampleCount <=
                           feedbackAtShot &&
                       Time.realtimeSinceStartupAsDouble < feedbackDeadline)
                    yield return null;
                yield return new WaitForSecondsRealtime(0.08f);
            }
            AimAt(replica.PresentedPosition + Vector3.forward * 30f,
                fire: false);
            double deadline = Time.realtimeSinceStartupAsDouble + 5d;
            while (Time.realtimeSinceStartupAsDouble < deadline &&
                   metrics.Complete().HitFeedbackSampleCount - feedbackBefore <
                   requiredFeedback)
                yield return null;
            int feedback = metrics.Complete().HitFeedbackSampleCount -
                feedbackBefore;
            if (feedback < requiredFeedback)
            {
                Abort(Issue100AcceptanceSteps.Fire,
                    $"射击反馈不足：{feedback}/{requiredFeedback};" +
                    $"attempts={attempts};life=" +
                    $"{replica.PresentedLifeState}。");
                yield break;
            }
            if (targetEnemies && hitFeedbacks <= hitsBefore)
            {
                Abort(Issue100AcceptanceSteps.Fire,
                    "射击反馈均为未命中或遮挡，没有对权威敌人造成伤害。");
                yield break;
            }
            evidence.Passed(Issue100AcceptanceSteps.Fire, Tick(),
                $"feedbackSamples={feedback};hits=" +
                $"{hitFeedbacks - hitsBefore}");

            yield return PresentStep(Issue100AcceptanceSteps.Reload);
            int magazineBefore = replica.PresentedMagazineAmmo;
            input.SubmitPresentationAction(NetworkPresentationAction.Reload);
            yield return WaitUntil(() =>
                authority.TryGetPlayerState(replica.PlayerId,
                    out NetcodePlayerState state) &&
                !state.Reloading &&
                state.MagazineAmmo > magazineBefore, 6d);
            if (!authority.TryGetPlayerState(replica.PlayerId,
                    out NetcodePlayerState reloadedState) ||
                reloadedState.Reloading ||
                reloadedState.MagazineAmmo <= magazineBefore)
            {
                Abort(Issue100AcceptanceSteps.Reload,
                    "服务器没有完成换弹并增加弹匣子弹。");
                yield break;
            }
            evidence.Passed(Issue100AcceptanceSteps.Reload, Tick(),
                $"magazine={magazineBefore}->{reloadedState.MagazineAmmo}");
        }

        private IEnumerator CompleteWave()
        {
            evidence.Started(Issue100AcceptanceSteps.WaveComplete, Tick());
            int hitsBefore = hitFeedbacks;
            int missesBefore = missedFeedbacks;
            int blockedBefore = blockedFeedbacks;
            if (options.RecordVideo)
                yield return new WaitForSecondsRealtime(0.9f);
            double deadline = Time.realtimeSinceStartupAsDouble +
                WaveClearTimeoutSeconds;
            double nextFireAt = 0d;
            while (authority.WorldState.RemainingEnemyCount > 0 &&
                   Time.realtimeSinceStartupAsDouble < deadline)
            {
                yield return RecoverTeamDuringCombat(
                    Issue100AcceptanceSteps.WaveComplete);
                if (!initialized) yield break;
                if (replica.PresentedLifeState !=
                    AuthoritativePlayerLifeState.Alive)
                    continue;
                if (replica.PresentedMagazineAmmo <= 1)
                {
                    if (replica.PresentedReserveAmmo <= 0) break;
                    input.SubmitPresentationAction(
                        NetworkPresentationAction.Reload);
                    yield return new WaitForSecondsRealtime(1.35f);
                    continue;
                }
                Vector3 target = ResolveTargetPoint(lockTarget: true);
                double now = Time.realtimeSinceStartupAsDouble;
                bool fireDue = now >= nextFireAt;
                bool eligible = CanFireAt(target, 3.5f);
                bool fire = fireDue && eligible;
                if (fireDue) nextFireAt = now + 0.18d;
                TraceCombatDecision(target, fireDue, eligible, fire);
                AimAt(target, fire: fire, approach: true);
                // Aim/movement are normal per-frame input; only requesting a
                // shot retains the original .18s acceptance cadence.
                yield return null;
            }
            yield return RecoverTeamDuringCombat(
                Issue100AcceptanceSteps.WaveComplete);
            if (!initialized) yield break;
            AimAt(replica.PresentedPosition + Vector3.forward * 20f,
                fire: false);
            if (authority.WorldState.RemainingEnemyCount > 0)
            {
                Abort(Issue100AcceptanceSteps.WaveComplete,
                    $"未能在时限内通过真实射击清除敌人：" +
                    $"remaining={authority.WorldState.RemainingEnemyCount};" +
                    $"ammo={replica.PresentedMagazineAmmo}/" +
                    $"{replica.PresentedReserveAmmo};" +
                    $"hits={hitFeedbacks - hitsBefore};" +
                    $"misses={missedFeedbacks - missesBefore};" +
                    $"blocked={blockedFeedbacks - blockedBefore}。");
                yield break;
            }
            evidence.Passed(Issue100AcceptanceSteps.WaveComplete, Tick(),
                "killed=" + authority.WorldState.KilledTargets);
            yield return PresentStep(Issue100AcceptanceSteps.DropSpawn);
            if (authority.ReplicatedWorldDropCount <= 0)
            {
                Abort(Issue100AcceptanceSteps.DropSpawn,
                    "波次完成后没有权威掉落物。");
                yield break;
            }
            evidence.Passed(Issue100AcceptanceSteps.DropSpawn, Tick(),
                "drops=" + authority.ReplicatedWorldDropCount);
        }

        private IEnumerator AssistWave()
        {
            const string step = "wave.coop-assist";
            evidence.Started(step, Tick(), "client-b=reconnected");
            double deadline = Time.realtimeSinceStartupAsDouble +
                WaveClearTimeoutSeconds;
            double nextFireAt = 0d;
            while (authority.WorldState.RemainingEnemyCount > 0 &&
                   Time.realtimeSinceStartupAsDouble < deadline)
            {
                yield return RecoverTeamDuringCombat(step);
                if (!initialized) yield break;
                if (replica.PresentedLifeState !=
                    AuthoritativePlayerLifeState.Alive)
                    continue;
                if (replica.PresentedMagazineAmmo <= 1)
                {
                    if (replica.PresentedReserveAmmo <= 0) break;
                    input.SubmitPresentationAction(
                        NetworkPresentationAction.Reload);
                    yield return new WaitForSecondsRealtime(1.35f);
                    continue;
                }
                Vector3 target = ResolveTargetPoint(lockTarget: true);
                double now = Time.realtimeSinceStartupAsDouble;
                bool fireDue = now >= nextFireAt;
                bool eligible = CanFireAt(target, 3.5f);
                bool fire = fireDue && eligible;
                if (fireDue) nextFireAt = now + 0.18d;
                TraceCombatDecision(target, fireDue, eligible, fire);
                AimAt(target, fire: fire, approach: true);
                yield return null;
            }
            yield return RecoverTeamDuringCombat(step);
            if (!initialized) yield break;
            AimAt(replica.PresentedPosition + Vector3.forward * 20f,
                fire: false);
            if (authority.WorldState.RemainingEnemyCount > 0)
            {
                Abort(step,
                    $"协作客户端未能清除剩余敌人：" +
                    $"remaining={authority.WorldState.RemainingEnemyCount};" +
                    $"ammo={replica.PresentedMagazineAmmo}/" +
                    $"{replica.PresentedReserveAmmo}。");
                yield break;
            }
            evidence.Passed(step, Tick(),
                "killed=" + authority.WorldState.KilledTargets);
        }

        private IEnumerator RecoverTeamDuringCombat(string parentStep)
        {
            if (replica.PresentedLifeState !=
                AuthoritativePlayerLifeState.Alive)
            {
                ReleaseInput();
                double revivedDeadline = Time.realtimeSinceStartupAsDouble +
                                         WaveClearTimeoutSeconds;
                while (replica.PresentedLifeState !=
                           AuthoritativePlayerLifeState.Alive &&
                       authority.WorldState.MissionPhase !=
                           AuthoritativeMissionPhase.Defeat &&
                       Time.realtimeSinceStartupAsDouble < revivedDeadline)
                    yield return null;
                if (replica.PresentedLifeState !=
                    AuthoritativePlayerLifeState.Alive)
                {
                    Abort(parentStep,
                        "协作角色倒地后未能由队友在时限内救起。");
                }
                yield break;
            }

            if (!TryGetDownedTeammate(out NetcodePlayerState teammate))
                yield break;

            // The deterministic clients have no tactical cover planner. A
            // revive hold while threats are still active repeatedly exposes
            // the only standing player and turns the acceptance run into a
            // random revive/down loop. Keep fighting until the encounter is
            // safe; the authoritative post-combat transition restores any
            // remaining downed squad member before terminal/extraction.
            if (authority.WorldState.RemainingEnemyCount > 0)
                yield break;

            const string reviveStep = "combat.revive-teammate";
            evidence.Started(reviveStep, Tick(),
                $"reviver={replica.PlayerId};downed={teammate.PlayerId}");
            yield return MoveTo(teammate.Position,
                Mathf.Max(0.75f, authority.WorldState.ReviveRadius * 0.6f),
                12d);
            double deadline = Time.realtimeSinceStartupAsDouble + 10d;
            while (TryGetPlayerState(teammate.PlayerId,
                       out NetcodePlayerState current) &&
                   current.LifeState == AuthoritativePlayerLifeState.Downed &&
                   replica.PresentedLifeState ==
                       AuthoritativePlayerLifeState.Alive &&
                   Time.realtimeSinceStartupAsDouble < deadline)
            {
                replica.SubmitMissionAction(
                    AuthoritativeMissionCommandKind.HoldRevive,
                    teammate.PlayerId);
                yield return new WaitForSecondsRealtime(0.05f);
            }
            if (TryGetPlayerState(teammate.PlayerId,
                    out NetcodePlayerState after) &&
                after.LifeState == AuthoritativePlayerLifeState.Downed)
            {
                Abort(parentStep,
                    $"未能救起倒地队友 {teammate.PlayerId}。");
                yield break;
            }
            evidence.Passed(reviveStep, Tick(),
                $"revived={teammate.PlayerId}");
        }

        private bool TryGetDownedTeammate(out NetcodePlayerState teammate)
        {
            for (int playerId = 1; playerId <= 2; playerId++)
            {
                if (playerId == replica.PlayerId ||
                    !TryGetPlayerState(playerId,
                        out NetcodePlayerState candidate) ||
                    candidate.LifeState !=
                        AuthoritativePlayerLifeState.Downed)
                    continue;
                teammate = candidate;
                return true;
            }
            teammate = default;
            return false;
        }

        private bool TryGetPlayerState(int playerId,
            out NetcodePlayerState player)
        {
            if (authority != null &&
                authority.TryGetPlayerState(playerId, out player))
                return true;
            player = default;
            return false;
        }

        private IEnumerator RallyForExtraction()
        {
            const string step = "mission.extraction-rally";
            evidence.Started(step, Tick(), "waiting-for-terminal");
            yield return WaitUntil(() =>
                    authority.WorldState.MissionPhase ==
                        AuthoritativeMissionPhase.Extraction ||
                    authority.WorldState.MissionPhase ==
                        AuthoritativeMissionPhase.Victory,
                70d);
            if (authority.WorldState.MissionPhase ==
                AuthoritativeMissionPhase.Victory)
            {
                evidence.Passed(step, Tick(), "already-victorious");
                yield break;
            }
            if (authority.WorldState.MissionPhase !=
                AuthoritativeMissionPhase.Extraction)
            {
                Abort(step, "等待终端完成时没有进入撤离阶段。");
                yield break;
            }

            Vector3 extraction = authority.WorldState.ExtractionPosition;
            yield return MoveTo(extraction, 4f, 36d);
            float distance = Vector3.Distance(
                replica.PresentedPosition, extraction);
            if (distance > authority.WorldState.ExtractionRadius)
            {
                Abort(step, $"协作客户端未进入撤离区：distance={distance:F1};" +
                    lastNavigationTrace);
                yield break;
            }
            evidence.Passed(step, Tick(), $"distance={distance:F1}");
        }

        private IEnumerator ExerciseEconomy()
        {
            yield return PresentStep(Issue100AcceptanceSteps.InventoryPickup);
            NetcodeWorldDropState drop = default;
            bool found = false;
            float nearestDistance = float.PositiveInfinity;
            for (int index = 0; index < authority.ReplicatedWorldDropCount;
                 index++)
            {
                NetcodeWorldDropState candidate =
                    authority.GetReplicatedWorldDrop(index);
                if (!candidate.Available || candidate.OwnerPlayerId != 0 &&
                    candidate.OwnerPlayerId != replica.PlayerId) continue;
                if (!CanUseConsumable(candidate.ItemId.ToString())) continue;
                float distance = Vector3.Distance(
                    replica.PresentedPosition, candidate.Position);
                if (distance >= nearestDistance) continue;
                drop = candidate;
                found = true;
                nearestDistance = distance;
            }
            if (!found)
            {
                Abort(Issue100AcceptanceSteps.InventoryPickup,
                    "没有可由 Client A 拾取且当前适用的权威消耗品。");
                yield break;
            }
            yield return MoveTo(drop.Position, 2.5f, 24d);
            if (Vector3.Distance(replica.PresentedPosition, drop.Position) >
                3.25f)
            {
                Abort(Issue100AcceptanceSteps.InventoryPickup,
                    "无法移动到掉落物拾取范围。");
                yield break;
            }
            string pickupItemId = drop.ItemId.ToString();
            int quantityBeforePickup = CountOwnedItemQuantity(pickupItemId);
            NetcodeProgressionState pickupBaseline = Progression();
            NetcodeEconomyCommand pickup = replica.SubmitEconomyAction(AuthoritativeEconomyCommandKind.Pickup,
                entityId: drop.DropId,
                expectedDropRevision: drop.Revision,
                expectedItemId: pickupItemId);
            yield return WaitUntil(() =>
                authority.IsReplicatedSnapshotComplete &&
                Progression().AcknowledgedEconomySequence >= pickup.Sequence &&
                Progression().InventoryRevision > pickupBaseline.InventoryRevision &&
                CountOwnedItemQuantity(pickupItemId) == quantityBeforePickup + drop.Quantity, 5d);
            NetcodeProgressionState pickupAfter = Progression();
            int quantityAfterPickup = CountOwnedItemQuantity(pickupItemId);
            if (!authority.IsReplicatedSnapshotComplete ||
                pickupAfter.AcknowledgedEconomySequence < pickup.Sequence ||
                pickupAfter.InventoryRevision <= pickupBaseline.InventoryRevision ||
                quantityAfterPickup != quantityBeforePickup + drop.Quantity)
            {
                Abort(Issue100AcceptanceSteps.InventoryPickup,
                    $"拾取未成功结算：seq={pickup.Sequence};" +
                    $"ack={pickupAfter.AcknowledgedEconomySequence};" +
                    $"quantity={quantityBeforePickup}->{quantityAfterPickup}。");
                yield break;
            }
            evidence.Passed(Issue100AcceptanceSteps.InventoryPickup, Tick(),
                $"item={drop.ItemId};seq={pickup.Sequence};" +
                $"quantity={quantityBeforePickup}->{quantityAfterPickup}");

            yield return PresentStep(Issue100AcceptanceSteps.InventoryUse);
            NetcodeInventorySlotState owned = FindOwnedItem();
            if (owned.Quantity <= 0)
            {
                Abort(Issue100AcceptanceSteps.InventoryUse,
                    "权威背包没有当前可适用的消耗品，不能记录使用成功。");
                yield break;
            }
            yield return WaitUntil(() => Tick() >= Progression().NextConsumableUseTick, 5d);
            NetcodeProgressionState useBaseline = Progression();
            string usedItemId = owned.ItemId.ToString();
            int quantityBeforeUse = CountOwnedItemQuantity(usedItemId);
            NetcodeEconomyCommand use = replica.SubmitEconomyAction(AuthoritativeEconomyCommandKind.Use,
                sourceSlot: owned.SlotIndex, quantity: 1);
            yield return WaitUntil(() =>
                authority.IsReplicatedSnapshotComplete &&
                HasConfirmedItemUse(use.Sequence, useBaseline, Progression(),
                    quantityBeforeUse, CountOwnedItemQuantity(usedItemId)), 5d);
            NetcodeProgressionState useAfter = Progression();
            int quantityAfterUse = CountOwnedItemQuantity(usedItemId);
            if (!authority.IsReplicatedSnapshotComplete ||
                !HasConfirmedItemUse(use.Sequence, useBaseline, useAfter,
                    quantityBeforeUse, quantityAfterUse))
            {
                Abort(Issue100AcceptanceSteps.InventoryUse,
                    $"消耗品使用超时或被拒绝：seq={use.Sequence};" +
                    $"ack={useAfter.AcknowledgedEconomySequence};" +
                    $"revision={useBaseline.InventoryRevision}->{useAfter.InventoryRevision};" +
                    $"quantity={quantityBeforeUse}->{quantityAfterUse}。");
                yield break;
            }
            evidence.Passed(Issue100AcceptanceSteps.InventoryUse, Tick(),
                $"item={owned.ItemId};seq={use.Sequence};" +
                $"quantity={quantityBeforeUse}->{quantityAfterUse}");

            yield return PresentStep(Issue100AcceptanceSteps.UpgradeSelect);
            NetcodeProgressionState progression = Progression();
            if (progression.PendingUpgradeChoices <= 0)
            {
                Abort(Issue100AcceptanceSteps.UpgradeSelect,
                    "没有待选升级，已有升级不能证明本次选择成功。");
                yield break;
            }
            int candidateIndex = SelectApplicableUpgradeCandidate(progression);
            string candidateId = candidateIndex switch
            {
                0 => progression.Candidate0.ToString(),
                1 => progression.Candidate1.ToString(),
                _ => progression.Candidate2.ToString()
            };
            int candidateLevelBefore = CountOwnedUpgradeLevel(candidateId);
            NetcodeEconomyCommand selected = replica.SubmitEconomyAction(
                AuthoritativeEconomyCommandKind.SelectUpgrade,
                candidateIndex: candidateIndex);
            yield return WaitUntil(() =>
                authority.IsReplicatedSnapshotComplete &&
                HasConfirmedUpgradeSelection(selected.Sequence, progression,
                    Progression(), candidateLevelBefore, CountOwnedUpgradeLevel(candidateId)), 5d);
            NetcodeProgressionState upgradeAfter = Progression();
            int candidateLevelAfter = CountOwnedUpgradeLevel(candidateId);
            if (!authority.IsReplicatedSnapshotComplete ||
                !HasConfirmedUpgradeSelection(selected.Sequence, progression,
                    upgradeAfter, candidateLevelBefore, candidateLevelAfter))
            {
                Abort(Issue100AcceptanceSteps.UpgradeSelect,
                    $"本次升级超时或未应用：seq={selected.Sequence};" +
                    $"ack={upgradeAfter.AcknowledgedEconomySequence};candidate={candidateId};" +
                    $"level={candidateLevelBefore}->{candidateLevelAfter};" +
                    $"pending={progression.PendingUpgradeChoices}->{upgradeAfter.PendingUpgradeChoices}。");
                yield break;
            }
            evidence.Passed(Issue100AcceptanceSteps.UpgradeSelect, Tick(),
                $"upgradeStacks={CountOwnedUpgrades()};seq={selected.Sequence};" +
                $"candidate={candidateId};level={candidateLevelBefore}->{candidateLevelAfter}");
        }

        private static bool HasConfirmedItemUse(uint requestedSequence,
            NetcodeProgressionState before, NetcodeProgressionState after,
            int quantityBefore, int quantityAfter) =>
            requestedSequence > before.AcknowledgedEconomySequence &&
            after.AcknowledgedEconomySequence >= requestedSequence &&
            after.InventoryRevision > before.InventoryRevision &&
            quantityBefore > 0 && quantityAfter == quantityBefore - 1;

        private static bool HasConfirmedUpgradeSelection(uint requestedSequence,
            NetcodeProgressionState before, NetcodeProgressionState after,
            int levelsBefore, int levelsAfter) =>
            requestedSequence > before.AcknowledgedEconomySequence &&
            after.AcknowledgedEconomySequence >= requestedSequence &&
            before.PendingUpgradeChoices > 0 &&
            (levelsAfter > levelsBefore ||
             after.PendingUpgradeChoices < before.PendingUpgradeChoices);

        private static int SelectApplicableUpgradeCandidate(
            NetcodeProgressionState progression)
        {
            string[] candidates =
            {
                progression.Candidate0.ToString(),
                progression.Candidate1.ToString(),
                progression.Candidate2.ToString()
            };
            for (int index = 0; index < candidates.Length; index++)
            {
                string candidate = candidates[index];
                if (string.IsNullOrEmpty(candidate)) continue;
                if (candidate == "survival_emergency_treatment" ||
                    candidate == "survival_field_armor_repair")
                    continue;
                return index;
            }
            return 0;
        }

        private IEnumerator CompleteMission()
        {
            yield return PresentStep(Issue100AcceptanceSteps.MissionTerminal);
            yield return WaitUntil(() => authority.WorldState.MissionPhase ==
                AuthoritativeMissionPhase.ActivateTerminal, 5d);
            Vector3 terminal = authority.WorldState.TerminalPosition;
            Vector3 terminalApproach = ResolveInteractionApproach(
                terminal);
            Debug.Log($"[ISSUE100][TERMINAL_APPROACH] " +
                      $"from={replica.PresentedPosition};" +
                      $"approach={terminalApproach};target={terminal}");
            if (Vector3.Distance(terminalApproach, terminal) >
                authority.WorldState.TerminalRadius)
            {
                yield return MoveTo(terminalApproach, 0.45f, 20d);
            }
            yield return MoveTo(terminal,
                Mathf.Max(1f,
                    authority.WorldState.TerminalRadius * 0.75f),
                18d);
            double terminalDeadline = Time.realtimeSinceStartupAsDouble + 12d;
            while (authority.WorldState.MissionPhase ==
                       AuthoritativeMissionPhase.ActivateTerminal &&
                   Time.realtimeSinceStartupAsDouble < terminalDeadline)
            {
                replica.SubmitMissionAction(
                    AuthoritativeMissionCommandKind.HoldTerminal);
                yield return new WaitForSecondsRealtime(0.05f);
            }
            if (authority.WorldState.MissionPhase !=
                AuthoritativeMissionPhase.Extraction)
            {
                Abort(Issue100AcceptanceSteps.MissionTerminal,
                    "终端交互没有推进到撤离阶段。");
                yield break;
            }
            evidence.Passed(Issue100AcceptanceSteps.MissionTerminal, Tick(),
                "phase=Extraction");

            yield return PresentStep(Issue100AcceptanceSteps.ReconnectRestore);
            double reconnectDeadline = Time.realtimeSinceStartupAsDouble + 20d;
            while (!GlobalStepPassed(Issue100AcceptanceSteps.ReconnectRestore) &&
                   Time.realtimeSinceStartupAsDouble < reconnectDeadline)
                yield return new WaitForSecondsRealtime(0.2f);
            if (!GlobalStepPassed(Issue100AcceptanceSteps.ReconnectRestore))
            {
                Abort(Issue100AcceptanceSteps.ReconnectRestore,
                    "没有收到 Client B 的重连恢复证据。");
                yield break;
            }

            yield return PresentStep(Issue100AcceptanceSteps.MissionExtraction);
            Vector3 extraction = authority.WorldState.ExtractionPosition;
            yield return MoveTo(extraction, 4f,
                options.RecordVideo ? 70d : 24d);
            float extractionDistance = Vector3.Distance(
                replica.PresentedPosition, extraction);
            if (extractionDistance > authority.WorldState.ExtractionRadius)
            {
                Abort(Issue100AcceptanceSteps.MissionExtraction,
                    $"录像客户端未进入撤离区：" +
                    $"distance={extractionDistance:F1};" +
                    lastNavigationTrace);
                yield break;
            }
            double extractionDeadline = Time.realtimeSinceStartupAsDouble +
                                        45d;
            while (authority.WorldState.MissionPhase ==
                       AuthoritativeMissionPhase.Extraction &&
                   Time.realtimeSinceStartupAsDouble < extractionDeadline)
            {
                replica.SubmitMissionAction(
                    AuthoritativeMissionCommandKind.StartExtraction);
                yield return new WaitForSecondsRealtime(0.05f);
            }
            if (authority.WorldState.MissionPhase !=
                AuthoritativeMissionPhase.Victory)
            {
                Abort(Issue100AcceptanceSteps.MissionExtraction,
                    "撤离交互没有产生胜利结算。");
                yield break;
            }
            evidence.Passed(Issue100AcceptanceSteps.MissionExtraction, Tick(),
                "phase=Victory");
            yield return PresentStep(Issue100AcceptanceSteps.MatchSettlement);
            evidence.Passed(Issue100AcceptanceSteps.MatchSettlement, Tick(),
                "outcome=" + authority.WorldState.MissionOutcomeReason);
            if (options.RecordVideo)
                yield return new WaitForSecondsRealtime(1.25f);
        }

        private IEnumerator PresentStep(string step, string detail = "")
        {
            evidence.Started(step, Tick(), detail);
            if (options.RecordVideo)
                yield return new WaitForSecondsRealtime(0.9f);
        }

        private IEnumerator FinalizeMetrics()
        {
            SetInputFrame(Vector2.zero, replica.PresentedAimYaw,
                replica.PresentedAimPitch, false, false, false, false, false);
            yield return new WaitForSecondsRealtime(3f);
            RecordAcknowledgements();
            while (acceptedCommands < sentCommands)
            {
                metrics.RecordCommandDroppedByCondition();
                acceptedCommands++;
            }

            DriverStatistics traffic = CurrentTraffic();
            long uplink = SaturatingDelta(traffic.TxTotalBytes,
                initialTraffic.TxTotalBytes);
            long downlink = SaturatingDelta(traffic.RxTotalBytes,
                initialTraffic.RxTotalBytes);
            metrics.RecordTraffic(uplink, downlink);
            NetworkDiagnosticScenarioResult result = metrics.Complete();
            evidence.WriteMetrics(ToMetricsRecord(result));
        }

        private void Update()
        {
            if (!initialized || metrics == null) return;
            metrics.Advance(Time.unscaledDeltaTime);
            if (network?.Transport != null && network.NetworkManager != null &&
                network.NetworkManager.IsConnectedClient)
            {
                rttSamples.Add(network.Transport.GetCurrentRtt(
                    NetworkManager.ServerClientId));
            }

            if (metricsBound) RecordAcknowledgements();
        }

        private void RecordAcknowledgements()
        {
            if (replica == null ||
                replica.PresentedAcknowledgedSequence <=
                highestAcknowledgedSequence)
                return;
            uint acknowledgement = replica.PresentedAcknowledgedSequence;
            uint rawDelta = acknowledgement - highestAcknowledgedSequence;
            int delta = rawDelta > int.MaxValue
                ? int.MaxValue
                : (int)rawDelta;
            int count = Math.Min(delta,
                Math.Max(0, sentCommands - acceptedCommands));
            for (int index = 0; index < count; index++)
                metrics.RecordCommandAccepted();
            acceptedCommands += count;
            highestAcknowledgedSequence = acknowledgement;
        }

        private void HandleCommandSubmitted(NetcodePlayerCommand command)
        {
            sentCommands++;
            metrics.RecordCommandSent();
            if (command.Fire)
                pendingShots[command.Sequence] =
                    Time.realtimeSinceStartupAsDouble;
        }

        private void HandleShotFeedback(NetcodeShotFeedbackEvent feedback)
        {
            if (!pendingShots.Remove(feedback.ShotCommandSequence,
                    out double started)) return;
            metrics.RecordHitFeedback((Time.realtimeSinceStartupAsDouble -
                started) * 1000d);
            if (feedback.DidHit) hitFeedbacks++;
            else if (feedback.Kind == ShotResolutionKind.Blocked)
                blockedFeedbacks++;
            else if (feedback.Kind == ShotResolutionKind.Miss)
                missedFeedbacks++;
        }

        private void HandlePredictionReconciled(long _,
            PredictionCorrection correction)
        {
            bool divergent = correction.WasCorrected;
            double now = Time.realtimeSinceStartupAsDouble;
            if (divergent && divergentSince < 0d) divergentSince = now;
            double duration = divergent && divergentSince >= 0d
                ? (now - divergentSince) * 1000d
                : 0d;
            metrics.RecordStateComparison(divergent,
                correction.ErrorDistance, duration);
            if (!divergent) divergentSince = -1d;
            if (correction.WasCorrected)
                metrics.RecordCorrection(correction.ErrorDistance);
        }

        private IEnumerator DriveFor(Vector2 movement, float seconds,
            bool sprint, bool crouch, bool aiming)
        {
            SetInputFrame(movement, replica.PresentedAimYaw,
                replica.PresentedAimPitch, false, false, sprint, crouch,
                aiming);
            yield return new WaitForSecondsRealtime(seconds);
            ReleaseInput();
            yield return new WaitForSecondsRealtime(0.1f);
        }

        private void ReleaseInput()
        {
            SetInputFrame(Vector2.zero, replica.PresentedAimYaw,
                replica.PresentedAimPitch, false, false, false, false, false);
        }

        private bool IsAuthoritativelyCrouching()
        {
            return authority != null && authority.TryGetPlayerState(
                       replica.PlayerId, out NetcodePlayerState state) &&
                   (PlayerStance)state.Stance == PlayerStance.Crouching;
        }

        private bool IsAuthoritativelyAiming()
        {
            return authority != null && authority.TryGetPlayerState(
                       replica.PlayerId, out NetcodePlayerState state) &&
                   state.Aiming;
        }

        private IEnumerator MoveTo(Vector3 target, float radius,
            double timeoutSeconds)
        {
            Vector3 navigationTarget = ResolveReachableDestination(
                target, radius);
            double deadline = Time.realtimeSinceStartupAsDouble +
                timeoutSeconds;
            var route = new NavMeshPath();
            Vector3[] corners = Array.Empty<Vector3>();
            int waypointIndex = 0;
            NavMeshPathStatus pathStatus = NavMeshPathStatus.PathInvalid;
            double nextRepathAt = 0d;
            double nextTraceAt = 0d;
            double progressCheckedAt = Time.realtimeSinceStartupAsDouble;
            Vector3 progressPosition = replica.PresentedPosition;
            int stallCount = 0;
            double recoveryUntil = 0d;
            Vector3 recoveryDirection = Vector3.zero;
            while (Vector3.Distance(replica.PresentedPosition, target) >
                       radius &&
                   Time.realtimeSinceStartupAsDouble < deadline)
            {
                double now = Time.realtimeSinceStartupAsDouble;
                Vector3 current = replica.PresentedPosition;
                if (Vector3.Distance(replica.PresentedPosition,
                        navigationTarget) <= 0.75f)
                {
                    navigationTarget = ResolveReachableDestination(
                        target, radius);
                    nextRepathAt = 0d;
                }

                if (now >= nextRepathAt)
                {
                    if (TryCalculateCompletePath(current,
                            navigationTarget, route))
                    {
                        corners = route.corners;
                        waypointIndex = 1;
                    }
                    else
                    {
                        corners = Array.Empty<Vector3>();
                        waypointIndex = 0;
                    }
                    pathStatus = route.status;
                    nextRepathAt = now + 2d;
                }

                waypointIndex = AdvanceWaypointIndex(current, corners,
                    waypointIndex, 0.8f);
                Vector3 waypoint = waypointIndex < corners.Length
                    ? corners[waypointIndex]
                    : navigationTarget;
                Vector3 direction = waypoint - current;
                direction.y = 0f;
                if (direction.sqrMagnitude <= 0.01f)
                    direction = navigationTarget - current;
                direction.y = 0f;
                direction = direction.sqrMagnitude > 0.01f
                    ? direction.normalized
                    : Vector3.forward;

                if (now - progressCheckedAt >= 2.5d)
                {
                    float traveled = Vector3.Distance(current,
                        progressPosition);
                    if (traveled < 0.3f)
                    {
                        stallCount++;
                        nextRepathAt = 0d;
                        TraceNavigation("stall", current, target,
                            navigationTarget, waypoint, route.status,
                            corners.Length, waypointIndex, traveled,
                            stallCount);
                        if (stallCount >= 2 &&
                            TryResolvePhysicalCombatRecovery(current, direction,
                                stallCount, out recoveryDirection))
                        {
                            recoveryUntil = now + 0.7d;
                            nextRepathAt = recoveryUntil;
                            TraceNavigation("sidestep", current, target,
                                navigationTarget,
                                current + recoveryDirection * 2f,
                                route.status, corners.Length,
                                waypointIndex, traveled, stallCount);
                        }
                    }
                    else
                        stallCount = 0;
                    progressPosition = current;
                    progressCheckedAt = now;
                }
                if (now < recoveryUntil)
                    direction = recoveryDirection;
                // NavMesh can step over CityNew's low walls, but the formal
                // player capsule cannot. Use the same physical route and
                // bounded, normal Jump input as combat navigation.
                bool jumpRequested = false;
                if (!CanWalkCombatDirection(current, direction))
                {
                    jumpRequested = CanJumpCombatObstacle(current, direction);
                    if (!jumpRequested)
                    {
                        nextRepathAt = 0d;
                        if (TryResolvePhysicalCombatRecovery(current, direction,
                                stallCount, out recoveryDirection))
                        {
                            direction = recoveryDirection;
                            recoveryUntil = now + 0.7d;
                            nextRepathAt = recoveryUntil;
                        }
                        else
                            direction = Vector3.zero;
                    }
                }
                if (now >= nextTraceAt)
                {
                    TraceNavigation("progress", current, target,
                        navigationTarget, waypoint, pathStatus,
                        corners.Length, waypointIndex, 0f, stallCount);
                    nextTraceAt = now + 3d;
                }
                float targetYaw = direction.sqrMagnitude > 0.01f
                    ? Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg
                    : replica.PresentedAimYaw;
                float yaw = Mathf.MoveTowardsAngle(
                    replica.PresentedAimYaw, targetYaw, 12f);
                Vector2 localMovement = WorldDirectionToLocal(
                    direction, yaw);
                SetInputFrame(localMovement, yaw, 0f,
                    false, jumpRequested, localMovement.y > 0.1f,
                    false, false);
                yield return null;
            }
            SetInputFrame(Vector2.zero, replica.PresentedAimYaw, 0f,
                false, false, false, false, false);
            TraceNavigation("end", replica.PresentedPosition, target,
                navigationTarget, navigationTarget, pathStatus,
                corners.Length, waypointIndex, 0f, stallCount);
            yield return new WaitForSecondsRealtime(0.15f);
        }

        private static bool TryCalculateCompletePath(Vector3 current,
            Vector3 target, NavMeshPath route)
        {
            return NavMesh.SamplePosition(current, out NavMeshHit from,
                       2.5f, NavMesh.AllAreas) &&
                   NavMesh.SamplePosition(target, out NavMeshHit to,
                       2.5f, NavMesh.AllAreas) &&
                   NavMesh.CalculatePath(from.position, to.position,
                       NavMesh.AllAreas, route) &&
                   route.status == NavMeshPathStatus.PathComplete &&
                   route.corners.Length > 1;
        }

        private static int AdvanceWaypointIndex(Vector3 current,
            Vector3[] corners, int next, float arrivalRadius)
        {
            if (corners == null || corners.Length == 0) return 0;
            next = Mathf.Clamp(next, 1, corners.Length);
            current.y = 0f;
            while (next < corners.Length)
            {
                Vector3 corner = corners[next];
                corner.y = 0f;
                if (Vector3.Distance(current, corner) <= arrivalRadius)
                {
                    next++;
                    continue;
                }
                Vector3 previous = corners[next - 1];
                previous.y = 0f;
                Vector3 segment = corner - previous;
                float lengthSquared = segment.sqrMagnitude;
                if (lengthSquared <= 0.001f) return next;
                float along = Vector3.Dot(current - previous, segment) /
                    lengthSquared;
                float lateral = Vector3.Distance(current,
                    previous + segment * along);
                if (along < 1f || lateral > arrivalRadius * 1.5f)
                    break;
                next++;
            }
            return next;
        }

        private static bool TryResolveRecoveryDirection(Vector3 current,
            Vector3 intended, int stallCount, out Vector3 direction)
        {
            direction = Vector3.zero;
            if (!NavMesh.SamplePosition(current, out NavMeshHit from,
                    2.5f, NavMesh.AllAreas))
                return false;
            Vector3 lateral = new Vector3(intended.z, 0f, -intended.x);
            float firstSide = stallCount % 2 == 0 ? 1f : -1f;
            for (int index = 0; index < 2; index++)
            {
                float side = index == 0 ? firstSide : -firstSide;
                Vector3 candidate = current + lateral * (side * 2f);
                if (!NavMesh.SamplePosition(candidate, out NavMeshHit to,
                        0.75f, NavMesh.AllAreas) ||
                    NavMesh.Raycast(from.position, to.position,
                        out _, NavMesh.AllAreas))
                    continue;
                Vector3 delta = to.position - current;
                delta.y = 0f;
                if (delta.sqrMagnitude < 0.25f) continue;
                direction = delta.normalized;
                return true;
            }
            return false;
        }

        private void TraceNavigation(string reason, Vector3 current,
            Vector3 target, Vector3 destination, Vector3 waypoint,
            NavMeshPathStatus status, int cornerCount, int waypointIndex,
            float traveled, int stallCount)
        {
            lastNavigationTrace =
                $"navReason={reason};position={current};" +
                $"target={target};destination={destination};" +
                $"waypoint={waypoint};path={status};" +
                $"corners={cornerCount};waypointIndex={waypointIndex};" +
                $"traveled={traveled:F2};stalls={stallCount};" +
                $"commandAck={replica.PresentedAcknowledgedSequence};" +
                $"sentCommands={sentCommands}";
            Debug.Log("[ISSUE100][NAV] " +
                      $"step={evidence.CurrentStep};" +
                      $"tick={Tick()};player={replica.PlayerId};" +
                      lastNavigationTrace);
        }

        private Vector3 ResolveReachableDestination(
            Vector3 target,
            float arrivalRadius)
        {
            Vector3 current = replica.PresentedPosition;
            if (!NavMesh.SamplePosition(current, out NavMeshHit from,
                    2.5f, NavMesh.AllAreas))
                return target;

            Vector3 best = target;
            float bestLength = float.PositiveInfinity;
            var path = new NavMeshPath();
            const int directions = 12;
            for (int index = -1; index < directions; index++)
            {
                float ring = Mathf.Max(0.5f, arrivalRadius * 0.8f);
                Vector3 candidate = index < 0
                    ? target
                    : target + new Vector3(
                        Mathf.Cos(index * Mathf.PI * 2f / directions) * ring,
                        0f,
                        Mathf.Sin(index * Mathf.PI * 2f / directions) * ring);
                if (!NavMesh.SamplePosition(candidate, out NavMeshHit sampled,
                        1.5f, NavMesh.AllAreas) ||
                    Vector3.Distance(sampled.position, target) >
                    arrivalRadius ||
                    !NavMesh.CalculatePath(from.position, sampled.position,
                        NavMesh.AllAreas, path) ||
                    path.status != NavMeshPathStatus.PathComplete ||
                    path.corners.Length < 2)
                    continue;
                float length = 0f;
                for (int corner = 1; corner < path.corners.Length; corner++)
                    length += Vector3.Distance(
                        path.corners[corner - 1], path.corners[corner]);
                if (length >= bestLength) continue;
                bestLength = length;
                best = sampled.position;
            }
            return best;
        }

        private Vector3 ResolveInteractionApproach(
            Vector3 target)
        {
            Vector3 current = replica.PresentedPosition;
            if (!NavMesh.SamplePosition(current, out NavMeshHit from,
                    2.5f, NavMesh.AllAreas))
                return current;

            var path = new NavMeshPath();
            Vector3 best = current;
            float bestLength = float.PositiveInfinity;
            float[] ringDistances = { 4.5f, 5.5f, 6.5f, 7.5f };
            const int directions = 72;
            foreach (float ringDistance in ringDistances)
            {
                for (int index = 0; index < directions; index++)
                {
                    float angle = index * Mathf.PI * 2f / directions;
                    Vector3 candidate = target + new Vector3(
                        Mathf.Cos(angle) * ringDistance,
                        0f,
                        Mathf.Sin(angle) * ringDistance);
                    if (!NavMesh.SamplePosition(candidate,
                            out NavMeshHit sampled,
                            1.25f, NavMesh.AllAreas) ||
                        !HasClearInteractionLine(sampled.position, target) ||
                        !NavMesh.CalculatePath(from.position,
                            sampled.position,
                            NavMesh.AllAreas, path) ||
                        path.status != NavMeshPathStatus.PathComplete ||
                        path.corners.Length < 2)
                        continue;

                    float length = 0f;
                    for (int corner = 1;
                         corner < path.corners.Length;
                         corner++)
                        length += Vector3.Distance(
                            path.corners[corner - 1],
                            path.corners[corner]);
                    if (length >= bestLength) continue;
                    bestLength = length;
                    best = sampled.position;
                }
            }
            return best;
        }

        private bool HasClearInteractionLine(Vector3 from, Vector3 target)
        {
            Vector3 origin = from + Vector3.up * PredictedShotOriginHeight;
            Vector3 destination = target;
            destination.y = origin.y;
            Vector3 delta = destination - origin;
            float distance = delta.magnitude;
            if (distance <= 0.1f) return true;
            int count = Physics.SphereCastNonAlloc(
                origin,
                0.28f,
                delta / distance,
                combatLineHits,
                Mathf.Max(0f, distance - 0.55f),
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);
            for (int index = 0; index < count; index++)
            {
                Collider collider = combatLineHits[index].collider;
                if (collider == null ||
                    collider.GetComponentInParent<NetworkPlayerReplica>() !=
                    null)
                    continue;
                return false;
            }
            return true;
        }

        private void AimAt(Vector3 target, bool fire, bool approach = false)
        {
            Vector3 origin = replica.PresentedPosition + Vector3.up * 1.25f;
            Vector3 delta = target - origin;
            float horizontal = new Vector2(delta.x, delta.z).magnitude;
            float targetYaw = Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg;
            float targetPitch =
                -Mathf.Atan2(delta.y, horizontal) * Mathf.Rad2Deg;
            // The acceptance driver used to snap directly to each nearest
            // target. The authoritative anti-cheat correctly rejected those
            // synthetic turns as AimRateExceeded, leaving the bot unable to
            // shoot. Move by less than the 18°/tick server allowance so this
            // remains representative of a real mouse turn.
            float yaw = Mathf.MoveTowardsAngle(
                replica.PresentedAimYaw, targetYaw, 12f);
            float pitch = Mathf.MoveTowards(
                replica.PresentedAimPitch, targetPitch, 12f);
            if (!input.SetExclusivePredictedCombatFrame(
                    this, NetworkPresentationIds.RifleGameplay))
            {
                Abort("input.exclusive-combat-context",
                    "验收驱动失去了网络枪口上下文的独占权。 ");
                return;
            }
            bool clearLine = HasClearCombatLine(target);
            combatJumpRequested = false;
            SetInputFrame(approach
                    ? ResolveCombatMovement(
                        target, yaw, horizontal, clearLine)
                    : Vector2.zero,
                yaw, pitch, fire,
                combatJumpRequested, approach &&
                       (horizontal > 9f || !clearLine), false, true);
            // Off-screen recording can make rendered frames much slower than
            // the network tick accumulator. Submit the requested shot now so
            // one coroutine fire sample always maps to one real command,
            // instead of being coalesced in the queued-fire boolean.
            if (fire && initialized)
                input.TrySubmitCurrentFrame(out _);
        }

        private Vector2 ResolveCombatMovement(
            Vector3 target,
            float aimYaw,
            float targetDistance,
            bool clearLine)
        {
            if (!clearLine)
                return WorldDirectionToLocal(
                    ResolveNavigationDirection(target), aimYaw);

            // Keep both bots on the same circling direction so they remain
            // close enough to exercise the cooperative revive loop instead
            // of drifting to opposite sides of CityNew.
            const float side = 0.55f;
            Vector2 movement;
            if (replica.PresentedHealth <= 35f)
                movement = new Vector2(side, -1f).normalized;
            else if (targetDistance > 9f)
                movement = new Vector2(side * 0.25f, 0.97f).normalized;
            else if (targetDistance < 7f)
                movement = new Vector2(side, -0.76f).normalized;
            else
                movement = new Vector2(side, 0.15f).normalized;
            Vector3 preferred = Quaternion.Euler(0f, aimYaw, 0f) *
                new Vector3(movement.x, 0f, movement.y);
            if (CanWalkCombatDirection(replica.PresentedPosition, preferred))
                return movement;
            if (CanJumpCombatObstacle(replica.PresentedPosition, preferred))
            {
                combatJumpRequested = true;
                return movement;
            }
            return WorldDirectionToLocal(ResolveNavigationDirection(target), aimYaw);
        }

        private Vector3 ResolveNavigationDirection(Vector3 target)
        {
            navigationPath ??= new NavMeshPath();
            Vector3 current = replica.PresentedPosition;
            double now = Time.realtimeSinceStartupAsDouble;
            if (combatProgressCheckedAt < 0d)
            {
                combatProgressPosition = current;
                combatProgressCheckedAt = now;
            }
            if (now - combatProgressCheckedAt >= 1d)
            {
                float traveled = Vector3.Distance(current, combatProgressPosition);
                combatStallCount = traveled < 0.15f ? combatStallCount + 1 : 0;
                if (combatStallCount > 0) combatNextRepathAt = 0d;
                combatProgressPosition = current;
                combatProgressCheckedAt = now;
                if (evidence != null)
                    TraceNavigation("combat-progress", current, target, target,
                        combatWaypointIndex < combatCorners.Length ? combatCorners[combatWaypointIndex] : target,
                        navigationPath.status, combatCorners.Length, combatWaypointIndex,
                        traveled, combatStallCount);
            }
            if (now >= combatNextRepathAt || (target - combatRouteTarget).sqrMagnitude > 1f)
            {
                combatCorners = TryCalculateCompletePath(current, target, navigationPath)
                    ? navigationPath.corners : Array.Empty<Vector3>();
                combatWaypointIndex = combatCorners.Length > 1 ? 1 : 0;
                combatRouteTarget = target;
                combatNextRepathAt = now + 1.5d;
            }
            combatWaypointIndex = AdvanceWaypointIndex(current, combatCorners,
                combatWaypointIndex, 0.4f);
            Vector3 waypoint = combatWaypointIndex < combatCorners.Length
                ? combatCorners[combatWaypointIndex] : target;
            Vector3 intended = waypoint - current;
            intended.y = 0f;
            if (intended.sqrMagnitude <= 0.01f) return Vector3.zero;
            intended.Normalize();
            if (now < combatRecoveryUntil && CanWalkCombatDirection(current, combatRecoveryDirection))
                return combatRecoveryDirection;
            // A Partial path is never consumed as a complete route. Its direct
            // fallback and every real corner are subject to the same authority
            // capsule sweep, including the low obstacles NavMesh can step over.
            // Historical stalls request a replan above, but cannot veto a
            // presently safe route forever while waiting for movement to clear them.
            if (CanWalkCombatDirection(current, intended))
                return intended;
            if (CanJumpCombatObstacle(current, intended))
            {
                combatJumpRequested = true;
                return intended;
            }
            if (TryResolvePhysicalCombatRecovery(current, intended, combatStallCount,
                    out Vector3 recovery))
            {
                combatRecoveryDirection = recovery;
                combatRecoveryUntil = now + 0.7d;
                combatNextRepathAt = combatRecoveryUntil;
                return recovery;
            }
            return Vector3.zero;
        }

        private bool CanWalkCombatDirection(Vector3 current, Vector3 direction)
        {
            Vector3 requested = direction.normalized * CombatProbeDistance;
            Vector3 resolved = combatMovementCollision.ResolveHorizontalDisplacement(current, requested);
            return (resolved - requested).sqrMagnitude < 0.0004f;
        }

        private bool TryResolvePhysicalCombatRecovery(Vector3 current, Vector3 intended,
            int stallCount, out Vector3 direction)
        {
            direction = Vector3.zero;
            // Retain MoveTo's deterministic side preference, but do not accept
            // its NavMesh-only recovery unless the actual player capsule fits.
            if (TryResolveRecoveryDirection(current, intended, stallCount,
                    out Vector3 lateral) && CanWalkCombatDirection(current, lateral))
            {
                direction = lateral;
                return true;
            }
            if (!NavMesh.SamplePosition(current, out NavMeshHit from, 2.5f, NavMesh.AllAreas))
                return false;
            float firstSide = stallCount % 2 == 0 ? 1f : -1f;
            for (int angleIndex = 0; angleIndex < 3; angleIndex++)
            for (int sideIndex = 0; sideIndex < 2; sideIndex++)
            {
                float side = sideIndex == 0 ? firstSide : -firstSide;
                Vector3 candidate = Quaternion.Euler(0f, side * (45f + angleIndex * 45f), 0f) * intended;
                Vector3 destination = current + candidate * 1.25f;
                if (!NavMesh.SamplePosition(destination, out NavMeshHit to, 0.4f, NavMesh.AllAreas) ||
                    (to.position - destination).sqrMagnitude > 0.16f ||
                    NavMesh.Raycast(from.position, to.position, out _, NavMesh.AllAreas) ||
                    !CanWalkCombatDirection(current, candidate))
                    continue;
                direction = candidate.normalized;
                return true;
            }
            return false;
        }

        private bool CanJumpCombatObstacle(Vector3 current, Vector3 direction)
        {
            if (!replica.PresentedGrounded || authority?.Rules == null ||
                direction.sqrMagnitude < 0.01f || CanWalkCombatDirection(current, direction))
                return false;
            direction.Normalize();
            // Jump is a normal input, never a step-up/teleport. Limit it to a
            // low obstacle with clear full jump-apex headroom and a real landing
            // at the server's existing ground level (vertical terrain is not
            // invented by this acceptance driver).
            const float lowObstacleClearance = 0.7f;
            const float landingDistance = 1.6f;
            Vector3 raised = current + Vector3.up * lowObstacleClearance;
            Vector3 crossing = direction * landingDistance;
            if ((combatMovementCollision.ResolveHorizontalDisplacement(raised, crossing) - crossing)
                .sqrMagnitude > 0.0004f)
                return false;
            float apex = (float)(authority.Rules.JumpSpeed * authority.Rules.JumpSpeed /
                (2d * authority.Rules.Gravity));
            Vector3 bottom = current + Vector3.up *
                (CoopPlayerMovementCollision.CapsuleRadius + CoopPlayerMovementCollision.CapsuleSkin);
            Vector3 top = current + Vector3.up *
                (CoopPlayerMovementCollision.CapsuleHeight - CoopPlayerMovementCollision.CapsuleRadius -
                 CoopPlayerMovementCollision.CapsuleSkin);
            if (HasWorldCapsuleHit(bottom, top, Vector3.up, apex + CoopPlayerMovementCollision.CapsuleSkin) ||
                HasWorldCapsuleHit(bottom + crossing, top + crossing, Vector3.up,
                    apex + CoopPlayerMovementCollision.CapsuleSkin) ||
                HasWorldCapsuleHit(bottom + Vector3.up * apex, top + Vector3.up * apex,
                    direction, landingDistance))
                return false;
            Vector3 landing = current + crossing;
            int count = Physics.RaycastNonAlloc(landing + Vector3.up * 0.9f, Vector3.down,
                combatLineHits, 1.4f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            RaycastHit? ground = null;
            for (int index = 0; index < count; index++)
            {
                RaycastHit hit = combatLineHits[index];
                if (!IsCombatWorldCollider(hit.collider)) continue;
                if (ground == null || hit.distance < ground.Value.distance) ground = hit;
            }
            return ground != null && ground.Value.normal.y >= 0.7f &&
                Mathf.Abs(ground.Value.point.y - current.y) <= 0.25f;
        }

        private bool HasWorldCapsuleHit(Vector3 bottom, Vector3 top, Vector3 direction, float distance)
        {
            int count = Physics.CapsuleCastNonAlloc(bottom, top, CoopPlayerMovementCollision.CapsuleRadius,
                direction, combatLineHits, distance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int index = 0; index < count; index++)
                if (IsCombatWorldCollider(combatLineHits[index].collider)) return true;
            return false;
        }

        private static bool IsCombatWorldCollider(Collider value) => value != null &&
            value.GetComponentInParent<NetworkPlayerReplica>() == null &&
            value.GetComponentInParent<CharacterController>() == null;

        private static Vector2 WorldDirectionToLocal(
            Vector3 worldDirection,
            float aimYaw)
        {
            float yaw = aimYaw * Mathf.Deg2Rad;
            float localX = Mathf.Cos(yaw) * worldDirection.x -
                           Mathf.Sin(yaw) * worldDirection.z;
            float localZ = Mathf.Sin(yaw) * worldDirection.x +
                           Mathf.Cos(yaw) * worldDirection.z;
            return Vector2.ClampMagnitude(new Vector2(localX, localZ), 1f);
        }

        private void SetInputFrame(
            Vector2 move,
            float yaw,
            float pitch,
            bool fire,
            bool jump,
            bool sprint,
            bool crouch,
            bool aiming)
        {
            if (input == null || !input.SetExclusiveInputFrame(
                    this, move, yaw, pitch, fire, jump, sprint, crouch,
                    aiming))
            {
                Abort("input.exclusive-control",
                    "验收驱动失去了网络输入独占权。");
            }
        }

        private Vector3 ResolveTargetPoint(bool lockTarget = false)
        {
            if (lockTarget && lockedCombatTargetId > 0 &&
                TryGetAliveTarget(lockedCombatTargetId,
                    out NetcodeTargetState locked) &&
                HasClearCombatLine(AimPoint(locked)))
                return AimPoint(locked);

            lockedCombatTargetId = 0;

            NetcodeTargetState? selected = null;
            NetcodeTargetState? nearestFallback = null;
            float fallbackDistance = float.PositiveInfinity;
            for (int index = 0; index < authority.ReplicatedTargetCount;
                 index++)
            {
                NetcodeTargetState target =
                    authority.GetReplicatedTarget(index);
                if (!target.Active || target.Health <= 0f) continue;
                float candidate = Vector3.Distance(replica.PresentedPosition,
                    target.Position);
                if (candidate < fallbackDistance)
                {
                    fallbackDistance = candidate;
                    nearestFallback = target;
                }
                if (!HasClearCombatLine(AimPoint(target))) continue;
                if (selected.HasValue &&
                    target.TargetId >= selected.Value.TargetId)
                    continue;
                selected = target;
            }
            selected ??= nearestFallback;
            if (!selected.HasValue)
            {
                lockedCombatTargetId = 0;
                return replica.PresentedPosition + Vector3.forward * 30f;
            }

            if (lockTarget)
                lockedCombatTargetId = selected.Value.TargetId;
            return AimPoint(selected.Value);
        }

        private bool TryGetAliveTarget(int targetId,
            out NetcodeTargetState target)
        {
            for (int index = 0; index < authority.ReplicatedTargetCount;
                 index++)
            {
                target = authority.GetReplicatedTarget(index);
                if (target.TargetId == targetId && target.Active &&
                    target.Health > 0f)
                    return true;
            }
            target = default;
            return false;
        }

        private static Vector3 AimPoint(NetcodeTargetState target)
        {
            if (AuthoritativeHitGeometry.HasBox(NetcodeConversions.ToDomain(target.BodyHalfExtents)))
                return target.Position + Quaternion.Euler(0f, target.YawDegrees, 0f) * target.BodyOffset;
            // Legacy diagnostic targets have no calibration; only those keep
            // their original sphere aiming rule.
            return target.Position + Vector3.up * Mathf.Min(
                Mathf.Max(0.25f, target.Radius * 0.45f),
                target.Radius * 0.8f);
        }

        private bool IsAimAligned(Vector3 target, float toleranceDegrees)
        {
            Vector3 origin = replica.PresentedPosition + Vector3.up * 1.25f;
            Vector3 delta = target - origin;
            float horizontal = new Vector2(delta.x, delta.z).magnitude;
            float targetYaw = Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg;
            float targetPitch =
                -Mathf.Atan2(delta.y, horizontal) * Mathf.Rad2Deg;
            return Mathf.Abs(Mathf.DeltaAngle(
                       replica.PresentedAimYaw, targetYaw)) <=
                   toleranceDegrees &&
                   Mathf.Abs(replica.PresentedAimPitch - targetPitch) <=
                   toleranceDegrees;
        }

        private bool CanFireAt(Vector3 target, float toleranceDegrees) =>
            IsAimAligned(target, toleranceDegrees) &&
            HasClearCombatLine(target);

        private void TraceCombatDecision(Vector3 target, bool fireDue,
            bool eligible, bool fireRequested)
        {
            int generation = authority.WorldState.RunGeneration;
            if (combatDecisionRunGeneration != generation)
            {
                combatDecisionRunGeneration = generation;
                combatDecisionSampleCount = 0;
                nextCombatDecisionAt = 0d;
            }
            double now = Time.realtimeSinceStartupAsDouble;
            if (combatDecisionSampleCount >= 128 || now < nextCombatDecisionAt)
                return;
            combatDecisionSampleCount++;
            nextCombatDecisionAt = now + 1d;
            Vector3 origin = replica.PresentedPosition + Vector3.up * PredictedShotOriginHeight;
            Vector3 delta = target - origin;
            float horizontal = new Vector2(delta.x, delta.z).magnitude;
            float targetYaw = Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg;
            float targetPitch = -Mathf.Atan2(delta.y, horizontal) * Mathf.Rad2Deg;
            NetcodePlayerCommand lastWire = input.LastSubmittedCommand;
            // fireRequested is this decision, not a claim that RPC delivery
            // succeeded. The separately identified last actual wire command
            // lets process evidence distinguish aiming from input delivery.
            Debug.Log("[ISSUE100][COMBAT] " +
                $"step={evidence.CurrentStep};tick={Tick()};run={generation};player={replica.PlayerId};" +
                $"position={replica.PresentedPosition};targetId={lockedCombatTargetId};target={target};" +
                $"yaw={replica.PresentedAimYaw:F3};pitch={replica.PresentedAimPitch:F3};" +
                $"targetYaw={targetYaw:F3};targetPitch={targetPitch:F3};" +
                $"yawError={Mathf.Abs(Mathf.DeltaAngle(replica.PresentedAimYaw, targetYaw)):F3};" +
                $"pitchError={Mathf.Abs(replica.PresentedAimPitch - targetPitch):F3};" +
                $"clear={HasClearCombatLine(target)};eligible={eligible};fireDue={fireDue};fireRequested={fireRequested};" +
                $"inputTick={input.ClientTick};wireHas={input.HasSubmittedCommand};" +
                $"wireTick={lastWire.ClientTick};wireSeq={lastWire.Sequence};wireFire={lastWire.Fire}");
        }

        private bool HasClearCombatLine(Vector3 target)
        {
            Vector3 origin = replica.PresentedPosition + Vector3.up *
                PredictedShotOriginHeight;
            Vector3 delta = target - origin;
            float distance = delta.magnitude;
            if (distance <= 0.05f) return true;
            int count = Physics.RaycastNonAlloc(
                origin,
                delta / distance,
                combatLineHits,
                distance - 0.05f,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);
            for (int index = 0; index < count; index++)
            {
                Collider collider = combatLineHits[index].collider;
                if (!IsCombatWorldCollider(collider))
                    continue;
                return false;
            }
            return true;
        }

        private int CountOwnedItems()
        {
            int count = 0;
            for (int index = 0;
                 index < authority.ReplicatedInventorySlotCount; index++)
            {
                NetcodeInventorySlotState slot =
                    authority.GetReplicatedInventorySlot(index);
                if (slot.PlayerId == replica.PlayerId && slot.Quantity > 0)
                    count++;
            }
            return count;
        }

        private int CountOwnedItemQuantity(string itemId)
        {
            int quantity = 0;
            for (int index = 0; index < authority.ReplicatedInventorySlotCount; index++)
            {
                NetcodeInventorySlotState slot = authority.GetReplicatedInventorySlot(index);
                if (slot.PlayerId == replica.PlayerId && slot.ItemId.ToString() == itemId)
                    quantity += slot.Quantity;
            }
            return quantity;
        }

        private NetcodeInventorySlotState FindOwnedItem()
        {
            for (int index = 0;
                 index < authority.ReplicatedInventorySlotCount; index++)
            {
                NetcodeInventorySlotState slot =
                    authority.GetReplicatedInventorySlot(index);
                if (slot.PlayerId == replica.PlayerId && slot.Quantity > 0 &&
                    CanUseConsumable(slot.ItemId.ToString()))
                    return slot;
            }
            return default;
        }

        private int CountOwnedUpgrades()
        {
            int count = 0;
            for (int index = 0; index < authority.ReplicatedUpgradeCount;
                 index++)
            {
                if (authority.GetReplicatedUpgrade(index).PlayerId ==
                    replica.PlayerId) count++;
            }
            return count;
        }

        private bool CanUseConsumable(string itemId) => itemId switch
        {
            "medical_kit" or "medkit" => replica.PresentedHealth < replica.PresentedMaximumHealth,
            "armor_pack" or "armor_plate" => replica.PresentedArmor < replica.PresentedMaximumArmor,
            "rifle_ammo" or "handgun_ammo" => true,
            _ => false
        };

        private int CountOwnedUpgradeLevel(string upgradeId)
        {
            int levels = 0;
            for (int index = 0; index < authority.ReplicatedUpgradeCount; index++)
            {
                NetcodeUpgradeStackState stack = authority.GetReplicatedUpgrade(index);
                if (stack.PlayerId == replica.PlayerId && stack.UpgradeId.ToString() == upgradeId)
                    levels += stack.Level;
            }
            return levels;
        }

        private NetcodeProgressionState Progression()
        {
            return authority.TryGetProgression(replica.PlayerId,
                out NetcodeProgressionState value)
                ? value
                : default;
        }

        private IEnumerator WaitUntil(Func<bool> predicate,
            double timeoutSeconds)
        {
            double deadline = Time.realtimeSinceStartupAsDouble +
                timeoutSeconds;
            while (!predicate() &&
                   Time.realtimeSinceStartupAsDouble < deadline)
                yield return null;
        }

        private static IEnumerator WaitTask(Task task)
        {
            while (!task.IsCompleted) yield return null;
        }

        private bool GlobalStepPassed(string step)
        {
            string scenarioRoot = System.IO.Directory.GetParent(
                options.OutputDirectory)?.FullName ?? string.Empty;
            if (string.IsNullOrEmpty(scenarioRoot)) return false;
            string[] timelines = System.IO.Directory.GetFiles(scenarioRoot,
                "timeline.ndjson", System.IO.SearchOption.AllDirectories);
            string marker = "\"step\":\"" + step + "\"";
            string passed = "\"state\":\"passed\"";
            for (int index = 0; index < timelines.Length; index++)
            {
                try
                {
                    string[] lines = System.IO.File.ReadAllLines(
                        timelines[index]);
                    for (int lineIndex = 0; lineIndex < lines.Length;
                         lineIndex++)
                    {
                        string line = lines[lineIndex];
                        if (line.Contains(marker, StringComparison.Ordinal) &&
                            line.Contains(passed, StringComparison.Ordinal))
                            return true;
                    }
                }
                catch (System.IO.IOException)
                {
                    // A peer may currently be appending; retry next frame.
                }
            }
            return false;
        }

        private void WriteReconnectBaseline()
        {
            var baseline = new Issue100RuntimeSnapshot
            {
                serverTick = Tick(),
                simulationPlayerId = replica.PlayerId,
                missionPhase = authority.WorldState.MissionPhase.ToString(),
                waveStatus = authority.WorldState.WaveStatus.ToString(),
                remainingEnemies = authority.WorldState.RemainingEnemyCount,
                positionX = replica.PresentedPosition.x,
                positionY = replica.PresentedPosition.y,
                positionZ = replica.PresentedPosition.z,
                health = replica.PresentedHealth,
                armor = replica.PresentedArmor,
                magazineAmmo = replica.PresentedMagazineAmmo,
                reserveAmmo = replica.PresentedReserveAmmo
            };
            evidence.WriteSnapshot(baseline);
        }

        private DriverStatistics CurrentTraffic()
        {
            if (network?.Transport == null) return default;
            ref NetworkDriver driver = ref network.Transport.GetNetworkDriver();
            return driver.IsCreated ? driver.GetStatistics() : default;
        }

        private Issue100ClientMetricsRecord ToMetricsRecord(
            NetworkDiagnosticScenarioResult result)
        {
            double meanRtt = rttSamples.Count == 0
                ? 0d
                : rttSamples.Average();
            double maxRtt = rttSamples.Count == 0
                ? 0d
                : rttSamples.Max();
            return new Issue100ClientMetricsRecord
            {
                configuredRttMilliseconds =
                    options.Scenario.RoundTripLatencyMilliseconds,
                configuredJitterMilliseconds =
                    options.Scenario.JitterMilliseconds,
                configuredPacketLossBasisPoints =
                    options.Scenario.PacketLossBasisPoints,
                durationSeconds = result.DurationSeconds,
                hitFeedbackSampleCount = result.HitFeedbackSampleCount,
                meanHitFeedbackMilliseconds =
                    result.MeanHitFeedbackMilliseconds,
                p95HitFeedbackMilliseconds =
                    result.P95HitFeedbackMilliseconds,
                p99HitFeedbackMilliseconds =
                    result.P99HitFeedbackMilliseconds,
                correctionCount = result.CorrectionCount,
                correctionsPerMinute = result.CorrectionsPerMinute,
                meanCorrectionMagnitude = result.MeanCorrectionMagnitude,
                p95CorrectionMagnitude = result.P95CorrectionMagnitude,
                maximumCorrectionMagnitude =
                    result.MaximumCorrectionMagnitude,
                uplinkBytes = result.UplinkBytes,
                downlinkBytes = result.DownlinkBytes,
                uplinkBytesPerSecond = result.UplinkBytesPerSecond,
                downlinkBytesPerSecond = result.DownlinkBytesPerSecond,
                stateComparisonCount = result.StateComparisonCount,
                stateDivergenceCount = result.StateDivergenceCount,
                stateDivergenceRate = result.StateDivergenceRate,
                maximumStateDivergenceMagnitude =
                    result.MaximumStateDivergenceMagnitude,
                maximumStateDivergenceDurationMilliseconds =
                    result.MaximumStateDivergenceDurationMilliseconds,
                sentCommandCount = result.SentCommandCount,
                acceptedCommandCount = result.AcceptedCommandCount,
                droppedCommandCount = result.DroppedCommandCount,
                rejectedCommandCount = result.RejectedCommandCount,
                meanTransportRttMilliseconds = meanRtt,
                maximumTransportRttMilliseconds = maxRtt
            };
        }

        private long Tick() => authority?.WorldState.ServerTick ?? 0L;

        private void Abort(string step, string reason)
        {
            initialized = false;
            UnbindMetrics();
            runtime.Fail(step, reason);
        }

        private void OnDestroy()
        {
            initialized = false;
            UnbindMetrics();
        }

        private void UnbindMetrics()
        {
            metricsBound = false;
            if (input != null)
            {
                input.CommandSubmitted -= HandleCommandSubmitted;
                input.ReleaseExclusiveInput(this);
            }
            if (replica != null)
            {
                replica.ShotFeedbackReceived -= HandleShotFeedback;
                replica.PredictionReconciled -= HandlePredictionReconciled;
            }
        }

        private static long SaturatingDelta(ulong current, ulong previous) =>
            current <= previous
                ? 0L
                : current - previous > long.MaxValue
                    ? long.MaxValue
                    : (long)(current - previous);

        private static string SafeUsername(string value)
        {
            string normalized = value?.Trim() ?? "issue100-client";
            return normalized.Length <= 20
                ? normalized
                : normalized.Substring(0, 20);
        }
    }
}
