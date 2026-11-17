using System.Numerics;
using Content.Client.UserInterface;
using Content.Client.UserInterface.Systems.Chat;
using Content.Client.UserInterface.Systems.Chat.Controls;
using Content.Shared.Chat;
using Content.Shared.Input;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Input;
using Robust.Shared.Maths;

namespace Content.Client._KS14.Tgui.Chat;

/// <summary>
///     The say bar's channel menu: a tgui dropdown listing every channel the player can talk in, each with its name in the
///         channel's colour at the left and, at the right, its chat prefix and the key that focuses the bar on it. It is the
///         popup an <see cref="OptionButton"/> makes, so <see cref="KsTguiFeelUIController"/> styles, places and eases it as
///         it does any dropdown, and tags the rows it gains while open.
/// </summary>
public sealed class KsTguiChannelMenu : Popup, IKsTguiDropdownAnchored
{
    /// <summary>
    ///     The key that focuses the bar on a channel, for the column at the right.
    /// </summary>
    private static readonly Dictionary<ChatSelectChannel, BoundKeyFunction> FocusKeys = new()
    {
        { ChatSelectChannel.Local, ContentKeyFunctions.FocusLocalChat },
        { ChatSelectChannel.Whisper, ContentKeyFunctions.FocusWhisperChat },
        { ChatSelectChannel.Emotes, ContentKeyFunctions.FocusEmote },
        { ChatSelectChannel.Radio, ContentKeyFunctions.FocusRadio },
        { ChatSelectChannel.LOOC, ContentKeyFunctions.FocusLOOC },
        { ChatSelectChannel.OOC, ContentKeyFunctions.FocusOOC },
        { ChatSelectChannel.Dead, ContentKeyFunctions.FocusDeadChat },
        { ChatSelectChannel.Admin, ContentKeyFunctions.FocusAdminChat },
    };

    /// <summary>
    ///     Between a row's name and its key and prefix.
    /// </summary>
    private const float ColumnGap = 18f;

    private readonly ChatUIController _chatController;
    private readonly ScrollContainer _scroll;
    private readonly BoxContainer _rows;
    private readonly Dictionary<ChatSelectChannel, Button> _buttons = new();
    private Action<ChatSelectChannel>? _channelsChangedHandler;
    private ChatSelectChannel _current;

    public KsTguiChannelMenu()
    {
        _chatController = UserInterfaceManager.GetUIController<ChatUIController>();

        // The same pieces an option button's popup has, which is what the dropdown rules and the controller look for.
        _rows = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical };
        _scroll = new ScrollContainer { ReturnMeasure = true, MaxHeight = 300f };
        _scroll.AddChild(_rows);

        AddChild(new PanelContainer { StyleClasses = { OptionButton.StyleClassOptionsBackground } });
        AddChild(_scroll);
        StyleClasses.Add(OptionButton.StyleClassPopup);
    }

    public Control? DropdownAnchor { get; private set; }

    /// <summary>
    ///     A row was picked.
    /// </summary>
    public event Action<ChatSelectChannel>? Selected;

    /// <summary>
    ///     The rows, in the order the stock selector lists them, for tests.
    /// </summary>
    public IReadOnlyDictionary<ChatSelectChannel, Button> Rows => _buttons;

    /// <summary>
    ///     Opens the menu under <paramref name="anchor"/>, or above it where there is no room, with <paramref name="current"/>
    ///         marked.
    /// </summary>
    public void OpenBeside(Control anchor, ChatSelectChannel current)
    {
        if (anchor.Root == null)
            return;

        _current = current;
        Rebuild(_chatController.SelectableChannels);

        _channelsChangedHandler ??= channels => Rebuild(channels);
        _chatController.SelectableChannelsChanged -= _channelsChangedHandler;
        _chatController.SelectableChannelsChanged += _channelsChangedHandler;

        DropdownAnchor = anchor;

        var globalPosition = anchor.GlobalPosition;
        globalPosition.Y += anchor.Size.Y;
        _scroll.Measure(anchor.Window?.Size ?? Vector2Helpers.Infinity);
        var (minimumX, minimumY) = _scroll.DesiredSize;
        var box = UIBox2.FromDimensions(globalPosition, new Vector2(MathF.Max(minimumX, anchor.Width), minimumY));

        anchor.Root.ModalRoot.AddChild(this);
        Open(box);
    }

    protected override void ModalRemoved()
    {
        base.ModalRemoved();

        if (_channelsChangedHandler != null)
            _chatController.SelectableChannelsChanged -= _channelsChangedHandler;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && _channelsChangedHandler != null)
            _chatController.SelectableChannelsChanged -= _channelsChangedHandler;

        base.Dispose(disposing);
    }

    private void Rebuild(ChatSelectChannel channels)
    {
        _rows.DisposeAllChildren();
        _buttons.Clear();

        foreach (var channel in ChannelSelectorPopup.ChannelSelectorOrder)
        {
            if ((channels & channel) == 0)
                continue;

            var row = MakeRow(channel);
            _buttons.Add(channel, row);
            _rows.AddChild(row);
        }
    }

    private Button MakeRow(ChatSelectChannel channel)
    {
        var button = new Button { ToggleMode = true, Pressed = channel == _current };
        var contents = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, MouseFilter = MouseFilterMode.Ignore };

        var name = new Label
        {
            Text = ChannelSelectorButton.ChannelSelectorName(channel),
            FontColorOverride = KsTguiSayPalette.ForChannel(channel),
            HorizontalExpand = true,
            MouseFilter = MouseFilterMode.Ignore,
        };

        var shortcuts = new Label
        {
            Text = ShortcutsText(channel),
            FontColorOverride = KsTguiPalettes.Label,
            Margin = new Thickness(ColumnGap, 0f, 0f, 0f),
            MouseFilter = MouseFilterMode.Ignore,
        };

        contents.AddChild(name);
        contents.AddChild(shortcuts);
        button.AddChild(contents);

        button.OnPressed += _ =>
        {
            Close();
            Selected?.Invoke(channel);
        };

        return button;
    }

    /// <summary>
    ///     The prefix typed at the start of a message, then the key bound to the channel; either is left out where there is none.
    /// </summary>
    public static string ShortcutsText(ChatSelectChannel channel)
    {
        var text = string.Empty;
        if (ChatUIController.ChannelPrefixes.TryGetValue(channel, out var prefix) && prefix != default)
            text = prefix.ToString();

        if (FocusKeys.TryGetValue(channel, out var function) && BoundKeyHelper.TryGetShortKeyName(function, out var key))
            text = text.Length == 0 ? key : $"{text}  {key}";

        return text;
    }
}
