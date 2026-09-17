using System.Numerics;

namespace Content.Client._KS14.ZLevel;

/// <summary>
///     Client-only bookkeeping for making a transiting entity look like it is still up where it fell from.
///     Holds the sprite state to restore when the transit ends, so the compensation can never accumulate across
///         frames, and carries the compensation itself from the pre-animation pass to the post-animation one.
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
    ///     The sprite scale the entity had before it started transiting, captured once.
    ///     Anything that wants the entity's real on-the-ground size - <see cref="ShadowOverlay.KsShadowOverlay"/>,
    ///         for one - must read this rather than the live sprite scale.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly)]
    public Vector2 BaseScale = Vector2.One;

    /// <summary>
    ///     The draw depth the entity had before it started transiting, captured once.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly)]
    public int BaseDrawDepth;

    /// <summary>
    ///     World-space displacement that keeps the sprite where the z-level it is falling from would have drawn
    ///         it. Written by <see cref="KsZLevelTransitSpriteSystem"/> before the animation player runs, and
    ///         added on top of the animation output by <see cref="KsZLevelTransitSpriteLiftSystem"/> after it.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly)]
    public Vector2 Lift;

    /// <summary>
    ///     Scale factor that undoes the per-z-level camera shrink, for the same reason as <see cref="Lift"/>.
    ///     1 once the entity has landed.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly)]
    public float ScaleMultiplier = 1f;

    /// <summary>
    ///     The sprite colour the entity had before it started transiting, captured once.
    /// </summary>
    /// <remarks>
    ///     The fade scales this colour's alpha rather than lerping towards <see cref="Color.Transparent"/>,
    ///         which is white - lerping to it would wash the sprite out as it faded.
    /// </remarks>
    [ViewVariables(VVAccess.ReadOnly)]
    public Color BaseColor = Color.White;

    /// <summary>
    ///     Alpha factor applied on top of whatever colour the animation player left, for fading an entity in as
    ///         it drops onto the viewer's own z-level. 1 whenever no fade applies.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly)]
    public float AlphaMultiplier = 1f;
}
