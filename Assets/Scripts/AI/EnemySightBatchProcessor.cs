using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

internal sealed class EnemySightBatchProcessor : IDisposable
{
    internal const int MaximumHitsPerRay = 32;
    internal const int MaximumColliderIdsPerEntity = 32;

    private struct PendingRequest
    {
        public EnemyPerceptionController Member;
        public int Generation;
        public int SubmittedFrame;
        public EnemyAiLodTier LodTier;
        public Vector3 ObservedTargetPosition;
    }

    private struct SightInput
    {
        public Vector3 Eye;
        public Vector3 TargetPoint;
        public Vector3 Forward;
        public float SightDistance;
        public float MinimumDot;
        public int SelfColliderOffset;
        public int SelfColliderCount;
        public int TargetColliderOffset;
        public int TargetColliderCount;
        public byte HasTarget;
    }

    [BurstCompile]
    private struct PrepareCommandsJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<SightInput> Inputs;
        public NativeArray<RaycastCommand> Commands;
        public NativeArray<byte> Eligible;
        public PhysicsScene PhysicsScene;

        public void Execute(int index)
        {
            SightInput input = Inputs[index];
            Vector3 toTarget = input.TargetPoint - input.Eye;
            float squaredDistance = toTarget.sqrMagnitude;
            float maximumSquared = input.SightDistance * input.SightDistance;

            if (input.HasTarget == 0 || squaredDistance <= 0.000001f ||
                squaredDistance > maximumSquared)
            {
                Eligible[index] = 0;
                Commands[index] = new RaycastCommand(
                    PhysicsScene,
                    input.Eye,
                    input.Forward,
                    new QueryParameters(
                        Physics.DefaultRaycastLayers,
                        false,
                        QueryTriggerInteraction.Ignore,
                        false),
                    0f);
                return;
            }

            float distance = Mathf.Sqrt(squaredDistance);
            Vector3 direction = toTarget / distance;

            if (Vector3.Dot(input.Forward, direction) < input.MinimumDot)
            {
                Eligible[index] = 0;
                Commands[index] = new RaycastCommand(
                    PhysicsScene,
                    input.Eye,
                    direction,
                    new QueryParameters(
                        Physics.DefaultRaycastLayers,
                        false,
                        QueryTriggerInteraction.Ignore,
                        false),
                    0f);
                return;
            }

            Eligible[index] = 1;
            Commands[index] = new RaycastCommand(
                PhysicsScene,
                input.Eye,
                direction,
                new QueryParameters(
                    Physics.DefaultRaycastLayers,
                    false,
                    QueryTriggerInteraction.Ignore,
                    false),
                distance + 0.1f);
        }
    }

    [BurstCompile]
    private struct ResolveResultsJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<SightInput> Inputs;
        [ReadOnly] public NativeArray<byte> Eligible;
        [ReadOnly] public NativeArray<RaycastHit> Hits;
        [ReadOnly] public NativeArray<EntityId> SelfColliderIds;
        [ReadOnly] public NativeArray<EntityId> TargetColliderIds;
        public NativeArray<byte> Visible;
        public NativeArray<byte> Saturated;

        public void Execute(int index)
        {
            if (Eligible[index] == 0)
            {
                Visible[index] = 0;
                Saturated[index] = 0;
                return;
            }

            SightInput input = Inputs[index];
            int hitOffset = index * MaximumHitsPerRay;
            EntityId nearestColliderId = default;
            float nearestDistance = float.MaxValue;
            byte reachedHitLimit = 1;

            for (int hitIndex = 0;
                 hitIndex < MaximumHitsPerRay;
                 hitIndex++)
            {
                RaycastHit hit = Hits[hitOffset + hitIndex];
                EntityId colliderId = hit.colliderEntityId;

                // RaycastCommand guarantees a contiguous hit sequence and
                // writes a null collider immediately after the last result.
                // Entries beyond that terminator are undefined and may still
                // contain data from an older batch when buffers are reused.
                if (colliderId == default)
                {
                    reachedHitLimit = 0;
                    break;
                }

                if (Contains(
                        SelfColliderIds,
                        input.SelfColliderOffset,
                        input.SelfColliderCount,
                        colliderId))
                {
                    continue;
                }

                if (hit.distance < nearestDistance)
                {
                    nearestDistance = hit.distance;
                    nearestColliderId = colliderId;
                }
            }

            Visible[index] = (byte)(nearestColliderId == default || Contains(
                TargetColliderIds,
                input.TargetColliderOffset,
                input.TargetColliderCount,
                nearestColliderId) ? 1 : 0);
            Saturated[index] = reachedHitLimit;
        }

        private static bool Contains(
            NativeArray<EntityId> ids,
            int offset,
            int count,
            EntityId expected)
        {
            for (int index = 0; index < count; index++)
            {
                if (ids[offset + index] == expected)
                {
                    return true;
                }
            }

            return false;
        }
    }

    private NativeList<SightInput> inputs;
    private NativeList<RaycastCommand> commands;
    private NativeList<RaycastHit> hits;
    private NativeList<EntityId> selfColliderIds;
    private NativeList<EntityId> targetColliderIds;
    private NativeList<byte> eligible;
    private NativeList<byte> visible;
    private NativeList<byte> saturated;
    private PendingRequest[] pendingRequests = Array.Empty<PendingRequest>();
    private JobHandle pendingHandle;
    private int pendingCount;
    private bool hasPending;

    public EnemySightBatchProcessor(int initialCapacity)
    {
        int capacity = Mathf.Max(1, initialCapacity);
        inputs = new NativeList<SightInput>(capacity, Allocator.Persistent);
        commands = new NativeList<RaycastCommand>(capacity, Allocator.Persistent);
        hits = new NativeList<RaycastHit>(
            capacity * MaximumHitsPerRay,
            Allocator.Persistent);
        selfColliderIds = new NativeList<EntityId>(
            capacity * MaximumColliderIdsPerEntity,
            Allocator.Persistent);
        targetColliderIds = new NativeList<EntityId>(
            capacity * MaximumColliderIdsPerEntity,
            Allocator.Persistent);
        eligible = new NativeList<byte>(capacity, Allocator.Persistent);
        visible = new NativeList<byte>(capacity, Allocator.Persistent);
        saturated = new NativeList<byte>(capacity, Allocator.Persistent);
        pendingRequests = new PendingRequest[capacity];
    }

    public bool HasPending => hasPending;
    public int PendingCount => pendingCount;
    public long ScheduledBatchCount { get; private set; }
    public long CompletedBatchCount { get; private set; }
    public long ScheduledCommandCount { get; private set; }
    public long DiscardedStaleResultCount { get; private set; }
    public int PeakBatchSize { get; private set; }

    public void ResetMetrics()
    {
        ScheduledBatchCount = 0;
        CompletedBatchCount = 0;
        ScheduledCommandCount = 0;
        DiscardedStaleResultCount = 0;
        PeakBatchSize = 0;
    }

    public bool Schedule(
        System.Collections.Generic.List<EnemyPerceptionController> members,
        int submittedFrame)
    {
        if (hasPending || members == null || members.Count == 0)
        {
            return false;
        }

        int count = members.Count;
        EnsureManagedCapacity(count);
        ResizeNativeBuffers(count);
        NativeArray<SightInput> inputArray = inputs.AsArray();
        NativeArray<EntityId> selfIds = selfColliderIds.AsArray();
        NativeArray<EntityId> targetIds = targetColliderIds.AsArray();

        for (int index = 0; index < count; index++)
        {
            EnemyPerceptionController member = members[index];
            EnemySightQueryDescriptor descriptor =
                member.CreateSightQueryDescriptor();
            int colliderOffset = index * MaximumColliderIdsPerEntity;
            int selfCount = Mathf.Min(
                descriptor.SelfColliderCount,
                MaximumColliderIdsPerEntity);
            int targetCount = Mathf.Min(
                descriptor.TargetColliderCount,
                MaximumColliderIdsPerEntity);

            for (int collider = 0; collider < selfCount; collider++)
            {
                selfIds[colliderOffset + collider] =
                    descriptor.SelfColliderIds[collider];
            }

            for (int collider = 0; collider < targetCount; collider++)
            {
                targetIds[colliderOffset + collider] =
                    descriptor.TargetColliderIds[collider];
            }

            inputArray[index] = new SightInput
            {
                Eye = descriptor.Eye,
                TargetPoint = descriptor.TargetPoint,
                Forward = descriptor.Forward,
                SightDistance = descriptor.SightDistance,
                MinimumDot = descriptor.MinimumDot,
                SelfColliderOffset = colliderOffset,
                SelfColliderCount = selfCount,
                TargetColliderOffset = colliderOffset,
                TargetColliderCount = targetCount,
                HasTarget = (byte)(descriptor.HasTarget ? 1 : 0)
            };
            pendingRequests[index] = new PendingRequest
            {
                Member = member,
                Generation = member.SightGeneration,
                SubmittedFrame = submittedFrame,
                LodTier = member.LodTier,
                ObservedTargetPosition = descriptor.ObservedTargetPosition
            };
        }

        var prepareJob = new PrepareCommandsJob
        {
            Inputs = inputArray,
            Commands = commands.AsArray(),
            Eligible = eligible.AsArray(),
            PhysicsScene = Physics.defaultPhysicsScene
        };
        JobHandle prepareHandle = prepareJob.Schedule(count, 16);
        JobHandle raycastHandle = RaycastCommand.ScheduleBatch(
            commands.AsArray(),
            hits.AsArray(),
            1,
            MaximumHitsPerRay,
            prepareHandle);
        var resolveJob = new ResolveResultsJob
        {
            Inputs = inputArray,
            Eligible = eligible.AsArray(),
            Hits = hits.AsArray(),
            SelfColliderIds = selfIds,
            TargetColliderIds = targetIds,
            Visible = visible.AsArray(),
            Saturated = saturated.AsArray()
        };
        pendingHandle = resolveJob.Schedule(count, 16, raycastHandle);
        pendingCount = count;
        hasPending = true;
        ScheduledBatchCount++;
        ScheduledCommandCount += count;
        PeakBatchSize = Mathf.Max(PeakBatchSize, count);
        return true;
    }

    public int CompleteAndApply(
        out int nearCount,
        out int midCount,
        out int farCount)
    {
        nearCount = 0;
        midCount = 0;
        farCount = 0;

        if (!hasPending)
        {
            return 0;
        }

        pendingHandle.Complete();
        NativeArray<byte> visibleResults = visible.AsArray();
        NativeArray<byte> saturatedResults = saturated.AsArray();
        int applied = 0;

        for (int index = 0; index < pendingCount; index++)
        {
            PendingRequest request = pendingRequests[index];

            if (request.Member == null ||
                !request.Member.ApplyBatchedSightResult(
                    request.Generation,
                    visibleResults[index] != 0,
                    saturatedResults[index] != 0,
                    request.SubmittedFrame,
                    request.ObservedTargetPosition))
            {
                DiscardedStaleResultCount++;
                continue;
            }

            applied++;

            switch (request.LodTier)
            {
                case EnemyAiLodTier.Near:
                    nearCount++;
                    break;
                case EnemyAiLodTier.Mid:
                    midCount++;
                    break;
                default:
                    farCount++;
                    break;
            }
        }

        pendingCount = 0;
        hasPending = false;
        CompletedBatchCount++;
        return applied;
    }

    public void Dispose()
    {
        if (hasPending)
        {
            pendingHandle.Complete();
            hasPending = false;
            pendingCount = 0;
        }

        if (inputs.IsCreated) inputs.Dispose();
        if (commands.IsCreated) commands.Dispose();
        if (hits.IsCreated) hits.Dispose();
        if (selfColliderIds.IsCreated) selfColliderIds.Dispose();
        if (targetColliderIds.IsCreated) targetColliderIds.Dispose();
        if (eligible.IsCreated) eligible.Dispose();
        if (visible.IsCreated) visible.Dispose();
        if (saturated.IsCreated) saturated.Dispose();
    }

    private void ResizeNativeBuffers(int count)
    {
        inputs.ResizeUninitialized(count);
        commands.ResizeUninitialized(count);
        hits.ResizeUninitialized(count * MaximumHitsPerRay);
        selfColliderIds.ResizeUninitialized(
            count * MaximumColliderIdsPerEntity);
        targetColliderIds.ResizeUninitialized(
            count * MaximumColliderIdsPerEntity);
        eligible.ResizeUninitialized(count);
        visible.ResizeUninitialized(count);
        saturated.ResizeUninitialized(count);
    }

    private void EnsureManagedCapacity(int count)
    {
        if (pendingRequests.Length >= count)
        {
            return;
        }

        int capacity = Mathf.NextPowerOfTwo(count);
        Array.Resize(ref pendingRequests, capacity);
    }
}

internal readonly struct EnemySightQueryDescriptor
{
    public EnemySightQueryDescriptor(
        bool hasTarget,
        Vector3 eye,
        Vector3 targetPoint,
        Vector3 observedTargetPosition,
        Vector3 forward,
        float sightDistance,
        float minimumDot,
        EntityId[] selfColliderIds,
        int selfColliderCount,
        EntityId[] targetColliderIds,
        int targetColliderCount)
    {
        HasTarget = hasTarget;
        Eye = eye;
        TargetPoint = targetPoint;
        ObservedTargetPosition = observedTargetPosition;
        Forward = forward;
        SightDistance = sightDistance;
        MinimumDot = minimumDot;
        SelfColliderIds = selfColliderIds;
        SelfColliderCount = selfColliderCount;
        TargetColliderIds = targetColliderIds;
        TargetColliderCount = targetColliderCount;
    }

    public bool HasTarget { get; }
    public Vector3 Eye { get; }
    public Vector3 TargetPoint { get; }
    public Vector3 ObservedTargetPosition { get; }
    public Vector3 Forward { get; }
    public float SightDistance { get; }
    public float MinimumDot { get; }
    public EntityId[] SelfColliderIds { get; }
    public int SelfColliderCount { get; }
    public EntityId[] TargetColliderIds { get; }
    public int TargetColliderCount { get; }
}
