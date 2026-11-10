using System.Numerics;
using Content.Client.UserInterface.Controls;
using Content.Shared._KS14.Fluidmos;
using Content.Shared.Chemistry.Reagent;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;

namespace Content.Client._KS14.Fluidmos;

/// <summary>
///     A table of the reagents a gas mixture carries, by phase, laid out as the gas analyzer's gas table is, with a bar of
///         their shares under it: the fluid half of the gas analyzer's and canister's readouts. Hidden while there is
///         nothing to list.
/// </summary>
/// <remarks>
///     A phase a reagent has none of is left blank rather than marked, which read as a minus sign beside its amount. A
///         reagent that evaporates into its own gas has no vapor cell at all, since its vapor is that gas, listed with
///         the gases; when every reagent listed is one, there is no vapor column.
/// </remarks>
public sealed partial class KsFluidListControl : BoxContainer
{
    [Dependency] private IPrototypeManager _prototypeManager = default!;

    /// <summary>
    ///     The gas table's stripe edge colour.
    /// </summary>
    private static readonly Color HeadingRuleColor = Color.FromHex("#525252");

    /// <summary>
    ///     Gap between the table's columns, and between its rows.
    /// </summary>
    private const int TableSeparation = 10;

    private readonly GridContainer _table;
    private readonly SplitBar _bar;

    public KsFluidListControl()
    {
        IoCManager.InjectDependencies(this);

        Orientation = LayoutOrientation.Vertical;
        Visible = false;

        // A grid rather than the gas table's separate columns, so that rows stay level when a cell is left blank. Its two
        //     separations are kept equal: GridContainer measures with them one way round and arranges with them the
        //     other, so with a wider column gap each row was laid out lower than measured, and from three reagents the
        //     last row slid under the bar.
        _table = new GridContainer { HSeparationOverride = TableSeparation, VSeparationOverride = TableSeparation };
        AddChild(_table);

        AddChild(new Control { MinSize = new Vector2(0, 14) });

        _bar = new SplitBar { MinHeight = 30, MinBarSize = new Vector2(12, 0) };
        AddChild(_bar);
    }

    public void SetEntries(KsFluidEntry[]? entries)
    {
        _table.RemoveAllChildren();
        _bar.Clear();
        Visible = entries is { Length: > 0 };
        if (entries == null)
            return;

        var showVapor = Array.Exists(entries, entry => !entry.Linked);
        _table.Columns = showVapor ? 4 : 3;

        AddHeading("ks-fluid-list-column-reagent");
        AddHeading("ks-fluid-list-column-liquid");
        AddHeading("ks-fluid-list-column-solid");
        if (showVapor)
            AddHeading("ks-fluid-list-column-vapor");

        // A rule under the headings rather than the gas table's stripe: a grid sizes its rows in whole pixels, which at
        //     a fractional UI scale can leave a stripe less room than its own padding.
        for (var i = 0; i < _table.Columns; i++)
        {
            _table.AddChild(new PanelContainer
            {
                MinHeight = 2,
                Margin = new Thickness(0, 2, 0, 4),
                PanelOverride = new StyleBoxFlat(HeadingRuleColor),
            });
        }

        var total = 0f;
        foreach (var entry in entries)
        {
            total += entry.Total;
        }

        foreach (var entry in entries)
        {
            _prototypeManager.TryIndex(entry.Reagent, out var reagentPrototype);
            var name = reagentPrototype?.LocalizedName ?? entry.Reagent.Id;

            _table.AddChild(new Label
            {
                Text = name,
                HorizontalExpand = true,
                ToolTip = entry.Linked ? Loc.GetString("ks-fluid-list-linked-tooltip") : null,
                // A label ignores the mouse by default, and a tooltip needs it.
                MouseFilter = entry.Linked ? MouseFilterMode.Pass : MouseFilterMode.Ignore,
            });
            AddAmount(entry.Liquid);
            AddAmount(entry.Solid);
            // A blank cell for a linked reagent, which has no vapor of its own.
            if (showVapor)
                AddAmount(entry.Linked ? 0f : entry.Vapor);

            if (total > 0f && entry.Total > 0f)
            {
                _bar.AddEntry(entry.Total, reagentPrototype?.SubstanceColor ?? Color.White, tooltip: Loc.GetString("ks-fluid-list-bar-tooltip",
                    ("reagent", name),
                    ("units", $"{entry.Total:0.##}"),
                    ("percentage", $"{entry.Total / total * 100f:0.#}")));
            }
        }
    }

    private void AddHeading(string key)
    {
        _table.AddChild(new Label { Text = Loc.GetString(key), Align = Label.AlignMode.Center });
    }

    private void AddAmount(float units)
    {
        _table.AddChild(new Label
        {
            Text = units >= KsFluidEntry.MinUnits ? Loc.GetString("ks-fluid-list-units", ("units", $"{units:0.##}")) : string.Empty,
            Align = Label.AlignMode.Right,
        });
    }
}
