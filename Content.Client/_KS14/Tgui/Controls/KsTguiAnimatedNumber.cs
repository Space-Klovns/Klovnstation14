using System.Globalization;
using Robust.Shared.Timing;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._KS14.Tgui.Controls;

/// <summary>
///     tgui-core's AnimatedNumber: a number that eases to its new value instead of jumping. Each 1/60 s closes a sixth of
///         the distance (<c>0.8333 ** (ms / 16.67)</c>), so the step is by frame time and looks the same at any frame rate.
///         It shows <see cref="Format"/> of the value, or, as tgui, the target's own decimals (<c>1.5</c> shows one).
/// </summary>
/// <remarks>
///     Once it reaches the target it does nothing: <see cref="FrameUpdate"/> returns at the first line until the next
///         change, and setting <see cref="Value"/> to what it already is costs a compare.
/// </remarks>
public sealed class KsTguiAnimatedNumber : Label
{
    /// <summary>
    ///     tgui steps in these, whatever the frame rate.
    /// </summary>
    private const float StepSeconds = 1f / 60f;

    /// <summary>
    ///     What is left of the distance after one step.
    /// </summary>
    private const float Decay = 0.8333f;

    private float _target;
    private float _current;
    private float? _initial;
    private bool _hasValue;
    private bool _animating;
    private int _decimals;
    private Func<float, string>? _format;

    /// <summary>
    ///     Where the first <see cref="Value"/> starts from (tgui's <c>initial</c>); without it the first value is shown at once.
    /// </summary>
    public float? Initial
    {
        get => _initial;
        set => _initial = value;
    }

    /// <summary>
    ///     The number being eased to.
    /// </summary>
    public float Value
    {
        get => _target;
        set
        {
            if (!float.IsFinite(value))
                return;

            if (_hasValue && _target == value)
                return;

            _target = value;
            _decimals = DecimalsOf(value);

            if (!_hasValue)
            {
                _hasValue = true;
                _current = _initial ?? value;
            }

            _animating = _current != value;
            if (!_animating)
                _current = value;

            Refresh();
        }
    }

    /// <summary>
    ///     What is shown right now, which is the target once <see cref="Animating"/> is false.
    /// </summary>
    public float Displayed => _current;

    /// <summary>
    ///     Whether it is still easing, and so doing work each frame.
    /// </summary>
    public bool Animating => _animating;

    /// <summary>
    ///     Turns a value into the text, such as a money format. Null shows the target's decimals.
    /// </summary>
    public Func<float, string>? Format
    {
        get => _format;
        set
        {
            _format = value;
            Refresh();
        }
    }

    /// <summary>
    ///     One step of the tween. Settled gives the target itself, so the text ends exactly on it, not 0.0009 short.
    /// </summary>
    public static float Step(float current, float target, float frameSeconds, out bool settled)
    {
        var keep = MathF.Pow(Decay, frameSeconds / StepSeconds);
        var next = current * keep + target * (1f - keep);

        if (MathF.Abs(target - next) < MathF.Max(0.001f, 0.001f * target))
        {
            settled = true;
            return target;
        }

        settled = false;
        return next;
    }

    /// <summary>
    ///     How many digits tgui keeps: those after the point in the value's own text, up to 8.
    /// </summary>
    public static int DecimalsOf(float value)
    {
        var text = value.ToString("R", CultureInfo.InvariantCulture);
        if (text.Contains('E', StringComparison.Ordinal))
            return 0;

        var point = text.IndexOf('.', StringComparison.Ordinal);
        return point < 0 ? 0 : Math.Clamp(text.Length - point - 1, 0, 8);
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        if (!_animating)
            return;

        _current = Step(_current, _target, args.DeltaSeconds, out var settled);
        _animating = !settled;
        Refresh();
    }

    private void Refresh()
    {
        if (!_hasValue)
            return;

        Text = _format != null
            ? _format(_current)
            : _current.ToString("F" + _decimals, CultureInfo.CurrentCulture);
    }
}
