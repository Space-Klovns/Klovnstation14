using System.Numerics;

namespace Content.Client._KS14.ZLevel;

/// <summary>
///     Client-only bookkeeping for making a transiting entity look like it is still up where it fell from.
///     Carries the compensation from the pre-animation pass to the post-animation one, and remembers what the
///         sprite looked like underneath it so that it can be put back.
/// </summary>
[RegisterComponent]
[Access(typeof(KsZLevelTransitSpriteSystem), typeof(KsZLevelTransitSpriteLiftSystem))]
public sealed partial class KsZLevelTransitSpriteComponent : Component
{
    /*
        Offset, Scale and Color are all [Animatable], so none of them can be snapshotted once at the start of a
            transit: whatever an in-progress animation happened to be showing at that instant would be treated
            as the entity's real appearance for the rest of the fall, and restored as such at the end of it. A
            stun's blue colour flash starting just before a fall is exactly that, and the entity stays blue.

        So these are rewritten every frame instead, recorded by the post-animation pass immediately before it
            layers the transit compensation on top. The pre-animation pass puts them back, which undoes our own
            last write and nothing else - whether the animation player wrote in between or not.
    */

    /// <summary>
    ///     The sprite offset as the animation player left it, before the lift was added.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly)]
    public Vector2 PreLiftOffset;

    /// <summary>
    ///     The sprite scale as the animation player left it, before the depth compensation was applied.
    ///     Anything that wants the entity's real on-the-ground size - <see cref="ShadowOverlay.KsShadowOverlay"/>,
    ///         for one - must read this rather than the live sprite scale.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly)]
    public Vector2 PreLiftScale = Vector2.One;

    /// <summary>
    ///     The sprite colour as the animation player left it, before the transit fade was applied.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly)]
    public Color PreLiftColor = Color.White;

    /// <summary>
    ///     The draw depth the entity had before it started transiting.
    ///     Unlike the three above this one is captured once, because draw depth is not animatable.
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
    ///     Alpha factor applied on top of whatever colour the animation player left, for fading an entity in as
    ///         it drops onto the viewer's own z-level. 1 whenever no fade applies.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly)]
    public float AlphaMultiplier = 1f;
}
