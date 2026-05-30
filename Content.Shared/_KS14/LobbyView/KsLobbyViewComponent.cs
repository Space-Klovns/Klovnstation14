using System.Numerics;
using Robust.Shared.GameStates;

namespace Content.Shared._KS14.LobbyView;

/// <summary>
///     Lobby view will be enabled if any unpaused entity (clientside)
///         with this component exists, and it will originate from this entity's
///         eye.
/// </summary>
[RegisterComponent, NetworkedComponent]
[AutoGenerateComponentState]
public sealed partial class KsLobbyViewComponent : Component
{
    /// <summary>
    ///     Affects which lobby view will be displayed.
    ///         The one with the highest of this value will be the one displayed.
    /// </summary>
    [AutoNetworkedField]
    [ViewVariables(VVAccess.ReadWrite)]
    public int Priority = 0;

    [DataField, AutoNetworkedField]
    [ViewVariables(VVAccess.ReadWrite)]
    public Vector2 SizePixels = new(500, 500);
}
