using System;
using System.Collections.Generic;

namespace FPS.Networking.Session
{
    public enum CoopSceneLoadState
    {
        Idle = 0,
        Loading = 1,
        Ready = 2,
        Cancelled = 3,
        TimedOut = 4
    }

    public enum CoopSceneReadyResult
    {
        Accepted = 0,
        Duplicate = 1,
        WrongEpoch = 2,
        UnexpectedPlayer = 3,
        NotLoading = 4,
        Completed = 5
    }

    public sealed class CoopSceneLoadBarrier
    {
        private readonly HashSet<string> expected = new(StringComparer.Ordinal);
        private readonly HashSet<string> ready = new(StringComparer.Ordinal);
        private double deadline;

        public CoopSceneLoadState State { get; private set; } =
            CoopSceneLoadState.Idle;
        public string Epoch { get; private set; } = string.Empty;
        public string ScenePath { get; private set; } = string.Empty;
        public int Seed { get; private set; }
        public int ExpectedCount => expected.Count;
        public int ReadyCount => ready.Count;

        public void Begin(string epoch, string scenePath, int seed,
            IEnumerable<string> expectedPlayers, double nowSeconds,
            double timeoutSeconds)
        {
            if (string.IsNullOrWhiteSpace(epoch))
                throw new ArgumentException("Load epoch is required.",
                    nameof(epoch));
            if (string.IsNullOrWhiteSpace(scenePath))
                throw new ArgumentException("Scene path is required.",
                    nameof(scenePath));
            if (timeoutSeconds <= 0d)
                throw new ArgumentOutOfRangeException(nameof(timeoutSeconds));
            expected.Clear();
            ready.Clear();
            foreach (string player in expectedPlayers ?? Array.Empty<string>())
            {
                if (!string.IsNullOrWhiteSpace(player)) expected.Add(player.Trim());
            }
            if (expected.Count == 0)
                throw new ArgumentException("Expected players are required.",
                    nameof(expectedPlayers));
            Epoch = epoch.Trim();
            ScenePath = scenePath.Trim();
            Seed = seed;
            deadline = nowSeconds + timeoutSeconds;
            State = CoopSceneLoadState.Loading;
        }

        public CoopSceneReadyResult ReportReady(string accountPlayerId,
            string epoch)
        {
            if (State != CoopSceneLoadState.Loading)
                return CoopSceneReadyResult.NotLoading;
            if (!string.Equals(Epoch, epoch, StringComparison.Ordinal))
                return CoopSceneReadyResult.WrongEpoch;
            string account = accountPlayerId?.Trim() ?? string.Empty;
            if (!expected.Contains(account))
                return CoopSceneReadyResult.UnexpectedPlayer;
            if (!ready.Add(account)) return CoopSceneReadyResult.Duplicate;
            if (ready.Count != expected.Count) return CoopSceneReadyResult.Accepted;
            State = CoopSceneLoadState.Ready;
            return CoopSceneReadyResult.Completed;
        }

        public bool RemovePlayer(string accountPlayerId)
        {
            if (State != CoopSceneLoadState.Loading ||
                !expected.Remove(accountPlayerId?.Trim() ?? string.Empty))
                return false;
            ready.Remove(accountPlayerId?.Trim() ?? string.Empty);
            State = CoopSceneLoadState.Cancelled;
            return true;
        }

        public bool Tick(double nowSeconds)
        {
            if (State != CoopSceneLoadState.Loading || nowSeconds < deadline)
                return false;
            State = CoopSceneLoadState.TimedOut;
            return true;
        }

        public void Cancel()
        {
            if (State == CoopSceneLoadState.Loading)
                State = CoopSceneLoadState.Cancelled;
        }
    }
}
