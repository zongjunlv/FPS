using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class EnemySpatialHash
{
    private readonly struct Cell : IEquatable<Cell>
    {
        public Cell(int x, int z)
        {
            X = x;
            Z = z;
        }

        public int X { get; }
        public int Z { get; }

        public bool Equals(Cell other) => X == other.X && Z == other.Z;
        public override bool Equals(object obj) =>
            obj is Cell other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(X, Z);
    }

    private sealed class Entry
    {
        public EnemyPerceptionController Perception;
        public EnemyController Controller;
        public Health Health;
        public Cell Cell;
        public int RegistrationOrder;
    }

    private readonly float cellSize;
    private readonly Dictionary<Cell, List<Entry>> buckets;
    private readonly Dictionary<EnemyPerceptionController, Entry> entries;
    private readonly List<Entry> orderedEntries;
    private readonly Stack<List<Entry>> bucketPool = new();
    private int nextRegistrationOrder;

    public EnemySpatialHash(float configuredCellSize, int capacity = 128)
    {
        cellSize = Mathf.Max(0.5f, configuredCellSize);
        int safeCapacity = Mathf.Max(1, capacity);
        buckets = new Dictionary<Cell, List<Entry>>(safeCapacity);
        entries = new Dictionary<EnemyPerceptionController, Entry>(
            safeCapacity);
        orderedEntries = new List<Entry>(safeCapacity);
    }

    public int RegisteredCount => entries.Count;
    public int OccupiedCellCount => buckets.Count;
    public int LastCandidateVisitCount { get; private set; }
    public long TotalCandidateVisitCount { get; private set; }

    public bool Register(EnemyPerceptionController perception)
    {
        if (perception == null)
        {
            return false;
        }

        if (entries.ContainsKey(perception))
        {
            Update(perception);
            return false;
        }

        Cell cell = ResolveCell(perception.transform.position);
        var entry = new Entry
        {
            Perception = perception,
            Controller = perception.GetComponent<EnemyController>(),
            Health = perception.GetComponent<Health>(),
            Cell = cell,
            RegistrationOrder = nextRegistrationOrder++
        };
        entries.Add(perception, entry);
        orderedEntries.Add(entry);
        GetOrCreateBucket(cell).Add(entry);
        return true;
    }

    public bool Update(EnemyPerceptionController perception)
    {
        if (ReferenceEquals(perception, null) ||
            !entries.TryGetValue(perception, out Entry entry))
        {
            return false;
        }

        entry.Controller ??= perception.GetComponent<EnemyController>();
        entry.Health ??= perception.GetComponent<Health>();

        Cell next = ResolveCell(perception.transform.position);

        if (next.Equals(entry.Cell))
        {
            return false;
        }

        RemoveFromBucket(entry);
        entry.Cell = next;
        GetOrCreateBucket(next).Add(entry);
        return true;
    }

    public bool Unregister(EnemyPerceptionController perception)
    {
        if (ReferenceEquals(perception, null) ||
            !entries.TryGetValue(perception, out Entry entry))
        {
            return false;
        }

        entries.Remove(perception);
        orderedEntries.Remove(entry);
        RemoveFromBucket(entry);
        return true;
    }

    public int Query(
        Vector3 center,
        float radius,
        EnemyPerceptionController excluded,
        string requiredTag,
        int maximumResults,
        List<EnemyPerceptionController> results)
    {
        if (results == null)
        {
            throw new ArgumentNullException(nameof(results));
        }

        results.Clear();
        LastCandidateVisitCount = 0;
        int safeMaximum = Mathf.Max(0, maximumResults);

        if (safeMaximum == 0)
        {
            return 0;
        }

        float safeRadius = Mathf.Max(0f, radius);
        float squaredRadius = safeRadius * safeRadius;
        // The service synchronizes buckets in LateUpdate. Expanding the cell
        // range by one keeps a transform that just crossed a cell boundary
        // queryable during the current frame; the exact radius check below
        // still rejects false positives.
        int minimumX = Mathf.FloorToInt((center.x - safeRadius) / cellSize) - 1;
        int maximumX = Mathf.FloorToInt((center.x + safeRadius) / cellSize) + 1;
        int minimumZ = Mathf.FloorToInt((center.z - safeRadius) / cellSize) - 1;
        int maximumZ = Mathf.FloorToInt((center.z + safeRadius) / cellSize) + 1;
        string tag = NormalizeTag(requiredTag);

        for (int x = minimumX; x <= maximumX; x++)
        {
            for (int z = minimumZ; z <= maximumZ; z++)
            {
                if (!buckets.TryGetValue(new Cell(x, z), out List<Entry> bucket))
                {
                    continue;
                }

                for (int index = 0; index < bucket.Count; index++)
                {
                    Entry entry = bucket[index];
                    LastCandidateVisitCount++;

                    if (!IsEligible(entry, excluded, tag, center, squaredRadius))
                    {
                        continue;
                    }

                    results.Add(entry.Perception);
                }
            }
        }

        // Unlimited production queries do their own domain-specific ordering
        // (for example support target priority), so avoid paying for a stable
        // sort when no truncation is requested.
        if (safeMaximum != int.MaxValue)
        {
            SortByRegistrationOrder(results);

            if (results.Count > safeMaximum)
            {
                results.RemoveRange(safeMaximum, results.Count - safeMaximum);
            }
        }

        TotalCandidateVisitCount += LastCandidateVisitCount;
        return results.Count;
    }

    public int QueryNaively(
        Vector3 center,
        float radius,
        EnemyPerceptionController excluded,
        string requiredTag,
        int maximumResults,
        List<EnemyPerceptionController> results)
    {
        if (results == null)
        {
            throw new ArgumentNullException(nameof(results));
        }

        results.Clear();
        LastCandidateVisitCount = 0;
        int safeMaximum = Mathf.Max(0, maximumResults);

        if (safeMaximum == 0)
        {
            return 0;
        }

        float safeRadius = Mathf.Max(0f, radius);
        float squaredRadius = safeRadius * safeRadius;
        string tag = NormalizeTag(requiredTag);

        for (int index = 0;
             index < orderedEntries.Count && results.Count < safeMaximum;
             index++)
        {
            Entry entry = orderedEntries[index];
            LastCandidateVisitCount++;

            if (IsEligible(entry, excluded, tag, center, squaredRadius))
            {
                results.Add(entry.Perception);
            }
        }

        TotalCandidateVisitCount += LastCandidateVisitCount;
        return results.Count;
    }

    private void SortByRegistrationOrder(
        List<EnemyPerceptionController> results)
    {
        // Insertion sort avoids a comparer allocation and keeps the indexed
        // path deterministic with the naive registration-order scan.
        for (int index = 1; index < results.Count; index++)
        {
            EnemyPerceptionController value = results[index];
            int order = entries[value].RegistrationOrder;
            int insertionIndex = index - 1;

            while (insertionIndex >= 0 &&
                   entries[results[insertionIndex]].RegistrationOrder > order)
            {
                results[insertionIndex + 1] = results[insertionIndex];
                insertionIndex--;
            }

            results[insertionIndex + 1] = value;
        }
    }

    private static string NormalizeTag(string requiredTag) =>
        string.IsNullOrWhiteSpace(requiredTag)
            ? "enemy"
            : requiredTag.Trim();

    private static bool IsEligible(
        Entry entry,
        EnemyPerceptionController excluded,
        string requiredTag,
        Vector3 center,
        float squaredRadius)
    {
        EnemyPerceptionController perception = entry.Perception;

        if (perception == null || perception == excluded ||
            !perception.isActiveAndEnabled)
        {
            return false;
        }

        if (entry.Controller != null)
        {
            if (!entry.Controller.isActiveAndEnabled ||
                entry.Health == null || entry.Health.IsDead ||
                !entry.Controller.HasGameplayTag(requiredTag))
            {
                return false;
            }
        }
        else if (requiredTag != "*")
        {
            return false;
        }

        Vector3 offset = perception.transform.position - center;
        offset.y = 0f;
        return offset.sqrMagnitude <= squaredRadius;
    }

    private Cell ResolveCell(Vector3 position)
    {
        return new Cell(
            Mathf.FloorToInt(position.x / cellSize),
            Mathf.FloorToInt(position.z / cellSize));
    }

    private List<Entry> GetOrCreateBucket(Cell cell)
    {
        if (buckets.TryGetValue(cell, out List<Entry> bucket))
        {
            return bucket;
        }

        bucket = bucketPool.Count > 0
            ? bucketPool.Pop()
            : new List<Entry>(8);
        buckets.Add(cell, bucket);
        return bucket;
    }

    private void RemoveFromBucket(Entry entry)
    {
        if (!buckets.TryGetValue(entry.Cell, out List<Entry> bucket))
        {
            return;
        }

        bucket.Remove(entry);

        if (bucket.Count > 0)
        {
            return;
        }

        buckets.Remove(entry.Cell);
        bucket.Clear();
        bucketPool.Push(bucket);
    }
}
