using System.Collections.Generic;
using UnityEngine;

public sealed class EnemySquadAlertDebugView : MonoBehaviour
{
    public bool IsActive { get; private set; }
    public float DisplayedRadius { get; private set; }

    public void Show(
        EnemySquadAlert alert,
        IReadOnlyList<EnemyAlertDebugRelation> relations)
    {
        DisplayedRadius = alert.Radius;
        IsActive = true;
    }

    public void Hide()
    {
        IsActive = false;
    }
}
