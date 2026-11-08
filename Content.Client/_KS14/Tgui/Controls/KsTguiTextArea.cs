using System.Linq;
using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Input;
using Robust.Shared.Timing;
using Robust.Shared.Utility;
using XColor = Robust.Shared.Maths.Color;

namespace Content.Client._KS14.Tgui.Controls;

/// <summary>
///     tgui-core's TextArea (<c>TextArea.js</c>, <c>Input.scss</c>): an <c>Input</c> that holds several lines. The engine's
///         <see cref="TextEdit"/> has no stylebox property, so this is a panel drawing tgui's input (its background, 1px
///         border, radius, and the 2 / 5 pixel padding that includes the border) around one. The border lights up while the
///         text has the keyboard, and goes grey when it is <see cref="Disabled"/>. Enter (without Shift) raises
///         <see cref="OnEnter"/> and lets go of the keyboard, as tgui's does, where Shift+Enter is a new line; Escape raises
///         <see cref="OnEscape"/> and lets go too.
/// </summary>
/// <remarks>
///     <para>
///         Tab inserts a tab character in place of the selection and raises <see cref="OnChanged"/>, as tgui's does, unless
///             <see cref="DontUseTabForIndent"/> is set. The engine's font draws no glyph for it.
///     </para>
///     <para>
///         <see cref="MaxLength"/> is a <c>textarea</c>'s <c>maxlength</c>: typing and pasting stop at the limit (a paste is cut
///             to fit, a replaced selection makes room), a <see cref="Text"/> set from code is not cut, and Tab, which tgui inserts
///             from script, ignores it. Counted in UTF-16 units as a browser does.
///     </para>
///     <para>
///         Not ported: <c>userMarkup</c>, <c>expensive</c>, <c>selfClear</c>.
///     </para>
/// </remarks>
public sealed partial class KsTguiTextArea : Control
{
    /// <summary>
    ///     The Input's vertical padding with its border: <c>--space-xxs</c> and the 1px border.
    /// </summary>
    public const float VerticalMargin = 2f;

    /// <summary>
    ///     The Input's side padding with its border: <c>--space-sm</c> and the 1px border.
    /// </summary>
    public const float HorizontalMargin = 5f;

    /// <summary>
    ///     The Input's border, which both margins include.
    /// </summary>
    public const float BorderWidth = 1f;

    /// <summary>
    ///     <c>--input-width</c>, 9em.
    /// </summary>
    public const float DefaultWidth = 108f;

    /// <summary>
    ///     A <c>textarea</c>'s default two rows.
    /// </summary>
    public const int DefaultRows = 2;

    private static readonly XColor Background = XColor.FromHex("#0D0D0D");
    private static readonly XColor BorderIdle = XColor.FromHex("#6392C2");

    /// <summary>
    ///     <c>--input-border-disabled</c>, <c>--color-gray</c>.
    /// </summary>
    public static readonly XColor BorderDisabled = XColor.FromHex("#808080");

    [Dependency] private IInputManager _inputManager = default!;

    // The window theme's, from style properties; these are the default theme's where the sheet doesn't say.
    private XColor _background = Background;
    private XColor _borderIdle = BorderIdle;
    private XColor _borderFocused = KsTguiPalettes.InputBorder;

    private readonly TextEdit _edit;
    private readonly KsTguiStyleBox _box = new(Background, 2f) { BorderThickness = new Thickness(1f) };
    private int _rows = DefaultRows;
    private bool _fluid;
    private bool _monospace;
    private bool _disabled;
    private bool _focused;
    private string _text = string.Empty;
    private bool _setting;
    private bool _inserting;
    private bool _limitPending;
    private int? _maxLength;
    private bool _dontUseTabForIndent;

    /// <summary>
    ///     tgui's <c>onChange</c>: the whole text, on every edit by the player.
    /// </summary>
    public event Action<string>? OnChanged;

    /// <summary>
    ///     tgui's <c>onEnter</c>: Enter, with the text, before the box lets go of the keyboard.
    /// </summary>
    public event Action<string>? OnEnter;

    /// <summary>
    ///     tgui's <c>onEscape</c>.
    /// </summary>
    public event Action<string>? OnEscape;

    /// <summary>
    ///     tgui's <c>onBlur</c>: the keyboard left the box, however it did.
    /// </summary>
    public event Action<string>? OnBlurred;

    public KsTguiTextArea()
    {
        IoCManager.InjectDependencies(this);

        HorizontalAlignment = HAlignment.Left;

        _edit = new TextEdit
        {
            HorizontalExpand = true,
            VerticalExpand = true,
            // The right padding is the text's, inside (see below), so that the TextEdit reaches the border there.
            Margin = new Thickness(HorizontalMargin, VerticalMargin, BorderWidth, VerticalMargin),
            StyleClasses = { KsTguiSheetlet.StyleClassTextArea },
        };
        _edit.OnTextChanged += OnEditChanged;
        _edit.OnKeyBindDown += OnEditKeyBindDown;
        AddChild(_edit);

        // tg's text area is scrollbar-width thin with scrollbar-gutter stable: the bar's column is always kept, and the
        //      bar is only drawn while there is something to scroll. The engine's TextEdit lays its text across its whole
        //      width with the bar over it, and never hides the bar, so both are done here, on its two children.
        _scrollBar = _edit.Children.OfType<VScrollBar>().First();
        _scrollBar.AddStyleClass(KsTguiSheetlet.StyleClassThinScrollBar);
        _scrollBar.Visible = false;

        // A browser draws a box's bar between its padding and its border. So the TextEdit reaches the border on the right
        //      and its text stops the bar's column and the padding short of it, and the bar reaches up and down past the
        //      1px of vertical padding to the border, less its arrows' cells. Keeping the whole column inside the TextEdit
        //      keeps the arrows' cells under it for the mouse. Only the right padding moves: the TextEdit maps clicks from
        //      its own top left, so the text's top and left stay where they were.
        var thin = KsTguiScrollBarMetrics.Thin;
        var paddingY = VerticalMargin - BorderWidth;
        _scrollBar.Margin = new Thickness(0f, thin.EndInset - paddingY, 0f, thin.EndInset - paddingY);

        foreach (var child in _edit.Children)
        {
            if (child != _scrollBar)
                child.Margin = new Thickness(0f, 0f, thin.BarWidth + HorizontalMargin - BorderWidth, 0f);
        }
    }

    private readonly VScrollBar _scrollBar;

    /// <summary>
    ///     Whether the text overflows the box, and so its thin scroll bar is shown.
    /// </summary>
    public bool ScrollBarShown => _scrollBar.Visible;

    /// <summary>
    ///     The bar, for tests.
    /// </summary>
    public VScrollBar ScrollBar => _scrollBar;

    /// <summary>
    ///     The text inside.
    /// </summary>
    public TextEdit Edit => _edit;

    /// <summary>
    ///     tgui's <c>maxLength</c>: the most characters the player can type or paste in. Text already in the box, or set from
    ///         code, is left as it is.
    /// </summary>
    public int? MaxLength
    {
        get => _maxLength;
        set => _maxLength = value;
    }

    /// <summary>
    ///     tgui's <c>dontUseTabForIndent</c>: Tab does nothing in the box instead of inserting a tab character.
    /// </summary>
    public bool DontUseTabForIndent
    {
        get => _dontUseTabForIndent;
        set => _dontUseTabForIndent = value;
    }

    /// <summary>
    ///     The text. Setting it raises nothing, as tgui's <c>value</c> doesn't.
    /// </summary>
    public string Text
    {
        get => _text;
        set => SetText(value);
    }

    /// <summary>
    ///     Shown in the sheet's placeholder style while the box is empty.
    /// </summary>
    public string? Placeholder
    {
        get => _edit.Placeholder == null ? null : Rope.Collapse(_edit.Placeholder);
        set => _edit.Placeholder = value == null ? null : new Rope.Leaf(value);
    }

    /// <summary>
    ///     tgui's <c>fluid</c>: the whole width, where the default is <see cref="DefaultWidth"/>.
    /// </summary>
    public bool Fluid
    {
        get => _fluid;
        set
        {
            _fluid = value;
            HorizontalExpand = value;
            HorizontalAlignment = value ? HAlignment.Stretch : HAlignment.Left;
            InvalidateMeasure();
        }
    }

    /// <summary>
    ///     The lines the box is tall for when nothing sets its height, as a <c>textarea</c>'s <c>rows</c>.
    /// </summary>
    public int Rows
    {
        get => _rows;
        set
        {
            _rows = Math.Max(1, value);
            InvalidateMeasure();
        }
    }

    /// <summary>
    ///     tgui's <c>monospace</c>.
    /// </summary>
    public bool Monospace
    {
        get => _monospace;
        set
        {
            _monospace = value;
            if (value)
                _edit.AddStyleClass(KsTguiSheetlet.StyleClassMonospace);
            else
                _edit.RemoveStyleClass(KsTguiSheetlet.StyleClassMonospace);

            InvalidateMeasure();
        }
    }

    /// <summary>
    ///     tgui's <c>disabled</c>: grey border, dim text, and nothing can be typed.
    /// </summary>
    public bool Disabled
    {
        get => _disabled;
        set
        {
            _disabled = value;
            _edit.Editable = !value;
        }
    }

    /// <summary>
    ///     The keyboard is in the box. Followed from <see cref="FrameUpdate"/>: the engine's <see cref="TextEdit"/> is sealed
    ///         and raises no event for it.
    /// </summary>
    public bool IsFocused => _focused;

    /// <summary>
    ///     <c>--input-border-color</c>, lighter (<c>--input-border-color-focus</c>) while the keyboard is in the box, and
    ///         <c>--input-border-disabled</c> once <see cref="Disabled"/>.
    /// </summary>
    public XColor BorderColor => _disabled ? BorderDisabled : _focused ? _borderFocused : _borderIdle;

    /// <summary>
    ///     <c>--input-background</c>.
    /// </summary>
    public XColor BackgroundColor => _background;

    public void SetText(string value)
    {
        _text = value;

        // Not the player's edit: the box's own change event would otherwise report it.
        _setting = true;
        _edit.TextRope = new Rope.Leaf(value);
        var end = new TextEdit.CursorPos(value.Length, TextEdit.LineBreakBias.Top);
        _edit.CursorPosition = end;
        _edit.SelectionStart = end;
        _setting = false;
    }

    /// <summary>
    ///     Puts the keyboard in the box.
    /// </summary>
    public void Focus()
    {
        _edit.GrabKeyboardFocus();
    }

    private void OnEditChanged(TextEdit.TextEditEventArgs args)
    {
        if (_setting)
            return;

        var text = Rope.Collapse(args.TextRope);

        // The box puts its caret after what was typed only once this handler returns, so the text can't be cut to the limit
        //      here: it is done in FrameUpdate, and until then nothing is reported.
        if (_limitPending || (!_inserting && ExceedsLimit(text)))
        {
            _limitPending = true;
            return;
        }

        _text = text;
        OnChanged?.Invoke(_text);
    }

    /// <summary>
    ///     A browser blocks an insertion that would pass <c>maxlength</c>, and lets text that is already longer be edited down.
    /// </summary>
    private bool ExceedsLimit(string text)
    {
        return _maxLength is { } max && text.Length > Math.Max(max, _text.Length);
    }

    /// <summary>
    ///     Cuts what the player just typed or pasted back to <see cref="MaxLength"/>: from the end of it, which is where the
    ///         caret is, as a browser keeps the start of a paste.
    /// </summary>
    private void EnforceMaxLength()
    {
        _limitPending = false;
        if (_maxLength is not { } max)
            return;

        var text = Rope.Collapse(_edit.TextRope);
        var excess = text.Length - Math.Max(max, _text.Length);
        if (excess > 0)
        {
            var caret = Math.Min(_edit.CursorPosition.Index, text.Length);
            var cut = Math.Min(excess, caret);
            text = text.Remove(caret - cut, cut);
            if (text.Length > Math.Max(max, _text.Length))
                text = text.Substring(0, Math.Max(max, _text.Length));

            _setting = true;
            _edit.TextRope = new Rope.Leaf(text);
            var cursor = new TextEdit.CursorPos(Math.Min(caret - cut, text.Length), TextEdit.LineBreakBias.Top);
            _edit.CursorPosition = cursor;
            _edit.SelectionStart = cursor;
            _setting = false;
        }

        if (text == _text)
            return;

        _text = text;
        OnChanged?.Invoke(_text);
    }

    private void OnEditKeyBindDown(GUIBoundKeyEventArgs args)
    {
        if (args.Function == EngineKeyFunctions.TextReleaseFocus)
        {
            OnEscape?.Invoke(_text);
            _edit.ReleaseKeyboardFocus();
            args.Handle();
            return;
        }

        // tgui: Tab is a tab character, in place of the selection, and not held to maxLength.
        if (args.Function == EngineKeyFunctions.TextTabComplete)
        {
            if (!_dontUseTabForIndent && _edit.Editable)
            {
                _inserting = true;
                _edit.InsertAtCursor("	");
                _inserting = false;
                args.Handle();
            }

            return;
        }

        // Shift+Enter is a new line, which is what the engine's box does with Enter itself.
        if (args.Function == EngineKeyFunctions.TextNewline && !_inputManager.IsKeyDown(Keyboard.Key.Shift))
        {
            OnEnter?.Invoke(_text);
            _edit.ReleaseKeyboardFocus();
            args.Handle();
        }
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        if (_limitPending)
            EnforceMaxLength();

        // The engine sets the bar's range as it lays the text out, and raises nothing when it does.
        _scrollBar.Visible = _scrollBar.MaxValue > _scrollBar.Page + 0.5f;

        var focused = UserInterfaceManager.KeyboardFocused == _edit;
        if (focused == _focused)
            return;

        _focused = focused;
        if (!focused)
            OnBlurred?.Invoke(_text);
    }

    protected override void StylePropertiesChanged()
    {
        base.StylePropertiesChanged();

        _background = TryGetStyleProperty<XColor>(KsTguiSheetlet.StylePropertyInputBackground, out var background) ? background : Background;
        _borderIdle = TryGetStyleProperty<XColor>(KsTguiSheetlet.StylePropertyInputBorderIdle, out var idle) ? idle : BorderIdle;
        _borderFocused = TryGetStyleProperty<XColor>(KsTguiSheetlet.StylePropertyInputBorder, out var focused) ? focused : KsTguiPalettes.InputBorder;
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        _edit.Measure(availableSize);

        var font = _edit.TryGetStyleProperty<Font>("font", out var styled) ? styled : UserInterfaceManager.ThemeDefaults.DefaultFont;
        var height = font.GetLineHeight(1f) * _rows + 2f * VerticalMargin;
        var width = _fluid ? _edit.DesiredSize.X : DefaultWidth;
        return new Vector2(MathF.Max(width, 2f * HorizontalMargin), MathF.Max(height, _edit.DesiredSize.Y));
    }

    protected override Vector2 ArrangeOverride(Vector2 finalSize)
    {
        _edit.Arrange(UIBox2.FromDimensions(Vector2.Zero, finalSize));
        return finalSize;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        _box.BackgroundColor = BackgroundColor;
        _box.BorderColor = BorderColor;
        _box.Draw(handle, PixelSizeBox, UIScale);
    }
}
