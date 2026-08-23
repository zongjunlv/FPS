using System;
using System.Collections.Generic;

public enum GameplayLockReason
{
    PauseMenu,
    UpgradeChoice,
    Inventory,
    Victory,
    Defeat,
    Legacy
}

public sealed class GameplayLockState
{
    private readonly Dictionary<int, GameplayLockReason> activeLocks = new();
    private int nextLockId;

    public event Action Changed;

    public bool IsLocked => activeLocks.Count > 0;
    public int ActiveLockCount => activeLocks.Count;
    public GameplayLockReason? TopReason
    {
        get
        {
            GameplayLockReason? topReason = null;
            int topPriority = int.MinValue;
            int topLockId = int.MinValue;

            foreach (KeyValuePair<int, GameplayLockReason> activeLock
                     in activeLocks)
            {
                int priority = GetPriority(activeLock.Value);

                if (priority < topPriority ||
                    (priority == topPriority &&
                     activeLock.Key <= topLockId))
                {
                    continue;
                }

                topPriority = priority;
                topLockId = activeLock.Key;
                topReason = activeLock.Value;
            }

            return topReason;
        }
    }

    public GameplayLockLease Acquire(GameplayLockReason reason)
    {
        int lockId = ++nextLockId;
        activeLocks.Add(lockId, reason);
        Changed?.Invoke();
        return new GameplayLockLease(this, lockId, reason);
    }

    public bool IsReasonActive(GameplayLockReason reason)
    {
        foreach (GameplayLockReason activeReason in activeLocks.Values)
        {
            if (activeReason == reason)
            {
                return true;
            }
        }

        return false;
    }

    public bool IsTopmost(GameplayLockReason reason)
    {
        return TopReason == reason;
    }

    public void Reset()
    {
        if (activeLocks.Count == 0)
        {
            return;
        }

        activeLocks.Clear();
        Changed?.Invoke();
    }

    internal void Release(int lockId)
    {
        if (activeLocks.Remove(lockId))
        {
            Changed?.Invoke();
        }
    }

    private static int GetPriority(GameplayLockReason reason)
    {
        return reason switch
        {
            GameplayLockReason.Victory => 400,
            GameplayLockReason.Defeat => 400,
            GameplayLockReason.UpgradeChoice => 300,
            GameplayLockReason.PauseMenu => 200,
            GameplayLockReason.Inventory => 100,
            _ => 0
        };
    }
}

public sealed class GameplayLockLease : IDisposable
{
    private GameplayLockState owner;
    private readonly int lockId;

    internal GameplayLockLease(
        GameplayLockState owner,
        int lockId,
        GameplayLockReason reason)
    {
        this.owner = owner;
        this.lockId = lockId;
        Reason = reason;
    }

    public GameplayLockReason Reason { get; }
    public bool IsReleased => owner == null;

    public void Dispose()
    {
        GameplayLockState currentOwner = owner;

        if (currentOwner == null)
        {
            return;
        }

        owner = null;
        currentOwner.Release(lockId);
    }
}
