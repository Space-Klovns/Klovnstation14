using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._KS14.Tgui.Controls;

/// <summary>
///     tgui's <c>Table.Cell</c> where a plain control will not do: it can span columns, be a header cell (bold, with 6px under
///         it) or be collapsing, and it sets the vertical alignment. Its children are its content, stacked.
/// </summary>
public sealed class KsTguiTableCell : BoxContainer
{
    private int _colSpan = 1;
    private bool _header;
    private bool _collapsing;
    private KsTguiVerticalAlign _verticalAlign = KsTguiVerticalAlign.Baseline;

    public KsTguiTableCell()
    {
        Orientation = LayoutOrientation.Vertical;
    }

    /// <summary>
    ///     tgui's <c>colSpan</c>: how many columns the cell covers.
    /// </summary>
    public int ColSpan
    {
        get => _colSpan;
        set
        {
            _colSpan = Math.Max(1, value);
            Parent?.InvalidateMeasure();
            Parent?.Parent?.InvalidateMeasure();
        }
    }

    /// <summary>
    ///     tgui's <c>header</c>: bold text and 6px under the cell.
    /// </summary>
    public bool Header
    {
        get => _header;
        set
        {
            _header = value;
            if (value)
            {
                foreach (var child in Children)
                    KsTguiTableRow.Embolden(child);
            }

            Parent?.InvalidateMeasure();
            Parent?.Parent?.InvalidateMeasure();
        }
    }

    /// <summary>
    ///     tgui's <c>collapsing</c>: the column is as narrow as its content, and the others share the spare width.
    /// </summary>
    public bool Collapsing
    {
        get => _collapsing;
        set
        {
            _collapsing = value;
            Parent?.Parent?.InvalidateMeasure();
        }
    }

    /// <summary>
    ///     tgui's <c>verticalAlign</c>.
    /// </summary>
    public KsTguiVerticalAlign VerticalAlign
    {
        get => _verticalAlign;
        set
        {
            _verticalAlign = value;
            Parent?.Parent?.InvalidateMeasure();
        }
    }

    protected override void ChildAdded(Control newChild)
    {
        base.ChildAdded(newChild);

        if (_header)
            KsTguiTableRow.Embolden(newChild);
    }
}
