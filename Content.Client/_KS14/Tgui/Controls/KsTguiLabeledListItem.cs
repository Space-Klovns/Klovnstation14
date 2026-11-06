using System.Numerics;
using Content.Client.Stylesheets;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._KS14.Tgui.Controls;

/// <summary>
///     One row of a <see cref="KsTguiLabeledList"/>: <see cref="Label"/>, a value (<see cref="Text"/> and
///         <see cref="TextColor"/>, or XAML children), and a <see cref="Buttons"/> slot at the right.
/// </summary>
public sealed class KsTguiLabeledListItem : Control
{
    private readonly Label _labelControl;
    private readonly Label _valueLabel;
    private readonly BoxContainer _value;
    private readonly BoxContainer _buttonsSlot;

    private Control? _buttons;
    private bool _colon;
    private string? _label;
    private float _labelColumn;
    private float _buttonsColumn;

    public KsTguiLabeledListItem()
    {
        _labelControl = new Label();
        _labelControl.AddStyleClass(StyleClass.LabelWeak);

        _valueLabel = new Label { Visible = false };

        _value = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical };
        _value.AddChild(_valueLabel);

        _buttonsSlot = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal };

        AddChild(_labelControl);
        AddChild(_value);
        AddChild(_buttonsSlot);

        XamlChildren = _value.Children;
    }

    /// <summary>
    ///     The text of the label cell.
    /// </summary>
    public string? Label
    {
        get => _label;
        set
        {
            _label = value;
            RefreshLabel();
        }
    }

    /// <summary>
    ///     tgui's <c>colon</c>: a colon after the label.
    /// </summary>
    public bool Colon
    {
        get => _colon;
        set
        {
            _colon = value;
            RefreshLabel();
        }
    }

    /// <summary>
    ///     The value as text. Children added in XAML follow it.
    /// </summary>
    public string? Text
    {
        get => _valueLabel.Text;
        set
        {
            _valueLabel.Text = value;
            _valueLabel.Visible = !string.IsNullOrEmpty(value);
        }
    }

    /// <summary>
    ///     The colour of <see cref="Text"/>; null is the default text colour.
    /// </summary>
    public Color? TextColor
    {
        get => _valueLabel.FontColorOverride;
        set => _valueLabel.FontColorOverride = value;
    }

    /// <summary>
    ///     A control at the right of the row, normally one or more <see cref="KsTguiButton"/>s.
    /// </summary>
    public Control? Buttons
    {
        get => _buttons;
        set
        {
            if (_buttons != null)
                _buttonsSlot.RemoveChild(_buttons);

            _buttons = value;
            if (value != null)
            {
                value.Orphan();
                _buttonsSlot.AddChild(value);
            }
        }
    }

    internal float LabelWidth { get; private set; }
    internal float ButtonsWidth { get; private set; }
    internal float ValueWidth => _value.DesiredSize.X;

    private void RefreshLabel()
    {
        _labelControl.Text = _colon && !string.IsNullOrEmpty(_label) ? _label + ":" : _label;
    }

    /// <summary>
    ///     Measures the label and buttons cells on their own, for the list to size its columns by.
    /// </summary>
    internal void MeasureColumns()
    {
        _labelControl.Measure(Vector2Helpers.Infinity);
        LabelWidth = _labelControl.DesiredSize.X;

        _buttonsSlot.Measure(Vector2Helpers.Infinity);
        ButtonsWidth = _buttonsSlot.DesiredSize.X;
    }

    internal void Columns(float labelColumn, float buttonsColumn)
    {
        _labelColumn = labelColumn;
        _buttonsColumn = buttonsColumn;
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        _labelControl.Measure(Vector2Helpers.Infinity);
        _value.Measure(availableSize);
        _buttonsSlot.Measure(Vector2Helpers.Infinity);

        // A buttons cell sits 1px down and has no space under it; the others have 3px either side of the text, which the
        //      row gap and the list's own offset account for.
        var height = MathF.Max(_labelControl.DesiredSize.Y, _value.DesiredSize.Y);
        height = MathF.Max(height, _buttonsSlot.DesiredSize.Y - 2f);
        var gap = KsTguiLabeledList.GapAfterLabel;
        var width = _labelColumn + gap + _value.DesiredSize.X + (_buttonsColumn > 0f ? gap + _buttonsColumn : 0f);
        return new Vector2(width, height);
    }

    protected override Vector2 ArrangeOverride(Vector2 finalSize)
    {
        var gap = KsTguiLabeledList.GapAfterLabel;
        var valueLeft = _labelColumn + gap;
        var buttonsWidth = _buttonsColumn;
        var buttonsLeft = finalSize.X - buttonsWidth;
        var valueRight = buttonsWidth > 0f ? buttonsLeft - gap : finalSize.X;

        _labelControl.Arrange(UIBox2.FromDimensions(0f, 0f, _labelColumn, _labelControl.DesiredSize.Y));
        _value.Arrange(new UIBox2(valueLeft, 0f, MathF.Max(valueLeft, valueRight), finalSize.Y));
        _buttonsSlot.Arrange(UIBox2.FromDimensions(buttonsLeft, 0f, buttonsWidth, _buttonsSlot.DesiredSize.Y));
        return finalSize;
    }
}
