using System;
using System.Collections.Generic;
using System.IO;
using FPS.Networking.Diagnostics;

namespace FPS.Networking.Acceptance
{
    public sealed class Issue100RuntimeArguments
    {
        public string RunId { get; private set; } = string.Empty;
        public Issue100ProcessRole Role { get; private set; }
        public NetworkConditionScenario Scenario { get; private set; }
        public string OutputDirectory { get; private set; } = string.Empty;
        public string AccountId { get; private set; } = string.Empty;
        public string AppearanceId { get; private set; } = string.Empty;
        public string MatchId { get; private set; } = string.Empty;
        public string ReconnectCredential { get; private set; } = string.Empty;
        public bool Reconnect { get; private set; }
        public bool RecordVideo { get; private set; }
        public string VideoPipePath { get; private set; } = string.Empty;
        public int TimeoutSeconds { get; private set; } = 150;

        public bool IsServer => Role == Issue100ProcessRole.DedicatedServer;
        public bool IsClient => !IsServer;

        public static bool IsRequested(IReadOnlyList<string> arguments) =>
            HasFlag(arguments, "-issue100-acceptance");

        public static bool TryParse(IReadOnlyList<string> arguments,
            out Issue100RuntimeArguments options, out string error)
        {
            options = null;
            error = string.Empty;
            if (!IsRequested(arguments))
            {
                error = "缺少 -issue100-acceptance。";
                return false;
            }

            string roleText = Value(arguments, "-issue100-role");
            if (!TryRole(roleText, out Issue100ProcessRole role))
            {
                error = "-issue100-role 必须为 server、client-a 或 client-b。";
                return false;
            }

            string scenarioId = Value(arguments, "-issue100-scenario");
            NetworkConditionScenario scenario =
                Issue65NetworkConditionMatrix.Find(scenarioId);
            if (scenario == null)
            {
                error = "-issue100-scenario 不是固定四档网络场景之一。";
                return false;
            }

            string runId = Value(arguments, "-issue100-run-id").Trim();
            string output = Value(arguments, "-issue100-output").Trim();
            string matchId = Value(arguments, "-issue99-match").Trim();
            if (!Identifier(runId) || string.IsNullOrWhiteSpace(output) ||
                !Identifier(matchId))
            {
                error = "run-id、output 和 match-id 必须完整且格式有效。";
                return false;
            }

            string account = Value(arguments, "-issue86-account").Trim();
            if (role != Issue100ProcessRole.DedicatedServer &&
                !Identifier(account))
            {
                error = "客户端必须提供有效的 -issue86-account。";
                return false;
            }

            int timeout = 150;
            string timeoutText = Value(arguments, "-issue100-timeout");
            if (!string.IsNullOrWhiteSpace(timeoutText) &&
                (!int.TryParse(timeoutText, out timeout) || timeout < 30 ||
                 timeout > 900))
            {
                error = "-issue100-timeout 必须为 30—900 秒。";
                return false;
            }

            options = new Issue100RuntimeArguments
            {
                RunId = runId,
                Role = role,
                Scenario = scenario,
                OutputDirectory = Path.GetFullPath(output),
                AccountId = account,
                AppearanceId = Value(arguments,
                    "-issue100-appearance").Trim(),
                MatchId = matchId,
                ReconnectCredential = CredentialValue(arguments,
                    "-issue101-reconnect-ticket",
                    "-issue101-reconnect-ticket-file"),
                Reconnect = HasFlag(arguments, "-issue100-reconnect"),
                RecordVideo = HasFlag(arguments, "-issue100-record-video"),
                VideoPipePath = Value(arguments,
                    "-issue100-video-pipe").Trim(),
                TimeoutSeconds = timeout
            };
            if (options.RecordVideo && role != Issue100ProcessRole.ClientA)
            {
                error = "连续录像只能由 client-a 生成。";
                options = null;
                return false;
            }
            if (options.RecordVideo &&
                string.IsNullOrWhiteSpace(options.VideoPipePath))
            {
                error = "录像模式必须提供 -issue100-video-pipe。";
                options = null;
                return false;
            }
            return true;
        }

        private static bool TryRole(string value, out Issue100ProcessRole role)
        {
            switch (value?.Trim().ToLowerInvariant())
            {
                case "server":
                    role = Issue100ProcessRole.DedicatedServer;
                    return true;
                case "client-a":
                    role = Issue100ProcessRole.ClientA;
                    return true;
                case "client-b":
                    role = Issue100ProcessRole.ClientB;
                    return true;
                default:
                    role = default;
                    return false;
            }
        }

        private static bool Identifier(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 96)
                return false;
            for (int index = 0; index < value.Length; index++)
            {
                char character = value[index];
                if (!char.IsLetterOrDigit(character) && character != '-' &&
                    character != '_' && character != '.') return false;
            }
            return true;
        }

        private static bool HasFlag(IReadOnlyList<string> arguments,
            string key)
        {
            if (arguments == null) return false;
            for (int index = 0; index < arguments.Count; index++)
            {
                if (string.Equals(arguments[index], key,
                        StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private static string Value(IReadOnlyList<string> arguments,
            string key)
        {
            if (arguments == null) return string.Empty;
            for (int index = 0; index < arguments.Count - 1; index++)
            {
                if (string.Equals(arguments[index], key,
                        StringComparison.OrdinalIgnoreCase))
                    return arguments[index + 1] ?? string.Empty;
            }
            return string.Empty;
        }

        private static string CredentialValue(
            IReadOnlyList<string> arguments,
            string directKey,
            string fileKey)
        {
            string path = Value(arguments, fileKey).Trim();
            if (!string.IsNullOrWhiteSpace(path))
            {
                try
                {
                    return File.ReadAllText(Path.GetFullPath(path)).Trim();
                }
                catch (IOException)
                {
                    return string.Empty;
                }
                catch (UnauthorizedAccessException)
                {
                    return string.Empty;
                }
            }
            return Value(arguments, directKey).Trim();
        }
    }
}
