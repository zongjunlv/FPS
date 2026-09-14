using System.Collections.Generic;
using FPS.Networking.Netcode;
using UnityEngine;

namespace FPS.Networking.Session
{
    [DisallowMultipleComponent]
    public sealed class CoopNetworkWorldPresenter : MonoBehaviour
    {
        private readonly Dictionary<int, GameObject> targetViews = new();
        private NetworkCoopSessionAuthority authority;

        private void Update()
        {
            if (authority == null)
            {
                authority = FindFirstObjectByType<
                    NetworkCoopSessionAuthority>();
            }
            if (authority == null) return;

            for (int index = 0;
                 index < authority.ReplicatedTargetCount;
                 index++)
            {
                NetcodeTargetState state =
                    authority.GetReplicatedTarget(index);
                GameObject view = ResolveTargetView(state.TargetId);
                view.transform.position = state.Position;
                view.transform.localScale = Vector3.one *
                    Mathf.Max(0.25f, state.Radius * 2f);
                view.SetActive(state.IsAlive);
            }
        }

        private void OnDestroy()
        {
            foreach (GameObject view in targetViews.Values)
            {
                if (view != null) Destroy(view);
            }
            targetViews.Clear();
        }

        private GameObject ResolveTargetView(int targetId)
        {
            if (targetViews.TryGetValue(targetId, out GameObject existing) &&
                existing != null)
            {
                return existing;
            }

            GameObject view = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            view.name = $"Coop Authority Target {targetId}";
            Collider collider = view.GetComponent<Collider>();
            if (collider != null) Destroy(collider);
            Renderer renderer = view.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.material.color = new Color(0.95f, 0.2f, 0.16f);
            }
            targetViews[targetId] = view;
            return view;
        }
    }
}
