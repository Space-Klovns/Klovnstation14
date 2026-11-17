#nullable enable
using System.Linq;
using Content.Client._KS14.Fluidmos;
using Content.Client.UserInterface.Controls;
using Content.IntegrationTests.Fixtures;
using Content.Shared._KS14.Fluidmos;
using Content.Shared.Chemistry.Reagent;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.Fluidmos;

/// <summary>
///     The reagent table in gas analyzers: blank where a reagent has none of a phase, no vapor for one that
///         evaporates into its own gas, and no vapor column when every reagent listed does.
/// </summary>
[TestOf(typeof(KsFluidListControl))]
public sealed class KsFluidListControlTests : GameTest
{
    private static readonly ProtoId<ReagentPrototype> WaterReagent = "Water";
    private static readonly ProtoId<ReagentPrototype> EthanolReagent = "Ethanol";
    private static readonly ProtoId<ReagentPrototype> BicaridineReagent = "Bicaridine";
    private static readonly ProtoId<ReagentPrototype> CryoxadoneReagent = "Cryoxadone";

    /// <summary>
    ///     Lays the control out in a window and returns the text of each table row, by row.
    /// </summary>
    private async Task<string[][]> Rows(KsFluidEntry[] entries, bool showPhasePoints = false)
    {
        var client = Pair.Client;
        DefaultWindow window = null!;
        KsFluidListControl list = null!;

        await client.WaitPost(() =>
        {
            window = new DefaultWindow();
            list = new KsFluidListControl { ShowPhasePoints = showPhasePoints };
            list.SetEntries(entries);
            window.Contents.AddChild(list);
            window.OpenCentered();
        });
        await Pair.RunTicksSync(5);

        string[][] rows = null!;
        await client.WaitPost(() =>
        {
            var table = list.Children.OfType<GridContainer>().Single();
            var cells = table.Children.ToList();
            var columns = table.Columns;
            Assert.That(cells.Count % columns, Is.Zero, "The table has a ragged last row.");

            rows = Enumerable.Range(0, cells.Count / columns)
                .Select(row => cells.Skip(row * columns).Take(columns).Select(cell => cell is Label label ? label.Text ?? string.Empty : "<rule>").ToArray())
                .ToArray();

            window.Close();
        });

        return rows;
    }

    [Test]
    public async Task BlankPhasesAndNoVaporForReagentsThatBecomeGas()
    {
        // Water is linked to water vapor; ethanol's vapor is its own.
        var rows = await Rows([
            new KsFluidEntry(WaterReagent, Solid: 0f, Liquid: 100f, Vapor: 0f, Linked: true),
            new KsFluidEntry(EthanolReagent, Solid: 0f, Liquid: 5f, Vapor: 2f, Linked: false),
        ]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(rows[0], Has.Length.EqualTo(4), "The vapor column is missing, though ethanol has vapor of its own.");
            Assert.That(rows.Skip(2).SelectMany(row => row), Has.None.EqualTo("-"), "An empty phase is shown as a dash, which reads as a minus sign.");
            Assert.That(rows[2][2], Is.Empty, "Water has no solid, but its solid cell isn't blank.");
            Assert.That(rows[2][3], Is.Empty, "Water becomes water vapor, a gas, but has a vapor cell.");
            Assert.That(rows[3][3], Is.Not.Empty, "Ethanol's own vapor isn't shown.");
        }
    }

    /// <summary>
    ///     However many reagents are listed, every row of the table is laid out above the share bar under it, not behind it.
    /// </summary>
    [TestCase(1)]
    [TestCase(4)]
    public async Task TableRowsStayClearOfTheBar(int reagents)
    {
        var client = Pair.Client;
        DefaultWindow window = null!;
        KsFluidListControl list = null!;
        ProtoId<ReagentPrototype>[] prototypes = [WaterReagent, EthanolReagent, BicaridineReagent, CryoxadoneReagent];

        await client.WaitPost(() =>
        {
            window = new DefaultWindow();
            list = new KsFluidListControl();
            list.SetEntries(prototypes.Take(reagents)
                .Select(reagent => new KsFluidEntry(reagent, Solid: 0f, Liquid: 10f, Vapor: 0f, Linked: false))
                .ToArray());
            window.Contents.AddChild(list);
            window.OpenCentered();
        });
        await Pair.RunTicksSync(5);

        await client.WaitPost(() =>
        {
            var table = list.Children.OfType<GridContainer>().Single();
            var bar = list.Children.OfType<SplitBar>().Single();
            var lastCell = table.Children.Last();
            var lastRowBottom = lastCell.GlobalPosition.Y + lastCell.Size.Y;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(lastRowBottom, Is.LessThanOrEqualTo(table.GlobalPosition.Y + table.Size.Y + 0.5f),
                    "The table's last row is laid out past the table's own bounds.");
                Assert.That(lastRowBottom, Is.LessThanOrEqualTo(bar.GlobalPosition.Y + 0.5f),
                    "The table's last row is laid out behind the share bar.");
            }

            window.Close();
        });
    }

    [Test]
    public async Task NoVaporColumnWhenEveryReagentBecomesGas()
    {
        var rows = await Rows([new KsFluidEntry(WaterReagent, Solid: 3f, Liquid: 100f, Vapor: 0f, Linked: true)]);

        Assert.That(rows[0], Has.Length.EqualTo(3), "Every reagent listed becomes a gas, but there is still a vapor column.");
    }

    /// <summary>
    ///     The gas analyzer's boils-at and melts-at columns: there only when asked for, blank for a reagent that never
    ///         boils, and "supercritical" for a gas past its critical temperature.
    /// </summary>
    [Test]
    public async Task PhasePointColumnsOnlyWhenAskedFor()
    {
        KsFluidEntry[] entries =
        [
            new(WaterReagent, Solid: 0f, Liquid: 100f, Vapor: 0f, Linked: true, BoilingPoint: 360.4f, MeltingPoint: 273.15f),
            new(EthanolReagent, Solid: 0f, Liquid: 5f, Vapor: 2f, Linked: false, BoilingPoint: float.NaN, MeltingPoint: float.NaN),
            new(BicaridineReagent, Solid: 0f, Liquid: 5f, Vapor: 0f, Linked: true, Supercritical: true),
        ];

        var plain = await Rows(entries);
        var withPoints = await Rows(entries, showPhasePoints: true);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(plain[0], Has.Length.EqualTo(4), "The table grew phase-point columns it didn't ask for.");
            Assert.That(withPoints[0], Has.Length.EqualTo(6), "The analyzer's table has no boils-at and melts-at columns.");
            Assert.That(withPoints[2][4], Does.Contain("360"), "Water's boiling point at the pipe's pressure isn't shown.");
            Assert.That(withPoints[2][5], Does.Contain("273"), "Water's melting point isn't shown.");
            Assert.That(withPoints[3][4], Is.Empty, "A reagent that never boils shows a boiling point.");
            Assert.That(withPoints[4][4], Is.EqualTo("supercritical"), "A gas past its critical temperature isn't marked supercritical.");
        }
    }
}
