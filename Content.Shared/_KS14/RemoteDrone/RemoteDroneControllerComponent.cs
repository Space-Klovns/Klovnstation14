using Robust.Shared.GameStates;
using Robust.Shared.Player;

namespace Content.Shared._KS14.RemoteDrone;

/// <summary>
///     Component for things that control remote drones (computers, laptops, etc.),
///         not actually the person controlling the drone.
/// </summary>
[RegisterComponent, NetworkedComponent]
[AutoGenerateComponentState]
public sealed partial class RemoteDroneControllerComponent : Component
{
    /// <summary>
    ///     Drone linked to this controller.
    /// </summary>
    [AutoNetworkedField]
    [ViewVariables(VVAccess.ReadOnly)]
    public EntityUid? LinkedDroneUid = null;

    /// <summary>
    ///     Session of the player controlling the drone.
    ///
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly)]
    public ICommonSession? UserSession = null;
}
