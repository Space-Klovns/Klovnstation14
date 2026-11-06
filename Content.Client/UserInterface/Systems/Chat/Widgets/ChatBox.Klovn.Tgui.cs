using Content.Client._KS14.Tgui;
using Content.Client._KS14.Tgui.Chat;
using Content.Client.UserInterface.Screens;
using Content.Client.UserInterface.Systems.Chat.Controls;
using Content.Shared._KS14.CCVar;
using Content.Shared.Chat;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Configuration;

namespace Content.Client.UserInterface.Systems.Chat.Widgets;

// The tgui chat panel and tgui-say, built around the stock chat box. The stock input and filter popup stay in the tree, hidden
// while tgui is on, because the controller and the rest of the chat code are wired to them: the tgui bar mirrors what is typed
// into the stock input and sends through the same path, so every channel, prefix and typing indicator behaves as before.
public partial class ChatBox
{
    private KsTguiChatPages _ksPages = default!;
    private IConfigurationManager _ksConfig = default!;
    private KsTguiChatHeader _ksHeader = default!;
    private KsTguiChatSettings _ksSettings = default!;
    private KsTguiSayBar _ksSay = default!;
    private KsTguiSayWindow _ksSayWindow = default!;
    private BoxContainer? _ksColumn;
    private UIScreen? _ksScreen;
    private bool _ksSettingsOpen;

    /// <summary>
    ///     Whether the tgui panel is what this box shows. Follows the theme toggle.
    /// </summary>
    public bool KsTguiActive { get; private set; }

    /// <summary>
    ///     True when the say bar is a floating window (the separated layout) rather than docked under the output.
    /// </summary>
    public bool KsSayFloating { get; private set; }

    /// <summary>
    ///     How many lines the output holds.
    /// </summary>
    public int KsEntryCount => Contents.EntryCount;

    public KsTguiChatPages KsPages => _ksPages;
    public KsTguiChatHeader KsHeader => _ksHeader;
    public KsTguiChatSettings KsSettings => _ksSettings;
    public KsTguiSayBar KsSay => _ksSay;
    public KsTguiSayWindow KsSayWindow => _ksSayWindow;

    private void KsTguiInitialize()
    {
        _ksConfig = IoCManager.Resolve<IConfigurationManager>();
        _ksPages = _controller.KsPages;

        _ksColumn = ChatInput.Parent as BoxContainer;

        _ksHeader = new KsTguiChatHeader(_ksPages) { Visible = false };
        _ksHeader.OnAddPressed += KsOnAddPage;
        _ksHeader.OnSettingsPressed += () => KsSetSettingsOpen(!_ksSettingsOpen);

        _ksSettings = new KsTguiChatSettings(_ksPages, _controller) { Visible = false, VerticalExpand = true };
        _ksSettings.OnFontSizeChanged += Repopulate;

        _ksSay = new KsTguiSayBar { Visible = false, HorizontalExpand = true };
        _ksSay.OnTextEdited += KsOnSayTextEdited;
        _ksSay.OnSubmitted += KsOnSaySubmitted;
        _ksSay.OnEscaped += KsOnSayEscaped;
        _ksSay.OnCycled += forward => { CycleChatChannel(forward); KsRefreshSayChannel(); };
        _ksSay.OnChannelClicked += () =>
        {
            CycleChatChannel(true);
            KsRefreshSayChannel();
            _ksSay.FocusInput();
        };
        _ksSay.OnFocusChanged += _controller.NotifyChatFocus;

        _ksSayWindow = new KsTguiSayWindow();

        if (_ksColumn != null)
        {
            _ksColumn.AddChild(_ksHeader);
            _ksHeader.SetPositionInParent(0);
            _ksColumn.AddChild(_ksSettings);
            _ksSettings.SetPositionInParent(1);
            _ksColumn.AddChild(_ksSay);
        }

        KsSetTguiMode(KsTguiTheme.ReadEnabled());
    }

    protected override void EnteredTree()
    {
        base.EnteredTree();

        _ksPages.FilterChanged += KsOnFilterChanged;
        _ksPages.Changed += _ksHeader.Refresh;

        _ksScreen = null;
        for (var parent = Parent; parent != null; parent = parent.Parent)
        {
            if (parent is not UIScreen screen)
                continue;

            _ksScreen = screen;
            break;
        }

        KsPlaceSay();
        _ksHeader.Refresh();
    }

    protected override void ExitedTree()
    {
        base.ExitedTree();

        _ksPages.FilterChanged -= KsOnFilterChanged;
        _ksPages.Changed -= _ksHeader.Refresh;

        // The window lives in the screen, not in this box, so it goes when the box does.
        _ksSayWindow.Close();
        _ksSayWindow.Orphan();
        _ksScreen = null;
    }

    /// <summary>
    ///     Switches between the stock chat and the tgui panel, in place. Called when the theme changes and when a box is built.
    /// </summary>
    public void KsSetTguiMode(bool enabled)
    {
        KsTguiActive = enabled;
        _ksPages.Counting = enabled;

        _ksHeader.Visible = enabled;
        _ksSettings.Visible = enabled && _ksSettingsOpen;
        ChatInput.Visible = !enabled;
        KsPlaceSay();
        KsRefreshSayChannel();

        Repopulate();
    }

    /// <summary>
    ///     Docks the say bar under the output, or puts it in its floating window, as the layout and the theme ask.
    /// </summary>
    private void KsPlaceSay()
    {
        var floating = KsTguiActive && _ksScreen is SeparatedChatGameScreen;
        KsSayFloating = floating;

        if (floating)
        {
            if (_ksSayWindow.Parent != _ksScreen)
            {
                _ksSayWindow.Orphan();
                _ksScreen!.AddChild(_ksSayWindow);
            }

            _ksSayWindow.Host(_ksSay);
            _ksSay.Visible = true;
            return;
        }

        _ksSayWindow.Close();
        if (_ksSay.Parent != _ksColumn && _ksColumn != null)
        {
            _ksSay.Orphan();
            _ksSay.HorizontalExpand = true;
            _ksColumn.AddChild(_ksSay);
        }

        _ksSay.Visible = KsTguiActive;
    }

    private void KsSetSettingsOpen(bool open)
    {
        _ksSettingsOpen = open;
        _ksHeader.SettingsOpen = open;
        _ksSettings.Visible = KsTguiActive && open;
    }

    private void KsOnAddPage()
    {
        if (_ksPages.Add(Loc.GetString("ks-tgui-chat-new-page")) == null)
            return;

        KsSetSettingsOpen(true);
        _ksSettings.ShowPagesSection();
    }

    /// <summary>
    ///     The open page, or its channels, changed: what is shown has to be drawn again.
    /// </summary>
    private void KsOnFilterChanged()
    {
        if (KsTguiActive)
            Repopulate();
    }

    /// <summary>
    ///     Whether a message of this channel is drawn here. The tgui panel asks the open page; the stock chat the filter popup.
    /// </summary>
    private bool KsShows(ChatChannel channel)
    {
        return KsTguiActive ? _ksPages.Current.Accepts(channel) : ChatInput.FilterButton.Popup.IsActive(channel);
    }

    /// <summary>
    ///     Whether a message of this channel counts as seen, which is what lets its sound play. On tgui any page showing it is
    ///         enough, as the player may be looking at another.
    /// </summary>
    private bool KsCountsAsRead(ChatChannel channel)
    {
        return KsTguiActive ? _ksPages.AcceptsOnAnyPage(channel) : ChatInput.FilterButton.Popup.IsActive(channel);
    }

    /// <summary>
    ///     The message at the configured font size. 12 is the font every message is drawn with already.
    /// </summary>
    private string KsSized(string message)
    {
        if (!KsTguiActive)
            return message;

        var size = _ksConfig.GetCVar(KsCCVars.TguiChatFontSize);
        return size == 12 ? message : $"[font size={size}]{message}[/font]";
    }

    /// <summary>
    ///     Shows the channel the next message would go to on the say bar's button: the prefix typed if there is one, else the
    ///         selected channel.
    /// </summary>
    public void KsRefreshSayChannel()
    {
        var (prefixChannel, _, radioChannel) = _controller.SplitInputContents(ChatInput.Input.Text.ToLower());
        var channel = prefixChannel == ChatSelectChannel.None ? SelectedChannel : prefixChannel;

        var label = radioChannel != null
            ? Loc.GetString(radioChannel.Name)
            : ChannelSelectorButton.ChannelSelectorName(channel);

        _ksSay.SetChannel(label, KsTguiSayPalette.ForChannel(channel, radioChannel?.Color));
    }

    private void KsOnSayTextEdited(string text)
    {
        var maxLength = _controller.MaxMessageLength;
        if (text.Length > maxLength)
        {
            text = text[..maxLength];
            _ksSay.SetText(text);
        }

        // The stock input is what the controller reads, and its change event is what drives the typing indicator.
        ChatInput.Input.SetText(text, invokeEvent: true);
        KsRefreshSayChannel();
    }

    private void KsOnSaySubmitted(string text)
    {
        if (!string.IsNullOrWhiteSpace(text))
            _ksSay.AddToHistory(text);

        ChatInput.Input.SetText(text);
        _controller.SendMessage(this, SelectedChannel);

        KsCloseSay();
    }

    private void KsOnSayEscaped()
    {
        ChatInput.Input.Clear();
        KsCloseSay();
    }

    private void KsCloseSay()
    {
        _ksSay.Reset();
        _ksSay.ReleaseInput();
        _ksSayWindow.Close();
        _controller.UpdateSelectedChannel(this);
        KsRefreshSayChannel();
    }

    /// <summary>
    ///     The chat-focus keybinds: opens the floating window if there is one, and puts the cursor in the say bar.
    /// </summary>
    private void KsTguiFocus(ChatSelectChannel? channel)
    {
        if (channel != null)
            ChatInput.ChannelSelector.Select(channel.Value);

        KsRefreshSayChannel();
        if (KsSayFloating)
            _ksSayWindow.Open();

        _ksSay.FocusInput();
    }
}
