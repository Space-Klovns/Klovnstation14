using System.Globalization;
using Content.Shared.Localizations;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._KS14.Tgui.Windows.Atmos;

/// <summary>
///     tgui's NumberInput as far as a window needs it: a number with its unit after it, committed when the player presses
///         Enter or leaves the box, clamped to a range. A value the server sends is not written over what the player is
///         typing.
/// </summary>
public sealed class KsTguiNumberInput : BoxContainer
{
    private readonly LineEdit _edit;
    private readonly Label _unitLabel;

    private float _value;
    private float _minValue;
    private float _maxValue = float.MaxValue;
    private string _format = "0.##";

    /// <summary>
    ///     Raised with the clamped number when the player commits one that is not what the box already showed.
    /// </summary>
    public event Action<float>? OnCommitted;

    public KsTguiNumberInput()
    {
        Orientation = LayoutOrientation.Horizontal;
        SeparationOverride = 4;

        _edit = new LineEdit { MinWidth = 64f, HorizontalExpand = true };
        _unitLabel = new Label { VerticalAlignment = VAlignment.Center, Visible = false };
        AddChild(_edit);
        AddChild(_unitLabel);

        _edit.OnTextEntered += _ => Commit();
        _edit.OnFocusExit += _ => Commit();
    }

    public LineEdit Edit => _edit;

    public float Value => _value;

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
    ///     The unit shown after the number.
    /// </summary>
    public string? Unit
    {
        get => _unitLabel.Text;
        set
        {
            _unitLabel.Text = value;
            _unitLabel.Visible = !string.IsNullOrEmpty(value);
        }
    }

    public string Format
    {
        get => _format;
        set => _format = value;
    }

    /// <summary>
    ///     Shows a number from outside. While the box has focus, the player's typing stays.
    /// </summary>
    public void SetValue(float value)
    {
        _value = value;

        if (!_edit.HasKeyboardFocus())
            _edit.Text = value.ToString(_format, CultureInfo.CurrentCulture);
    }

    /// <summary>
    ///     Commits what is in the box, as Enter or losing focus does.
    /// </summary>
    public void Commit()
    {
        if (!UserInputParser.TryFloat(_edit.Text, out var parsed) || !float.IsFinite(parsed))
        {
            _edit.Text = _value.ToString(_format, CultureInfo.CurrentCulture);
            return;
        }

        var clamped = Math.Clamp(parsed, _minValue, MathF.Max(_minValue, _maxValue));
        _edit.Text = clamped.ToString(_format, CultureInfo.CurrentCulture);

        if (MathF.Abs(clamped - _value) < 0.0001f)
            return;

        _value = clamped;
        OnCommitted?.Invoke(clamped);
    }
}
