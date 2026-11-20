using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.Construction.TilePlacement;

/// <summary>
///     Clicking a tile in reach with this item builds <see cref="Prototype"/> there after <see cref="DoAfter"/>, using up
///         <see cref="StackCost"/> of the stack, or the item itself when it isn't a stack. Predicted: the builder sees it
///         go up the moment their own do-after ends.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(KsPlaceOnTileSystem))]
public sealed partial class KsPlaceOnTileComponent : Component
{
    /// <summary>
    ///     What is built.
    /// </summary>
    [DataField(required: true), AutoNetworkedField]
    public EntProtoId Prototype;

    /// <summary>
    ///     How long building it takes. Zero builds it at once.
    /// </summary>
    [DataField, AutoNetworkedField]
    public TimeSpan DoAfter = TimeSpan.FromSeconds(1);

    /// <summary>
    ///     How much of the stack one build uses. Ignored for an item that isn't a stack, which is used up whole.
    /// </summary>
    [DataField, AutoNetworkedField]
    public int StackCost = 1;

    /// <summary>
    ///     Builds on the middle of the clicked tile, facing south, rather than exactly where was clicked.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool SnapToGrid = true;

    /// <summary>
    ///     Refuses a tile with a wall, a table, anything the built thing would collide with, or one of the same thing
    ///         already on it.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool RequireFreeTile = true;

    /// <summary>
    ///     Shown to the builder when the tile is refused.
    /// </summary>
    [DataField, AutoNetworkedField]
    public LocId BlockedPopup = "ks-place-on-tile-blocked";

    /// <summary>
    ///     Shown to the builder when the stack holds less than <see cref="StackCost"/>.
    /// </summary>
    [DataField, AutoNetworkedField]
    public LocId NotEnoughPopup = "ks-place-on-tile-not-enough";
}
