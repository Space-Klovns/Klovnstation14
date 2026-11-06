using Content.Client._KS14.Tgui.Controls;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Network;
using Robust.Shared.Timing;

namespace Content.Client._KS14.Tgui.Chat;

/// <summary>
///     The bar across the top of tgui-panel (<c>Panel.tsx</c>, <c>ChatTabs.tsx</c>): a tab for each chat page with its unread
///         count, a plus that adds a page, the connection's ping and the settings cog. tgui's music button has no SS14
///         counterpart and is left out.
/// </summary>
public sealed partial class KsTguiChatHeader : PanelContainer
{
    /// <summary>
    ///     How often the ping label is read.
    /// </summary>
    /// <summary>
    ///     On the message output, for the sheetlet's font rule.
    /// </summary>
    public const string StyleClassOutput = "KsTguiChatOutput";

    private static readonly TimeSpan PingInterval = TimeSpan.FromSeconds(1);

    [Dependency] private IClientNetManager _netManager = default!;
    [Dependency] private IGameTiming _gameTiming = default!;

    private readonly KsTguiChatPages _pages;
    private readonly KsTguiTabs _tabs;
    private readonly KsTguiButton _addButton;
    private readonly KsTguiButton _settingsButton;
    private readonly Label _pingLabel;
    private readonly List<(KsTguiTabItem Item, KsTguiChatPage Page)> _items = new();

    private TimeSpan _nextPing;
    private bool _settingsOpen;

    public KsTguiChatHeader(KsTguiChatPages pages)
    {
        IoCManager.InjectDependencies(this);
        _pages = pages;

        // tgui: a fitted Section, so its darkened body and nothing else.
        PanelOverride = new StyleBoxFlat(KsTguiPalettes.Section);
        ModulateSelfOverride = Color.White;

        _tabs = new KsTguiTabs();
        _tabs.AutoSelect = false;
        _tabs.OnTabSelected += OnTabSelected;

        var scroll = new ScrollContainer
        {
            HScrollEnabled = true,
            VScrollEnabled = false,
            HorizontalExpand = true,
            ReturnMeasure = true,
        };
        scroll.AddChild(_tabs);

        _addButton = new KsTguiButton { Icon = KsTguiIcons.Plus, Color = KsTguiColor.Transparent, ToolTip = Loc.GetString("ks-tgui-chat-add-page") };
        _addButton.OnPressed += _ => OnAddPressed?.Invoke();

        _pingLabel = new Label { FontOverride = IoCManager.Resolve<KsTguiFonts>().GetFont(12), Margin = new Thickness(6f, 0f), VerticalAlignment = VAlignment.Center };

        _settingsButton = new KsTguiButton { Icon = KsTguiIcons.Cog, ToolTip = Loc.GetString("ks-tgui-chat-open-settings") };
        _settingsButton.OnPressed += _ => OnSettingsPressed?.Invoke();

        var row = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, Margin = new Thickness(0f, 0f, 6f, 0f) };
        row.AddChild(scroll);
        row.AddChild(CenterVertically(_addButton));
        row.AddChild(_pingLabel);
        row.AddChild(CenterVertically(_settingsButton));
        AddChild(row);

        Refresh();
    }

    /// <summary>
    ///     The plus was pressed: the owner adds a page and opens the settings, as <c>ChatTabs</c> does.
    /// </summary>
    public event Action? OnAddPressed;

    public event Action? OnSettingsPressed;

    public bool SettingsOpen
    {
        get => _settingsOpen;
        set
        {
            _settingsOpen = value;
            _settingsButton.Selected = value;
            _settingsButton.Icon = value ? KsTguiIcons.Times : KsTguiIcons.Cog;
            _settingsButton.ToolTip = Loc.GetString(value ? "ks-tgui-chat-close-settings" : "ks-tgui-chat-open-settings");
        }
    }

    public int TabCount => _items.Count;

    public KsTguiTabItem TabFor(KsTguiChatPage page)
    {
        foreach (var (item, owner) in _items)
        {
            if (owner == page)
                return item;
        }

        throw new KeyNotFoundException(page.Id);
    }

    private static Control CenterVertically(Control control)
    {
        control.VerticalAlignment = VAlignment.Center;
        return control;
    }

    /// <summary>
    ///     Makes the tabs match the pages. The tabs are only rebuilt when the pages themselves changed: an unread count must not.
    /// </summary>
    public void Refresh()
    {
        var sameTabs = _items.Count == _pages.Pages.Count;
        for (var index = 0; sameTabs && index < _items.Count; index++)
            sameTabs = _items[index].Page == _pages.Pages[index];

        if (!sameTabs)
        {
            _tabs.RemoveAllChildren();
            _items.Clear();
            foreach (var page in _pages.Pages)
                _items.Add((_tabs.AddTab(page.Name), page));
        }

        foreach (var (item, page) in _items)
        {
            item.Text = page.Name;
            item.Selected = page == _pages.Current;

            // tgui: nothing on the open page, and "99" for anything more.
            item.Count = page.HideUnread || page.Unread <= 0 || page == _pages.Current
                ? null
                : Math.Min(page.Unread, KsTguiChatPages.UnreadCap).ToString();
        }
    }

    private void OnTabSelected(KsTguiTabItem selected)
    {
        foreach (var (item, page) in _items)
        {
            if (item == selected)
            {
                _pages.Select(page);
                return;
            }
        }
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        if (_gameTiming.RealTime < _nextPing)
            return;

        _nextPing = _gameTiming.RealTime + PingInterval;
        if (_netManager.ServerChannel is not { } channel)
        {
            _pingLabel.Text = null;
            return;
        }

        // PingIndicator: good under 60 ms, average under 150, bad beyond.
        var ping = channel.Ping;
        _pingLabel.Text = Loc.GetString("ks-tgui-chat-ping", ("ping", (int)ping));
        _pingLabel.FontColorOverride = ping < 60 ? KsTguiPalettes.Good : ping < 150 ? KsTguiPalettes.Average : KsTguiPalettes.Bad;
    }
}
