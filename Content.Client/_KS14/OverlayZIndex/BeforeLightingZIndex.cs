using Robust.Shared.Enums;

namespace Content.Client._KS14.OverlayZIndex;

/// <summary>
///     Draw order of the overlays in <see cref="OverlaySpace.BeforeLighting"/>, first to last. Members have no values on
///         purpose: the order they are written in is the order they draw in, and an overlay's
///         <see cref="Overlay.ZIndex"/> is <see cref="OverlayZIndexExtensions.ToZIndex(BeforeLightingZIndex)"/>.
/// </summary>
/// <remarks>
///     <para>
///         These overlays build the light map. <see cref="BeforeLightTarget"/> creates and clears the enlarged light
///         target, the glow overlays draw into it, <see cref="LightBlur"/> blurs it and <see cref="AfterLightTarget"/>
///         copies it into the real light render target, clearing that first. A glow overlay therefore has to be written
///         after <see cref="BeforeLightTarget"/> and before <see cref="LightBlur"/>: any later and it is drawn
///         unblurred, or after the copy, where it does nothing.
///     </para>
///     <para>
///         Glow overlays only add light, so their order among themselves is free.
///     </para>
///     <para>
///         Keep everything before <see cref="Default"/>. The night vision overlay also draws here, with its
///         <see cref="WorldSpaceBelowFovZIndex.PhosphorNightVision"/> index, which is above <see cref="Default"/>, so it
///         draws after the whole chain: it adds its light to the finished light map.
///     </para>
/// </remarks>
public enum BeforeLightingZIndex
{
    /// <summary>
    ///     Creates and clears the enlarged light target.
    /// </summary>
    BeforeLightTarget,

    Roof,

    /// <summary>
    ///     Glowing sprite layers.
    /// </summary>
    Emissive,

    LaserSightEmissive,

    TileEmission,

    /// <summary>
    ///     Light from other z-levels, the last thing written into the enlarged light target before the blur.
    /// </summary>
    ZLevelLightBuffer,

    LightBlur,

    /// <summary>
    ///     Copies the enlarged target into the viewport's light render target.
    /// </summary>
    AfterLightTarget,

    SunShadow,

    /// <summary>
    ///     Where an overlay with no <see cref="Overlay.ZIndex"/> draws. Nothing here has none, but should one appear it
    ///         lands after the light chain, with the night vision overlay's light.
    /// </summary>
    Default,
}
