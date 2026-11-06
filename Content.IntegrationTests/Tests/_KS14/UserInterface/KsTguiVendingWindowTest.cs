#nullable enable
using System.Collections.Generic;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Threading.Tasks;
using Content.Client._KS14.Tgui.Controls;
using Content.Client._KS14.Tgui.Windows.Vending;
using Content.Client.VendingMachines.UI;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Shared._KS14.CCVar;
using Content.Shared.VendingMachines;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.UserInterface;

/// <summary>
///     The vending machine's BUI opens the tgui window under the tgui theme and the stock one otherwise, and the tgui
///         window's products and categories send what the stock list does.
/// </summary>
[TestOf(typeof(KsTguiVendingWindow))]
public sealed class KsTguiVendingWindowTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    private static readonly EntProtoId VendingMachine = "VendingMachineCola";
    private static readonly EntProtoId Human = "MobHuman";

    [TestCase(true)]
    [TestCase(false)]
    public async Task TheBuiOpensTheThemesWindow(bool tgui)
    {
        await OverrideCVar(Side.Client, KsCCVars.TguiStyle, tgui);
        await Pair.RunTicksSync(2);

        var testMap = await Pair.CreateTestMap();
        EntityUid vendor = default;
        EntityUid user = default;
        await Server.WaitPost(() =>
        {
            vendor = SEntMan.SpawnEntity(VendingMachine, new EntityCoordinates(testMap.Grid, new Vector2(0.5f, 0.5f)));
            user = SEntMan.SpawnEntity(Human, new EntityCoordinates(testMap.Grid, new Vector2(0.5f, 1.5f)));
            Server.PlayerMan.SetAttachedEntity(ServerSession!, user);
            SEntMan.System<SharedUserInterfaceSystem>().OpenUi(vendor, VendingMachineUiKey.Key, user);
        });
        await Pair.RunTicksSync(10);

        await Client.WaitAssertion(() =>
        {
            var windows = Client.ResolveDependency<IUserInterfaceManager>().WindowRoot.Children.ToList();
            Assert.That(windows.OfType<KsTguiVendingWindow>().Any(), Is.EqualTo(tgui));
            Assert.That(windows.OfType<VendingMachineMenu>().Any(), Is.EqualTo(!tgui));
        });
    }

    /// <summary>
    ///     Fires a button's press as a click would, which a headless test has no mouse for.
    /// </summary>
    private static void Press(BaseButton button)
    {
        var field = typeof(BaseButton).GetField("OnPressed", BindingFlags.Instance | BindingFlags.NonPublic)!;
        ((Action<BaseButton.ButtonEventArgs>?)field.GetValue(button))?.Invoke(null!);
    }

    private static IEnumerable<T> Descendants<T>(Control root) where T : Control
    {
        foreach (var child in root.Children)
        {
            if (child is T match)
                yield return match;

            foreach (var inner in Descendants<T>(child))
                yield return inner;
        }
    }

    [Test]
    public async Task EveryActionFiresItsEvent()
    {
        KsTguiVendingWindow window = null!;
        var vended = new List<(InventoryType, string)>();

        await Client.WaitPost(() =>
        {
            window = new KsTguiVendingWindow();
            window.OnVend += (type, id) => vended.Add((type, id));

            window.Populate(new List<VendingMachineInventoryEntry>
            {
                new(InventoryType.Regular, "Crowbar", 3),
                new(InventoryType.Regular, "Wrench", 0),
                new(InventoryType.Contraband, "Screwdriver", 2),
            }, enabled: true);
        });

        await Client.WaitPost(() =>
        {
            var entries = Descendants<KsTguiVendingEntry>(window).ToList();
            Assert.That(entries, Has.Count.EqualTo(2), "Only the selected category should list.");
            Assert.That(entries.Count(entry => entry.Disabled), Is.EqualTo(1), "The sold out product should be disabled.");

            Press(entries.First(entry => !entry.Disabled));
            Assert.That(vended, Is.EqualTo(new[] { (InventoryType.Regular, "Crowbar") }));

            // The other category is a button away.
            var categories = Descendants<KsTguiButton>(window).Where(button => button.Text != null).ToList();
            Assert.That(categories, Has.Count.EqualTo(2));
            Press(categories.Last());
        });

        await Client.WaitPost(() =>
        {
            var entries = Descendants<KsTguiVendingEntry>(window).ToList();
            Assert.That(entries, Has.Count.EqualTo(1));
            Press(entries.Single());
            Assert.That(vended.Last(), Is.EqualTo((InventoryType.Contraband, "Screwdriver")));

            // The layout toggle swaps the grid for the list without losing a product.
            var toggle = Descendants<KsTguiButton>(window).Single(button => button.Text == null && button.Icon != null);
            Press(toggle);
        });

        await Client.WaitPost(() =>
        {
            Assert.That(Descendants<KsTguiVendingEntry>(window).Count(), Is.EqualTo(1));
            window.Dispose();
        });
    }
}
