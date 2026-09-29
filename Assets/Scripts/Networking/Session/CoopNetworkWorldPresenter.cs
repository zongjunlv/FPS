using System;
using System.Collections.Generic;
using System.Globalization;
using FPS.Networking.Domain;
using FPS.Networking.Netcode;
using Unity.Netcode;
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
        private const float MaximumVisibleDistance = 120f;
        private const float InterpolationSharpness = 14f;
        private const double PresentationHiatusSeconds = 0.75d;
        private const long PresentationHiatusTicks = 45;
        private const double TraceSampleIntervalSeconds = 0.2d;
        private static readonly bool TraceEnabled = string.Equals(
            Environment.GetEnvironmentVariable("FPS_NETTRACE"), "1",
            StringComparison.Ordinal);

        private readonly Dictionary<int, TargetView> targetViews = new();
        private readonly HashSet<int> replicatedIds = new();
        private readonly Plane[] frustumPlanes = new Plane[6];
        private MaterialPropertyBlock propertyBlock;
        private NetworkCoopSessionAuthority authority;
        private NetworkCoopSessionAuthority presentedAuthority;
        private OptionalNetworkBootstrap networkBootstrap;
        private Camera presentationCamera;
        private OptionalNetworkBootstrap traceBootstrap;
        private bool forcePresentationForTests;
        private bool snapOnNextCompleteSnapshot;
        private long lastPresentedServerTick = -1;
        private double lastServerTickAt;
        private long lastTracedWorldTick = -1;
        private double lastWorldTickAt;
        private double lastWorldTraceAt;
        private double lastIncompleteTraceAt;
        private int presentedClockRevision = -1;
        private double renderTick = -1d;
        private bool hasCompleteFrame;
        private int completeFrameRun;
        private long completeFrameTick = -1;
        private double completeFrameReceivedAt;

        public int ViewCount => targetViews.Count;
        public int CreatedViewCount { get; private set; }
        public int RecreatedViewCount { get; private set; }
        public int VisibleViewCount { get; private set; }
        public double LastResolvedShotViewTick { get; private set; } = -1d;

        public void ResetShotViewSample() => LastResolvedShotViewTick = -1d;

        private void Awake()
        {
            propertyBlock = new MaterialPropertyBlock();
            networkBootstrap = GetComponent<OptionalNetworkBootstrap>();
        }

        private void OnDisable()
        {
            hasCompleteFrame = false;
            SuspendPresentation();
            HideViews();
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

        public Vector3? RaycastVisibleTarget(Ray ray, float maximumDistance)
        {
            LastResolvedShotViewTick = -1d;
            double nearest = maximumDistance;
            bool found = false;
            foreach (TargetView view in targetViews.Values)
            {
                if (view.View == null || !view.View.activeInHierarchy || view.Definition == null) continue;
                var root = NetcodeConversions.ToDomain(view.View.transform.position);
                double yaw = view.View.transform.eulerAngles.y;
                foreach (var box in new[]
                {
                    (view.Definition.BodyCenter, view.Definition.BodyHalfExtents),
                    (view.Definition.HeadCenter, view.Definition.HeadHalfExtents)
                })
                {
                    if (AuthoritativeHitGeometry.RayBox(NetcodeConversions.ToDomain(ray.origin),
                        NetcodeConversions.ToDomain(ray.direction), root, yaw,
                        NetcodeConversions.ToDomain(box.Item1), NetcodeConversions.ToDomain(box.Item2),
                        nearest, out double distance))
                    {
                        nearest = distance;
                        LastResolvedShotViewTick = view.DisplayedTick;
                        found = true;
                    }
                }
            }
            return found ? ray.GetPoint((float)nearest) : null;
        }

        public void BindForTests(NetworkCoopSessionAuthority value)
        {
            authority = value;
            forcePresentationForTests = true;
        }

        public void PresentForTests(
            IReadOnlyList<NetcodeTargetState> states,
            float deltaTime,
            Camera camera = null,
            long serverTick = -1,
            double presentationTick = -1d)
        {
            forcePresentationForTests = true;
            if (serverTick >= 0)
                ObserveServerTick(serverTick);
            renderTick = presentationTick;
            Present(states, Mathf.Max(0f, deltaTime), camera);
        }

        private void Update()
        {
            // Headless diagnostics can stop rendering at the same time as a
            // disconnect. Remember that interruption before the early return.
            if (!CanRender())
            {
                hasCompleteFrame = false;
                SuspendPresentation();
                HideViews();
                return;
            }
            if (networkBootstrap == null)
                networkBootstrap = GetComponent<OptionalNetworkBootstrap>();
            if (networkBootstrap?.NetworkManager != null &&
                !networkBootstrap.NetworkManager.IsServer &&
                !networkBootstrap.NetworkManager.IsConnectedClient)
            {
                hasCompleteFrame = false;
                SuspendPresentation();
                HideViews();
                return;
            }
            if (authority == null)
            {
                authority = FindFirstObjectByType<
                    NetworkCoopSessionAuthority>();
            }
            if (authority == null)
            {
                hasCompleteFrame = false;
                SuspendPresentation();
                HideViews();
                return;
            }
            if (!ReferenceEquals(presentedAuthority, authority))
            {
                presentedAuthority = authority;
                hasCompleteFrame = false;
                lastPresentedServerTick = -1;
                SuspendPresentation();
                HideViews();
            }
            if (TraceEnabled) TraceWorldState();
            if (!authority.IsReplicatedSnapshotComplete)
            {
                if (TraceEnabled) TraceIncompleteSnapshot();
                // NGO may apply the new world commit before its list fields.
                // Hold the last *displayed complete* frame briefly, rather than
                // blinking every model off or consuming a mixture of two ticks.
                // A new run, disconnect or prolonged gap must never retain it.
                if (hasCompleteFrame && completeFrameRun == authority.WorldState.RunGeneration &&
                    Time.realtimeSinceStartupAsDouble - completeFrameReceivedAt <= PresentationHiatusSeconds)
                    return;
                hasCompleteFrame = false;
                SuspendPresentation();
                HideViews();
                return;
            }

            if (!hasCompleteFrame || completeFrameRun != authority.WorldState.RunGeneration ||
                completeFrameTick != authority.WorldState.ServerTick)
                completeFrameReceivedAt = Time.realtimeSinceStartupAsDouble;
            hasCompleteFrame = true;
            completeFrameRun = authority.WorldState.RunGeneration;
            completeFrameTick = authority.WorldState.ServerTick;

            ObserveServerTick(authority.WorldState.ServerTick);
            if (presentedClockRevision != authority.ClockRevision)
            {
                presentedClockRevision = authority.ClockRevision;
                SuspendPresentation();
            }
            renderTick = authority.PresentationTick;
            var states = new NetcodeTargetState[
                authority.ReplicatedTargetCount];
            for (int index = 0; index < states.Length; index++)
                states[index] = authority.GetReplicatedTarget(index);
            Present(states, Time.unscaledDeltaTime, ResolveCamera());
        }

        private void ObserveServerTick(long tick)
        {
            if (tick == lastPresentedServerTick) return;
            double now = Time.realtimeSinceStartupAsDouble;
            if (lastPresentedServerTick >= 0 &&
                (tick < lastPresentedServerTick ||
                 tick - lastPresentedServerTick > PresentationHiatusTicks ||
                 now - lastServerTickAt > PresentationHiatusSeconds))
                SuspendPresentation();
            lastPresentedServerTick = tick;
            lastServerTickAt = now;
        }

        private void SuspendPresentation()
        {
            snapOnNextCompleteSnapshot = true;
        }

        private void HideViews()
        {
            foreach (TargetView view in targetViews.Values)
                if (view.View != null) view.View.SetActive(false);
            VisibleViewCount = 0;
        }

        private void TraceIncompleteSnapshot()
        {
            double now = Time.realtimeSinceStartupAsDouble;
            if (now - lastIncompleteTraceAt < TraceSampleIntervalSeconds)
                return;
            lastIncompleteTraceAt = now;
            Debug.Log(string.Format(CultureInfo.InvariantCulture,
                "[NETTRACE-v1] role=client kind=incomplete " +
                "utcMs={0} monoMs={1:F1} tick={2} actualTargets={3} expectedTargets={4}",
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                now * 1000d, authority.WorldState.ServerTick,
                authority.ReplicatedTargetCount,
                authority.WorldState.SnapshotTargetCount));
        }

        private void Present(
            IReadOnlyList<NetcodeTargetState> states,
            float deltaTime,
            Camera camera)
        {
            if (states == null) return;
            replicatedIds.Clear();
            VisibleViewCount = 0;
            bool snapAfterInterruption = snapOnNextCompleteSnapshot;
            bool hasFrustum = camera != null;
            if (hasFrustum)
                GeometryUtility.CalculateFrustumPlanes(camera, frustumPlanes);

            for (int index = 0; index < states.Count; index++)
            {
                NetcodeTargetState state = states[index];
                replicatedIds.Add(state.TargetId);
                TargetView targetView = ResolveTargetView(state);
                bool generationChanged = targetView.SpawnGeneration !=
                    state.SpawnGeneration || targetView.RunGeneration != state.RunGeneration;
                targetView.SpawnGeneration = state.SpawnGeneration;
                targetView.RunGeneration = state.RunGeneration;
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
                bool reset = snapAfterInterruption || generationChanged || !targetView.Initialized;
                Vector3 before = targetView.View.transform.position;
                if (reset)
                {
                    targetView.Snapshots = new RemoteSnapshotInterpolator(32, 0);
                    targetView.LastSnapshotTick = -1;
                    targetView.Animation?.ResetPose();
                }
                long snapshotTick = state.SnapshotTick > 0 ? state.SnapshotTick : lastPresentedServerTick;
                if (snapshotTick >= 0 && snapshotTick > targetView.LastSnapshotTick)
                {
                    targetView.Snapshots.Push(new RemotePlayerSnapshot(snapshotTick, state.TargetId,
                        NetcodeConversions.ToDomain(state.Position), state.YawDegrees, 0d));
                    targetView.LastSnapshotTick = snapshotTick;
                }
                if (renderTick >= 0 && targetView.Snapshots.Count > 0)
                {
                    RemoteInterpolationSample sample = targetView.Snapshots.Sample(renderTick);
                    targetView.View.transform.SetPositionAndRotation(NetcodeConversions.ToUnity(sample.Position),
                        Quaternion.Euler(0f, (float)sample.AimYawDegrees, 0f));
                    // A cursor can outrun the available buffer. Rewind to the
                    // pose actually displayed, not to a future clock estimate.
                    targetView.DisplayedTick = sample.FromTick +
                        (sample.ToTick - sample.FromTick) * sample.Ratio;
                }
                else
                {
                    float blend = reset ? 1f : 1f - Mathf.Exp(-InterpolationSharpness * deltaTime);
                    targetView.View.transform.SetPositionAndRotation(
                        Vector3.Lerp(before, state.Position, blend),
                        Quaternion.Slerp(targetView.View.transform.rotation, targetView.TargetRotation, blend));
                    targetView.DisplayedTick = snapshotTick;
                }
                targetView.Initialized = true;
                // LOD may reduce animation work/culling, never alter physical presentation size.
                targetView.View.transform.localScale = Vector3.one *
                    (targetView.UsesDebugPrimitive
                        ? Mathf.Max(0.25f, state.Radius * 2f)
                        : 1f);
                bool visible = state.IsAlive &&
                    (targetView.Model != null || targetView.UsesDebugPrimitive) &&
                    distance <= MaximumVisibleDistance &&
                    (!hasFrustum || IsInFrustum(targetView));
                targetView.View.SetActive(visible);
                Vector3 velocity = reset || deltaTime <= 0f ? Vector3.zero :
                    (targetView.View.transform.position - before) / deltaTime;
                targetView.Animation?.Present(state.Behavior, velocity, deltaTime, state.LastAttackTick);
                if (visible) VisibleViewCount++;
                if (TraceEnabled)
                    TraceEnemy(state, targetView, generationChanged);
            }

            foreach (KeyValuePair<int, TargetView> pair in targetViews)
            {
                if (!replicatedIds.Contains(pair.Key) &&
                    pair.Value.View != null)
                    pair.Value.View.SetActive(false);
            }
            if (states.Count > 0)
                snapOnNextCompleteSnapshot = false;
        }

        private void TraceWorldState()
        {
            double now = Time.realtimeSinceStartupAsDouble;
            long tick = authority.WorldState.ServerTick;
            if (tick != lastTracedWorldTick)
            {
                lastTracedWorldTick = tick;
                lastWorldTickAt = now;
            }
            if (now - lastWorldTraceAt < TraceSampleIntervalSeconds)
                return;
            lastWorldTraceAt = now;
            if (traceBootstrap == null)
                traceBootstrap = FindFirstObjectByType<OptionalNetworkBootstrap>();
            bool connected = traceBootstrap?.NetworkManager != null &&
                traceBootstrap.NetworkManager.IsConnectedClient;
            ulong rttMs = connected && traceBootstrap.Transport != null
                ? traceBootstrap.Transport.GetCurrentRtt(
                    NetworkManager.ServerClientId)
                : 0UL;
            Debug.Log(string.Format(CultureInfo.InvariantCulture,
                "[NETTRACE-v1] role=client kind=world " +
                "utcMs={0} monoMs={1:F1} tick={2} tickAgeMs={3:F1} " +
                "connected={4} rttMs={5}",
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                now * 1000d, tick, (now - lastWorldTickAt) * 1000d,
                connected ? 1 : 0, rttMs));
        }

        private void TraceEnemy(
            NetcodeTargetState state,
            TargetView view,
            bool generationChanged)
        {
            double now = Time.realtimeSinceStartupAsDouble;
            Vector3 rendered = view.View.transform.position;
            bool targetJump = !generationChanged && view.HasTraceSample &&
                Vector3.Distance(view.LastTraceTarget, state.Position) > 2f;
            bool viewJump = !generationChanged && view.HasTraceSample &&
                Vector3.Distance(view.LastTraceView, rendered) > 2f;
            if (!targetJump && !viewJump &&
                now - view.LastTraceAt < TraceSampleIntervalSeconds)
                return;

            bool modelReady = view.Model != null && view.Definition != null;
            Vector3 modelWorld = modelReady ? view.Model.transform.position : Vector3.one * float.NaN;
            Vector3 modelLocal = modelReady ? view.Model.transform.localPosition : Vector3.one * float.NaN;
            float rendererBottomY = float.NaN;
            if (view.Renderers != null && view.Renderers.Length > 0)
            {
                Bounds bounds = view.Renderers[0].bounds;
                for (int index = 1; index < view.Renderers.Length; index++)
                    bounds.Encapsulate(view.Renderers[index].bounds);
                rendererBottomY = bounds.min.y;
            }
            Debug.Log(string.Format(CultureInfo.InvariantCulture,
                "[NETTRACE-v1] role=client kind=enemy " +
                "utcMs={0} monoMs={1:F1} tick={2} id={3} gen={4} " +
                "targetX={5:F3} targetY={6:F3} targetZ={7:F3} " +
                "viewX={8:F3} viewY={9:F3} viewZ={10:F3} " +
                "modelY={11:F3} bottomY={12:F3} alive={13} " +
                "targetJump={14} viewJump={15} archetype={16} " +
                "modelReady={17} modelX={18:F3} modelZ={19:F3} " +
                "modelLocalX={20:F3} modelLocalY={21:F3} modelLocalZ={22:F3} " +
                "run={23} snapshotTick={24} renderTick={25:F2}",
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                now * 1000d,
                authority == null ? -1 : authority.WorldState.ServerTick,
                state.TargetId, state.SpawnGeneration,
                state.Position.x, state.Position.y, state.Position.z,
                rendered.x, rendered.y, rendered.z,
                modelWorld.y, rendererBottomY,
                state.IsAlive ? 1 : 0, targetJump ? 1 : 0,
                viewJump ? 1 : 0, state.ArchetypeId, modelReady ? 1 : 0,
                modelWorld.x, modelWorld.z, modelLocal.x, modelLocal.y, modelLocal.z,
                state.RunGeneration, state.SnapshotTick, renderTick));
            view.LastTraceAt = now;
            view.LastTraceTarget = state.Position;
            view.LastTraceView = rendered;
            view.HasTraceSample = true;
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
                    !targetView.View.activeInHierarchy || targetView.Model == null ||
                    targetView.Definition == null) continue;
                Vector3 screen = camera.WorldToScreenPoint(
                    targetView.Model.transform.TransformPoint(targetView.Definition.StatusAnchor));
                if (screen.z <= camera.nearClipPlane || screen.x < 0f || screen.x > Screen.width ||
                    screen.y < 0f || screen.y > Screen.height) continue;
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
            string address = state.PresentationAddress.ToString();
            if (targetViews.TryGetValue(state.TargetId,
                    out TargetView existing) && existing.View != null && existing.Address == address)
                return existing;

            bool recreation = existing != null;
            if (existing != null)
            {
                existing.ReleaseRequested = true;
                ReleasePresentation(existing);
                if (existing.View != null) Destroy(existing.View);
            }
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
            if (targetView.LoadHandle.IsValid() &&
                !targetView.LoadHandle.IsDone)
            {
                targetView.ReleaseRequested = true;
                return;
            }
            if (targetView.LoadHandle.IsValid())
                Addressables.Release(targetView.LoadHandle);
            targetView.HasLoadHandle = false;
        }

        private void BeginLoadPresentation(
            TargetView targetView,
            string address,
            int targetId)
        {
            targetView.LoadHandle = Addressables.LoadAssetAsync<GameObject>("coop/presentation/" + address);
            targetView.HasLoadHandle = true;
            targetView.LoadHandle.Completed += handle =>
            {
                if (!targetView.HasLoadHandle) return;
                if (targetView.ReleaseRequested || this == null ||
                    targetView.View == null || !targetViews.TryGetValue(targetId, out var current) ||
                    !ReferenceEquals(current, targetView))
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

                if (!IsPurePresentation(handle.Result))
                {
                    Debug.LogError($"联机表现资源含有游戏逻辑，已拒绝实例化：{address}", this);
                    return;
                }
                GameObject model = Instantiate(
                    handle.Result,
                    targetView.View.transform,
                    false);
                model.name = $"{handle.Result.name} (Presentation Only)";
                model.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                model.transform.localScale = Vector3.one;
                targetView.Model = model;
                targetView.Definition = model.GetComponent<CoopEnemyPresentationDefinition>();
                targetView.Animation = model.AddComponent<CoopEnemyVisualAnimator>();
                targetView.Renderers = model.GetComponentsInChildren<Renderer>(true);
                targetView.Renderer = model.GetComponentInChildren<Renderer>(
                    includeInactive: true);
            };
        }

        private static bool IsPurePresentation(GameObject root)
        {
            foreach (Component component in root.GetComponentsInChildren<Component>(true))
                if (component == null || component is not Transform and not Renderer and not MeshFilter and
                    not Animator and not CoopEnemyPresentationDefinition) return false;
            return root.GetComponent<CoopEnemyPresentationDefinition>() != null;
        }

        private bool IsInFrustum(TargetView targetView)
        {
            if (targetView.UsesDebugPrimitive)
                return targetView.Renderer == null || GeometryUtility.TestPlanesAABB(frustumPlanes, targetView.Renderer.bounds);
            if (targetView.Renderers == null || targetView.Renderers.Length == 0) return false;
            Bounds bounds = targetView.Renderers[0].bounds;
            for (int index = 1; index < targetView.Renderers.Length; index++) bounds.Encapsulate(targetView.Renderers[index].bounds);
            return GeometryUtility.TestPlanesAABB(frustumPlanes, bounds);
        }

        private Camera ResolveCamera()
        {
            if (presentationCamera == null) presentationCamera = Camera.main;
            return presentationCamera;
        }

        private bool CanRender() => forcePresentationForTests ||
            SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null ||
            (TraceEnabled && NetworkManager.Singleton != null &&
             NetworkManager.Singleton.IsClient);

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
            public Renderer[] Renderers;
            public CoopEnemyPresentationDefinition Definition;
            public CoopEnemyVisualAnimator Animation;
            public RemoteSnapshotInterpolator Snapshots = new(32, 0);
            public long LastSnapshotTick = -1;
            public double DisplayedTick = -1d;
            public int RunGeneration;
            public string Address;
            public AsyncOperationHandle<GameObject> LoadHandle;
            public bool HasLoadHandle;
            public bool ReleaseRequested;
            public bool UsesDebugPrimitive;
            public Vector3 TargetPosition;
            public Quaternion TargetRotation;
            public int SpawnGeneration = -1;
            public bool Initialized;
            public float Health;
            public float MaximumHealth;
            public AuthoritativeEnemyBehavior Behavior;
            public double LastTraceAt;
            public Vector3 LastTraceTarget;
            public Vector3 LastTraceView;
            public bool HasTraceSample;
        }
    }
}
