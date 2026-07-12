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
    ///     The entityuid of the anchored gun, if any
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly), AutoNetworkedField]
    public EntityUid? anchoring;

    /// <summary>
    ///     How large can the gun mounted to this be?
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite), DataField("size"), AutoNetworkedField]
    public weaponSizes CompatibleSizes = weaponSizes.Small;
}

[Serializable, NetSerializable]
public enum weaponSizes
{
    Small = 1,
    Medium = 2,
    Large = 3
}
