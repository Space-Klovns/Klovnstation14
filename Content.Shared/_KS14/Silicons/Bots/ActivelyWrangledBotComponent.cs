using Robust.Shared.GameStates;

namespace Content.Shared._KS14.Silicons.Bots;

/// <summary>
///     Added to a bot with <see cref="ControllableBotComponent"/> when someone is trying to move it.
/// </summary>
[RegisterComponent, NetworkedComponent]
[AutoGenerateComponentState(raiseAfterAutoHandleState: true)]
public sealed partial class ActivelyWrangledBotComponent : Component
{
    /// <summary>
    ///     UID of the thing trying to wrangle this bot.
    /// </summary>
    [DataField, AutoNetworkedField]
    public EntityUid? UserUid = null;
}
