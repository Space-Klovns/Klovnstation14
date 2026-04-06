using System.Numerics;

namespace Content.Server._KS14.MovementIllusion;

/// <summary>
///     Added to a map which should move every thing on it
///         that doesn't have <see cref="MovementIllusionFocusComponent"/>.
/// </summary>
[RegisterComponent]
public sealed partial class MovementIllusionMapComponent : Component
{
    /// <summary>
    ///     Velocity of things affected.
    ///         This is how much they will move per second.
    /// </summary>
    [DataField]
    [ViewVariables(VVAccess.ReadWrite)]
    public Vector2 Velocity = Vector2.Zero;
}
