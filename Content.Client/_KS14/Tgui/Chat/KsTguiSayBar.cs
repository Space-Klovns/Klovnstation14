using System.Linq;
using System.Numerics;
using Content.Client.Stylesheets.Fonts;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Input;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client._KS14.Tgui.Chat;

/// <summary>
///     The shell of tgui-say, port of <c>TguiSay.tsx</c> and its scss: a window coloured for the channel, a black bar inside it,
///         the channel button at the left and the text at the right. Enter sends, Escape closes, Tab (the cycle keybind) changes channel and a
///         click on the button opens the menu of channels, Up and Down walk the last five messages, and it grows by a line at a time up
///         to three, as the browser's textarea did. It holds no chat logic of its own: <c>ChatBox</c> owns that and just
///         listens to the events.
/// </summary>
public sealed partial class KsTguiSayBar : PanelContainer
{
    /// <summary>
    ///     <c>WindowSize.Width</c>.
    /// </summary>
    public const float WindowWidth = 231f;

    /// <summary>
    ///     <c>WindowSize.Small</c>, <c>Medium</c> and <c>Large</c>: one, two, three lines.
    /// </summary>
    public const float SmallHeight = 30f;
    public const float MediumHeight = 50f;
    public const float LargeHeight = 70f;

    /// <summary>
    ///     On the text box, for the sheetlet's font rule.
    /// </summary>
    public const string StyleClassEdit = "KsTguiSayEdit";

    private const float WindowInset = 2f;
    private const float ButtonWidth = 48f;
    private const int HistoryLimit = 5;

    /// <summary>
    ///     How long after it takes focus the bar drops what it is typed, which is the key that opened it.
    /// </summary>
    private static readonly TimeSpan OpeningKeyWindow = TimeSpan.FromMilliseconds(120);

    [Dependency] private IGameTiming _gameTiming = default!;

    private readonly KsTguiStyleBox _windowBox = new(Color.White);
    private readonly SayButton _button;
    private readonly TextEdit _edit;
    private readonly List<string> _history = new();

    private int _historyIndex = -1;
    private string? _historyTemp;
    private string _channelLabel = string.Empty;
    private Color _channelColor = Color.White;
    private string _lastText = string.Empty;
    private string _textAtFocus = string.Empty;
    private TimeSpan _ignoreUntil;
    private bool _hadFocus;
    private bool _setting;

    // The engine's TextEdit sets the cursor after raising OnTextChanged, from where the insertion was. Setting the text from
    //      inside that event leaves the cursor past the end of the new text: no caret, and every key after it throws.
    //      So a change made while the event is being handled waits for the next frame.
    private bool _inEditChange;
    private bool _openingKeyPending;
    private string? _deferredText;
    private readonly VScrollBar _scrollBar;

    public KsTguiSayBar()
    {
        IoCManager.InjectDependencies(this);

        PanelOverride = _windowBox;
        ModulateSelfOverride = Color.White;
        MinHeight = SmallHeight;

        var content = new PanelContainer
        {
            PanelOverride = new StyleBoxFlat(KsTguiSayPalette.Content),
            ModulateSelfOverride = Color.White,
            Margin = new Thickness(WindowInset),
            HorizontalExpand = true,
            VerticalExpand = true,
        };

        _button = new SayButton { MinWidth = ButtonWidth };
        _button.OnPressed += _ => OnChannelClicked?.Invoke();

        _edit = new TextEdit
        {
            HorizontalExpand = true,
            VerticalExpand = true,
            Margin = new Thickness(5f, 1f, 0f, 0f),
            StyleClasses = { StyleClassEdit },
        };
        _edit.OnTextChanged += OnEditChanged;
        _edit.OnKeyBindDown += OnEditKeyBindDown;

        // The engine's TextEdit always has a bar, and its minimum size alone is taller than the whole line, so the bar is
        //      only there while the text overflows the three lines the window grows to.
        _scrollBar = _edit.Children.OfType<VScrollBar>().First();
        _scrollBar.AddStyleClass(KsTguiSheetlet.StyleClassThinScrollBar);
        _scrollBar.ReservesSpace = false; // a hidden bar still takes its minimum size otherwise
        _scrollBar.Visible = false;

        var row = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            Margin = new Thickness(2f),
            SeparationOverride = 0,
        };
        row.AddChild(_button);
        row.AddChild(_edit);
        content.AddChild(row);
        AddChild(content);

        RefreshLook();
    }

    /// <summary>
    ///     The text was changed by the player (not by <see cref="SetText"/>).
    /// </summary>
    public event Action<string>? OnTextEdited;

    /// <summary>
    ///     Enter was pressed with something typed.
    /// </summary>
    public event Action<string>? OnSubmitted;

    /// <summary>
    ///     Escape was pressed.
    /// </summary>
    public event Action? OnEscaped;

    /// <summary>
    ///     The player asked for the next (true) or previous channel, by key.
    /// </summary>
    public event Action<bool>? OnCycled;

    /// <summary>
    ///     The channel button was clicked.
    /// </summary>
    public event Action? OnChannelClicked;

    public event Action<bool>? OnFocusChanged;

    public string Text => _deferredText ?? Rope.Collapse(_edit.TextRope);

    /// <summary>
    ///     Whether the scroll bar is shown, which is only while the text has more lines than the bar can grow to.
    /// </summary>
    public bool ScrollBarShown => _scrollBar.Visible;

    /// <summary>
    ///     The text box inside, for tests.
    /// </summary>
    public TextEdit Edit => _edit;

    public bool HasInputFocus => _edit.HasKeyboardFocus();

    public int HistoryCount => _history.Count;

    /// <summary>
    ///     The number the button shows while browsing the history, or 0 when it isn't.
    /// </summary>
    public int HistoryPosition => _historyIndex + 1;

    /// <summary>
    ///     Height the bar wants for the lines of text it holds.
    /// </summary>
    public float WantedHeight { get; private set; } = SmallHeight;

    /// <summary>
    ///     The channel button's text.
    /// </summary>
    public string ChannelLabel => _channelLabel;

    /// <summary>
    ///     The channel button, which a menu of channels opens from.
    /// </summary>
    public Control ChannelButton => _button;

    public void SetChannel(string label, Color color)
    {
        _channelLabel = label;
        _channelColor = color;
        RefreshLook();
    }

    /// <summary>
    ///     Replaces the text. <paramref name="notify"/> raises <see cref="OnTextEdited"/> as if the player had typed it.
    /// </summary>
    public void SetText(string text, bool notify = false)
    {
        if (_inEditChange)
        {
            _deferredText = text;
            _lastText = text;
            if (notify)
                OnTextEdited?.Invoke(text);

            return;
        }

        _deferredText = null;
        _setting = true;
        _edit.TextRope = new Rope.Leaf(text);
        var end = new TextEdit.CursorPos(text.Length, TextEdit.LineBreakBias.Top);
        _edit.CursorPosition = end;
        _edit.SelectionStart = end;
        _setting = false;
        _lastText = text;

        if (notify)
            OnTextEdited?.Invoke(text);
    }

    /// <summary>
    ///     What Enter does.
    /// </summary>
    public void Submit()
    {
        ApplyDeferredText();
        OnSubmitted?.Invoke(Text);
    }

    /// <summary>
    ///     Takes keyboard focus, dropping the character of the key that opened the bar.
    /// </summary>
    public void FocusInput()
    {
        _textAtFocus = Text;
        _ignoreUntil = _gameTiming.RealTime + OpeningKeyWindow;
        _openingKeyPending = true;
        _edit.GrabKeyboardFocus();
    }

    public void ReleaseInput()
    {
        _edit.ReleaseKeyboardFocus();
    }

    /// <summary>
    ///     Empties the bar and leaves the history, as tgui-say's close does.
    /// </summary>
    public void Reset()
    {
        SetText(string.Empty);
        ResetHistoryPosition();
        RefreshLook();
    }

    public void AddToHistory(string text)
    {
        _history.Insert(0, text);
        if (_history.Count > HistoryLimit)
            _history.RemoveAt(_history.Count - 1);

        ResetHistoryPosition();
    }

    private void ResetHistoryPosition()
    {
        _historyIndex = -1;
        _historyTemp = null;
        RefreshLook();
    }

    private void OnEditChanged(TextEdit.TextEditEventArgs args)
    {
        if (_setting)
            return;

        _inEditChange = true;
        try
        {
            ChangeFromEdit(Rope.Collapse(args.TextRope));
        }
        finally
        {
            _inEditChange = false;
        }
    }

    private void ChangeFromEdit(string text)
    {
        // The key that opened the bar arrives as text a moment after the focus: a stock LineEdit has IgnoreNext for it.
        if (_openingKeyPending && _gameTiming.RealTime < _ignoreUntil && text.Length == _textAtFocus.Length + 1 && text.StartsWith(_textAtFocus))
        {
            _openingKeyPending = false;
            SetText(_textAtFocus);
            return;
        }

        if (text.Contains('\n'))
        {
            text = text.Replace("\n", string.Empty);
            SetText(text);
        }

        if (text == _lastText)
            return;

        _lastText = text;
        OnTextEdited?.Invoke(text);
    }

    /// <summary>
    ///     Puts in the text a change made during the engine's change event asked for, now that the event is over.
    /// </summary>
    private void ApplyDeferredText()
    {
        if (_deferredText is not { } text)
            return;

        _deferredText = null;
        SetText(text);
    }

    private void OnEditKeyBindDown(GUIBoundKeyEventArgs args)
    {
        var function = args.Function;
        if (function == EngineKeyFunctions.TextSubmit
            || function == EngineKeyFunctions.TextNewline
            || function == EngineKeyFunctions.MultilineTextSubmit)
        {
            args.Handle();
            Submit();
            return;
        }

        if (function == EngineKeyFunctions.TextReleaseFocus)
        {
            args.Handle();
            OnEscaped?.Invoke();
            return;
        }

        if (function == EngineKeyFunctions.TextHistoryPrev || function == EngineKeyFunctions.TextCursorUp)
        {
            args.Handle();
            BrowseHistory(older: true);
            return;
        }

        if (function == EngineKeyFunctions.TextHistoryNext || function == EngineKeyFunctions.TextCursorDown)
        {
            args.Handle();
            BrowseHistory(older: false);
            return;
        }

        if (function == Content.Shared.Input.ContentKeyFunctions.CycleChatChannelForward)
        {
            args.Handle();
            OnCycled?.Invoke(true);
            return;
        }

        if (function == Content.Shared.Input.ContentKeyFunctions.CycleChatChannelBackward)
        {
            args.Handle();
            OnCycled?.Invoke(false);
            return;
        }

        // tgui: editing a recalled message makes it the message, and the button goes back to the channel.
        if (_historyIndex != -1 && (function == EngineKeyFunctions.TextBackspace || function == EngineKeyFunctions.TextDelete))
            ResetHistoryPosition();
    }

    /// <summary>
    ///     <c>handleArrowKeys</c>: Up fetches an older message, remembering what was typed; Down comes back towards it.
    /// </summary>
    public void BrowseHistory(bool older)
    {
        string? text;
        if (older)
        {
            if (_historyIndex == -1 && Text.Length > 0)
                _historyTemp = Text;

            if (_history.Count == 0 || _historyIndex >= _history.Count - 1)
                return;

            _historyIndex++;
            text = _history[_historyIndex];
        }
        else
        {
            if (_historyIndex > 0)
            {
                _historyIndex--;
                text = _history[_historyIndex];
            }
            else
            {
                // Down on what was being typed already: nothing to come back to.
                if (_historyIndex == -1)
                    return;

                _historyIndex = -1;
                text = _historyTemp ?? string.Empty;
                _historyTemp = null;
            }
        }

        SetText(text);
        RefreshLook();
        OnTextEdited?.Invoke(text);
    }

    private void RefreshLook()
    {
        _windowBox.BackgroundColor = _channelColor;
        _button.Look(_historyIndex == -1 ? _channelLabel : (_historyIndex + 1).ToString(), _channelColor);
        _edit.Modulate = _channelColor;
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        ApplyDeferredText();

        var hasFocus = _edit.HasKeyboardFocus();
        if (hasFocus != _hadFocus)
        {
            _hadFocus = hasFocus;
            OnFocusChanged?.Invoke(hasFocus);
        }

        // One line is 30, two 50, three and more 70, as WindowSize.
        var lines = _edit.GetLineCount();
        _scrollBar.Visible = lines > 3;
        var wanted = lines <= 1 ? SmallHeight : lines == 2 ? MediumHeight : LargeHeight;
        if (MathF.Abs(wanted - WantedHeight) > 0.5f)
        {
            WantedHeight = wanted;
            MinHeight = wanted;
        }
    }

    /// <summary>
    ///     tgui-say's <c>.button</c>: dark, rounded, bold 11px text in the channel's colour, lighter when hovered.
    /// </summary>
    private sealed class SayButton : ContainerButton
    {
        private readonly KsTguiStyleBox _box = new(KsTguiSayPalette.Button, radius: 4f);
        // ContainerButton's constructor calls DrawModeChanged before this exists.
        private readonly Label _label = null!;
        private bool _built;
        private Color _color = Color.White;

        public SayButton()
        {
            StyleBoxOverride = _box;
            ModulateSelfOverride = Color.White;
            _label = new Label
            {
                Align = Label.AlignMode.Center,
                VerticalAlignment = VAlignment.Center,
                HorizontalExpand = true,
                Margin = new Thickness(2f, 0f),
            };
            _label.FontOverride = IoCManager.Resolve<KsTguiFonts>().GetFont(11, FontKind.Bold);
            AddChild(_label);
            _built = true;
            Refresh();
        }

        public void Look(string text, Color color)
        {
            _label.Text = text;
            _color = color;
            Refresh();

            // Wide enough for any channel's name, so the button takes its text's width and never clips it.
            InvalidateMeasure();
            Parent?.InvalidateMeasure();
        }

        protected override void DrawModeChanged()
        {
            base.DrawModeChanged();
            Refresh();
        }

        private void Refresh()
        {
            if (!_built)
                return;

            var hovered = DrawMode == DrawModeEnum.Hover || DrawMode == DrawModeEnum.Pressed;
            _box.BackgroundColor = hovered ? KsTguiSayPalette.ButtonHovered : KsTguiSayPalette.Button;
            _label.FontColorOverride = hovered ? KsTguiSayPalette.Lighten(_color, 5f) : _color;
        }
    }
}
