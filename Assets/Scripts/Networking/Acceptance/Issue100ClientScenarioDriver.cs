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

namespace FPS.Networking.Acceptance
{
    [DisallowMultipleComponent]
    internal sealed class Issue100ClientScenarioDriver : MonoBehaviour
    {
        private const string AcceptancePassword = "Issue100!Pass";
        private readonly Dictionary<uint, double> pendingShots = new();
        private readonly List<double> rttSamples = new();
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
                    "rtt-000-loss-00");
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
                    .FirstOrDefault(value => value.IsLocallyControlled &&
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
            evidence.Passed("reconnect.ready-to-disconnect",
                disconnectedAtTick,
                $"player={expectedPlayerId};tick={disconnectedAtTick}");

            UnbindMetrics();
            network.Shutdown();
            yield return WaitUntil(() => !network.IsListening, 5d);
            yield return new WaitForSecondsRealtime(0.35f);

            if (!CoopAdmissionEnvironment.TryCreateCodec(
                    out CoopConnectionTicketCodec codec, out string error))
            {
                Abort(Issue100AcceptanceSteps.ReconnectRestore, error);
                yield break;
            }
            string ticket = codec.Issue(options.AccountId, "local-dev",
                DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                matchId: options.MatchId);
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
                Abort(Issue100AcceptanceSteps.ReconnectRestore,
                    "重连后的生命、弹药或位置没有恢复权威状态。");
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
            int requiredFeedback = Math.Min(20, count);
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
                    ? ResolveTargetPoint()
                    : replica.PresentedPosition + Vector3.forward * 30f +
                      Vector3.up * 1.2f;
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
            if (targetEnemies &&
                options.Role == Issue100ProcessRole.ClientB &&
                hitFeedbacks <= hitsBefore)
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
            yield return new WaitForSecondsRealtime(1.6f);
            evidence.Passed(Issue100AcceptanceSteps.Reload, Tick(),
                $"magazine={magazineBefore}->{replica.PresentedMagazineAmmo}");
        }

        private IEnumerator CompleteWave()
        {
            evidence.Started(Issue100AcceptanceSteps.WaveComplete, Tick());
            int hitsBefore = hitFeedbacks;
            int missesBefore = missedFeedbacks;
            int blockedBefore = blockedFeedbacks;
            if (options.RecordVideo)
                yield return new WaitForSecondsRealtime(0.9f);
            double deadline = Time.realtimeSinceStartupAsDouble + 65d;
            while (authority.WorldState.RemainingEnemyCount > 0 &&
                   Time.realtimeSinceStartupAsDouble < deadline)
            {
                if (replica.PresentedLifeState !=
                    AuthoritativePlayerLifeState.Alive)
                {
                    Abort(Issue100AcceptanceSteps.WaveComplete,
                        "自动验收角色在清除波次时已被击倒。");
                    yield break;
                }
                if (replica.PresentedMagazineAmmo <= 1)
                {
                    if (replica.PresentedReserveAmmo <= 0) break;
                    input.SubmitPresentationAction(
                        NetworkPresentationAction.Reload);
                    yield return new WaitForSecondsRealtime(1.35f);
                    continue;
                }
                AimAt(ResolveTargetPoint(), fire: true, approach: true);
                yield return new WaitForSecondsRealtime(0.18f);
            }
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
            double deadline = Time.realtimeSinceStartupAsDouble + 65d;
            while (authority.WorldState.RemainingEnemyCount > 0 &&
                   Time.realtimeSinceStartupAsDouble < deadline)
            {
                if (replica.PresentedLifeState !=
                    AuthoritativePlayerLifeState.Alive)
                {
                    Abort(step, "协作客户端在清除波次时已被击倒。");
                    yield break;
                }
                if (replica.PresentedMagazineAmmo <= 1)
                {
                    if (replica.PresentedReserveAmmo <= 0) break;
                    input.SubmitPresentationAction(
                        NetworkPresentationAction.Reload);
                    yield return new WaitForSecondsRealtime(1.35f);
                    continue;
                }
                AimAt(ResolveTargetPoint(), fire: true, approach: true);
                yield return new WaitForSecondsRealtime(0.18f);
            }
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

        private IEnumerator RallyForExtraction()
        {
            const string step = "mission.extraction-rally";
            evidence.Started(step, Tick(), "waiting-for-terminal");
            yield return WaitUntil(() =>
                    authority.WorldState.MissionPhase ==
                        AuthoritativeMissionPhase.Extraction ||
                    authority.WorldState.MissionPhase ==
                        AuthoritativeMissionPhase.Victory,
                45d);
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
            yield return MoveTo(extraction, 4f, 24d);
            float distance = Vector3.Distance(
                replica.PresentedPosition, extraction);
            if (distance > authority.WorldState.ExtractionRadius)
            {
                Abort(step, $"协作客户端未进入撤离区：distance={distance:F1}。");
                yield break;
            }
            evidence.Passed(step, Tick(), $"distance={distance:F1}");
        }

        private IEnumerator ExerciseEconomy()
        {
            yield return PresentStep(Issue100AcceptanceSteps.InventoryPickup);
            NetcodeWorldDropState drop = default;
            bool found = false;
            for (int index = 0; index < authority.ReplicatedWorldDropCount;
                 index++)
            {
                NetcodeWorldDropState candidate =
                    authority.GetReplicatedWorldDrop(index);
                if (!candidate.Available || candidate.OwnerPlayerId != 0 &&
                    candidate.OwnerPlayerId != replica.PlayerId) continue;
                drop = candidate;
                found = true;
                break;
            }
            if (!found)
            {
                Abort(Issue100AcceptanceSteps.InventoryPickup,
                    "没有可由 Client A 拾取的权威掉落物。");
                yield break;
            }
            yield return MoveTo(drop.Position, 2.5f, 14d);
            if (Vector3.Distance(replica.PresentedPosition, drop.Position) >
                3.25f)
            {
                Abort(Issue100AcceptanceSteps.InventoryPickup,
                    "无法移动到掉落物拾取范围。");
                yield break;
            }
            int inventoryBefore = CountOwnedItems();
            replica.SubmitEconomyAction(AuthoritativeEconomyCommandKind.Pickup,
                entityId: drop.DropId,
                expectedDropRevision: drop.Revision,
                expectedItemId: drop.ItemId.ToString());
            yield return WaitUntil(() => CountOwnedItems() > inventoryBefore,
                5d);
            if (CountOwnedItems() <= inventoryBefore)
            {
                Abort(Issue100AcceptanceSteps.InventoryPickup,
                    "拾取命令未改变权威背包。");
                yield break;
            }
            evidence.Passed(Issue100AcceptanceSteps.InventoryPickup, Tick(),
                "item=" + drop.ItemId);

            yield return PresentStep(Issue100AcceptanceSteps.InventoryUse);
            NetcodeInventorySlotState owned = FindOwnedItem();
            uint ackBefore = Progression().AcknowledgedEconomySequence;
            replica.SubmitEconomyAction(AuthoritativeEconomyCommandKind.Use,
                sourceSlot: owned.SlotIndex, quantity: 1);
            yield return WaitUntil(() =>
                Progression().AcknowledgedEconomySequence > ackBefore, 5d);
            evidence.Passed(Issue100AcceptanceSteps.InventoryUse, Tick(),
                "item=" + owned.ItemId);

            yield return PresentStep(Issue100AcceptanceSteps.UpgradeSelect);
            NetcodeProgressionState progression = Progression();
            if (progression.PendingUpgradeChoices > 0)
            {
                int upgradesBefore = CountOwnedUpgrades();
                int candidateIndex = SelectApplicableUpgradeCandidate(
                    progression);
                replica.SubmitEconomyAction(
                    AuthoritativeEconomyCommandKind.SelectUpgrade,
                    candidateIndex: candidateIndex);
                yield return WaitUntil(() =>
                    CountOwnedUpgrades() > upgradesBefore, 5d);
            }
            if (CountOwnedUpgrades() <= 0)
            {
                Abort(Issue100AcceptanceSteps.UpgradeSelect,
                    "清敌经验没有产生并应用肉鸽升级。");
                yield break;
            }
            evidence.Passed(Issue100AcceptanceSteps.UpgradeSelect, Tick(),
                "upgradeStacks=" + CountOwnedUpgrades());
        }

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
            yield return MoveTo(authority.WorldState.TerminalPosition, 2.25f,
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
                    $"distance={extractionDistance:F1}。");
                yield break;
            }
            double extractionDeadline = Time.realtimeSinceStartupAsDouble + 14d;
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
            double deadline = Time.realtimeSinceStartupAsDouble +
                timeoutSeconds;
            while (Vector3.Distance(replica.PresentedPosition, target) >
                       radius &&
                   Time.realtimeSinceStartupAsDouble < deadline)
            {
                Vector3 delta = target - replica.PresentedPosition;
                float yaw = Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg;
                SetInputFrame(new Vector2(0f, 1f), yaw, 0f,
                    false, false, true, false, false);
                yield return null;
            }
            SetInputFrame(Vector2.zero, replica.PresentedAimYaw, 0f,
                false, false, false, false, false);
            yield return new WaitForSecondsRealtime(0.15f);
        }

        private void AimAt(Vector3 target, bool fire, bool approach = false)
        {
            Vector3 origin = replica.PresentedPosition + Vector3.up * 1.25f;
            Vector3 delta = target - origin;
            float horizontal = new Vector2(delta.x, delta.z).magnitude;
            float yaw = Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg;
            float pitch = -Mathf.Atan2(delta.y, horizontal) * Mathf.Rad2Deg;
            input.SetCombatFrame(NetworkPresentationIds.RifleGameplay, origin);
            SetInputFrame(approach
                    ? ResolveCombatMovement(horizontal)
                    : Vector2.zero,
                yaw, pitch, fire,
                false, false, false, true);
            // Off-screen recording can make rendered frames much slower than
            // the network tick accumulator. Submit the requested shot now so
            // one coroutine fire sample always maps to one real command,
            // instead of being coalesced in the queued-fire boolean.
            if (fire && initialized)
                input.TrySubmitCurrentFrame(out _);
        }

        private Vector2 ResolveCombatMovement(float targetDistance)
        {
            float side = options.Role == Issue100ProcessRole.ClientA
                ? -0.65f
                : 0.65f;
            if (targetDistance > 9f)
                return new Vector2(side * 0.25f, 0.97f).normalized;
            if (targetDistance < 5.5f)
                return new Vector2(side, -0.76f).normalized;
            return new Vector2(side, 0.15f).normalized;
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

        private Vector3 ResolveTargetPoint()
        {
            NetcodeTargetState? selected = null;
            float distance = float.PositiveInfinity;
            for (int index = 0; index < authority.ReplicatedTargetCount;
                 index++)
            {
                NetcodeTargetState target =
                    authority.GetReplicatedTarget(index);
                if (!target.Active || target.Health <= 0f) continue;
                float candidate = Vector3.Distance(replica.PresentedPosition,
                    target.Position);
                if (candidate >= distance) continue;
                distance = candidate;
                selected = target;
            }
            return selected.HasValue
                ? selected.Value.Position
                : replica.PresentedPosition + Vector3.forward * 30f;
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

        private NetcodeInventorySlotState FindOwnedItem()
        {
            for (int index = 0;
                 index < authority.ReplicatedInventorySlotCount; index++)
            {
                NetcodeInventorySlotState slot =
                    authority.GetReplicatedInventorySlot(index);
                if (slot.PlayerId == replica.PlayerId && slot.Quantity > 0)
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
