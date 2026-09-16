using System;
using System.Collections.Generic;
using System.Linq;

namespace FPS.Networking.Domain
{
    public enum AuthoritativeItemEffect : byte
    {
        RestoreHealth = 0,
        RestoreArmor = 1,
        AddRifleAmmo = 2,
        AddHandgunAmmo = 3
    }

    public enum AuthoritativeUpgradeEffect : byte
    {
        WeaponDamage = 0,
        WeaponFireRate = 1,
        MagazineCapacity = 2,
        ReloadSpeed = 3,
        RecoilControl = 4,
        Accuracy = 5,
        MaximumHealth = 6,
        MaximumArmor = 7,
        HealthRestore = 8,
        ArmorRestore = 9,
        MovementSpeed = 10
    }

    public readonly struct AuthoritativeItemDefinition
    {
        public AuthoritativeItemDefinition(
            string stableId,
            int maximumStack,
            AuthoritativeItemEffect effect,
            double effectAmount)
        {
            if (string.IsNullOrWhiteSpace(stableId))
                throw new ArgumentException("Item ID is required.",
                    nameof(stableId));
            if (maximumStack < 1)
                throw new ArgumentOutOfRangeException(nameof(maximumStack));
            if (!Finite(effectAmount) || effectAmount < 0d)
                throw new ArgumentOutOfRangeException(nameof(effectAmount));
            StableId = stableId.Trim();
            MaximumStack = maximumStack;
            Effect = effect;
            EffectAmount = effectAmount;
        }

        public string StableId { get; }
        public int MaximumStack { get; }
        public AuthoritativeItemEffect Effect { get; }
        public double EffectAmount { get; }

        public static IReadOnlyList<AuthoritativeItemDefinition>
            CreateProjectDefaults() => new[]
        {
            new AuthoritativeItemDefinition("medical_kit", 5,
                AuthoritativeItemEffect.RestoreHealth, 35d),
            new AuthoritativeItemDefinition("armor_pack", 5,
                AuthoritativeItemEffect.RestoreArmor, 35d),
            new AuthoritativeItemDefinition("rifle_ammo", 4,
                AuthoritativeItemEffect.AddRifleAmmo, 60d),
            new AuthoritativeItemDefinition("handgun_ammo", 6,
                AuthoritativeItemEffect.AddHandgunAmmo, 24d),
            // Compatibility aliases used by the existing network targets.
            new AuthoritativeItemDefinition("medkit", 5,
                AuthoritativeItemEffect.RestoreHealth, 35d),
            new AuthoritativeItemDefinition("armor_plate", 5,
                AuthoritativeItemEffect.RestoreArmor, 35d)
        };

        private static bool Finite(double value) =>
            !double.IsNaN(value) && !double.IsInfinity(value);
    }

    public readonly struct AuthoritativeUpgradeDefinition
    {
        public AuthoritativeUpgradeDefinition(
            string stableId,
            int maximumLevel,
            AuthoritativeUpgradeEffect effect,
            double effectAmount,
            string buildTag)
        {
            if (string.IsNullOrWhiteSpace(stableId))
                throw new ArgumentException("Upgrade ID is required.",
                    nameof(stableId));
            if (maximumLevel < 1)
                throw new ArgumentOutOfRangeException(nameof(maximumLevel));
            if (!Finite(effectAmount) || effectAmount < 0d)
                throw new ArgumentOutOfRangeException(nameof(effectAmount));
            StableId = stableId.Trim();
            MaximumLevel = maximumLevel;
            Effect = effect;
            EffectAmount = effectAmount;
            BuildTag = buildTag?.Trim() ?? string.Empty;
        }

        public string StableId { get; }
        public int MaximumLevel { get; }
        public AuthoritativeUpgradeEffect Effect { get; }
        public double EffectAmount { get; }
        public string BuildTag { get; }

        public static IReadOnlyList<AuthoritativeUpgradeDefinition>
            CreateProjectDefaults() => new[]
        {
            Upgrade("damage_hardened_rounds", 3,
                AuthoritativeUpgradeEffect.WeaponDamage, 0.25d, "build.damage"),
            Upgrade("damage_overcharged_core", 1,
                AuthoritativeUpgradeEffect.WeaponDamage, 0.35d, "build.damage"),
            Upgrade("damage_weakpoint_analysis", 2,
                AuthoritativeUpgradeEffect.WeaponDamage, 0.2d, "build.weakpoint"),
            Upgrade("fire_rate_rapid_cycling", 3,
                AuthoritativeUpgradeEffect.WeaponFireRate, 0.15d, "build.fire_rate"),
            Upgrade("magazine_extended_capacity", 3,
                AuthoritativeUpgradeEffect.MagazineCapacity, 0.2d, "build.magazine"),
            Upgrade("reload_quick_hands", 3,
                AuthoritativeUpgradeEffect.ReloadSpeed, 0.2d, "build.reload"),
            Upgrade("recoil_dampening", 3,
                AuthoritativeUpgradeEffect.RecoilControl, 0.18d, "build.recoil"),
            Upgrade("accuracy_tight_grouping", 2,
                AuthoritativeUpgradeEffect.Accuracy, 0.2d, "build.accuracy"),
            Upgrade("survival_vitality_reinforcement", 3,
                AuthoritativeUpgradeEffect.MaximumHealth, 0.2d, "build.survival.health"),
            Upgrade("survival_reinforced_plating", 3,
                AuthoritativeUpgradeEffect.MaximumArmor, 0.2d, "build.survival.armor"),
            Upgrade("survival_emergency_treatment", 5,
                AuthoritativeUpgradeEffect.HealthRestore, 30d, "build.restore.health"),
            Upgrade("survival_field_armor_repair", 5,
                AuthoritativeUpgradeEffect.ArmorRestore, 30d, "build.restore.armor"),
            Upgrade("survival_mobility_training", 3,
                AuthoritativeUpgradeEffect.MovementSpeed, 0.1d, "build.mobility")
        };

        private static AuthoritativeUpgradeDefinition Upgrade(
            string id,
            int level,
            AuthoritativeUpgradeEffect effect,
            double amount,
            string tag) => new(id, level, effect, amount, tag);

        private static bool Finite(double value) =>
            !double.IsNaN(value) && !double.IsInfinity(value);
    }

    public enum AuthoritativeEconomyCommandKind : byte
    {
        Pickup = 0,
        Transfer = 1,
        Split = 2,
        Compact = 3,
        Drop = 4,
        Use = 5,
        SelectUpgrade = 6
    }

    public enum AuthoritativeEconomyRejection : byte
    {
        None = 0,
        UnknownPlayer = 1,
        InvalidSequence = 2,
        DuplicateNonce = 3,
        UnknownDrop = 4,
        DropUnavailable = 5,
        DropOwnership = 6,
        OutOfRange = 7,
        InventoryFull = 8,
        InvalidSlot = 9,
        InvalidQuantity = 10,
        InvalidOperation = 11,
        ItemUnavailable = 12,
        CooldownActive = 13,
        EffectUnavailable = 14,
        NoPendingUpgrade = 15,
        InvalidCandidate = 16,
        StaleInventory = 17,
        StaleDrop = 18,
        StaleUpgradeChoice = 19,
        MatchEnded = 20
    }

    public readonly struct AuthoritativeEconomyCommand
    {
        public AuthoritativeEconomyCommand(
            int playerId,
            uint sequence,
            ulong nonce,
            AuthoritativeEconomyCommandKind kind,
            int entityId = 0,
            int sourceSlot = -1,
            int destinationSlot = -1,
            int quantity = 0,
            int candidateIndex = -1,
            int expectedInventoryRevision = 0,
            int expectedDropRevision = 0,
            int choiceGeneration = 0,
            string expectedItemId = "")
        {
            PlayerId = playerId;
            Sequence = sequence;
            Nonce = nonce;
            Kind = kind;
            EntityId = entityId;
            SourceSlot = sourceSlot;
            DestinationSlot = destinationSlot;
            Quantity = quantity;
            CandidateIndex = candidateIndex;
            ExpectedInventoryRevision = Math.Max(0,
                expectedInventoryRevision);
            ExpectedDropRevision = Math.Max(0, expectedDropRevision);
            ChoiceGeneration = Math.Max(0, choiceGeneration);
            ExpectedItemId = expectedItemId?.Trim() ?? string.Empty;
        }

        public int PlayerId { get; }
        public uint Sequence { get; }
        public ulong Nonce { get; }
        public AuthoritativeEconomyCommandKind Kind { get; }
        public int EntityId { get; }
        public int SourceSlot { get; }
        public int DestinationSlot { get; }
        public int Quantity { get; }
        public int CandidateIndex { get; }
        public int ExpectedInventoryRevision { get; }
        public int ExpectedDropRevision { get; }
        public int ChoiceGeneration { get; }
        public string ExpectedItemId { get; }
    }

    public readonly struct AuthoritativeEconomyResolution
    {
        public AuthoritativeEconomyResolution(
            AuthoritativeEconomyCommand command,
            bool accepted,
            AuthoritativeEconomyRejection rejection,
            string definitionId = "",
            int affectedQuantity = 0)
        {
            Command = command;
            Accepted = accepted;
            Rejection = rejection;
            DefinitionId = definitionId ?? string.Empty;
            AffectedQuantity = Math.Max(0, affectedQuantity);
        }

        public AuthoritativeEconomyCommand Command { get; }
        public bool Accepted { get; }
        public AuthoritativeEconomyRejection Rejection { get; }
        public string DefinitionId { get; }
        public int AffectedQuantity { get; }
    }

    public readonly struct AuthoritativeInventorySlotState
    {
        public AuthoritativeInventorySlotState(
            int playerId,
            int slotIndex,
            string itemId,
            int quantity,
            int maximumStack)
        {
            PlayerId = playerId;
            SlotIndex = slotIndex;
            ItemId = itemId ?? string.Empty;
            Quantity = Math.Max(0, quantity);
            MaximumStack = Math.Max(0, maximumStack);
        }

        public int PlayerId { get; }
        public int SlotIndex { get; }
        public string ItemId { get; }
        public int Quantity { get; }
        public int MaximumStack { get; }
        public bool IsEmpty => Quantity <= 0 || string.IsNullOrEmpty(ItemId);
    }

    public readonly struct AuthoritativeWorldDropState
    {
        public AuthoritativeWorldDropState(
            int dropId,
            string itemId,
            int quantity,
            NetVector3 position,
            int ownerPlayerId,
            bool available,
            int revision)
        {
            DropId = dropId;
            ItemId = itemId ?? string.Empty;
            Quantity = Math.Max(0, quantity);
            Position = position;
            OwnerPlayerId = Math.Max(0, ownerPlayerId);
            Available = available && Quantity > 0;
            Revision = Math.Max(1, revision);
        }

        public int DropId { get; }
        public string ItemId { get; }
        public int Quantity { get; }
        public NetVector3 Position { get; }
        public int OwnerPlayerId { get; }
        public bool Available { get; }
        public int Revision { get; }
    }

    public readonly struct AuthoritativeUpgradeStackState
    {
        public AuthoritativeUpgradeStackState(
            int playerId,
            string upgradeId,
            int level,
            string buildTag)
        {
            PlayerId = playerId;
            UpgradeId = upgradeId ?? string.Empty;
            Level = Math.Max(0, level);
            BuildTag = buildTag ?? string.Empty;
        }

        public int PlayerId { get; }
        public string UpgradeId { get; }
        public int Level { get; }
        public string BuildTag { get; }
    }

    public readonly struct AuthoritativeProgressionState
    {
        public AuthoritativeProgressionState(
            int playerId,
            int level,
            int currentExperience,
            int experienceToNextLevel,
            int totalExperience,
            int pendingUpgradeChoices,
            IReadOnlyList<string> candidateIds,
            IReadOnlyList<string> buildTags,
            uint acknowledgedEconomySequence,
            int inventoryRevision = 1,
            int choiceGeneration = 0,
            long nextConsumableUseTick = 0)
        {
            PlayerId = playerId;
            Level = Math.Max(1, level);
            CurrentExperience = Math.Max(0, currentExperience);
            ExperienceToNextLevel = Math.Max(0, experienceToNextLevel);
            TotalExperience = Math.Max(0, totalExperience);
            PendingUpgradeChoices = Math.Max(0, pendingUpgradeChoices);
            CandidateIds = candidateIds ?? Array.Empty<string>();
            BuildTags = buildTags ?? Array.Empty<string>();
            AcknowledgedEconomySequence = acknowledgedEconomySequence;
            InventoryRevision = Math.Max(1, inventoryRevision);
            ChoiceGeneration = Math.Max(0, choiceGeneration);
            NextConsumableUseTick = Math.Max(0L, nextConsumableUseTick);
        }

        public int PlayerId { get; }
        public int Level { get; }
        public int CurrentExperience { get; }
        public int ExperienceToNextLevel { get; }
        public int TotalExperience { get; }
        public int PendingUpgradeChoices { get; }
        public IReadOnlyList<string> CandidateIds { get; }
        public IReadOnlyList<string> BuildTags { get; }
        public uint AcknowledgedEconomySequence { get; }
        public int InventoryRevision { get; }
        public int ChoiceGeneration { get; }
        public long NextConsumableUseTick { get; }
        public bool IsMaxLevel => ExperienceToNextLevel <= 0;
    }

    public sealed class AuthoritativeEconomySnapshot
    {
        private readonly AuthoritativeInventorySlotState[] inventorySlots;
        private readonly AuthoritativeWorldDropState[] worldDrops;
        private readonly AuthoritativeProgressionState[] progression;
        private readonly AuthoritativeUpgradeStackState[] upgrades;

        public AuthoritativeEconomySnapshot(
            IEnumerable<AuthoritativeInventorySlotState> inventorySlots,
            IEnumerable<AuthoritativeWorldDropState> worldDrops,
            IEnumerable<AuthoritativeProgressionState> progression,
            IEnumerable<AuthoritativeUpgradeStackState> upgrades)
        {
            this.inventorySlots = (inventorySlots ??
                Array.Empty<AuthoritativeInventorySlotState>())
                .OrderBy(value => value.PlayerId)
                .ThenBy(value => value.SlotIndex).ToArray();
            this.worldDrops = (worldDrops ??
                Array.Empty<AuthoritativeWorldDropState>())
                .OrderBy(value => value.DropId).ToArray();
            this.progression = (progression ??
                Array.Empty<AuthoritativeProgressionState>())
                .OrderBy(value => value.PlayerId).ToArray();
            this.upgrades = (upgrades ??
                Array.Empty<AuthoritativeUpgradeStackState>())
                .OrderBy(value => value.PlayerId)
                .ThenBy(value => value.UpgradeId,
                    StringComparer.Ordinal).ToArray();
        }

        public IReadOnlyList<AuthoritativeInventorySlotState> InventorySlots =>
            inventorySlots;
        public IReadOnlyList<AuthoritativeWorldDropState> WorldDrops =>
            worldDrops;
        public IReadOnlyList<AuthoritativeProgressionState> Progression =>
            progression;
        public IReadOnlyList<AuthoritativeUpgradeStackState> Upgrades =>
            upgrades;
        public AuthoritativeProgressionState Player(int playerId) =>
            progression.Single(value => value.PlayerId == playerId);
        public AuthoritativeWorldDropState Drop(int dropId) =>
            worldDrops.Single(value => value.DropId == dropId);
    }

    internal sealed class AuthoritativeCoopEconomy
    {
        private static readonly int[] ExperienceThresholds =
        {
            100, 150, 225, 325, 450, 600, 800, 1050, 1350
        };

        private readonly Dictionary<string, AuthoritativeItemDefinition> items;
        private readonly Dictionary<string, AuthoritativeUpgradeDefinition>
            upgrades;
        private readonly Dictionary<int, MutableEconomyPlayer> players;
        private readonly Dictionary<int, MutableWorldDrop> drops = new();
        private readonly int runSeed;
        private int nextDropId = 1;

        public AuthoritativeCoopEconomy(
            IEnumerable<int> playerIds,
            IEnumerable<AuthoritativeItemDefinition> itemDefinitions,
            IEnumerable<AuthoritativeUpgradeDefinition> upgradeDefinitions,
            int runSeed,
            int inventoryCapacity)
        {
            items = (itemDefinitions ??
                    AuthoritativeItemDefinition.CreateProjectDefaults())
                .ToDictionary(value => value.StableId, StringComparer.Ordinal);
            upgrades = (upgradeDefinitions ??
                    AuthoritativeUpgradeDefinition.CreateProjectDefaults())
                .ToDictionary(value => value.StableId, StringComparer.Ordinal);
            this.runSeed = runSeed;
            players = playerIds.ToDictionary(value => value,
                value => new MutableEconomyPlayer(value,
                    Math.Max(1, inventoryCapacity)));
        }

        public int SpawnDrop(
            string itemId,
            int quantity,
            NetVector3 position,
            int ownerPlayerId = 0)
        {
            if (!items.ContainsKey(itemId ?? string.Empty) || quantity <= 0 ||
                !position.IsFinite ||
                ownerPlayerId != 0 && !players.ContainsKey(ownerPlayerId))
                return 0;
            int id = nextDropId++;
            drops.Add(id, new MutableWorldDrop(id, itemId, quantity,
                position, ownerPlayerId));
            return id;
        }

        public int GrantExperience(int playerId, int amount)
        {
            if (!players.TryGetValue(playerId, out MutableEconomyPlayer player) ||
                amount <= 0)
                return 0;
            int gained = player.GrantExperience(amount,
                ExperienceThresholds);
            if (gained > 0 && player.CandidateIds.Count == 0)
                GenerateCandidates(player);
            return gained;
        }

        public double Modifier(
            int playerId,
            AuthoritativeUpgradeEffect effect)
        {
            if (!players.TryGetValue(playerId, out MutableEconomyPlayer player))
                return 1d;
            double bonus = 0d;
            foreach (KeyValuePair<string, int> pair in player.UpgradeLevels)
            {
                if (upgrades.TryGetValue(pair.Key, out var definition) &&
                    definition.Effect == effect)
                    bonus += definition.EffectAmount * pair.Value;
            }
            return 1d + bonus;
        }

        public AuthoritativeEconomyResolution Apply(
            AuthoritativeEconomyCommand command,
            long currentTick,
            Func<int, NetVector3> playerPosition,
            Func<int, AuthoritativeItemDefinition, bool> applyItem,
            Func<int, AuthoritativeUpgradeDefinition, bool> applyUpgrade)
        {
            if (!players.TryGetValue(command.PlayerId,
                    out MutableEconomyPlayer player))
                return Reject(command,
                    AuthoritativeEconomyRejection.UnknownPlayer);
            AuthoritativeEconomyRejection identity =
                player.ValidateAndReserve(command);
            if (identity != AuthoritativeEconomyRejection.None)
                return Reject(command, identity);

            return command.Kind switch
            {
                AuthoritativeEconomyCommandKind.Pickup =>
                    Pickup(command, player, playerPosition(command.PlayerId)),
                AuthoritativeEconomyCommandKind.Transfer =>
                    Transfer(command, player),
                AuthoritativeEconomyCommandKind.Split =>
                    Split(command, player),
                AuthoritativeEconomyCommandKind.Compact =>
                    Compact(command, player),
                AuthoritativeEconomyCommandKind.Drop =>
                    Drop(command, player, playerPosition(command.PlayerId)),
                AuthoritativeEconomyCommandKind.Use =>
                    Use(command, player, currentTick, applyItem),
                AuthoritativeEconomyCommandKind.SelectUpgrade =>
                    SelectUpgrade(command, player, applyUpgrade),
                _ => Reject(command,
                    AuthoritativeEconomyRejection.InvalidOperation)
            };
        }

        public AuthoritativeEconomySnapshot Snapshot()
        {
            var slots = new List<AuthoritativeInventorySlotState>();
            var progress = new List<AuthoritativeProgressionState>();
            var stacks = new List<AuthoritativeUpgradeStackState>();
            foreach (MutableEconomyPlayer player in players.Values
                         .OrderBy(value => value.PlayerId))
            {
                for (int index = 0; index < player.Slots.Length; index++)
                {
                    MutableSlot slot = player.Slots[index];
                    slots.Add(new AuthoritativeInventorySlotState(
                        player.PlayerId, index, slot.ItemId,
                        slot.Quantity, slot.MaximumStack));
                }
                string[] tags = player.UpgradeLevels.Keys
                    .Where(upgrades.ContainsKey)
                    .Select(value => upgrades[value].BuildTag)
                    .Where(value => !string.IsNullOrEmpty(value))
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(value => value, StringComparer.Ordinal)
                    .ToArray();
                progress.Add(player.Snapshot(ExperienceThresholds, tags));
                foreach (KeyValuePair<string, int> pair in
                         player.UpgradeLevels.OrderBy(value => value.Key,
                             StringComparer.Ordinal))
                {
                    stacks.Add(new AuthoritativeUpgradeStackState(
                        player.PlayerId, pair.Key, pair.Value,
                        upgrades[pair.Key].BuildTag));
                }
            }
            return new AuthoritativeEconomySnapshot(
                slots,
                drops.Values.Select(value => value.Snapshot()),
                progress,
                stacks);
        }

        private AuthoritativeEconomyResolution Pickup(
            AuthoritativeEconomyCommand command,
            MutableEconomyPlayer player,
            NetVector3 position)
        {
            if (!drops.TryGetValue(command.EntityId, out MutableWorldDrop drop))
                return Reject(command,
                    AuthoritativeEconomyRejection.UnknownDrop);
            if (!drop.Available)
                return Reject(command,
                    AuthoritativeEconomyRejection.DropUnavailable);
            if (drop.OwnerPlayerId != 0 &&
                drop.OwnerPlayerId != command.PlayerId)
                return Reject(command,
                    AuthoritativeEconomyRejection.DropOwnership);
            if (NetVector3.Distance(position, drop.Position) > 3.25d)
                return Reject(command,
                    AuthoritativeEconomyRejection.OutOfRange);
            if (command.ExpectedDropRevision > 0 &&
                command.ExpectedDropRevision != drop.Revision)
                return Reject(command,
                    AuthoritativeEconomyRejection.StaleDrop);
            if (!string.IsNullOrEmpty(command.ExpectedItemId) &&
                !string.Equals(command.ExpectedItemId, drop.ItemId,
                    StringComparison.Ordinal))
                return Reject(command,
                    AuthoritativeEconomyRejection.StaleDrop);
            if (StaleInventory(command, player))
                return Reject(command,
                    AuthoritativeEconomyRejection.StaleInventory);
            AuthoritativeItemDefinition item = items[drop.ItemId];
            if (!player.CanAdd(item, drop.Quantity))
                return Reject(command,
                    AuthoritativeEconomyRejection.InventoryFull);
            int accepted = player.Add(item, drop.Quantity);
            if (accepted <= 0)
                return Reject(command,
                    AuthoritativeEconomyRejection.InventoryFull);
            drop.Quantity -= accepted;
            drop.Revision++;
            if (drop.Quantity <= 0) drop.Available = false;
            player.InventoryRevision++;
            return Accept(command, drop.ItemId, accepted);
        }

        private static AuthoritativeEconomyResolution Transfer(
            AuthoritativeEconomyCommand command,
            MutableEconomyPlayer player)
        {
            if (StaleInventory(command, player))
                return Reject(command,
                    AuthoritativeEconomyRejection.StaleInventory);
            if (!player.ValidSlot(command.SourceSlot) ||
                !player.ValidSlot(command.DestinationSlot))
                return Reject(command,
                    AuthoritativeEconomyRejection.InvalidSlot);
            if (!player.Transfer(command.SourceSlot,
                    command.DestinationSlot, out int moved))
                return Reject(command,
                    AuthoritativeEconomyRejection.InvalidOperation);
            player.InventoryRevision++;
            return Accept(command, string.Empty, moved);
        }

        private static AuthoritativeEconomyResolution Split(
            AuthoritativeEconomyCommand command,
            MutableEconomyPlayer player)
        {
            if (StaleInventory(command, player))
                return Reject(command,
                    AuthoritativeEconomyRejection.StaleInventory);
            if (!player.ValidSlot(command.SourceSlot) ||
                !player.ValidSlot(command.DestinationSlot))
                return Reject(command,
                    AuthoritativeEconomyRejection.InvalidSlot);
            if (command.Quantity <= 0)
                return Reject(command,
                    AuthoritativeEconomyRejection.InvalidQuantity);
            if (!player.Split(command.SourceSlot,
                    command.DestinationSlot, command.Quantity))
                return Reject(command,
                    AuthoritativeEconomyRejection.InvalidOperation);
            player.InventoryRevision++;
            return Accept(command, string.Empty, command.Quantity);
        }

        private static AuthoritativeEconomyResolution Compact(
            AuthoritativeEconomyCommand command,
            MutableEconomyPlayer player)
        {
            if (StaleInventory(command, player))
                return Reject(command,
                    AuthoritativeEconomyRejection.StaleInventory);
            if (!player.Compact())
                return Reject(command,
                    AuthoritativeEconomyRejection.InvalidOperation);
            player.InventoryRevision++;
            return Accept(command);
        }

        private AuthoritativeEconomyResolution Drop(
            AuthoritativeEconomyCommand command,
            MutableEconomyPlayer player,
            NetVector3 position)
        {
            if (StaleInventory(command, player))
                return Reject(command,
                    AuthoritativeEconomyRejection.StaleInventory);
            if (!player.ValidSlot(command.SourceSlot))
                return Reject(command,
                    AuthoritativeEconomyRejection.InvalidSlot);
            if (command.Quantity <= 0)
                return Reject(command,
                    AuthoritativeEconomyRejection.InvalidQuantity);
            MutableSlot slot = player.Slots[command.SourceSlot];
            if (slot.IsEmpty || slot.Quantity < command.Quantity)
                return Reject(command,
                    AuthoritativeEconomyRejection.ItemUnavailable);
            string itemId = slot.ItemId;
            if (!player.RemoveAt(command.SourceSlot, command.Quantity))
                return Reject(command,
                    AuthoritativeEconomyRejection.InvalidOperation);
            int dropId = SpawnDrop(itemId, command.Quantity,
                position + new NetVector3(0d, 0d, 1.25d));
            if (dropId == 0)
            {
                player.Add(items[itemId], command.Quantity);
                return Reject(command,
                    AuthoritativeEconomyRejection.InvalidOperation);
            }
            player.InventoryRevision++;
            return Accept(command, itemId, command.Quantity);
        }

        private AuthoritativeEconomyResolution Use(
            AuthoritativeEconomyCommand command,
            MutableEconomyPlayer player,
            long currentTick,
            Func<int, AuthoritativeItemDefinition, bool> applyItem)
        {
            if (StaleInventory(command, player))
                return Reject(command,
                    AuthoritativeEconomyRejection.StaleInventory);
            if (currentTick < player.NextConsumableUseTick)
                return Reject(command,
                    AuthoritativeEconomyRejection.CooldownActive);
            if (!player.ValidSlot(command.SourceSlot))
                return Reject(command,
                    AuthoritativeEconomyRejection.InvalidSlot);
            MutableSlot slot = player.Slots[command.SourceSlot];
            if (slot.IsEmpty || !items.TryGetValue(slot.ItemId,
                    out AuthoritativeItemDefinition item))
                return Reject(command,
                    AuthoritativeEconomyRejection.ItemUnavailable);
            if (applyItem == null || !applyItem(command.PlayerId, item))
                return Reject(command,
                    AuthoritativeEconomyRejection.EffectUnavailable);
            player.RemoveAt(command.SourceSlot, 1);
            player.InventoryRevision++;
            player.NextConsumableUseTick = currentTick + 15L;
            return Accept(command, item.StableId, 1);
        }

        private AuthoritativeEconomyResolution SelectUpgrade(
            AuthoritativeEconomyCommand command,
            MutableEconomyPlayer player,
            Func<int, AuthoritativeUpgradeDefinition, bool> applyUpgrade)
        {
            if (player.PendingUpgradeChoices <= 0 ||
                player.CandidateIds.Count == 0)
                return Reject(command,
                    AuthoritativeEconomyRejection.NoPendingUpgrade);
            if (command.ChoiceGeneration > 0 &&
                command.ChoiceGeneration != player.ChoiceGeneration)
                return Reject(command,
                    AuthoritativeEconomyRejection.StaleUpgradeChoice);
            if (command.CandidateIndex < 0 ||
                command.CandidateIndex >= player.CandidateIds.Count)
                return Reject(command,
                    AuthoritativeEconomyRejection.InvalidCandidate);
            string id = player.CandidateIds[command.CandidateIndex];
            AuthoritativeUpgradeDefinition definition = upgrades[id];
            if (player.UpgradeLevel(id) >= definition.MaximumLevel ||
                applyUpgrade == null ||
                !applyUpgrade(command.PlayerId, definition) ||
                !player.ApplyUpgrade(definition))
                return Reject(command,
                    AuthoritativeEconomyRejection.EffectUnavailable);
            player.PendingUpgradeChoices--;
            player.CandidateIds.Clear();
            if (player.PendingUpgradeChoices > 0)
                GenerateCandidates(player);
            return Accept(command, id, 1);
        }

        private void GenerateCandidates(MutableEconomyPlayer player)
        {
            var eligible = upgrades.Values.Where(value =>
                    player.UpgradeLevel(value.StableId) < value.MaximumLevel)
                .OrderBy(value => value.StableId, StringComparer.Ordinal)
                .ToList();
            ulong random = SeedFor(player);
            for (int index = eligible.Count - 1; index > 0; index--)
            {
                random = NextRandom(random);
                int swap = (int)(random % (ulong)(index + 1));
                (eligible[index], eligible[swap]) =
                    (eligible[swap], eligible[index]);
            }
            player.CandidateIds.Clear();
            player.ChoiceGeneration++;
            for (int index = 0; index < Math.Min(3, eligible.Count); index++)
                player.CandidateIds.Add(eligible[index].StableId);
            if (player.CandidateIds.Count == 0)
                player.PendingUpgradeChoices = 0;
        }

        private ulong SeedFor(MutableEconomyPlayer player)
        {
            ulong value = 1469598103934665603UL;
            value = Hash(value, (uint)runSeed);
            value = Hash(value, (uint)player.PlayerId);
            value = Hash(value, (uint)player.SelectionHistory.Count);
            foreach (string id in player.SelectionHistory)
            {
                for (int index = 0; index < id.Length; index++)
                    value = Hash(value, id[index]);
            }
            return value == 0UL ? 1UL : value;
        }

        private static ulong Hash(ulong value, uint data) =>
            (value ^ data) * 1099511628211UL;

        private static ulong NextRandom(ulong value)
        {
            value ^= value << 13;
            value ^= value >> 7;
            value ^= value << 17;
            return value;
        }

        private static AuthoritativeEconomyResolution Accept(
            AuthoritativeEconomyCommand command,
            string id = "",
            int quantity = 0) => new(command, true,
                AuthoritativeEconomyRejection.None, id, quantity);

        private static AuthoritativeEconomyResolution Reject(
            AuthoritativeEconomyCommand command,
            AuthoritativeEconomyRejection rejection) => new(
                command, false, rejection);

        private static bool StaleInventory(
            AuthoritativeEconomyCommand command,
            MutableEconomyPlayer player) =>
            command.ExpectedInventoryRevision > 0 &&
            command.ExpectedInventoryRevision != player.InventoryRevision;

        private sealed class MutableWorldDrop
        {
            public MutableWorldDrop(int id, string itemId, int quantity,
                NetVector3 position, int ownerPlayerId)
            {
                Id = id;
                ItemId = itemId;
                Quantity = quantity;
                Position = position;
                OwnerPlayerId = ownerPlayerId;
            }

            public int Id;
            public string ItemId;
            public int Quantity;
            public NetVector3 Position;
            public int OwnerPlayerId;
            public bool Available = true;
            public int Revision = 1;
            public AuthoritativeWorldDropState Snapshot() => new(
                Id, ItemId, Quantity, Position, OwnerPlayerId,
                Available, Revision);
        }

        private sealed class MutableEconomyPlayer
        {
            private readonly HashSet<ulong> nonces = new();
            private readonly Queue<ulong> nonceOrder = new();

            public MutableEconomyPlayer(int playerId, int capacity)
            {
                PlayerId = playerId;
                Slots = new MutableSlot[capacity];
            }

            public int PlayerId;
            public MutableSlot[] Slots;
            public uint LastSequence;
            public bool HasSequence;
            public int Level = 1;
            public int CurrentExperience;
            public int TotalExperience;
            public int PendingUpgradeChoices;
            public int InventoryRevision = 1;
            public int ChoiceGeneration;
            public long NextConsumableUseTick;
            public readonly List<string> CandidateIds = new();
            public readonly Dictionary<string, int> UpgradeLevels = new(
                StringComparer.Ordinal);
            public readonly List<string> SelectionHistory = new();

            public AuthoritativeEconomyRejection ValidateAndReserve(
                AuthoritativeEconomyCommand command)
            {
                if (command.Sequence == 0 ||
                    HasSequence && command.Sequence <= LastSequence)
                    return AuthoritativeEconomyRejection.InvalidSequence;
                if (command.Nonce == 0 || nonces.Contains(command.Nonce))
                    return AuthoritativeEconomyRejection.DuplicateNonce;
                HasSequence = true;
                LastSequence = command.Sequence;
                nonces.Add(command.Nonce);
                nonceOrder.Enqueue(command.Nonce);
                while (nonceOrder.Count > 64)
                    nonces.Remove(nonceOrder.Dequeue());
                return AuthoritativeEconomyRejection.None;
            }

            public bool ValidSlot(int index) =>
                index >= 0 && index < Slots.Length;

            public int Add(AuthoritativeItemDefinition item, int quantity)
            {
                int remaining = Math.Max(0, quantity);
                for (int index = 0; index < Slots.Length && remaining > 0;
                     index++)
                {
                    MutableSlot slot = Slots[index];
                    if (slot.IsEmpty || !string.Equals(slot.ItemId,
                            item.StableId, StringComparison.Ordinal))
                        continue;
                    int amount = Math.Min(remaining,
                        item.MaximumStack - slot.Quantity);
                    slot.Quantity += amount;
                    Slots[index] = slot;
                    remaining -= amount;
                }
                for (int index = 0; index < Slots.Length && remaining > 0;
                     index++)
                {
                    if (!Slots[index].IsEmpty) continue;
                    int amount = Math.Min(remaining, item.MaximumStack);
                    Slots[index] = new MutableSlot(item.StableId,
                        amount, item.MaximumStack);
                    remaining -= amount;
                }
                return quantity - remaining;
            }

            public bool CanAdd(AuthoritativeItemDefinition item, int quantity)
            {
                int remaining = Math.Max(0, quantity);
                for (int index = 0; index < Slots.Length && remaining > 0;
                     index++)
                {
                    MutableSlot slot = Slots[index];
                    if (slot.IsEmpty)
                    {
                        remaining -= item.MaximumStack;
                        continue;
                    }
                    if (string.Equals(slot.ItemId, item.StableId,
                            StringComparison.Ordinal))
                        remaining -= Math.Max(0,
                            item.MaximumStack - slot.Quantity);
                }
                return remaining <= 0;
            }

            public bool RemoveAt(int index, int quantity)
            {
                if (!ValidSlot(index) || quantity <= 0 ||
                    Slots[index].IsEmpty || Slots[index].Quantity < quantity)
                    return false;
                MutableSlot slot = Slots[index];
                slot.Quantity -= quantity;
                Slots[index] = slot.Quantity > 0 ? slot : default;
                return true;
            }

            public bool Transfer(int source, int destination, out int moved)
            {
                moved = 0;
                if (!ValidSlot(source) || !ValidSlot(destination) ||
                    source == destination || Slots[source].IsEmpty)
                    return false;
                MutableSlot left = Slots[source];
                MutableSlot right = Slots[destination];
                if (right.IsEmpty)
                {
                    Slots[destination] = left;
                    Slots[source] = default;
                    moved = left.Quantity;
                    return true;
                }
                if (!string.Equals(left.ItemId, right.ItemId,
                        StringComparison.Ordinal))
                {
                    Slots[source] = right;
                    Slots[destination] = left;
                    return true;
                }
                int amount = Math.Min(left.Quantity,
                    right.MaximumStack - right.Quantity);
                if (amount <= 0) return false;
                right.Quantity += amount;
                left.Quantity -= amount;
                Slots[destination] = right;
                Slots[source] = left.Quantity > 0 ? left : default;
                moved = amount;
                return true;
            }

            public bool Split(int source, int destination, int quantity)
            {
                if (!ValidSlot(source) || !ValidSlot(destination) ||
                    source == destination || quantity <= 0 ||
                    Slots[source].IsEmpty || !Slots[destination].IsEmpty ||
                    quantity >= Slots[source].Quantity)
                    return false;
                MutableSlot slot = Slots[source];
                Slots[destination] = new MutableSlot(
                    slot.ItemId, quantity, slot.MaximumStack);
                slot.Quantity -= quantity;
                Slots[source] = slot;
                return true;
            }

            public bool Compact()
            {
                MutableSlot[] before = (MutableSlot[])Slots.Clone();
                var order = new List<string>();
                var totals = new Dictionary<string, int>(
                    StringComparer.Ordinal);
                var maximums = new Dictionary<string, int>(
                    StringComparer.Ordinal);
                foreach (MutableSlot slot in Slots)
                {
                    if (slot.IsEmpty) continue;
                    if (!totals.ContainsKey(slot.ItemId))
                    {
                        order.Add(slot.ItemId);
                        totals[slot.ItemId] = 0;
                        maximums[slot.ItemId] = slot.MaximumStack;
                    }
                    totals[slot.ItemId] += slot.Quantity;
                }
                Array.Clear(Slots, 0, Slots.Length);
                int destination = 0;
                foreach (string id in order)
                {
                    int remaining = totals[id];
                    while (remaining > 0 && destination < Slots.Length)
                    {
                        int amount = Math.Min(remaining, maximums[id]);
                        Slots[destination++] = new MutableSlot(
                            id, amount, maximums[id]);
                        remaining -= amount;
                    }
                }
                return !before.SequenceEqual(Slots);
            }

            public int GrantExperience(int amount, IReadOnlyList<int> thresholds)
            {
                TotalExperience += amount;
                CurrentExperience += amount;
                int gained = 0;
                while (Level <= thresholds.Count &&
                       CurrentExperience >= thresholds[Level - 1])
                {
                    CurrentExperience -= thresholds[Level - 1];
                    Level++;
                    PendingUpgradeChoices++;
                    gained++;
                }
                return gained;
            }

            public int UpgradeLevel(string id) =>
                UpgradeLevels.TryGetValue(id, out int level) ? level : 0;

            public bool ApplyUpgrade(AuthoritativeUpgradeDefinition definition)
            {
                int next = UpgradeLevel(definition.StableId) + 1;
                if (next > definition.MaximumLevel) return false;
                UpgradeLevels[definition.StableId] = next;
                SelectionHistory.Add(definition.StableId);
                return true;
            }

            public AuthoritativeProgressionState Snapshot(
                IReadOnlyList<int> thresholds,
                IReadOnlyList<string> tags)
            {
                int next = Level <= thresholds.Count
                    ? thresholds[Level - 1]
                    : 0;
                return new AuthoritativeProgressionState(
                    PlayerId, Level, CurrentExperience, next,
                    TotalExperience, PendingUpgradeChoices,
                    CandidateIds.ToArray(), tags.ToArray(), LastSequence,
                    InventoryRevision, ChoiceGeneration,
                    NextConsumableUseTick);
            }
        }

        private struct MutableSlot : IEquatable<MutableSlot>
        {
            public MutableSlot(string itemId, int quantity, int maximumStack)
            {
                ItemId = itemId;
                Quantity = quantity;
                MaximumStack = maximumStack;
            }

            public string ItemId;
            public int Quantity;
            public int MaximumStack;
            public bool IsEmpty => Quantity <= 0 || string.IsNullOrEmpty(ItemId);
            public bool Equals(MutableSlot other) =>
                string.Equals(ItemId, other.ItemId,
                    StringComparison.Ordinal) &&
                Quantity == other.Quantity &&
                MaximumStack == other.MaximumStack;
        }
    }
}
