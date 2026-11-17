using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._KS14.Fluidmos.Ice;

/// <summary>
///     Marks a tile's sheet of frozen reagents: a puddle's solution has no phases, so what freezes out of one lives here,
///         in the sheet's <see cref="SolutionName"/> solution, one sheet per tile. Holds no state of its own; it keys the
///         server's per-tile cache and the client's tint.
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
}
