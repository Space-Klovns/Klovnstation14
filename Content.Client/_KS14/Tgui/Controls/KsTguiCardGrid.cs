using System.Numerics;
using Robust.Client.UserInterface;

namespace Content.Client._KS14.Tgui.Controls;

/// <summary>
///     A grid of cards that fills the width it is given: as many columns as fit the narrowest card the children need, then
///         the width shared out equally among them, so the gaps stay constant and the first and last cards touch the left
///         and right edges. A <c>WrapContainer</c> of fixed-size cards leaves the remainder on the right, which tgui's
///         inline cards do too, but looks like a hole in a window the player can resize. The height follows the rows.
/// </summary>
/// <remarks>
///     Children need a minimum width that does not depend on the width they are given (a <see cref="KsTguiImageButton"/>
///         card is its picture's size plus 8), and may be wider than it. Set <see cref="HorizontalExpand"/> so the grid is
///         given the whole row.
/// </remarks>
public sealed class KsTguiCardGrid : Control
{
    private float _gap = 6f;
    private float _verticalGap = 6f;
    private float _minCardWidth;

    /// <summary>
    ///     Space between columns, which stays constant at any width. tgui's cards carry a 0.25em margin, so 6 apart.
    /// </summary>
    public float Gap
    {
        get => _gap;
        set
        {
            _gap = value;
            InvalidateMeasure();
        }
    }

    /// <summary>
    ///     Space between rows; follows <see cref="Gap"/> when not set.
    /// </summary>
    public float VerticalGap
    {
        get => _verticalGap;
        set
        {
            _verticalGap = value;
            InvalidateMeasure();
        }
    }

    /// <summary>
    ///     The narrowest a card may be. Zero takes the widest minimum among the children.
    /// </summary>
    public float MinCardWidth
    {
        get => _minCardWidth;
        set
        {
            _minCardWidth = value;
            InvalidateMeasure();
        }
    }

    /// <summary>
    ///     How many columns of at least <paramref name="minCardWidth"/> fit in <paramref name="width"/>: at least one.
    /// </summary>
    public static int ColumnsFor(float width, float minCardWidth, float gap)
    {
        if (float.IsInfinity(width) || float.IsNaN(width))
            return int.MaxValue;

        return Math.Max(1, (int)MathF.Floor((width + gap) / (MathF.Max(minCardWidth, 1f) + gap)));
    }

    /// <summary>
    ///     What each card gets when <paramref name="columns"/> share <paramref name="width"/>.
    /// </summary>
    public static float CardWidthFor(float width, int columns, float gap)
    {
        return MathF.Max(0f, (width - (columns - 1) * gap) / columns);
    }

    private float ResolveMinimumWidth()
    {
        var minimum = _minCardWidth;
        foreach (var child in Children)
        {
            if (!child.Visible)
                continue;

            // At any size but unlimited a child's desired width is cut down to it, which would read as no minimum.
            child.Measure(new Vector2(float.PositiveInfinity, float.PositiveInfinity));
            minimum = MathF.Max(minimum, child.DesiredSize.X);
        }

        return minimum;
    }

    private int VisibleCount()
    {
        var count = 0;
        foreach (var child in Children)
        {
            if (child.Visible)
                count++;
        }

        return count;
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        var count = VisibleCount();
        if (count == 0)
            return Vector2.Zero;

        var minimum = ResolveMinimumWidth();
        var columns = Math.Min(ColumnsFor(availableSize.X, minimum, _gap), count);
        var cardWidth = float.IsInfinity(availableSize.X)
            ? minimum
            : CardWidthFor(availableSize.X, columns, _gap);

        var height = LayOut(columns, cardWidth, arrange: false, finalWidth: 0f);
        var width = float.IsInfinity(availableSize.X)
            ? columns * cardWidth + (columns - 1) * _gap
            : availableSize.X;
        return new Vector2(width, height);
    }

    protected override Vector2 ArrangeOverride(Vector2 finalSize)
    {
        var count = VisibleCount();
        if (count == 0)
            return finalSize;

        var minimum = ResolveMinimumWidth();

        // Fewer cards than columns still use the column width, so a short last row lines up with the rows above it.
        var widthColumns = ColumnsFor(finalSize.X, minimum, _gap);
        var cardWidth = CardWidthFor(finalSize.X, widthColumns, _gap);
        LayOut(widthColumns, cardWidth, arrange: true, finalWidth: finalSize.X);
        return finalSize;
    }

    /// <summary>
    ///     Measures every child at the card width and, when asked, arranges it. Returns the height of all the rows.
    /// </summary>
    private float LayOut(int columns, float cardWidth, bool arrange, float finalWidth)
    {
        if (columns == int.MaxValue)
            columns = Math.Max(1, VisibleCount());

        var row = new List<Control>(columns);
        var y = 0f;
        var firstRow = true;

        void FlushRow()
        {
            if (row.Count == 0)
                return;

            var rowHeight = 0f;
            foreach (var child in row)
                rowHeight = MathF.Max(rowHeight, child.DesiredSize.Y);

            if (!firstRow)
                y += _verticalGap;

            if (arrange)
            {
                for (var column = 0; column < row.Count; column++)
                {
                    var left = column * (cardWidth + _gap);

                    // The last column ends on the edge exactly, not wherever the float sums land.
                    var right = column == columns - 1 ? finalWidth : left + cardWidth;
                    row[column].Arrange(new UIBox2(left, y, right, y + rowHeight));
                }
            }

            y += rowHeight;
            firstRow = false;
            row.Clear();
        }

        foreach (var child in Children)
        {
            if (!child.Visible)
                continue;

            child.Measure(new Vector2(cardWidth, float.PositiveInfinity));
            row.Add(child);
            if (row.Count == columns)
                FlushRow();
        }

        FlushRow();
        return y;
    }
}
