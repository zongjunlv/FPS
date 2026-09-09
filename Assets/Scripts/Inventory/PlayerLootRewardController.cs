using System;
using System.Collections.Generic;
using UnityEngine;

public readonly struct LootRewardSettlement
{
    public LootRewardSettlement(
        LootRewardTier tier,
        int waveNumber,
        int sourceId,
        IReadOnlyList<LootDropStack> resolvedDrops,
        int spawnedStackCount)
    {
        RewardTier = tier;
        WaveNumber = Mathf.Max(1, waveNumber);
        SourceId = sourceId;
        ResolvedDrops = resolvedDrops ?? Array.Empty<LootDropStack>();
        SpawnedStackCount = Mathf.Max(0, spawnedStackCount);
    }

    public LootRewardTier RewardTier { get; }
    public int WaveNumber { get; }
    public int SourceId { get; }
    public IReadOnlyList<LootDropStack> ResolvedDrops { get; }
    public int SpawnedStackCount { get; }
}

public sealed class LootRewardRestoreSnapshot
{
    public int RunSeed;
    public int ConfiguredTotalWaves;
    public List<int> ProcessedSpawnIds = new();
    public List<int> RewardedWaves = new();
    public int EnemySettlementCount;
    public int WaveRewardCount;
    public int FinalRewardCount;
    public int SpawnedStackCount;
    public bool FinalRewardRequested;
    public bool AcceptingRewards;
    public bool HasLastDeathPosition;
    public Vector3 LastDeathPosition;
}

[DefaultExecutionOrder(-250)]
public sealed class PlayerLootRewardController : MonoBehaviour
{
    private const float RetryInterval = 0.25f;

    private sealed class PendingLootReward
    {
        public PendingLootReward(
            LootRewardContext context,
            Vector3 origin,
            Transform ignoredRoot,
            IReadOnlyList<LootDropStack> drops,
            string noticePrefix,
            bool highlighted)
        {
            Context = context;
            Origin = origin;
            IgnoredRoot = ignoredRoot;
            Drops = drops ?? Array.Empty<LootDropStack>();
            States = new byte[Drops.Count];
            CommittedDrops = new List<LootDropStack>(Drops.Count);
            NoticePrefix = noticePrefix;
            Highlighted = highlighted;
        }

        public LootRewardContext Context { get; }
        public Vector3 Origin { get; }
        public Transform IgnoredRoot { get; set; }
        public IReadOnlyList<LootDropStack> Drops { get; }
        public byte[] States { get; }
        public List<LootDropStack> CommittedDrops { get; }
        public string NoticePrefix { get; }
        public bool Highlighted { get; }
    }

    private readonly HashSet<int> processedSpawnIds = new();
    private readonly HashSet<int> rewardedWaves = new();
    private readonly List<PendingLootReward> pendingRewards = new();
    private WaveDirector waveDirector;
    private LootDropTableDefinition dropTable;
    private DeterministicLootResolver resolver;
    private PlayerInventoryController inventory;
    private WorldItemFactory worldItemFactory;
    private Health playerHealth;
    private Vector3 lastDeathPosition;
    private bool hasLastDeathPosition;
    private bool finalRewardRequested;
    private bool subscribed;
    private bool healthSubscribed;
    private bool acceptingRewards = true;
    private int configuredTotalWaves;
    private float nextRetryTime;

    public event Action<LootRewardSettlement> RewardSettled;

    public int EnemySettlementCount { get; private set; }
    public int WaveRewardCount { get; private set; }
    public int FinalRewardCount { get; private set; }
    public int SpawnedStackCount { get; private set; }
    public int PendingRewardCount => pendingRewards.Count;
    public LootRewardSettlement LastSettlement { get; private set; }
    public int RunSeed { get; private set; }
    public bool AcceptingRewards => acceptingRewards;
    public bool CanCaptureSnapshot => pendingRewards.Count == 0;
    public bool FinalRewardRequested => finalRewardRequested;

    private void Awake()
    {
        inventory = GetComponent<PlayerInventoryController>();
        playerHealth = GetComponent<Health>();
        worldItemFactory = GetComponent<WorldItemFactory>();
        worldItemFactory ??= gameObject.AddComponent<WorldItemFactory>();
    }

    private void OnEnable()
    {
        SubscribeHealth();
        Subscribe();
    }

    private void OnDisable()
    {
        Unsubscribe();
        UnsubscribeHealth();
    }

    private void Update()
    {
        if (!acceptingRewards || pendingRewards.Count == 0 ||
            Time.unscaledTime < nextRetryTime)
        {
            return;
        }

        nextRetryTime = Time.unscaledTime + RetryInterval;
        RetryPendingRewards();
    }

    public void Configure(
        WaveDirector source,
        LootDropTableDefinition table,
        int runSeed)
    {
        Unsubscribe();
        waveDirector = source;
        dropTable = table;
        RunSeed = runSeed;
        resolver = new DeterministicLootResolver(runSeed);
        acceptingRewards = playerHealth == null || !playerHealth.IsDead;
        configuredTotalWaves = source != null
            ? source.CurrentProgress.TotalWaves
            : 0;
        processedSpawnIds.Clear();
        rewardedWaves.Clear();
        pendingRewards.Clear();
        EnemySettlementCount = 0;
        WaveRewardCount = 0;
        FinalRewardCount = 0;
        SpawnedStackCount = 0;
        LastSettlement = default;
        hasLastDeathPosition = false;
        finalRewardRequested = false;
        nextRetryTime = 0f;
        Subscribe();
    }

    public void ConfigureForTesting(
        LootDropTableDefinition table,
        int runSeed,
        int totalWaves)
    {
        Configure(null, table, runSeed);
        configuredTotalWaves = Mathf.Max(1, totalWaves);
    }

    public bool ProcessEnemyDeath(EnemyDeathEvent death)
    {
        if (!acceptingRewards || resolver == null || dropTable == null ||
            !processedSpawnIds.Add(death.SpawnId))
        {
            return false;
        }

        lastDeathPosition = death.WorldPosition;
        hasLastDeathPosition = true;
        EnemySettlementCount++;
        LootRewardTier tier = death.RewardTier == LootRewardTier.Elite
            ? LootRewardTier.Elite
            : LootRewardTier.Normal;
        QueueReward(
            new LootRewardContext(
                death.EnemyTypeId,
                death.WaveNumber,
                tier,
                death.SpawnId,
                death.LootQuantityMultiplier),
            death.WorldPosition,
            death.Enemy != null ? death.Enemy.transform : null,
            tier == LootRewardTier.Elite ? "精英奖励" : null,
            tier == LootRewardTier.Elite);

        return true;
    }

    public bool TryCaptureSnapshot(
        out LootRewardRestoreSnapshot snapshot,
        out string error)
    {
        snapshot = null;

        if (pendingRewards.Count > 0)
        {
            error = "仍有尚未落地的奖励，当前帧不能安全保存。";
            return false;
        }

        var spawnIds = new List<int>(processedSpawnIds);
        var waves = new List<int>(rewardedWaves);
        spawnIds.Sort();
        waves.Sort();
        snapshot = new LootRewardRestoreSnapshot
        {
            RunSeed = RunSeed,
            ConfiguredTotalWaves = configuredTotalWaves,
            ProcessedSpawnIds = spawnIds,
            RewardedWaves = waves,
            EnemySettlementCount = EnemySettlementCount,
            WaveRewardCount = WaveRewardCount,
            FinalRewardCount = FinalRewardCount,
            SpawnedStackCount = SpawnedStackCount,
            FinalRewardRequested = finalRewardRequested,
            AcceptingRewards = acceptingRewards,
            HasLastDeathPosition = hasLastDeathPosition,
            LastDeathPosition = lastDeathPosition
        };
        error = string.Empty;
        return true;
    }

    public bool CanRestoreSnapshot(
        LootRewardRestoreSnapshot snapshot,
        out string error)
    {
        return TryValidateSnapshot(snapshot, out _, out _, out error);
    }

    public bool TryRestoreSnapshot(
        LootRewardRestoreSnapshot snapshot,
        out string error)
    {
        if (!TryValidateSnapshot(
                snapshot,
                out HashSet<int> spawnIds,
                out HashSet<int> waves,
                out error))
        {
            return false;
        }

        Unsubscribe();
        processedSpawnIds.Clear();
        rewardedWaves.Clear();

        foreach (int spawnId in spawnIds)
        {
            processedSpawnIds.Add(spawnId);
        }

        foreach (int wave in waves)
        {
            rewardedWaves.Add(wave);
        }

        RunSeed = snapshot.RunSeed;
        configuredTotalWaves = snapshot.ConfiguredTotalWaves;
        resolver = new DeterministicLootResolver(RunSeed);
        EnemySettlementCount = snapshot.EnemySettlementCount;
        WaveRewardCount = snapshot.WaveRewardCount;
        FinalRewardCount = snapshot.FinalRewardCount;
        SpawnedStackCount = snapshot.SpawnedStackCount;
        finalRewardRequested = snapshot.FinalRewardRequested;
        acceptingRewards = snapshot.AcceptingRewards;
        hasLastDeathPosition = snapshot.HasLastDeathPosition;
        lastDeathPosition = snapshot.LastDeathPosition;
        LastSettlement = default;
        nextRetryTime = 0f;

        if (acceptingRewards)
        {
            Subscribe();
        }

        error = string.Empty;
        return true;
    }

    public bool ProcessWaveEnded(int waveNumber)
    {
        if (!acceptingRewards)
        {
            return false;
        }

        int totalWaves = waveDirector != null
            ? waveDirector.CurrentProgress.TotalWaves
            : configuredTotalWaves;

        if (waveNumber <= 0 || waveNumber >= totalWaves ||
            !rewardedWaves.Add(waveNumber))
        {
            return false;
        }

        QueueReward(
            new LootRewardContext(
                "*",
                waveNumber,
                LootRewardTier.WaveClear,
                -waveNumber),
            transform.position,
            transform,
            $"第 {waveNumber} 波奖励",
            false);
        return true;
    }

    public bool ProcessRunCompleted()
    {
        if (!acceptingRewards || finalRewardRequested || resolver == null ||
            dropTable == null)
        {
            return false;
        }

        finalRewardRequested = true;
        int finalWave = waveDirector != null
            ? Mathf.Max(1, waveDirector.CurrentProgress.TotalWaves)
            : Mathf.Max(1, configuredTotalWaves);
        QueueReward(
            new LootRewardContext(
                "*",
                finalWave,
                LootRewardTier.FinalWave,
                int.MinValue + finalWave),
            hasLastDeathPosition ? lastDeathPosition : transform.position,
            null,
            "最终波奖励",
            true);
        return true;
    }

    public void RetryPendingRewards()
    {
        if (!acceptingRewards)
        {
            return;
        }

        for (int index = pendingRewards.Count - 1; index >= 0; index--)
        {
            PendingLootReward pending = pendingRewards[index];

            if (TryCommitPending(pending))
            {
                pendingRewards.RemoveAt(index);
            }
        }
    }

    private void QueueReward(
        LootRewardContext context,
        Vector3 origin,
        Transform ignoredRoot,
        string noticePrefix,
        bool highlighted)
    {
        if (!acceptingRewards)
        {
            return;
        }

        IReadOnlyList<LootDropStack> drops = resolver.Resolve(
            dropTable,
            context);
        var pending = new PendingLootReward(
            context,
            origin,
            ignoredRoot,
            drops,
            noticePrefix,
            highlighted);

        if (!TryCommitPending(pending))
        {
            // 池化敌人下一帧可能已代表新租约，重试不可保留旧尸体引用。
            pending.IgnoredRoot = null;
            pendingRewards.Add(pending);
            nextRetryTime = Time.unscaledTime + RetryInterval;
        }
    }

    private bool TryCommitPending(PendingLootReward pending)
    {
        bool hasUnresolvedDrop = false;

        for (int index = 0; index < pending.Drops.Count; index++)
        {
            if (pending.States[index] != 0)
            {
                continue;
            }

            LootDropStack drop = pending.Drops[index];

            if (drop.Quantity <= 0 || inventory == null ||
                !inventory.TryGetDefinition(
                    drop.ItemStableId, out ItemDefinition definition))
            {
                pending.States[index] = 2;
                continue;
            }

            if (worldItemFactory == null ||
                !worldItemFactory.TrySpawnRewardDrop(
                    definition,
                    drop.Quantity,
                    pending.Origin,
                    pending.IgnoredRoot,
                    out _))
            {
                hasUnresolvedDrop = true;
                continue;
            }

            pending.States[index] = 1;
            pending.CommittedDrops.Add(drop);
            SpawnedStackCount++;
        }

        if (hasUnresolvedDrop)
        {
            return false;
        }

        LastSettlement = new LootRewardSettlement(
            pending.Context.RewardTier,
            pending.Context.WaveNumber,
            pending.Context.UniqueId,
            pending.CommittedDrops.ToArray(),
            pending.CommittedDrops.Count);

        if (pending.Context.RewardTier == LootRewardTier.WaveClear)
        {
            WaveRewardCount++;
        }
        else if (pending.Context.RewardTier == LootRewardTier.FinalWave)
        {
            FinalRewardCount++;
        }

        RewardSettled?.Invoke(LastSettlement);

        if (!string.IsNullOrEmpty(pending.NoticePrefix))
        {
            ShowHudNotice(
                BuildNotice(pending.NoticePrefix, LastSettlement),
                pending.Highlighted);
        }

        return true;
    }

    private string BuildNotice(
        string prefix,
        LootRewardSettlement settlement)
    {
        if (settlement.SpawnedStackCount <= 0)
        {
            return prefix + "：本次没有生成掉落";
        }

        var parts = new List<string>();

        for (int index = 0;
             index < settlement.ResolvedDrops.Count;
             index++)
        {
            LootDropStack drop = settlement.ResolvedDrops[index];

            if (inventory != null && inventory.TryGetDefinition(
                    drop.ItemStableId,
                    out ItemDefinition definition))
            {
                parts.Add($"{definition.DisplayName} ×{drop.Quantity}");
            }
        }

        return parts.Count > 0
            ? prefix + "：" + string.Join("  ", parts)
            : prefix + $"：已生成 {settlement.SpawnedStackCount} 组物资";
    }

    private static void ShowHudNotice(string message, bool highlighted)
    {
        UnityEngine.Object.FindAnyObjectByType<UnifiedGameHud>()
            ?.ShowRewardCue(message, highlighted);
    }

    private void Subscribe()
    {
        if (!acceptingRewards || !isActiveAndEnabled || subscribed ||
            waveDirector == null)
        {
            return;
        }

        waveDirector.EnemyDied += HandleEnemyDied;
        waveDirector.WaveEnded += HandleWaveEnded;
        waveDirector.WaveCompleted += HandleWaveCompleted;
        subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!subscribed)
        {
            return;
        }

        if (waveDirector != null)
        {
            waveDirector.EnemyDied -= HandleEnemyDied;
            waveDirector.WaveEnded -= HandleWaveEnded;
            waveDirector.WaveCompleted -= HandleWaveCompleted;
        }

        subscribed = false;
    }

    private void HandleEnemyDied(EnemyDeathEvent death)
    {
        ProcessEnemyDeath(death);
    }

    private void HandleWaveEnded(int waveNumber)
    {
        ProcessWaveEnded(waveNumber);
    }

    private void HandleWaveCompleted()
    {
        ProcessRunCompleted();
    }

    public void EndRun()
    {
        if (!acceptingRewards)
        {
            return;
        }

        acceptingRewards = false;
        pendingRewards.Clear();
        Unsubscribe();
    }

    private void SubscribeHealth()
    {
        if (healthSubscribed || playerHealth == null)
        {
            return;
        }

        playerHealth.Died += HandlePlayerDied;
        healthSubscribed = true;
    }

    private void UnsubscribeHealth()
    {
        if (!healthSubscribed || playerHealth == null)
        {
            return;
        }

        playerHealth.Died -= HandlePlayerDied;
        healthSubscribed = false;
    }

    private void HandlePlayerDied()
    {
        EndRun();
    }

    private bool TryValidateSnapshot(
        LootRewardRestoreSnapshot snapshot,
        out HashSet<int> spawnIds,
        out HashSet<int> waves,
        out string error)
    {
        spawnIds = null;
        waves = null;

        if (snapshot == null || snapshot.ProcessedSpawnIds == null ||
            snapshot.RewardedWaves == null)
        {
            error = "掉落奖励快照或奖励账本缺失。";
            return false;
        }

        if (pendingRewards.Count > 0)
        {
            error = "当前控制器仍有待结算奖励，不能原子恢复。";
            return false;
        }

        if (dropTable == null || snapshot.ConfiguredTotalWaves < 1 ||
            (configuredTotalWaves > 0 &&
             configuredTotalWaves != snapshot.ConfiguredTotalWaves) ||
            snapshot.EnemySettlementCount < 0 ||
            snapshot.WaveRewardCount < 0 ||
            snapshot.FinalRewardCount < 0 || snapshot.FinalRewardCount > 1 ||
            snapshot.SpawnedStackCount < 0 ||
            !IsFinite(snapshot.LastDeathPosition))
        {
            error = "掉落奖励快照与当前配置不兼容或包含非法数值。";
            return false;
        }

        var validatedSpawnIds = new HashSet<int>();

        for (int index = 0; index < snapshot.ProcessedSpawnIds.Count; index++)
        {
            int spawnId = snapshot.ProcessedSpawnIds[index];

            if (spawnId <= 0 || !validatedSpawnIds.Add(spawnId))
            {
                error = "敌人奖励账本包含非法或重复的 spawnId。";
                return false;
            }
        }

        var validatedWaves = new HashSet<int>();

        for (int index = 0; index < snapshot.RewardedWaves.Count; index++)
        {
            int wave = snapshot.RewardedWaves[index];

            if (wave <= 0 || wave >= snapshot.ConfiguredTotalWaves ||
                !validatedWaves.Add(wave))
            {
                error = "波次奖励账本包含非法或重复的波次。";
                return false;
            }
        }

        if (snapshot.EnemySettlementCount != validatedSpawnIds.Count ||
            snapshot.WaveRewardCount != validatedWaves.Count ||
            snapshot.FinalRewardRequested != (snapshot.FinalRewardCount == 1))
        {
            error = "奖励计数与奖励账本不一致。";
            return false;
        }

        spawnIds = validatedSpawnIds;
        waves = validatedWaves;
        error = string.Empty;
        return true;
    }

    private static bool IsFinite(Vector3 value)
    {
        return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
               !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
               !float.IsNaN(value.z) && !float.IsInfinity(value.z);
    }
}
