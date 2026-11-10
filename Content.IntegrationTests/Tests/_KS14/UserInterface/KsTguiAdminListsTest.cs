#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Content.Client._KS14.Tgui;
using Content.Client.Administration.Managers;
using Content.Client.Administration.UI.CustomControls;
using Content.Client.Administration.UI.Tabs.ObjectsTab;
using Content.Client.Administration.UI.Tabs.PlayerTab;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Shared._KS14.CCVar;
using Content.Shared.Administration;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Network;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.UserInterface;

/// <summary>
///     The F7 menu's Objects and Players lists under tgui: their rows are a tgui <c>Table</c>'s (candystripe instead of the
///         stock alternating boxes, no bars between columns, a bold header with no box), and under Baron they are exactly the
///         stock ones, also after the cvar is flipped live. The stock boxes are set from code, so a sheet rule could not
///         have changed them.
/// </summary>
[TestOf(typeof(ObjectsTabEntry))]
public sealed class KsTguiAdminListsTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    private static readonly Color StockAlt = Color.FromHex("#292B38");
    private static readonly Color StockDefault = Color.FromHex("#2F2F3B");

    private async Task SetTheme(bool tgui)
    {
        await OverrideCVar(Side.Client, KsCCVars.TguiStyle, tgui);
        await Pair.RunTicksSync(2);
    }

    private static IEnumerable<T> Descendants<T>(Control control) where T : Control
    {
        foreach (var child in control.Children)
        {
            if (child is T match)
                yield return match;

            foreach (var inner in Descendants<T>(child))
                yield return inner;
        }
    }

    /// <summary>
    ///     A row of each of the four kinds, in a control of its own on the window root, with the stock boxes they were given.
    /// </summary>
    private sealed record Rows(
        Control Root,
        ObjectsTabEntry ObjectsOdd,
        ObjectsTabEntry ObjectsEven,
        PlayerTabEntry PlayersOdd,
        PlayerTabEntry PlayersEven,
        ObjectsTabHeader ObjectsHeader,
        PlayerTabHeader PlayersHeader,
        StyleBoxFlat Stock);

    private Rows MakeRows()
    {
        var admin = Client.ResolveDependency<IClientAdminManager>();
        var stock = new StyleBoxFlat(StockAlt);
        var player = new PlayerInfo("name", "character", "character", "job", false, null, null, 0, null,
            new NetUserId(Guid.NewGuid()), true, true, TimeSpan.FromHours(1));

        var rows = new Rows(
            new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical },
            new ObjectsTabEntry(admin, "odd", new NetEntity(1), stock, striped: true),
            new ObjectsTabEntry(admin, "even", new NetEntity(2), new StyleBoxFlat(StockDefault), striped: false),
            new PlayerTabEntry(player, stock, AdminPlayerTabColorOption.Off, AdminPlayerTabRoleTypeOption.RoleType, AdminPlayerTabSymbolOption.Off, striped: true),
            new PlayerTabEntry(player, new StyleBoxFlat(StockDefault), AdminPlayerTabColorOption.Off, AdminPlayerTabRoleTypeOption.RoleType, AdminPlayerTabSymbolOption.Off, striped: false),
            new ObjectsTabHeader(),
            new PlayerTabHeader(),
            stock);

        // The Players tab gives its header the stock box from code, after making it.
        rows.PlayersHeader.BackgroundColorPanel.PanelOverride = new StyleBoxFlat(StockAlt);

        foreach (var row in new Control[] { rows.ObjectsHeader, rows.ObjectsOdd, rows.ObjectsEven, rows.PlayersHeader, rows.PlayersOdd, rows.PlayersEven })
            rows.Root.AddChild(row);

        Client.ResolveDependency<IUserInterfaceManager>().WindowRoot.AddChild(rows.Root);
        return rows;
    }

    private static void AssertStock(Rows rows)
    {
        Assert.Multiple(() =>
        {
            Assert.That(rows.ObjectsOdd.PanelOverride, Is.SameAs(rows.Stock), "objects odd box");
            Assert.That(rows.PlayersOdd.PanelOverride, Is.SameAs(rows.Stock), "players odd box");
            Assert.That(((StyleBoxFlat)rows.ObjectsEven.PanelOverride!).BackgroundColor, Is.EqualTo(StockDefault), "objects even box");
            Assert.That(((StyleBoxFlat)rows.PlayersEven.PanelOverride!).BackgroundColor, Is.EqualTo(StockDefault), "players even box");
            Assert.That(((StyleBoxFlat)rows.PlayersHeader.BackgroundColorPanel.PanelOverride!).BackgroundColor, Is.EqualTo(StockAlt), "players header box");

            foreach (var row in new Control[] { rows.ObjectsHeader, rows.ObjectsOdd, rows.PlayersHeader, rows.PlayersOdd })
            {
                var separators = Descendants<VSeparator>(row).ToList();
                Assert.That(separators, Is.Not.Empty, row.GetType().Name + " has bars");
                Assert.That(separators.All(separator => separator.Visible), row.GetType().Name + " bars shown");
            }

            foreach (var label in Descendants<Label>(rows.ObjectsHeader).Concat(Descendants<Label>(rows.PlayersHeader)))
                Assert.That(label.HasStyleClass(KsTguiSheetlet.StyleClassTableHeaderLabel), Is.False, "header text not bold");
        });
    }

    private static void AssertTable(Rows rows)
    {
        Assert.Multiple(() =>
        {
            // The odd rows are the candystripe, and not the stock box. Under the Nanotrasen theme the even ones are clear.
            foreach (var odd in new PanelContainer[] { rows.ObjectsOdd, rows.PlayersOdd })
            {
                var stripe = odd.PanelOverride as StyleBoxFlat;
                Assert.That(stripe, Is.Not.Null.And.Not.SameAs(rows.Stock), "odd row has the stripe");
                Assert.That(stripe!.BackgroundColor, Is.Not.EqualTo(StockAlt));
                Assert.That(stripe.BackgroundColor.A, Is.GreaterThan(0f).And.LessThan(1f), "the stripe is a translucent wash");
            }

            Assert.That(rows.ObjectsEven.PanelOverride, Is.Null, "objects even row is clear");
            Assert.That(rows.PlayersEven.PanelOverride, Is.Null, "players even row is clear");
            Assert.That(rows.PlayersHeader.BackgroundColorPanel.PanelOverride, Is.Null, "a header row is not striped");

            foreach (var row in new Control[] { rows.ObjectsHeader, rows.ObjectsOdd, rows.ObjectsEven, rows.PlayersHeader, rows.PlayersOdd, rows.PlayersEven })
                Assert.That(Descendants<VSeparator>(row).Any(separator => separator.Visible), Is.False, row.GetType().Name + " has no bars");

            foreach (var label in Descendants<Label>(rows.ObjectsHeader).Concat(Descendants<Label>(rows.PlayersHeader)))
                Assert.That(label.HasStyleClass(KsTguiSheetlet.StyleClassTableHeaderLabel), Is.True, "header text is bold");
        });
    }

    [Test]
    public async Task RowsAreATguiTableUnderTguiAndStockUnderBaronAndFollowTheCvarLive()
    {
        await SetTheme(true);

        Rows rows = null!;
        await Client.WaitPost(() => rows = MakeRows());
        await Pair.RunTicksSync(2);

        await Client.WaitAssertion(() => AssertTable(rows));

        await SetTheme(false);
        await Client.WaitAssertion(() => AssertStock(rows));

        await SetTheme(true);
        await Client.WaitAssertion(() => AssertTable(rows));

        // Rows made while Baron is on are stock from the start.
        await SetTheme(false);
        Rows baron = null!;
        await Client.WaitPost(() => baron = MakeRows());
        await Pair.RunTicksSync(2);
        await Client.WaitAssertion(() => AssertStock(baron));

        await SetTheme(true);
        await Client.WaitAssertion(() => AssertTable(baron));

        await Client.WaitPost(() =>
        {
            rows.Root.Orphan();
            baron.Root.Orphan();
        });
    }
}
