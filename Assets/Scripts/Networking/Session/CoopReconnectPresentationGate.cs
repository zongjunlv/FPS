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
        public bool MenuOverlayVisible { get; set; }

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
                StatusText(), labelStyle);
            GUI.color = previous;
        }

        private string StatusText()
        {
            if (session == null) return "正在连接专用服务器…";
            if (session.State == CoopSessionState.Failed)
            {
                string reason = string.IsNullOrWhiteSpace(session.LastFailure)
                    ? "专用服务器连接失败。"
                    : session.LastFailure;
                return $"联机战局连接失败\n{reason}\n\n按 ESC 打开菜单并返回模式大厅";
            }
            if (string.Equals(session.LobbyPhase,
                    CoopSessionController.PhaseLoading,
                    System.StringComparison.Ordinal))
                return "正在加载 CityNew 并等待队友…";
            return session.IsBattleTransportConnected
                ? "正在同步服务器战局…"
                : "正在连接专用服务器…";
        }

        private bool ShouldBlock()
        {
            session ??= GetComponent<CoopSessionController>();
            if (MenuOverlayVisible) return false;
            NetworkManager manager = NetworkManager.Singleton;
            bool hasLocalReplica = false;
            bool hasConsumedServerState = false;
            NetworkPlayerReplica[] replicas =
                FindObjectsByType<NetworkPlayerReplica>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None);
            for (int index = 0; index < replicas.Length; index++)
            {
                if (!replicas[index].IsLocallyControlled) continue;
                hasLocalReplica = true;
                hasConsumedServerState =
                    replicas[index].HasConsumedServerState;
                break;
            }

            return ShouldBlockForState(
                session?.State ?? CoopSessionState.Offline,
                session != null && session.HasActiveSession,
                session?.LobbyPhase ?? CoopSessionController.PhaseLobby,
                manager != null && manager.IsClient,
                manager != null && manager.IsConnectedClient,
                hasLocalReplica,
                hasConsumedServerState);
        }

        public static bool ShouldBlockForState(
            CoopSessionState sessionState,
            bool hasActiveSession,
            string sessionPhase,
            bool isNetworkClient,
            bool isConnectedClient,
            bool hasLocalReplica,
            bool hasConsumedServerState)
        {
            if (sessionState == CoopSessionState.Reconnecting) return true;

            bool networkGameplayPhase = hasActiveSession &&
                (string.Equals(sessionPhase,
                     CoopSessionController.PhaseLoading,
                     System.StringComparison.Ordinal) ||
                 string.Equals(sessionPhase,
                     CoopSessionController.PhaseBattle,
                     System.StringComparison.Ordinal));
            if (networkGameplayPhase &&
                sessionState == CoopSessionState.Failed)
                return true;
            if (sessionState != CoopSessionState.Connected) return false;

            // Session/Relay rooms intentionally defer player spawning while
            // members select characters and ready up in the lobby. No replica
            // in this phase is expected and must never trigger a reconnect gate.
            if (hasActiveSession &&
                !string.Equals(sessionPhase,
                    CoopSessionController.PhaseLoading,
                    System.StringComparison.Ordinal) &&
                !string.Equals(sessionPhase,
                    CoopSessionController.PhaseBattle,
                    System.StringComparison.Ordinal))
                return false;

            // During the battle phase a UGS lobby connection alone is not
            // playable. Keep the scene covered until NGO is connected instead
            // of exposing CityNew's legacy local Spider and solo controls.
            if (networkGameplayPhase &&
                (!isNetworkClient || !isConnectedClient))
                return true;
            if (!isNetworkClient || !isConnectedClient) return false;
            return !hasLocalReplica || !hasConsumedServerState;
        }
    }
}
