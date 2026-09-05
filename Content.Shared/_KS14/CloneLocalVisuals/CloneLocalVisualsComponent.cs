using Content.Shared.DisplacementMap;
using Robust.Shared.GameStates;

namespace Content.Shared._KS14.CloneLocalVisuals;

/// <summary>
///     Draws the local attached entity ontop of this.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class CloneLocalVisualsComponent : Component
{
    /// <summary>
    ///     The displacement map data applied to each sprite layer.
    /// </summary>
    [DataField]
    public DisplacementData? Displacement = null;
}
