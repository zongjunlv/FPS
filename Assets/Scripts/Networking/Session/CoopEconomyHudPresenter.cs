using System;
using FPS.Networking.Domain;
using FPS.Networking.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FPS.Networking.Session
{
    /// <summary>
    /// Read-only client projection of the server economy snapshot. Buttons
    /// only submit intents; inventory, XP and upgrades change after replication.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CoopEconomyHudPresenter : MonoBehaviour
    {
        private const int InventoryWindowId = 97097;
        private Rect inventoryRect = new(20f, 170f, 330f, 390f);
        private NetworkCoopSessionAuthority authority;
        private NetworkPlayerReplica localPlayer;
        private bool inventoryVisible;

        private void Update()
        {
            ResolveBindings();
            if (authority != null && IsOutcome(authority.WorldState))
            {
                inventoryVisible = false;
                return;
            }
            if (Keyboard.current == null || localPlayer == null) return;
            if (Keyboard.current.iKey.wasPressedThisFrame)
                inventoryVisible = !inventoryVisible;
            if (Keyboard.current.fKey.wasPressedThisFrame &&
                TryNearestDrop(out NetcodeWorldDropState drop))
            {
                localPlayer.SubmitEconomyAction(
                    AuthoritativeEconomyCommandKind.Pickup,
                    entityId: drop.DropId,
                    expectedDropRevision: drop.Revision,
                    expectedItemId: drop.ItemId.ToString());
            }
        }

        private void OnGUI()
        {
            if (authority == null || localPlayer == null ||
                IsOutcome(authority.WorldState) ||
                !authority.TryGetProgression(localPlayer.PlayerId,
                    out NetcodeProgressionState progression))
                return;

            DrawProgression(progression);
            if (TryNearestDrop(out NetcodeWorldDropState drop))
            {
                GUI.Box(new Rect(Screen.width * 0.5f - 145f,
                    Screen.height - 105f, 290f, 36f),
                    $"[F] 拾取 {ItemName(drop.ItemId.ToString())} ×{drop.Quantity}");
            }
            if (inventoryVisible)
                inventoryRect = GUI.Window(InventoryWindowId, inventoryRect,
                    _ => DrawInventory(progression), "联机消耗品背包 · I 关闭");
            if (progression.PendingUpgradeChoices > 0)
                DrawUpgradeChoices(progression);
        }

        private void DrawProgression(NetcodeProgressionState value)
        {
            float width = 280f;
            Rect area = new(20f, 80f, width, 78f);
            GUI.Box(area, GUIContent.none);
            GUI.Label(new Rect(32f, 88f, width - 24f, 24f),
                $"等级 {value.Level}   经验 {value.CurrentExperience} / " +
                (value.ExperienceToNextLevel > 0
                    ? value.ExperienceToNextLevel.ToString()
                    : "MAX"));
            float normalized = value.ExperienceToNextLevel <= 0
                ? 1f
                : Mathf.Clamp01((float)value.CurrentExperience /
                    value.ExperienceToNextLevel);
            GUI.Box(new Rect(32f, 119f, width - 24f, 14f), GUIContent.none);
            GUI.Box(new Rect(34f, 121f,
                (width - 28f) * normalized, 10f), GUIContent.none);
            GUI.Label(new Rect(32f, 136f, width - 24f, 20f),
                $"构筑：{BuildTags(value.BuildTags.ToString())}   [I] 背包");
        }

        private void DrawInventory(NetcodeProgressionState progression)
        {
            GUILayout.Label($"服务器版本 {progression.InventoryRevision}");
            for (int index = 0; index < authority.ReplicatedInventorySlotCount;
                 index++)
            {
                NetcodeInventorySlotState slot =
                    authority.GetReplicatedInventorySlot(index);
                if (slot.PlayerId != localPlayer.PlayerId ||
                    slot.Quantity <= 0) continue;
                GUILayout.BeginHorizontal();
                GUILayout.Label(
                    $"{slot.SlotIndex + 1:00}  {ItemName(slot.ItemId.ToString())} ×{slot.Quantity}",
                    GUILayout.Width(190f));
                if (GUILayout.Button("使用", GUILayout.Width(52f)))
                    localPlayer.SubmitEconomyAction(
                        AuthoritativeEconomyCommandKind.Use,
                        sourceSlot: slot.SlotIndex);
                if (GUILayout.Button("丢弃", GUILayout.Width(52f)))
                    localPlayer.SubmitEconomyAction(
                        AuthoritativeEconomyCommandKind.Drop,
                        sourceSlot: slot.SlotIndex,
                        quantity: 1);
                GUILayout.EndHorizontal();
            }
            GUILayout.Space(8f);
            if (GUILayout.Button("自动整理空位"))
                localPlayer.SubmitEconomyAction(
                    AuthoritativeEconomyCommandKind.Compact);
            GUI.DragWindow(new Rect(0f, 0f, 10000f, 24f));
        }

        private void DrawUpgradeChoices(NetcodeProgressionState value)
        {
            string[] candidates =
            {
                value.Candidate0.ToString(),
                value.Candidate1.ToString(),
                value.Candidate2.ToString()
            };
            float width = 210f;
            float total = candidates.Length * width + 24f;
            GUILayout.BeginArea(new Rect(
                (Screen.width - total) * 0.5f,
                Screen.height * 0.18f,
                total,
                190f));
            GUILayout.Label("选择一项肉鸽强化（由服务器结算）");
            GUILayout.BeginHorizontal();
            for (int index = 0; index < candidates.Length; index++)
            {
                if (string.IsNullOrEmpty(candidates[index])) continue;
                if (GUILayout.Button(
                        UpgradeName(candidates[index]),
                        GUILayout.Width(width - 8f),
                        GUILayout.Height(126f)))
                {
                    localPlayer.SubmitEconomyAction(
                        AuthoritativeEconomyCommandKind.SelectUpgrade,
                        candidateIndex: index);
                }
            }
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        private bool TryNearestDrop(out NetcodeWorldDropState selected)
        {
            selected = default;
            if (authority == null || localPlayer == null) return false;
            float best = 3.25f * 3.25f;
            Vector3 position = localPlayer.PresentedPosition;
            bool found = false;
            for (int index = 0; index < authority.ReplicatedWorldDropCount;
                 index++)
            {
                NetcodeWorldDropState candidate =
                    authority.GetReplicatedWorldDrop(index);
                if (!candidate.Available || candidate.OwnerPlayerId != 0 &&
                    candidate.OwnerPlayerId != localPlayer.PlayerId) continue;
                float distance = (candidate.Position - position).sqrMagnitude;
                if (distance > best) continue;
                best = distance;
                selected = candidate;
                found = true;
            }
            return found;
        }

        private void ResolveBindings()
        {
            if (authority == null)
                authority = FindFirstObjectByType<NetworkCoopSessionAuthority>();
            if (localPlayer != null) return;
            NetworkPlayerReplica[] players =
                FindObjectsByType<NetworkPlayerReplica>(
                    FindObjectsInactive.Exclude,
                    FindObjectsSortMode.None);
            for (int index = 0; index < players.Length; index++)
            {
                if (!players[index].IsLocallyControlled) continue;
                localPlayer = players[index];
                break;
            }
        }

        private static string ItemName(string id) => id switch
        {
            "medical_kit" or "medkit" => "医疗包",
            "armor_pack" or "armor_plate" => "护甲包",
            "rifle_ammo" => "步枪弹药",
            "handgun_ammo" => "手枪弹药",
            _ => id
        };

        private static string UpgradeName(string id) => id switch
        {
            "damage_hardened_rounds" => "硬化弹头\n武器伤害 +25%",
            "damage_overcharged_core" => "过载核心\n武器伤害 +35%",
            "damage_weakpoint_analysis" => "弱点分析\n武器伤害 +20%",
            "fire_rate_rapid_cycling" => "快速循环\n射速 +15%",
            "magazine_extended_capacity" => "扩容弹匣\n容量 +20%",
            "reload_quick_hands" => "快速换弹\n换弹速度 +20%",
            "recoil_dampening" => "后坐阻尼\n后坐控制 +18%",
            "accuracy_tight_grouping" => "密集弹着\n精准 +20%",
            "survival_vitality_reinforcement" => "生命强化\n最大生命 +20%",
            "survival_reinforced_plating" => "强化护甲\n最大护甲 +20%",
            "survival_emergency_treatment" => "紧急治疗\n恢复 30 生命",
            "survival_field_armor_repair" => "战地修甲\n恢复 30 护甲",
            "survival_mobility_training" => "机动训练\n移动速度 +10%",
            _ => id
        };

        private static string BuildTags(string tags) =>
            string.IsNullOrWhiteSpace(tags) ? "尚未形成" :
            tags.Replace("build.", string.Empty).Replace("|", " / ");

        private static bool IsOutcome(NetcodeWorldState world) =>
            world.MissionPhase == AuthoritativeMissionPhase.Victory ||
            world.MissionPhase == AuthoritativeMissionPhase.Defeat;
    }
}
