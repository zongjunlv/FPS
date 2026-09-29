using System.Collections.Generic;
using System.Linq;
using FPS.Networking.Domain;
using NUnit.Framework;

namespace FPS.Tests.Architecture
{
    public sealed class MovementReconciliationDiagnosticTests
    {
        [Test]
        public void RejectedMovementReportsExactValidationOperandsWithoutChangingState()
        {
            var rules = new CoopServerRules();
            AuthoritativeCoopSimulation simulation = CreateSimulation(rules);
            var values = new List<CommandRejectionDiagnostic>();
            simulation.CommandRejectionDiagnosed += values.Add;
            AuthoritativePlayerState before = simulation.Step(new[]
                { Command(1, 1, default) }).Snapshot.Player(1);
            var invalid = new PlayerInputCommand(1, 2, 2, 2,
                0d, 1d, 0d, 0d, false, new NetVector3(100d, 0d, 100d),
                false, true, false);
            PlayerMovementState candidate = CoopGameplayRules.IntegrateMovement(
                before.Movement, invalid, 1, rules);

            AuthoritativeTickResult result = simulation.Step(new[] { invalid });

            Assert.That(result.Commands.Single().RejectionReason,
                Is.EqualTo(CommandRejectionReason.ImpossibleDisplacement));
            Assert.That(values.Count, Is.EqualTo(1),
                "成功输入不得产生拒绝诊断，拒绝输入也不得遗漏。");
            CommandRejectionDiagnostic value = values.Single();
            Assert.That(value.Before.Position, Is.EqualTo(before.Position));
            Assert.That(value.Before.Velocity, Is.EqualTo(before.Velocity));
            Assert.That(value.Candidate.Position, Is.EqualTo(candidate.Position));
            Assert.That(value.Candidate.Velocity, Is.EqualTo(candidate.Velocity));
            Assert.That(value.LastAcceptedClientTick, Is.EqualTo(1));
            Assert.That(value.ElapsedTicks, Is.EqualTo(1));
            Assert.That(value.CandidateIntegrated, Is.True);
            Assert.That(value.ClaimedTolerance, Is.EqualTo(rules.ClaimedPositionTolerance));
            Assert.That(value.Command.SprintHeld, Is.True);
            Assert.That(value.Command.Sequence, Is.EqualTo(2));
            Assert.That(result.Snapshot.Player(1).Position, Is.EqualTo(before.Position),
                "诊断不能改变拒绝后的权威物理状态。");
        }

        [Test]
        public void IdentityRejectionDoesNotPretendToHaveAnIntegratedCandidate()
        {
            AuthoritativeCoopSimulation simulation = CreateSimulation(new CoopServerRules());
            var values = new List<CommandRejectionDiagnostic>();
            simulation.CommandRejectionDiagnosed += values.Add;
            AuthoritativePlayerState before = simulation.Step(new[]
                { Command(1, 1, default) }).Snapshot.Player(1);
            simulation.Step(new[] { Command(1, 2, before.Position) });

            Assert.That(values.Count, Is.EqualTo(1));
            Assert.That(values[0].Reason, Is.EqualTo(CommandRejectionReason.InvalidSequence));
            Assert.That(values[0].CandidateIntegrated, Is.False);
            Assert.That(values[0].ElapsedTicks, Is.Zero);
            Assert.That(values[0].Before.Position, Is.EqualTo(before.Position));
            Assert.That(values[0].LastAcceptedClientTick, Is.EqualTo(1));
        }

        [Test]
        public void ReconciliationReportsBeforeAndReplayedPhysicsWithTheirActualAckOrigins()
        {
            var rules = new CoopServerRules();
            AuthoritativeCoopSimulation simulation = CreateSimulation(rules);
            var prediction = new LocalPredictionBuffer(rules, 1, default);
            PlayerInputCommand first = Command(1, 1, default);
            prediction.Predict(first);
            AuthoritativePlayerState state = simulation.Step(new[] { first }).Snapshot.Player(1);
            PlayerInputCommand pending = Command(2, 2, prediction.PredictedPosition);
            prediction.Predict(pending);
            PlayerMovementState before = prediction.PredictedMovement;

            prediction.Reconcile(state);

            PredictionReconciliationDiagnostic value = prediction.LastReconciliationDiagnostic;
            Assert.That(value.Before.Position, Is.EqualTo(before.Position));
            Assert.That(value.Before.Velocity, Is.EqualTo(before.Velocity));
            Assert.That(value.Replay.Position, Is.EqualTo(prediction.PredictedPosition));
            Assert.That(value.Replay.Velocity, Is.EqualTo(prediction.PredictedMovement.Velocity));
            Assert.That(value.Authoritative.Position, Is.EqualTo(state.Position));
            Assert.That(value.Authoritative.AcknowledgedSequence, Is.EqualTo(1));
            Assert.That(value.Authoritative.LastAcceptedClientTick, Is.EqualTo(1));
            Assert.That(value.PreviousPredictedTick, Is.EqualTo(2));
            Assert.That(value.ReplayTick, Is.EqualTo(2));
            Assert.That(value.PendingBefore, Is.EqualTo(2));
            Assert.That(value.PendingAfter, Is.EqualTo(1));
            Assert.That(value.HasPendingCommand, Is.True);
            Assert.That(value.LastPendingCommand.Sequence, Is.EqualTo(2));
            prediction.ResetToAuthoritative(state);
            Assert.That(prediction.LastReconciliationDiagnostic.PendingBefore, Is.Zero);
        }

        [Test]
        public void ReconnectedOwnerRecoversMovementAfterReloadRejectedInputAndPendingTickGap()
        {
            var rules = new CoopServerRules(maximumPastCommandTicks: 30,
                historyCapacity: 64, maximumMoveSpeed: 5d,
                claimedPositionTolerance: 0.45d,
                predictionCorrectionThreshold: 0.18d,
                predictionSnapThreshold: 1.5d,
                walkSpeed: 2d, sprintSpeed: 5d, crouchSpeed: 1.5d,
                maximumAcceleration: 30d);
            AuthoritativeCoopSimulation simulation = CreateSimulation(rules);
            var previousShot = new PlayerInputCommand(1, 1, 1, 1,
                0d, 0d, 0d, 0d, true, default);
            Assert.That(simulation.Step(new[] { previousShot }).Commands.Single().Accepted,
                Is.True);
            Assert.That(simulation.ApplyWeaponAction(1,
                AuthoritativeWeaponAction.Reload, "weapon.rifle").Accepted, Is.True);

            // Reconnection retains combat state, but starts with no accepted
            // input tick. The first new shot is genuinely rejected by reload.
            simulation.ResetPlayerInputClock(1);
            var prediction = new LocalPredictionBuffer(rules, 1, default);
            prediction.ResetToAuthoritative(simulation.CaptureSnapshot().Player(1));
            var rejectedShot = new PlayerInputCommand(1, 2, 2, 2,
                0d, 0d, 0d, 0d, true, default);
            prediction.Predict(rejectedShot);
            AuthoritativeTickResult rejection = simulation.Step(new[] { rejectedShot });
            AuthoritativePlayerState rejectedState = rejection.Snapshot.Player(1);
            Assert.That(rejection.Commands.Single().RejectionReason,
                Is.EqualTo(CommandRejectionReason.Reloading));
            Assert.That(rejectedState.AcknowledgedSequence, Is.EqualTo(2));
            Assert.That(rejectedState.LastAcceptedClientTick, Is.EqualTo(long.MinValue));

            // A short input gap occurs before the delayed rejection ACK arrives.
            for (int tick = 3; tick < 15; tick++)
                simulation.Step(System.Array.Empty<PlayerInputCommand>());
            var pendingInput = new PlayerInputCommand(1, 3, 3, 15,
                0d, 1d, 0d, 0d, false, prediction.PredictedPosition,
                false, true, false);
            NetVector3 pendingClaim = prediction.Predict(pendingInput);
            var pendingPayload = new PlayerInputCommand(1, 3, 3, 15,
                0d, 1d, 0d, 0d, false, pendingClaim,
                false, true, false);
            PlayerMovementState firstAcceptedMovement = CoopGameplayRules.IntegrateMovement(
                rejectedState.Movement, pendingInput, 1, rules);
            Assert.That(NetVector3.Distance(pendingClaim, firstAcceptedMovement.Position),
                Is.GreaterThan(rules.ClaimedPositionTolerance),
                "真实拒绝与输入gap必须自然产生超出容差的原始预测，不能伪造快照位置。 ");

            prediction.Reconcile(rejectedState);
            NetVector3 replayedPosition = prediction.PredictedPosition;
            // The already-sent pending payload cannot be rewritten by a later
            // ACK. Only subsequent claims should use the corrected replay.
            Assert.That(simulation.Step(new[] { pendingPayload }).Commands.Single().RejectionReason,
                Is.EqualTo(CommandRejectionReason.ImpossibleDisplacement));
            var nextInput = new PlayerInputCommand(1, 4, 4, 16,
                0d, 1d, 0d, 0d, false, prediction.PredictedPosition,
                false, true, false);
            NetVector3 nextClaim = prediction.Predict(nextInput);
            var nextPayload = new PlayerInputCommand(1, 4, 4, 16,
                0d, 1d, 0d, 0d, false, nextClaim,
                false, true, false);
            AuthoritativeTickResult nextResult = simulation.Step(new[] { nextPayload });

            Assert.That(NetVector3.Distance(replayedPosition, firstAcceptedMovement.Position),
                Is.LessThan(0.000001d),
                "拒绝ACK不能充当已接受输入tick；首次重放必须和服务器一样只积分一步。 ");
            Assert.That(nextResult.Commands.Single().Accepted, Is.True,
                $"纠正后的下一输入应恢复接受，实际为 {nextResult.Commands.Single().RejectionReason}。");
        }

        private static AuthoritativeCoopSimulation CreateSimulation(CoopServerRules rules) =>
            new(rules, new[] { new CoopPlayerSpawn(1, default) },
                new[] { new CoopTargetSpawn(1, new NetVector3(0d, 0d, 30d),
                    0.5d, 1000d, moveSpeed: 0d, attackDamage: 0d) }, 1);

        private static PlayerInputCommand Command(uint sequence, long tick,
            NetVector3 claim) => new(1, sequence, sequence, tick,
                0d, 1d, 0d, 0d, false, claim);
    }
}
