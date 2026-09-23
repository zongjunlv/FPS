using System;
using System.Collections.Generic;
using System.Linq;

namespace FPS.Networking.Domain
{
    public readonly struct AuthoritativeWaveDefinition
    {
        public AuthoritativeWaveDefinition(
            int waveIndex,
            int maximumAlive,
            int spawnIntervalTicks,
            int intermissionTicks,
            IReadOnlyList<AuthoritativeLootStack> waveRewards = null,
            IReadOnlyList<AuthoritativeLootStack> finalRewards = null)
        {
            if (waveIndex < 1)
                throw new ArgumentOutOfRangeException(nameof(waveIndex));
            if (maximumAlive < 1)
                throw new ArgumentOutOfRangeException(nameof(maximumAlive));
            if (spawnIntervalTicks < 0)
                throw new ArgumentOutOfRangeException(nameof(spawnIntervalTicks));
            if (intermissionTicks < 0)
                throw new ArgumentOutOfRangeException(nameof(intermissionTicks));

            WaveIndex = waveIndex;
            MaximumAlive = maximumAlive;
            SpawnIntervalTicks = spawnIntervalTicks;
            IntermissionTicks = intermissionTicks;
            WaveRewards = waveRewards?.ToArray() ??
                Array.Empty<AuthoritativeLootStack>();
            FinalRewards = finalRewards?.ToArray() ??
                Array.Empty<AuthoritativeLootStack>();
        }

        public int WaveIndex { get; }
        public int MaximumAlive { get; }
        public int SpawnIntervalTicks { get; }
        public int IntermissionTicks { get; }
        public IReadOnlyList<AuthoritativeLootStack> WaveRewards { get; }
        public IReadOnlyList<AuthoritativeLootStack> FinalRewards { get; }
    }
}
