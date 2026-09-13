using System;
using System.Linq;
using FPS.GameplayEffects;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;
using Object = UnityEngine.Object;

public sealed class Issue49ContentValidationTests
{
    private string root;

    [SetUp]
    public void SetUp()
    {
        root = "Assets/__Issue49Tests_" + Guid.NewGuid().ToString("N");
        AssetDatabase.CreateFolder("Assets", root.Substring("Assets/".Length));
    }

    [TearDown]
    public void TearDown()
    {
        if (!string.IsNullOrEmpty(root) && AssetDatabase.IsValidFolder(root))
            AssetDatabase.DeleteAsset(root);
    }

    [Test]
    public void FormalContentAssetsPassWithoutModification()
    {
        var report = ContentAssetValidator.Validate();
        Assert.That(report.IsValid, Is.True, Describe(report));
        Assert.That(report.ErrorCount, Is.Zero);
    }

    [Test]
    public void EmptyIdIsReportedWithNavigableAssetPath()
    {
        var asset = Create<EnemyDefinition>("EmptyId");
        var report = Validate();
        AssertIssue(report, "ID_EMPTY", asset);
    }

    [Test]
    public void DuplicateStableIdsReportBothConflictingAssets()
    {
        var first = Create<EnemyDefinition>("First");
        var second = Create<EnemyDefinition>("Second");
        first.Configure("duplicate.enemy", "First", 10f, 1f, 0f, 1);
        second.Configure("duplicate.enemy", "Second", 10f, 1f, 0f, 1);
        var report = Validate();
        AssertIssue(report, "ID_DUPLICATE", first);
        AssertIssue(report, "ID_DUPLICATE", second);
    }

    [Test]
    public void MissingAffixEffectIsAnExplicitReferenceError()
    {
        var affix = Create<EnemyAffixDefinition>("MissingEffect");
        SetString(affix, "stableId", "test.missing.effect");
        AssertIssue(Validate(), "REFERENCE_MISSING", affix);
    }

    [Test]
    public void MissingItemIconIsReportedAtTheItemAsset()
    {
        var item = Create<ItemDefinition>("MissingIcon");
        item.Configure("test.ammo", "测试弹药", "测试描述", null,
            ItemType.Consumable, 10, ItemEffectType.AddRifleAmmo, 10f);
        AssertIssue(Validate(), "ICON_MISSING", item);
    }

    [Test]
    public void SequenceWithMissingWaveReferenceIsRejected()
    {
        var sequence = Create<WaveSequenceDefinition>("MissingWave");
        SetString(sequence, "stableId", "test.sequence");
        var serialized = new SerializedObject(sequence);
        var stages = serialized.FindProperty("stages");
        stages.arraySize = 1;
        stages.GetArrayElementAtIndex(0).FindPropertyRelative("wave")
            .objectReferenceValue = null;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        AssertIssue(Validate(), "REFERENCE_MISSING", sequence);
    }

    [Test]
    public void WaveWithMissingEnemyArchetypeReferenceIsRejected()
    {
        var wave = Create<WaveDefinition>("MissingEnemyArchetype");
        wave.ConfigureIdentity("test.wave");
        var serialized = new SerializedObject(wave);
        var entries = serialized.FindProperty("enemyEntries");
        entries.arraySize = 1;
        entries.GetArrayElementAtIndex(0).FindPropertyRelative("archetype")
            .objectReferenceValue = null;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        AssertIssue(Validate(), "REFERENCE_MISSING", wave);
    }

    [Test]
    public void EnemyAffixRejectsAPlayerOnlyWeaponAttribute()
    {
        var effect = Create<GameplayEffectDefinition>("PlayerWeaponEffect");
        effect.Configure("test.player.weapon.effect", new GameplayEffectModifier(
            GameplayAttributeId.WeaponFireRate, GameplayModifierOperation.Multiply, 1.2f));
        var affix = Create<EnemyAffixDefinition>("InvalidAttributeAffix");
        affix.Configure("test.affix.invalid.attribute", "TEST", Color.white, effect);
        AssertIssue(Validate(), "EFFECT_INVALID", affix);
    }

    [TestCase("   ")]
    [TestCase(" effect.test")]
    public void MalformedEffectTagsAreRejected(string tag)
    {
        var effect = Create<GameplayEffectDefinition>("InvalidTags");
        effect.Configure("test.effect");
        var serialized = new SerializedObject(effect);
        var tags = serialized.FindProperty("gameplayTags");
        tags.arraySize = 1;
        tags.GetArrayElementAtIndex(0).stringValue = tag;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        AssertIssue(Validate(), "TAG_INVALID", effect);
    }

    [Test]
    public void DuplicateEffectTagsAreRejected()
    {
        var effect = Create<GameplayEffectDefinition>("DuplicateTags");
        effect.Configure("test.effect");
        var serialized = new SerializedObject(effect);
        var tags = serialized.FindProperty("gameplayTags");
        tags.arraySize = 2;
        tags.GetArrayElementAtIndex(0).stringValue = "effect.test";
        tags.GetArrayElementAtIndex(1).stringValue = "effect.test";
        serialized.ApplyModifiedPropertiesWithoutUndo();
        AssertIssue(Validate(), "TAG_INVALID", effect);
    }

    [Test]
    public void ConflictingMovementAbilitiesAreRejected()
    {
        var raider = Create<RaiderApproachAbilityDefinition>("Raider");
        var ranged = Create<SuppressorRangedAbilityDefinition>("Ranged");
        SetString(raider, "stableId", "test.raider");
        SetString(ranged, "stableId", "test.ranged");
        var set = Create<EnemyAbilitySetDefinition>("ConflictingSet");
        set.Configure("test.conflict", "TEST", Color.white,
            new EnemyAbilityDefinition[] { raider, ranged });
        AssertIssue(Validate(), "ABILITY_CONFLICT", set);
    }

    [Test]
    public void InvalidSerializedBudgetIsNotHiddenByRuntimeClamping()
    {
        var source = AssetDatabase.LoadAssetAtPath<CityNewContentCatalog>(
            "Assets/Resources/Content/CityNew/CityNewContentCatalog.asset");
        Assert.That(source, Is.Not.Null);
        var wave = Object.Instantiate(source.WaveSequence.GetStage(0).Wave);
        AssetDatabase.CreateAsset(wave, root + "/InvalidBudget.asset");
        var serialized = new SerializedObject(wave);
        serialized.FindProperty("threatBudget").intValue = 0;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        AssertIssue(Validate(), "BUDGET_INVALID", wave);
    }

    [Test]
    public void MissingAddressableEntryIsReportedAtArchetype()
    {
        var archetype = CreateArchetype();
        archetype.ConfigureTemplateAddress("test/missing-enemy");
        AssertIssue(Validate(), "ADDRESSABLE_MISSING", archetype);
    }

    [Test]
    public void MissingLayoutPrefabAddressIsReportedAtModule()
    {
        var module = Create<CombatAreaModuleDefinition>("MissingLayoutPrefab");
        module.Configure(
            "test.module.missing_prefab",
            CombatAreaModuleKind.Combat,
            "Content/Test/DoesNotExist",
            Vector2Int.one,
            new[]
            {
                new LayoutConnectorDefinition(
                    "north",
                    LayoutConnectorDirection.North,
                    CombatAreaModuleKindMask.All)
            },
            new[] { new LayoutPointDefinition("combat_center", Vector3.zero) },
            new[] { new LayoutPointDefinition("enemy", Vector3.right) });
        AssertIssue(Validate(), "LAYOUT_RESOURCE_MISSING", module);
    }

    [Test]
    public void WrongAddressableGroupIsRejectedWithoutChangingProjectSettings()
    {
        var archetype = CreateArchetype();
        archetype.ConfigureTemplateAddress("test/enemy");
        var settings = AddressableAssetSettings.Create(root + "/Addressables",
            "TestSettings", false, false);
        var group = settings.CreateGroup("Wrong Enemies", false, false, false,
            null, typeof(BundledAssetGroupSchema));
        string prefabPath = root + "/Enemy.prefab";
        var host = new GameObject("Test Enemy");
        host.SetActive(false);
        try
        {
            host.AddComponent<EnemyController>();
            PrefabUtility.SaveAsPrefabAsset(host, prefabPath);
        }
        finally
        {
            Object.DestroyImmediate(host);
        }
        settings.CreateOrMoveEntry(AssetDatabase.AssetPathToGUID(prefabPath),
            group).address = "test/enemy";
        AssertIssue(ContentAssetValidator.Validate(new[] { root }, settings),
            "ADDRESSABLE_GROUP", archetype);
    }

    [Test]
    public void InvalidScanRootCannotSilentlyPass()
    {
        var report = ContentAssetValidator.Validate(new[] { root + "/Missing" });
        Assert.That(report.IsValid, Is.False);
        Assert.That(report.Issues.Any(issue => issue.Code == "SCAN_ROOT_INVALID"),
            Is.True, Describe(report));
    }

    private EnemyArchetypeDefinition CreateArchetype()
    {
        var archetype = Create<EnemyArchetypeDefinition>("Archetype");
        archetype.Configure("test.archetype", "test.enemy", null,
            LootRewardTier.Normal, null, null, 2, "assault");
        return archetype;
    }

    private T Create<T>(string name) where T : ScriptableObject
    {
        T asset = ScriptableObject.CreateInstance<T>();
        AssetDatabase.CreateAsset(asset, root + "/" + name + ".asset");
        return asset;
    }

    private static void SetString(Object asset, string property, string value)
    {
        var serialized = new SerializedObject(asset);
        Assert.That(serialized.FindProperty(property), Is.Not.Null, property);
        serialized.FindProperty(property).stringValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private ContentValidationReport Validate() =>
        ContentAssetValidator.Validate(new[] { root });

    private static void AssertIssue(ContentValidationReport report, string code,
        Object asset)
    {
        Assert.That(report.IsValid, Is.False, Describe(report));
        Assert.That(report.Issues.Any(issue => issue.Code == code &&
            issue.Asset == asset &&
            issue.AssetPath == AssetDatabase.GetAssetPath(asset) &&
            issue.Severity == ContentValidationSeverity.Error),
            Is.True, Describe(report));
    }

    private static string Describe(ContentValidationReport report) =>
        string.Join("\n", report.Issues.Select(issue =>
            $"{issue.Code}: {issue.AssetPath}: {issue.Message}"));
}
