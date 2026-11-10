using Content.Client._KS14.Tgui;
using Content.Client._KS14.Tgui.Chat;
using Content.Client.UserInterface.Systems.Chat.Widgets;
using Content.Shared.CCVar;

namespace Content.Client.UserInterface.Systems.Chat;

public sealed partial class ChatUIController
{
    private KsTguiChatPages? _ksPages;

    /// <summary>
    ///     The tgui chat panel's pages, which every chat box shares. Made when first asked for, and counts unread messages from
    ///         then on.
    /// </summary>
    public KsTguiChatPages KsPages
    {
        get
        {
            if (_ksPages != null)
                return _ksPages;

            _ksPages = new KsTguiChatPages(_config) { Counting = KsTguiTheme.ReadEnabled() };
            MessageAdded += message => _ksPages.CountUnread(message.Channel);
            return _ksPages;
        }
    }

    /// <summary>
    ///     Takes the chat window's colour from the stylesheet again, after a theme change. Once it has been given an
    ///         opacity it holds an override, and would keep the old theme's colour.
    /// </summary>
    public void KsRefreshChatWindow()
    {
        // The tgui panel and its say bar come and go with the theme.
        var tguiEnabled = KsTguiTheme.ReadEnabled();
        foreach (var chat in _chats)
        {
            chat.KsSetTguiMode(tguiEnabled);
        }

        var chatBox = UIManager.ActiveScreen?.GetWidget<ChatBox>() ?? UIManager.ActiveScreen?.GetWidget<ResizableChatBox>();
        if (chatBox?.ChatWindowPanel is not { } panel)
            return;

        // Left to the lobby's own translucency, as SetChatWindowOpacity does.
        if (UIManager.ActiveScreen is Content.Client.Lobby.UI.LobbyGui)
            return;

        panel.PanelOverride = null;
        SetChatWindowOpacity(_config.GetCVar(CCVars.ChatWindowOpacity));
    }
}
