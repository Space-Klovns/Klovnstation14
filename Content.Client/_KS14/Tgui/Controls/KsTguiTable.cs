using System.Linq;
using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;

namespace Content.Client._KS14.Tgui.Controls;

/// <summary>
///     tgui's Table: <see cref="KsTguiTableRow"/>s whose cells line up in columns, each as wide as its widest cell. Cells are
///         padded 3px sideways, except the first column's left and the last column's right. A cell with
///         <c>HorizontalExpand</c> makes its column take the spare width (tgui marks the other columns <c>collapsing</c>);
///         with none, every column grows in proportion. A row with <see cref="KsTguiTableRow.Candystripe"/> is striped across
///         the table's width when it is an odd row.
/// </summary>
public sealed class KsTguiTable : Control
{
    private const float CellPadding = 3f;
    private const float HeaderSpace = 6f;

    private float[] _columns = [];
    private bool[] _expanding = [];
    private Color? _stripeColor;

    public KsTguiTable()
    {
        AddStyleClass(KsTguiSheetlet.StyleClassCandystripe);
    }

    /// <summary>
    ///     Whether a row is a dark one: it asks for stripes and is an odd row among the visible ones.
    /// </summary>
    public bool IsStriped(KsTguiTableRow row)
    {
        var index = 0;
        foreach (var child in Children)
        {
            if (child is not KsTguiTableRow { Visible: true } other)
                continue;

            if (other == row)
                return row.Candystripe && index % 2 == 0;

            index++;
        }

        return false;
    }

    protected override void StylePropertiesChanged()
    {
        base.StylePropertiesChanged();
        _stripeColor = TryGetStyleProperty<Color>(KsTguiSheetlet.StylePropertyCandystripe, out var color) ? color : null;
    }

    private IEnumerable<KsTguiTableRow> Rows()
    {
        foreach (var child in Children)
        {
            if (child is KsTguiTableRow { Visible: true } row)
                yield return row;
        }
    }

    private static IEnumerable<Control> Cells(KsTguiTableRow row)
    {
        foreach (var cell in row.Children)
        {
            if (cell.Visible)
                yield return cell;
        }
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        var columnCount = 0;
        foreach (var row in Rows())
            columnCount = Math.Max(columnCount, Cells(row).Count());

        _columns = new float[columnCount];
        _expanding = new bool[columnCount];

        foreach (var row in Rows())
        {
            var column = 0;
            foreach (var cell in Cells(row))
            {
                cell.Measure(Vector2Helpers.Infinity);
                _columns[column] = MathF.Max(_columns[column], cell.DesiredSize.X);
                _expanding[column] |= cell.HorizontalExpand;
                column++;
            }
        }

        var height = 0f;
        foreach (var row in Rows())
        {
            row.Measure(Vector2Helpers.Infinity);
            height += RowHeight(row);
        }

        return new Vector2(ColumnsWidth(), height);
    }

    private float ColumnsWidth()
    {
        var width = 0f;
        foreach (var column in _columns)
            width += column;

        return width + MathF.Max(0f, (float)_columns.Length - 1f) * CellPadding * 2f;
    }

    private static float RowHeight(KsTguiTableRow row)
    {
        var height = 0f;
        foreach (var cell in Cells(row))
            height = MathF.Max(height, cell.DesiredSize.Y);

        return height + (row.Header ? HeaderSpace : 0f);
    }

    protected override Vector2 ArrangeOverride(Vector2 finalSize)
    {
        var spare = MathF.Max(0f, finalSize.X - ColumnsWidth());
        var widths = (float[])_columns.Clone();

        var expandingCount = 0;
        var expandingTotal = 0f;
        var total = 0f;
        for (var i = 0; i < widths.Length; i++)
        {
            total += _columns[i];
            if (!_expanding[i])
                continue;

            expandingCount++;
            expandingTotal += _columns[i];
        }

        if (spare > 0f)
        {
            // Columns are 'collapsing' unless a cell asks to expand; with no such cell every column grows in proportion.
            for (var i = 0; i < widths.Length; i++)
            {
                if (expandingCount > 0)
                {
                    if (_expanding[i])
                        widths[i] += spare * (expandingTotal > 0f ? _columns[i] / expandingTotal : 1f / (float)expandingCount);
                }
                else
                {
                    widths[i] += spare * (total > 0f ? _columns[i] / total : 1f / (float)widths.Length);
                }
            }
        }

        var y = 0f;
        foreach (var row in Rows())
        {
            var height = RowHeight(row);
            var cellsHeight = height - (row.Header ? HeaderSpace : 0f);
            row.Arrange(UIBox2.FromDimensions(0f, y, finalSize.X, height));

            var x = 0f;
            var column = 0;
            foreach (var cell in Cells(row))
            {
                var cellHeight = MathF.Min(cell.DesiredSize.Y, cellsHeight);
                cell.Arrange(UIBox2.FromDimensions(x, (cellsHeight - cellHeight) / 2f, widths[column], cellHeight));
                x += widths[column] + CellPadding * 2f;
                column++;
            }

            y += height;
        }

        return finalSize;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        if (_stripeColor is not { } color)
            return;

        foreach (var row in Rows())
        {
            if (!IsStriped(row))
                continue;

            handle.DrawRect(new UIBox2(0f, row.PixelPosition.Y, PixelWidth, row.PixelPosition.Y + row.PixelHeight), color);
        }
    }
}
