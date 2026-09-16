using System.Collections.Generic;
using FPS.Networking.Domain;
using FPS.Networking.Netcode;
using UnityEngine;
using UnityEngine.Rendering;

namespace FPS.Networking.Session
{
    /// <summary>
    /// Client-only presentation pool for server-authoritative enemies. These
    /// views contain no gameplay scripts or colliders, so deleting one locally
    /// cannot mutate the authoritative match.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CoopNetworkWorldPresenter : MonoBehaviour
    {
        private const float NearLodDistance = 25f;
        private const float MidLodDistance = 55f;
        private const float MaximumVisibleDistance = 120f;
        private const float InterpolationSharpness = 14f;

        private readonly Dictionary<int, TargetView> targetViews = new();
        private readonly HashSet<int> replicatedIds = new();
        private readonly Plane[] frustumPlanes = new Plane[6];
        private MaterialPropertyBlock propertyBlock;
        private NetworkCoopSessionAuthority authority;
        private Camera presentationCamera;
        private bool forcePresentationForTests;

        public int ViewCount => targetViews.Count;
        public int CreatedViewCount { get; private set; }
        public int RecreatedViewCount { get; private set; }
        public int VisibleViewCount { get; private set; }

        private void Awake()
        {
            propertyBlock = new MaterialPropertyBlock();
        }

        public bool TryGetTargetView(int targetId, out GameObject view)
        {
            if (targetViews.TryGetValue(targetId, out TargetView targetView) &&
                targetView.View != null)
            {
                view = targetView.View;
                return true;
            }

            view = null;
            return false;
        }

        public void BindForTests(NetworkCoopSessionAuthority value)
        {
            authority = value;
            forcePresentationForTests = true;
        }

        public void PresentForTests(
            IReadOnlyList<NetcodeTargetState> states,
            float deltaTime,
            Camera camera = null)
        {
            forcePresentationForTests = true;
            Present(states, Mathf.Max(0f, deltaTime), camera);
        }

        private void Update()
        {
            if (!CanRender()) return;
            if (authority == null)
            {
                authority = FindFirstObjectByType<
                    NetworkCoopSessionAuthority>();
            }
            if (authority == null) return;

            var states = new NetcodeTargetState[
                authority.ReplicatedTargetCount];
            for (int index = 0; index < states.Length; index++)
                states[index] = authority.GetReplicatedTarget(index);
            Present(states, Time.unscaledDeltaTime, ResolveCamera());
        }

        private void Present(
            IReadOnlyList<NetcodeTargetState> states,
            float deltaTime,
            Camera camera)
        {
            if (states == null) return;
            replicatedIds.Clear();
            VisibleViewCount = 0;
            bool hasFrustum = camera != null;
            if (hasFrustum)
                GeometryUtility.CalculateFrustumPlanes(camera, frustumPlanes);

            for (int index = 0; index < states.Count; index++)
            {
                NetcodeTargetState state = states[index];
                replicatedIds.Add(state.TargetId);
                TargetView targetView = ResolveTargetView(state);
                bool generationChanged = targetView.SpawnGeneration !=
                    state.SpawnGeneration;
                targetView.SpawnGeneration = state.SpawnGeneration;
                targetView.TargetPosition = state.Position;
                targetView.TargetRotation = Quaternion.Euler(
                    0f, state.YawDegrees, 0f);

                float distance = camera == null
                    ? 0f
                    : Vector3.Distance(camera.transform.position,
                        state.Position);
                int cadence = distance > MidLodDistance
                    ? 4
                    : distance > NearLodDistance ? 2 : 1;
                bool updateTransform = forcePresentationForTests ||
                    generationChanged || !targetView.Initialized ||
                    Time.frameCount >= targetView.NextUpdateFrame;
                if (updateTransform)
                {
                    targetView.NextUpdateFrame = Time.frameCount + cadence;
                    if (generationChanged || !targetView.Initialized)
                    {
                        targetView.View.transform.SetPositionAndRotation(
                            state.Position, targetView.TargetRotation);
                        targetView.Initialized = true;
                    }
                    else
                    {
                        float blend = 1f - Mathf.Exp(
                            -InterpolationSharpness * deltaTime);
                        Transform targetTransform = targetView.View.transform;
                        targetTransform.position = Vector3.Lerp(
                            targetTransform.position,
                            targetView.TargetPosition,
                            blend);
                        targetTransform.rotation = Quaternion.Slerp(
                            targetTransform.rotation,
                            targetView.TargetRotation,
                            blend);
                    }
                }

                float lodScale = distance > MidLodDistance
                    ? 0.55f
                    : distance > NearLodDistance ? 0.75f : 1f;
                targetView.View.transform.localScale = Vector3.one *
                    Mathf.Max(0.25f, state.Radius * 2f * lodScale);
                bool visible = state.IsAlive &&
                    distance <= MaximumVisibleDistance &&
                    (!hasFrustum || targetView.Renderer == null ||
                     GeometryUtility.TestPlanesAABB(
                         frustumPlanes, targetView.Renderer.bounds));
                targetView.View.SetActive(visible);
                if (visible) VisibleViewCount++;
            }

            foreach (KeyValuePair<int, TargetView> pair in targetViews)
            {
                if (!replicatedIds.Contains(pair.Key) &&
                    pair.Value.View != null)
                    pair.Value.View.SetActive(false);
            }
        }

        private void OnDestroy()
        {
            foreach (TargetView targetView in targetViews.Values)
            {
                if (targetView.View != null) Destroy(targetView.View);
            }
            targetViews.Clear();
        }

        private TargetView ResolveTargetView(NetcodeTargetState state)
        {
            if (targetViews.TryGetValue(state.TargetId,
                    out TargetView existing) && existing.View != null)
                return existing;

            bool recreation = existing != null;
            GameObject view = GameObject.CreatePrimitive(
                PrimitiveForRole(state.Role));
            view.name = $"Coop Enemy View {state.TargetId}";
            view.transform.SetParent(transform, worldPositionStays: true);
            Collider collider = view.GetComponent<Collider>();
            if (collider != null)
            {
                collider.enabled = false;
                Destroy(collider);
            }
            Renderer renderer = view.GetComponent<Renderer>();
            if (renderer != null)
            {
                propertyBlock ??= new MaterialPropertyBlock();
                Color color = ColorForRole(state.Role);
                renderer.GetPropertyBlock(propertyBlock);
                propertyBlock.SetColor("_Color", color);
                propertyBlock.SetColor("_BaseColor", color);
                renderer.SetPropertyBlock(propertyBlock);
            }

            var created = new TargetView(view, renderer);
            targetViews[state.TargetId] = created;
            CreatedViewCount++;
            if (recreation) RecreatedViewCount++;
            return created;
        }

        private Camera ResolveCamera()
        {
            if (presentationCamera == null) presentationCamera = Camera.main;
            return presentationCamera;
        }

        private bool CanRender() => forcePresentationForTests ||
            SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null;

        private static PrimitiveType PrimitiveForRole(
            AuthoritativeEnemyRole role) => role switch
        {
            AuthoritativeEnemyRole.Raider => PrimitiveType.Capsule,
            AuthoritativeEnemyRole.Support => PrimitiveType.Cylinder,
            AuthoritativeEnemyRole.Suppressor => PrimitiveType.Cube,
            AuthoritativeEnemyRole.Elite => PrimitiveType.Capsule,
            _ => PrimitiveType.Sphere
        };

        private static Color ColorForRole(
            AuthoritativeEnemyRole role) => role switch
        {
            AuthoritativeEnemyRole.Raider => new Color(1f, 0.42f, 0.08f),
            AuthoritativeEnemyRole.Support => new Color(0.18f, 0.82f, 0.4f),
            AuthoritativeEnemyRole.Suppressor =>
                new Color(0.55f, 0.25f, 0.95f),
            AuthoritativeEnemyRole.Elite => new Color(1f, 0.08f, 0.18f),
            _ => new Color(0.95f, 0.2f, 0.16f)
        };

        private sealed class TargetView
        {
            public TargetView(GameObject view, Renderer renderer)
            {
                View = view;
                Renderer = renderer;
            }

            public GameObject View;
            public Renderer Renderer;
            public Vector3 TargetPosition;
            public Quaternion TargetRotation;
            public int SpawnGeneration = -1;
            public int NextUpdateFrame;
            public bool Initialized;
        }
    }
}
