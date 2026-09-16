using System.Collections.Generic;
using UnityEngine;

namespace FPS.Networking.Netcode
{
    /// <summary>Client-only pooled views for authoritative world drops.</summary>
    [DisallowMultipleComponent]
    public sealed class CoopNetworkDropPresenter : MonoBehaviour
    {
        private readonly Dictionary<int, GameObject> views = new();
        private NetworkCoopSessionAuthority authority;

        public int ViewCount => views.Count;

        private void Update()
        {
            if (authority == null)
                authority = FindFirstObjectByType<NetworkCoopSessionAuthority>();
            if (authority == null) return;
            if (!authority.IsReplicatedSnapshotComplete)
            {
                foreach (GameObject view in views.Values)
                    if (view != null) view.SetActive(false);
                return;
            }

            var active = new HashSet<int>();
            for (int index = 0; index < authority.ReplicatedWorldDropCount;
                 index++)
            {
                NetcodeWorldDropState state =
                    authority.GetReplicatedWorldDrop(index);
                active.Add(state.DropId);
                if (!views.TryGetValue(state.DropId, out GameObject view) ||
                    view == null)
                {
                    view = CreateView(state.DropId);
                    views[state.DropId] = view;
                }
                view.SetActive(state.Available);
                if (!state.Available) continue;
                view.transform.position = state.Position + Vector3.up * 0.3f;
                view.transform.Rotate(Vector3.up, 45f * Time.deltaTime,
                    Space.World);
                ApplyColor(view, state.ItemId.ToString());
            }

            foreach (KeyValuePair<int, GameObject> pair in views)
                if (!active.Contains(pair.Key) && pair.Value != null)
                    pair.Value.SetActive(false);
        }

        private GameObject CreateView(int dropId)
        {
            GameObject view = GameObject.CreatePrimitive(PrimitiveType.Cube);
            view.name = $"Network Drop {dropId}";
            view.transform.SetParent(transform, false);
            view.transform.localScale = new Vector3(0.28f, 0.28f, 0.28f);
            Collider collider = view.GetComponent<Collider>();
            if (collider != null) Destroy(collider);
            return view;
        }

        private static void ApplyColor(GameObject view, string itemId)
        {
            Renderer renderer = view.GetComponent<Renderer>();
            if (renderer == null) return;
            Color color = itemId switch
            {
                "medical_kit" or "medkit" => new Color(0.95f, 0.2f, 0.2f),
                "armor_pack" or "armor_plate" => new Color(0.15f, 0.65f, 1f),
                "rifle_ammo" => new Color(1f, 0.72f, 0.15f),
                "handgun_ammo" => new Color(0.75f, 0.75f, 0.75f),
                _ => new Color(0.2f, 1f, 0.7f)
            };
            renderer.material.color = color;
        }

        private void OnDestroy()
        {
            foreach (GameObject view in views.Values)
                if (view != null) Destroy(view);
            views.Clear();
        }
    }
}
