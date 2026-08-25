using System;
using System.Collections.Generic;

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
    private const uint FnvOffset = 2166136261;
    private const uint FnvPrime = 16777619;
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

        var random = new LootRandom(CreateSeed(context));
        int rollCount = random.NextInclusive(
            rule.MinimumRolls,
            rule.MaximumRolls);
        var quantities = new Dictionary<string, int>(StringComparer.Ordinal);
        var order = new List<string>();

        for (int roll = 0; roll < rollCount; roll++)
        {
            LootDropEntry selected = SelectWeighted(rule.Entries, ref random);

            if (selected == null || random.NextFloat() > selected.DropChance)
            {
                continue;
            }

            int quantity = random.NextInclusive(
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
        ref LootRandom random)
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

        int selection = random.NextExclusive(totalWeight);

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

    private uint CreateSeed(LootRewardContext context)
    {
        uint hash = FnvOffset;
        Mix(ref hash, "FPS_LOOT_STREAM_V1");
        Mix(ref hash, runSeed.ToString());
        Mix(ref hash, context.EnemyTypeId);
        Mix(ref hash, context.WaveNumber.ToString());
        Mix(ref hash, ((int)context.RewardTier).ToString());
        Mix(ref hash, context.UniqueId.ToString());
        return hash == 0 ? 0x9E3779B9u : hash;
    }

    private static void Mix(ref uint hash, string value)
    {
        string source = value ?? string.Empty;

        for (int index = 0; index < source.Length; index++)
        {
            hash ^= source[index];
            hash *= FnvPrime;
        }

        hash ^= 0xFF;
        hash *= FnvPrime;
    }

    private struct LootRandom
    {
        private uint state;

        public LootRandom(uint seed)
        {
            state = seed == 0 ? 0x9E3779B9u : seed;
        }

        public int NextExclusive(int maximum)
        {
            return maximum <= 1
                ? 0
                : (int)(NextUInt() % (uint)maximum);
        }

        public int NextInclusive(int minimum, int maximum)
        {
            int low = Math.Min(minimum, maximum);
            int high = Math.Max(minimum, maximum);
            long range = (long)high - low + 1L;
            return range <= 1L
                ? low
                : low + (int)(NextUInt() % (uint)range);
        }

        public float NextFloat()
        {
            return (NextUInt() & 0x00FFFFFFu) / 16777215f;
        }

        private uint NextUInt()
        {
            uint value = state;
            value ^= value << 13;
            value ^= value >> 17;
            value ^= value << 5;
            state = value;
            return value;
        }
    }
}
