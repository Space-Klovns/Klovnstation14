using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._KS14.Fluidmos.Ice;

/// <summary>
///     Marks a tile's sheet of frozen reagents: a puddle's solution has no phases, so what freezes out of one lives here,
///         in the sheet's <see cref="SolutionName"/> solution, one sheet per tile. Holds no state of its own; it keys the
///         server's per-tile cache and the client's look, which is the puddle it froze out of, frozen.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class KsIceSheetComponent : Component
{
    public const string SolutionName = "ice";
}

[Serializable, NetSerializable]
public enum KsIceSheetVisuals : byte
{
    /// <summary>
    ///     The sheet's tint: its solution's colour, with alpha by how thick it is.
    /// </summary>
    Color,

    /// <summary>
    ///     How full the sheet is, as a share of what a puddle of the same reagents holds before it overflows: the same
    ///         measure as <c>PuddleVisuals.CurrentVolume</c>, so the sheet takes the shape the puddle had.
    /// </summary>
    Fill,
}
