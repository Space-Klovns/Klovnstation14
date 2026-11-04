using System.Globalization;
using System.Numerics;
using Robust.Client.UserInterface;
using Robust.Shared.Input;

namespace Content.Client._KS14.UserInterface.Tgui;

/// <summary>
///     The drag-to-change behaviour tgui's NumberInput and Knob share: press and drag up or right to raise the value
///         and down or left to lower it, one <see cref="Step"/> every <see cref="StepPixelSize"/> pixels.
///         A press that doesn't move is a click, which <see cref="Clicked"/> handles.
/// </summary>
public abstract class KsTguiDragNumber : Control
{
    private float _value;
    private float _minValue;
    private float _maxValue = 100f;
    private float _step = 1f;
    private string? _unit;
    private int? _decimals;

    private bool _dragging;
    private bool _moved;
    private Vector2 _dragOrigin;
    private float _dragStartValue;

    protected KsTguiDragNumber()
    {
        MouseFilter = MouseFilterMode.Stop;
        DefaultCursorShape = CursorShape.VResize;
    }

    /// <summary>
    ///     Raised with the new value when the user commits one: at the end of a drag (or every step of it, with
    ///         <see cref="TickWhileDragging"/>), or through <see cref="SubmitValue"/>. Setting <see cref="Value"/> doesn't.
    /// </summary>
    public event Action<float>? OnValueChanged;

    /// <summary>
    ///     Raised whenever the shown value changes, including every step of a drag. For labels that follow the control.
    /// </summary>
    public event Action<float>? OnValueShown;

    public float Value
    {
        get => _value;
        set
        {
            // The server's echo must not yank the number out from under a drag or a half-typed value.
            if (_dragging || IsEditing)
                return;

            _value = Clamp(value);
            Refresh();
        }
    }

    public float MinValue
    {
        get => _minValue;
        set
        {
            _minValue = value;
            Refresh();
        }
    }

    public float MaxValue
    {
        get => _maxValue;
        set
        {
            _maxValue = value;
            Refresh();
        }
    }

    public float Step
    {
        get => _step;
        set
        {
            _step = MathF.Max(value, 0.0001f);
            Refresh();
        }
    }

    /// <summary>
    ///     How far, in virtual pixels, the mouse has to move for one <see cref="Step"/>.
    /// </summary>
    public float StepPixelSize { get; set; } = 1f;

    /// <summary>
    ///     Send every step of a drag rather than only where it ends.
    /// </summary>
    public bool TickWhileDragging { get; set; }

    public string? Unit
    {
        get => _unit;
        set
        {
            _unit = value;
            Refresh();
        }
    }

    /// <summary>
    ///     Decimal places shown. By default as many as <see cref="Step"/> has.
    /// </summary>
    public int Decimals
    {
        get => _decimals ?? DecimalsOf(_step);
        set
        {
            _decimals = value;
            Refresh();
        }
    }

    public bool IsDragging => _dragging;

    public virtual bool IsEditing => false;

    /// <summary>
    ///     How far along its range the value is, from 0 to 1.
    /// </summary>
    public float Fraction => _maxValue > _minValue ? Math.Clamp((_value - _minValue) / (_maxValue - _minValue), 0f, 1f) : 0f;

    /// <summary>
    ///     Sets the value as if the user had entered it, raising <see cref="OnValueChanged"/>. For preset buttons.
    /// </summary>
    public void SubmitValue(float value)
    {
        if (!float.IsFinite(value))
            return;

        _value = Clamp(value);
        Refresh();
        OnValueChanged?.Invoke(_value);
    }

    /// <summary>
    ///     The value with its unit, as shown.
    /// </summary>
    public string FormatValue()
    {
        var number = FormatNumber(_value);
        return string.IsNullOrEmpty(_unit) ? number : Loc.GetString("ks-tgui-number-with-unit", ("number", number), ("unit", _unit));
    }

    protected string FormatNumber(float value)
    {
        return value.ToString("F" + Decimals, CultureInfo.CurrentCulture);
    }

    /// <summary>
    ///     The value, range or format changed and the display should follow.
    /// </summary>
    protected abstract void ValueUpdated();

    private void Refresh()
    {
        ValueUpdated();
        OnValueShown?.Invoke(_value);
    }

    /// <summary>
    ///     The control was pressed and released without dragging.
    /// </summary>
    protected virtual void Clicked()
    {
    }

    protected override void KeyBindDown(GUIBoundKeyEventArgs args)
    {
        base.KeyBindDown(args);

        if (args.Function != EngineKeyFunctions.UIClick || IsEditing)
            return;

        _dragging = true;
        _moved = false;
        _dragOrigin = args.PointerLocation.Position;
        _dragStartValue = _value;
        args.Handle();
    }

    protected override void MouseMove(GUIMouseMoveEventArgs args)
    {
        base.MouseMove(args);

        if (!_dragging)
            return;

        // tgui's drag matrix: right and up both count as more.
        var delta = (args.GlobalPixelPosition.Position - _dragOrigin) / UIScale;
        var steps = MathF.Truncate((delta.X - delta.Y) / MathF.Max(StepPixelSize, 0.01f));
        var newValue = Clamp(MathF.Round(_dragStartValue + steps * _step, Math.Min(Decimals, 6)));

        if (MathHelper.CloseTo(newValue, _value))
            return;

        _moved = true;
        _value = newValue;
        Refresh();

        if (TickWhileDragging)
            OnValueChanged?.Invoke(_value);
    }

    protected override void KeyBindUp(GUIBoundKeyEventArgs args)
    {
        base.KeyBindUp(args);

        if (args.Function != EngineKeyFunctions.UIClick || !_dragging)
            return;

        _dragging = false;
        Refresh();

        if (!_moved)
        {
            Clicked();
            return;
        }

        if (!TickWhileDragging)
            OnValueChanged?.Invoke(_value);
    }

    protected float Clamp(float value)
    {
        return _maxValue >= _minValue ? Math.Clamp(value, _minValue, _maxValue) : value;
    }

    private static int DecimalsOf(float step)
    {
        var decimals = 0;
        while (decimals < 4 && MathF.Abs(step - MathF.Round(step, decimals)) > 0.00001f)
        {
            decimals++;
        }

        return decimals;
    }
}
