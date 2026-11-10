using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Content.Client._KS14.Tgui.Controls;
using Content.Client._KS14.Tgui.Windows.Cargo;
using Content.Client.Cargo.BUI;
using Content.Client.Cargo.UI;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Shared._KS14.Cargo;
using Content.Shared._KS14.CCVar;
using Content.Shared.Cargo;
using Content.Shared.Cargo.BUI;
using Content.Shared.Cargo.Components;
using Content.Shared.Cargo.Prototypes;
using Robust.Client.GameObjects;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.UserInterface;

/// <summary>
///     The cargo console BUI opens the tgui window with the theme on and the stock one with it off, and every action the
///         tgui window offers raises its event.
/// </summary>
[TestOf(typeof(KsTguiCargoConsoleWindow))]
public sealed class KsTguiCargoWindowTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    private static readonly EntProtoId ConsolePrototype = "ComputerCargoOrders";

    [TestCase(true)]
    [TestCase(false)]
    public async Task BuiPicksWindowByTheme(bool tgui)
    {
        await OverrideCVar(Side.Client, KsCCVars.TguiStyle, tgui);
        await Pair.RunTicksSync(2);

        CargoOrderConsoleBoundUserInterface bui = null;
        EntityUid console = default;
        EntityUid station = default;
        List<ProtoId<CargoProductPrototype>> products = null;
        await Client.WaitPost(() =>
        {
            var entityManager = Client.EntMan;
            console = entityManager.SpawnEntity(ConsolePrototype, new MapCoordinates(0f, 0f, MapId.Nullspace));
            station = entityManager.SpawnEntity(null, new MapCoordinates(0f, 0f, MapId.Nullspace));
            entityManager.AddComponent<StationBankAccountComponent>(station);

            var protoMan = Client.ProtoMan;
            var allowed = entityManager.GetComponent<CargoOrderConsoleComponent>(console).AllowedGroups;
            products = protoMan.EnumeratePrototypes<CargoProductPrototype>()
                .Where(product => allowed.Contains(product.Group))
                .Take(3)
                .Select(product => new ProtoId<CargoProductPrototype>(product.ID))
                .ToList();
            Assert.That(products, Is.Not.Empty, "precondition: the console sells something");

            var account = entityManager.GetComponent<CargoOrderConsoleComponent>(console).Account;
            var pending = new CargoOrderData(1, products[0], 2, "Requester", "Because", account);
            var approved = new CargoOrderData(2, products[0], 1, "Other", "Shipping", account) { Approved = true };
            var state = new CargoConsoleInterfaceState("Cargo", 1, 6, entityManager.GetNetEntity(station), [pending, approved], products);

            bui = new CargoOrderConsoleBoundUserInterface(console, CargoConsoleUiKey.Orders);
            typeof(BoundUserInterface).GetMethod("Open", Any)!.Invoke(bui, null);
            typeof(BoundUserInterface).GetMethod("UpdateState", Any)!.Invoke(bui, [state]);
        });
        await Pair.RunTicksSync(3);

        await Client.WaitAssertion(() =>
        {
            var tguiWindow = (KsTguiCargoConsoleWindow)typeof(CargoOrderConsoleBoundUserInterface).GetField("_ksTguiMenu", Any)!.GetValue(bui);
            var stockWindow = (CargoConsoleMenu)typeof(CargoOrderConsoleBoundUserInterface).GetField("_menu", Any)!.GetValue(bui);
            Assert.That(tguiWindow != null, Is.EqualTo(tgui));
            Assert.That(stockWindow != null, Is.EqualTo(!tgui));

            if (!tgui)
            {
                bui.Dispose();
                return;
            }

            var approvedOrders = new List<int>();
            var canceledOrders = new List<int>();
            var submitted = new List<(string, string, string, int)>();
            var accountActions = 0;
            var limitToggles = 0;
            tguiWindow.OnOrderApproved += order => approvedOrders.Add(order.OrderId);
            tguiWindow.OnOrderCanceled += order => canceledOrders.Add(order.OrderId);
            tguiWindow.OnOrderSubmitted += (requester, reason, product, amount) => submitted.Add((requester, reason, product, amount));
            tguiWindow.OnAccountAction += (_, _) => accountActions++;
            tguiWindow.OnToggleUnboundedLimit += () => limitToggles++;

            // Every button, by its icon or text: the request's approve and deny, tgui's Clear, and the transfer buttons.
            var buttons = new List<KsTguiButton>();
            Collect(tguiWindow, buttons);

            var approve = buttons.Single(button => button.Icon == KsTguiIcons.Check && string.IsNullOrEmpty(button.Text));
            var deny = buttons.Single(button => button.Icon == KsTguiIcons.Times && string.IsNullOrEmpty(button.Text));
            var clear = buttons.Single(button => button.Icon == KsTguiIcons.Times && !string.IsNullOrEmpty(button.Text));
            var transfer = buttons.Single(button => button.Icon == KsTguiIcons.Check && !string.IsNullOrEmpty(button.Text));
            var toggleLimit = buttons.Single(button => button.Icon == KsTguiIcons.Lock);

            Press(approve);
            Press(deny);
            Press(clear);
            Press(transfer);
            Press(toggleLimit);

            Assert.That(approvedOrders, Is.EqualTo(new List<int> { 1 }));
            Assert.That(canceledOrders, Is.EqualTo(new List<int> { 1, 1 }), "deny, then Clear denying the one pending request");
            Assert.That(accountActions, Is.EqualTo(1));
            Assert.That(limitToggles, Is.EqualTo(1));

            // The catalog: a row opens the order form and its submit raises the order.
            var rows = new List<KsTguiCargoProductRow>();
            Collect(tguiWindow, rows);
            Assert.That(rows, Is.Not.Empty, "the catalog lists the console's products");
            Press(rows[0]);

            var orderWindow = (KsTguiCargoOrderModal)typeof(KsTguiCargoConsoleWindow).GetField("_orderWindow", Any)!.GetValue(tguiWindow);
            Assert.That(orderWindow.IsOpen, "pressing a product opens the form");

            var submit = new List<KsTguiButton>();
            Collect(orderWindow, submit);
            Press(submit.Single(button => button.Icon == KsTguiIcons.Check));
            Assert.That(submitted, Has.Count.EqualTo(1));
            Assert.That(submitted[0].Item4, Is.EqualTo(1));
            Assert.That(products.Select(id => (string)id), Does.Contain(submitted[0].Item3));

            bui.Dispose();
        });
    }

    [Test]
    public async Task CheckoutListsWhatWaitsForPickupAndTabsDrawAttention()
    {
        await OverrideCVar(Side.Client, KsCCVars.TguiStyle, true);
        await Pair.RunTicksSync(2);

        CargoOrderConsoleBoundUserInterface bui = null;
        List<ProtoId<CargoProductPrototype>> products = null;
        ProtoId<CargoAccountPrototype> account = default;
        EntityUid console = default;
        NetEntity stationNet = default;
        await Client.WaitPost(() =>
        {
            var entityManager = Client.EntMan;
            console = entityManager.SpawnEntity(ConsolePrototype, new MapCoordinates(0f, 0f, MapId.Nullspace));
            var station = entityManager.SpawnEntity(null, new MapCoordinates(0f, 0f, MapId.Nullspace));
            entityManager.AddComponent<StationBankAccountComponent>(station);
            stationNet = entityManager.GetNetEntity(station);

            var allowed = entityManager.GetComponent<CargoOrderConsoleComponent>(console).AllowedGroups;
            products = Client.ProtoMan.EnumeratePrototypes<CargoProductPrototype>()
                .Where(product => allowed.Contains(product.Group))
                .Take(2)
                .Select(product => new ProtoId<CargoProductPrototype>(product.ID))
                .ToList();
            account = entityManager.GetComponent<CargoOrderConsoleComponent>(console).Account;

            bui = new CargoOrderConsoleBoundUserInterface(console, CargoConsoleUiKey.Orders);
            typeof(BoundUserInterface).GetMethod("Open", Any)!.Invoke(bui, null);
        });

        var pending = new CargoOrderData(1, products[0], 1, "Requester", "Because", account);
        var waiting = new List<KsCargoAwaitingOrder>
        {
            new(2, products[0], "Orderer", "Approver", account, KsCargoDeliveryPoint.TradeStation, 2, 0),
            new(3, products[1], "Other", null, account, KsCargoDeliveryPoint.Telepad, 1, 2),
        };

        await Client.WaitPost(() => typeof(BoundUserInterface).GetMethod("UpdateState", Any)!.Invoke(bui,
            [new CargoConsoleInterfaceState("Cargo", 0, 6, stationNet, [pending], products) { KsAwaiting = waiting }]));
        await Pair.RunTicksSync(3);

        await Client.WaitAssertion(() =>
        {
            var window = (KsTguiCargoConsoleWindow)typeof(CargoOrderConsoleBoundUserInterface).GetField("_ksTguiMenu", Any)!.GetValue(bui);
            var requestsTab = (KsTguiTabItem)typeof(KsTguiCargoConsoleWindow).GetField("_requestsTab", Any)!.GetValue(window);
            var checkoutTab = (KsTguiTabItem)typeof(KsTguiCargoConsoleWindow).GetField("_checkoutTab", Any)!.GetValue(window);
            var catalogTab = (KsTguiTabItem)requestsTab.Parent.GetChild(0);
            requestsTab.SettleAnimations();
            checkoutTab.SettleAnimations();
            catalogTab.SettleAnimations();

            var yellow = KsTguiColors.Bright(KsTguiColor.Yellow);
            Assert.That(requestsTab.Attention && checkoutTab.Attention, "Both tabs have something waiting.");
            Assert.That(requestsTab.CurrentTextColor, Is.EqualTo(yellow), "An unselected tab with attention has yellow text.");
            Assert.That(checkoutTab.CurrentTextColor, Is.EqualTo(yellow));
            Assert.That(catalogTab.CurrentTextColor, Is.Not.EqualTo(yellow), "A plain unselected tab is not yellow.");
            Assert.That(checkoutTab.Count, Is.EqualTo("5"), "Two on a pallet, one on a telepad and two queued.");

            // Select Checkout: the text and the indicator are yellow.
            requestsTab.Selected = false;
            checkoutTab.Selected = true;
            checkoutTab.SettleAnimations();
            ((Control)window).Visible = true;
            ((BaseButton)checkoutTab).Pressed = false;
            Assert.That(checkoutTab.CurrentTextColor, Is.EqualTo(yellow));
            Assert.That(checkoutTab.IndicatorProgress, Is.GreaterThan(0f));

            var labels = new List<Label>();
            Collect(window, labels);
            var texts = labels.Select(label => label.Text).ToList();
            Assert.That(texts, Does.Contain(Loc.GetString("ks-tgui-cargo-where-trade-station")));
            Assert.That(texts, Does.Contain(Loc.GetString("ks-tgui-cargo-where-queued",
                ("where", Loc.GetString("ks-tgui-cargo-where-telepad")), ("count", 2))));
            Assert.That(texts, Does.Contain("#2").And.Contain("#3"));
            Assert.That(texts, Does.Not.Contain(Loc.GetString("ks-tgui-cargo-empty-checkout")));
        });

        // Nothing waiting and nothing pending: no attention, and the notice shows.
        await Client.WaitPost(() => typeof(BoundUserInterface).GetMethod("UpdateState", Any)!.Invoke(bui,
            [new CargoConsoleInterfaceState("Cargo", 0, 6, stationNet, [], products)]));
        await Pair.RunTicksSync(3);

        await Client.WaitAssertion(() =>
        {
            var window = (KsTguiCargoConsoleWindow)typeof(CargoOrderConsoleBoundUserInterface).GetField("_ksTguiMenu", Any)!.GetValue(bui);
            var requestsTab = (KsTguiTabItem)typeof(KsTguiCargoConsoleWindow).GetField("_requestsTab", Any)!.GetValue(window);
            var checkoutTab = (KsTguiTabItem)typeof(KsTguiCargoConsoleWindow).GetField("_checkoutTab", Any)!.GetValue(window);
            requestsTab.SettleAnimations();
            checkoutTab.SettleAnimations();

            Assert.That(requestsTab.Attention || checkoutTab.Attention, Is.False);
            Assert.That(requestsTab.CurrentTextColor, Is.Not.EqualTo(KsTguiColors.Bright(KsTguiColor.Yellow)));

            var notices = new List<KsTguiNoticeBox>();
            Collect(window, notices);
            Assert.That(notices.Select(notice => notice.Text), Does.Contain(Loc.GetString("ks-tgui-cargo-empty-checkout")));
            bui.Dispose();
        });
    }

    private static void Press(BaseButton button)
    {
        var press = typeof(BaseButton).GetField("OnPressed", Any)!;
        ((System.Delegate)press.GetValue(button))!.DynamicInvoke([null]);
    }

    private static void Collect<T>(Control control, List<T> found) where T : Control
    {
        foreach (var child in control.Children)
        {
            // The order form is a modal inside the window; its own buttons are looked at from the form.
            if (child is KsTguiModal)
                continue;

            if (child is T match)
                found.Add(match);
            Collect(child, found);
        }
    }
}
