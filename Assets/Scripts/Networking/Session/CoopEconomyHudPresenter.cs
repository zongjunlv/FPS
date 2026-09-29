using System;
using System.Collections.Generic;
using FPS.Networking.Domain;
using FPS.Networking.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FPS.Networking.Session
{
    /// <summary>
    /// Read-only projection of the server economy into the shared game UI.
    /// Every operation sends an intent; only a complete server snapshot updates
    /// inventory, XP and upgrade levels. Opening a modal never pauses the server.
    /// </summary>
    [DefaultExecutionOrder(-500)]
    [DisallowMultipleComponent]
    public sealed class CoopEconomyHudPresenter : MonoBehaviour
    {
        private const float PickupScrollReleaseDelay = 0.2f;
        private const float WaitingNoticeDelay = 8f;
        private readonly List<NetcodeInventorySlotState> inventory = new();
        private readonly List<NetcodeUpgradeStackState> upgrades = new();
        private readonly List<NetcodeWorldDropState> drops = new();
        private readonly CoopNearbyDropSelection selection = new();
        private readonly CoopEconomyRequestGate requests = new();
        private NetworkCoopSessionAuthority authority;
        private NetworkCoopSessionAuthority presentedAuthority;
        private NetworkPlayerReplica localPlayer;
        private ICoopEconomyPresentation presentation;
        private CoopEconomyPresentationFrame lastCompleteFrame;
        private int presentedRunGeneration = -1;
        private int presentedPlayerId;
        private bool inventoryVisible;
        private bool modalVisible;
        private bool economyCursorOwned;
        private float pickupScrollCaptureUntil;
        private float requestStartedAt;
        private string status = string.Empty;

        public bool InventoryVisible => inventoryVisible;
        public bool RequestPending => requests.IsPending;
        public int SelectedDropId => selection.Selected.DropId;

        private void Update()
        {
            ResolveBindings();
            if (authority == null || localPlayer == null ||
                !localPlayer.HasConsumedServerState ||
                IsOutcome(authority.WorldState))
            {
                HideUnavailablePresentation();
                return;
            }

            NetcodeWorldState world = authority.WorldState;
            if (!authority.IsReplicatedSnapshotComplete)
            {
                if (presentedAuthority == authority &&
                    presentedRunGeneration == world.RunGeneration &&
                    presentedPlayerId == localPlayer.PlayerId &&
                    lastCompleteFrame != null && presentation != null)
                    SuspendForIncompleteSnapshot();
                else
                    HideUnavailablePresentation();
                return;
            }
            if (!authority.TryGetProgression(localPlayer.PlayerId,
                    out NetcodeProgressionState progression))
            {
                HideUnavailablePresentation();
                return;
            }
            if (presentedAuthority != authority ||
                presentedRunGeneration != world.RunGeneration ||
                presentedPlayerId != localPlayer.PlayerId)
            {
                requests.Reset();
                selection.Clear();
                inventoryVisible = false;
                status = string.Empty;
                pickupScrollCaptureUntil = 0f;
                presentation?.Hide();
                lastCompleteFrame = null;
                presentedAuthority = authority;
                presentedRunGeneration = world.RunGeneration;
                presentedPlayerId = localPlayer.PlayerId;
            }

            presentation ??= CoopEconomyPresentationRegistry.Create(
                gameObject, SubmitIntent, CloseInventory);
            if (presentation == null || !presentation.IsReady)
            {
                HideUnavailablePresentation();
                return;
            }

            bool retry = false;
            if (requests.Observe(progression))
            {
                retry = !requests.WasAccepted;
                status = requests.WasAccepted
                    ? "服务器已确认"
                    : "操作未生效：物品或候选已变化，请重新选择";
            }
            else if (requests.IsPending &&
                     Time.unscaledTime - requestStartedAt >= WaitingNoticeDelay)
            {
                // A local timeout cannot prove the server rejected the command.
                // Do not resubmit, unlock it, or apply its effects speculatively.
                status = "仍在等待服务器确认，请勿重复操作";
            }

            bool paused = CoopUiInputGate.PauseMenuVisible;
            bool choosing = progression.PendingUpgradeChoices > 0;
            Keyboard keyboard = Keyboard.current;
            if (!paused && !choosing && keyboard != null &&
                (keyboard.iKey.wasPressedThisFrame ||
                 keyboard.tabKey.wasPressedThisFrame))
                inventoryVisible = !inventoryVisible;
            if (choosing) inventoryVisible = false;
            SetModalVisible(inventoryVisible || choosing);

            ReadSnapshotLists();
            selection.Refresh(drops, localPlayer.PlayerId,
                localPlayer.PresentedPosition);
            HandlePickupInput(paused);
            lastCompleteFrame = new CoopEconomyPresentationFrame
            {
                Progression = progression,
                Inventory = inventory,
                Upgrades = upgrades,
                NearbyDrops = selection.Nearby,
                SelectedDropIndex = selection.SelectedIndex,
                RunGeneration = world.RunGeneration,
                InventoryVisible = inventoryVisible,
                Suspended = paused,
                Pending = requests.IsPending,
                RetryAfterRejection = retry,
                Status = status
            };
            presentation.Present(lastCompleteFrame);
        }

        private void ReadSnapshotLists()
        {
            inventory.Clear();
            upgrades.Clear();
            drops.Clear();
            for (int index = 0; index < authority.ReplicatedInventorySlotCount; index++)
            {
                NetcodeInventorySlotState slot = authority.GetReplicatedInventorySlot(index);
                if (slot.PlayerId == localPlayer.PlayerId) inventory.Add(slot);
            }
            for (int index = 0; index < authority.ReplicatedUpgradeCount; index++)
            {
                NetcodeUpgradeStackState upgrade = authority.GetReplicatedUpgrade(index);
                if (upgrade.PlayerId == localPlayer.PlayerId) upgrades.Add(upgrade);
            }
            for (int index = 0; index < authority.ReplicatedWorldDropCount; index++)
                drops.Add(authority.GetReplicatedWorldDrop(index));
        }

        private void HandlePickupInput(bool paused)
        {
            if (selection.HasSelection)
                pickupScrollCaptureUntil = Time.unscaledTime + PickupScrollReleaseDelay;
            bool ownsScroll = selection.HasSelection ||
                Time.unscaledTime < pickupScrollCaptureUntil;
            CoopEconomyPresentationRegistry.PickupScrollOwned = ownsScroll;
            if (ownsScroll)
            {
                // This is the same normalized event queue used by weapon cycling,
                // consumed once. The bridge must not consume it again this frame.
                int direction = presentation.ConsumePickupScroll();
                if (direction != 0)
                {
                    pickupScrollCaptureUntil = Time.unscaledTime + PickupScrollReleaseDelay;
                    if (!paused && !modalVisible) selection.Cycle(direction);
                }
            }
            if (paused || modalVisible || requests.IsPending ||
                !selection.HasSelection || !presentation.PickupPressed) return;
            NetcodeWorldDropState selected = selection.Selected;
            SubmitIntent(new CoopEconomyIntent(AuthoritativeEconomyCommandKind.Pickup,
                dropId: selected.DropId, dropRevision: selected.Revision,
                itemId: selected.ItemId.ToString()));
        }

        private bool SubmitIntent(CoopEconomyIntent intent)
        {
            if (!isActiveAndEnabled || requests.IsPending ||
                CoopUiInputGate.PauseMenuVisible || authority == null ||
                localPlayer == null || !localPlayer.HasConsumedServerState ||
                !authority.IsReplicatedSnapshotComplete ||
                IsOutcome(authority.WorldState) ||
                !authority.TryGetProgression(localPlayer.PlayerId,
                    out NetcodeProgressionState baseline)) return false;
            try
            {
                NetcodeEconomyCommand command = localPlayer.SubmitEconomyAction(
                    intent.Kind, entityId: intent.DropId,
                    sourceSlot: intent.SourceSlot,
                    destinationSlot: intent.DestinationSlot,
                    quantity: intent.Quantity,
                    candidateIndex: intent.CandidateIndex,
                    expectedDropRevision: intent.DropRevision,
                    expectedItemId: intent.ItemId);
                if (!requests.Begin(command, baseline)) return false;
                requestStartedAt = Time.unscaledTime;
                status = "正在等待服务器确认……";
                return true;
            }
            catch (InvalidOperationException)
            {
                status = "战斗连接尚未就绪，请稍后重试";
                return false;
            }
        }

        private void CloseInventory()
        {
            inventoryVisible = false;
            bool choosing = authority != null && localPlayer != null &&
                authority.TryGetProgression(localPlayer.PlayerId,
                    out NetcodeProgressionState progression) &&
                progression.PendingUpgradeChoices > 0;
            SetModalVisible(choosing);
            // Closing a view is not cancellation of an already submitted intent.
        }

        private void HideUnavailablePresentation()
        {
            inventoryVisible = false;
            presentation?.Hide();
            lastCompleteFrame = null;
            selection.Clear();
            pickupScrollCaptureUntil = 0f;
            CoopEconomyPresentationRegistry.PickupScrollOwned = false;
            SetModalVisible(false, restoreGameplayCursor: false);
        }

        private void SuspendForIncompleteSnapshot()
        {
            // NGO may apply list elements and the committed world header in
            // different frames. Keep the last validated projection and the
            // player's open intent, but never interact with the partial data.
            bool choosing = lastCompleteFrame.Progression.PendingUpgradeChoices > 0;
            SetModalVisible(inventoryVisible || choosing);
            lastCompleteFrame.InventoryVisible = inventoryVisible;
            lastCompleteFrame.Suspended = true;
            lastCompleteFrame.Pending = requests.IsPending;
            lastCompleteFrame.Status = "正在同步战局，请稍候……";
            presentation.Present(lastCompleteFrame);
            // Keep consuming an already-owned pickup wheel gesture while it
            // is suspended; it must not leak into weapon cycling on recovery.
            HandlePickupInput(paused: true);
        }

        private void LateUpdate()
        {
            if (!modalVisible || CoopUiInputGate.PauseMenuVisible) return;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void OnDisable() => HideUnavailablePresentation();

        private void OnDestroy()
        {
            HideUnavailablePresentation();
            presentation?.Dispose();
            presentation = null;
            requests.Reset();
        }

        private void SetModalVisible(bool visible, bool restoreGameplayCursor = true)
        {
            bool changed = modalVisible != visible;
            modalVisible = visible;
            CoopUiInputGate.EconomyModalVisible = visible;
            if (visible) economyCursorOwned = true;
            // A partial replicated frame may temporarily hide the modal before
            // its acknowledgement arrives. Retain the cursor lease so the next
            // complete frame can restore gameplay even if modalVisible is false.
            if ((changed || economyCursorOwned) && !visible && restoreGameplayCursor &&
                !CoopUiInputGate.PauseMenuVisible && authority != null &&
                !IsOutcome(authority.WorldState))
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
                economyCursorOwned = false;
            }
        }

        private void ResolveBindings()
        {
            if (authority == null)
                authority = FindFirstObjectByType<NetworkCoopSessionAuthority>();
            if (localPlayer != null && localPlayer.Session == authority &&
                localPlayer.IsLocallyControlled) return;
            localPlayer = null;
            NetworkPlayerReplica[] players = FindObjectsByType<NetworkPlayerReplica>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            foreach (NetworkPlayerReplica player in players)
            {
                if (!player.IsLocallyControlled || player.Session != authority) continue;
                localPlayer = player;
                break;
            }
        }

        private static bool IsOutcome(NetcodeWorldState world) =>
            world.MissionPhase == AuthoritativeMissionPhase.Victory ||
            world.MissionPhase == AuthoritativeMissionPhase.Defeat;
    }
}
