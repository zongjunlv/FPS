using System.Collections.Generic;
using FPS.Networking.Domain;
using FPS.Networking.Netcode;
using UnityEngine.AddressableAssets;
using UnityEngine;
using UnityEngine.ResourceManagement.AsyncOperations;
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

        public bool TryGetTargetStatus(
            int targetId,
            out float health,
            out float maximumHealth,
            out AuthoritativeEnemyBehavior behavior)
        {
            if (targetViews.TryGetValue(targetId, out TargetView targetView))
            {
                health = targetView.Health;
                maximumHealth = targetView.MaximumHealth;
                behavior = targetView.Behavior;
                return true;
            }

            health = 0f;
            maximumHealth = 0f;
            behavior = AuthoritativeEnemyBehavior.Pooled;
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
            if (!authority.IsReplicatedSnapshotComplete)
            {
                foreach (TargetView view in targetViews.Values)
                    if (view.View != null) view.View.SetActive(false);
                VisibleViewCount = 0;
                return;
            }

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
                targetView.Health = state.Health;
                targetView.MaximumHealth = state.MaximumHealth > 0f
                    ? state.MaximumHealth
                    : Mathf.Max(1f, state.Health);
                targetView.Behavior = state.Behavior;

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
                    (targetView.UsesDebugPrimitive
                        ? Mathf.Max(0.25f, state.Radius * 2f * lodScale)
                        : lodScale);
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
                ReleasePresentation(targetView);
                if (targetView.View != null) Destroy(targetView.View);
            }
            targetViews.Clear();
        }

        private void OnGUI()
        {
            if (!CanRender()) return;
            Camera camera = ResolveCamera();
            if (camera == null) return;
            GUIStyle statusStyle = new(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 11,
                normal = { textColor = Color.white }
            };
            foreach (TargetView targetView in targetViews.Values)
            {
                if (targetView.View == null ||
                    !targetView.View.activeInHierarchy) continue;
                Vector3 screen = camera.WorldToScreenPoint(
                    targetView.View.transform.position + Vector3.up * 2.15f);
                if (screen.z <= 0f) continue;
                float x = screen.x - 45f;
                float y = Screen.height - screen.y;
                Rect background = new(x, y, 90f, 7f);
                Color previous = GUI.color;
                GUI.color = new Color(0f, 0f, 0f, 0.8f);
                GUI.DrawTexture(background, Texture2D.whiteTexture);
                GUI.color = new Color(0.22f, 0.9f, 0.5f, 0.95f);
                GUI.DrawTexture(new Rect(
                    background.x + 1f,
                    background.y + 1f,
                    (background.width - 2f) * Mathf.Clamp01(
                        targetView.Health / Mathf.Max(
                            1f, targetView.MaximumHealth)),
                    background.height - 2f), Texture2D.whiteTexture);
                GUI.color = previous;
                GUI.Label(new Rect(x, y + 7f, 90f, 17f),
                    BehaviorLabel(targetView.Behavior), statusStyle);
            }
        }

        private TargetView ResolveTargetView(NetcodeTargetState state)
        {
            if (targetViews.TryGetValue(state.TargetId,
                    out TargetView existing) && existing.View != null)
                return existing;

            bool recreation = existing != null;
            if (existing != null) ReleasePresentation(existing);
            string address = state.PresentationAddress.ToString();
            GameObject view = new GameObject(
                $"Coop Enemy View {state.TargetId}");
            view.name = $"Coop Enemy View {state.TargetId}";
            view.transform.SetParent(transform, worldPositionStays: true);
            var created = new TargetView(view, null, address);
            targetViews[state.TargetId] = created;
            if (!string.IsNullOrWhiteSpace(address))
            {
                BeginLoadPresentation(created, address, state.TargetId);
            }
            else if (forcePresentationForTests)
            {
                GameObject debug = GameObject.CreatePrimitive(
                    PrimitiveForRole(state.Role));
                debug.name = "Test-only enemy presentation";
                debug.transform.SetParent(view.transform, false);
                Collider collider = debug.GetComponent<Collider>();
                if (collider != null) Destroy(collider);
                created.Renderer = debug.GetComponent<Renderer>();
                created.UsesDebugPrimitive = true;
            }
            CreatedViewCount++;
            if (recreation) RecreatedViewCount++;
            return created;
        }

        private static void ReleasePresentation(TargetView targetView)
        {
            if (targetView == null || !targetView.HasLoadHandle) return;
            if (targetView.LoadHandle.IsValid())
                Addressables.Release(targetView.LoadHandle);
            targetView.HasLoadHandle = false;
        }

        private void BeginLoadPresentation(
            TargetView targetView,
            string address,
            int targetId)
        {
            targetView.LoadHandle =
                Addressables.LoadAssetAsync<GameObject>(address);
            targetView.HasLoadHandle = true;
            targetView.LoadHandle.Completed += handle =>
            {
                if (this == null || targetView.View == null)
                {
                    if (handle.IsValid()) Addressables.Release(handle);
                    targetView.HasLoadHandle = false;
                    return;
                }
                if (handle.Status != AsyncOperationStatus.Succeeded ||
                    handle.Result == null)
                {
                    Debug.LogError(
                        $"联机敌人 {targetId} 无法加载正式模型：{address}",
                        this);
                    return;
                }

                GameObject model = Instantiate(
                    handle.Result,
                    targetView.View.transform,
                    false);
                model.name = $"{handle.Result.name} (Presentation Only)";
                StripGameplayComponents(model);
                targetView.Model = model;
                targetView.Renderer = model.GetComponentInChildren<Renderer>(
                    includeInactive: true);
            };
        }

        private static void StripGameplayComponents(GameObject root)
        {
            Collider[] colliders = root.GetComponentsInChildren<Collider>(true);
            for (int index = 0; index < colliders.Length; index++)
                colliders[index].enabled = false;
            Rigidbody[] rigidbodies =
                root.GetComponentsInChildren<Rigidbody>(true);
            for (int index = 0; index < rigidbodies.Length; index++)
            {
                rigidbodies[index].isKinematic = true;
                rigidbodies[index].detectCollisions = false;
            }
            AudioSource[] audioSources =
                root.GetComponentsInChildren<AudioSource>(true);
            for (int index = 0; index < audioSources.Length; index++)
                audioSources[index].enabled = false;
            MonoBehaviour[] gameplay =
                root.GetComponentsInChildren<MonoBehaviour>(true);
            for (int index = 0; index < gameplay.Length; index++)
                gameplay[index].enabled = false;
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

        private static string BehaviorLabel(
            AuthoritativeEnemyBehavior behavior) => behavior switch
        {
            AuthoritativeEnemyBehavior.Patrol => "巡逻",
            AuthoritativeEnemyBehavior.Pursue => "追击",
            AuthoritativeEnemyBehavior.Attack => "攻击",
            AuthoritativeEnemyBehavior.Dead => "已消灭",
            _ => string.Empty
        };

        private sealed class TargetView
        {
            public TargetView(
                GameObject view,
                Renderer renderer,
                string address)
            {
                View = view;
                Renderer = renderer;
                Address = address ?? string.Empty;
            }

            public GameObject View;
            public GameObject Model;
            public Renderer Renderer;
            public string Address;
            public AsyncOperationHandle<GameObject> LoadHandle;
            public bool HasLoadHandle;
            public bool UsesDebugPrimitive;
            public Vector3 TargetPosition;
            public Quaternion TargetRotation;
            public int SpawnGeneration = -1;
            public int NextUpdateFrame;
            public bool Initialized;
            public float Health;
            public float MaximumHealth;
            public AuthoritativeEnemyBehavior Behavior;
        }
    }
}
