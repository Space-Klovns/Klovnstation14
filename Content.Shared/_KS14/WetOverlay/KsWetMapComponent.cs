using Robust.Shared.GameStates;

namespace Content.Shared._KS14.WetOverlay;

/// <summary>
///     Max number of droplets on a players screen at once, on this map.
/// </summary>
[RegisterComponent, NetworkedComponent]
[AutoGenerateComponentState]
public sealed partial class KsWetMapComponent : Component
{
    [DataField, AutoNetworkedField]
    [ViewVariables(VVAccess.ReadWrite)]
    public int SoftDropletCap = 0;
}
