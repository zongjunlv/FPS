using System;

namespace FPS.Simulation.Offline
{
    public static class OfflineRunReplay
    {
        public static OfflineReplayVerification Verify(
            OfflineBalanceScenario scenario,
            OfflineRunResult expected)
        {
            if (scenario == null)
                throw new ArgumentNullException(nameof(scenario));
            if (expected == null)
                throw new ArgumentNullException(nameof(expected));
            if (!string.Equals(
                    scenario.StableId,
                    expected.ScenarioId,
                    StringComparison.Ordinal) ||
                scenario.ContentVersion != expected.ContentVersion ||
                !string.Equals(
                    scenario.RulesVersion,
                    expected.RulesVersion,
                    StringComparison.Ordinal) ||
                scenario.WorldModel.Version != expected.WorldModelVersion ||
                scenario.WorldModel.EnemyAttackOpportunityBasisPoints !=
                    expected.EnemyAttackOpportunityBasisPoints)
            {
                return new OfflineReplayVerification(
                    false,
                    expected.Digest,
                    string.Empty,
                    new OfflineStateDivergence(
                        0,
                        0,
                        "Scenario identity or rules version differs."));
            }

            // This validates that the exported command stream is still accepted
            // by the actual authoritative kernel before comparing rich output.
            try
            {
                RunSimulationKernel.Replay(
                    scenario.CreateRunConfiguration(expected.Seed),
                    expected.Commands);
            }
            catch (Exception exception)
            {
                return new OfflineReplayVerification(
                    false,
                    expected.Digest,
                    string.Empty,
                    new OfflineStateDivergence(
                        0,
                        0,
                        "Authoritative command replay failed: " +
                        exception.Message));
            }

            OfflineRunResult actual = OfflineBalanceSimulator.Run(
                scenario,
                expected.Seed);
            if (string.Equals(
                    actual.Digest,
                    expected.Digest,
                    StringComparison.Ordinal))
            {
                return new OfflineReplayVerification(
                    true,
                    expected.Digest,
                    actual.Digest,
                    null);
            }

            int common = Math.Min(
                expected.Commands.Count,
                actual.Commands.Count);
            for (int index = 0; index < common; index++)
            {
                if (CommandsEqual(
                        expected.Commands[index],
                        actual.Commands[index]))
                    continue;
                return new OfflineReplayVerification(
                    false,
                    expected.Digest,
                    actual.Digest,
                    new OfflineStateDivergence(
                        index,
                        Math.Min(
                            expected.Commands[index].Tick,
                            actual.Commands[index].Tick),
                        "First authoritative command differs."));
            }

            long divergenceTick = common == 0
                ? 0L
                : expected.Commands.Count > common
                    ? expected.Commands[common].Tick
                    : actual.Commands.Count > common
                        ? actual.Commands[common].Tick
                        : expected.DurationTicks;
            string reason = expected.Commands.Count != actual.Commands.Count
                ? "Authoritative command stream length differs."
                : "Commands match but derived balance metrics differ.";
            return new OfflineReplayVerification(
                false,
                expected.Digest,
                actual.Digest,
                new OfflineStateDivergence(
                    common,
                    divergenceTick,
                    reason));
        }

        private static bool CommandsEqual(
            SimulationCommand left,
            SimulationCommand right)
        {
            return left != null && right != null &&
                   left.Tick == right.Tick &&
                   left.Type == right.Type &&
                   left.EntityId == right.EntityId &&
                   SameBits(left.PrimaryValue, right.PrimaryValue) &&
                   SameBits(left.SecondaryValue, right.SecondaryValue);
        }

        private static bool SameBits(float left, float right)
        {
            byte[] first = BitConverter.GetBytes(left);
            byte[] second = BitConverter.GetBytes(right);
            for (int index = 0; index < first.Length; index++)
                if (first[index] != second[index])
                    return false;
            return true;
        }
    }
}
