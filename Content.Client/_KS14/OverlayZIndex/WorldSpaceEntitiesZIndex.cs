using Robust.Shared.Enums;
using DrawDepth = Content.Shared.DrawDepth.DrawDepth;

namespace Content.Client._KS14.OverlayZIndex;

/// <summary>
///     <see cref="Overlay.ZIndex"/> of the overlays in <see cref="OverlaySpace.WorldSpaceEntities"/> that have a
///         <c>ContentZIndex</c>.
/// </summary>
/// <remarks>
///     <para>
///         The one enum whose members carry values. In this space the engine draws an overlay before every sprite whose
///         <see cref="DrawDepth"/> is at least its <see cref="Overlay.ZIndex"/>, so the index is a
///         <see cref="DrawDepth"/>, not a place in a list: an overlay draws above every sprite below that depth and below
///         the ones at or above it. Order alone can't say that. Visible gas draws at <see cref="DrawDepth.Gasses"/>.
///     </para>
///     <para>
///         Use the member as it is, with a plain <c>(int)</c> cast, not <see cref="OverlayZIndexExtensions"/>.
///     </para>
/// </remarks>
public enum WorldSpaceEntitiesZIndex
{
    /// <summary>
    ///     Where an object on the floor would draw: drawn before every sprite at this depth or above, so mobs and items in
    ///         the fire cover it.
    /// </summary>
    ChemicalFireUnder = (int)DrawDepth.Objects,

    /// <summary>
    ///     Above mobs and doors, so flames cover what is standing in them, but below visible gas.
    /// </summary>
    ChemicalFireOver = (int)DrawDepth.Overdoors,
}
