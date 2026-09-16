using System;

namespace FPS.Networking.Domain
{
    public enum AuthoritativeEnemyRole : byte
    {
        Assault = 0,
        Raider = 1,
        Support = 2,
        Suppressor = 3,
        Elite = 4
    }

    public enum AuthoritativeEnemyBehavior : byte
    {
        Pooled = 0,
        Patrol = 1,
        Pursue = 2,
        Attack = 3,
        Dead = 4
    }

    public readonly struct AuthoritativeEnemyDecision
    {
        public AuthoritativeEnemyDecision(
            AuthoritativeEnemyBehavior behavior,
            int targetPlayerId,
            double patrolScore,
            double pursueScore,
            double attackScore)
        {
            Behavior = behavior;
            TargetPlayerId = targetPlayerId;
            PatrolScore = patrolScore;
            PursueScore = pursueScore;
            AttackScore = attackScore;
        }

        public AuthoritativeEnemyBehavior Behavior { get; }
        public int TargetPlayerId { get; }
        public double PatrolScore { get; }
        public double PursueScore { get; }
        public double AttackScore { get; }
    }

    public static class AuthoritativeEnemyUtility
    {
        public static AuthoritativeEnemyDecision Decide(
            AuthoritativeEnemyRole role,
            int targetPlayerId,
            double distance,
            double attackRange,
            bool canMove)
        {
            if (targetPlayerId <= 0 || !Finite(distance) || distance < 0d)
                return new AuthoritativeEnemyDecision(
                    AuthoritativeEnemyBehavior.Patrol,
                    0,
                    1d,
                    0d,
                    0d);

            double safeRange = Math.Max(0.1d, attackRange);
            double attackScore = distance <= safeRange
                ? 2d + RoleAttackBias(role)
                : 0d;
            double pursueScore = canMove && distance > safeRange
                ? 1d + RolePursuitBias(role) +
                  Math.Min(0.75d, safeRange / Math.Max(distance, 0.1d))
                : 0d;
            double patrolScore = 0.1d;
            AuthoritativeEnemyBehavior behavior = attackScore >= pursueScore &&
                                                  attackScore >= patrolScore
                ? AuthoritativeEnemyBehavior.Attack
                : pursueScore >= patrolScore
                    ? AuthoritativeEnemyBehavior.Pursue
                    : AuthoritativeEnemyBehavior.Patrol;
            return new AuthoritativeEnemyDecision(
                behavior,
                targetPlayerId,
                patrolScore,
                pursueScore,
                attackScore);
        }

        public static double MoveSpeedMultiplier(
            AuthoritativeEnemyRole role) => role switch
        {
            AuthoritativeEnemyRole.Raider => 1.35d,
            AuthoritativeEnemyRole.Support => 0.82d,
            AuthoritativeEnemyRole.Suppressor => 0.72d,
            AuthoritativeEnemyRole.Elite => 1.08d,
            _ => 1d
        };

        public static double DamageMultiplier(
            AuthoritativeEnemyRole role) => role switch
        {
            AuthoritativeEnemyRole.Support => 0.75d,
            AuthoritativeEnemyRole.Suppressor => 1.15d,
            AuthoritativeEnemyRole.Elite => 1.35d,
            _ => 1d
        };

        private static double RoleAttackBias(
            AuthoritativeEnemyRole role) => role switch
        {
            AuthoritativeEnemyRole.Suppressor => 0.35d,
            AuthoritativeEnemyRole.Elite => 0.25d,
            _ => 0d
        };

        private static double RolePursuitBias(
            AuthoritativeEnemyRole role) => role switch
        {
            AuthoritativeEnemyRole.Raider => 0.45d,
            AuthoritativeEnemyRole.Elite => 0.2d,
            _ => 0d
        };

        private static bool Finite(double value) =>
            !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
