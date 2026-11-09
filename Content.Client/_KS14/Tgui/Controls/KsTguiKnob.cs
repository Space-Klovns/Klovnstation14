using Content.Client._KS14.Tgui.Backdrop;
using System.Globalization;
using System.Numerics;
using Content.Shared.Localizations;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Input;
using Robust.Shared.Timing;
using XColor = Robust.Shared.Maths.Color;

namespace Content.Client._KS14.Tgui.Controls;

/// <summary>
///     tgui-core's Knob: a dark cap with a light cursor line that turns with the value, inside a ring that sweeps 270 degrees
///         and shows how far the value is, in a colour. Press and drag up or down to change the value, relative to where it
///         was pressed, by <see cref="Step"/> for every <see cref="StepPixelSize"/> pixels; the number shows in a box above
///         it while dragging. <see cref="OnChanged"/> is raised when the button is let go. A click is a press let go within a
///         tenth of a second: it turns the knob into a text box with everything selected, where Enter commits, Escape
///         reverts, and losing focus commits. Drawn as triangles in device pixels with soft edges, so it stays sharp at any UI
///         scale. The drag is <c>DraggableControl</c> with the matrix [0, -1], and its numbers are in
///         <see cref="KsTguiKnobMath"/>.
/// </summary>
public sealed class KsTguiKnob : Control
{
    /// <summary>
    ///     How long a press must be held before it is a drag, and a release sooner than this is a click
    ///         (<c>DraggableControl</c>'s 100 ms timeout).
    /// </summary>
    public const float HoldSeconds = 0.1f;

    /// <summary>
    ///     <c>updateRate</c>, the interval of <see cref="TickWhileDragging"/>.
    /// </summary>
    public const float TickSeconds = 0.4f;

    /// <summary>
    ///     The width of the soft edge, in device pixels, centred on the true outline.
    /// </summary>
    private const float Fringe = 1f;

    /// <summary>
    ///     <c>--shadow-glow-medium</c>, the cap's glow: 1rem of blur, which does not scale with the knob.
    /// </summary>
    private const float GlowBlur = 12f;

    private const float GlowAlpha = 0.25f;
    private const int GlowRings = 12;

    /// <summary>
    ///     <c>--knob-color</c> is the window's base with its saturation set to 5 and its lightness to 20; the cap's gradient
    ///         starts 15 lighter, and the cursor is at 85.
    /// </summary>
    private const float CapSaturation = 5f;

    private const float CapLightness = 20f;
    private const float CapTopLightness = 35f;
    private const float CursorLightness = 85f;

    /// <summary>
    ///     The ring's alpha when it is the theme's colour: <c>--input-border-color</c> is at .75.
    /// </summary>
    private const float DefaultRingAlpha = 0.75f;

    /// <summary>
    ///     <c>--tooltip-border-radius</c>, and the popup's <c>--space-s</c> and <c>--space-m</c> padding.
    /// </summary>
    private const float PopupRadius = 4f;

    private const float PopupPaddingVertical = 3f;
    private const float PopupPaddingHorizontal = 6f;

    /// <summary>
    ///     <c>contentOffset</c> of the popup's Floating.
    /// </summary>
    private const float PopupOffset = 6f;

    /// <summary>
    ///     The gap between the dial and the readout under it, in virtual pixels.
    /// </summary>
    private const float ReadoutGap = 2f;

    private static readonly XColor EditBackground = XColor.FromHex("#0D0D0D");

    private readonly KsTguiRightAlignedLineEdit _edit;
    private readonly KsTguiAnimatedNumber _readout;
    private readonly KsTguiStyleBox _editBox = new(EditBackground);
    private readonly KsTguiBackdropPanel _popup;
    private readonly KsTguiScaleHost _popupHost;
    private readonly KsTguiFloatingTransition _popupTransition = new();
    private readonly Label _popupLabel;
    private readonly KsTguiStyleBox _popupBox = new(KsTguiPalettes.Base, PopupRadius);
    private readonly List<(KsTguiColor Color, float Min, float Max)> _ranges = new();

    private DrawVertexUV2DColor[] _vertices = new DrawVertexUV2DColor[2048];
    private int _vertexCount;
    private XColor _modulate = XColor.White;

    // The window theme's, from style properties; the default theme's where the sheet doesn't say.
    private XColor _ring = KsTguiPalettes.InputBorder;
    private XColor _windowBase = KsTguiPalettes.Base;

    private float _value;
    private float _minValue;
    private float _maxValue = 100f;
    private float _step = 1f;
    private float? _stepPixelSize;
    private float _knobSize = 1f;
    private string? _unit;
    private Func<float, string>? _format;
    private KsTguiColor? _color;
    private float? _fillValue;
    private bool _bipolar;
    private bool _disabled;
    private bool _unclamped;
    private bool _tickWhileDragging;
    private bool _showValue;

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
    ///         committed, and every <see cref="TickSeconds"/> of a drag when <see cref="TickWhileDragging"/>.
    /// </summary>
    public event Action<float>? OnChanged;

    public KsTguiKnob()
    {
        MouseFilter = MouseFilterMode.Stop;
        DefaultCursorShape = CursorShape.VResize;

        // The value under the knob, in the label style of tgui's LabeledControls. It is added first so the edit box, which
        //      opens in its place, is drawn over it.
        _readout = new KsTguiAnimatedNumber { FontColorOverride = KsTguiPalettes.Label, Align = Label.AlignMode.Center, Visible = false };
        _readout.Format = FormatValue;
        AddChild(_readout);

        _editBox.SetContentMarginOverride(StyleBox.Margin.Vertical, 0f);
        _edit = new KsTguiRightAlignedLineEdit(_editBox, 6f) { Visible = false, SelectAllOnFocus = true };
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

        // tgui's Floating scales and fades its content in and out, about the edge nearest the knob (the bottom, for a popup
        //      above it). The host does that, and blurs what is behind the panel at the scale and fade it has, so the panel
        //      has no blur of its own.
        _popupHost = new KsTguiScaleHost { MouseFilter = MouseFilterMode.Ignore, Radius = PopupRadius, PivotBottom = true };
        _popupHost.AddChild(_popup);
    }

    public KsTguiRightAlignedLineEdit Edit => _edit;

    /// <summary>
    ///     The value written under the knob. tgui-core's Knob has no such readout (the number is only in the popup while
    ///         dragging), so this is off by default. It counts to a new value like tgui's <c>AnimatedNumber</c> and is live
    ///         while dragging, which is why the drag popup is not shown with it. Clicking it opens the typing box in its place.
    /// </summary>
    public bool ShowValue
    {
        get => _showValue;
        set
        {
            if (_showValue == value)
                return;

            _showValue = value;
            _readout.Visible = value;
            ToolTip = value ? Loc.GetString("ks-tgui-knob-type-value") : null;
            SyncReadout();
            InvalidateMeasure();
        }
    }

    /// <summary>
    ///     The label that writes the value under the knob.
    /// </summary>
    public KsTguiAnimatedNumber Readout => _readout;

    /// <summary>
    ///     The knob's side, in virtual pixels. The readout is under this square.
    /// </summary>
    public float KnobSide => KsTguiKnobMath.BaseSize * _knobSize;

    /// <summary>
    ///     Where the readout sits, and where the typing box opens with <see cref="ShowValue"/>, in this control's own pixels.
    /// </summary>
    public UIBox2 ReadoutBox => UIBox2.FromDimensions(0f, KnobSide + ReadoutGap, MathF.Max(Size.X, MeasuredWidth()), ReadoutHeight);

    private float ReadoutHeight
    {
        get
        {
            _readout.Measure(Vector2.PositiveInfinity);
            _edit.Measure(Vector2.PositiveInfinity);
            return MathF.Max(_readout.DesiredSize.Y, _edit.DesiredSize.Y);
        }
    }

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
        set => _minValue = value;
    }

    public float MaxValue
    {
        get => _maxValue;
        set => _maxValue = value;
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
    ///     Pixels of drag for one <see cref="Step"/>. Null is tgui's default: the knob's own width over the number of steps,
    ///         which makes dragging up its own width sweep the whole range.
    /// </summary>
    public float? StepPixelSize
    {
        get => _stepPixelSize;
        set => _stepPixelSize = value is > 0f ? value : null;
    }

    /// <summary>
    ///     tgui's <c>size</c>: the knob's <c>font-size</c> in ems, which everything about it is measured in.
    /// </summary>
    public float KnobSize
    {
        get => _knobSize;
        set
        {
            _knobSize = value > 0f ? value : 1f;
            InvalidateMeasure();
        }
    }

    public string? Unit
    {
        get => _unit;
        set
        {
            _unit = value;
            _readout.Format = FormatValue;
        }
    }

    /// <summary>
    ///     How the value is written in the popup. Null writes it as <c>0.##</c>.
    /// </summary>
    public Func<float, string>? Format
    {
        get => _format;
        set
        {
            _format = value;
            _readout.Format = FormatValue;
        }
    }

    /// <summary>
    ///     The ring's colour, tgui's <c>color</c>. Null takes the range the value is in, then the theme's.
    /// </summary>
    public KsTguiColor? Color
    {
        get => _color;
        set => _color = value;
    }

    /// <summary>
    ///     The ring shows this instead of the value, while the cursor still shows the value.
    /// </summary>
    public float? FillValue
    {
        get => _fillValue;
        set => _fillValue = value;
    }

    /// <summary>
    ///     The ring fills from the middle of the dial rather than the left.
    /// </summary>
    public bool Bipolar
    {
        get => _bipolar;
        set => _bipolar = value;
    }

    public bool Disabled
    {
        get => _disabled;
        set
        {
            _disabled = value;
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

    /// <summary>
    ///     Pressed, whether or not it has been held long enough to be a drag yet.
    /// </summary>
    public bool IsPressed => _pressed;

    /// <summary>
    ///     Held long enough to be a drag.
    /// </summary>
    public bool IsDragging => _pressed && _holdPassed;

    /// <summary>
    ///     tgui shows the popup when the drag next re-renders the knob, which is when the value first changes.
    /// </summary>
    public bool PopupVisible => _pressed && _moved && !_showValue;

    public string PopupText => FormatValue(_liveValue);

    public Control PopupControl => _popupHost;

    /// <summary>
    ///     The panel holding the value, inside <see cref="PopupControl"/>.
    /// </summary>
    public KsTguiBackdropPanel PopupPanel => _popup;

    /// <summary>
    ///     Floating's open and close transition, which outlasts <see cref="PopupVisible"/> by 0.2 s on the way out.
    /// </summary>
    public KsTguiFloatingTransition PopupTransition => _popupTransition;

    /// <summary>
    ///     The pixels of drag for a step that the last press used.
    /// </summary>
    public float EffectiveStepPixelSize => _effectiveStepPixelSize;

    /// <summary>
    ///     Colours a stretch of the dial, in the knob's own values, for when no <see cref="Color"/> is set.
    /// </summary>
    public void AddRange(KsTguiColor color, float min, float max)
    {
        _ranges.Add((color, min, max));
    }

    public void ClearRanges()
    {
        _ranges.Clear();
    }

    /// <summary>
    ///     The colour of the first range that holds the value the ring shows, both ends included, as tgui's
    ///         <c>keyOfMatchingRange</c>; null when none does.
    /// </summary>
    public KsTguiColor? MatchingRange()
    {
        var shown = _fillValue ?? Value;
        foreach (var (color, min, max) in _ranges)
        {
            if (shown >= min && shown <= max)
                return color;
        }

        return null;
    }

    /// <summary>
    ///     The colour name that decides the ring: <see cref="Color"/>, else the matching range, else null for the theme's.
    ///         Names that tgui has no ring colour for (default, caution, danger, transparent) are the theme's too.
    /// </summary>
    public KsTguiColor? RingColorName
    {
        get
        {
            var name = _color ?? MatchingRange();
            return name is KsTguiColor.Default or KsTguiColor.Caution or KsTguiColor.Danger or KsTguiColor.Transparent ? null : name;
        }
    }

    /// <summary>
    ///     The ring's colour at full opacity.
    /// </summary>
    public XColor RingColor => RingColorName is { } name ? KsTguiColors.Shade(name, 0f) : _ring.WithAlpha(DefaultRingAlpha);

    /// <summary>
    ///     Where the cursor points, in degrees from straight up.
    /// </summary>
    public float CursorDegrees => KsTguiKnobMath.CursorDegrees(Value, _minValue, _maxValue);

    /// <summary>
    ///     Where the ring's fill starts and how far it sweeps, in degrees clockwise from 3 o'clock; negative sweeps go
    ///         counter-clockwise.
    /// </summary>
    public (float Start, float Sweep) FillArc =>
        KsTguiKnobMath.FillArc(KsTguiKnobMath.Scale(_fillValue ?? Value, _minValue, _maxValue), _bipolar);

    /// <summary>
    ///     Shows a number from outside. While the player is dragging, theirs stays.
    /// </summary>
    public void SetValue(float value)
    {
        _value = value;
        if (!_pressed)
            _liveValue = value;

        SyncReadout();
    }

    /// <summary>
    ///     Puts the value on the readout: eased to while it is the settled one, at once while the player is dragging.
    /// </summary>
    private void SyncReadout()
    {
        if (!_showValue)
            return;

        _readout.Instant = _pressed;
        _readout.Value = _pressed ? _liveValue : _value;
    }

    /// <summary>
    ///     Whether a point at this height from the control's top is on the readout rather than the dial.
    /// </summary>
    public bool IsOverReadout(float relativeY)
    {
        return _showValue && relativeY >= KnobSide + ReadoutGap / 2f;
    }

    /// <summary>
    ///     A click on the readout: opens the typing box in its place, with everything selected. Nothing is reported until it
    ///         is committed.
    /// </summary>
    public void ClickReadout()
    {
        if (!_showValue || _disabled || _editing || _pressed)
            return;

        BeginEditing();
    }

    /// <summary>
    ///     A press on the knob. <paramref name="screenY"/> is the pointer's vertical position.
    /// </summary>
    public void BeginPress(float screenY)
    {
        if (_disabled || _editing)
            return;

        _effectiveStepPixelSize = _stepPixelSize
            ?? KsTguiKnobMath.DefaultStepPixelSize(KsTguiKnobMath.BaseSize * _knobSize, _minValue, _maxValue, _step);
        _pressed = true;
        _holdPassed = false;
        _pendingEdit = false;
        _moved = false;
        _held = 0f;
        _tickElapsed = 0f;
        _pressOrigin = screenY;
        _originalValue = _value;
        _liveValue = _value;
    }

    /// <summary>
    ///     The pointer moved while pressed. Only counts once the press has been held for <see cref="HoldSeconds"/>, as
    ///         DraggableControl only listens for moves from then; the distance is still from where it was pressed.
    /// </summary>
    public void DragTo(float screenY)
    {
        if (!_pressed || !_holdPassed)
            return;

        // The matrix is [0, -1]: up is more.
        var drag = _pressOrigin - screenY;
        var value = KsTguiKnobMath.DragValue(_originalValue, drag, _step, _effectiveStepPixelSize, _minValue, _maxValue);
        if (value != _liveValue)
            _moved = true;

        _liveValue = value;
        SyncReadout();
    }

    /// <summary>
    ///     The button was let go. As in tgui this is <see cref="OnChanged"/> every time, with the value the drag reached, or
    ///         the one it started on if it did not move. A release before <see cref="HoldSeconds"/> also starts editing, once
    ///         that time is up.
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
        SyncReadout();

        OnChanged?.Invoke(released);
    }

    /// <summary>
    ///     Time passing: the hold that makes a press a drag or a click, and the tick of <see cref="TickWhileDragging"/>.
    /// </summary>
    public void Advance(float seconds)
    {
        if (_pressed)
        {
            if (!_holdPassed)
            {
                _held += seconds;
                if (_held >= HoldSeconds)
                    _holdPassed = true;

                return;
            }

            _tickElapsed += seconds;
            while (_tickElapsed >= TickSeconds)
            {
                _tickElapsed -= TickSeconds;
                if (_tickWhileDragging)
                    OnChanged?.Invoke(_liveValue);
            }

            return;
        }

        if (!_pendingEdit)
            return;

        _held += seconds;
        if (_held < HoldSeconds)
            return;

        _pendingEdit = false;
        if (!_disabled)
            BeginEditing();
    }

    /// <summary>
    ///     Commits what is in the box, as Enter or losing focus does: held to the range unless <see cref="Unclamped"/>, and a
    ///         non-number cancels.
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
        SyncReadout();
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
        SyncReadout();
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
        _readout.Modulate = XColor.Transparent; // Hidden by Modulate, not Visible, so the layout holds.
        InvalidateArrange();
    }

    private void EndEditing()
    {
        if (!_editing)
            return;

        _editing = false;
        _edit.Visible = false;
        _readout.Modulate = XColor.White;
        if (_edit.HasKeyboardFocus())
            _edit.ReleaseKeyboardFocus();
    }

    private string FormatValue(float value)
    {
        var text = _format != null ? _format(value) : value.ToString("0.##", CultureInfo.CurrentCulture);
        return string.IsNullOrEmpty(_unit) ? text : text + " " + _unit;
    }

    private void ShowPopup()
    {
        _popupLabel.Text = FormatValue(_liveValue);

        var popupRoot = Root?.PopupRoot ?? UserInterfaceManager.PopupRoot;
        if (_popupHost.Parent == null)
        {
            // tgui blurs what is behind the value.
            _popupHost.Backdrop = UserInterfaceManager.GetUIController<KsTguiBackdropUIController>();
            popupRoot.AddChild(_popupHost);
        }

        _popupTransition.SetOpen(true);

        _popupHost.Measure(Vector2.PositiveInfinity);
        var popupSize = _popupHost.DesiredSize;
        var rootSize = popupRoot.Size;

        // Above the knob, centred, and below it when there is no room above; kept inside the window.
        var x = GlobalPosition.X + (Size.X - popupSize.X) / 2f;
        var y = GlobalPosition.Y - PopupOffset - popupSize.Y;
        _popupHost.PivotBottom = y >= 0f;
        if (y < 0f)
            y = GlobalPosition.Y + Size.Y + PopupOffset;

        if (rootSize.X > popupSize.X)
            x = Math.Clamp(x, 0f, rootSize.X - popupSize.X);

        LayoutContainer.SetPosition(_popupHost, new Vector2(x, y));
    }

    /// <summary>
    ///     Closes the popup the way tgui's Floating does: it fades and shrinks for 0.2 s and is removed when that is over. Opened
    ///         again meanwhile it turns round from where it got to.
    /// </summary>
    private void HidePopup()
    {
        _popupTransition.SetOpen(false);
    }

    /// <summary>
    ///     Moves the popup's transition on and puts what it says on the host; takes the host out once it has closed.
    /// </summary>
    public void AdvancePopup(float seconds)
    {
        if (_popupHost.Parent == null)
            return;

        _popupTransition.Advance(seconds);
        _popupHost.Scale = _popupTransition.Scale;
        _popupHost.Modulate = XColor.White.WithAlpha(_popupTransition.Alpha);

        if (!_popupTransition.Open && _popupTransition.Settled)
            RemovePopup();
    }

    private void RemovePopup()
    {
        _popupTransition.Reset();
        _popupHost.Scale = 1f;
        _popupHost.Modulate = XColor.White;

        if (_popupHost.Parent != null)
            _popupHost.Orphan();

        _popupHost.ReleaseBackdrop();
        _popup.ReleaseBackdrop();
    }

    protected override void ExitedTree()
    {
        base.ExitedTree();
        RemovePopup();
    }

    protected override void KeyBindDown(GUIBoundKeyEventArgs args)
    {
        base.KeyBindDown(args);

        if (args.Function != EngineKeyFunctions.UIClick || _disabled || _editing)
            return;

        if (IsOverReadout(args.RelativePosition.Y))
        {
            ClickReadout();
            args.Handle();
            return;
        }

        BeginPress(args.PointerLocation.Position.Y / UIScale);
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

        if (_showValue)
            DefaultCursorShape = !_pressed && IsOverReadout(args.RelativePosition.Y) ? CursorShape.Hand : CursorShape.VResize;

        if (_pressed)
            DragTo(args.GlobalPosition.Y);
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        Advance(args.DeltaSeconds);

        UpdatePopup(args.DeltaSeconds);
    }

    /// <summary>
    ///     What a frame does for the popup: shows and places it while the value is moving, then moves its transition on.
    /// </summary>
    public void UpdatePopup(float seconds)
    {
        if (PopupVisible)
            ShowPopup();

        AdvancePopup(seconds);
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        _edit.Measure(Vector2.PositiveInfinity);
        var side = KsTguiKnobMath.BaseSize * _knobSize;
        if (!_showValue)
            return new Vector2(side, side);

        return new Vector2(MeasuredWidth(), side + ReadoutGap + ReadoutHeight);
    }

    /// <summary>
    ///     With a readout the control is as wide as the widest number it can show, so the text changing never moves what
    ///         is beside it.
    /// </summary>
    private float MeasuredWidth()
    {
        var side = KsTguiKnobMath.BaseSize * _knobSize;
        if (!_showValue)
            return side;

        var font = TryGetStyleProperty<Font>("font", out var styled) ? styled : UserInterfaceManager.ThemeDefaults.DefaultFont;
        var widest = 0f;
        foreach (var value in new[] { _minValue, _maxValue, _value, _liveValue })
            widest = MathF.Max(widest, KsTguiFitText.StringWidth(font, FormatValue(value), UIScale) / UIScale);

        return MathF.Max(side, widest + 2f);
    }

    protected override Vector2 ArrangeOverride(Vector2 finalSize)
    {
        if (_showValue)
        {
            // The box opens where the number is read.
            var readoutBox = UIBox2.FromDimensions(0f, KnobSide + ReadoutGap, finalSize.X, ReadoutHeight);
            _readout.Arrange(readoutBox);
            _edit.Arrange(readoutBox);
            return finalSize;
        }

        _edit.Arrange(UIBox2.FromDimensions(Vector2.Zero, finalSize));
        return finalSize;
    }

    protected override void StylePropertiesChanged()
    {
        base.StylePropertiesChanged();

        _ring = TryGetStyleProperty<XColor>(KsTguiSheetlet.StylePropertyGaugeRing, out var ring) ? ring : KsTguiPalettes.InputBorder;
        _windowBase = TryGetStyleProperty<XColor>(KsTguiSheetlet.StylePropertyWindowBase, out var windowBase) ? windowBase : KsTguiPalettes.Base;
        _popupBox.BackgroundColor = TryGetStyleProperty<XColor>(KsTguiSheetlet.StylePropertyTooltip, out var tooltip) ? tooltip : KsTguiPalettes.Base;
        _popupBox.Tint = TryGetStyleProperty<KsTguiTint>(KsTguiSheetlet.StylePropertyTooltipTint, out var tooltipTint) ? tooltipTint : null;
        _editBox.BackgroundColor = TryGetStyleProperty<XColor>(KsTguiSheetlet.StylePropertyInputBackground, out var background) ? background : EditBackground;
    }

    // Everything below draws the knob, in device pixels, from the same em the css uses.

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        // The dial is the square at the top; with a readout the control is wider and taller than that.
        var sidePixels = KnobSide * UIScale;
        var vertices = BuildVertices(UIScale, new Vector2(sidePixels, sidePixels), handle.Modulate);
        if (vertices.Count == 0)
            return;

        handle.SetTransform(Matrix3x2.CreateTranslation((PixelSize.X - sidePixels) / 2f, 0f));
        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList, Texture.White, vertices.AsSpan());
        handle.SetTransform(Matrix3x2.Identity);
    }

    /// <summary>
    ///     The knob as triangles in device pixels: glow, cap, cursor, then the ring's track and fill. Public so that what is
    ///         drawn can be checked without a screen.
    /// </summary>
    /// <remarks>A segment, not a span: ILVerify, and so the sandbox, refuses a method that returns a span.</remarks>
    public ArraySegment<DrawVertexUV2DColor> BuildVertices(float uiScale, Vector2 pixelSize, XColor modulate)
    {
        _modulate = modulate;
        _vertexCount = 0;

        var em = KsTguiKnobMath.Em * _knobSize * uiScale;
        var centre = pixelSize / 2f;
        var hue = XColor.ToHsl(_windowBase).X * 360f;
        var capColor = KsTguiColors.FromHsl(hue, CapSaturation, CapLightness);
        var capTop = KsTguiColors.FromHsl(hue, CapSaturation, CapTopLightness);
        var cursorColor = KsTguiColors.FromHsl(hue, CapSaturation, CursorLightness);
        var circleRadius = KsTguiKnobMath.CircleRadiusEm * em;

        AppendGlow(centre, circleRadius, capColor.WithAlpha(GlowAlpha), GlowBlur * uiScale);
        AppendCircle(centre, circleRadius, capTop, capColor);
        AppendCursor(centre, circleRadius, em, cursorColor);

        var ring = RingColor;
        var ringRadius = KsTguiKnobMath.RingRadiusEm * em;
        var halfWidth = KsTguiKnobMath.RingWidthEm * em / 2f;
        AppendArc(centre, ringRadius, halfWidth, KsTguiKnobMath.RingStartDegrees, KsTguiKnobMath.TrackSweepDegrees,
            ring.WithAlpha(KsTguiKnobMath.TrackAlpha));

        var (start, sweep) = FillArc;
        AppendArc(centre, ringRadius, halfWidth, start, sweep, ring);

        return new ArraySegment<DrawVertexUV2DColor>(_vertices, 0, _vertexCount);
    }

    // Raw vertices don't get the handle's modulate, so it is applied here. Their colour stays sRGB (see KsTguiStyleBox).
    private void AppendVertex(Vector2 position, XColor color)
    {
        if (_vertexCount == _vertices.Length)
            Array.Resize(ref _vertices, _vertices.Length * 2);

        _vertices[_vertexCount++] = new DrawVertexUV2DColor(position, color * _modulate);
    }

    private void AppendTriangle(Vector2 a, XColor colorA, Vector2 b, XColor colorB, Vector2 c, XColor colorC)
    {
        AppendVertex(a, colorA);
        AppendVertex(b, colorB);
        AppendVertex(c, colorC);
    }

    private void AppendQuad(Vector2 a, XColor colorA, Vector2 b, XColor colorB, Vector2 c, XColor colorC, Vector2 d, XColor colorD)
    {
        AppendTriangle(a, colorA, b, colorB, c, colorC);
        AppendTriangle(a, colorA, c, colorC, d, colorD);
    }

    private static Vector2 OnCircle(Vector2 centre, float radius, float radians)
    {
        return centre + new Vector2(MathF.Cos(radians), MathF.Sin(radians)) * radius;
    }

    private static int SegmentsFor(float radius, float radians)
    {
        // About one segment per two device pixels of rim.
        return Math.Clamp((int)MathF.Ceiling(MathF.Abs(radians) * radius / 2f), 6, 256);
    }

    /// <summary>
    ///     A disc with a top to bottom gradient, as <c>linear-gradient(to bottom, ...)</c>, and a soft edge.
    /// </summary>
    private void AppendCircle(Vector2 centre, float radius, XColor top, XColor bottom)
    {
        XColor ColorAt(float y)
        {
            return XColor.InterpolateBetween(top, bottom, Math.Clamp((y - (centre.Y - radius)) / (2f * radius), 0f, 1f));
        }

        var inner = radius - Fringe / 2f;
        var outer = radius + Fringe / 2f;
        var segments = SegmentsFor(radius, MathF.Tau);
        var centreColor = ColorAt(centre.Y);

        for (var i = 0; i < segments; i++)
        {
            var angleA = MathF.Tau * i / segments;
            var angleB = MathF.Tau * (i + 1) / segments;
            var innerA = OnCircle(centre, inner, angleA);
            var innerB = OnCircle(centre, inner, angleB);
            var outerA = OnCircle(centre, outer, angleA);
            var outerB = OnCircle(centre, outer, angleB);

            AppendTriangle(centre, centreColor, innerA, ColorAt(innerA.Y), innerB, ColorAt(innerB.Y));
            AppendQuad(innerA, ColorAt(innerA.Y), outerA, ColorAt(outerA.Y).WithAlpha(0f),
                outerB, ColorAt(outerB.Y).WithAlpha(0f), innerB, ColorAt(innerB.Y));
        }
    }

    /// <summary>
    ///     <c>box-shadow: 0 0 blur colour</c> around a disc: the colour's alpha fading out along a Gaussian edge. It starts
    ///         under the disc, which covers it.
    /// </summary>
    private void AppendGlow(Vector2 centre, float radius, XColor color, float blur)
    {
        var segments = SegmentsFor(radius + blur, MathF.Tau);
        var previousRadius = radius - Fringe;
        var previousAlpha = color.A * KsTguiKnobMath.ShadowFalloff(0f, blur);

        for (var ring = 0; ring <= GlowRings; ring++)
        {
            var distance = blur * ring / GlowRings;
            var currentRadius = radius + distance;
            var currentAlpha = color.A * KsTguiKnobMath.ShadowFalloff(distance, blur);

            for (var i = 0; i < segments; i++)
            {
                var angleA = MathF.Tau * i / segments;
                var angleB = MathF.Tau * (i + 1) / segments;
                AppendQuad(
                    OnCircle(centre, previousRadius, angleA), color.WithAlpha(previousAlpha),
                    OnCircle(centre, currentRadius, angleA), color.WithAlpha(currentAlpha),
                    OnCircle(centre, currentRadius, angleB), color.WithAlpha(currentAlpha),
                    OnCircle(centre, previousRadius, angleB), color.WithAlpha(previousAlpha));
            }

            previousRadius = currentRadius;
            previousAlpha = currentAlpha;
        }
    }

    /// <summary>
    ///     The cursor: <c>0.2em</c> by <c>0.8em</c>, <c>0.05em</c> below the cap's top, turned about the cap's centre.
    /// </summary>
    private void AppendCursor(Vector2 centre, float circleRadius, float em, XColor color)
    {
        var radians = CursorDegrees * MathF.PI / 180f;
        var right = new Vector2(MathF.Cos(radians), MathF.Sin(radians));
        var down = new Vector2(-MathF.Sin(radians), MathF.Cos(radians));

        var top = -circleRadius + 0.05f * em;
        var bottom = top + 0.8f * em;
        var half = 0.1f * em;
        var soft = Fringe / 2f;

        Vector2 Corner(float x, float y)
        {
            return centre + right * x + down * y;
        }

        var inner = new[]
        {
            Corner(-half + soft, top + soft), Corner(half - soft, top + soft),
            Corner(half - soft, bottom - soft), Corner(-half + soft, bottom - soft),
        };
        var outer = new[]
        {
            Corner(-half - soft, top - soft), Corner(half + soft, top - soft),
            Corner(half + soft, bottom + soft), Corner(-half - soft, bottom + soft),
        };
        var clear = color.WithAlpha(0f);

        AppendTriangle(inner[0], color, inner[1], color, inner[2], color);
        AppendTriangle(inner[0], color, inner[2], color, inner[3], color);

        for (var i = 0; i < 4; i++)
        {
            var next = (i + 1) % 4;
            AppendQuad(inner[i], color, outer[i], clear, outer[next], clear, inner[next], color);
        }
    }

    /// <summary>
    ///     A stroked arc with round caps, as the svg circle with a dash array and <c>stroke-linecap: round</c>: one shape, so a
    ///         translucent colour is not doubled where the caps meet the stroke. A full turn has no caps.
    /// </summary>
    private void AppendArc(Vector2 centre, float radius, float halfWidth, float startDegrees, float sweepDegrees, XColor color)
    {
        if (MathF.Abs(sweepDegrees) < 0.01f || color.A <= 0f)
            return;

        var start = startDegrees * MathF.PI / 180f;
        var sweep = sweepDegrees * MathF.PI / 180f;
        var soft = Fringe / 2f;
        var clear = color.WithAlpha(0f);

        // Four radii across the stroke: the outside of the soft edge, the solid start, the solid end, the outside again.
        var radii = new[] { radius - halfWidth - soft, radius - halfWidth + soft, radius + halfWidth - soft, radius + halfWidth + soft };
        var colors = new[] { clear, color, color, clear };
        var segments = SegmentsFor(radius, sweep);

        for (var i = 0; i < segments; i++)
        {
            var angleA = start + sweep * i / segments;
            var angleB = start + sweep * (i + 1) / segments;
            for (var row = 0; row < 3; row++)
            {
                AppendQuad(
                    OnCircle(centre, radii[row], angleA), colors[row],
                    OnCircle(centre, radii[row + 1], angleA), colors[row + 1],
                    OnCircle(centre, radii[row + 1], angleB), colors[row + 1],
                    OnCircle(centre, radii[row], angleB), colors[row]);
            }
        }

        if (MathF.Abs(sweepDegrees) >= 360f)
            return;

        var direction = MathF.Sign(sweep);
        AppendCap(centre, radius, halfWidth, start, -direction, color);
        AppendCap(centre, radius, halfWidth, start + sweep, direction, color);
    }

    /// <summary>
    ///     A half disc on the end of a stroke, bulging along <paramref name="outward"/> (+1 along increasing angle).
    /// </summary>
    private void AppendCap(Vector2 centre, float radius, float halfWidth, float angle, float outward, XColor color)
    {
        var point = OnCircle(centre, radius, angle);
        var radial = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
        var tangent = new Vector2(-MathF.Sin(angle), MathF.Cos(angle)) * outward;
        var soft = Fringe / 2f;
        var clear = color.WithAlpha(0f);
        const int segments = 12;

        for (var i = 0; i < segments; i++)
        {
            var psiA = -MathF.PI / 2f + MathF.PI * i / segments;
            var psiB = -MathF.PI / 2f + MathF.PI * (i + 1) / segments;
            var directionA = radial * MathF.Sin(psiA) + tangent * MathF.Cos(psiA);
            var directionB = radial * MathF.Sin(psiB) + tangent * MathF.Cos(psiB);

            AppendTriangle(point, color, point + directionA * (halfWidth - soft), color, point + directionB * (halfWidth - soft), color);
            AppendQuad(
                point + directionA * (halfWidth - soft), color,
                point + directionA * (halfWidth + soft), clear,
                point + directionB * (halfWidth + soft), clear,
                point + directionB * (halfWidth - soft), color);
        }
    }
}
