using Robust.Shared.GameStates;
using Content.Shared._KS14.ShipMode.Hardpoint;
namespace Content.Shared._KS14.ShipMode.ShipGun;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ShipGunComponent : Component
{
    /// <summary>
    ///     The entityuid of the hardpoint this is anchored to, if any.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly), AutoNetworkedField]
    public EntityUid? anchoredTo;

    /// <summary>
    ///     How large does the hardpoint have to be, at minimum?
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite), DataField("size")]
    public weaponSizes CompatibleSizes = weaponSizes.Small;
}
