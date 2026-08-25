using NUnit.Framework;

public sealed class Issue42AiLodPolicyTests
{
    [Test]
    public void DistanceVisibilityDamageAndAlertJointlyChooseTier()
    {
        var policy = new EnemyAiLodPolicy(12f, 30f);

        Assert.That(policy.Classify(
            8f, false, false, EnemyAwarenessState.Patrol),
            Is.EqualTo(EnemyAiLodTier.Near));
        Assert.That(policy.Classify(
            18f, true, false, EnemyAwarenessState.Patrol),
            Is.EqualTo(EnemyAiLodTier.Near));
        Assert.That(policy.Classify(
            24f, false, false, EnemyAwarenessState.Alert),
            Is.EqualTo(EnemyAiLodTier.Mid));
        Assert.That(policy.Classify(
            80f, false, false, EnemyAwarenessState.Suspicious),
            Is.EqualTo(EnemyAiLodTier.Mid));
        Assert.That(policy.Classify(
            80f, false, false, EnemyAwarenessState.Patrol),
            Is.EqualTo(EnemyAiLodTier.Far));
        Assert.That(policy.Classify(
            80f, false, true, EnemyAwarenessState.Patrol),
            Is.EqualTo(EnemyAiLodTier.Near));
    }

    [Test]
    public void TierIntervalsDefineExplicitLatencyCaps()
    {
        Assert.That(EnemyAiLodPolicy.DecisionInterval(
            EnemyAiLodTier.Near), Is.EqualTo(2));
        Assert.That(EnemyAiLodPolicy.SightInterval(
            EnemyAiLodTier.Near), Is.EqualTo(2));
        Assert.That(EnemyAiLodPolicy.DecisionInterval(
            EnemyAiLodTier.Mid), Is.EqualTo(6));
        Assert.That(EnemyAiLodPolicy.SightInterval(
            EnemyAiLodTier.Mid), Is.EqualTo(8));
        Assert.That(EnemyAiLodPolicy.DecisionInterval(
            EnemyAiLodTier.Far), Is.EqualTo(20));
        Assert.That(EnemyAiLodPolicy.SightInterval(
            EnemyAiLodTier.Far), Is.EqualTo(24));
    }

    [Test]
    public void FixedSlotsDistributeWorkFairlyWithoutStarvation()
    {
        const int memberCount = 100;
        var executionCounts = new int[memberCount];
        var maximumGaps = new int[memberCount];
        var lastFrames = new int[memberCount];

        for (int index = 0; index < memberCount; index++)
        {
            lastFrames[index] = -1;
        }

        for (int frame = 0; frame < 240; frame++)
        {
            for (int slot = 0; slot < memberCount; slot++)
            {
                if (!EnemyAiLodPolicy.IsScheduledFrame(
                        frame,
                        slot,
                        EnemyAiLodPolicy.DecisionInterval(
                        EnemyAiLodTier.Far)))
                {
                    continue;
                }

                if (lastFrames[slot] >= 0)
                {
                    maximumGaps[slot] = System.Math.Max(
                        maximumGaps[slot],
                        frame - lastFrames[slot]);
                }

                lastFrames[slot] = frame;
                executionCounts[slot]++;
            }
        }

        Assert.That(executionCounts, Has.All.EqualTo(12));
        Assert.That(maximumGaps, Has.All.LessThanOrEqualTo(20));
    }
}
