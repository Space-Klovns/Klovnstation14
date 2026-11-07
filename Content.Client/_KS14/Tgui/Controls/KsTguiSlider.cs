using System.Globalization;
using System.Numerics;
using Content.Client._KS14.Tgui.Backdrop;
using Content.Shared.Localizations;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Input;
using Robust.Shared.Timing;
using XColor = Robust.Shared.Maths.Color;

namespace Content.Client._KS14.Tgui.Controls;

/// <summary>
///     tgui-core's Slider (<c>Slider.js</c>, <c>Slider.scss</c>), a <c>ProgressBar</c> you drag: the bar fills to the value, a
///         light line with a small pointer under it marks it, and the value is written inside, right-aligned. Press and drag
///         sideways to change it, relative to the press, by <see cref="Step"/> for every <see cref="StepPixelSize"/> pixels,
///         with the number in a box above the line while the value has moved. <see cref="OnChanged"/> is raised when the
///         button is let go. A click (a press let go within a tenth of a second) turns the bar into a text box with everything
///         selected, where Enter commits, Escape reverts and losing the focus commits. The drag is <c>DraggableControl</c>
///         with the matrix [1, 0], on the same numbers as the knob's (<see cref="KsTguiKnobMath"/>).
/// </summary>
/// <remarks>
///     The popup is a control of its own, in the popup layer, like the knob's: <see cref="PopupControl"/> is where a
///         backdrop blur goes.
/// </remarks>
public sealed class KsTguiSlider : Control
{
    /// <summary>
    ///     <c>--button-height</c>, 1.667em, the bar's minimum height.
    /// </summary>
    private const float MinHeightPixels = 20f;

    /// <summary>
    ///     <c>--space-m</c>, the bar's padding at the sides.
    /// </summary>
    private const float TextPadding = 6f;

    private const float BorderLayoutPixels = 1f;

    /// <summary>
    ///     <c>--progress-bar-transition</c>.
    /// </summary>
    public const float TransitionSeconds = 1f;

    /// <summary>
    ///     <c>.Slider .NumberInput__input</c> is <c>inset: 2px</c> from the padding box.
    /// </summary>
    private const float EditInset = 2f;

    /// <summary>
    ///     <c>--border-thickness-small</c>, 0.34 of half an em: the cursor line's width.
    /// </summary>
    public const float CursorWidth = 2.04f;

    /// <summary>
    ///     The pointer under the cursor: a 2.04px square turned 45 degrees, shifted down by half its side and scaled by 4,
    ///         so a triangle with a half base of this and the same height again, the base below the bar.
    /// </summary>
    public const float PointerHalfBase = CursorWidth * 1.41421356f / 2f * 4f;

    /// <summary>
    ///     How far below the bar's padding box the pointer's base is: the square's centre starts half a side above the
    ///         box's bottom, and is moved down by half a side, scaled by 4, which is 1.5 sides in all.
    /// </summary>
    public const float PointerBaseDrop = CursorWidth * 1.5f;

    /// <summary>
    ///     <c>--slider-popup-border-radius</c> is <c>--tooltip-border-radius</c>, and the popup's <c>--space-s</c> and
    ///         <c>--space-m</c> padding.
    /// </summary>
    private const float PopupRadius = 4f;

    private const float PopupPaddingVertical = 3f;
    private const float PopupPaddingHorizontal = 6f;

    /// <summary>
    ///     <c>top: -2em</c> of the popup, from the top of the padding box.
    /// </summary>
    private const float PopupRise = 24f;

    /// <summary>
    ///     <c>--slider-disabled-opacity</c>.
    /// </summary>
    private const float DisabledOpacity = 0.5f;

    /// <summary>
    ///     The wide fill's opacity, <c>.4</c>, which shows where <see cref="FillValue"/> is when it differs from the value.
    /// </summary>
    private const float WideFillOpacity = 0.4f;

    private static readonly XColor EditBackground = XColor.FromHex("#0D0D0D");

    private readonly KsTguiRightAlignedLineEdit _edit;
    private readonly KsTguiStyleBox _editBox = new(EditBackground);
    private readonly KsTguiAnimatedNumber _label;
    private readonly KsTguiStyleBox _border = new(XColor.Black.WithAlpha(0f), 2f) { BorderThickness = new Thickness(1f) };
    private readonly KsTguiStyleBox _cursorBox = new(XColor.White, CursorWidth / 2f);
    private readonly KsTguiBackdropPanel _popup;
    private readonly Label _popupLabel;
    private readonly KsTguiStyleBox _popupBox = new(KsTguiPalettes.Base, PopupRadius);
    private readonly List<(KsTguiColor Color, float Min, float Max)> _ranges = new();

    private readonly DrawVertexUV2DColor[] _pointer = new DrawVertexUV2DColor[3];

    // The window theme's, from style properties; the default theme's where the sheet doesn't say.
    private XColor _defaultElement = KsTguiColors.Element(KsTguiColor.Default);
    private XColor _backdrop = XColor.Black.WithAlpha(0f);
    private XColor _windowBase = KsTguiPalettes.Base;

    // Each transition runs from what was shown when its target changed, as a CSS transition does. The fills' width and colour
    //      use CSS's default timing function (ease), the border's colour ease-out; nothing transitions during a drag.
    private Eased _mainFill;
    private Eased _wideFill;
    private EasedColor _fillColor = new(KsTguiProgressBar.EaseDefault);
    private EasedColor _borderColor = new(KsTguiProgressBar.EaseOut);
    private bool _framed;

    private float _value;
    private float _minValue;
    private float _maxValue = 100f;
    private float _step = 1f;
    private float? _stepPixelSize;
    private string? _unit;
    private Func<float, string>? _format;
    private KsTguiColor? _color;
    private float? _fillValue;
    private bool _disabled;
    private bool _unclamped;
    private bool _tickWhileDragging;
    private bool _animated;
    private bool _hasOutsideValue;
    private Control? _content;

    private bool _pressed;
    private bool _holdPassed;
    private bool _pendingEdit;
    private bool _moved;
    private bool _editing;
    private float _held;
    private float _tickElapsed;
    private float _pressOrigin;
    private float _originalValue;
    private float _liveValue;
    private float _effectiveStepPixelSize = 1f;

    /// <summary>
    ///     tgui's <c>onChange</c>, with the value: when the button is let go (after a drag or a click), when a typed value is
    ///         committed, and every <see cref="KsTguiKnob.TickSeconds"/> of a drag when <see cref="TickWhileDragging"/>.
    /// </summary>
    public event Action<float>? OnChanged;

    public KsTguiSlider()
    {
        MouseFilter = MouseFilterMode.Stop;
        DefaultCursorShape = CursorShape.HResize;
        HorizontalExpand = true;

        _label = new KsTguiAnimatedNumber { Align = Label.AlignMode.Right, Instant = true };
        _label.MouseFilter = MouseFilterMode.Ignore;
        AddChild(_label);

        // .NumberInput__input: no border, 0 var(--space-m) of padding, over the bar.
        _editBox.SetContentMarginOverride(StyleBox.Margin.Vertical, 0f);
        _edit = new KsTguiRightAlignedLineEdit(_editBox, TextPadding) { Visible = false, SelectAllOnFocus = true };
        AddChild(_edit);

        _edit.OnTextEntered += _ => Commit();
        _edit.OnFocusExit += _ =>
        {
            if (_editing)
                Commit();
        };
        _edit.OnKeyBindDown += args =>
        {
            if (args.Function != EngineKeyFunctions.TextReleaseFocus)
                return;

            Cancel();
            args.Handle();
        };

        _popupBox.SetContentMarginOverride(StyleBox.Margin.Horizontal, PopupPaddingHorizontal);
        _popupBox.SetContentMarginOverride(StyleBox.Margin.Vertical, PopupPaddingVertical);
        _popupLabel = new Label { FontColorOverride = XColor.White };
        _popup = new KsTguiBackdropPanel { PanelOverride = _popupBox, MouseFilter = MouseFilterMode.Ignore, Radius = PopupRadius };
        _popup.AddChild(_popupLabel);

        RefreshLabel();
        Retarget(snap: true);
    }

    public KsTguiRightAlignedLineEdit Edit => _edit;

    /// <summary>
    ///     The value shown: the one last set, committed or released at, or the live one while dragging.
    /// </summary>
    public float Value
    {
        get => _pressed ? _liveValue : _value;
        set => SetValue(value);
    }

    public float MinValue
    {
        get => _minValue;
        set
        {
            _minValue = value;
            Retarget();
        }
    }

    public float MaxValue
    {
        get => _maxValue;
        set
        {
            _maxValue = value;
            Retarget();
        }
    }

    /// <summary>
    ///     What a drag moves the value by and rounds it to.
    /// </summary>
    public float Step
    {
        get => _step;
        set => _step = value > 0f ? value : 1f;
    }

    /// <summary>
    ///     Pixels of drag for one <see cref="Step"/>. Null is tgui's default: the bar's own width over the number of steps,
    ///         which makes dragging its whole width sweep the whole range.
    /// </summary>
    public float? StepPixelSize
    {
        get => _stepPixelSize;
        set => _stepPixelSize = value is > 0f ? value : null;
    }

    public string? Unit
    {
        get => _unit;
        set
        {
            _unit = value;
            RefreshLabel();
        }
    }

    /// <summary>
    ///     How the value is written, in the bar and in the popup. Null writes it as <c>0.##</c>.
    /// </summary>
    public Func<float, string>? Format
    {
        get => _format;
        set
        {
            _format = value;
            RefreshLabel();
        }
    }

    /// <summary>
    ///     The bar's colour, tgui's <c>color</c>. Null takes the range the value is in, then the theme's.
    /// </summary>
    public KsTguiColor? Color
    {
        get => _color;
        set
        {
            _color = value;
            Retarget();
        }
    }

    /// <summary>
    ///     The bar fills to this instead of the value, and the part between them is the lighter, wide fill.
    /// </summary>
    public float? FillValue
    {
        get => _fillValue;
        set
        {
            _fillValue = value;
            Retarget();
        }
    }

    /// <summary>
    ///     tgui's <c>animated</c>: a value set from outside eases to its number in the bar instead of changing at once. A
    ///         dragged one never does.
    /// </summary>
    public bool Animated
    {
        get => _animated;
        set
        {
            _animated = value;
            RefreshLabel();
        }
    }

    /// <summary>
    ///     tgui's <c>children</c>: shown in the bar's content in place of the value.
    /// </summary>
    public Control? Content
    {
        get => _content;
        set
        {
            if (_content != null)
                RemoveChild(_content);

            _content = value;
            if (value != null)
            {
                AddChild(value);
                value.SetPositionInParent(1);
            }

            _label.Visible = value == null;
            InvalidateMeasure();
        }
    }

    public bool Disabled
    {
        get => _disabled;
        set
        {
            _disabled = value;
            Modulate = value ? XColor.White.WithAlpha(DisabledOpacity) : XColor.White;
            if (!value)
                return;

            if (_editing)
                Cancel();

            CancelPress();
        }
    }

    /// <summary>
    ///     A typed value may be outside the range. A drag never is.
    /// </summary>
    public bool Unclamped
    {
        get => _unclamped;
        set => _unclamped = value;
    }

    public bool TickWhileDragging
    {
        get => _tickWhileDragging;
        set => _tickWhileDragging = value;
    }

    public bool IsEditing => _editing;

    public bool IsPressed => _pressed;

    /// <summary>
    ///     Held long enough to be a drag.
    /// </summary>
    public bool IsDragging => _pressed && _holdPassed;

    /// <summary>
    ///     tgui shows the popup when the drag next re-renders the bar, which is when the value first changes. Never while it is
    ///         being typed in.
    /// </summary>
    public bool PopupVisible => _pressed && _moved && !_editing;

    public string PopupText => FormatValue(_liveValue);

    /// <summary>
    ///     The popup, a control of its own in the popup layer while <see cref="PopupVisible"/>.
    /// </summary>
    public Control PopupControl => _popup;

    /// <summary>
    ///     The pixels of drag for a step that the last press used.
    /// </summary>
    public float EffectiveStepPixelSize => _effectiveStepPixelSize;

    /// <summary>
    ///     The text in the bar.
    /// </summary>
    public string? ShownText => _label.Text;

    /// <summary>
    ///     Where the value is in the range, clamped: how far the cursor is across.
    /// </summary>
    public float CursorFraction => Math.Clamp(KsTguiKnobMath.Scale(Value, _minValue, _maxValue), 0f, 1f);

    /// <summary>
    ///     Where the fill ends, as tgui's <c>$</c> and <c>B</c>: the value's and the fill value's (or the value's again).
    /// </summary>
    public float FillFraction => Math.Clamp(KsTguiKnobMath.Scale(_fillValue ?? Value, _minValue, _maxValue), 0f, 1f);

    public float DisplayedMainFill => _mainFill.Displayed;

    public float DisplayedWideFill => _wideFill.Displayed;

    public XColor DisplayedFillColor => _fillColor.Displayed;

    public XColor DisplayedBorderColor => _borderColor.Displayed;

    /// <summary>
    ///     Colours a stretch of the bar, in its own values, for when no <see cref="Color"/> is set.
    /// </summary>
    public void AddRange(KsTguiColor color, float min, float max)
    {
        _ranges.Add((color, min, max));
        Retarget();
    }

    public void ClearRanges()
    {
        _ranges.Clear();
        Retarget();
    }

    /// <summary>
    ///     The colour name that decides the bar: <see cref="Color"/>, else the first range that holds the value the bar
    ///         fills to, ends included, else the default.
    /// </summary>
    public KsTguiColor ResolvedColor
    {
        get
        {
            if (_color is { } name)
                return name;

            var shown = _fillValue ?? _value;
            foreach (var (color, min, max) in _ranges)
            {
                if (shown >= min && shown <= max)
                    return color;
            }

            return KsTguiColor.Default;
        }
    }

    /// <summary>
    ///     Shows a number from outside. While the player is dragging, theirs stays.
    /// </summary>
    public void SetValue(float value)
    {
        _value = value;
        if (!_pressed)
            _liveValue = value;

        RefreshLabel();
        Retarget();
        _hasOutsideValue = true;
    }

    /// <summary>
    ///     A press on the bar. <paramref name="screenX"/> is the pointer's horizontal position.
    /// </summary>
    public void BeginPress(float screenX)
    {
        if (_disabled || _editing)
            return;

        _effectiveStepPixelSize = _stepPixelSize
            ?? KsTguiKnobMath.DefaultStepPixelSize(Size.X, _minValue, _maxValue, _step);
        _pressed = true;
        _holdPassed = false;
        _pendingEdit = false;
        _moved = false;
        _held = 0f;
        _tickElapsed = 0f;
        _pressOrigin = screenX;
        _originalValue = _value;
        _liveValue = _value;
        RefreshLabel();
        Retarget();
    }

    /// <summary>
    ///     The pointer moved while pressed. Only counts once the press has been held for <see cref="KsTguiKnob.HoldSeconds"/>,
    ///         as DraggableControl only listens for moves from then; the distance is still from where it was pressed.
    /// </summary>
    public void DragTo(float screenX)
    {
        if (!_pressed || !_holdPassed)
            return;

        // The matrix is [1, 0]: right is more.
        var drag = screenX - _pressOrigin;
        var value = KsTguiKnobMath.DragValue(_originalValue, drag, _step, _effectiveStepPixelSize, _minValue, _maxValue);
        if (value != _liveValue)
            _moved = true;

        _liveValue = value;
        RefreshLabel();
        Retarget();
    }

    /// <summary>
    ///     The button was let go. A drag keeps where it got to, and a press let go too soon is a click, which opens the text
    ///         box once the hold would have been up. Either is reported, as tgui's is.
    /// </summary>
    public void Release()
    {
        if (!_pressed)
            return;

        var released = _liveValue;

        _pressed = false;
        _moved = false;
        _value = released;
        _pendingEdit = !_holdPassed;
        HidePopup();
        RefreshLabel();
        Retarget();

        OnChanged?.Invoke(released);
    }

    /// <summary>
    ///     Moves the hold and tick timers on.
    /// </summary>
    public void Advance(float seconds)
    {
        if (_pressed)
        {
            if (!_holdPassed)
            {
                _held += seconds;
                if (_held >= KsTguiKnob.HoldSeconds)
                    _holdPassed = true;

                return;
            }

            _tickElapsed += seconds;
            while (_tickElapsed >= KsTguiKnob.TickSeconds)
            {
                _tickElapsed -= KsTguiKnob.TickSeconds;
                if (_tickWhileDragging)
                    OnChanged?.Invoke(_liveValue);
            }

            return;
        }

        if (!_pendingEdit)
            return;

        _held += seconds;
        if (_held < KsTguiKnob.HoldSeconds)
            return;

        _pendingEdit = false;
        if (!_disabled)
            BeginEditing();
    }

    /// <summary>
    ///     Enter or losing focus: takes the typed number, held to the range unless <see cref="Unclamped"/>. Nothing that
    ///         isn't a number changes anything.
    /// </summary>
    public void Commit()
    {
        if (!_editing)
            return;

        EndEditing();

        if (!UserInputParser.TryFloat(_edit.Text, out var parsed))
            return;

        if (KsTguiKnobMath.TypedValue(parsed, _minValue, _maxValue, _unclamped) is not { } typed)
            return;

        _value = typed;
        _liveValue = typed;
        RefreshLabel();
        Retarget();
        OnChanged?.Invoke(typed);
    }

    /// <summary>
    ///     Escape: stops editing and keeps the value there was.
    /// </summary>
    public void Cancel()
    {
        EndEditing();
    }

    private void CancelPress()
    {
        _pressed = false;
        _holdPassed = false;
        _pendingEdit = false;
        _moved = false;
        _liveValue = _value;
        HidePopup();
        RefreshLabel();
        Retarget();
    }

    private void BeginEditing()
    {
        _editing = true;
        _edit.Visible = true;
        _edit.Text = _liveValue.ToString(CultureInfo.CurrentCulture);
        _edit.UpdateAlignment();
        _edit.GrabKeyboardFocus();
        _edit.CursorPosition = _edit.Text.Length;
        _edit.SelectionStart = 0;
        InvalidateArrange();
    }

    private void EndEditing()
    {
        if (!_editing)
            return;

        _editing = false;
        _edit.Visible = false;
        if (_edit.HasKeyboardFocus())
            _edit.ReleaseKeyboardFocus();
    }

    private string FormatValue(float value)
    {
        var text = _format != null ? _format(value) : value.ToString("0.##", CultureInfo.CurrentCulture);
        return string.IsNullOrEmpty(_unit) ? text : text + " " + _unit;
    }

    private void RefreshLabel()
    {
        // The window's first value is what it opens on, not a change to ease across, and a drag never eases.
        _label.Instant = !_animated || _pressed || !_hasOutsideValue;
        _label.Format = FormatValue;
        _label.Value = Value;
        InvalidateMeasure();
    }

    private void ShowPopup()
    {
        _popupLabel.Text = FormatValue(_liveValue);

        var popupRoot = Root?.PopupRoot ?? UserInterfaceManager.PopupRoot;
        if (_popup.Parent == null)
        {
            // tgui blurs what is behind the value.
            _popup.Backdrop = UserInterfaceManager.GetUIController<KsTguiBackdropUIController>();
            popupRoot.AddChild(_popup);
        }

        _popup.Measure(Vector2.PositiveInfinity);
        var popupSize = _popup.DesiredSize;
        var rootSize = popupRoot.Size;

        // Centred on the cursor, 2em above the padding box's top; below the bar when there is no room above, and kept
        //      inside the window sideways.
        var cursorX = GlobalPosition.X + BorderLayoutPixels + (Size.X - 2f * BorderLayoutPixels) * CursorFraction;
        var x = cursorX - popupSize.X / 2f;
        var y = GlobalPosition.Y + BorderLayoutPixels - PopupRise;
        if (y < 0f)
            y = GlobalPosition.Y + Size.Y + PopupRise / 4f;

        if (rootSize.X > popupSize.X)
            x = Math.Clamp(x, 0f, rootSize.X - popupSize.X);

        LayoutContainer.SetPosition(_popup, new Vector2(x, y));
    }

    private void HidePopup()
    {
        if (_popup.Parent != null)
            _popup.Orphan();
    }

    protected override void ExitedTree()
    {
        base.ExitedTree();
        _popup.ReleaseBackdrop();
        HidePopup();
    }

    protected override void KeyBindDown(GUIBoundKeyEventArgs args)
    {
        base.KeyBindDown(args);

        if (args.Function != EngineKeyFunctions.UIClick || _disabled || _editing)
            return;

        BeginPress(args.PointerLocation.Position.X / UIScale);
        args.Handle();
    }

    protected override void KeyBindUp(GUIBoundKeyEventArgs args)
    {
        base.KeyBindUp(args);

        if (args.Function != EngineKeyFunctions.UIClick)
            return;

        Release();
        args.Handle();
    }

    protected override void MouseMove(GUIMouseMoveEventArgs args)
    {
        base.MouseMove(args);

        if (_pressed)
            DragTo(args.GlobalPosition.X);
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        _framed = true;
        Advance(args.DeltaSeconds);
        AdvanceTransitions(args.DeltaSeconds);

        if (PopupVisible)
            ShowPopup();
    }

    /// <summary>
    ///     Moves the fills' and the border's transitions on. Nothing changes once they have settled.
    /// </summary>
    public void AdvanceTransitions(float frameSeconds)
    {
        _framed = true;
        _mainFill.Advance(frameSeconds);
        _wideFill.Advance(frameSeconds);
        _fillColor.Advance(frameSeconds);
        _borderColor.Advance(frameSeconds);
    }

    /// <summary>
    ///     Whether any transition is still running.
    /// </summary>
    public bool Transitioning => _mainFill.Running || _wideFill.Running || _fillColor.Running || _borderColor.Running;

    private void Retarget(bool snap = false)
    {
        var colorName = ResolvedColor;
        var element = colorName == KsTguiColor.Default ? _defaultElement : KsTguiColors.Element(colorName);

        var scale = Math.Clamp(KsTguiKnobMath.Scale(Value, _minValue, _maxValue), 0f, 1f);
        var fill = FillFraction;

        // .ProgressBar__fill--animated is on the wide fill only with a fill value, and on both only while not dragging.
        var animated = !snap && _framed && !_pressed;
        _mainFill.Set(MathF.Min(fill, scale), animated);
        _wideFill.Set(MathF.Max(fill, scale), animated && _fillValue != null);
        _fillColor.Set(element, animated);

        // The border is always eased, even through a drag.
        _borderColor.Set(element, !snap && _framed);
    }

    protected override void StylePropertiesChanged()
    {
        base.StylePropertiesChanged();

        _defaultElement = TryGetStyleProperty<XColor>(KsTguiSheetlet.StylePropertyProgressColor, out var element)
            ? element
            : KsTguiColors.Element(KsTguiColor.Default);
        _backdrop = TryGetStyleProperty<XColor>(KsTguiSheetlet.StylePropertyProgressBackdrop, out var backdrop)
            ? backdrop
            : XColor.Black.WithAlpha(0f);
        _windowBase = TryGetStyleProperty<XColor>(KsTguiSheetlet.StylePropertyWindowBase, out var windowBase)
            ? windowBase
            : KsTguiPalettes.Base;
        _popupBox.BackgroundColor = TryGetStyleProperty<XColor>(KsTguiSheetlet.StylePropertyTooltip, out var tooltip)
            ? tooltip
            : KsTguiPalettes.Base;
        _editBox.BackgroundColor = TryGetStyleProperty<XColor>(KsTguiSheetlet.StylePropertyInputBackground, out var background)
            ? background
            : EditBackground;

        // A theme change is not a value changing: the colour lands at once.
        Retarget(snap: true);
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        var inset = BorderLayoutPixels + TextPadding;
        var content = _content ?? _label;
        content.Measure(new Vector2(MathF.Max(0f, availableSize.X - 2f * inset), availableSize.Y));
        _edit.Measure(Vector2.PositiveInfinity);
        return new Vector2(content.DesiredSize.X + 2f * inset, MathF.Max(MinHeightPixels, content.DesiredSize.Y));
    }

    protected override Vector2 ArrangeOverride(Vector2 finalSize)
    {
        var inset = BorderLayoutPixels + TextPadding;
        var content = _content ?? _label;
        var contentHeight = MathF.Min(finalSize.Y, content.DesiredSize.Y);
        content.Arrange(UIBox2.FromDimensions(inset, (finalSize.Y - contentHeight) / 2f,
            MathF.Max(0f, finalSize.X - 2f * inset), contentHeight));

        var editInset = BorderLayoutPixels + EditInset;
        _edit.Arrange(new UIBox2(editInset, editInset, MathF.Max(editInset, finalSize.X - editInset),
            MathF.Max(editInset, finalSize.Y - editInset)));
        return finalSize;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        var box = PixelSizeBox;
        var border = MathF.Max(1f, MathF.Round(UIScale));

        if (_backdrop.A > 0f)
            handle.DrawRect(box, _backdrop);

        var inner = new UIBox2(box.Left + border, box.Top + border, box.Right - border, box.Bottom - border);

        // The wide fill is the fill colour at .4 under the real one; where they are the same width it is covered.
        var wideRight = inner.Left + inner.Width * _wideFill.Displayed;
        if (wideRight > inner.Left)
        {
            var wide = KsTguiPalettes.Translucent(_fillColor.Displayed, WideFillOpacity, _windowBase);
            handle.DrawRect(new UIBox2(inner.Left, inner.Top, wideRight, inner.Bottom), wide);
        }

        var mainRight = inner.Left + inner.Width * _mainFill.Displayed;
        if (mainRight > inner.Left)
            handle.DrawRect(new UIBox2(inner.Left, inner.Top, mainRight, inner.Bottom), _fillColor.Displayed);

        _border.BorderColor = _borderColor.Displayed;
        _border.Draw(handle, box, UIScale);

        // The cursor line is centred on the value, and the text and everything drawn after are over it.
        var cursorX = inner.Left + inner.Width * CursorFraction;
        var half = CursorWidth * UIScale / 2f;
        _cursorBox.Draw(handle, new UIBox2(cursorX - half, inner.Top, cursorX + half, inner.Bottom), UIScale);

        if (_disabled || _editing)
            return;

        // Its pointer, an upright triangle with its base below the bar.
        var baseY = inner.Bottom + PointerBaseDrop * UIScale;
        var apexY = baseY - PointerHalfBase * UIScale;
        var halfBase = PointerHalfBase * UIScale;
        var color = XColor.White;
        _pointer[0] = new DrawVertexUV2DColor(new Vector2(cursorX, apexY), Vector2.Zero, color);
        _pointer[1] = new DrawVertexUV2DColor(new Vector2(cursorX + halfBase, baseY), Vector2.Zero, color);
        _pointer[2] = new DrawVertexUV2DColor(new Vector2(cursorX - halfBase, baseY), Vector2.Zero, color);
        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList, Texture.White, _pointer);
    }

    /// <summary>
    ///     A number that eases to its target over <see cref="TransitionSeconds"/>, from what it showed when the target changed.
    /// </summary>
    private struct Eased
    {
        private float _from;
        private float _to;
        private float _elapsed;
        private bool _set;

        public readonly bool Running => _elapsed < TransitionSeconds;

        public readonly float Displayed =>
            Running ? _from + (_to - _from) * KsTguiProgressBar.EaseDefault(_elapsed / TransitionSeconds) : _to;

        public void Set(float target, bool animated)
        {
            if (!_set || !animated)
            {
                _set = true;
                _from = _to = target;
                _elapsed = TransitionSeconds;
                return;
            }

            if (target == _to)
                return;

            _from = Displayed;
            _to = target;
            _elapsed = 0f;
        }

        public void Advance(float seconds)
        {
            if (Running)
                _elapsed = MathF.Min(TransitionSeconds, _elapsed + seconds);
        }
    }

    private struct EasedColor
    {
        private readonly Func<float, float> _ease;
        private XColor _from;
        private XColor _to;
        private float _elapsed;
        private bool _set;

        public EasedColor(Func<float, float> ease)
        {
            _ease = ease;
        }

        public readonly bool Running => _elapsed < TransitionSeconds;

        public readonly XColor Displayed
        {
            get
            {
                if (!Running)
                    return _to;

                // CSS interpolates colours component by component, in sRGB.
                var t = (_ease ?? KsTguiProgressBar.EaseDefault)(_elapsed / TransitionSeconds);
                return new XColor(_from.R + (_to.R - _from.R) * t, _from.G + (_to.G - _from.G) * t,
                    _from.B + (_to.B - _from.B) * t, _from.A + (_to.A - _from.A) * t);
            }
        }

        public void Set(XColor target, bool animated)
        {
            if (!_set || !animated)
            {
                _set = true;
                _from = _to = target;
                _elapsed = TransitionSeconds;
                return;
            }

            if (target == _to)
                return;

            _from = Displayed;
            _to = target;
            _elapsed = 0f;
        }

        public void Advance(float seconds)
        {
            if (Running)
                _elapsed = MathF.Min(TransitionSeconds, _elapsed + seconds);
        }
    }
}
