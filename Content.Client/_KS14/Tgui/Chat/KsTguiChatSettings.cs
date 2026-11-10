using Content.Client._KS14.Tgui.Controls;
using Content.Client.UserInterface.Systems.Chat;
using Content.Shared._KS14.CCVar;
using Content.Shared.CCVar;
using Content.Shared.Chat;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Configuration;
using Robust.Shared.Utility;

namespace Content.Client._KS14.Tgui.Chat;

/// <summary>
///     The settings panel that the cog opens (<c>SettingsPanel.tsx</c>), with the three pages of it that mean something in
///         SS14: General (message font size), Chat Tabs (<c>ChatPageSettings.tsx</c>: name, order, mute, remove, and which
///         channels the page shows) and Text Highlight. It replaces the channel filter popup while tgui is on.
/// </summary>
public sealed partial class KsTguiChatSettings : Control
{
    /// <summary>
    ///     On the settings' inputs, for the sheetlet's 12px font rule.
    /// </summary>
    public const string StyleClassInput = "KsTguiChatSettingsInput";

    public const int MinFontSize = 8;
    public const int MaxFontSize = 32;

    // The order the stock filter popup lists channels in. Server is tgui's always-on "System Messages", so has no toggle.
    private static readonly ChatChannel[] ChannelOrder =
    [
        ChatChannel.Local,
        ChatChannel.Whisper,
        ChatChannel.Emotes,
        ChatChannel.Radio,
        ChatChannel.Notifications,
        ChatChannel.LOOC,
        ChatChannel.OOC,
        ChatChannel.Dead,
    ];

    private static readonly ChatChannel[] AdminChannelOrder =
    [
        ChatChannel.Admin,
        ChatChannel.AdminAlert,
        ChatChannel.AdminChat,
    ];

    [Dependency] private IConfigurationManager _configurationManager = default!;

    private readonly KsTguiChatPages _pages;
    private readonly ChatUIController _controller;
    private readonly KsTguiTabs _sectionTabs;
    private readonly KsTguiTabItem _pagesTab;
    private readonly Control[] _sectionBodies;

    private readonly Label _fontSizeLabel;

    private readonly LineEdit _nameEdit;
    private readonly KsTguiButton _moveLeftButton;
    private readonly KsTguiButton _moveRightButton;
    private readonly KsTguiButton _muteButton;
    private readonly KsTguiButton _removeButton;
    private readonly BoxContainer _channelBox;
    private readonly BoxContainer _adminChannelBox;
    private readonly KsTguiButton _adminToggle;
    private readonly Dictionary<ChatChannel, KsTguiButton> _channelButtons = new();

    private readonly TextEdit _highlightEdit;

    private bool _refreshing;

    public KsTguiChatSettings(KsTguiChatPages pages, ChatUIController controller)
    {
        IoCManager.InjectDependencies(this);
        _pages = pages;
        _controller = controller;

        _sectionTabs = new KsTguiTabs { Vertical = true };
        _sectionTabs.AddTab(Loc.GetString("ks-tgui-chat-settings-general"), selected: true);
        _pagesTab = _sectionTabs.AddTab(Loc.GetString("ks-tgui-chat-settings-pages"));
        _sectionTabs.AddTab(Loc.GetString("ks-tgui-chat-settings-highlights"));
        _sectionTabs.OnTabSelected += _ => ShowSection();

        // General.
        var general = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical };
        var list = new KsTguiLabeledList();
        var fontRow = list.AddItem(Loc.GetString("ks-tgui-chat-settings-font-size"));
        var fontButtons = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 4 };
        var smaller = new KsTguiButton { Icon = KsTguiIcons.Minus, Compact = true };
        smaller.OnPressed += _ => ChangeFontSize(-1);
        var font = IoCManager.Resolve<KsTguiFonts>().GetFont(12);
        _fontSizeLabel = new Label { FontOverride = font, MinWidth = 36f, Align = Label.AlignMode.Center, VerticalAlignment = VAlignment.Center };
        var larger = new KsTguiButton { Icon = KsTguiIcons.Plus, Compact = true };
        larger.OnPressed += _ => ChangeFontSize(1);
        fontButtons.AddChild(smaller);
        fontButtons.AddChild(_fontSizeLabel);
        fontButtons.AddChild(larger);
        fontRow.XamlChildren.Add(fontButtons);
        general.AddChild(list);

        // Chat tabs.
        var pagesBody = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 6 };
        var topRow = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 4 };
        _moveLeftButton = new KsTguiButton { Icon = KsTguiChatGlyphs.Left, Color = KsTguiColor.Blue, ToolTip = Loc.GetString("ks-tgui-chat-move-left") };
        _moveLeftButton.OnPressed += _ => MoveCurrent(-1);
        _nameEdit = new LineEdit { HorizontalExpand = true, MinWidth = 80f, StyleClasses = { StyleClassInput } };
        _nameEdit.OnTextEntered += _ => CommitName();
        _nameEdit.OnFocusExit += _ => CommitName();
        _moveRightButton = new KsTguiButton { Icon = KsTguiChatGlyphs.Right, Color = KsTguiColor.Blue, ToolTip = Loc.GetString("ks-tgui-chat-move-right") };
        _moveRightButton.OnPressed += _ => MoveCurrent(1);
        _muteButton = new KsTguiButton { Text = Loc.GetString("ks-tgui-chat-mute"), ToolTip = Loc.GetString("ks-tgui-chat-mute-tooltip") };
        _muteButton.OnPressed += _ => ToggleMute();
        _removeButton = new KsTguiButton { Icon = KsTguiIcons.Times, Text = Loc.GetString("ks-tgui-chat-remove"), Color = KsTguiColor.Red };
        _removeButton.OnPressed += _ => RemoveCurrent();
        topRow.AddChild(_moveLeftButton);
        topRow.AddChild(_nameEdit);
        topRow.AddChild(_moveRightButton);
        topRow.AddChild(_muteButton);
        topRow.AddChild(_removeButton);
        pagesBody.AddChild(topRow);

        var channelsSection = new KsTguiSection { Title = Loc.GetString("ks-tgui-chat-messages-to-display") };
        _channelBox = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 2 };
        _adminToggle = new KsTguiButton { Icon = KsTguiIcons.AngleRight, Text = Loc.GetString("ks-tgui-chat-admin-stuff"), Color = KsTguiColor.Transparent };
        _adminToggle.OnPressed += _ => ToggleAdminChannels();
        _adminChannelBox = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 2, Visible = false };
        channelsSection.XamlChildren.Add(_channelBox);
        channelsSection.XamlChildren.Add(_adminToggle);
        channelsSection.XamlChildren.Add(_adminChannelBox);
        pagesBody.AddChild(channelsSection);

        // Text highlight.
        var highlights = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 6 };
        highlights.AddChild(new Label { Text = Loc.GetString("hud-chatbox-highlights"), FontOverride = font });
        _highlightEdit = new TextEdit { MinHeight = 90f, VerticalExpand = true, StyleClasses = { StyleClassInput } };
        _highlightEdit.Placeholder = new Rope.Leaf(Loc.GetString("hud-chatbox-highlights-placeholder"));
        highlights.AddChild(_highlightEdit);
        var submit = new KsTguiButton { Text = Loc.GetString("hud-chatbox-highlights-button"), ToolTip = Loc.GetString("hud-chatbox-highlights-tooltip") };
        submit.OnPressed += _ => _controller.UpdateHighlights(Rope.Collapse(_highlightEdit.TextRope));
        highlights.AddChild(submit);

        _sectionBodies = [general, pagesBody, highlights];

        var content = new Control { HorizontalExpand = true, VerticalExpand = true };
        var scroll = new ScrollContainer { HScrollEnabled = false, HorizontalExpand = true, VerticalExpand = true };
        var bodies = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, HorizontalExpand = true };
        foreach (var body in _sectionBodies)
            bodies.AddChild(body);

        scroll.AddChild(bodies);
        content.AddChild(scroll);

        var row = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 8, VerticalExpand = true };
        row.AddChild(_sectionTabs);
        row.AddChild(content);

        var section = new KsTguiSection { Fill = true };
        section.XamlChildren.Add(row);
        AddChild(section);

        _highlightEdit.TextRope = new Rope.Leaf(_configurationManager.GetCVar(CCVars.ChatHighlights));
        RebuildChannels();
        ShowSection();
        Refresh();
    }

    /// <summary>
    ///     Opens the Chat Tabs page, as adding a page does in tgui.
    /// </summary>
    public void ShowPagesSection()
    {
        _sectionTabs.Select(_pagesTab);
        ShowSection();
    }

    protected override void EnteredTree()
    {
        base.EnteredTree();

        _pages.Changed += Refresh;
        _controller.FilterableChannelsChanged += OnFilterableChannelsChanged;
        _controller.HighlightsUpdated += OnHighlightsUpdated;
        RebuildChannels();
        Refresh();
    }

    protected override void ExitedTree()
    {
        base.ExitedTree();

        _pages.Changed -= Refresh;
        _controller.FilterableChannelsChanged -= OnFilterableChannelsChanged;
        _controller.HighlightsUpdated -= OnHighlightsUpdated;
    }

    private void OnHighlightsUpdated(string highlights)
    {
        _highlightEdit.TextRope = new Rope.Leaf(highlights);
    }

    private void OnFilterableChannelsChanged(ChatChannel channels)
    {
        RebuildChannels();
        Refresh();
    }

    private void ShowSection()
    {
        var shown = Math.Max(0, _sectionTabs.SelectedIndex);
        for (var index = 0; index < _sectionBodies.Length; index++)
            _sectionBodies[index].Visible = index == shown;
    }

    private void ChangeFontSize(int step)
    {
        var size = Math.Clamp(_configurationManager.GetCVar(KsCCVars.TguiChatFontSize) + step, MinFontSize, MaxFontSize);
        _configurationManager.SetCVar(KsCCVars.TguiChatFontSize, size);
        _fontSizeLabel.Text = Loc.GetString("ks-tgui-chat-font-size-value", ("size", size));
        OnFontSizeChanged?.Invoke();
    }

    /// <summary>
    ///     The message font size was changed, so what is on screen should be laid out again.
    /// </summary>
    public event Action? OnFontSizeChanged;

    private void CommitName()
    {
        if (_refreshing)
            return;

        _pages.Rename(_pages.Current, _nameEdit.Text);
    }

    private void MoveCurrent(int direction)
    {
        _pages.Move(_pages.Current, direction);
    }

    private void ToggleMute()
    {
        _pages.SetHideUnread(_pages.Current, !_pages.Current.HideUnread);
    }

    private void RemoveCurrent()
    {
        _pages.Remove(_pages.Current);
    }

    private void ToggleAdminChannels()
    {
        _adminChannelBox.Visible = !_adminChannelBox.Visible;
        _adminToggle.Icon = _adminChannelBox.Visible ? KsTguiIcons.AngleDown : KsTguiIcons.AngleRight;
    }

    /// <summary>
    ///     One checkbox for each channel the player can see, the admin ones behind the "Admin stuff" fold.
    /// </summary>
    private void RebuildChannels()
    {
        _channelBox.RemoveAllChildren();
        _adminChannelBox.RemoveAllChildren();
        _channelButtons.Clear();

        var filterable = _controller.FilterableChannels;
        foreach (var channel in ChannelOrder)
            AddChannel(_channelBox, channel, filterable);

        foreach (var channel in AdminChannelOrder)
            AddChannel(_adminChannelBox, channel, filterable);

        _adminToggle.Visible = _adminChannelBox.ChildCount > 0;
        if (!_adminToggle.Visible)
            _adminChannelBox.Visible = false;
    }

    private void AddChannel(BoxContainer box, ChatChannel channel, ChatChannel filterable)
    {
        if ((filterable & channel) == 0)
            return;

        var button = new KsTguiButton
        {
            Text = Loc.GetString($"hud-chatbox-channel-{channel}"),
            Color = KsTguiColor.Transparent,
        };
        button.OnPressed += _ => _pages.ToggleChannel(_pages.Current, channel);
        _channelButtons[channel] = button;
        box.AddChild(button);
    }

    /// <summary>
    ///     Shows the open page's settings.
    /// </summary>
    public void Refresh()
    {
        _refreshing = true;

        var page = _pages.Current;
        if (_nameEdit.Text != page.Name && !_nameEdit.HasKeyboardFocus())
            _nameEdit.Text = page.Name;

        _moveLeftButton.Visible = !page.IsMain;
        _moveRightButton.Visible = !page.IsMain;
        _moveLeftButton.Disabled = !_pages.CanMove(page, -1);
        _moveRightButton.Disabled = !_pages.CanMove(page, 1);
        _removeButton.Visible = !page.IsMain;

        _muteButton.Icon = page.HideUnread ? KsTguiChatGlyphs.BellSlash : KsTguiChatGlyphs.Bell;
        _muteButton.Selected = page.HideUnread;

        foreach (var (channel, button) in _channelButtons)
        {
            var shown = (page.Channels & channel) != 0;
            button.Icon = shown ? KsTguiIcons.CheckSquare : KsTguiIcons.Square;
            button.Selected = false;
        }

        _fontSizeLabel.Text = Loc.GetString("ks-tgui-chat-font-size-value", ("size", _configurationManager.GetCVar(KsCCVars.TguiChatFontSize)));

        _refreshing = false;
    }
}

/// <summary>
///     Glyphs the control library hasn't got (Font Awesome 6 Solid codepoints).
/// </summary>
public static class KsTguiChatGlyphs
{
    public const string Left = "";
    public const string Right = "";
    public const string Bell = "";
    public const string BellSlash = "";
}
