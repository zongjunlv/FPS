using System;
using System.Collections.Generic;

namespace FPS.Networking.Domain
{
    /// <summary>
    /// Short FIFO input queue with a per-player bound and a fair per-tick drain.
    /// Fire/jump commands are never silently coalesced into a newer movement.
    /// </summary>
    public sealed class BoundedInputCommandBuffer
    {
        private readonly List<PlayerInputCommand> commands = new();

        public BoundedInputCommandBuffer(int capacityPerPlayer = 32,
            int maximumPerPlayerPerTick = 4)
        {
            if (capacityPerPlayer < 1)
                throw new ArgumentOutOfRangeException(nameof(capacityPerPlayer));
            if (maximumPerPlayerPerTick < 1 ||
                maximumPerPlayerPerTick > capacityPerPlayer)
                throw new ArgumentOutOfRangeException(
                    nameof(maximumPerPlayerPerTick));
            CapacityPerPlayer = capacityPerPlayer;
            MaximumPerPlayerPerTick = maximumPerPlayerPerTick;
        }

        public int CapacityPerPlayer { get; }
        public int MaximumPerPlayerPerTick { get; }
        public int Count => commands.Count;

        public int CountForPlayer(int playerId)
        {
            int count = 0;
            foreach (PlayerInputCommand command in commands)
                if (command.PlayerId == playerId) count++;
            return count;
        }

        public bool TryEnqueue(PlayerInputCommand command)
        {
            if (CountForPlayer(command.PlayerId) >= CapacityPerPlayer)
                return false;
            commands.Add(command);
            return true;
        }

        public PlayerInputCommand[] DrainTick(long maximumClientTick = long.MaxValue)
        {
            var drained = new List<PlayerInputCommand>();
            var counts = new Dictionary<int, int>();
            var waitingPlayers = new HashSet<int>();
            for (int index = 0; index < commands.Count;)
            {
                PlayerInputCommand command = commands[index];
                if (waitingPlayers.Contains(command.PlayerId) ||
                    command.ClientTick > maximumClientTick)
                {
                    // An early input may wait, but a later sequence from that
                    // same player must not overtake it. Other players continue.
                    waitingPlayers.Add(command.PlayerId);
                    index++;
                    continue;
                }
                counts.TryGetValue(command.PlayerId, out int count);
                if (count >= MaximumPerPlayerPerTick)
                {
                    index++;
                    continue;
                }
                counts[command.PlayerId] = count + 1;
                drained.Add(command);
                commands.RemoveAt(index);
            }
            return drained.ToArray();
        }

        public void RemovePlayer(int playerId) =>
            commands.RemoveAll(command => command.PlayerId == playerId);

        public void Clear() => commands.Clear();
    }
}
