#nullable enable
using System.Linq;
using Content.Client._KS14.Fluidmos;
using Content.IntegrationTests.Fixtures;
using Content.Shared._KS14.Fluidmos;
using Content.Shared.Chemistry.Reagent;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.Fluidmos;

/// <summary>
///     The reagent table in gas analyzers and canisters: blank where a reagent has none of a phase, no vapor for one that
///         evaporates into its own gas, and no vapor column when every reagent listed does.
/// </summary>
[TestOf(typeof(KsFluidListControl))]
public sealed class KsFluidListControlTests : GameTest
{
    private static readonly ProtoId<ReagentPrototype> WaterReagent = "Water";
    private static readonly ProtoId<ReagentPrototype> EthanolReagent = "Ethanol";

    /// <summary>
    ///     Lays the control out in a window and returns the text of each table row, by row.
    /// </summary>
    private async Task<string[][]> Rows(KsFluidEntry[] entries)
    {
        var client = Pair.Client;
        DefaultWindow window = null!;
        KsFluidListControl list = null!;

        await client.WaitPost(() =>
        {
            window = new DefaultWindow();
            list = new KsFluidListControl();
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

    [Test]
    public async Task NoVaporColumnWhenEveryReagentBecomesGas()
    {
        var rows = await Rows([new KsFluidEntry(WaterReagent, Solid: 3f, Liquid: 100f, Vapor: 0f, Linked: true)]);

        Assert.That(rows[0], Has.Length.EqualTo(3), "Every reagent listed becomes a gas, but there is still a vapor column.");
    }
}
