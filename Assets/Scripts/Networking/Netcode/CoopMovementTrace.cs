using System.Globalization;
using FPS.Networking.Domain;

namespace FPS.Networking.Netcode
{
    internal static class CoopMovementTrace
    {
        internal const int MaximumSamplesPerPlayerRun = 256;

        internal static string MovementFields(string prefix,
            PlayerMovementState state) => string.Format(
            CultureInfo.InvariantCulture,
            "{0}X={1:F6} {0}Y={2:F6} {0}Z={3:F6} {0}VX={4:F6} {0}VY={5:F6} {0}VZ={6:F6} {0}Stance={7} {0}Grounded={8} {0}GroundY={9:F6} {0}JumpTick={10}",
            prefix, state.Position.X, state.Position.Y, state.Position.Z,
            state.Velocity.X, state.Velocity.Y, state.Velocity.Z,
            state.Stance, state.Grounded ? 1 : 0, state.GroundHeight,
            state.LastJumpTick);
    }
}
