using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public sealed class Issue80ThirdPartyLicenseTests
{
    private ThirdPartyAssetLicenseManifest manifest;

    [SetUp]
    public void SetUp()
    {
        manifest = AssetDatabase.LoadAssetAtPath<
            ThirdPartyAssetLicenseManifest>(
            ThirdPartyAssetLicenseManifest.DefaultAssetPath);
    }

    [Test]
    public void FormalRosterContainsThreeSeparatelyTrackedLicensedCharacters()
    {
        Assert.That(manifest, Is.Not.Null);
        Assert.That(manifest.Sources.Count, Is.GreaterThanOrEqualTo(1));
        Assert.That(manifest.FormalCharacterStableIds.Count,
            Is.GreaterThanOrEqualTo(3));
        Assert.That(manifest.FormalCharacterStableIds.Distinct().Count(),
            Is.EqualTo(manifest.FormalCharacterStableIds.Count));

        foreach (string id in manifest.FormalCharacterStableIds)
        {
            ThirdPartyAssetRecord record = manifest.Assets.SingleOrDefault(
                asset => asset.StableId == id);
            Assert.That(record, Is.Not.Null, id);
            Assert.That(record.Kind, Is.EqualTo(ThirdPartyAssetKind.CharacterModel), id);
            Assert.That(record.ApprovedForProduction, Is.True, id);
            Assert.That(File.Exists(record.AssetPath), Is.True, record.AssetPath);
        }
    }

    [Test]
    public void AnimationAuthorizationIsSeparateAndIncludesCoreClips()
    {
        ThirdPartyAssetRecord animation = manifest.Assets.SingleOrDefault(
            asset => asset.Kind == ThirdPartyAssetKind.AnimationSet);
        Assert.That(animation, Is.Not.Null);
        Assert.That(manifest.FormalCharacterStableIds,
            Does.Not.Contain(animation.StableId));
        CollectionAssert.IsSubsetOf(
            new[]
            {
                "idle", "walk", "sprint", "holding-both",
                "holding-both-shoot", "die"
            },
            animation.RequiredSubAssets);

        string[] imported = AssetDatabase.LoadAllAssetsAtPath(animation.AssetPath)
            .OfType<AnimationClip>()
            .Select(clip => clip.name.ToLowerInvariant())
            .ToArray();
        foreach (string required in animation.RequiredSubAssets)
        {
            Assert.That(imported, Does.Contain(required.ToLowerInvariant()), required);
        }
    }

    [Test]
    public void OfficialManifestPassesEvidenceAndHashAudit()
    {
        ThirdPartyLicenseAuditReport report =
            ThirdPartyAssetLicenseAuditor.Audit(manifest);
        string details = string.Join(
            Environment.NewLine,
            report.Entries.Select(entry =>
                $"[{entry.Code}] {entry.SubjectId}: {entry.Message}"));
        Assert.That(report.IsValid, Is.True, details);
        Assert.That(report.ApprovedModelCount, Is.GreaterThanOrEqualTo(3));
        Assert.That(report.ApprovedAnimationSetCount, Is.GreaterThanOrEqualTo(1));
    }

    [Test]
    public void NonCommercialOrUnclearLicensesAreRejected()
    {
        ThirdPartyAssetLicenseManifest invalid = ScriptableObject.CreateInstance<
            ThirdPartyAssetLicenseManifest>();
        try
        {
            var source = new ThirdPartySourceRecord();
            source.Configure(
                "source.invalid",
                "禁止准入资源",
                "Unknown",
                "1",
                "https://example.com/source",
                "https://example.com/download",
                "CC-BY-NC-4.0",
                "https://example.com/license",
                "Assets/missing-license.txt",
                new string('a', 64),
                "Assets/missing-legal-code.txt",
                new string('b', 64),
                "2026-09-16",
                new string('c', 64),
                false,
                false,
                false,
                false,
                false);
            invalid.Configure(
                "content.invalid",
                new[] { source },
                Array.Empty<ThirdPartyAssetRecord>(),
                Array.Empty<string>());

            ThirdPartyLicenseAuditReport report =
                ThirdPartyAssetLicenseAuditor.Audit(invalid);
            Assert.That(report.IsValid, Is.False);
            Assert.That(report.Entries.Select(entry => entry.Code),
                Does.Contain("LICENSE_NOT_PERMITTED"));
            Assert.That(report.Entries.Select(entry => entry.Code),
                Does.Contain("LICENSE_RIGHTS_INSUFFICIENT"));
            Assert.That(report.Entries.Select(entry => entry.Code),
                Does.Contain("THIRD_PARTY_SOURCE_UNVERIFIED"));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(invalid);
        }
    }

    [Test]
    public void MissingEvidenceBlocksFormalCharacterRoster()
    {
        ThirdPartyAssetLicenseManifest invalid = ScriptableObject.CreateInstance<
            ThirdPartyAssetLicenseManifest>();
        try
        {
            var source = new ThirdPartySourceRecord();
            source.Configure(
                "source.unverified",
                "证据缺失来源",
                "Author",
                "1.0",
                "https://example.com/source",
                "https://example.com/download",
                "CC0-1.0",
                "https://example.com/license",
                "Assets/missing-license.txt",
                new string('a', 64),
                "Assets/missing-legal-code.txt",
                new string('b', 64),
                "2026-09-16",
                new string('c', 64),
                true,
                true,
                true,
                false,
                true);
            ThirdPartyAssetRecord model = new();
            model.Configure(
                "model.unverified",
                "证据缺失角色",
                ThirdPartyAssetKind.CharacterModel,
                source.StableId,
                "Assets/missing-character.fbx",
                new string('d', 64),
                true);
            invalid.Configure(
                "content.invalid",
                new[] { source },
                new[] { model },
                new[] { model.StableId, model.StableId, model.StableId });

            ThirdPartyLicenseAuditReport report =
                ThirdPartyAssetLicenseAuditor.Audit(invalid);
            Assert.That(report.IsValid, Is.False);
            Assert.That(report.ApprovedModelCount, Is.Zero);
            Assert.That(report.Entries.Select(entry => entry.Code),
                Does.Contain("LICENSE_EVIDENCE_MISSING"));
            Assert.That(report.Entries.Select(entry => entry.Code),
                Does.Contain("FORMAL_CHARACTER_BLOCKED"));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(invalid);
        }
    }
}
