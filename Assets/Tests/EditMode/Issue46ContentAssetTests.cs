using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

public sealed class Issue46ContentAssetTests
{
    private const string CatalogPath =
        "Assets/Resources/Content/CityNew/CityNewContentCatalog.asset";

    [Test]
    public void CityNewCatalogContainsStableVersionedContentAssets()
    {
        CityNewContentCatalog catalog =
            AssetDatabase.LoadAssetAtPath<CityNewContentCatalog>(CatalogPath);

        Assert.That(catalog, Is.Not.Null);
        Assert.That(catalog.TryValidate(out string error), Is.True, error);
        Assert.That(catalog.StableId, Is.EqualTo("city_new.default"));
        Assert.That(catalog.DefaultEnemy.StableId,
            Is.EqualTo("spider_bot"));
        Assert.That(catalog.WaveSequence.StableId,
            Is.EqualTo("city_new.sequence.default"));
        Assert.That(catalog.LootDropTable.StableId,
            Is.EqualTo("city_new.loot.default"));
        Assert.That(catalog.WaveSequence.WaveCount, Is.EqualTo(3));
        Assert.That(catalog.EncounterSequence, Is.Not.Null);
        Assert.That(catalog.EncounterSequence.ContentVersion, Is.EqualTo(1));
        Assert.That(catalog.EncounterSequence.Count, Is.EqualTo(4));
        Assert.That(catalog.LayoutSet, Is.Not.Null);
        Assert.That(catalog.LayoutSet.ContentVersion, Is.EqualTo(1));
        Assert.That(catalog.LayoutSet.Modules, Has.Count.EqualTo(7));
        Assert.That(catalog.LayoutSet.TryValidate(out string layoutError),
            Is.True, layoutError);
        Assert.That(catalog.UseRuntimeModularLayout, Is.False,
            "正式 CityNew 不应被模块化测试区域覆盖。");
        Assert.That(catalog.EnemyArchetypes, Has.Count.EqualTo(5));
        Assert.That(catalog.Items, Has.Count.EqualTo(4));
        Assert.That(catalog.Upgrades, Has.Count.EqualTo(13));
        Assert.That(catalog.EnemyArchetypes,
            Has.All.Matches<EnemyArchetypeDefinition>(
                AssetDatabase.Contains));
        Assert.That(catalog.Items,
            Has.All.Matches<ItemDefinition>(AssetDatabase.Contains));
        Assert.That(catalog.Upgrades,
            Has.All.Matches<UpgradeDefinition>(AssetDatabase.Contains));

        Assert.That(catalog.EnemyArchetypes
                .Select(definition => definition.StableId),
            Is.Unique);
        Assert.That(catalog.Items.Select(definition => definition.StableId),
            Is.Unique);

        IEnumerable<string> ids = catalog.Upgrades
            .Select(definition => definition.StableId);
        Assert.That(ids, Is.Unique);
        Assert.That(ids, Has.None.Null.Or.Empty);

        for (int index = 0;
             index < catalog.WaveSequence.WaveCount;
             index++)
        {
            WaveDefinition wave =
                catalog.WaveSequence.GetStage(index).Wave;
            Assert.That(wave.StableId, Is.Not.Empty);
            Assert.That(wave.ResolvedEntries, Is.Not.Empty);
            Assert.That(wave.ResolvedEntries,
                Has.All.Matches<WaveEnemyEntry>(entry =>
                    entry.Archetype != null &&
                    AssetDatabase.Contains(entry.Archetype) &&
                    !string.IsNullOrWhiteSpace(entry.EnemyTypeId)));
        }

        Assert.That(CityNewContentCatalog.LoadDefault(),
            Is.SameAs(catalog));

        UpgradeDefinition vitality = catalog.Upgrades.Single(definition =>
            definition.StableId == "survival_vitality_reinforcement");
        Assert.That(vitality.GameplayEffect, Is.Not.Null);
        Assert.That(AssetDatabase.Contains(vitality.GameplayEffect), Is.True);
        foreach (ItemDefinition item in catalog.Items.Where(definition =>
                     definition.EffectType == ItemEffectType.RestoreHealth ||
                     definition.EffectType == ItemEffectType.RestoreArmor))
        {
            Assert.That(item.GameplayEffect, Is.Not.Null, item.StableId);
            Assert.That(AssetDatabase.Contains(item.GameplayEffect),
                Is.True, item.StableId);
        }
    }

    [Test]
    public void MissingReferencesAreRejectedWithActionableError()
    {
        CityNewContentCatalog catalog =
            ScriptableObject.CreateInstance<CityNewContentCatalog>();

        try
        {
            Assert.That(catalog.TryValidate(out string error), Is.False);
            StringAssert.Contains("stable ID", error);
        }
        finally
        {
            Object.DestroyImmediate(catalog);
        }
    }

    [Test]
    public void RuntimeBootstrapsDoNotCreateContentScriptableObjects()
    {
        string scripts = Path.Combine(Application.dataPath, "Scripts");
        string[] migratedFiles =
        {
            "AI/CityNewWaveBootstrap.cs",
            "Inventory/CityNewInventoryBootstrap.cs",
            "Player/PlayerUpgradeController.cs"
        };

        foreach (string relativePath in migratedFiles)
        {
            string source = File.ReadAllText(
                Path.Combine(scripts, relativePath));
            StringAssert.DoesNotContain(
                "ScriptableObject.CreateInstance",
                source,
                relativePath);
            StringAssert.DoesNotContain(
                "CreateInstance<UpgradeDefinition>",
                source,
                relativePath);
        }
    }

    [Test]
    public void InvalidCatalogStopsBeforeEnemyFactoryTakesOverSceneContent()
    {
        GameObject host = new("Invalid Content Host");
        CityNewContentCatalog invalid =
            ScriptableObject.CreateInstance<CityNewContentCatalog>();

        try
        {
            CityNewWaveBootstrap bootstrap =
                host.AddComponent<CityNewWaveBootstrap>();
            bootstrap.SetContentCatalog(invalid);
            LogAssert.Expect(
                LogType.Error,
                "CityNew content configuration is invalid: " +
                "CityNew content catalog requires a stable ID.");

            Assert.That(bootstrap.ValidateContent(), Is.False);
            Assert.That(bootstrap.ConfigurationError,
                Does.Contain("requires a stable ID"));
            Assert.That(host.GetComponent<PooledEnemyFactory>(), Is.Null);
            Assert.That(host.GetComponent<SceneEnemyFactory>(), Is.Null);
        }
        finally
        {
            Object.DestroyImmediate(host);
            Object.DestroyImmediate(invalid);
        }
    }
}
