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
///     tgui-core's NumberInput. Drawn as a dark box with a thin bar at its left showing the value within its range. Dragging
///         it vertically changes the value by <see cref="Step"/> for every <see cref="StepPixelSize"/> pixels, shown live and
///         committed on release. A click that was not a drag turns it into a text box with everything selected: Enter
///         commits, Escape reverts, and losing focus commits. A value the server sends is not written over what the player
///         is typing.
/// </summary>
public sealed class KsTguiNumberInput : Control
{
    /// <summary>
    ///     <c>--space-m</c>, the content's left margin.
    /// </summary>
    private const float ContentMarginLeft = 6f;

    /// <summary>
    ///     <c>--space-sm</c>, the horizontal padding.
    /// </summary>
    private const float PaddingHorizontal = 4f;

    /// <summary>
    ///     <c>--space-xs</c>, the bar's inset.
    /// </summary>
    private const float BarInset = 2f;

    private const float BarWidth = 3f;

    /// <summary>
    ///     tgui's <c>tickWhileDragging</c> interval.
    /// </summary>
    private const float TickSeconds = 0.4f;

    /// <summary>
    ///     How long a press must be held to count as a drag without moving, and how far it must move to count as one sooner.
    /// </summary>
    private const float DragHoldSeconds = 0.25f;
    private const float DragStartPixels = 4f;

    private static readonly XColor Background = XColor.FromHex("#0D0D0D");
    private static readonly XColor BorderIdle = XColor.FromHex("#6392C2");

    private readonly LineEdit _edit;
    private readonly Label _content;
    private readonly KsTguiStyleBox _box = new(Background, 2f) { BorderThickness = new Thickness(1f) };

    private float _value;
    private float _minValue;
    private float _maxValue = float.MaxValue;
    private float _step = 1f;
    private float _stepPixelSize = 1f;
    private float _widthPixels;
    private bool _tickWhileDragging;
    private float _tickHeld;
    private float _tickedValue;
    private float _lineHeight = 17f;
    private int _fontSize;
    private string _format = "0.##";
    private string? _unit;
    private bool _disabled;

    private bool _pressed;
    private bool _dragging;
    private bool _editing;
    private float _dragOrigin;
    private float _dragHeld;
    private float _currentValue;

    /// <summary>
    ///     Raised with the clamped number when the player commits one that is not what the box already showed.
    /// </summary>
    public event Action<float>? OnCommitted;

    public KsTguiNumberInput()
    {
        MouseFilter = MouseFilterMode.Stop;
        HorizontalAlignment = HAlignment.Left;
        DefaultCursorShape = CursorShape.VResize;
        RectClipContent = true;

        _content = new Label { Align = Label.AlignMode.Right, VerticalAlignment = VAlignment.Center };
        _content.MouseFilter = MouseFilterMode.Ignore;
        _content.FontColorOverride = KsTguiPalettes.InputBorder;

        var editBox = new KsTguiStyleBox(Background);
        editBox.SetContentMarginOverride(StyleBox.Margin.Horizontal, ContentMarginLeft);
        editBox.SetContentMarginOverride(StyleBox.Margin.Vertical, 0f);
        _edit = new LineEdit { Visible = false, StyleBoxOverride = editBox, SelectAllOnFocus = true };

        AddChild(_content);
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

        RefreshContent();
    }

    public LineEdit Edit => _edit;

    /// <summary>
    ///     The value shown: the one last set or committed, or the live one while dragging.
    /// </summary>
    public float Value => _dragging ? _currentValue : _value;

    public bool IsEditing => _editing;

    public bool IsDragging => _dragging;

    public float MinValue
    {
        get => _minValue;
        set
        {
            _minValue = value;
            InvalidateBar();
        }
    }

    public float MaxValue
    {
        get => _maxValue;
        set
        {
            _maxValue = value;
            InvalidateBar();
        }
    }

    /// <summary>
    ///     How much a drag changes the value by, and what it rounds to.
    /// </summary>
    public float Step
    {
        get => _step;
        set => _step = value > 0f ? value : 1f;
    }

    /// <summary>
    ///     How many pixels of drag make one <see cref="Step"/>.
    /// </summary>
    public float StepPixelSize
    {
        get => _stepPixelSize;
        set => _stepPixelSize = value > 0f ? value : 1f;
    }

    /// <summary>
    ///     The unit shown after the number.
    /// </summary>
    public string? Unit
    {
        get => _unit;
        set
        {
            _unit = value;
            RefreshContent();
        }
    }

    public string Format
    {
        get => _format;
        set
        {
            _format = value;
            RefreshContent();
        }
    }

    /// <summary>
    ///     tgui's <c>width</c>: a minimum width in pixels. Zero fits the content.
    /// </summary>
    public float WidthPixels
    {
        get => _widthPixels;
        set
        {
            _widthPixels = value;
            InvalidateMeasure();
        }
    }

    /// <summary>
    ///     tgui's <c>lineHeight</c> in pixels, which with the border sets the control's height.
    /// </summary>
    public float LineHeight
    {
        get => _lineHeight;
        set
        {
            _lineHeight = value;
            InvalidateMeasure();
        }
    }

    /// <summary>
    ///     tgui's <c>fontSize</c> in pixels. Zero is the theme's.
    /// </summary>
    public int FontSize
    {
        get => _fontSize;
        set
        {
            _fontSize = value;
            var font = value > 0 ? IoCManager.Resolve<KsTguiFonts>().GetFont(value) : null;
            _content.FontOverride = font;
            InvalidateMeasure();
        }
    }

    /// <summary>
    ///     tgui's <c>fluid</c>: fills the width it is given.
    /// </summary>
    public bool Fluid
    {
        get => HorizontalExpand;
        set
        {
            HorizontalExpand = value;
            HorizontalAlignment = value ? HAlignment.Stretch : HAlignment.Left;
        }
    }

    /// <summary>
    ///     tgui's <c>tickWhileDragging</c>: raises <see cref="OnCommitted"/> every 0.4 seconds of a drag when the value moved.
    /// </summary>
    public bool TickWhileDragging
    {
        get => _tickWhileDragging;
        set => _tickWhileDragging = value;
    }

    public bool Disabled
    {
        get => _disabled;
        set
        {
            _disabled = value;
            if (value && _editing)
                Cancel();
        }
    }

    /// <summary>
    ///     The fraction of the range the shown value is at, 0 to 1.
    /// </summary>
    public float BarFraction
    {
        get
        {
            var span = _maxValue - _minValue;
            var fraction = (Value - _minValue) / span;
            return float.IsFinite(fraction) ? Math.Clamp(fraction, 0f, 1f) : 0f;
        }
    }

    /// <summary>
    ///     Shows a number from outside. While the player is dragging or typing, theirs stays.
    /// </summary>
    public void SetValue(float value)
    {
        _value = value;

        if (!_editing && !_edit.HasKeyboardFocus())
            _edit.Text = value.ToString(_format, CultureInfo.CurrentCulture);

        RefreshContent();
    }

    /// <summary>
    ///     Commits what is in the box, as Enter or losing focus does. Text that is not a number is dropped.
    /// </summary>
    public void Commit()
    {
        EndEditing();

        if (_disabled)
            return;

        if (!UserInputParser.TryFloat(_edit.Text, out var parsed) || !float.IsFinite(parsed))
        {
            _edit.Text = _value.ToString(_format, CultureInfo.CurrentCulture);
            return;
        }

        var clamped = Clamp(parsed);
        _edit.Text = clamped.ToString(_format, CultureInfo.CurrentCulture);

        if (MathF.Abs(clamped - _value) < 0.0001f)
            return;

        _value = clamped;
        RefreshContent();
        OnCommitted?.Invoke(clamped);
    }

    /// <summary>
    ///     Escape: stops editing and puts back the value there was.
    /// </summary>
    public void Cancel()
    {
        EndEditing();
        _edit.Text = _value.ToString(_format, CultureInfo.CurrentCulture);
    }

    /// <summary>
    ///     A press on the control. <paramref name="screenY"/> is the pointer's vertical position.
    /// </summary>
    public void BeginDrag(float screenY)
    {
        if (_disabled || _editing)
            return;

        _pressed = true;
        _dragging = false;
        _currentValue = _value;
        _dragOrigin = screenY;
        _dragHeld = 0f;
        _tickHeld = 0f;
        _tickedValue = _value;
    }

    /// <summary>
    ///     The pointer moved while pressed. Mirrors tgui's <c>handleDragMove</c>: the value moves by
    ///         <c>distance * step / stepPixelSize</c>, held to one step beyond the range, and takes the rounded result only
    ///         once that is at least a step away from where it is.
    /// </summary>
    public void DragMove(float screenY)
    {
        if (!_pressed || _disabled)
            return;

        var distance = _dragOrigin - screenY;
        if (!_dragging)
        {
            if (MathF.Abs(distance) > DragStartPixels)
                _dragging = true;

            RefreshContent();
            return;
        }

        var target = Math.Clamp(_currentValue + distance * _step / _stepPixelSize, _minValue - _step, _maxValue + _step);
        if (MathF.Abs(target - _currentValue) >= _step)
        {
            _currentValue = Math.Clamp(MathF.Round(target / _step, MidpointRounding.AwayFromZero) * _step, _minValue, _maxValue);
            _dragOrigin = screenY;
        }
        else if (MathF.Abs(distance) > _stepPixelSize)
        {
            _dragOrigin = screenY;
        }

        RefreshContent();
    }

    /// <summary>
    ///     The release. A drag commits its value, anything else starts editing.
    /// </summary>
    public void EndDrag()
    {
        if (!_pressed)
            return;

        _pressed = false;
        var wasDragging = _dragging;
        _dragging = false;

        if (_disabled)
            return;

        if (wasDragging)
        {
            var released = _currentValue;
            if (MathF.Abs(released - _value) >= 0.0001f)
            {
                _value = released;
                _edit.Text = released.ToString(_format, CultureInfo.CurrentCulture);
                RefreshContent();
                OnCommitted?.Invoke(released);
            }
            else
            {
                RefreshContent();
            }

            return;
        }

        BeginEditing();
    }

    private void BeginEditing()
    {
        _editing = true;
        _edit.Visible = true;
        _edit.Text = _value.ToString(_format, CultureInfo.CurrentCulture);
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

    private float Clamp(float value)
    {
        return Math.Clamp(value, _minValue, MathF.Max(_minValue, _maxValue));
    }

    private void InvalidateBar()
    {
        // Drawn from the range each frame; nothing to lay out.
    }

    private void RefreshContent()
    {
        var text = Value.ToString(_format, CultureInfo.CurrentCulture);
        _content.Text = string.IsNullOrEmpty(_unit) ? text : text + " " + _unit;
        InvalidateMeasure();
    }

    protected override void KeyBindDown(GUIBoundKeyEventArgs args)
    {
        base.KeyBindDown(args);

        if (args.Function != EngineKeyFunctions.UIClick || _disabled || _editing)
            return;

        BeginDrag(args.PointerLocation.Position.Y / UIScale);
        args.Handle();
    }

    protected override void KeyBindUp(GUIBoundKeyEventArgs args)
    {
        base.KeyBindUp(args);

        if (args.Function != EngineKeyFunctions.UIClick)
            return;

        EndDrag();
        args.Handle();
    }

    protected override void MouseMove(GUIMouseMoveEventArgs args)
    {
        base.MouseMove(args);

        if (_pressed)
            DragMove(args.GlobalPosition.Y);
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        if (_pressed && _dragging && _tickWhileDragging)
        {
            _tickHeld += args.DeltaSeconds;
            if (_tickHeld >= TickSeconds)
            {
                _tickHeld = 0f;
                if (MathF.Abs(_currentValue - _tickedValue) >= 0.0001f)
                {
                    _tickedValue = _currentValue;
                    OnCommitted?.Invoke(_currentValue);
                }
            }
        }

        if (!_pressed || _dragging)
            return;

        _dragHeld += args.DeltaSeconds;
        if (_dragHeld >= DragHoldSeconds)
        {
            _dragging = true;
            RefreshContent();
        }
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        _content.Measure(Vector2.PositiveInfinity);
        _edit.Measure(Vector2.PositiveInfinity);

        var contentWidth = _content.DesiredSize.X + ContentMarginLeft + 2f * (PaddingHorizontal + 1f);
        var width = MathF.Max(_widthPixels, contentWidth);
        var height = MathF.Max(_lineHeight, _content.DesiredSize.Y) + 2f;
        return new Vector2(width, height);
    }

    protected override Vector2 ArrangeOverride(Vector2 finalSize)
    {
        var inner = new UIBox2(1f, 1f, MathF.Max(1f, finalSize.X - 1f), MathF.Max(1f, finalSize.Y - 1f));
        var contentBox = new UIBox2(
            inner.Left + PaddingHorizontal + ContentMarginLeft,
            inner.Top,
            inner.Right - PaddingHorizontal,
            inner.Bottom);

        _content.Arrange(contentBox);
        _edit.Arrange(inner);
        return finalSize;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        var active = _pressed || _editing;
        _box.BackgroundColor = Background;
        _box.BorderColor = active ? KsTguiPalettes.InputBorder : BorderIdle;
        _box.Draw(handle, PixelSizeBox, UIScale);

        // The bar sits in the padding box, inset on every side but the right, and grows from its bottom.
        var barTop = (1f + BarInset) * UIScale;
        var barBottom = PixelSize.Y - (1f + BarInset) * UIScale;
        var barHeight = (barBottom - barTop) * BarFraction;
        if (barHeight > 0f)
        {
            var left = (1f + BarInset) * UIScale;
            handle.DrawRect(new UIBox2(left, barBottom - barHeight, left + BarWidth * UIScale, barBottom), KsTguiPalettes.InputBorder);
        }

        // Children draw after this, so the text is over the bar, as in tgui.
    }
}
