using System.Linq;
using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._KS14.Tgui.Controls;

/// <summary>
///     tgui's Table: <see cref="KsTguiTableRow"/>s whose cells line up in columns, each as wide as its widest cell. Cells are
///         padded 3px sideways, except the first column's left and the last column's right, and hang from a common text
///         baseline. A cell with <c>HorizontalExpand</c> makes its column take the spare width (tgui marks the other columns
///         <c>collapsing</c>, which <see cref="KsTguiTableCell.Collapsing"/> also does); with none, every column grows in
///         proportion. A row with <see cref="KsTguiTableRow.Candystripe"/> is striped across the table's width when it is an
///         odd row. <see cref="Collapsing"/> keeps the table to its columns' width.
/// </summary>
public sealed class KsTguiTable : Control
{
    private const float CellPadding = 3f;
    private const float HeaderSpace = 6f;

    private float[] _columns = [];
    private bool[] _expanding = [];
    private bool[] _collapsingColumn = [];
    private bool _collapsing;
    private Font? _referenceFont;
    private Color? _stripeColor;
    private Color? _evenStripeColor;

    public KsTguiTable()
    {
        AddStyleClass(KsTguiSheetlet.StyleClassCandystripe);
    }

    /// <summary>
    ///     tgui's <c>collapsing</c> on the table: it is as wide as its columns and no wider.
    /// </summary>
    public bool Collapsing
    {
        get => _collapsing;
        set
        {
            _collapsing = value;
            if (value)
                HorizontalAlignment = HAlignment.Left;

            InvalidateMeasure();
        }
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
        _evenStripeColor = TryGetStyleProperty<Color>(KsTguiSheetlet.StylePropertyCandystripeEven, out var evenColor) && evenColor.A > 0f ? evenColor : null;
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

    private static int SpanOf(Control cell)
    {
        return cell is KsTguiTableCell tableCell ? tableCell.ColSpan : 1;
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        var columnCount = 0;
        foreach (var row in Rows())
        {
            var count = 0;
            foreach (var cell in Cells(row))
                count += SpanOf(cell);

            columnCount = Math.Max(columnCount, count);
        }

        _columns = new float[columnCount];
        _expanding = new bool[columnCount];
        _collapsingColumn = new bool[columnCount];

        // Cells over one column first, then those spanning several take what the columns they cover lack.
        foreach (var row in Rows())
        {
            var column = 0;
            foreach (var cell in Cells(row))
            {
                cell.Measure(Vector2Helpers.Infinity);
                var span = SpanOf(cell);
                if (span == 1)
                {
                    _columns[column] = MathF.Max(_columns[column], cell.DesiredSize.X);
                    _expanding[column] |= cell.HorizontalExpand;
                    _collapsingColumn[column] |= cell is KsTguiTableCell { Collapsing: true };
                }

                column += span;
            }
        }

        foreach (var row in Rows())
        {
            var column = 0;
            foreach (var cell in Cells(row))
            {
                var span = SpanOf(cell);
                if (span > 1)
                {
                    var covered = 0f;
                    for (var i = column; i < Math.Min(column + span, columnCount); i++)
                        covered += _columns[i];

                    covered += (float)(span - 1) * CellPadding * 2f;
                    var lacking = cell.DesiredSize.X - covered;
                    if (lacking > 0f)
                        _columns[Math.Min(column + span - 1, columnCount - 1)] += lacking;
                }

                column += span;
            }
        }

        _referenceFont = null;
        var height = 0f;
        foreach (var row in Rows())
        {
            row.Measure(Vector2Helpers.Infinity);
            height += LayoutRow(row, null);
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

    private Font ReferenceFont(KsTguiTableRow row)
    {
        if (_referenceFont != null)
            return _referenceFont;

        foreach (var cell in Cells(row))
        {
            if (cell is Label label)
                return _referenceFont = KsTguiBaseline.FontOf(label);
        }

        return UserInterfaceManager.ThemeDefaults.LabelFont;
    }

    /// <summary>
    ///     A row's height and, if asked, where each cell sits in it. Cells hang from a common text baseline unless they ask
    ///         otherwise, as <c>td</c>s do, and a header row or cell has 6px under it.
    /// </summary>
    private float LayoutRow(KsTguiTableRow row, float[]? tops)
    {
        var scale = UIScale;
        var font = ReferenceFont(row);

        var baseline = 0f;
        foreach (var cell in Cells(row))
        {
            if (AlignOf(cell) != KsTguiVerticalAlign.Baseline)
                continue;

            baseline = MathF.Max(baseline, KsTguiBaseline.Of(cell, cell.DesiredSize.Y, font, scale));
        }

        baseline = MathF.Round(baseline);

        var height = 0f;
        foreach (var cell in Cells(row))
        {
            var cellHeight = cell.DesiredSize.Y + PaddingBelow(row, cell);
            if (AlignOf(cell) == KsTguiVerticalAlign.Baseline)
                cellHeight += baseline - KsTguiBaseline.Of(cell, cell.DesiredSize.Y, font, scale);

            height = MathF.Max(height, cellHeight);
        }

        if (tops == null)
            return height;

        var index = 0;
        foreach (var cell in Cells(row))
        {
            var cellHeight = cell.DesiredSize.Y;
            tops[index++] = AlignOf(cell) switch
            {
                KsTguiVerticalAlign.Baseline => MathF.Round(baseline - KsTguiBaseline.Of(cell, cellHeight, font, scale)),
                KsTguiVerticalAlign.Top => 0f,
                KsTguiVerticalAlign.Middle => MathF.Round((height - PaddingBelow(row, cell) - cellHeight) / 2f),
                _ => height - PaddingBelow(row, cell) - cellHeight,
            };
        }

        return height;
    }

    private static KsTguiVerticalAlign AlignOf(Control cell)
    {
        return cell is KsTguiTableCell tableCell ? tableCell.VerticalAlign : KsTguiVerticalAlign.Baseline;
    }

    private static float PaddingBelow(KsTguiTableRow row, Control cell)
    {
        return row.Header || cell is KsTguiTableCell { Header: true } ? HeaderSpace : 0f;
    }

    /// <summary>
    ///     Which columns share the spare width: those with a cell that asks to expand; failing that, those without a cell that
    ///         says it is collapsing, if any does; failing both, every column, in proportion to its width.
    /// </summary>
    private bool[] SpareTakers()
    {
        var takes = (bool[])_expanding.Clone();
        if (takes.Any(taker => taker) || !_collapsingColumn.Any(collapsing => collapsing))
            return takes;

        for (var i = 0; i < takes.Length; i++)
            takes[i] = !_collapsingColumn[i];

        return takes;
    }

    protected override Vector2 ArrangeOverride(Vector2 finalSize)
    {
        var spare = _collapsing ? 0f : MathF.Max(0f, finalSize.X - ColumnsWidth());
        var widths = (float[])_columns.Clone();
        var takes = SpareTakers();

        var takerCount = 0;
        var takerTotal = 0f;
        var total = 0f;
        for (var i = 0; i < widths.Length; i++)
        {
            total += _columns[i];
            if (!takes[i])
                continue;

            takerCount++;
            takerTotal += _columns[i];
        }

        if (spare > 0f)
        {
            for (var i = 0; i < widths.Length; i++)
            {
                if (takerCount > 0)
                {
                    if (takes[i])
                        widths[i] += spare * (takerTotal > 0f ? _columns[i] / takerTotal : 1f / (float)takerCount);
                }
                else
                {
                    widths[i] += spare * (total > 0f ? _columns[i] / total : 1f / (float)widths.Length);
                }
            }
        }

        var y = 0f;
        var tops = new float[Math.Max(1, _columns.Length)];
        foreach (var row in Rows())
        {
            var cellCount = Cells(row).Count();
            if (tops.Length < cellCount)
                tops = new float[cellCount];

            var height = LayoutRow(row, tops);
            row.Arrange(UIBox2.FromDimensions(0f, y, _collapsing ? ColumnsWidth() : finalSize.X, height));

            var x = 0f;
            var column = 0;
            var index = 0;
            foreach (var cell in Cells(row))
            {
                var span = SpanOf(cell);
                var width = 0f;
                for (var i = column; i < Math.Min(column + span, widths.Length); i++)
                    width += widths[i];

                width += (float)(span - 1) * CellPadding * 2f;

                var cellHeight = MathF.Min(cell.DesiredSize.Y, height - PaddingBelow(row, cell));
                cell.Arrange(UIBox2.FromDimensions(x, tops[index], width, cellHeight));

                for (var i = column; i < Math.Min(column + span, widths.Length); i++)
                    x += widths[i] + CellPadding * 2f;

                column += span;
                index++;
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

        var index = 0;
        foreach (var row in Rows())
        {
            // The first, third and so on row is the odd one, and --candystripe-even is clear unless a theme sets it.
            var odd = index++ % 2 == 0;
            if (!row.Candystripe || (odd ? color : _evenStripeColor) is not { } rowColor)
                continue;

            handle.DrawRect(new UIBox2(0f, row.PixelPosition.Y, PixelWidth, row.PixelPosition.Y + row.PixelHeight), rowColor);
        }
    }
}
