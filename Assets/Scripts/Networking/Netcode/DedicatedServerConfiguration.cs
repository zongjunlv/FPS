using System;
using System.Collections.Generic;
using System.IO;

namespace FPS.Networking.Netcode
{
    [Serializable]
    public sealed class DedicatedPlayerRosterEntry
    {
        public string AccountId = string.Empty;
        public string AppearanceId = "operative-alpha";
        public int PlayerId;
    }

    [Serializable]
    public sealed class DedicatedServerConfiguration
    {
        public const string CityNewScenePath =
            "Assets/ImportPackages/CSAssets2026/Scenes/CityNew.unity";
        public const int DefaultMaximumPlayers = 2;
        public const int DefaultSeed = 18018;
        public const uint DefaultTickRate = 60;
        public const int DefaultIdleTimeoutSeconds = 120;

        public string MapName = "CityNew";
        public string MapScenePath = CityNewScenePath;
        public ushort Port = NetworkEndpointSettings.DefaultPort;
        public string MatchId = "local-match";
        public int MaximumPlayers = DefaultMaximumPlayers;
        public int Seed = DefaultSeed;
        public string Version = "development";
        public string ProtocolVersion = "1";
        public string ContentVersion = "citynew-v1";
        public uint TickRate = DefaultTickRate;
        public int IdleTimeoutSeconds = DefaultIdleTimeoutSeconds;
        public string DiagnosticsPath = string.Empty;
        public DedicatedPlayerRosterEntry[] PlayerRoster =
            Array.Empty<DedicatedPlayerRosterEntry>();

        public static bool IsRequested(IReadOnlyList<string> arguments)
        {
            if (arguments == null) return false;
            for (int index = 0; index < arguments.Count; index++)
            {
                if (string.Equals(arguments[index], "-fps-server",
                        StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        public static bool TryParse(IReadOnlyList<string> arguments,
            string defaultVersion, string diagnosticsDirectory,
            out DedicatedServerConfiguration configuration,
            out string error)
        {
            configuration = new DedicatedServerConfiguration
            {
                Version = string.IsNullOrWhiteSpace(defaultVersion)
                    ? "development"
                    : defaultVersion.Trim()
            };
            error = string.Empty;
            var options = ReadOptions(arguments);

            string map = Value(options, "-server-map", "CityNew");
            if (!TryResolveMap(map, out configuration.MapName,
                    out configuration.MapScenePath))
            {
                error = $"不支持的服务器地图：{map}。当前可用地图为 CityNew。";
                return false;
            }

            if (!TryUShort(options, "-server-port",
                    NetworkEndpointSettings.DefaultPort,
                    out configuration.Port) || configuration.Port == 0)
            {
                error = "服务器端口必须是 1—65535 的整数。";
                return false;
            }

            configuration.MatchId = Value(options, "-server-match",
                "local-match").Trim();
            if (!IsIdentifier(configuration.MatchId, 3, 64))
            {
                error = "战局 ID 需要包含 3—64 个字母、数字、点、短横线或下划线。";
                return false;
            }

            if (!TryInt(options, "-server-max-players", DefaultMaximumPlayers,
                    out configuration.MaximumPlayers) ||
                configuration.MaximumPlayers < 1 ||
                configuration.MaximumPlayers > 16)
            {
                error = "最大人数必须是 1—16 的整数。";
                return false;
            }

            if (!TryParseRoster(Value(options, "-server-roster", string.Empty),
                    configuration.MaximumPlayers,
                    out configuration.PlayerRoster, out error))
                return false;

            if (!TryInt(options, "-server-seed", DefaultSeed,
                    out configuration.Seed))
            {
                error = "随机种子必须是 32 位整数。";
                return false;
            }

            string version = Value(options, "-server-version",
                configuration.Version).Trim();
            if (!IsIdentifier(version, 1, 64))
            {
                error = "服务器版本需要包含 1—64 个字母、数字、点、短横线或下划线。";
                return false;
            }
            configuration.Version = version;

            configuration.ProtocolVersion = Value(options,
                "-server-protocol-version", "1").Trim();
            if (!IsIdentifier(configuration.ProtocolVersion, 1, 64))
            {
                error = "服务器协议版本需要包含 1—64 个字母、数字、点、短横线或下划线。";
                return false;
            }

            configuration.ContentVersion = Value(options,
                "-server-content-version", "citynew-v1").Trim();
            if (!IsIdentifier(configuration.ContentVersion, 1, 64))
            {
                error = "服务器内容版本需要包含 1—64 个字母、数字、点、短横线或下划线。";
                return false;
            }

            if (!TryUInt(options, "-server-tick-rate", DefaultTickRate,
                    out configuration.TickRate) || configuration.TickRate < 10 ||
                configuration.TickRate > 240)
            {
                error = "服务器 Tick Rate 必须是 10—240 的整数。";
                return false;
            }

            if (!TryInt(options, "-server-idle-timeout",
                    DefaultIdleTimeoutSeconds,
                    out configuration.IdleTimeoutSeconds) ||
                configuration.IdleTimeoutSeconds < 15 ||
                configuration.IdleTimeoutSeconds > 86400)
            {
                error = "服务器空闲回收时间必须是 15—86400 秒的整数。";
                return false;
            }

            string report = Value(options, "-server-diagnostics", string.Empty);
            if (string.IsNullOrWhiteSpace(report))
            {
                string directory = string.IsNullOrWhiteSpace(diagnosticsDirectory)
                    ? "."
                    : diagnosticsDirectory;
                report = Path.Combine(directory,
                    $"server-{configuration.MatchId}-diagnostics.json");
            }
            configuration.DiagnosticsPath = Path.GetFullPath(report);
            return true;
        }

        private static Dictionary<string, string> ReadOptions(
            IReadOnlyList<string> arguments)
        {
            var result = new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);
            if (arguments == null) return result;
            for (int index = 0; index < arguments.Count; index++)
            {
                string current = arguments[index];
                if (string.IsNullOrWhiteSpace(current) ||
                    !current.StartsWith("-server-", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (index + 1 >= arguments.Count ||
                    arguments[index + 1].StartsWith("-"))
                {
                    result[current] = string.Empty;
                    continue;
                }
                result[current] = arguments[++index];
            }
            return result;
        }

        private static string Value(IReadOnlyDictionary<string, string> options,
            string key, string fallback)
        {
            return options.TryGetValue(key, out string value) &&
                   !string.IsNullOrWhiteSpace(value)
                ? value
                : fallback;
        }

        private static bool TryResolveMap(string value, out string name,
            out string path)
        {
            string normalized = value?.Trim() ?? string.Empty;
            if (string.Equals(normalized, "CityNew",
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normalized, CityNewScenePath,
                    StringComparison.OrdinalIgnoreCase))
            {
                name = "CityNew";
                path = CityNewScenePath;
                return true;
            }
            name = string.Empty;
            path = string.Empty;
            return false;
        }

        private static bool IsIdentifier(string value, int minimum, int maximum)
        {
            if (value.Length < minimum || value.Length > maximum) return false;
            for (int index = 0; index < value.Length; index++)
            {
                char character = value[index];
                if (!char.IsLetterOrDigit(character) && character != '.' &&
                    character != '-' && character != '_')
                    return false;
            }
            return true;
        }

        private static bool TryParseRoster(string value, int maximumPlayers,
            out DedicatedPlayerRosterEntry[] roster, out string error)
        {
            roster = Array.Empty<DedicatedPlayerRosterEntry>();
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(value)) return true;
            string[] entries = value.Split(',');
            if (entries.Length > maximumPlayers)
            {
                error = "服务器战局名单不能超过最大玩家数。";
                return false;
            }
            var result = new List<DedicatedPlayerRosterEntry>(entries.Length);
            var accounts = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < entries.Length; index++)
            {
                string[] parts = entries[index].Split('=');
                string account = parts.Length > 0
                    ? parts[0].Trim()
                    : string.Empty;
                string appearance = parts.Length == 2
                    ? parts[1].Trim()
                    : string.Empty;
                if (parts.Length != 2 ||
                    !IsIdentifier(account, 1, 64) ||
                    !IsIdentifier(appearance, 1, 64) ||
                    !accounts.Add(account))
                {
                    error = "服务器战局名单格式无效，应为 account=appearance 并使用唯一账号。";
                    roster = Array.Empty<DedicatedPlayerRosterEntry>();
                    return false;
                }
                result.Add(new DedicatedPlayerRosterEntry
                {
                    AccountId = account,
                    AppearanceId = appearance,
                    PlayerId = index + 1
                });
            }
            roster = result.ToArray();
            return true;
        }

        private static bool TryInt(IReadOnlyDictionary<string, string> values,
            string key, int fallback, out int result)
        {
            if (!values.TryGetValue(key, out string value))
            {
                result = fallback;
                return true;
            }
            return int.TryParse(value, out result);
        }

        private static bool TryUInt(IReadOnlyDictionary<string, string> values,
            string key, uint fallback, out uint result)
        {
            if (!values.TryGetValue(key, out string value))
            {
                result = fallback;
                return true;
            }
            return uint.TryParse(value, out result);
        }

        private static bool TryUShort(IReadOnlyDictionary<string, string> values,
            string key, ushort fallback, out ushort result)
        {
            if (!values.TryGetValue(key, out string value))
            {
                result = fallback;
                return true;
            }
            return ushort.TryParse(value, out result);
        }
    }
}
