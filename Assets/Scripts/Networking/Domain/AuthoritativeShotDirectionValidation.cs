using System;

namespace FPS.Networking.Domain
{
    /// <summary>Checks bounded direction deviation without widening hit volumes; not complete anti-cheat.</summary>
    internal static class AuthoritativeShotDirectionValidation
    {
        // PlayerGameplayRig.prefab crouching/standing camera heights; transitions
        // remain within this interval and do not need a client-provided eye origin.
        private const double MinimumCameraHeight = 1.05d;
        private const double MaximumCameraHeight = 1.65d;

        public static bool WithinWeaponAimAtDistance(PlayerInputCommand command,
            double distance, double maximumDirectionDeviationDegrees)
        {
            if (double.IsNaN(distance) || double.IsInfinity(distance) || distance < 0d ||
                double.IsNaN(maximumDirectionDeviationDegrees) ||
                double.IsInfinity(maximumDirectionDeviationDegrees) ||
                maximumDirectionDeviationDegrees < 0d || maximumDirectionDeviationDegrees >= 45d)
                return false;
            NetVector3 forward = CoopGameplayRules.AimDirection(
                command.AimYawDegrees, command.AimPitchDegrees);
            NetVector3 direction = command.ShotDirection.Normalized;
            double projection = NetVector3.Dot(forward, direction);
            if (projection <= 0d) return false;

            double lateralTravel = (direction - forward * projection).Magnitude * distance;
            NetVector3 muzzleOffset = command.ShotOrigin - command.ClaimedPosition;
            double parallax = Math.Max(
                TransverseMagnitude(muzzleOffset - new NetVector3(0d, MinimumCameraHeight, 0d), forward),
                TransverseMagnitude(muzzleOffset - new NetVector3(0d, MaximumCameraHeight, 0d), forward));
            double directionTravel = Math.Tan(maximumDirectionDeviationDegrees * Math.PI / 180d) *
                distance * projection;
            // The caller already limits muzzle-to-player displacement to 2.5m.
            // Parallax is therefore bounded and cannot become a 45-degree cone at range.
            return lateralTravel <= parallax + directionTravel + 0.000001d;
        }

        public static bool TryGetHitDistance(NetVector3 origin, NetVector3 direction,
            AuthoritativeTargetState pose, double accuracyAssist,
            double maximumDistance, out double distance)
        {
            double bodyDistance;
            bool body = AuthoritativeHitGeometry.HasBox(pose.BodyHalfExtents)
                ? AuthoritativeHitGeometry.RayBox(origin, direction, pose.Position, pose.YawDegrees,
                    pose.BodyOffset, pose.BodyHalfExtents, maximumDistance, out bodyDistance)
                : RaySphere(origin, direction,
                    pose.Position + AuthoritativeHitGeometry.RotateYaw(pose.BodyOffset, pose.YawDegrees),
                    pose.Radius * accuracyAssist, maximumDistance, out bodyDistance);
            double headDistance = 0d;
            bool head = AuthoritativeHitGeometry.HasBox(pose.HeadHalfExtents)
                ? AuthoritativeHitGeometry.RayBox(origin, direction, pose.Position, pose.YawDegrees,
                    pose.HeadOffset, pose.HeadHalfExtents, maximumDistance, out headDistance)
                : pose.HeadRadius > 0d && RaySphere(origin, direction,
                    pose.Position + AuthoritativeHitGeometry.RotateYaw(pose.HeadOffset, pose.YawDegrees),
                    pose.HeadRadius * accuracyAssist, maximumDistance, out headDistance);
            distance = body && head ? Math.Min(bodyDistance, headDistance) :
                body ? bodyDistance : headDistance;
            return body || head;
        }

        private static double TransverseMagnitude(NetVector3 value, NetVector3 forward) =>
            (value - forward * NetVector3.Dot(value, forward)).Magnitude;

        private static bool RaySphere(NetVector3 origin, NetVector3 direction,
            NetVector3 center, double radius, double maximumDistance, out double distance)
        {
            NetVector3 toCenter = center - origin;
            double projection = NetVector3.Dot(toCenter, direction);
            double perpendicularSquared = toCenter.SqrMagnitude - projection * projection;
            double radiusSquared = radius * radius;
            if (perpendicularSquared > radiusSquared)
            {
                distance = 0d;
                return false;
            }
            double offset = Math.Sqrt(Math.Max(0d, radiusSquared - perpendicularSquared));
            double near = projection - offset;
            distance = near >= 0d ? near : projection + offset;
            return distance >= 0d && distance <= maximumDistance;
        }
    }
}
