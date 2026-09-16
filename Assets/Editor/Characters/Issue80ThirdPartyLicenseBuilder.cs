using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class Issue80ThirdPartyLicenseBuilder
{
    private const string SourceId = "source.kenney.blocky-characters";
    private const string Root = "Assets/ThirdParty/Kenney/BlockyCharacters";
    private const string ArchiveHash =
        "5e123859aa0c1598342b600c6db197024a1d63eb9ec531398b310725f589887e";

    [MenuItem("FPS/Content/Issue 80/Rebuild Licensed Character Intake")]
    public static void Build()
    {
        EnsureFolder("Assets/Resources/Content/Characters");
        string[] modelPaths =
        {
            $"{Root}/Models/character-a.fbx",
            $"{Root}/Models/character-b.fbx",
            $"{Root}/Models/character-c.fbx"
        };
        foreach (string path in modelPaths)
        {
            ConfigureModelImporter(path);
        }

        ThirdPartyAssetLicenseManifest manifest =
            AssetDatabase.LoadAssetAtPath<ThirdPartyAssetLicenseManifest>(
                ThirdPartyAssetLicenseManifest.DefaultAssetPath);
        if (manifest == null)
        {
            manifest = ScriptableObject.CreateInstance<
                ThirdPartyAssetLicenseManifest>();
            AssetDatabase.CreateAsset(
                manifest,
                ThirdPartyAssetLicenseManifest.DefaultAssetPath);
        }

        ThirdPartySourceRecord source = new();
        source.Configure(
            SourceId,
            "Kenney Blocky Characters",
            "Kenney",
            "2.0",
            "https://kenney.nl/assets/blocky-characters",
            "https://kenney.nl/media/pages/assets/blocky-characters/8369c0cf30-1749547469/kenney_blocky-characters_20.zip",
            "CC0-1.0",
            "https://creativecommons.org/publicdomain/zero/1.0/legalcode",
            $"{Root}/License/License.txt",
            "610fec89c16826112e9d6b80497b726c43fea0e42c9cd9d7cb081f8ad550c0ec",
            $"{Root}/License/CC0-1.0-Legal-Code.txt",
            "a2010f343487d3f7618affe54f789f5487602331c0a8d03f49e9a7c547cf0499",
            "2026-09-16",
            ArchiveHash,
            true,
            true,
            true,
            false,
            true);

        ThirdPartyAssetRecord characterA = Character(
            "model.kenney.blocky.a",
            "Blocky Character A（胡须成人）",
            modelPaths[0],
            "ff5010002ca7b42d6055691d9f781a1dfa3a1266abb2ac32a7bf7a565bc62724");
        ThirdPartyAssetRecord characterB = Character(
            "model.kenney.blocky.b",
            "Blocky Character B（深肤色成人）",
            modelPaths[1],
            "4e44fe749cef19bc911a748f53737be78c2db194d78a10ec1fa875811d760645");
        ThirdPartyAssetRecord characterC = Character(
            "model.kenney.blocky.c",
            "Blocky Character C（年长成人）",
            modelPaths[2],
            "4f7067c692d70d24c4f0d8a0fb5acd1a027246e51a93c06a81a883d777a3db0d");
        ThirdPartyAssetRecord animations = new();
        animations.Configure(
            "animation.kenney.blocky.core",
            "Blocky Characters 核心动作集",
            ThirdPartyAssetKind.AnimationSet,
            SourceId,
            modelPaths[0],
            "ff5010002ca7b42d6055691d9f781a1dfa3a1266abb2ac32a7bf7a565bc62724",
            true,
            "idle",
            "walk",
            "sprint",
            "holding-both",
            "holding-both-shoot",
            "die");

        manifest.Configure(
            "content.third_party.character_intake",
            new[] { source },
            new[] { characterA, characterB, characterC, animations },
            new[]
            {
                characterA.StableId,
                characterB.StableId,
                characterC.StableId
            });
        EditorUtility.SetDirty(manifest);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        ThirdPartyLicenseAuditReport report =
            ThirdPartyAssetLicenseAuditor.Audit(manifest);
        if (!report.IsValid)
        {
            throw new InvalidOperationException(
                "Issue #80 授权准入清单未通过：\n" +
                string.Join("\n", report.Entries
                    .Where(entry => entry.Severity ==
                                    ThirdPartyLicenseAuditSeverity.Error)
                    .Select(entry => $"[{entry.Code}] {entry.Message}")));
        }
        Debug.Log(
            $"Issue #80 授权准入清单已生成：{report.ApprovedModelCount} 个角色模型、" +
            $"{report.ApprovedAnimationSetCount} 套动画通过证据校验。");
    }

    private static ThirdPartyAssetRecord Character(
        string id,
        string displayName,
        string path,
        string hash)
    {
        var record = new ThirdPartyAssetRecord();
        record.Configure(
            id,
            displayName,
            ThirdPartyAssetKind.CharacterModel,
            SourceId,
            path,
            hash,
            true);
        return record;
    }

    private static void ConfigureModelImporter(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("授权角色模型缺失。", path);
        }
        AssetDatabase.ImportAsset(
            path,
            ImportAssetOptions.ForceSynchronousImport |
            ImportAssetOptions.ForceUpdate);
        ModelImporter importer = AssetImporter.GetAtPath(path) as ModelImporter;
        if (importer == null)
        {
            throw new InvalidOperationException($"无法读取模型导入器：{path}");
        }
        bool dirty = false;
        if (!importer.importAnimation)
        {
            importer.importAnimation = true;
            dirty = true;
        }
        if (importer.animationType != ModelImporterAnimationType.Generic)
        {
            importer.animationType = ModelImporterAnimationType.Generic;
            dirty = true;
        }
        if (dirty)
        {
            importer.SaveAndReimport();
        }

        ModelImporterClipAnimation[] clips = importer.defaultClipAnimations;
        bool clipsChanged = false;
        foreach (ModelImporterClipAnimation clip in clips)
        {
            bool shouldLoop = clip.name.Equals("idle", StringComparison.OrdinalIgnoreCase) ||
                              clip.name.Equals("walk", StringComparison.OrdinalIgnoreCase) ||
                              clip.name.Equals("sprint", StringComparison.OrdinalIgnoreCase);
            if (clip.loopTime == shouldLoop)
            {
                continue;
            }
            clip.loopTime = shouldLoop;
            clipsChanged = true;
        }
        if (clipsChanged)
        {
            importer.clipAnimations = clips;
            importer.SaveAndReimport();
        }
    }

    private static void EnsureFolder(string path)
    {
        string current = "Assets";
        string[] segments = path.Split('/');
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
