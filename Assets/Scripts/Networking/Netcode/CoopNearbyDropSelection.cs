using System.Collections.Generic;
using UnityEngine;

namespace FPS.Networking.Netcode
{
    /// <summary>Selection follows stable drop IDs, not the size of the first list.</summary>
    public sealed class CoopNearbyDropSelection
    {
        private readonly List<NetcodeWorldDropState> nearby = new();
        private int selectedDropId;
        public IReadOnlyList<NetcodeWorldDropState> Nearby => nearby;
        public int SelectedIndex { get; private set; } = -1;
        public bool HasSelection => SelectedIndex >= 0 && SelectedIndex < nearby.Count;
        public NetcodeWorldDropState Selected => HasSelection ? nearby[SelectedIndex] : default;

        public void Refresh(IReadOnlyList<NetcodeWorldDropState> drops,
            int playerId, Vector3 playerPosition)
        {
            int previousIndex = SelectedIndex;
            nearby.Clear();
            if (drops != null)
            {
                foreach (NetcodeWorldDropState drop in drops)
                {
                    if (!drop.Available || drop.Quantity <= 0 ||
                        drop.OwnerPlayerId != 0 && drop.OwnerPlayerId != playerId ||
                        (drop.Position - playerPosition).sqrMagnitude > 3.25f * 3.25f)
                        continue;
                    nearby.Add(drop);
                }
            }
            nearby.Sort((left, right) => left.DropId.CompareTo(right.DropId));
            SelectedIndex = nearby.FindIndex(value => value.DropId == selectedDropId);
            if (SelectedIndex < 0 && nearby.Count > 0)
                SelectedIndex = Mathf.Clamp(previousIndex, 0, nearby.Count - 1);
            selectedDropId = HasSelection ? nearby[SelectedIndex].DropId : 0;
        }

        public void Cycle(int direction)
        {
            if (!HasSelection || direction == 0) return;
            int step = direction > 0 ? 1 : -1;
            SelectedIndex = (SelectedIndex + step + nearby.Count) % nearby.Count;
            selectedDropId = nearby[SelectedIndex].DropId;
        }

        public void Clear()
        {
            nearby.Clear();
            SelectedIndex = -1;
            selectedDropId = 0;
        }
    }
}
