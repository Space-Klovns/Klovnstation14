using Robust.Shared.GameStates;

namespace Content.Shared._KS14.ShipMode.ShipGun;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ShipGunComponent : Component
{
    /// <summary>
    ///     The entityuid of the hardpoint this is anchored to, if any.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly), AutoNetworkedField]
    public EntityUid? anchoredTo;
}
