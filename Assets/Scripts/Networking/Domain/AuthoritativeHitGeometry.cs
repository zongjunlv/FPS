using System;

namespace FPS.Networking.Domain
{
    public static class AuthoritativeHitGeometry
    {
        public static NetVector3 RotateYaw(NetVector3 value, double yaw)
        {
            double radians = yaw * Math.PI / 180d;
            double sine = Math.Sin(radians), cosine = Math.Cos(radians);
            return new NetVector3(value.X * cosine + value.Z * sine, value.Y,
                value.Z * cosine - value.X * sine);
        }

        public static bool HasBox(NetVector3 half) => half.IsFinite &&
            half.X > 0d && half.Y > 0d && half.Z > 0d;

        public static bool RayBox(NetVector3 origin, NetVector3 direction,
            NetVector3 root, double yaw, NetVector3 offset, NetVector3 half,
            double maximumDistance, out double distance)
        {
            NetVector3 localOrigin = RotateYaw(origin - root, -yaw) - offset;
            NetVector3 localDirection = RotateYaw(direction, -yaw);
            double near = 0d, far = maximumDistance;
            bool result = HasBox(half) &&
                Slab(localOrigin.X, localDirection.X, half.X, ref near, ref far) &&
                Slab(localOrigin.Y, localDirection.Y, half.Y, ref near, ref far) &&
                Slab(localOrigin.Z, localDirection.Z, half.Z, ref near, ref far);
            distance = near;
            return result && far >= near && near <= maximumDistance;
        }

        public static NetVector3 BoxNormal(NetVector3 hit, NetVector3 root, double yaw,
            NetVector3 offset, NetVector3 half)
        {
            NetVector3 local = RotateYaw(hit - root, -yaw) - offset;
            double x = Math.Abs(local.X / half.X), y = Math.Abs(local.Y / half.Y), z = Math.Abs(local.Z / half.Z);
            NetVector3 face = x >= y && x >= z ? new NetVector3(Math.Sign(local.X), 0, 0) :
                y >= z ? new NetVector3(0, Math.Sign(local.Y), 0) : new NetVector3(0, 0, Math.Sign(local.Z));
            return RotateYaw(face, yaw);
        }

        private static bool Slab(double origin, double direction, double half,
            ref double near, ref double far)
        {
            if (Math.Abs(direction) < 0.0000001d) return Math.Abs(origin) <= half;
            double first = (-half - origin) / direction, second = (half - origin) / direction;
            near = Math.Max(near, Math.Min(first, second));
            far = Math.Min(far, Math.Max(first, second));
            return far >= near;
        }
    }
}
