using System;
using FPS.Networking.Netcode;
using UnityEngine;

namespace FPS.Networking.Session
{
    public static class CoopSessionRuntimeBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void InstallOptionalOverlay()
        {
            if (UnityEngine.Object.FindFirstObjectByType<
                    CoopSessionController>() != null)
            {
                return;
            }

            var runtime = new GameObject("Optional Coop Session");
            runtime.AddComponent<CoopSessionController>();
            runtime.AddComponent<CoopSessionOverlay>();
            UnityEngine.Object.DontDestroyOnLoad(runtime);
            TryAutoStart(runtime.GetComponent<CoopSessionController>());
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
            controller.StartDirect(
                string.Equals(role, "host",
                    StringComparison.OrdinalIgnoreCase),
                settings);
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
