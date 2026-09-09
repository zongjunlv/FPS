using System;
using System.Collections.Generic;
using System.Globalization;
using FPS.Determinism;

public readonly struct LootRewardContext
{
    public LootRewardContext(
        string enemyTypeId,
        int waveNumber,
        LootRewardTier rewardTier,
        int uniqueId,
        float quantityMultiplier = 1f)
    {
        EnemyTypeId = string.IsNullOrWhiteSpace(enemyTypeId)
            ? "*"
            : enemyTypeId.Trim();
        WaveNumber = Math.Max(1, waveNumber);
        RewardTier = rewardTier;
        UniqueId = uniqueId;
        QuantityMultiplier = float.IsNaN(quantityMultiplier) ||
                             float.IsInfinity(quantityMultiplier)
            ? 1f
            : Math.Max(1f, quantityMultiplier);
    }

    public string EnemyTypeId { get; }
    public int WaveNumber { get; }
    public LootRewardTier RewardTier { get; }
    public int UniqueId { get; }
    public float QuantityMultiplier { get; }
}

public readonly struct LootDropStack
{
    public LootDropStack(string stableId, int quantity)
    {
        ItemStableId = stableId ?? string.Empty;
        Quantity = Math.Max(0, quantity);
    }

    public string ItemStableId { get; }
    public int Quantity { get; }
}

public sealed class DeterministicLootResolver
{
    private readonly int runSeed;

    public DeterministicLootResolver(int seed)
    {
        runSeed = seed;
    }

    public IReadOnlyList<LootDropStack> Resolve(
        LootDropTableDefinition table,
        LootRewardContext context)
    {
        LootDropRule rule = table?.ResolveRule(
            context.EnemyTypeId,
            context.WaveNumber,
            context.RewardTier);

        if (rule == null || rule.Entries == null || rule.Entries.Count == 0)
        {
            return Array.Empty<LootDropStack>();
        }

        DeterministicRandom random = new NamedRandomStreams(runSeed).Fork(
            RunRandomStream.Loot,
            CreateContextKey(context));
        int rollCount = NextInclusive(random,
            rule.MinimumRolls,
            rule.MaximumRolls);
        var quantities = new Dictionary<string, int>(StringComparer.Ordinal);
        var order = new List<string>();

        for (int roll = 0; roll < rollCount; roll++)
        {
            LootDropEntry selected = SelectWeighted(rule.Entries, random);

            if (selected == null || NextFloat(random) > selected.DropChance)
            {
                continue;
            }

            int quantity = NextInclusive(random,
                selected.MinimumQuantity,
                selected.MaximumQuantity);

            if (!quantities.ContainsKey(selected.ItemStableId))
            {
                quantities.Add(selected.ItemStableId, 0);
                order.Add(selected.ItemStableId);
            }

            quantities[selected.ItemStableId] += quantity;
        }

        var result = new List<LootDropStack>(order.Count);

        for (int index = 0; index < order.Count; index++)
        {
            string stableId = order[index];
            int scaledQuantity = Math.Max(
                1,
                (int)Math.Ceiling(
                    quantities[stableId] * context.QuantityMultiplier));
            result.Add(new LootDropStack(stableId, scaledQuantity));
        }

        return result;
    }

    private static LootDropEntry SelectWeighted(
        IReadOnlyList<LootDropEntry> entries,
        DeterministicRandom random)
    {
        int totalWeight = 0;

        for (int index = 0; index < entries.Count; index++)
        {
            if (entries[index] != null)
            {
                totalWeight += entries[index].Weight;
            }
        }

        if (totalWeight <= 0)
        {
            return null;
        }

        int selection = random.NextInt(totalWeight);

        for (int index = 0; index < entries.Count; index++)
        {
            LootDropEntry entry = entries[index];

            if (entry == null)
            {
                continue;
            }

            if (selection < entry.Weight)
            {
                return entry;
            }

            selection -= entry.Weight;
        }

        return null;
    }

    private static string CreateContextKey(LootRewardContext context)
    {
        return string.Join("|",
            context.EnemyTypeId,
            context.WaveNumber.ToString(CultureInfo.InvariantCulture),
            ((int)context.RewardTier).ToString(CultureInfo.InvariantCulture),
            context.UniqueId.ToString(CultureInfo.InvariantCulture));
    }

    private static int NextInclusive(
        DeterministicRandom random,
        int minimum,
        int maximum)
    {
        int low = Math.Min(minimum, maximum);
        int high = Math.Max(minimum, maximum);
        long range = (long)high - low + 1L;
        return range <= 1L ? low : low + random.NextInt((int)range);
    }

    private static float NextFloat(DeterministicRandom random)
    {
        return (random.NextUInt32() & 0x00FFFFFFu) / 16777215f;
    }
}
