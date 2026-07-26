using Robust.Shared.GameStates;

namespace Content.Shared._KS14.Silicons.Bots;

/// <summary>
/// This component makes a bot controllable
/// </summary>
[RegisterComponent, NetworkedComponent]
[AutoGenerateComponentState]
public sealed partial class BotWranglerComponent : Component
{
    [DataField, AutoNetworkedField]
    public HashSet<EntityUid> WrangledBotUids = [];
}
