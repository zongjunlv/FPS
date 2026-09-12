#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class Issue57UtilityAiBuilder
{
    private const string UtilityFolder =
        "Assets/Resources/Content/CityNew/Enemies/Utility";
    private const string ProfilePath = UtilityFolder +
        "/RaiderUtility.asset";
    private const string RaiderAbilityPath =
        "Assets/Resources/Content/CityNew/Enemies/Abilities/RaiderFlank.asset";

    [MenuItem("FPS/Issue 57/Build Raider Utility AI Profile")]
    public static void Build()
    {
        EnsureFolder(UtilityFolder);
        EnemyUtilityProfileDefinition profile =
            AssetDatabase.LoadAssetAtPath<EnemyUtilityProfileDefinition>(
                ProfilePath);

        if (profile == null)
        {
            profile = ScriptableObject.CreateInstance<
                EnemyUtilityProfileDefinition>();
            AssetDatabase.CreateAsset(profile, ProfilePath);
        }

        profile.Configure(
            "enemy.utility.raider",
            "raider.chase",
            14f,
            CreateActions());
        EditorUtility.SetDirty(profile);

        RaiderApproachAbilityDefinition raider =
            AssetDatabase.LoadAssetAtPath<RaiderApproachAbilityDefinition>(
                RaiderAbilityPath);

        if (raider == null)
        {
            throw new System.InvalidOperationException(
                "RaiderFlank ability asset is missing.");
        }

        raider.ConfigureUtilityProfile(profile);
        EditorUtility.SetDirty(raider);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("Issue 57 Utility AI profile built and assigned.");
    }

    private static IEnumerable<EnemyUtilityActionDefinition> CreateActions()
    {
        yield return new EnemyUtilityActionDefinition(
            "raider.chase",
            "追击",
            EnemyUtilityActionKind.Chase,
            0.62f,
            0.05f,
            0f,
            0.45f,
            0.1f,
            0f,
            1f,
            "raider.chase",
            new[]
            {
                new EnemyUtilityConsiderationDefinition(
                    EnemyUtilityFactId.TargetDistance,
                    EnemyUtilityResponseCurve.Rising,
                    1.5f,
                    10f,
                    1f,
                    0.65f)
            });

        yield return new EnemyUtilityActionDefinition(
            "raider.flank",
            "侧翼突袭",
            EnemyUtilityActionKind.Flank,
            1f,
            0.2f,
            3.5f,
            2.5f,
            0.15f,
            5.5f,
            1.35f,
            "raider.chase",
            new[]
            {
                new EnemyUtilityConsiderationDefinition(
                    EnemyUtilityFactId.HasLineOfSight,
                    EnemyUtilityResponseCurve.BooleanTrue),
                new EnemyUtilityConsiderationDefinition(
                    EnemyUtilityFactId.TargetDistance,
                    EnemyUtilityResponseCurve.Rising,
                    3.5f,
                    12f,
                    1f,
                    0.85f),
                new EnemyUtilityConsiderationDefinition(
                    EnemyUtilityFactId.HealthRatio,
                    EnemyUtilityResponseCurve.Rising,
                    0.3f,
                    0.85f,
                    1f,
                    0.75f),
                new EnemyUtilityConsiderationDefinition(
                    EnemyUtilityFactId.TargetInCover,
                    EnemyUtilityResponseCurve.BooleanFalse,
                    0f,
                    1f,
                    1f,
                    0.65f)
            });

        yield return new EnemyUtilityActionDefinition(
            "raider.retreat",
            "战术撤退",
            EnemyUtilityActionKind.Retreat,
            1.15f,
            0.2f,
            4f,
            1.8f,
            0.12f,
            7f,
            1.5f,
            "raider.chase",
            new[]
            {
                new EnemyUtilityConsiderationDefinition(
                    EnemyUtilityFactId.HealthRatio,
                    EnemyUtilityResponseCurve.Falling,
                    0.2f,
                    0.62f),
                new EnemyUtilityConsiderationDefinition(
                    EnemyUtilityFactId.TargetDistance,
                    EnemyUtilityResponseCurve.Falling,
                    2f,
                    12f,
                    1f,
                    0.35f),
                new EnemyUtilityConsiderationDefinition(
                    EnemyUtilityFactId.HasSupportCoverage,
                    EnemyUtilityResponseCurve.BooleanFalse,
                    0f,
                    1f,
                    1f,
                    0.3f)
            });
    }

    private static void EnsureFolder(string folder)
    {
        string[] segments = folder.Split('/');
        string current = segments[0];

        for (int index = 1; index < segments.Length; index++)
        {
            string next = current + "/" + segments[index];

            if (!AssetDatabase.IsValidFolder(next))
            {
                AssetDatabase.CreateFolder(current, segments[index]);
            }

            current = next;
        }
    }
}
#endif
