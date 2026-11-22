using Robust.Shared.Enums;

namespace Content.Client._KS14.OverlayZIndex;

/// <summary>
///     Draw order of the overlays in <see cref="OverlaySpace.WorldSpaceBelowFOV"/> that matter to night vision, first to
///         last. Members have no values on purpose: the order they are written in is the order they draw in, and an
///         overlay's <see cref="Overlay.ZIndex"/> is
///         <see cref="OverlayZIndexExtensions.ToZIndex(WorldSpaceBelowFovZIndex)"/>.
/// </summary>
/// <remarks>
///     Overlays outside this list that set a <see cref="Overlay.ZIndex"/> (<c>StainOverlay</c>, <c>CanisterOverlay</c>)
///         use draw depths, which sit at or below <see cref="Default"/> and so stay before everything after it.
/// </remarks>
public enum WorldSpaceBelowFovZIndex
{
    /// <summary>
    ///     Where an overlay with no <see cref="Overlay.ZIndex"/> draws, such as the emissive bloom, so those draw before
    ///         everything below.
    /// </summary>
    Default,

    /// <summary>
    ///     Weather (<c>StencilOverlay</c>).
    /// </summary>
    Weather,

    /// <summary>
    ///     The night vision shader samples the screen, so everything seen through the goggles has to draw before it: the
    ///         emissive bloom (at <see cref="Default"/>) and <see cref="Weather"/>.
    /// </summary>
    /// <remarks>
    ///     The overlay's <see cref="OverlaySpace.BeforeLighting"/> half has a single <see cref="Overlay.ZIndex"/> as well,
    ///         and needs it after the light chain, which <see cref="BeforeLightingZIndex"/> keeps before its own
    ///         <see cref="BeforeLightingZIndex.Default"/>. Being after <see cref="Default"/> here is what puts it there.
    /// </remarks>
    PhosphorNightVision,
}
