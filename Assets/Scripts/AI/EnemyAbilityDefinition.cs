using System;
using UnityEngine;

public abstract class EnemyAbilityDefinition : ScriptableObject
{
    [SerializeField] private string stableId;

    public string StableId => stableId;

    protected void ConfigureStableId(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException(
                "An enemy ability requires a stable ID.",
                nameof(id));
        }

        stableId = id.Trim();
    }
}
