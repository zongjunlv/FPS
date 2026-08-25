using UnityEngine;

public enum EnemyMovementDirectiveKind
{
    None,
    Move,
    Hold
}

public enum EnemyAttackMode
{
    Melee,
    Hitscan
}

public readonly struct EnemyMovementDirective
{
    private EnemyMovementDirective(
        EnemyMovementDirectiveKind kind,
        Vector3 destination)
    {
        Kind = kind;
        Destination = destination;
    }

    public EnemyMovementDirectiveKind Kind { get; }
    public Vector3 Destination { get; }

    public static EnemyMovementDirective None =>
        new(EnemyMovementDirectiveKind.None, default);
    public static EnemyMovementDirective Hold =>
        new(EnemyMovementDirectiveKind.Hold, default);

    public static EnemyMovementDirective MoveTo(Vector3 destination)
    {
        return new EnemyMovementDirective(
            EnemyMovementDirectiveKind.Move,
            destination);
    }
}
