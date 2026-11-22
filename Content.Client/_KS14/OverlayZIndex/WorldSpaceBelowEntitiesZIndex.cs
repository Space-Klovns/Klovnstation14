using Robust.Shared.Enums;

namespace Content.Client._KS14.OverlayZIndex;

/// <summary>
///     Draw order of the overlays in <see cref="OverlaySpace.WorldSpaceBelowEntities"/> that set a
///         <see cref="Overlay.ZIndex"/>, first to last. Members have no values on purpose: the order they are written
///         in is the order they draw in, and an overlay's <see cref="Overlay.ZIndex"/> is
///         <see cref="OverlayZIndexExtensions.ToZIndex(WorldSpaceBelowEntitiesZIndex)"/>.
/// </summary>
public enum WorldSpaceBelowEntitiesZIndex
{
    /// <summary>
    ///     Before every overlay in this space that sets no <see cref="Overlay.ZIndex"/>.
    /// </summary>
    AmbientOcclusion,

    /// <summary>
    ///     Where an overlay with no <see cref="Overlay.ZIndex"/> draws.
    /// </summary>
    Default,
}
