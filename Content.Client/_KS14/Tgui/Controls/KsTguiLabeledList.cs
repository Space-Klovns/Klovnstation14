using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;

namespace Content.Client._KS14.Tgui.Controls;

/// <summary>
///     tgui's LabeledList: rows of a label, a value and optional buttons, with the label and button columns as wide as their
///         widest cell. Meant to sit inside a <see cref="KsTguiSection"/>, whose body padding supplies the outer edge:
///         tgui widens the list over that padding and gives its cells the same back, so here the cells have none.
/// </summary>
public sealed class KsTguiLabeledList : Control
{
    /// <summary>
    ///     The gap between columns, a cell's padding either side of it.
    /// </summary>
    private const float ColumnGap = 12f;

    /// <summary>
    ///     The gap between rows: each cell has 3px above and below it, and the list takes the first and last off.
    /// </summary>
    private const float RowGap = 6f;

    /// <summary>
    ///     <c>min-width: 5em</c> on a label cell, which is <c>border-box</c>: 60px with the cell's 12px of padding in it, so
    ///         the value starts at least 60px in.
    /// </summary>
    private const float LabelMinWidth = 60f - ColumnGap;

    /// <summary>
    ///     The list's <c>margin-top</c>, <c>-var(--space-s)</c>, which takes the first row's padding away.
    /// </summary>
    private const float TopOffset = -3f;

    /// <summary>
    ///     A row's padding: 3px above and below, and 6px either side, which the list's negative margin carries to the edge of
    ///         the section around it.
    /// </summary>
    private const float StripePaddingY = 3f;

    private const float StripePaddingX = 6f;

    private float _labelColumn;
    private float _buttonsColumn;
    private bool _candystripe;
    private Color? _stripeColor;

    public KsTguiLabeledList()
    {
        AddStyleClass(KsTguiSheetlet.StyleClassCandystripe);
    }

    /// <summary>
    ///     tgui's <c>candystripe</c> on every row: the first, third and so on of the visible rows are darkened, from the
    ///         section's edge to its edge. Hiding or adding a row re-alternates the rest.
    /// </summary>
    public bool Candystripe
    {
        get => _candystripe;
        set => _candystripe = value;
    }

    /// <summary>
    ///     Whether a row is drawn dark: striping is on and it is an odd row among the visible ones.
    /// </summary>
    public bool IsStriped(KsTguiLabeledListItem row)
    {
        if (!_candystripe)
            return false;

        // A divider is a row of the table too, so it counts in CSS's nth-child.
        var index = 0;
        foreach (var other in Rows())
        {
            if (other == row)
                return index % 2 == 0;

            index++;
        }

        return false;
    }

    protected override void StylePropertiesChanged()
    {
        base.StylePropertiesChanged();
        _stripeColor = TryGetStyleProperty<Color>(KsTguiSheetlet.StylePropertyCandystripe, out var color) ? color : null;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        if (!_candystripe || _stripeColor is not { } color)
            return;

        var last = LastItem();

        foreach (var child in Children)
        {
            if (child is not KsTguiLabeledListItem { Visible: true } item || !IsStriped(item))
                continue;

            // tgui's last row has no padding below it.
            var top = (float)item.PixelPosition.Y - StripePaddingY * UIScale;
            var bottom = (float)(item.PixelPosition.Y + item.PixelHeight) + (item == last ? 0f : StripePaddingY * UIScale);
            handle.DrawRect(new UIBox2(-StripePaddingX * UIScale, top, (float)PixelWidth + StripePaddingX * UIScale, bottom), color);
        }
    }

    /// <summary>
    ///     Adds a row, and returns it.
    /// </summary>
    public KsTguiLabeledListItem AddItem(string label, string? text = null, Color? textColor = null)
    {
        var item = new KsTguiLabeledListItem { Label = label, Text = text, TextColor = textColor };
        AddChild(item);
        return item;
    }

    /// <summary>
    ///     Adds a divider row, <c>LabeledList.Divider</c>. With a <paramref name="size"/> it has <c>(size - 1)</c> rem
    ///         (12px) of space above and below.
    /// </summary>
    public KsTguiLabeledListDivider AddDivider(int size = 0)
    {
        var divider = new KsTguiLabeledListDivider { DividerSize = size };
        AddChild(divider);
        return divider;
    }

    private IEnumerable<Control> Rows()
    {
        foreach (var child in Children)
        {
            if (child is KsTguiLabeledListItem { Visible: true } or KsTguiLabeledListDivider { Visible: true })
                yield return child;
        }
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        _labelColumn = 0f;
        _buttonsColumn = 0f;

        foreach (var child in Children)
        {
            if (child is not KsTguiLabeledListItem { Visible: true } item)
                continue;

            item.MeasureColumns();
            _labelColumn = MathF.Max(_labelColumn, item.LabelWidth);
            _buttonsColumn = MathF.Max(_buttonsColumn, item.ButtonsWidth);
        }

        _labelColumn = MathF.Max(_labelColumn, LabelMinWidth);

        var width = 0f;
        var cursor = TopOffset;
        Control? last = null;
        foreach (var row in Rows())
            last = row;

        foreach (var row in Rows())
        {
            if (row is KsTguiLabeledListDivider divider)
            {
                divider.Measure(new Vector2(availableSize.X, float.PositiveInfinity));
                cursor += divider.DesiredSize.Y;
                continue;
            }

            var item = (KsTguiLabeledListItem)row;
            item.Columns(_labelColumn, _buttonsColumn, row == last);

            // A row with no buttons lets its value run on over the buttons column.
            var valueAvailable = MathF.Max(0f, availableSize.X - _labelColumn - ColumnGap
                - (item.HasButtons ? _buttonsColumn + ColumnGap : 0f));
            item.Measure(new Vector2(valueAvailable, float.PositiveInfinity));
            width = MathF.Max(width, _labelColumn + ColumnGap + item.ValueWidth + (_buttonsColumn > 0f ? ColumnGap + _buttonsColumn : 0f));
            cursor += item.RowHeight;
        }

        return new Vector2(width, MathF.Max(0f, cursor));
    }

    protected override Vector2 ArrangeOverride(Vector2 finalSize)
    {
        var cursor = TopOffset;
        foreach (var row in Rows())
        {
            if (row is KsTguiLabeledListDivider divider)
            {
                divider.Arrange(UIBox2.FromDimensions(0f, cursor, finalSize.X, divider.DesiredSize.Y));
                cursor += divider.DesiredSize.Y;
                continue;
            }

            var item = (KsTguiLabeledListItem)row;
            item.Columns(_labelColumn, _buttonsColumn, item == LastItem());

            // The item's box leaves out the 3px of padding above and below that the rows keep between them.
            var top = cursor + KsTguiLabeledListItem.CellPadding;
            item.Arrange(UIBox2.FromDimensions(0f, top, finalSize.X, item.DesiredSize.Y));
            cursor += item.RowHeight;
        }

        return finalSize;
    }

    private Control? LastItem()
    {
        Control? last = null;
        foreach (var row in Rows())
            last = row;

        return last;
    }

    /// <summary>
    ///     The gap after a label column, and after the value when buttons follow it.
    /// </summary>
    internal static float GapAfterLabel => ColumnGap;
}

/// <summary>
///     A <c>LabeledList.Divider</c> row: tgui's Divider across the list and the section's padding either side of it.
/// </summary>
public sealed class KsTguiLabeledListDivider : Control
{
    private readonly KsTguiDivider _divider = new();

    public KsTguiLabeledListDivider()
    {
        AddChild(_divider);
    }

    /// <summary>
    ///     tgui's <c>size</c>: <c>(size - 1)</c> rem of padding above and below; none without one.
    /// </summary>
    public int DividerSize
    {
        get => _size;
        set
        {
            _size = value;
            InvalidateMeasure();
        }
    }

    private int _size;

    private float Padding => MathF.Max(0f, (float)_size - 1f) * 12f;

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        _divider.Measure(new Vector2(availableSize.X, float.PositiveInfinity));
        return new Vector2(0f, _divider.DesiredSize.Y + Padding * 2f);
    }

    protected override Vector2 ArrangeOverride(Vector2 finalSize)
    {
        // The table is 6px wider than the list either side, so the line runs to the section's edges.
        _divider.Arrange(UIBox2.FromDimensions(-6f, Padding, finalSize.X + 12f, _divider.DesiredSize.Y));
        return finalSize;
    }
}
