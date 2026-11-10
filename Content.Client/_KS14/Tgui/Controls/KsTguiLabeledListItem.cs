using System.Numerics;
using Content.Client.Stylesheets;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Utility;

namespace Content.Client._KS14.Tgui.Controls;

/// <summary>
///     tgui's <c>verticalAlign</c> on a table cell. <c>td</c> is <see cref="Baseline"/> unless told otherwise (reset.scss).
/// </summary>
public enum KsTguiVerticalAlign : byte
{
    Baseline,
    Top,
    Middle,
    Bottom,
}

/// <summary>
///     One row of a <see cref="KsTguiLabeledList"/>: <see cref="Label"/> (with tgui's colon), a value (<see cref="Text"/> and
///         <see cref="TextColor"/>, or XAML children), and a <see cref="Buttons"/> slot at the right. The label and the
///         value sit on a common text baseline, as table cells do, unless <see cref="VerticalAlign"/> says otherwise.
/// </summary>
public sealed class KsTguiLabeledListItem : Control
{
    /// <summary>
    ///     A label or value cell's padding above and below: <c>var(--space-s)</c>.
    /// </summary>
    internal const float CellPadding = 3f;

    /// <summary>
    ///     The buttons cell's <c>padding-top</c>, <c>var(--space-xxs)</c>; it has no padding below.
    /// </summary>
    private const float ButtonsPaddingTop = 1f;

    private readonly Label _labelControl;
    private readonly RichTextLabel _wrapLabel;
    private readonly Label _valueLabel;
    private readonly BoxContainer _value;
    private readonly BoxContainer _buttonsSlot;

    private Control? _buttons;
    private bool _colon = true;
    private bool _labelWrap;
    private string? _label;
    private string? _tooltip;
    private Color? _labelColor;
    private KsTguiVerticalAlign _verticalAlign = KsTguiVerticalAlign.Baseline;
    private float _labelColumn;
    private float _buttonsColumn;
    private bool _last;

    // Worked out in MeasureOverride, relative to the top of the row's cells (their padding included).
    private float _labelTop;
    private float _valueTop;
    private float _buttonsTop;
    private float _rowHeight;

    public KsTguiLabeledListItem()
    {
        _labelControl = new Label();
        _labelControl.AddStyleClass(StyleClass.LabelWeak);

        _wrapLabel = new RichTextLabel { Visible = false };

        _valueLabel = new Label { Visible = false };

        _value = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical };
        _value.AddChild(_valueLabel);

        _buttonsSlot = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal };

        AddChild(_labelControl);
        AddChild(_value);
        AddChild(_buttonsSlot);
        AddChild(_wrapLabel);

        XamlChildren = _value.Children;
    }

    /// <summary>
    ///     <c>AddChild</c> from code goes to the value cell, as XAML children do.
    /// </summary>
    protected override void ChildAdded(Control newChild)
    {
        base.ChildAdded(newChild);

        if (newChild == _labelControl || newChild == _value || newChild == _buttonsSlot || newChild == _wrapLabel)
            return;

        newChild.Orphan();
        _value.AddChild(newChild);
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
    ///     tgui puts a colon after a label that is a string, and so does this unless it is turned off or the label already ends
    ///         in one. Off is for a label that tgui would pass as a component rather than a string.
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
    ///     tgui's <c>labelWrap</c>: the label may wrap over several lines, where otherwise it stays on one. It then takes no
    ///         part in the label column's width beyond its minimum.
    /// </summary>
    public bool LabelWrap
    {
        get => _labelWrap;
        set
        {
            _labelWrap = value;
            _labelControl.Visible = !value;
            _wrapLabel.Visible = value;
            RefreshLabel();
        }
    }

    /// <summary>
    ///     tgui's <c>tooltip</c> on a label: the label is underlined with a dotted line and the tooltip appears over it.
    /// </summary>
    public string? Tooltip
    {
        get => _tooltip;
        set
        {
            _tooltip = value;
            var present = !string.IsNullOrEmpty(value);
            _labelControl.ToolTip = value;
            _wrapLabel.ToolTip = value;
            _labelControl.MouseFilter = present ? MouseFilterMode.Pass : MouseFilterMode.Ignore;
            _wrapLabel.MouseFilter = present ? MouseFilterMode.Pass : MouseFilterMode.Ignore;
        }
    }

    /// <summary>
    ///     tgui's <c>textAlign</c> of the value cell, which reaches <see cref="Text"/>.
    /// </summary>
    public Label.AlignMode TextAlign
    {
        get => _valueLabel.Align;
        set => _valueLabel.Align = value;
    }

    /// <summary>
    ///     tgui's <c>verticalAlign</c>, shared by the label and the value.
    /// </summary>
    public KsTguiVerticalAlign VerticalAlign
    {
        get => _verticalAlign;
        set
        {
            _verticalAlign = value;
            InvalidateMeasure();
            Parent?.InvalidateMeasure();
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
    ///     The colour of the label cell; null is the label style's own colour. It wins over the style, which the wires window
    ///         needs to draw a wire's name in the wire's colour.
    /// </summary>
    public Color? LabelColor
    {
        get => _labelControl.FontColorOverride;
        set
        {
            _labelColor = value;
            _labelControl.FontColorOverride = value;
            RefreshLabel(force: true);
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

            // The buttons column is sized from every row, as the label column is.
            Parent?.InvalidateMeasure();
        }
    }

    /// <summary>
    ///     The text the label cell shows, colon included.
    /// </summary>
    public string? ShownLabel => _labelWrap ? _wrapLabelText : _labelControl.Text;

    internal float LabelWidth { get; private set; }
    internal float ButtonsWidth { get; private set; }
    internal float ValueWidth => _value.DesiredSize.X;
    internal bool HasButtons => _buttons != null;

    /// <summary>
    ///     This row's height as a table row, with its cells' padding: what the list steps by.
    /// </summary>
    internal float RowHeight => _rowHeight;

    private string? _wrapLabelText;

    private string? LabelWithColon()
    {
        if (!_colon || string.IsNullOrEmpty(_label) || _label.EndsWith(':'))
            return _label;

        return _label + ":";
    }

    private void RefreshLabel(bool force = false)
    {
        var text = LabelWithColon();

        if (_labelWrap)
        {
            if (!force && _wrapLabelText == text)
                return;

            _wrapLabelText = text;
            _wrapLabel.SetMessage(FormattedMessage.FromUnformatted(text ?? string.Empty), defaultColor: WrapColor());
        }
        else
        {
            if (!force && _labelControl.Text == text)
                return;

            _labelControl.Text = text;
        }

        // The list sizes the label column from every row. A row re-measured alone gets the column as it was, so its own size
        //      doesn't change and the list never hears: the new label overruns the column and the rows' values fall out of line.
        Parent?.InvalidateMeasure();
    }

    private Color WrapColor()
    {
        if (_labelColor is { } color)
            return color;

        return TryGetStyleProperty<Color>(KsTguiSheetlet.StylePropertyLabelColor, out var label) ? label : KsTguiPalettes.Label;
    }

    protected override void StylePropertiesChanged()
    {
        base.StylePropertiesChanged();

        if (_labelWrap)
            RefreshLabel(force: true);
    }

    /// <summary>
    ///     Measures the label and buttons cells on their own, for the list to size its columns by.
    /// </summary>
    internal void MeasureColumns()
    {
        if (_labelWrap)
        {
            LabelWidth = 0f;
        }
        else
        {
            _labelControl.Measure(Vector2Helpers.Infinity);
            LabelWidth = string.IsNullOrEmpty(_labelControl.Text) ? 0f : _labelControl.DesiredSize.X;
        }

        _buttonsSlot.Measure(Vector2Helpers.Infinity);
        ButtonsWidth = _buttonsSlot.DesiredSize.X;
    }

    internal void Columns(float labelColumn, float buttonsColumn, bool last)
    {
        _labelColumn = labelColumn;
        _buttonsColumn = buttonsColumn;
        _last = last;
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        var scale = UIScale;
        var labelContent = _labelWrap ? (Control)_wrapLabel : _labelControl;
        var labelEmpty = string.IsNullOrEmpty(_labelWrap ? _wrapLabelText : _labelControl.Text);

        if (_labelWrap)
            _wrapLabel.Measure(new Vector2(_labelColumn, float.PositiveInfinity));
        else
            _labelControl.Measure(Vector2Helpers.Infinity);

        _value.Measure(availableSize);
        _buttonsSlot.Measure(Vector2Helpers.Infinity);

        var referenceFont = KsTguiBaseline.FontOf(_labelControl);
        var referenceAscent = KsTguiBaseline.Ascent(referenceFont, scale);

        var valueEmpty = !_valueLabel.Visible && !HasVisibleValueChild();
        var buttonsEmpty = _buttons == null;

        var padBottom = _last ? 0f : CellPadding;

        var labelHeight = labelEmpty ? 0f : labelContent.DesiredSize.Y;
        var valueHeight = valueEmpty ? 0f : _value.DesiredSize.Y;
        var buttonsHeight = buttonsEmpty ? 0f : _buttonsSlot.DesiredSize.Y;

        var labelAscent = labelEmpty ? 0f : _labelWrap ? referenceAscent : KsTguiBaseline.Of(_labelControl, labelHeight, referenceFont, scale);
        var valueAscent = valueEmpty ? 0f : KsTguiBaseline.Of(_value, valueHeight, referenceFont, scale);
        var buttonsAscent = buttonsEmpty ? 0f : KsTguiBaseline.Of(_buttonsSlot, buttonsHeight, referenceFont, scale);

        // A row's baseline is the lowest one any baseline-aligned cell asks for; each cell then hangs from it.
        var baseline = 0f;
        if (_verticalAlign == KsTguiVerticalAlign.Baseline)
        {
            baseline = MathF.Max(CellPadding + labelAscent, CellPadding + valueAscent);
        }

        baseline = MathF.Max(baseline, ButtonsPaddingTop + buttonsAscent);
        baseline = MathF.Round(baseline);

        float Bottom(KsTguiVerticalAlign align, float padTop, float padBottomCell, float height, float ascent)
        {
            return align == KsTguiVerticalAlign.Baseline
                ? baseline - ascent + height + padBottomCell
                : padTop + height + padBottomCell;
        }

        var height = MathF.Max(
            Bottom(_verticalAlign, CellPadding, padBottom, labelHeight, labelAscent),
            Bottom(_verticalAlign, CellPadding, padBottom, valueHeight, valueAscent));
        height = MathF.Max(height, Bottom(KsTguiVerticalAlign.Baseline, ButtonsPaddingTop, 0f, buttonsHeight, buttonsAscent));
        _rowHeight = height;

        float Top(KsTguiVerticalAlign align, float padTop, float padBottomCell, float cellHeight, float ascent)
        {
            return align switch
            {
                KsTguiVerticalAlign.Baseline => MathF.Round(baseline - ascent),
                KsTguiVerticalAlign.Top => padTop,
                KsTguiVerticalAlign.Middle => MathF.Round(padTop + (height - padTop - padBottomCell - cellHeight) / 2f),
                _ => height - padBottomCell - cellHeight,
            };
        }

        _labelTop = Top(_verticalAlign, CellPadding, padBottom, labelHeight, labelAscent);
        _valueTop = Top(_verticalAlign, CellPadding, padBottom, valueHeight, valueAscent);
        _buttonsTop = Top(KsTguiVerticalAlign.Baseline, ButtonsPaddingTop, 0f, buttonsHeight, buttonsAscent);

        var gap = KsTguiLabeledList.GapAfterLabel;
        var width = _labelColumn + gap + _value.DesiredSize.X + (_buttonsColumn > 0f && HasButtons ? gap + _buttonsColumn : 0f);

        // The box is the row without the padding the neighbouring rows' boxes leave between them.
        return new Vector2(width, MathF.Max(0f, height - CellPadding - padBottom));
    }

    private bool HasVisibleValueChild()
    {
        foreach (var child in _value.Children)
        {
            if (child != _valueLabel && child.Visible)
                return true;
        }

        return false;
    }

    protected override Vector2 ArrangeOverride(Vector2 finalSize)
    {
        var gap = KsTguiLabeledList.GapAfterLabel;
        var valueLeft = _labelColumn + gap;
        var buttonsWidth = _buttonsSlot.DesiredSize.X;
        var buttonsLeft = finalSize.X - buttonsWidth;

        // A row without buttons lets its value run over the buttons column, as tgui's colSpan does.
        var valueRight = HasButtons ? finalSize.X - _buttonsColumn - gap : finalSize.X;

        // The box starts below the row's own top padding.
        var rowTop = -CellPadding;

        var labelContent = _labelWrap ? (Control)_wrapLabel : _labelControl;
        labelContent.Arrange(UIBox2.FromDimensions(0f, rowTop + _labelTop, _labelColumn, labelContent.DesiredSize.Y));
        _value.Arrange(new UIBox2(valueLeft, rowTop + _valueTop, MathF.Max(valueLeft, valueRight), rowTop + _valueTop + _value.DesiredSize.Y));

        // Right aligned in the column, as the cell's text-align is.
        _buttonsSlot.Arrange(UIBox2.FromDimensions(buttonsLeft, rowTop + _buttonsTop, buttonsWidth, _buttonsSlot.DesiredSize.Y));
        return finalSize;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        if (string.IsNullOrEmpty(_tooltip) || string.IsNullOrEmpty(ShownLabel))
            return;

        // tgui's span with border-bottom: 2px dotted rgba(255, 255, 255, 0.8), under the text's content area.
        var scale = UIScale;
        var label = _labelWrap ? (Control)_wrapLabel : _labelControl;
        var font = KsTguiBaseline.FontOf(_labelControl);
        var thickness = MathF.Max(1f, MathF.Round(2f * scale));
        var width = _labelWrap ? label.PixelWidth : (float)Math.Round(_labelControl.DesiredSize.X * scale);
        var left = (float)label.PixelPosition.X;
        var top = (float)label.PixelPosition.Y + font.GetAscent(scale) + font.GetDescent(scale);
        var color = KsTguiPalettes.Translucent(Color.White, 0.8f, KsTguiPalettes.Section);

        for (var x = 0f; x < width; x += thickness * 2f)
        {
            var dot = MathF.Min(thickness, width - x);
            handle.DrawRect(new UIBox2(left + x, top, left + x + dot, top + thickness), color);
        }
    }
}
