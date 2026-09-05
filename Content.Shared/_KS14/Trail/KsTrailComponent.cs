using Robust.Shared.GameStates;
using Robust.Shared.Utility;

namespace Content.Shared._KS14.Trail;

/// <summary>
///     Draws a 'trail' out of this entity: <see cref="StartSprite"/> at the first tile,
///         then <see cref="Sprite"/> repeated at its native size for every tile after that.
///     The trail runs along this entity's local 'up' axis, in its parent's frame, so it
///         rides whatever grid it was spawned on.
/// </summary>
/// <remarks>
///     Rendered entirely by KsTrailOverlay on the client; this entity needs no SpriteComponent.
/// </remarks>
[RegisterComponent, NetworkedComponent]
[AutoGenerateComponentState, AutoGenerateComponentPause]
public sealed partial class KsTrailComponent : Component
{
    /// <summary>
    ///     Maximum tile count the overlay will ever draw, no matter what <see cref="Length"/> says.
    /// </summary>
    public const int MaxLength = 128;

    #region Geometry (networked)
    /// <summary>
    ///     Drawn at tile index 1, closest to the trail's origin. Falls back to
    ///         <see cref="Sprite"/> when null.
    /// </summary>
    [DataField, AutoNetworkedField]
    public SpriteSpecifier? StartSprite;

    /// <summary>
    ///     Repeated at tile indices 2..<see cref="Length"/>, always at its native size.
    /// </summary>
    [DataField(required: true), AutoNetworkedField]
    public SpriteSpecifier Sprite = default!;

    /// <summary>
    ///     How many tiles long the trail is. Clamped to <see cref="MaxLength"/> when drawn.
    /// </summary>
    [DataField, AutoNetworkedField]
    public int Length = 1;

    /// <summary>
    ///     Distance, in world units, between the centres of two consecutive tiles.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float Spacing = 1f;

    /// <summary>
    ///     Game-time that the progressive reveal starts at. Tiles furthest from the origin
    ///         appear first, so the trail 'draws itself' as its source travels down it.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadOnly)]
    [AutoNetworkedField, AutoPausedField]
    public TimeSpan RevealStartTime;

    /// <summary>
    ///     How long the progressive reveal takes. Zero means the whole trail is visible immediately.
    /// </summary>
    [DataField, AutoNetworkedField]
    public TimeSpan RevealDuration = TimeSpan.Zero;

    /// <summary>
    ///     Fraction of <see cref="RevealDuration"/> over which the whole trail ramps up from
    ///         transparent, so it materialises alongside whatever is drawing it instead of
    ///         snapping to full brightness the instant the reveal starts.
    /// </summary>
    /// <remarks>
    ///     Expressed as a fraction rather than a duration so it tracks the reveal automatically,
    ///         however long that happens to be for a given source.
    /// </remarks>
    [DataField, AutoNetworkedField]
    public float RevealFadeInFraction = 0f;

    /// <summary>
    ///     The entity carving this trail out, if any. While it is alive the reveal follows its
    ///         actual position along the trail axis instead of a clock, so the two cannot drift
    ///         apart however late the source's own animation happens to start.
    /// </summary>
    /// <remarks>
    ///     Falls back to the <see cref="RevealStartTime"/> schedule once the source is gone,
    ///         by which point the trail is fully drawn anyway.
    /// </remarks>
    [DataField, AutoNetworkedField]
    public EntityUid? SourceEntity;
    #endregion

    #region Appearance (deliberately NOT networked; animated clientside)
    /// <summary>
    ///     Modulate applied to every tile. Its alpha is the master multiplier for the whole trail.
    /// </summary>
    [DataField]
    public Color Color = Color.White;

    /// <summary>
    ///     Artificial gaussian blur radius, in pixels. Zero takes the cheap no-render-target path.
    /// </summary>
    [DataField]
    public float Blur = 0f;

    /// <summary>
    ///     Tuning multiplier applied when mapping <see cref="Blur"/> onto the engine's blur radius.
    /// </summary>
    [DataField]
    public float BlurScale = 1f;

    /// <summary>
    ///     Number of tiles at the far end whose alpha ramps down to nothing, so the trail's
    ///         cut-off end isn't a hard edge.
    /// </summary>
    [DataField]
    public int TailFadeTiles = 0;

    /// <summary>
    ///     Point along the trail, as a fraction from head (0) to tail (1), where alpha starts
    ///         ramping linearly down to nothing at the tail. 1 leaves the trail alone.
    /// </summary>
    /// <remarks>
    ///     Unlike <see cref="TailFadeTiles"/>, which softens the last few tiles regardless of how
    ///         long the trail is, this scales with the trail - a long trail fades over a long
    ///         stretch. Use it to bleed the whole back half out rather than to hide a hard edge;
    ///         the two multiply if both are set.
    /// </remarks>
    [DataField]
    public float TailFadeStartFraction = 1f;
    #endregion
}
