using System.Collections.Generic;
using FPS.Core.GameModes;
using FPS.Networking.Netcode;
using FPS.Networking.Session;
using Unity.Netcode;
using UnityEngine;

public sealed class NetworkCombatFeedbackBootstrap : MonoBehaviour
{
    private readonly HashSet<NetworkPlayerReplica> configured = new();
    private float nextScanTime;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        if (GameModeContext.RequestedMode != GameModeId.Coop &&
            !CoopSessionRuntimeBootstrap.ShouldInstallForCurrentMode)
            return;
        if (FindFirstObjectByType<NetworkCombatFeedbackBootstrap>() != null)
            return;
        new GameObject("Network Combat Feedback Bootstrap")
            .AddComponent<NetworkCombatFeedbackBootstrap>();
    }

    private void Update()
    {
        // A headless dedicated server owns shot results, not their visual
        // presentation. Creating effect pools here repeatedly looks up
        // shaders that are stripped from Server builds.
        NetworkManager manager = NetworkManager.Singleton;
        if (manager != null && manager.IsServer && !manager.IsClient)
            return;
        if (Time.unscaledTime < nextScanTime) return;
        nextScanTime = Time.unscaledTime + 0.25f;
        NetworkPlayerReplica[] replicas =
            FindObjectsByType<NetworkPlayerReplica>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);
        PlayerCombatController localCombat =
            FindFirstObjectByType<PlayerCombatController>();
        for (int index = 0; index < replicas.Length; index++)
        {
            NetworkPlayerReplica replica = replicas[index];
            if (replica == null || configured.Contains(replica)) continue;
            NetworkCombatFeedbackPresenter presenter =
                replica.GetComponent<NetworkCombatFeedbackPresenter>() ??
                replica.gameObject.AddComponent<
                    NetworkCombatFeedbackPresenter>();
            presenter.Configure(replica,
                replica.IsLocallyControlled ? localCombat : null);
            configured.Add(replica);
        }
        configured.RemoveWhere(value => value == null);
    }
}
