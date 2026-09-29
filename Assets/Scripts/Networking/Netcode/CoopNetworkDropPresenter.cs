using System.Collections.Generic;
using UnityEngine;

namespace FPS.Networking.Netcode
{
    /// <summary>
    /// Pure 3D package views of authoritative drops. No local pickup trigger,
    /// colliders, inventory settlement or camera-facing billboard is installed.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CoopNetworkDropPresenter : MonoBehaviour
    {
        private readonly Dictionary<int, GameObject> views = new();
        private readonly Dictionary<int, string> itemIds = new();
        private readonly HashSet<int> active = new();
        private readonly List<int> removed = new();
        private NetworkCoopSessionAuthority authority;
        private NetworkCoopSessionAuthority presentedAuthority;
        private int presentedRunGeneration = -1;

        public int ViewCount => views.Count;

        private void Update()
        {
            if (authority == null)
                authority = FindFirstObjectByType<NetworkCoopSessionAuthority>();
            if (authority == null || !authority.IsReplicatedSnapshotComplete)
            {
                HideViews();
                return;
            }
            if (presentedAuthority != authority ||
                presentedRunGeneration != authority.WorldState.RunGeneration)
            {
                ClearViews();
                presentedAuthority = authority;
                presentedRunGeneration = authority.WorldState.RunGeneration;
            }

            active.Clear();
            for (int index = 0; index < authority.ReplicatedWorldDropCount; index++)
            {
                NetcodeWorldDropState state = authority.GetReplicatedWorldDrop(index);
                if (!state.Available || state.Quantity <= 0) continue;
                active.Add(state.DropId);
                string itemId = state.ItemId.ToString();
                if (views.TryGetValue(state.DropId, out GameObject previous) &&
                    previous != null && itemIds[state.DropId] != itemId)
                    ReleaseView(state.DropId);
                if (!views.TryGetValue(state.DropId, out GameObject view) || view == null)
                {
                    view = CreateView(state.DropId, itemId);
                    if (view == null) continue; // Composition has not registered yet.
                    views[state.DropId] = view;
                    itemIds[state.DropId] = itemId;
                }
                view.SetActive(true);
                // The replicated coordinate is the ground anchor, not a UI icon
                // center. Match the single-player package's half-height.
                view.transform.position = state.Position + Vector3.up * 0.22f;
                view.transform.rotation = Quaternion.identity;
            }

            removed.Clear();
            foreach (int id in views.Keys)
                if (!active.Contains(id)) removed.Add(id);
            foreach (int id in removed) ReleaseView(id);
        }

        private GameObject CreateView(int dropId, string itemId)
        {
            var view = new GameObject($"Network Drop {dropId}");
            view.transform.SetParent(transform, false);
            GameObject package = CoopEconomyPresentationRegistry.CreateDrop(
                view.transform, itemId);
            if (package != null) return view;
            Destroy(view);
            return null;
        }

        private void ReleaseView(int id)
        {
            if (views.TryGetValue(id, out GameObject view) && view != null)
            {
                view.SetActive(false);
                Destroy(view);
            }
            views.Remove(id);
            itemIds.Remove(id);
        }

        private void HideViews()
        {
            foreach (GameObject view in views.Values)
                if (view != null) view.SetActive(false);
        }

        private void ClearViews()
        {
            foreach (GameObject view in views.Values)
                if (view != null)
                {
                    view.SetActive(false);
                    Destroy(view);
                }
            views.Clear();
            itemIds.Clear();
            active.Clear();
            removed.Clear();
        }

        private void OnDisable() => HideViews();
        private void OnDestroy() => ClearViews();
    }
}
