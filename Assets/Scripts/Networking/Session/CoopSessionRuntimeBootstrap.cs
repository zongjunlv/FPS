using System;
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
                return existing;
            }

            var runtime = new GameObject("Optional Coop Session");
            runtime.AddComponent<CoopSessionController>();
            runtime.AddComponent<CoopSessionOverlay>();
            runtime.AddComponent<CoopEconomyHudPresenter>();
            runtime.AddComponent<CoopMissionHudPresenter>();
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
            if (string.Equals(role, "client", StringComparison.OrdinalIgnoreCase))
            {
                string account = Argument("-issue86-account");
                string version = Argument("-issue86-version");
                if (string.IsNullOrWhiteSpace(version)) version = "local-dev";
                if (!string.IsNullOrWhiteSpace(account) &&
                    CoopAdmissionEnvironment.TryCreateCodec(
                        out CoopConnectionTicketCodec codec, out _))
                {
                    string ticket = codec.Issue(account, version,
                        DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                    controller.ConfigureConnectionCredential(ticket);
                }
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
    }
}
