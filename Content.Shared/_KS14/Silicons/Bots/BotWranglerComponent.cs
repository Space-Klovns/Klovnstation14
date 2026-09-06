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

    /// <summary>
    ///     Maximum number of bots that can be wrangled at once.
    ///         If negative or zero, then there is no limit applied.
    /// </summary>
    [DataField]
    public int MaximumSelected = 0;
}
