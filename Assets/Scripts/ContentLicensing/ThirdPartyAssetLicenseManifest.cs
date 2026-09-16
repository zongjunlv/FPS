using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public enum ThirdPartyAssetKind
{
    CharacterModel,
    AnimationSet
}

[Serializable]
public sealed class ThirdPartySourceRecord
{
    [SerializeField] private string stableId;
    [SerializeField] private string displayName;
    [SerializeField] private string author;
    [SerializeField] private string version;
    [SerializeField] private string sourcePageUrl;
    [SerializeField] private string sourceDownloadUrl;
    [SerializeField] private string licenseSpdxId;
    [SerializeField] private string licenseUrl;
    [SerializeField] private string bundledLicensePath;
    [SerializeField] private string bundledLicenseSha256;
    [SerializeField] private string legalCodePath;
    [SerializeField] private string legalCodeSha256;
    [SerializeField] private string downloadedOn;
    [SerializeField] private string archiveSha256;
    [SerializeField] private bool commercialUseAllowed;
    [SerializeField] private bool redistributionAllowed;
    [SerializeField] private bool modificationAllowed;
    [SerializeField] private bool attributionRequired;
    [SerializeField] private bool verified;

    public string StableId => stableId;
    public string DisplayName => displayName;
    public string Author => author;
    public string Version => version;
    public string SourcePageUrl => sourcePageUrl;
    public string SourceDownloadUrl => sourceDownloadUrl;
    public string LicenseSpdxId => licenseSpdxId;
    public string LicenseUrl => licenseUrl;
    public string BundledLicensePath => bundledLicensePath;
    public string BundledLicenseSha256 => bundledLicenseSha256;
    public string LegalCodePath => legalCodePath;
    public string LegalCodeSha256 => legalCodeSha256;
    public string DownloadedOn => downloadedOn;
    public string ArchiveSha256 => archiveSha256;
    public bool CommercialUseAllowed => commercialUseAllowed;
    public bool RedistributionAllowed => redistributionAllowed;
    public bool ModificationAllowed => modificationAllowed;
    public bool AttributionRequired => attributionRequired;
    public bool Verified => verified;

    public void Configure(
        string id,
        string name,
        string creator,
        string sourceVersion,
        string pageUrl,
        string downloadUrl,
        string spdxId,
        string legalUrl,
        string licensePath,
        string licenseHash,
        string legalPath,
        string legalHash,
        string downloadDate,
        string archiveHash,
        bool permitsCommercialUse,
        bool permitsRedistribution,
        bool permitsModification,
        bool requiresAttribution,
        bool evidenceVerified)
    {
        stableId = Normalize(id);
        displayName = Normalize(name);
        author = Normalize(creator);
        version = Normalize(sourceVersion);
        sourcePageUrl = Normalize(pageUrl);
        sourceDownloadUrl = Normalize(downloadUrl);
        licenseSpdxId = Normalize(spdxId);
        licenseUrl = Normalize(legalUrl);
        bundledLicensePath = Normalize(licensePath);
        bundledLicenseSha256 = Normalize(licenseHash).ToLowerInvariant();
        legalCodePath = Normalize(legalPath);
        legalCodeSha256 = Normalize(legalHash).ToLowerInvariant();
        downloadedOn = Normalize(downloadDate);
        archiveSha256 = Normalize(archiveHash).ToLowerInvariant();
        commercialUseAllowed = permitsCommercialUse;
        redistributionAllowed = permitsRedistribution;
        modificationAllowed = permitsModification;
        attributionRequired = requiresAttribution;
        verified = evidenceVerified;
    }

    private static string Normalize(string value) => value?.Trim() ?? string.Empty;
}

[Serializable]
public sealed class ThirdPartyAssetRecord
{
    [SerializeField] private string stableId;
    [SerializeField] private string displayName;
    [SerializeField] private ThirdPartyAssetKind kind;
    [SerializeField] private string sourceStableId;
    [SerializeField] private string assetPath;
    [SerializeField] private string fileSha256;
    [SerializeField] private bool approvedForProduction;
    [SerializeField] private string[] requiredSubAssets = Array.Empty<string>();

    public string StableId => stableId;
    public string DisplayName => displayName;
    public ThirdPartyAssetKind Kind => kind;
    public string SourceStableId => sourceStableId;
    public string AssetPath => assetPath;
    public string FileSha256 => fileSha256;
    public bool ApprovedForProduction => approvedForProduction;
    public IReadOnlyList<string> RequiredSubAssets => requiredSubAssets ?? Array.Empty<string>();

    public void Configure(
        string id,
        string name,
        ThirdPartyAssetKind assetKind,
        string sourceId,
        string path,
        string sha256,
        bool approved,
        params string[] subAssets)
    {
        stableId = Normalize(id);
        displayName = Normalize(name);
        kind = assetKind;
        sourceStableId = Normalize(sourceId);
        assetPath = Normalize(path);
        fileSha256 = Normalize(sha256).ToLowerInvariant();
        approvedForProduction = approved;
        requiredSubAssets = subAssets?
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray() ?? Array.Empty<string>();
    }

    private static string Normalize(string value) => value?.Trim() ?? string.Empty;
}

[CreateAssetMenu(
    fileName = "ThirdPartyAssetLicenseManifest",
    menuName = "FPS/Content/Third-Party Asset License Manifest")]
public sealed class ThirdPartyAssetLicenseManifest : ScriptableObject
{
    public const string DefaultAssetPath =
        "Assets/Resources/Content/Characters/ThirdPartyAssetLicenseManifest.asset";

    [SerializeField] private string stableId = "content.third_party.character_intake";
    [SerializeField] private ThirdPartySourceRecord[] sources = Array.Empty<ThirdPartySourceRecord>();
    [SerializeField] private ThirdPartyAssetRecord[] assets = Array.Empty<ThirdPartyAssetRecord>();
    [SerializeField] private string[] formalCharacterStableIds = Array.Empty<string>();

    public string StableId => stableId;
    public IReadOnlyList<ThirdPartySourceRecord> Sources => sources ?? Array.Empty<ThirdPartySourceRecord>();
    public IReadOnlyList<ThirdPartyAssetRecord> Assets => assets ?? Array.Empty<ThirdPartyAssetRecord>();
    public IReadOnlyList<string> FormalCharacterStableIds =>
        formalCharacterStableIds ?? Array.Empty<string>();

    public void Configure(
        string id,
        IEnumerable<ThirdPartySourceRecord> sourceRecords,
        IEnumerable<ThirdPartyAssetRecord> assetRecords,
        IEnumerable<string> formalCharacters)
    {
        stableId = id?.Trim() ?? string.Empty;
        sources = sourceRecords?.Where(value => value != null).ToArray() ??
                  Array.Empty<ThirdPartySourceRecord>();
        assets = assetRecords?.Where(value => value != null).ToArray() ??
                 Array.Empty<ThirdPartyAssetRecord>();
        formalCharacterStableIds = formalCharacters?
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .ToArray() ?? Array.Empty<string>();
    }
}
