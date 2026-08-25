using System.Linq;
using NUnit.Framework;
using UnityEngine;

public sealed class Issue41ThreatBudgetWaveTests
{
    [Test]
    public void SameSeedAndRulesProduceSameComposition()
    {
        WaveEnemyEntry[] candidates =
        {
            Entry("normal", 1, "assault", 5),
            Entry("raider", 3, "raider", 2),
            Entry("support", 4, "support", 2)
        };
        ThreatRoleConstraint[] constraints =
        {
            new("raider", 1, 1),
            new("support", 1, 1)
        };

        ThreatBudgetWavePlan first = ThreatBudgetWaveComposer.Compose(
            candidates, 12, 4101, constraints, 0f);
        ThreatBudgetWavePlan second = ThreatBudgetWaveComposer.Compose(
            candidates, 12, 4101, constraints, 0f);

        Assert.That(
            first.Entries.Select(entry => entry.EnemyTypeId),
            Is.EqualTo(second.Entries.Select(entry => entry.EnemyTypeId)));
        Assert.That(first.TotalThreat, Is.LessThanOrEqualTo(12));
        Assert.That(first.CountRole("raider"), Is.EqualTo(1));
        Assert.That(first.CountRole("support"), Is.EqualTo(1));
    }

    [Test]
    public void RoleMaximumAndEliteBudgetRatioAreRespected()
    {
        WaveEnemyEntry[] candidates =
        {
            Entry("normal", 1, "assault", 1),
            Entry("support", 3, "support", 20),
            Entry("elite", 5, "elite", 20, LootRewardTier.Elite)
        };
        ThreatBudgetWavePlan plan = ThreatBudgetWaveComposer.Compose(
            candidates,
            20,
            99,
            new[] { new ThreatRoleConstraint("support", 1, 1) },
            0.25f);

        Assert.That(plan.CountRole("support"), Is.EqualTo(1));
        Assert.That(plan.EliteThreat, Is.LessThanOrEqualTo(5));
        Assert.That(plan.TotalThreat, Is.LessThanOrEqualTo(20));
    }

    [Test]
    public void ImpossibleBudgetSafelyFallsBackToCheapestEnemy()
    {
        WaveEnemyEntry[] candidates =
        {
            Entry("support", 4, "support", 1),
            Entry("elite", 6, "elite", 1, LootRewardTier.Elite)
        };

        ThreatBudgetWavePlan plan = ThreatBudgetWaveComposer.Compose(
            candidates,
            1,
            7,
            new[] { new ThreatRoleConstraint("missing", 1, 1) },
            0f);

        Assert.That(plan.Entries, Has.Count.EqualTo(1));
        Assert.That(plan.Entries[0].EnemyTypeId, Is.EqualTo("support"));
        Assert.That(plan.UsedFallback, Is.True);
    }

    [Test]
    public void FixedWaveModeKeepsWeightedEntrySelection()
    {
        WaveDefinition wave =
            ScriptableObject.CreateInstance<WaveDefinition>();
        WaveEnemyEntry normal = Entry("normal", 1, "assault", 2);
        WaveEnemyEntry raider = Entry("raider", 3, "raider", 1);

        try
        {
            wave.Configure(3, 2, 0f, new[] { normal, raider });
            Assert.That(wave.CompositionMode,
                Is.EqualTo(WaveCompositionMode.FixedCount));
            Assert.That(wave.GetEntry(0), Is.SameAs(normal));
            Assert.That(wave.GetEntry(1), Is.SameAs(normal));
            Assert.That(wave.GetEntry(2), Is.SameAs(raider));
        }
        finally
        {
            Object.DestroyImmediate(wave);
        }
    }

    private static WaveEnemyEntry Entry(
        string id,
        int threat,
        string role,
        int weight,
        LootRewardTier tier = LootRewardTier.Normal)
    {
        return new WaveEnemyEntry(
            null,
            weight,
            tier,
            id,
            null,
            null,
            threat,
            role);
    }
}
