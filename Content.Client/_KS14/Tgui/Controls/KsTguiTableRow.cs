using System.Linq;
using Content.Client.Stylesheets.Fonts;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._KS14.Tgui.Controls;

/// <summary>
///     tgui's <c>Table.Row</c>: its children are the cells, laid out by the parent <see cref="KsTguiTable"/>. A cell is any
///         control, or a <see cref="KsTguiTableCell"/> where it needs a column span, a header or a vertical alignment.
/// </summary>
public sealed class KsTguiTableRow : Control
{
    private bool _header;

    /// <summary>
    ///     tgui's <c>candystripe</c> class on the row: it is striped when it is an odd row of the table, the header row
    ///         included in the count, as CSS <c>nth-child(odd)</c> does.
    /// </summary>
    public bool Candystripe { get; set; }

    /// <summary>
    ///     tgui's <c>header</c>: 6px of space under the row, and bold text in its cells.
    /// </summary>
    public bool Header
    {
        get => _header;
        set
        {
            _header = value;
            if (value)
                EmboldenCells();

            InvalidateMeasure();
            Parent?.InvalidateMeasure();
        }
    }

    protected override void ChildAdded(Control newChild)
    {
        base.ChildAdded(newChild);

        if (_header)
            KsTguiTableRow.Embolden(newChild);
    }

    private void EmboldenCells()
    {
        foreach (var cell in Children)
            Embolden(cell);
    }

    /// <summary>
    ///     Makes the plain labels of a header cell bold, as <c>font-weight: bold</c> does. A label with a font of its own or a
    ///         style class keeps its look.
    /// </summary>
    internal static void Embolden(Control cell)
    {
        if (cell is Label label)
        {
            if (label.FontOverride != null || label.StyleClasses.Any())
                return;

            label.FontOverride = IoCManager.Resolve<KsTguiFonts>().GetFont(12, FontKind.Bold);
            return;
        }

        if (cell is not KsTguiTableCell)
            return;

        foreach (var child in cell.Children)
            Embolden(child);
    }
}
