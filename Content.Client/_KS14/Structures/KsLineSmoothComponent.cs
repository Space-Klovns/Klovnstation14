using System.Numerics;

namespace Content.Client._KS14.Structures;

/// <summary>
///     Draws a structure as part of a line joined to its neighbours of the same <see cref="Key"/>, decided per axis.
///         An axis with neighbours on both sides shows that axis's wire. One with a neighbour on one side only shows the
///         post that caps the line there, whose art carries the wire itself. A structure with no neighbours at all shows
///         both posts along the way it faces. Client-only: nothing about it is networked or changed by the server.
/// </summary>
/// <remarks>
///     A post layer's art is the cap of a line running in that direction: the north post has its frame at the south
///         edge and its wire running north. Each layer is shown in its own world direction, whichever way the structure
///         itself was placed.
/// </remarks>
[RegisterComponent, Access(typeof(KsLineSmoothSystem))]
public sealed partial class KsLineSmoothComponent : Component
{
    /// <summary>
    ///     Structures join only with others of the same key.
    /// </summary>
    [DataField(required: true)]
    public string Key = string.Empty;

    /// <summary>
    ///     The wire running north to south, shown with neighbours both north and south.
    /// </summary>
    [DataField]
    public KsLineSmoothLayer VerticalWire = new() { Key = "wireVertical" };

    /// <summary>
    ///     The wire running east to west, shown with neighbours both east and west.
    /// </summary>
    [DataField]
    public KsLineSmoothLayer HorizontalWire = new() { Key = "wireHorizontal" };

    /// <summary>
    ///     The post capping a line that runs north from here: shown with a neighbour north and none south.
    /// </summary>
    [DataField]
    public KsLineSmoothLayer NorthPost = new() { Key = "postNorth" };

    [DataField]
    public KsLineSmoothLayer SouthPost = new() { Key = "postSouth" };

    [DataField]
    public KsLineSmoothLayer EastPost = new() { Key = "postEast" };

    [DataField]
    public KsLineSmoothLayer WestPost = new() { Key = "postWest" };
}

/// <summary>
///     One of a line's layers.
/// </summary>
[DataDefinition]
public sealed partial class KsLineSmoothLayer
{
    /// <summary>
    ///     The sprite layer's map key.
    /// </summary>
    [DataField(required: true)]
    public string Key = string.Empty;

    /// <summary>
    ///     Where to draw it, in pixels along the grid (x east, y north), whichever way the structure faces: art whose
    ///         directions aren't centred alike (a wire a few pixels right of centre in one frame and left in another) is
    ///         lined up with this, so the pieces of a line meet.
    /// </summary>
    [DataField]
    public Vector2 Offset;
}
