using System;
using System.Collections.Generic;
using FPS.Networking.Domain;
using UnityEngine;

namespace FPS.Networking.Netcode
{
    public readonly struct CoopEconomyIntent
    {
        public CoopEconomyIntent(AuthoritativeEconomyCommandKind kind,
            int sourceSlot = -1, int destinationSlot = -1, int quantity = 0,
            int candidateIndex = -1, int dropId = 0, int dropRevision = 0,
            string itemId = "")
        {
            Kind = kind;
            SourceSlot = sourceSlot;
            DestinationSlot = destinationSlot;
            Quantity = quantity;
            CandidateIndex = candidateIndex;
            DropId = dropId;
            DropRevision = dropRevision;
            ItemId = itemId ?? string.Empty;
        }

        public AuthoritativeEconomyCommandKind Kind { get; }
        public int SourceSlot { get; }
        public int DestinationSlot { get; }
        public int Quantity { get; }
        public int CandidateIndex { get; }
        public int DropId { get; }
        public int DropRevision { get; }
        public string ItemId { get; }
    }

    /// <summary>Server snapshot data; presentation must never mutate gameplay.</summary>
    public sealed class CoopEconomyPresentationFrame
    {
        public NetcodeProgressionState Progression;
        public IReadOnlyList<NetcodeInventorySlotState> Inventory;
        public IReadOnlyList<NetcodeUpgradeStackState> Upgrades;
        public IReadOnlyList<NetcodeWorldDropState> NearbyDrops;
        public int SelectedDropIndex;
        public int RunGeneration;
        public bool InventoryVisible;
        public bool Suspended;
        public bool Pending;
        public bool RetryAfterRejection;
        public string Status = string.Empty;
    }

    public interface ICoopEconomyPresentation
    {
        bool IsReady { get; }
        void Present(CoopEconomyPresentationFrame frame);
        int ConsumePickupScroll();
        bool PickupPressed { get; }
        void Hide();
        void Dispose();
    }

    /// <summary>
    /// Composition supplies the game's UI and visual assets. Networking stays
    /// independent of single-player controllers and of their local settlement.
    /// </summary>
    public static class CoopEconomyPresentationRegistry
    {
        private static Func<GameObject, Func<CoopEconomyIntent, bool>, Action,
            ICoopEconomyPresentation> presentationFactory;
        private static Func<Transform, string, GameObject> dropFactory;
        public static bool PickupScrollOwned { get; set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            presentationFactory = null;
            dropFactory = null;
            PickupScrollOwned = false;
        }

        public static void Register(
            Func<GameObject, Func<CoopEconomyIntent, bool>, Action,
                ICoopEconomyPresentation> uiFactory,
            Func<Transform, string, GameObject> worldDropFactory)
        {
            presentationFactory = uiFactory ??
                throw new ArgumentNullException(nameof(uiFactory));
            dropFactory = worldDropFactory ??
                throw new ArgumentNullException(nameof(worldDropFactory));
        }

        public static ICoopEconomyPresentation Create(GameObject owner,
            Func<CoopEconomyIntent, bool> submit, Action close) =>
            presentationFactory?.Invoke(owner, submit, close);

        public static GameObject CreateDrop(Transform parent, string itemId) =>
            dropFactory?.Invoke(parent, itemId);
    }
}
