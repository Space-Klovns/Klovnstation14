using Robust.Client.UserInterface;

namespace Content.Client._KS14.Tgui.Controls;

/// <summary>
///     tgui's <c>Table.Row</c>: its children are the cells, laid out by the parent <see cref="KsTguiTable"/>.
/// </summary>
public sealed class KsTguiTableRow : Control
{
    /// <summary>
    ///     tgui's <c>candystripe</c> class on the row: it is striped when it is an odd row of the table, the header row
    ///         included in the count, as CSS <c>nth-child(odd)</c> does.
    /// </summary>
    public bool Candystripe { get; set; }

    /// <summary>
    ///     tgui's <c>header</c>: 6px of space under the row. Make the cells' text bold with the heading label class.
    /// </summary>
    public bool Header { get; set; }
}
