using System.Text;
using Content.Shared._KS14.CCVar;
using Content.Shared.Chat;
using Robust.Shared.Configuration;

namespace Content.Client._KS14.Tgui.Chat;

/// <summary>
///     One tab of the tgui chat panel: a name and the channels it shows.
/// </summary>
public sealed class KsTguiChatPage
{
    public string Id = string.Empty;
    public string Name = string.Empty;

    /// <summary>
    ///     The channels this page shows, besides <see cref="KsTguiChatPages.AlwaysShown"/>.
    /// </summary>
    public ChatChannel Channels;

    /// <summary>
    ///     tgui's "Mute": no unread count on the tab.
    /// </summary>
    public bool HideUnread;

    public bool IsMain;

    /// <summary>
    ///     Messages that arrived while another page was shown.
    /// </summary>
    public int Unread;

    public bool Accepts(ChatChannel channel)
    {
        return ((Channels | KsTguiChatPages.AlwaysShown) & channel) != 0;
    }
}

/// <summary>
///     The tgui chat panel's pages, port of <c>use-chat-pages.ts</c>: add, rename, remove and reorder pages, which channels
///         each shows, and the unread count of the ones not being looked at. Kept in a cvar. Plain data, shared by every chat
///         box, so that a game chat and the lobby's agree on the page that is open.
/// </summary>
public sealed class KsTguiChatPages
{
    /// <summary>
    ///     tgui's "System Messages, always enabled".
    /// </summary>
    public const ChatChannel AlwaysShown = ChatChannel.Server;

    public const string MainId = "main";

    /// <summary>
    ///     tgui's UnreadCount shows at most 99.
    /// </summary>
    public const int UnreadCap = 99;

    private const char PageSeparator = ';';
    private const char FieldSeparator = ',';
    private const int MaxPages = 12;
    private const int MaxNameLength = 20;

    private readonly IConfigurationManager _configurationManager;
    private readonly List<KsTguiChatPage> _pages = new();

    private int _nextId;

    public KsTguiChatPages(IConfigurationManager configurationManager)
    {
        _configurationManager = configurationManager;
        Load();
    }

    public IReadOnlyList<KsTguiChatPage> Pages => _pages;

    public KsTguiChatPage Current { get; private set; } = default!;

    /// <summary>
    ///     Unread is counted only while the tgui panel is the one showing.
    /// </summary>
    public bool Counting { get; set; }

    /// <summary>
    ///     The list of pages, a name or an unread count changed.
    /// </summary>
    public event Action? Changed;

    /// <summary>
    ///     Which messages the open page shows changed: another page opened, or its channels did.
    /// </summary>
    public event Action? FilterChanged;

    public KsTguiChatPage? Find(string id)
    {
        foreach (var page in _pages)
        {
            if (page.Id == id)
                return page;
        }

        return null;
    }

    public bool AcceptsOnAnyPage(ChatChannel channel)
    {
        foreach (var page in _pages)
        {
            if (page.Accepts(channel))
                return true;
        }

        return false;
    }

    /// <summary>
    ///     Counts a message towards the pages that show it but aren't open.
    /// </summary>
    public void CountUnread(ChatChannel channel)
    {
        if (!Counting)
            return;

        var changed = false;
        foreach (var page in _pages)
        {
            if (page == Current || !page.Accepts(channel))
                continue;

            page.Unread++;
            changed |= !page.HideUnread;
        }

        if (changed)
            Changed?.Invoke();
    }

    public void Select(KsTguiChatPage page)
    {
        if (!_pages.Contains(page))
            return;

        page.Unread = 0;
        var switched = Current != page;
        Current = page;
        Changed?.Invoke();
        if (switched)
            FilterChanged?.Invoke();
    }

    public KsTguiChatPage? Add(string name)
    {
        if (_pages.Count >= MaxPages)
            return null;

        var page = new KsTguiChatPage
        {
            Id = NewId(),
            Name = CleanName(name),
            Channels = ChatChannel.None,
        };

        _pages.Add(page);
        Save();
        Select(page);
        return page;
    }

    public void Rename(KsTguiChatPage page, string name)
    {
        var cleaned = CleanName(name);
        if (cleaned.Length == 0 || cleaned == page.Name)
            return;

        page.Name = cleaned;
        Save();
        Changed?.Invoke();
    }

    public void SetHideUnread(KsTguiChatPage page, bool hide)
    {
        page.HideUnread = hide;
        Save();
        Changed?.Invoke();
    }

    public void ToggleChannel(KsTguiChatPage page, ChatChannel channel)
    {
        page.Channels ^= channel;
        Save();
        if (page == Current)
            FilterChanged?.Invoke();
    }

    /// <summary>
    ///     Removes a page, never the main one, and opens the first.
    /// </summary>
    public void Remove(KsTguiChatPage page)
    {
        if (page.IsMain)
            return;

        var wasCurrent = Current == page;
        _pages.Remove(page);
        Save();
        if (wasCurrent)
            Select(_pages[0]);
        else
            Changed?.Invoke();
    }

    /// <summary>
    ///     tgui never moves the main page, nor anything in front of it.
    /// </summary>
    public bool CanMove(KsTguiChatPage page, int direction)
    {
        var from = _pages.IndexOf(page);
        var to = from + direction;
        return from > 0 && to > 0 && to < _pages.Count;
    }

    public void Move(KsTguiChatPage page, int direction)
    {
        if (!CanMove(page, direction))
            return;

        var from = _pages.IndexOf(page);
        (_pages[from], _pages[from + direction]) = (_pages[from + direction], _pages[from]);
        Save();
        Changed?.Invoke();
    }

    public void Save()
    {
        _configurationManager.SetCVar(KsCCVars.TguiChatPages, Serialize(_pages));
    }

    /// <summary>
    ///     Pages as <c>id,channels,hideUnread,name</c>, joined by <c>;</c>. The name is last, so it may hold a comma.
    /// </summary>
    public static string Serialize(IReadOnlyList<KsTguiChatPage> pages)
    {
        var builder = new StringBuilder();
        foreach (var page in pages)
        {
            if (builder.Length > 0)
                builder.Append(PageSeparator);

            builder.Append(page.Id).Append(FieldSeparator)
                .Append((int)page.Channels).Append(FieldSeparator)
                .Append(page.HideUnread ? '1' : '0').Append(FieldSeparator)
                .Append(page.Name);
        }

        return builder.ToString();
    }

    public static List<KsTguiChatPage> Deserialize(string raw)
    {
        var pages = new List<KsTguiChatPage>();
        foreach (var entry in raw.Split(PageSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = entry.Split(FieldSeparator, 4);
            if (fields.Length < 4 || fields[0].Length == 0 || !int.TryParse(fields[1], out var channels))
                continue;

            var duplicate = false;
            foreach (var existing in pages)
                duplicate |= existing.Id == fields[0];

            if (duplicate)
                continue;

            pages.Add(new KsTguiChatPage
            {
                Id = fields[0],
                Channels = (ChatChannel)channels,
                HideUnread = fields[2] == "1",
                Name = CleanName(fields[3]),
            });
        }

        return pages;
    }

    private void Load()
    {
        _pages.Clear();
        _pages.AddRange(Deserialize(_configurationManager.GetCVar(KsCCVars.TguiChatPages)));

        // The main page is always first, and shows everything until told otherwise.
        var main = Find(MainId);
        if (main == null)
        {
            main = new KsTguiChatPage { Id = MainId, Name = Loc.GetString("ks-tgui-chat-main-page"), Channels = (ChatChannel)ushort.MaxValue };
            _pages.Insert(0, main);
        }
        else
        {
            _pages.Remove(main);
            _pages.Insert(0, main);
        }

        main.IsMain = true;
        if (main.Name.Length == 0)
            main.Name = Loc.GetString("ks-tgui-chat-main-page");

        Current = main;
    }

    private string NewId()
    {
        string id;
        do
        {
            id = "page" + _nextId++;
        } while (Find(id) != null);

        return id;
    }

    /// <summary>
    ///     The separators can't live in a name, and a very long one would crowd the tabs.
    /// </summary>
    public static string CleanName(string name)
    {
        var cleaned = name.Replace(PageSeparator, ' ').Replace(FieldSeparator, ' ').Replace('\n', ' ').Trim();
        return cleaned.Length > MaxNameLength ? cleaned[..MaxNameLength] : cleaned;
    }
}
