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
    public sealed class NetworkMovementCollisionParityTests
    {
        private readonly List<GameObject> objects = new();
        private static readonly NetVector3 Spawn = new(1000d, 0.16d, 1000d);

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            foreach (GameObject value in objects)
                if (value != null) Object.Destroy(value);
            objects.Clear();
            yield return null;
        }

        [Test]
        public void PredictedWallContactMatchesAuthorityAtEveryThreeTickAcknowledgement()
        {
            CreateWall(new Vector3(1000f, 2f, 1001.8f), new Vector3(6f, 4f, 0.2f));
            Physics.SyncTransforms();
            NetworkCoopSessionAuthority authority = CreateAuthority();
            NetworkPlayerReplica replica = CreateReplica(authority);
            double maximumSameTickError = 0d;
            int rejected = 0;
            for (int tick = 1; tick <= 180; tick++)
            {
                NetcodePlayerCommand command = replica.BuildPredictedCommand(
                    0f, 1f, 0f, 0f, false, tick);
                Assert.That(authority.TryQueueCommand(10, command, false), Is.True);
                AuthoritativeTickResult result = authority.ServerStep();
                rejected += result.Commands.Count(value => !value.Accepted);
                if (tick % 3 != 0) continue;
                AuthoritativePlayerState player = result.Snapshot.Player(1);
                maximumSameTickError = System.Math.Max(maximumSameTickError,
                    NetVector3.Distance(NetcodeConversions.ToDomain(replica.PresentedPosition),
                        player.Position));
                replica.ConsumeServerState(NetcodePlayerState.FromDomain(tick, player), true, tick);
            }

            Assert.That(maximumSameTickError, Is.LessThanOrEqualTo(0.02d),
                "对账前双方已处理相同输入Tick；本地预测不得穿墙再依赖服务器反复纠正。");
            Assert.That(rejected, Is.Zero, "相同墙体规则不得触发ImpossibleDisplacement。");
            Assert.That(replica.PredictionCorrectionCount, Is.Zero);
            Assert.That(replica.PresentedPosition.z, Is.InRange(1001.35f, 1001.4f));
        }

        [Test]
        public void DiagonalInputSlidesAlongWallWithoutPredictionCorrections()
        {
            CreateWall(new Vector3(1000f, 2f, 1001.8f), new Vector3(30f, 4f, 0.2f));
            Physics.SyncTransforms();
            NetworkCoopSessionAuthority authority = CreateAuthority();
            NetworkPlayerReplica replica = CreateReplica(authority);
            for (int tick = 1; tick <= 120; tick++)
            {
                AuthoritativeTickResult result = PredictAndStep(authority, replica, tick, 1f, 1f);
                if (tick % 3 != 0) continue;
                AuthoritativePlayerState player = result.Snapshot.Player(1);
                Assert.That(NetVector3.Distance(
                    NetcodeConversions.ToDomain(replica.PresentedPosition), player.Position),
                    Is.LessThanOrEqualTo(0.02d), "斜向输入撞墙后，两端应沿同一接触平面滑动。");
                replica.ConsumeServerState(NetcodePlayerState.FromDomain(tick, player), true, tick);
            }

            Assert.That(replica.PredictionCorrectionCount, Is.Zero);
            Assert.That(replica.PresentedPosition.x, Is.GreaterThan(1003f),
                "撞墙不得将平行于墙面的有效位移一并锁死。");
            Assert.That(replica.PresentedPosition.z, Is.InRange(1001.35f, 1001.4f));
        }

        [Test]
        public void InsideCornerStopsBothAxesWithMatchingAuthorityPosition()
        {
            CreateWall(new Vector3(1000f, 2f, 1001.8f), new Vector3(12f, 4f, 0.2f));
            CreateWall(new Vector3(1001.8f, 2f, 1000f), new Vector3(0.2f, 4f, 12f));
            Physics.SyncTransforms();
            NetworkCoopSessionAuthority authority = CreateAuthority();
            NetworkPlayerReplica replica = CreateReplica(authority);
            for (int tick = 1; tick <= 120; tick++)
            {
                AuthoritativeTickResult result = PredictAndStep(authority, replica, tick, 1f, 1f);
                if (tick % 3 != 0) continue;
                AuthoritativePlayerState player = result.Snapshot.Player(1);
                Assert.That(NetVector3.Distance(
                    NetcodeConversions.ToDomain(replica.PresentedPosition), player.Position),
                    Is.LessThanOrEqualTo(0.02d), "第二次撞面必须阻止从内拐角穿过墙体。");
                replica.ConsumeServerState(NetcodePlayerState.FromDomain(tick, player), true, tick);
            }

            Assert.That(replica.PredictionCorrectionCount, Is.Zero);
            Assert.That(replica.PresentedPosition.x, Is.InRange(1001.35f, 1001.4f));
            Assert.That(replica.PresentedPosition.z, Is.InRange(1001.35f, 1001.4f));
        }

        [Test]
        public void DelayedAcknowledgementReplaysPendingInputsAgainstTheSameWall()
        {
            CreateWall(new Vector3(1000f, 2f, 1001.8f), new Vector3(6f, 4f, 0.2f));
            Physics.SyncTransforms();
            NetworkCoopSessionAuthority authority = CreateAuthority();
            NetworkPlayerReplica replica = CreateReplica(authority);
            NetcodePlayerState delayed = default;
            AuthoritativePlayerState current = default;
            for (int tick = 1; tick <= 60; tick++)
            {
                AuthoritativeTickResult result = PredictAndStep(authority, replica, tick, 0f, 1f);
                current = result.Snapshot.Player(1);
                if (tick == 30)
                    delayed = NetcodePlayerState.FromDomain(tick, current);
            }

            replica.ConsumeServerState(delayed, true, 60d);

            Assert.That(replica.LastPredictionCorrection.ReplayedCommandCount, Is.EqualTo(30));
            Assert.That(replica.PendingPredictionCount, Is.EqualTo(30));
            Assert.That(NetVector3.Distance(replica.LastPredictionCorrection.ReplayTargetPosition,
                current.Position), Is.LessThanOrEqualTo(0.02d),
                "延迟ACK后的输入重放也要经过碰撞，不能仅实时预测碰撞正确。");
            Assert.That(replica.LastPredictionCorrection.WasCorrected, Is.False);
            Assert.That(replica.PresentedPosition.z, Is.InRange(1001.35f, 1001.4f));
        }

        [Test]
        public void DelayedTwentyHzWalkSprintCrouchKeepsSimulationAndPredictionInParity()
        {
            CreateWall(new Vector3(1000f, 2f, 1001.8f), new Vector3(6f, 4f, 0.2f));
            Physics.SyncTransforms();
            var rules = new CoopServerRules(maximumPastCommandTicks: 30,
                historyCapacity: 64, maximumMoveSpeed: 5d,
                claimedPositionTolerance: 0.45d,
                predictionCorrectionThreshold: 0.18d,
                predictionSnapThreshold: 1.5d,
                walkSpeed: 2d, sprintSpeed: 5d, crouchSpeed: 1.5d,
                maximumAcceleration: 30d);
            var authorityCollision = new CoopPlayerMovementCollision();
            var ownerCollision = new CoopPlayerMovementCollision();
            var simulation = new AuthoritativeCoopSimulation(rules,
                new[] { new CoopPlayerSpawn(1, Spawn) },
                new[] { new CoopTargetSpawn(1,
                    Spawn + new NetVector3(0d, 0d, 30d), 0.5d, 1000d) });
            simulation.SetPlayerMovementResolver(authorityCollision.Resolve);
            var prediction = new LocalPredictionBuffer(rules, 1, Spawn,
                ownerCollision.Resolve);
            var clock = new ClientServerClock(rules.TickRate);
            clock.Observe(0, 1, 0d, 0.08d);
            var commands = new List<(int due, PlayerInputCommand command)>();
            var snapshots = new List<(int due, long tick, AuthoritativePlayerState state)>();
            var diagnostic = new System.Text.StringBuilder();
            long previousInputTick = 0;
            uint sequence = 0;
            int rejected = 0;
            int corrections = 0;
            double maximumReplayError = 0d;
            for (int tick = 1; tick <= 162; tick++)
            {
                double now = tick * rules.FixedDeltaSeconds;
                if (tick <= 150 && clock.TryGetNextInputTick(now,
                        previousInputTick, out long inputTick))
                {
                    previousInputTick = inputTick;
                    bool walk = tick <= 39;
                    bool sprint = tick > 45 && tick <= 78;
                    bool crouch = tick > 84 && tick <= 108;
                    double moveZ = walk || sprint ? 1d : 0d;
                    uint next = ++sequence;
                    var provisional = new PlayerInputCommand(1, next,
                        next + 1000UL, inputTick, 0d, moveZ, 0d, 0d,
                        false, prediction.PredictedPosition,
                        jumpPressed: false, sprintHeld: sprint,
                        crouchRequested: crouch);
                    NetVector3 claim = prediction.Predict(provisional);
                    var command = new PlayerInputCommand(1, next,
                        next + 1000UL, inputTick, 0d, moveZ, 0d, 0d,
                        false, claim, jumpPressed: false, sprintHeld: sprint,
                        crouchRequested: crouch);
                    commands.Add((tick + 3,
                        NetcodePlayerCommand.FromDomain(command).ToDomain()));
                }

                AuthoritativeTickResult result = simulation.Step(commands
                    .Where(value => value.due == tick)
                    .Select(value => value.command).ToArray());
                rejected += result.Commands.Count(value => !value.Accepted);
                if (tick % 3 == 0)
                    snapshots.Add((tick + 3, tick,
                        NetcodePlayerState.FromDomain(tick,
                            result.Snapshot.Player(1)).ToDomain()));
                foreach (var snapshot in snapshots.Where(value => value.due == tick))
                {
                    clock.Observe(snapshot.tick, 1, now, 0.08d);
                    PlayerMovementState before = prediction.PredictedMovement;
                    PredictionCorrection correction = prediction.Reconcile(snapshot.state);
                    maximumReplayError = System.Math.Max(maximumReplayError,
                        correction.ErrorDistance);
                    if (correction.WasCorrected) corrections++;
                    if (correction.ErrorDistance > 0.02d && diagnostic.Length < 3000)
                        diagnostic.AppendLine($"localTick={tick};snapshot={snapshot.tick};" +
                            $"acceptedClientTick={snapshot.state.LastAcceptedClientTick};" +
                            $"ack={snapshot.state.AcknowledgedSequence};" +
                            $"before={before.Position};beforeVel={before.Velocity};" +
                            $"replay={correction.ReplayTargetPosition};" +
                            $"applied={correction.AppliedPosition};" +
                            $"replayVel={prediction.PredictedMovement.Velocity};" +
                            $"error={correction.ErrorDistance:F6};kind={correction.Kind}");
                }
            }

            Assert.That(rejected, Is.Zero, diagnostic.ToString());
            Assert.That(maximumReplayError, Is.LessThanOrEqualTo(0.02d), diagnostic.ToString());
            Assert.That(corrections, Is.Zero, diagnostic.ToString());
        }

        [Test]
        public void NaturalGapRecoveryKeepsPresentationSmoothingOutOfTheNextClaimedPhysicsPosition()
        {
            CreateWall(new Vector3(1000f, 2f, 1004f), new Vector3(6f, 4f, 0.2f));
            Physics.SyncTransforms();
            var rules = new CoopServerRules(maximumPastCommandTicks: 30,
                historyCapacity: 64, maximumMoveSpeed: 5d,
                claimedPositionTolerance: 0.45d,
                predictionCorrectionThreshold: 0.18d,
                predictionSnapThreshold: 1.5d,
                walkSpeed: 2d, sprintSpeed: 5d, crouchSpeed: 1.5d,
                maximumAcceleration: 30d);
            var authorityCollision = new CoopPlayerMovementCollision();
            var ownerCollision = new CoopPlayerMovementCollision();
            var simulation = new AuthoritativeCoopSimulation(rules,
                new[] { new CoopPlayerSpawn(1, Spawn) },
                new[] { new CoopTargetSpawn(1,
                    Spawn + new NetVector3(0d, 0d, 30d), 0.5d, 1000d) });
            simulation.SetPlayerMovementResolver(authorityCollision.Resolve);
            var prediction = new LocalPredictionBuffer(rules, 1, Spawn,
                ownerCollision.Resolve);
            AuthoritativePlayerState delayed = default;
            for (int tick = 1; tick <= 18; tick++)
            {
                bool sprint = tick >= 14;
                var provisional = new PlayerInputCommand(1, (uint)tick,
                    (ulong)tick + 2000UL, tick, 0d, sprint ? 1d : 0d,
                    0d, 0d, false, prediction.PredictedPosition,
                    false, sprint, false);
                NetVector3 claim = prediction.Predict(provisional);
                var command = new PlayerInputCommand(1, (uint)tick,
                    (ulong)tick + 2000UL, tick, 0d, sprint ? 1d : 0d,
                    0d, 0d, false, claim, false, sprint, false);
                // A real input shortage: no fabricated authoritative pose.
                // The next valid input is integrated by the existing bounded
                // LastAcceptedClientTick gap rules, and then acknowledged.
                AuthoritativeTickResult result = simulation.Step(
                    tick >= 2 && tick <= 13
                        ? System.Array.Empty<PlayerInputCommand>()
                        : new[] { NetcodePlayerCommand.FromDomain(command).ToDomain() });
                if (tick == 15)
                    delayed = NetcodePlayerState.FromDomain(tick,
                        result.Snapshot.Player(1)).ToDomain();
            }

            PlayerMovementState before = prediction.PredictedMovement;
            PredictionCorrection correction = prediction.Reconcile(delayed);
            Assert.That(correction.Kind, Is.EqualTo(PredictionCorrectionKind.Smooth),
                "输入短缺与既有服务器gap积分必须自然产生Smooth纠正，不能伪造snapshot误差。");
            var nextInput = new PlayerInputCommand(1, 19, 2019UL, 19,
                0d, 1d, 0d, 0d, false, prediction.PredictedPosition,
                false, true, false);
            NetVector3 nextClaim = prediction.Predict(nextInput);
            var submitted = new PlayerInputCommand(1, 19, 2019UL, 19,
                0d, 1d, 0d, 0d, false, nextClaim, false, true, false);
            AuthoritativeTickResult nextResult = simulation.Step(new[]
                { NetcodePlayerCommand.FromDomain(submitted).ToDomain() });
            string detail = $"acceptedClientTick={delayed.LastAcceptedClientTick};" +
                $"ack={delayed.AcknowledgedSequence};before={before.Position};" +
                $"beforeVel={before.Velocity};replay={correction.ReplayTargetPosition};" +
                $"presentation={correction.AppliedPosition};nextClaim={nextClaim};" +
                $"nextVel={prediction.PredictedMovement.Velocity};" +
                $"serverNext={nextResult.Snapshot.Player(1).Position};" +
                $"reject={nextResult.Commands.Single().RejectionReason}";
            TestContext.WriteLine(detail);
            Assert.That(nextResult.Commands.Single().Accepted, Is.True, detail);
            Assert.That(NetVector3.Distance(nextClaim,
                nextResult.Snapshot.Player(1).Position), Is.LessThanOrEqualTo(0.02d), detail);
        }

        [Test]
        public void OwnerRetainsSmoothRenderOffsetWhileTheNextCommandUsesExactReplay()
        {
            var (authority, replica, delayed) = CreateNaturallyCorrectedOwner();
            replica.ConsumeServerState(delayed, true, 18d);
            Assert.That(replica.LastPredictionCorrection.Kind,
                Is.EqualTo(PredictionCorrectionKind.Smooth));

            NetcodePlayerCommand next = replica.BuildPredictedCommand(
                0f, 1f, 0f, 0f, false, 19, false, true, false);
            Assert.That(authority.TryQueueCommand(10, next, false), Is.True);
            AuthoritativeTickResult result = authority.ServerStep();

            Assert.That(result.Commands.Single().Accepted, Is.True);
            Assert.That(Vector3.Distance(next.ClaimedPosition,
                NetcodeConversions.ToUnity(result.Snapshot.Player(1).Position)),
                Is.LessThanOrEqualTo(0.02f));
            Assert.That(Vector3.Distance(replica.PresentedPosition,
                next.ClaimedPosition), Is.GreaterThan(0.3f),
                "下一输入必须使用准确physics，但不能把上一帧Smooth画面立即硬跳到physics。");
        }

        [UnityTest]
        public IEnumerator OwnerRenderOffsetDecaysAcrossFramesWithoutChangingPhysicsClaims()
        {
            var (authority, replica, delayed) = CreateNaturallyCorrectedOwner();
            replica.ConsumeServerState(delayed, true, 18d);
            NetcodePlayerCommand next = replica.BuildPredictedCommand(
                0f, 1f, 0f, 0f, false, 19, false, true, false);
            Assert.That(authority.TryQueueCommand(10, next, false), Is.True);
            Assert.That(authority.ServerStep().Commands.Single().Accepted, Is.True);
            float initialOffset = Vector3.Distance(replica.PresentedPosition, next.ClaimedPosition);
            Assert.That(initialOffset, Is.GreaterThan(0.3f));

            yield return new WaitForSecondsRealtime(0.4f);

            Assert.That(Vector3.Distance(replica.PresentedPosition,
                next.ClaimedPosition), Is.LessThan(0.02f),
                "没有新输入时，render-only纠正也必须逐帧衰减，不能停在半纠正姿势。");
            NetcodePlayerCommand afterDecay = replica.BuildPredictedCommand(
                0f, 1f, 0f, 0f, false, 20, false, true, false);
            Assert.That(authority.TryQueueCommand(10, afterDecay, false), Is.True);
            AuthoritativeTickResult result = authority.ServerStep();
            Assert.That(result.Commands.Single().Accepted, Is.True);
            Assert.That(Vector3.Distance(afterDecay.ClaimedPosition,
                NetcodeConversions.ToUnity(result.Snapshot.Player(1).Position)),
                Is.LessThanOrEqualTo(0.02f));
        }

        [Test]
        public void AuthoritativeDeathSnapClearsThePreviousRenderOffset()
        {
            var (authority, replica, delayed) = CreateNaturallyCorrectedOwner();
            replica.ConsumeServerState(delayed, true, 18d);
            Assert.That(replica.LastPredictionCorrection.Kind,
                Is.EqualTo(PredictionCorrectionKind.Smooth));
            authority.ApplyServerDamageToPlayer(1, 1000d);
            AuthoritativeTickResult result = null;
            // Before learning of server death, the owner keeps predicting
            // legitimate input. The server rejects it and ACKs those inputs.
            for (int tick = 19; tick <= 60; tick++)
            {
                NetcodePlayerCommand command = replica.BuildPredictedCommand(
                    0f, 1f, 0f, 0f, false, tick, false, true, false);
                Assert.That(authority.TryQueueCommand(10, command, false), Is.True);
                result = authority.ServerStep();
            }
            Assert.That(authority.TryGetPlayerState(1, out NetcodePlayerState state), Is.True);
            replica.ConsumeServerState(state, true, state.ServerTick);

            Assert.That(replica.LastPredictionCorrection.Kind,
                Is.EqualTo(PredictionCorrectionKind.Snap));
            Assert.That(Vector3.Distance(replica.PresentedPosition, state.Position),
                Is.LessThanOrEqualTo(0.02f));
            Assert.That(result.Commands.Single().Accepted, Is.False);
        }

        [Test]
        public void RebindingOwnerClearsThePreviousRenderOffset()
        {
            var (authority, replica, delayed) = CreateNaturallyCorrectedOwner();
            replica.ConsumeServerState(delayed, true, 18d);
            Assert.That(replica.LastPredictionCorrection.Kind,
                Is.EqualTo(PredictionCorrectionKind.Smooth));

            replica.EnableOwnerTestHook(authority, 1);
            NetcodePlayerCommand next = replica.BuildPredictedCommand(
                0f, 1f, 0f, 0f, false, 19, false, true, false);
            Assert.That(Vector3.Distance(replica.PresentedPosition, next.ClaimedPosition),
                Is.LessThanOrEqualTo(0.001f), "重绑或despawn-reset不得保留旧render offset。");
        }

        [Test]
        public void RealMissionRestartClearsThePreviousRenderOffsetAtTheNewClockEpoch()
        {
            var (authority, replica, delayed) = CreateNaturallyCorrectedOwner();
            replica.ConsumeServerState(delayed, true, 18d);
            Assert.That(replica.LastPredictionCorrection.Kind,
                Is.EqualTo(PredictionCorrectionKind.Smooth));
            authority.ApplyServerDamageToPlayer(1, 1000d);
            Assert.That(authority.TryRestartMission(10), Is.True);
            Assert.That(authority.WorldState.RunGeneration, Is.EqualTo(2));
            Assert.That(authority.TryGetPlayerState(1, out NetcodePlayerState state), Is.True);

            replica.ConsumeServerState(state, true, state.ServerTick);
            NetcodePlayerCommand first = replica.BuildPredictedCommand(
                0f, 0f, 0f, 0f, false, 1);

            Assert.That(Vector3.Distance(replica.PresentedPosition, state.Position),
                Is.LessThanOrEqualTo(0.001f));
            Assert.That(Vector3.Distance(replica.PresentedPosition, first.ClaimedPosition),
                Is.LessThanOrEqualTo(0.001f), "新run/clock epoch不得带入上一场纠正偏移。");
        }

        private (NetworkCoopSessionAuthority authority, NetworkPlayerReplica replica,
            NetcodePlayerState delayed) CreateNaturallyCorrectedOwner()
        {
            CreateWall(new Vector3(1000f, 2f, 1004f), new Vector3(6f, 4f, 0.2f));
            Physics.SyncTransforms();
            var rules = new CoopServerRules(maximumPastCommandTicks: 30,
                historyCapacity: 64, maximumMoveSpeed: 5d,
                claimedPositionTolerance: 0.45d,
                predictionCorrectionThreshold: 0.18d,
                predictionSnapThreshold: 1.5d,
                walkSpeed: 2d, sprintSpeed: 5d, crouchSpeed: 1.5d,
                maximumAcceleration: 30d);
            NetworkCoopSessionAuthority authority = CreateAuthority(rules);
            NetworkPlayerReplica replica = CreateReplica(authority);
            NetcodePlayerState delayed = default;
            for (int tick = 1; tick <= 18; tick++)
            {
                bool sprint = tick >= 14;
                NetcodePlayerCommand command = replica.BuildPredictedCommand(
                    0f, sprint ? 1f : 0f, 0f, 0f, false, tick,
                    false, sprint, false);
                if (tick < 2 || tick > 13)
                    Assert.That(authority.TryQueueCommand(10, command, false), Is.True);
                AuthoritativeTickResult result = authority.ServerStep();
                if (tick == 15)
                    delayed = NetcodePlayerState.FromDomain(tick,
                        result.Snapshot.Player(1));
            }
            return (authority, replica, delayed);
        }

        [Test]
        public void ObstructionDiagnosticSnapshotsActualWallWithoutChangingMovement()
        {
            CreateWall(new Vector3(1000f, 2f, 1001.8f), new Vector3(6f, 4f, 0.2f));
            Physics.SyncTransforms();
            var instrumented = new CoopPlayerMovementCollision();
            var ordinary = new CoopPlayerMovementCollision();
            var samples = new List<CoopMovementCollisionDiagnostic>();
            instrumented.ObstructionDiagnosed += samples.Add;
            Vector3 origin = NetcodeConversions.ToUnity(Spawn);
            Vector3 wanted = Vector3.forward * 2f;

            Vector3 actual = instrumented.ResolveHorizontalDisplacement(origin, wanted);
            Vector3 baseline = ordinary.ResolveHorizontalDisplacement(origin, wanted);

            Assert.That(actual, Is.EqualTo(baseline), "只读诊断不能改变胶囊碰撞结果。");
            Assert.That(samples, Is.Not.Empty);
            CoopMovementCollisionDiagnostic sample = samples[0];
            Assert.That(sample.ColliderHierarchy, Does.Contain("Collision_parity_wall"));
            Assert.That(sample.ColliderType, Is.EqualTo("BoxCollider"));
            Assert.That(sample.ColliderEntityId, Is.EqualTo(objects.Single()
                .GetComponent<BoxCollider>().GetEntityId().ToString()));
            Assert.That(sample.ColliderPosition, Is.EqualTo(new Vector3(1000f, 2f, 1001.8f)));
            Assert.That(sample.Cursor, Is.EqualTo(origin));
            Assert.That(sample.Displacement, Is.EqualTo(wanted));
            Assert.That(sample.AllowedDistance, Is.EqualTo(Mathf.Max(0f,
                sample.HitDistance - CoopPlayerMovementCollision.CapsuleSkin)));
            Assert.That(sample.HitNormal.z, Is.LessThan(-0.9f));
            Assert.That(sample.Index, Is.EqualTo(1));
            Assert.That(sample.Pass, Is.Zero);
        }

        [Test]
        public void ObstructionDiagnosticsAreBoundedForTheEntireSolverLifetime()
        {
            CreateWall(new Vector3(1000f, 2f, 1001.8f), new Vector3(6f, 4f, 0.2f));
            Physics.SyncTransforms();
            var collision = new CoopPlayerMovementCollision();
            int observed = 0;
            collision.ObstructionDiagnosed += _ => observed++;
            Vector3 origin = NetcodeConversions.ToUnity(Spawn);
            for (int query = 0; query < 300; query++)
                collision.ResolveHorizontalDisplacement(origin, Vector3.forward * 2f);

            Assert.That(observed, Is.EqualTo(CoopPlayerMovementCollision.MaximumDiagnosticSamples));
            Assert.That(collision.DiagnosticSampleCount, Is.EqualTo(observed));
            collision.ResolveHorizontalDisplacement(origin, Vector3.forward * 2f);
            Assert.That(observed, Is.EqualTo(128), "每次调用、每帧或二次碰面不能重置诊断预算。");
            var other = new CoopPlayerMovementCollision();
            int otherSamples = 0;
            other.ObstructionDiagnosed += _ => otherSamples++;
            other.ResolveHorizontalDisplacement(origin, Vector3.forward * 2f);
            Assert.That(otherSamples, Is.GreaterThan(0), "不同 solver 拥有独立有界预算。");
        }

        [Test]
        public void UnobstructedMovementDoesNotEmitCollisionDiagnostics()
        {
            var collision = new CoopPlayerMovementCollision();
            int observed = 0;
            collision.ObstructionDiagnosed += _ => observed++;
            Vector3 actual = collision.ResolveHorizontalDisplacement(
                NetcodeConversions.ToUnity(Spawn), Vector3.forward * 0.08f);
            Assert.That(actual, Is.EqualTo(Vector3.forward * 0.08f));
            Assert.That(observed, Is.Zero);
            Assert.That(collision.DiagnosticSampleCount, Is.Zero);
        }

        private static AuthoritativeTickResult PredictAndStep(
            NetworkCoopSessionAuthority authority,
            NetworkPlayerReplica replica,
            long tick,
            float moveX,
            float moveZ)
        {
            // Match the real input driver's unit-length diagonal input.
            Vector2 normalizedMove = Vector2.ClampMagnitude(new Vector2(moveX, moveZ), 1f);
            NetcodePlayerCommand command = replica.BuildPredictedCommand(
                normalizedMove.x, normalizedMove.y, 0f, 0f, false, tick);
            Assert.That(authority.TryQueueCommand(10, command, false), Is.True);
            AuthoritativeTickResult result = authority.ServerStep();
            Assert.That(result.Commands.Count(value => !value.Accepted), Is.Zero,
                "同一世界碰撞下的有效输入不得被拒绝；实际原因：" +
                string.Join(",", result.Commands.Where(value => !value.Accepted)
                    .Select(value => value.RejectionReason)));
            return result;
        }

        private void CreateWall(Vector3 position, Vector3 size)
        {
            var wall = new GameObject("Collision parity wall");
            objects.Add(wall);
            wall.transform.position = position;
            wall.AddComponent<BoxCollider>().size = size;
        }

        private NetworkCoopSessionAuthority CreateAuthority(CoopServerRules rules = null)
        {
            var host = new GameObject("Collision parity authority");
            objects.Add(host);
            NetworkCoopSessionAuthority authority = host.AddComponent<NetworkCoopSessionAuthority>();
            authority.EnableServerTestHook();
            authority.AutoSimulate = false;
            authority.ConfigureServer(rules ?? new CoopServerRules(),
                new[] { new CoopPlayerSpawn(1, Spawn) },
                new[] { new CoopTargetSpawn(1, Spawn + new NetVector3(0d, 0d, 30d),
                    0.5d, 1000d, moveSpeed: 0d, attackDamage: 0d) }, 1);
            authority.RegisterPlayerClient(10, 1);
            return authority;
        }

        private NetworkPlayerReplica CreateReplica(NetworkCoopSessionAuthority authority)
        {
            var host = new GameObject("Collision parity replica");
            objects.Add(host);
            NetworkPlayerReplica replica = host.AddComponent<NetworkPlayerReplica>();
            replica.EnableOwnerTestHook(authority, 1);
            return replica;
        }
    }
}
