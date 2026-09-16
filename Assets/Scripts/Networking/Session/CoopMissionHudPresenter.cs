using System;
using FPS.Networking.Domain;
using FPS.Networking.Netcode;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FPS.Networking.Session
{
    /// <summary>
    /// Read-only projection of the authoritative co-op mission snapshot.
    /// Interaction keys submit intents; this view never advances the mission.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CoopMissionHudPresenter : MonoBehaviour
    {
        private NetworkCoopSessionAuthority authority;
        private NetworkPlayerReplica localPlayer;
        private CoopSessionController session;
        private int presentedRunGeneration = -1;
        private bool outcomeVisible;

        public string ObjectiveText => authority == null
            ? string.Empty
            : Objective(authority.WorldState);
        public bool OutcomeVisible => outcomeVisible;

        private void Update()
        {
            ResolveBindings();
            if (authority == null || localPlayer == null) return;
            NetcodeWorldState world = authority.WorldState;
            if (presentedRunGeneration != world.RunGeneration)
            {
                presentedRunGeneration = world.RunGeneration;
                outcomeVisible = false;
                if (localPlayer.IsLocallyControlled)
                {
                    Cursor.lockState = CursorLockMode.Locked;
                    Cursor.visible = false;
                }
            }

            outcomeVisible = world.MissionPhase ==
                    AuthoritativeMissionPhase.Victory ||
                world.MissionPhase == AuthoritativeMissionPhase.Defeat;
            if (outcomeVisible)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                return;
            }

            Keyboard keyboard = Keyboard.current;
            if (keyboard == null || !keyboard.eKey.isPressed) return;
            if (TryDownedTeammate(out NetcodePlayerState teammate) &&
                InRange(localPlayer.PresentedPosition, teammate.Position,
                    world.ReviveRadius))
            {
                localPlayer.SubmitMissionAction(
                    AuthoritativeMissionCommandKind.HoldRevive,
                    teammate.PlayerId);
                return;
            }
            if (world.MissionPhase ==
                    AuthoritativeMissionPhase.ActivateTerminal &&
                InRange(localPlayer.PresentedPosition,
                    world.TerminalPosition, world.TerminalRadius))
            {
                localPlayer.SubmitMissionAction(
                    AuthoritativeMissionCommandKind.HoldTerminal);
                return;
            }
            if (world.MissionPhase == AuthoritativeMissionPhase.Extraction &&
                InRange(localPlayer.PresentedPosition,
                    world.ExtractionPosition, world.ExtractionRadius))
                localPlayer.SubmitMissionAction(
                    AuthoritativeMissionCommandKind.StartExtraction);
        }

        private void OnGUI()
        {
            if (authority == null || localPlayer == null) return;
            NetcodeWorldState world = authority.WorldState;
            DrawObjective(world);
            DrawInteractionHint(world);
            if (outcomeVisible) DrawOutcome(world);
        }

        private void DrawObjective(NetcodeWorldState world)
        {
            const float width = 520f;
            Rect area = new((Screen.width - width) * 0.5f, 18f, width, 70f);
            GUI.Box(area, GUIContent.none);
            GUI.Label(new Rect(area.x + 16f, area.y + 9f,
                width - 32f, 24f), Objective(world));
            float progress = world.MissionPhase switch
            {
                AuthoritativeMissionPhase.ClearEnemies =>
                    world.RequiredKills <= 0 ? 0f :
                    (float)world.KilledTargets / world.RequiredKills,
                AuthoritativeMissionPhase.ActivateTerminal => Ratio(
                    world.TerminalProgressTicks, world.TerminalRequiredTicks),
                AuthoritativeMissionPhase.Extraction => Ratio(
                    world.ExtractionProgressTicks,
                    world.ExtractionRequiredTicks),
                _ => 1f
            };
            Rect bar = new(area.x + 16f, area.y + 42f, width - 32f, 13f);
            GUI.Box(bar, GUIContent.none);
            GUI.Box(new Rect(bar.x + 2f, bar.y + 2f,
                (bar.width - 4f) * Mathf.Clamp01(progress), bar.height - 4f),
                GUIContent.none);
        }

        private void DrawInteractionHint(NetcodeWorldState world)
        {
            string hint = string.Empty;
            if (localPlayer.PresentedLifeState ==
                AuthoritativePlayerLifeState.Downed)
            {
                hint = "你已倒地，等待队友救援";
            }
            else if (TryDownedTeammate(out NetcodePlayerState teammate) &&
                InRange(localPlayer.PresentedPosition, teammate.Position,
                    world.ReviveRadius))
            {
                hint = world.DownedPlayerId == teammate.PlayerId
                    ? $"按住 [E] 救援队友  {Ratio(world.ReviveProgressTicks, world.ReviveRequiredTicks) * 100f:0}%"
                    : "按住 [E] 救援队友";
            }
            else if (world.MissionPhase ==
                    AuthoritativeMissionPhase.ActivateTerminal &&
                InRange(localPlayer.PresentedPosition,
                    world.TerminalPosition, world.TerminalRadius))
            {
                hint = "按住 [E] 接入终端";
            }
            else if (world.MissionPhase == AuthoritativeMissionPhase.Extraction &&
                InRange(localPlayer.PresentedPosition,
                    world.ExtractionPosition, world.ExtractionRadius))
            {
                hint = "按住 [E] 确认撤离，等待全队进入区域";
            }
            if (string.IsNullOrEmpty(hint)) return;
            float width = 430f;
            GUI.Box(new Rect((Screen.width - width) * 0.5f,
                Screen.height - 120f, width, 38f), hint);
        }

        private void DrawOutcome(NetcodeWorldState world)
        {
            float width = Mathf.Min(820f, Screen.width - 60f);
            float height = Mathf.Min(560f, Screen.height - 60f);
            Rect panel = new((Screen.width - width) * 0.5f,
                (Screen.height - height) * 0.5f, width, height);
            GUI.Box(panel, GUIContent.none);
            GUILayout.BeginArea(new Rect(panel.x + 28f, panel.y + 24f,
                panel.width - 56f, panel.height - 48f));
            string title = world.MissionPhase ==
                AuthoritativeMissionPhase.Victory
                ? "任务完成 · 全队撤离"
                : "任务失败 · " + Failure(world.MissionOutcomeReason);
            GUILayout.Label(title);
            GUILayout.Space(18f);
            GUILayout.Label("成员        击杀      造成伤害      承受伤害      强化");
            for (int playerId = 1; playerId <= 2; playerId++)
            {
                if (!authority.TryGetPlayerState(playerId,
                        out NetcodePlayerState player)) continue;
                GUILayout.Label(
                    $"玩家 {playerId}       {player.Kills,3}        " +
                    $"{player.DamageDealt,7:0.#}        " +
                    $"{player.DamageTaken,7:0.#}       " +
                    $"{player.UpgradesSelected,3}");
            }
            GUILayout.FlexibleSpace();
            if (session != null && session.IsHost)
            {
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("开始新战局", GUILayout.Height(46f)))
                {
                    ulong sender = NetworkManager.Singleton != null
                        ? NetworkManager.Singleton.LocalClientId
                        : NetworkManager.ServerClientId;
                    authority.TryRestartMission(sender);
                }
                if (GUILayout.Button("所有成员返回房间",
                        GUILayout.Height(46f)))
                    _ = session.ReturnToLobbyAfterMatchAsync();
                GUILayout.EndHorizontal();
            }
            else
            {
                GUILayout.Label("等待房主开始新战局或返回房间");
            }
            GUILayout.EndArea();
        }

        private bool TryDownedTeammate(out NetcodePlayerState teammate)
        {
            teammate = default;
            if (authority == null || localPlayer == null) return false;
            for (int playerId = 1; playerId <= 2; playerId++)
            {
                if (playerId == localPlayer.PlayerId ||
                    !authority.TryGetPlayerState(playerId,
                        out NetcodePlayerState candidate) ||
                    candidate.LifeState !=
                        AuthoritativePlayerLifeState.Downed) continue;
                teammate = candidate;
                return true;
            }
            return false;
        }

        private void ResolveBindings()
        {
            authority ??= FindFirstObjectByType<NetworkCoopSessionAuthority>();
            session ??= GetComponent<CoopSessionController>();
            if (localPlayer != null) return;
            NetworkPlayerReplica[] players =
                FindObjectsByType<NetworkPlayerReplica>(
                    FindObjectsInactive.Exclude,
                    FindObjectsSortMode.None);
            for (int index = 0; index < players.Length; index++)
                if (players[index].IsLocallyControlled)
                {
                    localPlayer = players[index];
                    break;
                }
        }

        private static string Objective(NetcodeWorldState world) =>
            world.MissionPhase switch
            {
                AuthoritativeMissionPhase.ClearEnemies =>
                    $"任务 1/3 · 清除怪物  {world.KilledTargets}/{world.RequiredKills}",
                AuthoritativeMissionPhase.ActivateTerminal =>
                    "任务 2/3 · 前往并接入控制终端",
                AuthoritativeMissionPhase.Extraction =>
                    "任务 3/3 · 全队进入撤离区域",
                AuthoritativeMissionPhase.Victory => "任务完成",
                AuthoritativeMissionPhase.Defeat => "任务失败",
                _ => string.Empty
            };

        private static string Failure(
            AuthoritativeMissionOutcomeReason reason) => reason switch
        {
            AuthoritativeMissionOutcomeReason.SquadWiped => "小队全灭",
            AuthoritativeMissionOutcomeReason.AllPlayersLeft => "成员已离开",
            _ => "行动终止"
        };

        private static float Ratio(int value, int required) =>
            required <= 0 ? 0f : Mathf.Clamp01((float)value / required);

        private static bool InRange(Vector3 left, Vector3 right, float radius)
        {
            left.y = 0f;
            right.y = 0f;
            return (left - right).sqrMagnitude <= radius * radius;
        }
    }
}
