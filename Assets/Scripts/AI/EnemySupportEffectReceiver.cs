using System.Collections.Generic;
using FPS.GameplayEffects;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Health))]
public sealed class EnemySupportEffectReceiver : MonoBehaviour
{
    private sealed class SupportLease
    {
        public long InstanceId;
        public Object Source;
        public float RemainingDuration;
    }

    private readonly Dictionary<string, SupportLease> leases = new();
    private readonly List<string> expiredSourceIds = new();
    private GameplayEffectRuntime runtime;
    private Health health;
    private EnemyBurnEffectController overhead;

    public int ActiveSourceCount => leases.Count;
    public int ApplicationCount { get; private set; }
    public int RefreshCount { get; private set; }
    public int RemovalCount { get; private set; }

    private void Awake()
    {
        EnsureInitialized();
    }

    private void EnsureInitialized()
    {
        health = GetComponent<Health>();
        overhead = GetComponent<EnemyBurnEffectController>();
        runtime ??= new GameplayEffectRuntime(
                gameObject,
                "Enemy Support Effects");
    }

    private void Update()
    {
        AdvanceLeases(Time.deltaTime);
    }

    private void OnDisable()
    {
        ClearAll();
    }

    public bool ApplyOrRefresh(
        string sourceId,
        Object source,
        GameplayEffectDefinition definition,
        float duration)
    {
        EnsureInitialized();

        if (health == null || health.IsDead ||
            source == null || source == gameObject ||
            definition == null ||
            definition.DurationPolicy !=
                GameplayEffectDurationPolicy.Persistent ||
            string.IsNullOrWhiteSpace(sourceId))
        {
            return false;
        }

        string normalizedSourceId = sourceId.Trim();
        float safeDuration = Mathf.Max(0.05f, duration);

        if (leases.TryGetValue(
                normalizedSourceId,
                out SupportLease existing))
        {
            existing.RemainingDuration = safeDuration;
            existing.Source = source;
            RefreshCount++;
            SyncPresentation();
            return true;
        }

        GameplayEffectInstance instance = runtime.Apply(
            definition,
            new GameplayEffectContext(
                normalizedSourceId,
                source,
                gameObject));
        leases.Add(
            normalizedSourceId,
            new SupportLease
            {
                InstanceId = instance.InstanceId,
                Source = source,
                RemainingDuration = safeDuration
            });
        ApplicationCount++;
        SyncPresentation();
        return true;
    }

    public bool RemoveSource(string sourceId)
    {
        if (string.IsNullOrWhiteSpace(sourceId) ||
            !leases.TryGetValue(sourceId.Trim(), out SupportLease lease))
        {
            return false;
        }

        runtime.Remove(lease.InstanceId);
        leases.Remove(sourceId.Trim());
        RemovalCount++;
        SyncPresentation();
        return true;
    }

    public void AdvanceLeases(float deltaTime)
    {
        if (leases.Count == 0 || deltaTime <= 0f)
        {
            return;
        }

        expiredSourceIds.Clear();

        foreach (KeyValuePair<string, SupportLease> pair in leases)
        {
            SupportLease lease = pair.Value;
            lease.RemainingDuration -= deltaTime;

            if (lease.RemainingDuration <= 0f ||
                lease.Source == null ||
                lease.Source is Behaviour behaviour &&
                !behaviour.isActiveAndEnabled ||
                lease.Source is GameObject sourceObject &&
                !sourceObject.activeInHierarchy)
            {
                expiredSourceIds.Add(pair.Key);
            }
        }

        for (int index = 0; index < expiredSourceIds.Count; index++)
        {
            RemoveSource(expiredSourceIds[index]);
        }
    }

    public void ClearAll()
    {
        if (leases.Count > 0)
        {
            RemovalCount += leases.Count;
        }

        leases.Clear();
        expiredSourceIds.Clear();
        runtime?.Clear();
        SyncPresentation();
    }

    public bool HasSource(string sourceId)
    {
        return !string.IsNullOrWhiteSpace(sourceId) &&
            leases.ContainsKey(sourceId.Trim());
    }

    public float Evaluate(
        GameplayAttributeId attribute,
        float baseValue)
    {
        return runtime != null
            ? runtime.Evaluate(attribute, baseValue)
            : baseValue;
    }

    private void SyncPresentation()
    {
        overhead ??= GetComponent<EnemyBurnEffectController>();

        if (leases.Count > 0)
        {
            overhead?.SetSupportStatus(
                leases.Count,
                "BOOST",
                new Color(0.35f, 1f, 0.42f, 1f));
        }
        else
        {
            overhead?.ClearSupportStatus();
        }
    }
}
