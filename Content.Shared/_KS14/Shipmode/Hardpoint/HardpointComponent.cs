using Robust.Shared.Prototypes;
using Robust.Shared.GameStates;

namespace Content.Shared._KS14.ShipMode.Hardpoint;

/// <summary>
///     A component for hardpoints.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class ScenarioObjectiveComponent : Component
{
    /// <summary>
    ///     Rotation, in degrees.
    /// </summary>
    [DataField]
    [ViewVariables(VVAccess.ReadWrite)]
    public float Rotation = 0f;
}
