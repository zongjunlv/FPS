using System;
using System.Globalization;
using System.Text;
using System.Threading;
using FPS.Networking.Domain;
using UnityEngine;

namespace FPS.Networking.Netcode
{
    public readonly struct CoopMovementCollisionDiagnostic
    {
        public CoopMovementCollisionDiagnostic(int solverId, int playerId,
            int index, int pass, int hitCount, Collider collider,
            Vector3 cursor, Vector3 displacement, float allowedDistance,
            float hitDistance, Vector3 hitNormal)
        {
            SolverId = solverId;
            PlayerId = playerId;
            Index = index;
            Pass = pass;
            HitCount = hitCount;
            ColliderEntityId = collider.GetEntityId().ToString();
            ColliderHierarchy = ColliderPath(collider.transform);
            ColliderType = collider.GetType().Name;
            ColliderLayer = collider.gameObject.layer;
            ColliderScene = Token(collider.gameObject.scene.name);
            ColliderPosition = collider.transform.position;
            Cursor = cursor;
            Displacement = displacement;
            AllowedDistance = allowedDistance;
            HitDistance = hitDistance;
            HitNormal = hitNormal;
        }

        public int SolverId { get; }
        public int PlayerId { get; }
        public int Index { get; }
        public int Pass { get; }
        public int HitCount { get; }
        public string ColliderEntityId { get; }
        public string ColliderHierarchy { get; }
        public string ColliderType { get; }
        public int ColliderLayer { get; }
        public string ColliderScene { get; }
        public Vector3 ColliderPosition { get; }
        public Vector3 Cursor { get; }
        public Vector3 Displacement { get; }
        public float AllowedDistance { get; }
        public float HitDistance { get; }
        public Vector3 HitNormal { get; }

        private static string ColliderPath(Transform transform)
        {
            var path = new StringBuilder();
            for (int depth = 0; transform != null && depth < 64; depth++)
            {
                if (path.Length > 0) path.Insert(0, '/');
                path.Insert(0, Token(transform.name));
                if (path.Length >= 512) break;
                transform = transform.parent;
            }
            return path.Length > 512 ? path.ToString(0, 512) : path.ToString();
        }

        private static string Token(string value) => value
            .Replace(' ', '_').Replace('\t', '_').Replace('\r', '_').Replace('\n', '_');
    }

    /// <summary>
    /// Identical static-world capsule sweep for authority, owner prediction,
    /// and acknowledgement replay. Each caller owns its query scratch buffer.
    /// Vertical integration remains in the shared gameplay movement rules.
    /// </summary>
    public sealed class CoopPlayerMovementCollision
    {
        public const float CapsuleRadius = 0.28f;
        public const float CapsuleHeight = 1.8f;
        public const float CapsuleSkin = 0.03f;
        public const int MaximumDiagnosticSamples = 128;
        private static readonly bool TraceEnabled = string.Equals(
            Environment.GetEnvironmentVariable("FPS_NETTRACE"), "1",
            StringComparison.Ordinal);
        private static int nextSolverId;
        private readonly int solverId = Interlocked.Increment(ref nextSolverId);
        private readonly RaycastHit[] movementHits = new RaycastHit[24];
        private int diagnosticCount;

        public event Action<CoopMovementCollisionDiagnostic> ObstructionDiagnosed;
        public int DiagnosticSampleCount => diagnosticCount;

        public PlayerMovementState Resolve(
            int playerId,
            PlayerMovementState current,
            PlayerMovementState desired)
        {
            Vector3 from = NetcodeConversions.ToUnity(current.Position);
            Vector3 to = NetcodeConversions.ToUnity(desired.Position);
            Vector3 horizontal = new(to.x - from.x, 0f, to.z - from.z);
            if (horizontal.sqrMagnitude <= 0.00000001f)
                return desired;

            Vector3 resolvedPosition = from +
                ResolveHorizontalDisplacement(from, horizontal, playerId);
            resolvedPosition.y = to.y;
            return new PlayerMovementState(
                NetcodeConversions.ToDomain(resolvedPosition),
                desired.Velocity,
                desired.AimYawDegrees,
                desired.AimPitchDegrees,
                desired.Stance,
                desired.Grounded,
                desired.LastJumpTick,
                desired.GroundHeight);
        }

        public Vector3 ResolveHorizontalDisplacement(
            Vector3 origin,
            Vector3 requestedDisplacement) =>
            ResolveHorizontalDisplacement(origin, requestedDisplacement, 0);

        private Vector3 ResolveHorizontalDisplacement(
            Vector3 origin, Vector3 requestedDisplacement, int playerId)
        {
            const float radius = CapsuleRadius;
            const float height = CapsuleHeight;
            const float skin = CapsuleSkin;
            Vector3 resolved = Vector3.zero;
            Vector3 remaining = requestedDisplacement;
            remaining.y = 0f;

            // Resolve the initial impact and one secondary corner impact.
            // Projecting the remainder onto the contact plane permits wall
            // sliding without giving the owner an extra travel allowance.
            for (int pass = 0; pass < 2; pass++)
            {
                float distance = remaining.magnitude;
                if (distance <= 0.0001f) break;
                Vector3 direction = remaining / distance;
                Vector3 cursor = origin + resolved;
                Vector3 bottom = cursor + Vector3.up * (radius + skin);
                Vector3 top = cursor +
                    Vector3.up * (height - radius - skin);
                int count = Physics.CapsuleCastNonAlloc(
                    bottom,
                    top,
                    radius,
                    direction,
                    movementHits,
                    distance + skin,
                    Physics.DefaultRaycastLayers,
                    QueryTriggerInteraction.Ignore);
                RaycastHit? nearest = null;
                for (int index = 0; index < count; index++)
                {
                    RaycastHit hit = movementHits[index];
                    if (!IsWorldMovementObstacle(hit.collider)) continue;
                    if (nearest == null ||
                        hit.distance < nearest.Value.distance)
                        nearest = hit;
                }
                if (nearest == null)
                {
                    resolved += remaining;
                    break;
                }

                float allowed = Mathf.Max(0f,
                    nearest.Value.distance - skin);
                TraceObstruction(playerId, pass, count, nearest.Value,
                    cursor, remaining, allowed);
                Vector3 advanced = direction *
                    Mathf.Min(distance, allowed);
                resolved += advanced;
                remaining -= advanced;

                Vector3 surfaceNormal = nearest.Value.normal;
                surfaceNormal.y = 0f;
                if (surfaceNormal.sqrMagnitude <= 0.0001f) break;
                surfaceNormal.Normalize();
                remaining = Vector3.ProjectOnPlane(
                    remaining, surfaceNormal);
            }
            return resolved;
        }

        private void TraceObstruction(int playerId, int pass, int hitCount,
            RaycastHit hit, Vector3 cursor, Vector3 remaining, float allowed)
        {
            if ((!TraceEnabled && ObstructionDiagnosed == null) ||
                diagnosticCount >= MaximumDiagnosticSamples) return;
            var value = new CoopMovementCollisionDiagnostic(solverId, playerId,
                ++diagnosticCount, pass, hitCount, hit.collider, cursor,
                remaining, allowed, hit.distance, hit.normal);
            ObstructionDiagnosed?.Invoke(value);
            if (!TraceEnabled) return;
            Debug.Log(string.Format(CultureInfo.InvariantCulture,
                "[NETTRACE-v1] role=physics kind=movementCollision utcMs={0} monoMs={1:F1} solver={2} player={3} diagnosticIndex={4} pass={5} hitCount={6} colliderId={7} colliderPath={8} colliderType={9} colliderLayer={10} colliderScene={11} colliderX={12:F6} colliderY={13:F6} colliderZ={14:F6} cursorX={15:F6} cursorY={16:F6} cursorZ={17:F6} requestX={18:F6} requestY={19:F6} requestZ={20:F6} allowed={21:F6} hitDistance={22:F6} normalX={23:F6} normalY={24:F6} normalZ={25:F6}",
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Time.realtimeSinceStartupAsDouble * 1000d,
                value.SolverId, value.PlayerId, value.Index, value.Pass,
                value.HitCount, value.ColliderEntityId, value.ColliderHierarchy,
                value.ColliderType, value.ColliderLayer, value.ColliderScene,
                value.ColliderPosition.x, value.ColliderPosition.y, value.ColliderPosition.z,
                value.Cursor.x, value.Cursor.y, value.Cursor.z,
                value.Displacement.x, value.Displacement.y, value.Displacement.z,
                value.AllowedDistance, value.HitDistance, value.HitNormal.x,
                value.HitNormal.y, value.HitNormal.z));
        }

        private static bool IsWorldMovementObstacle(Collider candidate)
        {
            if (candidate == null) return false;
            if (candidate.GetComponentInParent<NetworkPlayerReplica>() != null)
                return false;
            if (candidate.GetComponentInParent<CharacterController>() != null)
                return false;
            return true;
        }
    }
}
