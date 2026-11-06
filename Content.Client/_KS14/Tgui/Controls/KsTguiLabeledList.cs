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
    ///     <c>min-width: 5em</c> on a label cell.
    /// </summary>
    private const float LabelMinWidth = 60f;

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

        var index = 0;
        foreach (var child in Children)
        {
            if (child is not KsTguiLabeledListItem { Visible: true } other)
                continue;

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

        KsTguiLabeledListItem? last = null;
        foreach (var child in Children)
        {
            if (child is KsTguiLabeledListItem { Visible: true } visible)
                last = visible;
        }

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

        var valueAvailable = MathF.Max(0f, availableSize.X - _labelColumn - _buttonsColumn - Gaps(_buttonsColumn));

        var width = 0f;
        var height = 0f;
        var first = true;
        foreach (var child in Children)
        {
            if (child is not KsTguiLabeledListItem { Visible: true } item)
                continue;

            item.Columns(_labelColumn, _buttonsColumn);
            item.Measure(new Vector2(valueAvailable, float.PositiveInfinity));
            width = MathF.Max(width, _labelColumn + _buttonsColumn + Gaps(_buttonsColumn) + item.ValueWidth);
            height += item.DesiredSize.Y + (first ? 0f : RowGap);
            first = false;
        }

        return new Vector2(width, height);
    }

    protected override Vector2 ArrangeOverride(Vector2 finalSize)
    {
        var y = 0f;
        foreach (var child in Children)
        {
            if (child is not KsTguiLabeledListItem { Visible: true } item)
                continue;

            item.Columns(_labelColumn, _buttonsColumn);
            item.Arrange(UIBox2.FromDimensions(0f, y, finalSize.X, item.DesiredSize.Y));
            y += item.DesiredSize.Y + RowGap;
        }

        return finalSize;
    }

    private static float Gaps(float buttonsColumn)
    {
        return buttonsColumn > 0f ? ColumnGap * 2f : ColumnGap;
    }

    /// <summary>
    ///     The gap after a label column, and after the value when buttons follow it.
    /// </summary>
    internal static float GapAfterLabel => ColumnGap;
}
