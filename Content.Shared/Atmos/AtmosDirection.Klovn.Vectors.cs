// KS14: added in this fork
using System.Numerics;

namespace Content.Shared.Atmos
{
    public static partial class AtmosDirectionHelpers
    {
        /// <summary>
        ///     The unit vector pointing along a cardinal <see cref="AtmosDirection"/>.
        ///     The floating-point counterpart of <see cref="CardinalToIntVec"/>.
        /// </summary>
        /// <exception cref="ArgumentException">Thrown if the direction is not a single cardinal.</exception>
        public static Vector2 CardinalToVec(this AtmosDirection direction)
        {
            switch (direction)
            {
                case AtmosDirection.North:
                    return new Vector2(0f, 1f);
                case AtmosDirection.East:
                    return new Vector2(1f, 0f);
                case AtmosDirection.South:
                    return new Vector2(0f, -1f);
                case AtmosDirection.West:
                    return new Vector2(-1f, 0f);
                default:
                    throw new ArgumentException($"Direction dir {direction} is not a cardinal direction", nameof(direction));
            }
        }
    }
}
