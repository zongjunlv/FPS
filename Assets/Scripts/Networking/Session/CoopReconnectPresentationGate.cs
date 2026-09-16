using FPS.Networking.Netcode;
using Unity.Netcode;
using UnityEngine;

namespace FPS.Networking.Session
{
    /// <summary>
    /// Covers the default spawn frame until the local replica has consumed its
    /// authoritative baseline. This prevents a reconnect from briefly showing
    /// fresh-run position, health, ammo or HUD values.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CoopReconnectPresentationGate : MonoBehaviour
    {
        private GUIStyle labelStyle;
        private CoopSessionController session;

        public bool IsBlocking => ShouldBlock();

        private void OnGUI()
        {
            if (!ShouldBlock()) return;
            GUI.depth = -32760;
            Color previous = GUI.color;
            GUI.color = Color.black;
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height),
                Texture2D.whiteTexture, ScaleMode.StretchToFill);
            GUI.color = Color.white;
            labelStyle ??= new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 24,
                normal = { textColor = Color.white }
            };
            GUI.Label(new Rect(0f, 0f, Screen.width, Screen.height),
                "正在恢复战局…", labelStyle);
            GUI.color = previous;
        }

        private bool ShouldBlock()
        {
            session ??= GetComponent<CoopSessionController>();
            if (session != null &&
                session.State == CoopSessionState.Reconnecting)
                return true;
            NetworkManager manager = NetworkManager.Singleton;
            if (manager == null || !manager.IsClient ||
                !manager.IsConnectedClient)
                return false;
            NetworkPlayerReplica[] replicas =
                FindObjectsByType<NetworkPlayerReplica>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None);
            for (int index = 0; index < replicas.Length; index++)
            {
                if (!replicas[index].IsLocallyControlled) continue;
                return !replicas[index].HasConsumedServerState;
            }
            return true;
        }
    }
}
