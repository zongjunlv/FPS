using System;
using System.IO;
using FPS.Networking.Netcode;
using UnityEngine;

namespace FPS.Networking.Session
{
    public static class CoopSessionRuntimeBootstrap
    {
        public static bool ShouldInstallForCurrentMode =>
            HasCommandLineRole();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void InstallOptionalOverlay()
        {
            if (HasCommandLineRole())
            {
                EnsureForCurrentMode();
            }
        }

        public static CoopSessionController EnsureForCurrentMode()
        {
            CoopSessionController existing =
                UnityEngine.Object.FindFirstObjectByType<
                    CoopSessionController>();
            if (existing != null)
            {
                if (existing.GetComponent<CoopEconomyHudPresenter>() == null)
                    existing.gameObject.AddComponent<CoopEconomyHudPresenter>();
                if (existing.GetComponent<CoopMissionHudPresenter>() == null)
                    existing.gameObject.AddComponent<CoopMissionHudPresenter>();
                if (existing.GetComponent<CoopReconnectPresentationGate>() ==
                    null)
                    existing.gameObject.AddComponent<
                        CoopReconnectPresentationGate>();
                return existing;
            }

            var runtime = new GameObject("Optional Coop Session");
            runtime.AddComponent<CoopSessionController>();
            runtime.AddComponent<CoopSessionOverlay>();
            runtime.AddComponent<CoopEconomyHudPresenter>();
            runtime.AddComponent<CoopMissionHudPresenter>();
            runtime.AddComponent<CoopReconnectPresentationGate>();
            UnityEngine.Object.DontDestroyOnLoad(runtime);
            CoopSessionController controller =
                runtime.GetComponent<CoopSessionController>();
            TryAutoStart(controller);
            return controller;
        }

        private static void TryAutoStart(CoopSessionController controller)
        {
            string role = Argument("-issue65-role");
            if (!string.Equals(role, "host",
                    StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(role, "client",
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            ushort port = NetworkEndpointSettings.DefaultPort;
            if (ushort.TryParse(Argument("-issue65-port"), out ushort parsed))
            {
                port = parsed;
            }

            NetworkEndpointSettings settings =
                NetworkEndpointSettings.Localhost;
            settings.Port = port;
            string address = Argument("-issue65-address");
            if (!string.IsNullOrWhiteSpace(address))
                settings.Address = address.Trim();
            if (string.Equals(role, "client", StringComparison.OrdinalIgnoreCase))
            {
                string account = Argument("-issue86-account");
                string version = Argument("-issue86-version");
                if (string.IsNullOrWhiteSpace(version)) version = "local-dev";
                string protocol = Argument("-issue101-protocol-version");
                if (string.IsNullOrWhiteSpace(protocol)) protocol = "1";
                string content = Argument("-issue101-content-version");
                if (string.IsNullOrWhiteSpace(content)) content = "citynew-v1";
                var localCompatibility = new CoopBuildCompatibility(version,
                    protocol, content);
                string ticket = CredentialArgument(
                    "-issue86-ticket", "-issue86-ticket-file");
                if (string.IsNullOrWhiteSpace(ticket) &&
                    !string.IsNullOrWhiteSpace(account) &&
                    CoopAdmissionEnvironment.TryCreateCodec(
                        out CoopConnectionTicketCodec codec, out _))
                {
                    ticket = codec.Issue(account, localCompatibility,
                        DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                        matchId: Argument("-issue99-match"));
                }

                string serverVersion = Argument(
                    "-issue101-server-version");
                string serverProtocol = Argument(
                    "-issue101-server-protocol-version");
                string serverContent = Argument(
                    "-issue101-server-content-version");
                if (!string.IsNullOrWhiteSpace(serverVersion) ||
                    !string.IsNullOrWhiteSpace(serverProtocol) ||
                    !string.IsNullOrWhiteSpace(serverContent))
                {
                    var connection = new RemoteMatchConnectionInfo
                    {
                        host = settings.Address,
                        port = settings.Port,
                        matchId = Argument("-issue99-match"),
                        applicationVersion = serverVersion,
                        protocolVersion = serverProtocol,
                        contentVersion = serverContent,
                        maximumPlayers = 2
                    };
                    controller.StartRemote(connection, localCompatibility,
                        ticket);
                    return;
                }
                controller.ConfigureConnectionCredential(ticket);
            }
            controller.StartDirect(
                string.Equals(role, "host",
                    StringComparison.OrdinalIgnoreCase),
                settings);
        }

        private static bool HasCommandLineRole()
        {
            string role = Argument("-issue65-role");
            return string.Equals(role, "host", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(role, "client", StringComparison.OrdinalIgnoreCase);
        }

        private static string Argument(string key)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int index = 0; index < args.Length - 1; index++)
            {
                if (string.Equals(args[index], key,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return args[index + 1];
                }
            }

            return string.Empty;
        }

        private static string CredentialArgument(string directKey,
            string fileKey)
        {
            string path = Argument(fileKey);
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
            return Argument(directKey).Trim();
        }
    }
}
