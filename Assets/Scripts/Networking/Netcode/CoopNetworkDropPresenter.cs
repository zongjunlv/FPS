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
        private Camera presentationCamera;
        private Sprite healthIcon;
        private Sprite armorIcon;
        private Sprite ammoIcon;

        public int ViewCount => views.Count;

        private void Awake()
        {
            healthIcon = Resources.Load<Sprite>("UI/Icons/health");
            armorIcon = Resources.Load<Sprite>("UI/Icons/armor");
            ammoIcon = Resources.Load<Sprite>("UI/Icons/ammo");
        }

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
                float bob = Mathf.Sin(Time.unscaledTime * 2.5f +
                                      state.DropId) * 0.07f;
                view.transform.position = state.Position +
                    Vector3.up * (0.48f + bob);
                presentationCamera ??= Camera.main;
                if (presentationCamera != null)
                    view.transform.rotation =
                        presentationCamera.transform.rotation;
                ApplyIcon(view, state.ItemId.ToString());
            }

            foreach (KeyValuePair<int, GameObject> pair in views)
                if (!active.Contains(pair.Key) && pair.Value != null)
                    pair.Value.SetActive(false);
        }

        private GameObject CreateView(int dropId)
        {
            var view = new GameObject($"Network Drop {dropId}");
            view.name = $"Network Drop {dropId}";
            view.transform.SetParent(transform, false);
            view.transform.localScale = Vector3.one * 0.52f;
            SpriteRenderer renderer = view.AddComponent<SpriteRenderer>();
            renderer.sortingOrder = 32;
            return view;
        }

        private void ApplyIcon(GameObject view, string itemId)
        {
            SpriteRenderer renderer = view.GetComponent<SpriteRenderer>();
            if (renderer == null) return;
            renderer.sprite = itemId switch
            {
                "medical_kit" or "medkit" => healthIcon,
                "armor_pack" or "armor_plate" => armorIcon,
                "rifle_ammo" or "handgun_ammo" => ammoIcon,
                _ => ammoIcon
            };
            renderer.color = itemId switch
            {
                "medical_kit" or "medkit" => new Color(0.95f, 0.2f, 0.2f),
                "armor_pack" or "armor_plate" => new Color(0.15f, 0.65f, 1f),
                "rifle_ammo" => new Color(1f, 0.72f, 0.15f),
                "handgun_ammo" => new Color(0.75f, 0.75f, 0.75f),
                _ => new Color(0.2f, 1f, 0.7f)
            };
        }

        private void OnDestroy()
        {
            foreach (GameObject view in views.Values)
                if (view != null) Destroy(view);
            views.Clear();
        }
    }
}
