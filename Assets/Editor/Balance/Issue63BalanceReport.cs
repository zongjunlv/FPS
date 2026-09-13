using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using FPS.Balance;
using FPS.Simulation;
using FPS.Simulation.Offline;
using UnityEngine;

namespace FPS.Editor.Balance
{
    [DataContract]
    public sealed class Issue63BalanceReport
    {
        [DataMember(Name = "schemaVersion", Order = 0)]
        public string SchemaVersion { get; set; }

        [DataMember(Name = "rulesVersion", Order = 1)]
        public string RulesVersion { get; set; }

        [DataMember(Name = "policyVersion", Order = 2)]
        public string PolicyVersion { get; set; }

        [DataMember(Name = "contentVersion", Order = 3)]
        public int ContentVersion { get; set; }

        [DataMember(Name = "contentFingerprint", Order = 4)]
        public string ContentFingerprint { get; set; }

        [DataMember(Name = "batchDigest", Order = 5)]
        public string BatchDigest { get; set; }

        [DataMember(Name = "environment", Order = 6)]
        public Issue63EnvironmentDto Environment { get; set; }

        [DataMember(Name = "results", Order = 7)]
        public List<Issue63ResultDto> Results { get; set; } = new();
    }

    [DataContract]
    public sealed class Issue63EnvironmentDto
    {
        [DataMember(Name = "unityVersion", Order = 0)]
        public string UnityVersion { get; set; }

        [DataMember(Name = "operatingSystem", Order = 1)]
        public string OperatingSystem { get; set; }

        [DataMember(Name = "processorCount", Order = 2)]
        public int ProcessorCount { get; set; }

        [DataMember(Name = "batchMode", Order = 3)]
        public bool BatchMode { get; set; }

        [DataMember(Name = "fixedTickRate", Order = 4)]
        public int FixedTickRate { get; set; }

        [DataMember(Name = "parallelism", Order = 5)]
        public int Parallelism { get; set; }

        [DataMember(Name = "runtimeVersion", Order = 6)]
        public string RuntimeVersion { get; set; }

        [DataMember(Name = "gitCommit", Order = 7)]
        public string GitCommit { get; set; }

        [DataMember(Name = "worldModelVersion", Order = 8)]
        public int WorldModelVersion { get; set; }

        [DataMember(Name = "enemyAttackOpportunityBasisPoints", Order = 9)]
        public int EnemyAttackOpportunityBasisPoints { get; set; }
    }

    [DataContract]
    public sealed class Issue63NamedCountDto
    {
        [DataMember(Name = "key", Order = 0)]
        public string Key { get; set; }

        [DataMember(Name = "count", Order = 1)]
        public int Count { get; set; }
    }

    [DataContract]
    public sealed class Issue63DirectorEventDto
    {
        [DataMember(Name = "tick", Order = 0)]
        public long Tick { get; set; }

        [DataMember(Name = "signal", Order = 1)]
        public string Signal { get; set; }

        [DataMember(Name = "enemyTypeId", Order = 2)]
        public string EnemyTypeId { get; set; }

        [DataMember(Name = "role", Order = 3)]
        public string Role { get; set; }

        [DataMember(Name = "requestedCount", Order = 4)]
        public int RequestedCount { get; set; }
    }

    [DataContract]
    public sealed class Issue63EncounterEventDto
    {
        [DataMember(Name = "tick", Order = 0)]
        public long Tick { get; set; }

        [DataMember(Name = "encounterId", Order = 1)]
        public string EncounterId { get; set; }

        [DataMember(Name = "signal", Order = 2)]
        public string Signal { get; set; }

        [DataMember(Name = "phase", Order = 3)]
        public string Phase { get; set; }

        [DataMember(Name = "progress", Order = 4)]
        public int Progress { get; set; }

        [DataMember(Name = "target", Order = 5)]
        public int Target { get; set; }
    }

    [DataContract]
    public sealed class Issue63CombatRuleEventDto
    {
        [DataMember(Name = "eventId", Order = 0)]
        public long EventId { get; set; }

        [DataMember(Name = "tick", Order = 1)]
        public long Tick { get; set; }

        [DataMember(Name = "trigger", Order = 2)]
        public string Trigger { get; set; }

        [DataMember(Name = "ruleId", Order = 3)]
        public string RuleId { get; set; }

        [DataMember(Name = "sourceId", Order = 4)]
        public string SourceId { get; set; }

        [DataMember(Name = "targetId", Order = 5)]
        public string TargetId { get; set; }
    }

    [DataContract]
    public sealed class Issue63ResultDto
    {
        [DataMember(Name = "seed", Order = 0)]
        public long Seed { get; set; }

        [DataMember(Name = "outcome", Order = 1)]
        public string Outcome { get; set; }

        [DataMember(Name = "failureReason", Order = 2)]
        public string FailureReason { get; set; }

        [DataMember(Name = "failureDetail", Order = 3)]
        public string FailureDetail { get; set; }

        [DataMember(Name = "durationTicks", Order = 4)]
        public long DurationTicks { get; set; }

        [DataMember(Name = "enemyTtkTicks", Order = 5)]
        public List<int> EnemyTtkTicks { get; set; } = new();

        [DataMember(Name = "healthDamage", Order = 6)]
        public float HealthDamage { get; set; }

        [DataMember(Name = "armorDamage", Order = 7)]
        public float ArmorDamage { get; set; }

        [DataMember(Name = "ammoSpent", Order = 8)]
        public int AmmoSpent { get; set; }

        [DataMember(Name = "ammoGranted", Order = 9)]
        public int AmmoGranted { get; set; }

        [DataMember(Name = "waveCounts", Order = 10)]
        public List<Issue63NamedCountDto> WaveCounts { get; set; } = new();

        [DataMember(Name = "roleCounts", Order = 11)]
        public List<Issue63NamedCountDto> RoleCounts { get; set; } = new();

        [DataMember(Name = "encounterCounts", Order = 12)]
        public List<Issue63NamedCountDto> EncounterCounts { get; set; } = new();

        [DataMember(Name = "upgradeSelections", Order = 13)]
        public List<string> UpgradeSelections { get; set; } = new();

        [DataMember(Name = "directorEvents", Order = 14)]
        public List<Issue63DirectorEventDto> DirectorEvents { get; set; } = new();

        [DataMember(Name = "encounterEvents", Order = 15)]
        public List<Issue63EncounterEventDto> EncounterEvents { get; set; } = new();

        [DataMember(Name = "anomalyCodes", Order = 16)]
        public List<string> AnomalyCodes { get; set; } = new();

        [DataMember(Name = "deterministicDigest", Order = 17)]
        public string DeterministicDigest { get; set; }

        [DataMember(Name = "combatRuleCounts", Order = 18)]
        public List<Issue63NamedCountDto> CombatRuleCounts { get; set; } = new();

        [DataMember(Name = "combatRuleEvents", Order = 19)]
        public List<Issue63CombatRuleEventDto> CombatRuleEvents { get; set; } = new();

        [DataMember(Name = "combatRuleEffectsExpanded", Order = 20)]
        public bool CombatRuleEffectsExpanded { get; set; }

        [DataMember(Name = "combatRuleAbstractionNote", Order = 21)]
        public string CombatRuleAbstractionNote { get; set; }
    }

    public static class Issue63BalanceReportWriter
    {
        public const string SchemaVersion = "issue63.balance-report.v1";
        public const long ExtremeDurationThresholdTicks = 18000L;
        public const int ExtremeTtkThresholdTicks = 1800;
        public static Issue63BalanceReport Create(
            Issue63PreparedBatch batch,
            IReadOnlyList<OfflineRunResult> results,
            int parallelism)
        {
            if (batch == null) throw new ArgumentNullException(nameof(batch));
            if (results == null) throw new ArgumentNullException(nameof(results));
            OfflineRunResult[] ordered = results
                .OrderBy(result => result.Seed)
                .ToArray();

            var report = new Issue63BalanceReport
            {
                SchemaVersion = SchemaVersion,
                RulesVersion = ordered.Length > 0
                    ? ordered[0].RulesVersion
                    : OfflineBalanceScenario.CurrentRulesVersion,
                PolicyVersion = batch.PolicyVersion,
                ContentVersion = batch.ContentVersion,
                ContentFingerprint = batch.ContentFingerprint,
                BatchDigest = ComputeBatchDigest(ordered),
                Environment = new Issue63EnvironmentDto
                {
                    UnityVersion = Application.unityVersion,
                    OperatingSystem = SystemInfo.operatingSystem,
                    ProcessorCount = SystemInfo.processorCount,
                    BatchMode = Application.isBatchMode,
                    FixedTickRate = ordered.Length > 0
                        ? ordered[0].FixedTickRate
                        : CityNewOfflineBalanceContentAdapter.FixedTickRate,
                    Parallelism = Math.Max(1, parallelism),
                    RuntimeVersion = Environment.Version.ToString(),
                    GitCommit = ResolveGitCommit(),
                    WorldModelVersion = ordered.Length > 0
                        ? ordered[0].WorldModelVersion
                        : OfflineWorldModelSpec.CurrentVersion,
                    EnemyAttackOpportunityBasisPoints = ordered.Length > 0
                        ? ordered[0].EnemyAttackOpportunityBasisPoints
                        : CityNewOfflineBalanceContentAdapter
                            .EnemyAttackOpportunityBasisPoints
                }
            };

            for (int index = 0; index < ordered.Length; index++)
                report.Results.Add(ToResultDto(ordered[index]));
            return report;
        }

        public static void WriteJson(Issue63BalanceReport report, string path)
        {
            WriteObject(report, path);
        }

        public static void WriteMarkdown(
            Issue63BalanceReport report,
            string path,
            double elapsedSeconds)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
            int count = report.Results.Count;
            int victories = report.Results.Count(result =>
                result.Outcome == OfflineRunOutcome.Victory.ToString());
            int depleted = report.Results.Count(result =>
                result.FailureReason == "ResourceExhausted");
            double averageDuration = count == 0
                ? 0d
                : report.Results.Average(result => (double)result.DurationTicks);
            double averageTtk = report.Results
                .SelectMany(result => result.EnemyTtkTicks)
                .DefaultIfEmpty(0)
                .Average(value => value);

            var text = new StringBuilder();
            text.AppendLine("# Issue 63 离线遭遇模拟报告");
            text.AppendLine();
            text.AppendLine("- Seed 数量：" + count);
            text.AppendLine("- 通关率：" +
                (count == 0 ? 0d : (double)victories / count).ToString("P2"));
            text.AppendLine("- 资源枯竭率：" +
                (count == 0 ? 0d : (double)depleted / count).ToString("P2"));
            text.AppendLine("- 平均战斗时长：" +
                averageDuration.ToString("F1") + " Tick");
            text.AppendLine("- 平均 TTK：" + averageTtk.ToString("F1") +
                            " Tick");
            text.AppendLine("- 执行耗时：" + elapsedSeconds.ToString("F3") +
                            " 秒");
            text.AppendLine("- 规则版本：`" + report.RulesVersion + "`");
            text.AppendLine("- 策略版本：`" + report.PolicyVersion + "`");
            text.AppendLine("- 内容指纹：`" + report.ContentFingerprint + "`");
            text.AppendLine("- 批次摘要：`" + report.BatchDigest + "`");
            text.AppendLine("- 世界模型：v" +
                            report.Environment.WorldModelVersion +
                            "，敌人攻击机会 " +
                            (report.Environment
                                .EnemyAttackOpportunityBasisPoints / 100d)
                            .ToString("F1") + "%");
            text.AppendLine();
            text.AppendLine("## 失败分布");
            text.AppendLine();
            foreach (IGrouping<string, Issue63ResultDto> group in
                     report.Results.GroupBy(result => result.FailureReason)
                         .OrderBy(group => group.Key, StringComparer.Ordinal))
            {
                text.AppendLine("- " + group.Key + "：" + group.Count());
            }
            File.WriteAllText(path, text.ToString(), new UTF8Encoding(false));
        }

        public static int WriteReproductions(
            Issue63PreparedBatch batch,
            IReadOnlyList<OfflineRunResult> results,
            string directory)
        {
            if (batch == null) throw new ArgumentNullException(nameof(batch));
            if (results == null) throw new ArgumentNullException(nameof(results));
            var scenarios = batch.Entries.ToDictionary(
                entry => entry.Seed,
                entry => entry.Scenario);
            int written = 0;
            OfflineBalanceReproBundle latest = null;

            foreach (OfflineRunResult result in results.OrderBy(item => item.Seed))
            {
                if (!scenarios.TryGetValue(result.Seed, out OfflineBalanceScenario scenario))
                    continue;
                OfflineReplayVerification verification =
                    OfflineRunReplay.Verify(scenario, result);
                List<string> anomalies = BuildAnomalyCodes(result, verification);
                if (anomalies.Count == 0) continue;

                OfflineBalanceReproBundle reproduction =
                    OfflineBalanceReproBundle.Create(
                        scenario,
                        result,
                        batch.PolicyVersion,
                        batch.ContentFingerprint,
                        anomalies,
                        verification);

                string path = Path.Combine(
                    directory,
                    "seed-" + result.Seed + ".json");
                OfflineBalanceReproBundleCodec.Save(path, reproduction);
                latest = reproduction;
                written++;
            }

            if (latest != null) OfflineBalanceReproStorage.SaveLatest(latest);
            return written;
        }

        private static Issue63ResultDto ToResultDto(OfflineRunResult result)
        {
            var dto = new Issue63ResultDto
            {
                Seed = result.Seed,
                Outcome = result.Outcome.ToString(),
                FailureReason = ToFailureReason(result.FailureReason),
                FailureDetail = result.FailureDetail,
                DurationTicks = result.DurationTicks,
                HealthDamage = result.HealthLost,
                ArmorDamage = result.ArmorLost,
                AmmoSpent = result.AmmoConsumed,
                AmmoGranted = result.AmmoSupplied,
                WaveCounts = ToCounts(result.WaveDistribution),
                RoleCounts = ToCounts(result.RoleDistribution),
                EncounterCounts = ToCounts(result.EncounterDistribution),
                UpgradeSelections = result.UpgradeChoices
                    .Select(choice => choice.UpgradeId)
                    .ToList(),
                DirectorEvents = result.DirectorEvents.Select(item =>
                    new Issue63DirectorEventDto
                    {
                        Tick = item.Tick,
                        Signal = item.Signal.ToString(),
                        EnemyTypeId = item.EnemyTypeId,
                        Role = item.RoleTag,
                        RequestedCount = item.RequestedCount
                    }).ToList(),
                EncounterEvents = result.EncounterEvents.Select(item =>
                    new Issue63EncounterEventDto
                    {
                        Tick = item.Tick,
                        EncounterId = item.EncounterId,
                        Signal = item.Signal.ToString(),
                        Phase = item.Phase.ToString(),
                        Progress = item.Progress,
                        Target = item.Target
                    }).ToList(),
                CombatRuleCounts = ToCounts(
                    result.CombatRuleTriggerDistribution),
                CombatRuleEvents = result.CombatRuleEvents.Select(item =>
                    new Issue63CombatRuleEventDto
                    {
                        EventId = item.EventId,
                        Tick = item.Tick,
                        Trigger = item.Trigger.ToString(),
                        RuleId = item.RuleId,
                        SourceId = item.SourceId,
                        TargetId = item.TargetId
                    }).ToList(),
                CombatRuleEffectsExpanded =
                    result.CombatRuleEffectsExpanded,
                CombatRuleAbstractionNote =
                    result.CombatRuleAbstractionNote,
                DeterministicDigest = result.Digest
            };

            dto.EnemyTtkTicks = result.EnemyTtkTicks.ToList();
            dto.AnomalyCodes = BuildAnomalyCodes(result, null);
            return dto;
        }

        private static List<Issue63NamedCountDto> ToCounts(
            IReadOnlyList<OfflineNamedCount> counts)
        {
            return counts.Select(item => new Issue63NamedCountDto
                {
                    Key = item.Id,
                    Count = item.Count
                })
                .OrderBy(item => item.Key, StringComparer.Ordinal)
                .ToList();
        }

        private static List<string> BuildAnomalyCodes(
            OfflineRunResult result,
            OfflineReplayVerification verification)
        {
            var codes = new List<string>();
            if (result.FailureReason != OfflineFailureReason.None)
                codes.Add(ToFailureReason(result.FailureReason));
            if (result.DurationTicks > ExtremeDurationThresholdTicks)
                codes.Add("ExtremeDuration");
            if (result.EnemyTtkTicks.Any(
                    ttk => ttk > ExtremeTtkThresholdTicks))
            {
                codes.Add("ExtremeTtk");
            }
            if (verification != null && !verification.IsMatch)
                codes.Add("ReplayDivergence");
            return codes.Distinct(StringComparer.Ordinal).ToList();
        }

        private static string ToFailureReason(OfflineFailureReason reason)
        {
            return reason == OfflineFailureReason.ResourceDepleted
                ? "ResourceExhausted"
                : reason.ToString();
        }

        private static string ComputeBatchDigest(
            IReadOnlyList<OfflineRunResult> results)
        {
            var canonical = new StringBuilder();
            for (int index = 0; index < results.Count; index++)
            {
                canonical.Append(results[index].Seed)
                    .Append(':')
                    .Append(results[index].Digest)
                    .Append('\n');
            }
            using SHA256 hash = SHA256.Create();
            byte[] bytes = hash.ComputeHash(
                Encoding.UTF8.GetBytes(canonical.ToString()));
            var text = new StringBuilder(bytes.Length * 2);
            for (int index = 0; index < bytes.Length; index++)
                text.Append(bytes[index].ToString("x2"));
            return text.ToString();
        }

        private static string ResolveGitCommit()
        {
            string fromCi = Environment.GetEnvironmentVariable("GITHUB_SHA");
            if (!string.IsNullOrWhiteSpace(fromCi)) return fromCi.Trim();

            try
            {
                string projectRoot = Path.GetFullPath(
                    Path.Combine(Application.dataPath, ".."));
                string gitEntry = FindGitEntry(projectRoot);
                if (string.IsNullOrEmpty(gitEntry)) return string.Empty;
                string gitDirectory = gitEntry;
                if (File.Exists(gitEntry))
                {
                    string pointer = File.ReadAllText(gitEntry).Trim();
                    const string prefix = "gitdir:";
                    if (!pointer.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        return string.Empty;
                    string path = pointer.Substring(prefix.Length).Trim();
                    gitDirectory = Path.GetFullPath(Path.IsPathRooted(path)
                        ? path
                        : Path.Combine(projectRoot, path));
                }

                string headPath = Path.Combine(gitDirectory, "HEAD");
                if (!File.Exists(headPath)) return string.Empty;
                string head = File.ReadAllText(headPath).Trim();
                const string refPrefix = "ref:";
                if (!head.StartsWith(refPrefix, StringComparison.OrdinalIgnoreCase))
                    return head;
                string reference = head.Substring(refPrefix.Length).Trim();
                string referencePath = Path.Combine(
                    gitDirectory,
                    reference.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(referencePath))
                    return File.ReadAllText(referencePath).Trim();

                string packedRefs = Path.Combine(gitDirectory, "packed-refs");
                if (!File.Exists(packedRefs)) return string.Empty;
                foreach (string line in File.ReadLines(packedRefs))
                {
                    if (line.StartsWith("#", StringComparison.Ordinal) ||
                        line.StartsWith("^", StringComparison.Ordinal))
                    {
                        continue;
                    }
                    string suffix = " " + reference;
                    if (line.EndsWith(suffix, StringComparison.Ordinal))
                        return line.Substring(0, line.Length - suffix.Length);
                }
            }
            catch (IOException)
            {
                return string.Empty;
            }
            catch (UnauthorizedAccessException)
            {
                return string.Empty;
            }

            return string.Empty;
        }

        private static string FindGitEntry(string startDirectory)
        {
            var current = new DirectoryInfo(startDirectory);
            for (int depth = 0; current != null && depth < 6; depth++)
            {
                string candidate = Path.Combine(current.FullName, ".git");
                if (Directory.Exists(candidate) || File.Exists(candidate))
                    return candidate;
                current = current.Parent;
            }
            return string.Empty;
        }

        private static void WriteObject<T>(T value, string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
            var settings = new DataContractJsonSerializerSettings
            {
                EmitTypeInformation = EmitTypeInformation.Never,
                UseSimpleDictionaryFormat = true
            };
            using var stream = new FileStream(
                path,
                FileMode.Create,
                FileAccess.Write,
                FileShare.Read);
            new DataContractJsonSerializer(typeof(T), settings)
                .WriteObject(stream, value);
            stream.WriteByte((byte)'\n');
        }
    }
}
