#nullable enable
using System.Linq;
using System.Threading.Tasks;
using Content.Client.Administration.UI;
using Content.Client.UserInterface.Controls;
using Content.Client.UserInterface.Systems.Admin;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server.Administration.Managers;
using Content.Shared._KS14.CCVar;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared;

namespace Content.IntegrationTests.Tests._KS14.UserInterface;

/// <summary>
///     A <see cref="ListContainer"/> filled while its window is closed, as the admin menu's player tab is, sizes its rows as
///         they really are once shown, at any UI scale, so the last player can be scrolled to (#454).
/// </summary>
[TestOf(typeof(ListContainer))]
public sealed class KsAdminPlayerTabTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    private const int Dummies = 30;

    [TestCase(false, 1f)]
    [TestCase(false, 1.25f)]
    [TestCase(true, 1f)]
    [TestCase(true, 1.25f)]
    public async Task TheLastPlayerCanBeScrolledTo(bool tgui, float uiScale)
    {
        await OverrideCVar(Side.Client, KsCCVars.TguiStyle, tgui);
        await OverrideCVar(Side.Client, CVars.ResAutoScaleEnabled, false);
        await OverrideCVar(Side.Client, CVars.DisplayUIScale, uiScale);

        // The admin UI controller's own window, made closed when the game state was entered, before the theme and scale
        //     above: the player list fills it while it is closed, as in a round.
        AdminMenuWindow menu = default!;
        await Client.WaitPost(() =>
        {
            var controller = Client.ResolveDependency<IUserInterfaceManager>().GetUIController<AdminUIController>();
            menu = (AdminMenuWindow) typeof(AdminUIController)
                .GetField("_window", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .GetValue(controller)!;
            Assert.That(menu, Is.Not.Null, "the controller made its window on entering the game");
        });
        await Server.AddDummySessions(Dummies);
        await Server.WaitPost(() => Server.ResolveDependency<IAdminManager>().PromoteHost(ServerSession!));
        await Pair.RunTicksSync(20);

        var tab = menu.PlayerTabControl;
        await Client.WaitPost(() =>
        {
            menu.OpenCentered();
            ((TabContainer) tab.Parent!).CurrentTab = tab.GetPositionInParent();
        });
        await Pair.RunTicksSync(10);

        await Client.WaitPost(() =>
        {
            var scrollBar = tab.SearchList.Children.OfType<VScrollBar>().Single();
            Assert.That(scrollBar.Visible, "thirty players overflow the list");
            scrollBar.Value = scrollBar.MaxValue;
        });
        await Pair.RunTicksSync(10);

        await Client.WaitPost(() =>
        {
            var list = tab.SearchList;
            Assert.That(list.UIScale, Is.EqualTo(uiScale).Within(0.001f));

            var rows = list.Children.OfType<ListContainerButton>().ToList();
            var last = rows.SingleOrDefault(row => row.Index == Dummies);
            Assert.That(last, Is.Not.Null, "scrolled to the end, the last player has no row");
            Assert.That(last!.GlobalPixelRect.Bottom, Is.LessThanOrEqualTo(list.GlobalPixelRect.Bottom),
                $"the last player's row is cut off: row {last.GlobalPixelRect}, list {list.GlobalPixelRect}");
            menu.Close();
        });
    }

    /// <summary>
    ///     A handful of players in a short window, as in the issue: five players and four rows, with no scrollbar to reach
    ///         the fifth. At every height, each player has a row that is either in sight or can be scrolled to.
    /// </summary>
    [TestCase(false, 1f)]
    [TestCase(false, 1.25f)]
    [TestCase(true, 1f)]
    [TestCase(true, 1.25f)]
    public async Task EveryPlayerFitsOrCanBeScrolledTo(bool tgui, float uiScale)
    {
        const int few = 4;

        await OverrideCVar(Side.Client, KsCCVars.TguiStyle, tgui);
        await OverrideCVar(Side.Client, CVars.ResAutoScaleEnabled, false);
        await OverrideCVar(Side.Client, CVars.DisplayUIScale, uiScale);

        AdminMenuWindow menu = default!;
        await Client.WaitPost(() =>
        {
            var controller = Client.ResolveDependency<IUserInterfaceManager>().GetUIController<AdminUIController>();
            menu = (AdminMenuWindow) typeof(AdminUIController)
                .GetField("_window", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .GetValue(controller)!;
        });
        await Server.AddDummySessions(few);
        await Server.WaitPost(() => Server.ResolveDependency<IAdminManager>().PromoteHost(ServerSession!));
        await Pair.RunTicksSync(20);

        var tab = menu.PlayerTabControl;
        await Client.WaitPost(() =>
        {
            menu.OpenCentered();
            ((TabContainer) tab.Parent!).CurrentTab = tab.GetPositionInParent();
        });
        await Pair.RunTicksSync(10);

        var players = 0;
        await Client.WaitPost(() => players = tab.SearchList.Data.Count);
        Assert.That(players, Is.EqualTo(few + 1), "every player, the host included, is in the list's data");

        for (var height = (int) menu.MinSize.Y; height <= 420; height += 10)
        {
            var windowHeight = height;
            await Client.WaitPost(() => menu.SetSize = new System.Numerics.Vector2(menu.MinSize.X, windowHeight));
            await Pair.RunTicksSync(5);

            var scrolls = false;
            await Client.WaitPost(() =>
            {
                var scrollBar = tab.SearchList.Children.OfType<VScrollBar>().Single();
                scrolls = scrollBar.Visible;
                if (scrolls)
                    scrollBar.Value = scrollBar.MaxValue;
            });
            if (scrolls)
                await Pair.RunTicksSync(5);

            await Client.WaitAssertion(() =>
            {
                var list = tab.SearchList;
                var rows = list.Children.OfType<ListContainerButton>().ToList();
                var last = rows.SingleOrDefault(row => row.Index == players - 1);
                Assert.That(last, Is.Not.Null, $"at a window {windowHeight} high, the last player has no row (scrollbar: {scrolls})");
                Assert.That(last!.GlobalPixelRect.Bottom, Is.LessThanOrEqualTo(list.GlobalPixelRect.Bottom),
                    $"at a window {windowHeight} high, the last player's row is cut off with no way to scroll to it: row {last.GlobalPixelRect}, list {list.GlobalPixelRect}, scrollbar: {scrolls}");
            });
        }

        await Client.WaitPost(() => menu.Close());
    }
}
