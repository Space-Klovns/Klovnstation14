using System.Numerics;

namespace Content.Client._KS14.ZLevel;

/// <summary>
///     Client-only bookkeeping for lifting a transiting entity's sprite up the screen.
///     Holds the sprite state to restore when the transit ends, so the lift can never accumulate across frames,
///         and carries the lift itself from the pre-animation pass to the post-animation one.
/// </summary>
[RegisterComponent]
[Access(typeof(KsZLevelTransitSpriteSystem), typeof(KsZLevelTransitSpriteLiftSystem))]
public sealed partial class KsZLevelTransitSpriteComponent : Component
{
    /// <summary>
    ///     The sprite offset the entity had before it started transiting, captured once.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly)]
    public Vector2 BaseOffset;

    /// <summary>
    ///     The draw depth the entity had before it started transiting, captured once.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly)]
    public int BaseDrawDepth;

    /// <summary>
    ///     Written by <see cref="KsZLevelTransitSpriteSystem"/> before the animation player runs, and added on
    ///         top of the animation output by <see cref="KsZLevelTransitSpriteLiftSystem"/> after it.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly)]
    public Vector2 Lift;
}
