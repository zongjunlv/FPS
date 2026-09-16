using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public enum ThirdPartyLicenseAuditSeverity
{
    Approved,
    Warning,
    Error
}

public sealed class ThirdPartyLicenseAuditEntry
{
    public string Code { get; }
    public string SubjectId { get; }
    public string DisplayName { get; }
    public string Message { get; }
    public string AssetPath { get; }
    public ThirdPartyLicenseAuditSeverity Severity { get; }

    public ThirdPartyLicenseAuditEntry(
        string code,
        string subjectId,
        string displayName,
        string message,
        string assetPath,
        ThirdPartyLicenseAuditSeverity severity)
    {
        Code = code;
        SubjectId = subjectId;
        DisplayName = displayName;
        Message = message;
        AssetPath = assetPath;
        Severity = severity;
    }
}

public sealed class ThirdPartyLicenseAuditReport
{
    private readonly List<ThirdPartyLicenseAuditEntry> entries = new();
    public IReadOnlyList<ThirdPartyLicenseAuditEntry> Entries => entries;
    public bool IsValid => entries.All(entry =>
        entry.Severity != ThirdPartyLicenseAuditSeverity.Error);
    public int ErrorCount => entries.Count(entry =>
        entry.Severity == ThirdPartyLicenseAuditSeverity.Error);
    public int ApprovedModelCount { get; internal set; }
    public int ApprovedAnimationSetCount { get; internal set; }

    internal void Add(
        string code,
        string subjectId,
        string displayName,
        string message,
        string path,
        ThirdPartyLicenseAuditSeverity severity) =>
        entries.Add(new ThirdPartyLicenseAuditEntry(
            code,
            subjectId,
            displayName,
            message,
            path,
            severity));
}

public static class ThirdPartyAssetLicenseAuditor
{
    private static readonly Regex Sha256Pattern = new(
        "^[a-f0-9]{64}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly HashSet<string> PermittedLicenses = new(
        StringComparer.OrdinalIgnoreCase)
    {
        "CC0-1.0",
        "CC-BY-4.0"
    };

    public static ThirdPartyLicenseAuditReport Audit(
        ThirdPartyAssetLicenseManifest manifest = null)
    {
        var report = new ThirdPartyLicenseAuditReport();
        manifest ??= AssetDatabase.LoadAssetAtPath<
            ThirdPartyAssetLicenseManifest>(
            ThirdPartyAssetLicenseManifest.DefaultAssetPath);
        if (manifest == null)
        {
            report.Add(
                "THIRD_PARTY_MANIFEST_MISSING",
                "manifest",
                "第三方授权清单",
                "正式角色授权清单不存在，角色不能进入正式内容。",
                ThirdPartyAssetLicenseManifest.DefaultAssetPath,
                ThirdPartyLicenseAuditSeverity.Error);
            return report;
        }

        string manifestPath = AssetDatabase.GetAssetPath(manifest);
        if (string.IsNullOrWhiteSpace(manifest.StableId))
        {
            Error(report, "THIRD_PARTY_MANIFEST_INVALID", "manifest",
                "第三方授权清单", "stableId 不能为空。", manifestPath);
        }

        Dictionary<string, ThirdPartySourceRecord> sources =
            ValidateSources(manifest, report, manifestPath);
        Dictionary<string, ThirdPartyAssetRecord> assets =
            ValidateAssets(manifest, sources, report, manifestPath);
        ValidateFormalRoster(manifest, sources, assets, report, manifestPath);
        return report;
    }

    public static void AppendTo(ContentValidationReport destination)
    {
        if (destination == null)
        {
            throw new ArgumentNullException(nameof(destination));
        }
        ThirdPartyLicenseAuditReport licenseReport = Audit();
        foreach (ThirdPartyLicenseAuditEntry entry in licenseReport.Entries)
        {
            if (entry.Severity == ThirdPartyLicenseAuditSeverity.Approved)
            {
                continue;
            }
            Object asset = string.IsNullOrWhiteSpace(entry.AssetPath)
                ? null
                : AssetDatabase.LoadMainAssetAtPath(entry.AssetPath);
            destination.Add(
                entry.Code,
                entry.Message,
                asset,
                entry.AssetPath,
                entry.Severity == ThirdPartyLicenseAuditSeverity.Error
                    ? ContentValidationSeverity.Error
                    : ContentValidationSeverity.Warning);
        }
    }

    private static Dictionary<string, ThirdPartySourceRecord> ValidateSources(
        ThirdPartyAssetLicenseManifest manifest,
        ThirdPartyLicenseAuditReport report,
        string manifestPath)
    {
        var sources = new Dictionary<string, ThirdPartySourceRecord>(
            StringComparer.Ordinal);
        foreach (ThirdPartySourceRecord source in manifest.Sources)
        {
            if (source == null)
            {
                Error(report, "THIRD_PARTY_SOURCE_INVALID", "source", "授权来源",
                    "授权来源记录不能为空。", manifestPath);
                continue;
            }
            string id = source.StableId;
            if (string.IsNullOrWhiteSpace(id) || !sources.TryAdd(id, source))
            {
                Error(report, "THIRD_PARTY_SOURCE_ID_INVALID", id, source.DisplayName,
                    "授权来源 stableId 为空或重复。", manifestPath);
                continue;
            }
            int errorsBefore = report.ErrorCount;
            RequireText(report, source, source.DisplayName, "名称", source.DisplayName, manifestPath);
            RequireText(report, source, source.DisplayName, "作者", source.Author, manifestPath);
            RequireText(report, source, source.DisplayName, "版本", source.Version, manifestPath);
            RequireHttps(report, source.StableId, source.DisplayName,
                "来源页面", source.SourcePageUrl, manifestPath);
            RequireHttps(report, source.StableId, source.DisplayName,
                "官方下载地址", source.SourceDownloadUrl, manifestPath);
            RequireHttps(report, source.StableId, source.DisplayName,
                "许可证地址", source.LicenseUrl, manifestPath);
            RequireHash(report, source.StableId, source.DisplayName,
                "下载归档", source.ArchiveSha256, manifestPath);
            RequireDate(source, report, manifestPath);
            ValidateLicensePolicy(source, report, manifestPath);
            ValidateEvidenceFile(
                source.StableId,
                source.DisplayName,
                "随包许可证",
                source.BundledLicensePath,
                source.BundledLicenseSha256,
                report,
                manifestPath);
            ValidateEvidenceFile(
                source.StableId,
                source.DisplayName,
                "许可证法律文本",
                source.LegalCodePath,
                source.LegalCodeSha256,
                report,
                manifestPath);
            if (!source.Verified)
            {
                Error(report, "THIRD_PARTY_SOURCE_UNVERIFIED", source.StableId,
                    source.DisplayName, "授权证据尚未人工确认。", manifestPath);
            }
            if (report.ErrorCount == errorsBefore)
            {
                Approved(report, source.StableId, source.DisplayName,
                    $"来源授权通过：{source.LicenseSpdxId}，允许商业使用、修改与再分发。",
                    manifestPath);
            }
        }
        if (sources.Count == 0)
        {
            Error(report, "THIRD_PARTY_SOURCE_MISSING", "manifest", "第三方授权清单",
                "至少需要一个可追溯授权来源。", manifestPath);
        }
        return sources;
    }

    private static Dictionary<string, ThirdPartyAssetRecord> ValidateAssets(
        ThirdPartyAssetLicenseManifest manifest,
        IReadOnlyDictionary<string, ThirdPartySourceRecord> sources,
        ThirdPartyLicenseAuditReport report,
        string manifestPath)
    {
        var assets = new Dictionary<string, ThirdPartyAssetRecord>(
            StringComparer.Ordinal);
        foreach (ThirdPartyAssetRecord asset in manifest.Assets)
        {
            if (asset == null)
            {
                Error(report, "THIRD_PARTY_ASSET_INVALID", "asset", "第三方资产",
                    "第三方资产记录不能为空。", manifestPath);
                continue;
            }
            string id = asset.StableId;
            if (string.IsNullOrWhiteSpace(id) || !assets.TryAdd(id, asset))
            {
                Error(report, "THIRD_PARTY_ASSET_ID_INVALID", id, asset.DisplayName,
                    "第三方资产 stableId 为空或重复。", manifestPath);
                continue;
            }
            int errorsBefore = report.ErrorCount;
            if (!sources.ContainsKey(asset.SourceStableId))
            {
                Error(report, "THIRD_PARTY_SOURCE_REFERENCE_MISSING", id,
                    asset.DisplayName, "资产未关联到有效的授权来源。", asset.AssetPath);
            }
            RequireText(report, id, asset.DisplayName, "名称", asset.DisplayName, asset.AssetPath);
            RequireHash(report, id, asset.DisplayName, "资产文件", asset.FileSha256, asset.AssetPath);
            ValidateAssetFile(asset, report);
            if (!asset.ApprovedForProduction)
            {
                Error(report, "THIRD_PARTY_ASSET_NOT_APPROVED", id,
                    asset.DisplayName, "资产尚未批准进入正式内容。", asset.AssetPath);
            }
            if (asset.Kind == ThirdPartyAssetKind.AnimationSet)
            {
                ValidateAnimationSet(asset, report);
            }
            if (report.ErrorCount == errorsBefore)
            {
                Approved(report, id, asset.DisplayName,
                    asset.Kind == ThirdPartyAssetKind.CharacterModel
                        ? "角色模型授权、文件与哈希均通过。"
                        : "动画授权、文件、哈希与动作片段均通过。",
                    asset.AssetPath);
            }
        }
        return assets;
    }

    private static void ValidateFormalRoster(
        ThirdPartyAssetLicenseManifest manifest,
        IReadOnlyDictionary<string, ThirdPartySourceRecord> sources,
        IReadOnlyDictionary<string, ThirdPartyAssetRecord> assets,
        ThirdPartyLicenseAuditReport report,
        string manifestPath)
    {
        string[] ids = manifest.FormalCharacterStableIds.ToArray();
        if (ids.Length < 3)
        {
            Error(report, "FORMAL_CHARACTER_COUNT_INSUFFICIENT", "formal-roster",
                "正式角色名单", "正式角色名单至少需要 3 个完整人物模型。", manifestPath);
        }
        if (ids.Distinct(StringComparer.Ordinal).Count() != ids.Length)
        {
            Error(report, "FORMAL_CHARACTER_DUPLICATE", "formal-roster",
                "正式角色名单", "正式角色名单不能包含重复记录。", manifestPath);
        }

        int approvedModels = 0;
        foreach (string id in ids.Distinct(StringComparer.Ordinal))
        {
            if (!assets.TryGetValue(id, out ThirdPartyAssetRecord asset) ||
                asset.Kind != ThirdPartyAssetKind.CharacterModel)
            {
                Error(report, "FORMAL_CHARACTER_REFERENCE_INVALID", id,
                    "正式角色名单", $"'{id}' 不是有效的人物模型记录。", manifestPath);
                continue;
            }
            bool assetHasErrors = report.Entries.Any(entry =>
                entry.SubjectId == id &&
                entry.Severity == ThirdPartyLicenseAuditSeverity.Error);
            bool sourceHasErrors = !sources.ContainsKey(asset.SourceStableId) ||
                report.Entries.Any(entry =>
                    entry.SubjectId == asset.SourceStableId &&
                    entry.Severity == ThirdPartyLicenseAuditSeverity.Error);
            if (!asset.ApprovedForProduction || assetHasErrors || sourceHasErrors)
            {
                Error(report, "FORMAL_CHARACTER_BLOCKED", id, asset.DisplayName,
                    "授权证据或文件校验未通过，禁止进入正式角色名单。", asset.AssetPath);
                continue;
            }
            approvedModels++;
        }

        ThirdPartyAssetRecord[] animations = assets.Values
            .Where(asset => asset.Kind == ThirdPartyAssetKind.AnimationSet)
            .ToArray();
        int approvedAnimations = animations.Count(asset =>
            asset.ApprovedForProduction &&
            !report.Entries.Any(entry =>
                entry.SubjectId == asset.StableId &&
                entry.Severity == ThirdPartyLicenseAuditSeverity.Error) &&
            sources.ContainsKey(asset.SourceStableId) &&
            !report.Entries.Any(entry =>
                entry.SubjectId == asset.SourceStableId &&
                entry.Severity == ThirdPartyLicenseAuditSeverity.Error));
        if (animations.Length == 0)
        {
            Error(report, "ANIMATION_LICENSE_RECORD_MISSING", "animation-roster",
                "动画授权名单", "动画来源必须与人物模型分开记录。", manifestPath);
        }
        else if (approvedAnimations == 0)
        {
            Error(report, "ANIMATION_LICENSE_BLOCKED", "animation-roster",
                "动画授权名单", "没有动画记录通过授权准入校验。", manifestPath);
        }

        report.ApprovedModelCount = approvedModels;
        report.ApprovedAnimationSetCount = approvedAnimations;
        if (approvedModels >= 3 && approvedAnimations > 0)
        {
            Approved(report, "formal-roster", "正式角色名单",
                $"{approvedModels} 个角色模型和 {approvedAnimations} 套动画具备完整证据链。",
                manifestPath);
        }
    }

    private static void ValidateLicensePolicy(
        ThirdPartySourceRecord source,
        ThirdPartyLicenseAuditReport report,
        string manifestPath)
    {
        if (!PermittedLicenses.Contains(source.LicenseSpdxId))
        {
            Error(report, "LICENSE_NOT_PERMITTED", source.StableId,
                source.DisplayName,
                $"许可证 '{source.LicenseSpdxId}' 不在正式内容准入白名单；" +
                "非商业、仅个人、仅查看或不明确许可均拒绝。",
                manifestPath);
        }
        if (!source.CommercialUseAllowed || !source.RedistributionAllowed ||
            !source.ModificationAllowed)
        {
            Error(report, "LICENSE_RIGHTS_INSUFFICIENT", source.StableId,
                source.DisplayName,
                "许可证必须同时明确允许商业使用、修改与随游戏再分发。",
                manifestPath);
        }
        if (source.LicenseSpdxId.Equals("CC-BY-4.0", StringComparison.OrdinalIgnoreCase) &&
            !source.AttributionRequired)
        {
            Error(report, "LICENSE_ATTRIBUTION_INVALID", source.StableId,
                source.DisplayName, "CC-BY-4.0 必须标记署名义务。", manifestPath);
        }
    }

    private static void ValidateAssetFile(
        ThirdPartyAssetRecord asset,
        ThirdPartyLicenseAuditReport report)
    {
        if (string.IsNullOrWhiteSpace(asset.AssetPath) ||
            !File.Exists(asset.AssetPath))
        {
            Error(report, "THIRD_PARTY_FILE_MISSING", asset.StableId,
                asset.DisplayName, "授权清单中的资产文件不存在。", asset.AssetPath);
            return;
        }
        if (Sha256Pattern.IsMatch(asset.FileSha256) &&
            !string.Equals(Hash(asset.AssetPath), asset.FileSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            Error(report, "THIRD_PARTY_HASH_MISMATCH", asset.StableId,
                asset.DisplayName, "资产文件 SHA-256 与授权清单不一致。", asset.AssetPath);
        }
        if (AssetDatabase.LoadMainAssetAtPath(asset.AssetPath) == null)
        {
            Error(report, "THIRD_PARTY_IMPORT_FAILED", asset.StableId,
                asset.DisplayName, "资产文件存在，但 Unity 未能正确导入。", asset.AssetPath);
        }
    }

    private static void ValidateAnimationSet(
        ThirdPartyAssetRecord asset,
        ThirdPartyLicenseAuditReport report)
    {
        if (asset.RequiredSubAssets.Count == 0)
        {
            Error(report, "ANIMATION_CLIPS_MISSING", asset.StableId,
                asset.DisplayName, "动画记录必须声明正式接入所需动作。", asset.AssetPath);
            return;
        }
        HashSet<string> imported = AssetDatabase.LoadAllAssetsAtPath(asset.AssetPath)
            .OfType<AnimationClip>()
            .Where(clip => !clip.name.StartsWith("__preview__", StringComparison.Ordinal))
            .Select(clip => clip.name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (string required in asset.RequiredSubAssets)
        {
            if (!imported.Contains(required))
            {
                Error(report, "ANIMATION_CLIP_NOT_IMPORTED", asset.StableId,
                    asset.DisplayName, $"缺少已授权动画片段 '{required}'。", asset.AssetPath);
            }
        }
    }

    private static void ValidateEvidenceFile(
        string id,
        string displayName,
        string label,
        string path,
        string expectedHash,
        ThirdPartyLicenseAuditReport report,
        string manifestPath)
    {
        RequireHash(report, id, displayName, label, expectedHash, manifestPath);
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            Error(report, "LICENSE_EVIDENCE_MISSING", id, displayName,
                $"{label}文件缺失。", path ?? manifestPath);
            return;
        }
        if (new FileInfo(path).Length < 64)
        {
            Error(report, "LICENSE_EVIDENCE_INVALID", id, displayName,
                $"{label}内容不完整。", path);
        }
        if (Sha256Pattern.IsMatch(expectedHash) &&
            !string.Equals(Hash(path), expectedHash, StringComparison.OrdinalIgnoreCase))
        {
            Error(report, "LICENSE_EVIDENCE_HASH_MISMATCH", id, displayName,
                $"{label} SHA-256 与清单不一致。", path);
        }
    }

    private static void RequireDate(
        ThirdPartySourceRecord source,
        ThirdPartyLicenseAuditReport report,
        string manifestPath)
    {
        if (!DateTime.TryParseExact(
                source.DownloadedOn,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out DateTime date) || date.Date > DateTime.Today)
        {
            Error(report, "DOWNLOAD_DATE_INVALID", source.StableId,
                source.DisplayName, "下载日期必须是有效且不晚于今天的 yyyy-MM-dd。", manifestPath);
        }
    }

    private static void RequireHttps(
        ThirdPartyLicenseAuditReport report,
        string id,
        string displayName,
        string label,
        string value,
        string path)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri uri) ||
            uri.Scheme != Uri.UriSchemeHttps)
        {
            Error(report, "SOURCE_URL_INVALID", id, displayName,
                $"{label}必须是可追溯的 HTTPS 地址。", path);
        }
    }

    private static void RequireHash(
        ThirdPartyLicenseAuditReport report,
        string id,
        string displayName,
        string label,
        string value,
        string path)
    {
        if (!Sha256Pattern.IsMatch(value ?? string.Empty))
        {
            Error(report, "SHA256_INVALID", id, displayName,
                $"{label}必须记录 64 位小写 SHA-256。", path);
        }
    }

    private static void RequireText(
        ThirdPartyLicenseAuditReport report,
        ThirdPartySourceRecord source,
        string displayName,
        string label,
        string value,
        string path) =>
        RequireText(report, source.StableId, displayName, label, value, path);

    private static void RequireText(
        ThirdPartyLicenseAuditReport report,
        string id,
        string displayName,
        string label,
        string value,
        string path)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            Error(report, "THIRD_PARTY_FIELD_MISSING", id, displayName,
                $"{label}不能为空。", path);
        }
    }

    private static string Hash(string path)
    {
        using FileStream stream = File.OpenRead(path);
        using SHA256 sha = SHA256.Create();
        return BitConverter.ToString(sha.ComputeHash(stream))
            .Replace("-", string.Empty)
            .ToLowerInvariant();
    }

    private static void Approved(
        ThirdPartyLicenseAuditReport report,
        string id,
        string displayName,
        string message,
        string path) =>
        report.Add("APPROVED", id, displayName, message, path,
            ThirdPartyLicenseAuditSeverity.Approved);

    private static void Error(
        ThirdPartyLicenseAuditReport report,
        string code,
        string id,
        string displayName,
        string message,
        string path) =>
        report.Add(code, id ?? string.Empty, displayName ?? string.Empty,
            message, path ?? string.Empty, ThirdPartyLicenseAuditSeverity.Error);
}
