using System;
using System.Collections.Generic;
using System.Linq;

public enum ThirdPersonWeaponCalibrationSeverity
{
    Warning,
    Error
}

public readonly struct ThirdPersonWeaponCalibrationIssue
{
    public ThirdPersonWeaponCalibrationIssue(
        string code,
        ThirdPersonWeaponCalibrationSeverity severity,
        string message)
    {
        Code = code ?? string.Empty;
        Severity = severity;
        Message = message ?? string.Empty;
    }

    public string Code { get; }
    public ThirdPersonWeaponCalibrationSeverity Severity { get; }
    public string Message { get; }
}

public sealed class ThirdPersonWeaponCalibrationAuditReport
{
    public ThirdPersonWeaponCalibrationAuditReport(
        IEnumerable<ThirdPersonWeaponCalibrationIssue> issues)
    {
        Issues = issues?.ToArray() ??
                 Array.Empty<ThirdPersonWeaponCalibrationIssue>();
    }

    public IReadOnlyList<ThirdPersonWeaponCalibrationIssue> Issues { get; }
    public bool IsValid => Issues.All(issue =>
        issue.Severity != ThirdPersonWeaponCalibrationSeverity.Error);
}

public static class ThirdPersonWeaponCalibrationAuditor
{
    public static ThirdPersonWeaponCalibrationAuditReport Audit(
        ThirdPersonWeaponCatalog weapons,
        PlayerAppearanceCatalog appearances,
        ThirdPersonWeaponCalibrationMatrix matrix)
    {
        var issues = new List<ThirdPersonWeaponCalibrationIssue>();
        string catalogError = string.Empty;
        if (weapons == null || !weapons.TryValidate(out catalogError))
        {
            issues.Add(Error("WEAPON_CATALOG_INVALID", catalogError));
            return new ThirdPersonWeaponCalibrationAuditReport(issues);
        }
        string appearanceError = string.Empty;
        if (appearances == null ||
            !appearances.TryValidate(out appearanceError))
        {
            issues.Add(Error("APPEARANCE_CATALOG_INVALID", appearanceError));
            return new ThirdPersonWeaponCalibrationAuditReport(issues);
        }
        if (matrix == null)
        {
            issues.Add(Error("CALIBRATION_MATRIX_MISSING",
                "缺少三角色与武器校准矩阵。"));
            return new ThirdPersonWeaponCalibrationAuditReport(issues);
        }

        var pairs = new HashSet<string>(StringComparer.Ordinal);
        foreach (ThirdPersonWeaponCalibrationEntry entry in matrix.Entries)
        {
            string key = $"{entry.AppearanceId}|{entry.WeaponId}";
            if (!pairs.Add(key))
                issues.Add(Error("CALIBRATION_PAIR_DUPLICATE",
                    $"校准矩阵包含重复组合：{key}"));
        }
        foreach (PlayerAppearanceDefinition appearance in
                 appearances.Definitions)
        foreach (ThirdPersonWeaponDefinition weapon in weapons.Definitions)
        {
            string key = $"{appearance.StableId}|{weapon.StableId}";
            if (!pairs.Contains(key))
                issues.Add(Error("CALIBRATION_PAIR_MISSING",
                    $"缺少角色/武器校准组合：{key}"));
        }
        return new ThirdPersonWeaponCalibrationAuditReport(issues);
    }

    private static ThirdPersonWeaponCalibrationIssue Error(
        string code,
        string message) => new(code,
        ThirdPersonWeaponCalibrationSeverity.Error,
        string.IsNullOrWhiteSpace(message) ? "校准数据无效。" : message);
}
