using Robust.Shared.Prototypes;
using Robust.Shared.GameStates;

namespace Content.Shared._KS14.ShipMode.Hardpoint;

/// <summary>
///     A component for hardpoints.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class HardpointComponent : Component
{
    /// <summary>
    ///     Rotation, in degrees.
    /// </summary>
    [DataField]
    [ViewVariables(VVAccess.ReadWrite)]
    public float Rotation = 0f;

    /// <summary>
    ///     The entityuid of the anchored gun, if any
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly), AutoNetworkedField]
    public EntityUid? anchoring;
}
