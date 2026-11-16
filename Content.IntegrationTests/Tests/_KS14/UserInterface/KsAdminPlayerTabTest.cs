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
}
