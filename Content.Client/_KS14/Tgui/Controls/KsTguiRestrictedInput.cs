using System.Globalization;
using System.Text.RegularExpressions;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Input;

namespace Content.Client._KS14.Tgui.Controls;

/// <summary>
///     tgui-core's RestrictedInput (<c>RestrictedInput.js</c>, <c>RestrictedInput.scss</c>): a number-only Input with a green
///         border (lighter focused, red once <see cref="IsInvalid"/>) and right-aligned text. Only digits (and a point, with
///         <see cref="AllowFloats"/>) can be typed, <c>-</c> negates the number instead of being typed, Enter and Escape
///         release the focus after raising <see cref="OnEnter"/> and <see cref="OnEscape"/>, and <see cref="MinValue"/> and
///         <see cref="MaxValue"/> only decide whether the border is red: nothing is clamped.
/// </summary>
public sealed class KsTguiRestrictedInput : KsTguiRightAlignedLineEdit
{
    private static readonly Regex IntegerText = new(@"^-?\d*$", RegexOptions.Compiled);
    private static readonly Regex FloatText = new(@"^-?\d*\.?\d*$", RegexOptions.Compiled);

    /// <summary>
    ///     The Input's side padding with its border, which the right-aligned text is kept clear of.
    /// </summary>
    private const float SidePadding = 5f;

    /// <summary>
    ///     <c>--input-background</c>, as the tgui sheet's inputs.
    /// </summary>
    private static readonly Color Background = Color.FromHex("#0D0D0D");

    /// <summary>
    ///     <c>--restricted-input-border-color</c>: <c>--color-good</c>, hsl(95, 62.5%, 40%).
    /// </summary>
    public static readonly Color BorderIdle = KsTguiPalettes.Positive.Base;

    /// <summary>
    ///     <c>--restricted-input-border-color-focus</c>: <c>--color-good</c> ten points lighter.
    /// </summary>
    public static readonly Color BorderFocused = KsTguiColors.FromHsl(95f, 62.5f, 50f);

    /// <summary>
    ///     <c>--restricted-input-border-color-invalid</c>: <c>--color-bad</c>, focused or not.
    /// </summary>
    public static readonly Color BorderInvalid = KsTguiPalettes.Negative.Base;

    private readonly KsTguiStyleBox _box;

    private float _minValue;
    private float _maxValue = 10000f;
    private bool _allowFloats;
    private float _value;
    private bool _valid = true;

    /// <summary>
    ///     tgui's <c>onChange</c>: raised with the number on every edit, a typed one or a negation.
    /// </summary>
    public event Action<float>? OnValueChanged;

    /// <summary>
    ///     tgui's <c>onEnter</c>.
    /// </summary>
    public event Action<float>? OnEnter;

    /// <summary>
    ///     tgui's <c>onEscape</c>.
    /// </summary>
    public event Action<float>? OnEscape;

    /// <summary>
    ///     tgui's <c>onBlur</c>.
    /// </summary>
    public event Action<float>? OnBlurred;

    /// <summary>
    ///     tgui's <c>onValidationChange</c>.
    /// </summary>
    public event Action<bool>? OnValidationChanged;

    public KsTguiRestrictedInput() : this(new KsTguiStyleBox(Background, 2f)
    {
        BorderColor = BorderIdle,
        BorderThickness = new Thickness(1f),
    })
    {
    }

    private KsTguiRestrictedInput(KsTguiStyleBox box) : base(box, SidePadding)
    {
        _box = box;
        IsValid = IsTypeable;
        _box.SetContentMarginOverride(StyleBox.Margin.Vertical, 2f);

        OnTextChanged += _ => TextEdited();
        OnTextEntered += _ =>
        {
            OnEnter?.Invoke(_value);
            ReleaseKeyboardFocus();
        };
        OnFocusEnter += _ => RefreshBorder();
        OnFocusExit += _ =>
        {
            RefreshBorder();
            OnBlurred?.Invoke(_value);
        };
        OnKeyBindDown += args =>
        {
            if (args.Function != EngineKeyFunctions.TextReleaseFocus)
                return;

            OnEscape?.Invoke(_value);
            ReleaseKeyboardFocus();
            args.Handle();
        };

        Text = FormatNumber(_value);
        RefreshBorder();
    }

    /// <summary>
    ///     tgui's <c>minValue</c>, 0 by default.
    /// </summary>
    public float MinValue
    {
        get => _minValue;
        set
        {
            _minValue = value;
            RefreshValidity();
        }
    }

    /// <summary>
    ///     tgui's <c>maxValue</c>, 10000 by default.
    /// </summary>
    public float MaxValue
    {
        get => _maxValue;
        set
        {
            _maxValue = value;
            RefreshValidity();
        }
    }

    /// <summary>
    ///     tgui's <c>allowFloats</c>: any number, where otherwise only whole steps from <see cref="MinValue"/> are valid.
    /// </summary>
    public bool AllowFloats
    {
        get => _allowFloats;
        set
        {
            _allowFloats = value;
            RefreshValidity();
        }
    }

    /// <summary>
    ///     The number in the box: 0 while it is empty.
    /// </summary>
    public float Value => _value;

    /// <summary>
    ///     Whether the box is red: the number is out of range, or off the step.
    /// </summary>
    public bool IsInvalid => !_valid;

    /// <summary>
    ///     The border colour, for the state it is in.
    /// </summary>
    public Color CurrentBorderColor => _box.BorderColor;

    /// <summary>
    ///     The border for a state.
    /// </summary>
    public static Color BorderFor(bool focused, bool invalid)
    {
        return invalid ? BorderInvalid : focused ? BorderFocused : BorderIdle;
    }

    /// <summary>
    ///     Whether <paramref name="number"/> is a valid entry: in range, and a whole step from the minimum unless floats are
    ///         allowed, which is the browser's validity.
    /// </summary>
    public static bool IsValidNumber(float number, float minValue, float maxValue, bool allowFloats)
    {
        if (!float.IsFinite(number) || number < minValue || number > maxValue)
            return false;

        if (allowFloats)
            return true;

        var fromBase = number - minValue;
        return MathF.Abs(fromBase - MathF.Round(fromBase)) < 0.000001f;
    }

    /// <summary>
    ///     What the box writes for a number, with no trailing zeroes.
    /// </summary>
    public static string FormatNumber(float number)
    {
        // -0 would read "-0".
        return (number == 0f ? 0f : number).ToString("0.######", CultureInfo.InvariantCulture);
    }

    /// <summary>
    ///     Shows a number from outside, as tgui's <c>value</c> prop does. While the box has the focus, the player's stays.
    /// </summary>
    public void SetValue(float value)
    {
        if (HasKeyboardFocus() || value == _value)
            return;

        _value = value;
        SetText(FormatNumber(value));
        RefreshValidity();
        UpdateAlignment();
    }

    /// <summary>
    ///     The <c>-</c> key: the number's sign flips and <see cref="OnValueChanged"/> follows, and no dash is typed.
    /// </summary>
    public void Negate()
    {
        _value = -_value;
        SetText(FormatNumber(_value));
        RefreshValidity();
        UpdateAlignment();
        OnValueChanged?.Invoke(_value);
    }

    private bool IsTypeable(string text)
    {
        return (_allowFloats ? FloatText : IntegerText).IsMatch(text);
    }

    private void TextEdited()
    {
        _value = float.TryParse(Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0f;
        RefreshValidity();
        UpdateAlignment();
        OnValueChanged?.Invoke(_value);
    }

    private void RefreshValidity()
    {
        // An empty box is valid whatever the range says, as an empty number input is.
        var valid = string.IsNullOrEmpty(Text) || IsValidNumber(_value, _minValue, _maxValue, _allowFloats);
        RefreshBorder(valid);
    }

    private void RefreshBorder(bool? valid = null)
    {
        if (valid is { } newValidity && newValidity != _valid)
        {
            _valid = newValidity;
            OnValidationChanged?.Invoke(newValidity);
        }

        _box.BorderColor = BorderFor(HasKeyboardFocus(), !_valid);
    }

    protected override void TextEntered(GUITextEnteredEventArgs args)
    {
        if (args.Text == "-")
        {
            Negate();
            return;
        }

        base.TextEntered(args);
    }
}
