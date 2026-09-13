using System.Collections.Generic;
using System.Linq;
using FPS.SaveGame;
using NUnit.Framework;
using UnityEngine;

public sealed class Issue62ModularLayoutTests
{
    private readonly List<Object> created = new();

    [TearDown]
    public void TearDown()
    {
        foreach (Object value in created)
            if (value != null) Object.DestroyImmediate(value);
        created.Clear();
    }

    [Test]
    public void SameVersionAndSeedProduceIdenticalCanonicalLayout()
    {
        ModularCombatLayoutSet set = ValidSet();
        CombatLayoutPlan first = DeterministicModularLayoutGenerator.Generate(set, 18018);
        CombatLayoutPlan second = DeterministicModularLayoutGenerator.Generate(set, 18018);

        Assert.That(first.UsedFallback, Is.False, first.Diagnostic);
        Assert.That(second.Fingerprint, Is.EqualTo(first.Fingerprint));
        Assert.That(second.LayoutId, Is.EqualTo(first.LayoutId));
        Assert.That(second.Placements.Select(Describe),
            Is.EqualTo(first.Placements.Select(Describe)));
    }

    [Test]
    public void DifferentSeedsProduceMeaningfullyDifferentRoutes()
    {
        ModularCombatLayoutSet set = ValidSet();
        string[] routes = Enumerable.Range(1, 64)
            .Select(seed => DeterministicModularLayoutGenerator.Generate(set, seed))
            .Select(plan => string.Join("|", plan.Placements.Select(item =>
                $"{item.DefinitionId}:{item.GridX},{item.GridZ}")))
            .Distinct()
            .ToArray();

        Assert.That(routes.Length, Is.GreaterThanOrEqualTo(8));
    }

    [Test]
    public void NewRunSeedSelectionRejectsTheCurrentVisibleRoute()
    {
        ModularCombatLayoutSet set = ValidSet();
        CombatLayoutPlan current =
            DeterministicModularLayoutGenerator.Generate(set, 18018);

        int selectedSeed = CombatLayoutReroll.SelectSeedForDifferentRoute(
            set,
            current,
            18018);
        CombatLayoutPlan selected =
            DeterministicModularLayoutGenerator.Generate(set, selectedSeed);

        Assert.That(selectedSeed, Is.Not.EqualTo(18018));
        Assert.That(
            CombatLayoutReroll.DescribeVisibleRoute(selected),
            Is.Not.EqualTo(CombatLayoutReroll.DescribeVisibleRoute(current)));
    }

    [Test]
    public void BatchSeedsAlwaysConnectAllRequiredZonesWithoutOverlap()
    {
        ModularCombatLayoutSet set = ValidSet();
        for (int seed = -128; seed < 128; seed++)
        {
            CombatLayoutPlan plan = DeterministicModularLayoutGenerator.Generate(set, seed);
            Assert.That(plan.UsedFallback, Is.False, $"seed={seed}: {plan.Diagnostic}");
            Assert.That(CombatLayoutGraphValidator.TryValidate(set, plan, out string error),
                Is.True, $"seed={seed}: {error}");
            Assert.That(plan.Placements.Select(item => (item.GridX, item.GridZ)), Is.Unique);
        }
    }

    [Test]
    public void ImpossibleCatalogProducesActionableSafeFallback()
    {
        ModularCombatLayoutSet invalid = New<ModularCombatLayoutSet>();
        invalid.Configure("bad", 1, 1, 16f, 3,
            new[] { Module("only", CombatAreaModuleKind.Spawn) });

        CombatLayoutPlan plan = DeterministicModularLayoutGenerator.Generate(invalid, 8);

        Assert.That(plan.UsedFallback, Is.True);
        Assert.That(plan.LayoutId, Is.EqualTo("city_new.layout.safe"));
        Assert.That(plan.Diagnostic, Does.Contain("内容校验失败"));
    }

    [Test]
    public void SaveRoundTripRestoresExactLayoutAndRejectsTampering()
    {
        ModularCombatLayoutSet set = ValidSet();
        CombatLayoutPlan plan = DeterministicModularLayoutGenerator.Generate(set, 4101);
        RunSnapshot snapshot = MinimalSnapshot(4101);
        snapshot.Layout = CombatLayoutPersistence.ToSnapshot(plan);

        string json = RunSnapshotCodec.Serialize(snapshot);
        Assert.That(RunSnapshotCodec.TryDeserialize(
            json, out RunSnapshot loaded, out string loadError), Is.True, loadError);
        Assert.That(CombatLayoutPersistence.TryRestore(
            set, loaded.Layout, loaded.Seed,
            out CombatLayoutPlan restored, out string restoreError),
            Is.True, restoreError);
        Assert.That(restored.Fingerprint, Is.EqualTo(plan.Fingerprint));
        Assert.That(restored.Placements.Select(Describe),
            Is.EqualTo(plan.Placements.Select(Describe)));

        loaded.Layout.Modules[1].QuarterTurns =
            (loaded.Layout.Modules[1].QuarterTurns + 1) % 4;
        Assert.That(SnapshotValidation.TryValidate(loaded, out string tamperError), Is.False);
        Assert.That(tamperError, Does.Contain("指纹"));
    }

    [Test]
    public void LayoutFingerprintDescribesPlacedContentRatherThanOuterRunSeed()
    {
        ModularCombatLayoutSet set = ValidSet();
        CombatLayoutPlan generated =
            DeterministicModularLayoutGenerator.Generate(set, 4101);
        RunSnapshot snapshot = MinimalSnapshot(9907);
        snapshot.Layout = CombatLayoutPersistence.ToSnapshot(generated);

        Assert.That(SnapshotValidation.TryValidate(snapshot, out string error),
            Is.True, error);
        Assert.That(CombatLayoutPersistence.TryRestore(
                set, snapshot.Layout, snapshot.Seed,
                out CombatLayoutPlan restored, out string restoreError),
            Is.True, restoreError);
        Assert.That(restored.Fingerprint, Is.EqualTo(generated.Fingerprint));
        Assert.That(restored.Placements.Select(Describe),
            Is.EqualTo(generated.Placements.Select(Describe)));
    }

    private ModularCombatLayoutSet ValidSet()
    {
        ModularCombatLayoutSet set = New<ModularCombatLayoutSet>();
        set.Configure("test.layout", 2, 3, 16f, 3, new[]
        {
            Module("spawn", CombatAreaModuleKind.Spawn),
            Module("combat.a", CombatAreaModuleKind.Combat),
            Module("combat.b", CombatAreaModuleKind.Combat),
            Module("combat.c", CombatAreaModuleKind.Combat),
            Module("combat.d", CombatAreaModuleKind.Combat),
            Module("event", CombatAreaModuleKind.Event),
            Module("extract", CombatAreaModuleKind.Extraction)
        });
        return set;
    }

    private CombatAreaModuleDefinition Module(string id, CombatAreaModuleKind kind)
    {
        string task = kind switch
        {
            CombatAreaModuleKind.Spawn => "player_spawn",
            CombatAreaModuleKind.Event => "terminal",
            CombatAreaModuleKind.Extraction => "extraction",
            _ => "combat_center"
        };
        CombatAreaModuleDefinition module = New<CombatAreaModuleDefinition>();
        module.Configure(id, kind, "Content/Test/" + id, Vector2Int.one,
            new[]
            {
                new LayoutConnectorDefinition("north", LayoutConnectorDirection.North, CombatAreaModuleKindMask.All),
                new LayoutConnectorDefinition("east", LayoutConnectorDirection.East, CombatAreaModuleKindMask.All),
                new LayoutConnectorDefinition("south", LayoutConnectorDirection.South, CombatAreaModuleKindMask.All),
                new LayoutConnectorDefinition("west", LayoutConnectorDirection.West, CombatAreaModuleKindMask.All)
            },
            new[] { new LayoutPointDefinition(task, Vector3.zero) },
            kind == CombatAreaModuleKind.Combat
                ? new[] { new LayoutPointDefinition("enemy", Vector3.right) }
                : System.Array.Empty<LayoutPointDefinition>());
        return module;
    }

    private T New<T>() where T : ScriptableObject
    {
        T value = ScriptableObject.CreateInstance<T>();
        created.Add(value);
        return value;
    }

    private static string Describe(CombatLayoutPlacement item) =>
        $"{item.InstanceId}:{item.DefinitionId}:{item.GridX}:{item.GridZ}:{item.QuarterTurns}";

    private static RunSnapshot MinimalSnapshot(int seed) => new()
    {
        Seed = seed,
        Health = 100f,
        Armor = 0f,
        CurrentWeaponId = "rifle",
        Weapons = new List<WeaponAmmoSnapshot>
        {
            new() { WeaponId = "rifle", Magazine = 30, Reserve = 90 }
        }
    };
}
