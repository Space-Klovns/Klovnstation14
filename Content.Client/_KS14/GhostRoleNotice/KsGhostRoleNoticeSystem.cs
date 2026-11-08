using Content.Client.UserInterface.Systems.Chat;
using Content.Shared._KS14.GhostRoleNotice;
using Content.Shared.Chat;
using Robust.Client.UserInterface;
using Robust.Shared.Utility;

namespace Content.Client._KS14.GhostRoleNotice;

/// <summary>
///     Prints the chat line for <see cref="KsGhostRoleNoticeEvent"/> in the client's selected culture.
/// </summary>
public sealed partial class KsGhostRoleNoticeSystem : EntitySystem
{
    [Dependency] private IUserInterfaceManager _userInterfaceManager = default!;

    /// <summary>
    ///     Every notice this client was sent since it connected.
    /// </summary>
    [ViewVariables]
    public int ReceivedCount { get; private set; }

    [SubscribeNetworkEvent]
    private void OnNotice(KsGhostRoleNoticeEvent args)
    {
        ReceivedCount++;

        var roleName = Loc.TryGetString(args.RoleName, out var translatedName) ? translatedName : args.RoleName;
        var message = Loc.GetString("ks-ghost-role-notice-message", ("role", roleName));
        var wrappedMessage = $"[font size=11][color=#a4885c]{FormattedMessage.EscapeText(message)}[/color][/font]";

        _userInterfaceManager.GetUIController<ChatUIController>().ProcessChatMessage(
            new ChatMessage(ChatChannel.Server, message, wrappedMessage, NetEntity.Invalid, senderKey: null));
    }
}
