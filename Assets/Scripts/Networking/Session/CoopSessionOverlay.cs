using System;
using UnityEngine.InputSystem;
using UnityEngine;
using FPS.Networking.Netcode;

namespace FPS.Networking.Session
{
    /// <summary>Small interview/demo control surface, toggled with F6.</summary>
    [DisallowMultipleComponent]
    public sealed class CoopSessionOverlay : MonoBehaviour
    {
        private const int WindowId = 65065;
        private CoopSessionController controller;
        private Rect windowRect = new(24f, 72f, 390f, 300f);
        private string joinCode = string.Empty;
        private string profile = string.Empty;
        private bool visible;

        public bool IsVisible => visible;

        private void Awake()
        {
            controller = GetComponent<CoopSessionController>();
            profile = ResolveArgument("-coop-profile");
        }

        private void Update()
        {
            if (Keyboard.current == null ||
                !Keyboard.current.f6Key.wasPressedThisFrame) return;
            visible = !visible;
            Cursor.visible = visible;
            Cursor.lockState = visible
                ? CursorLockMode.None
                : CursorLockMode.Locked;
        }

        private void OnGUI()
        {
            if (!visible || controller == null) return;
            windowRect = GUI.Window(
                WindowId,
                windowRect,
                DrawWindow,
                "双人合作切片 · F6 关闭");
        }

        private void DrawWindow(int id)
        {
            GUILayout.Label($"状态：{StateLabel(controller.State)}");
            if (controller.IsConnected)
            {
                GUILayout.Label(controller.IsHost
                    ? (string.IsNullOrEmpty(controller.JoinCode)
                        ? "本地直连主机"
                        : $"加入码：{controller.JoinCode}")
                    : "已加入主机战局");
                GUILayout.Label(
                    $"玩家：{controller.PlayerCount} / " +
                    CoopSessionController.MaximumPlayers);
                DrawAuthoritativeState();
                if (GUILayout.Button("离开合作战局", GUILayout.Height(34f)))
                {
                    _ = controller.LeaveAsync();
                }
            }
            else
            {
                GUILayout.Label("本机身份（同机双开时请填写不同值）");
                profile = GUILayout.TextField(profile, 30);
                if (GUILayout.Button("创建两人战局", GUILayout.Height(34f)))
                {
                    _ = controller.HostAsync(profile);
                }

                GUILayout.Space(8f);
                GUILayout.Label("加入码");
                joinCode = GUILayout.TextField(joinCode, 12);
                if (GUILayout.Button("加入战局", GUILayout.Height(34f)))
                {
                    _ = controller.JoinAsync(joinCode, profile);
                }
            }

            if (!string.IsNullOrWhiteSpace(controller.LastFailure))
            {
                GUILayout.Space(8f);
                GUILayout.Label("失败原因：" + controller.LastFailure);
            }

            GUILayout.FlexibleSpace();
            GUILayout.Label("单人游戏默认不启动网络；只有点击上方按钮才连接。");
            GUI.DragWindow(new Rect(0f, 0f, 10000f, 24f));
        }

        private static void DrawAuthoritativeState()
        {
            NetworkCoopSessionAuthority authority =
                FindFirstObjectByType<NetworkCoopSessionAuthority>();
            if (authority == null)
            {
                GUILayout.Label("权威战局：等待同步……");
                return;
            }

            NetcodeWorldState world = authority.WorldState;
            GUILayout.Label(
                $"权威 Tick：{world.ServerTick}  波次：{world.WaveStatus}");
            for (int index = 0;
                 index < authority.ReplicatedTargetCount;
                 index++)
            {
                NetcodeTargetState target =
                    authority.GetReplicatedTarget(index);
                GUILayout.Label(
                    $"目标 {target.TargetId}：{target.Health:0} HP");
            }

            NetworkPlayerReplica[] replicas =
                FindObjectsByType<NetworkPlayerReplica>(
                    FindObjectsInactive.Exclude,
                    FindObjectsSortMode.None);
            for (int index = 0; index < replicas.Length; index++)
            {
                if (!replicas[index].IsLocallyControlled) continue;
                GUILayout.Label(
                    $"本地校正：{replicas[index].LastPredictionCorrection.Kind} " +
                    $"{replicas[index].LastPredictionCorrection.ErrorDistance:0.000}m");
                break;
            }
        }

        private static string ResolveArgument(string key)
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

        private static string StateLabel(CoopSessionState state)
        {
            return state switch
            {
                CoopSessionState.Offline => "离线",
                CoopSessionState.Initializing => "初始化服务",
                CoopSessionState.Hosting => "创建中",
                CoopSessionState.Joining => "加入中",
                CoopSessionState.Connected => "已连接",
                CoopSessionState.Leaving => "离开中",
                CoopSessionState.Failed => "失败",
                _ => state.ToString()
            };
        }
    }
}
