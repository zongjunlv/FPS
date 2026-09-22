using System;

namespace FPS.Networking.Domain
{
    public readonly struct AuthoritativeWaveDefinition
    {
        public AuthoritativeWaveDefinition(
            int waveIndex,
            int maximumAlive,
            int spawnIntervalTicks,
            int intermissionTicks)
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
        }

        public int WaveIndex { get; }
        public int MaximumAlive { get; }
        public int SpawnIntervalTicks { get; }
        public int IntermissionTicks { get; }
    }
}
