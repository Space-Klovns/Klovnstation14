#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Threading.Tasks;
using Content.Client._KS14.Tgui.Controls;
using Content.Client._KS14.Tgui.Windows.Lathe;
using Content.Client.Lathe.UI;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Shared._KS14.CCVar;
using Content.Shared.Lathe;
using Content.Shared.Materials;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.UserInterface;

/// <summary>
///     The lathe's BUI opens the tgui window under the tgui theme and the stock menu otherwise, and the tgui window's
///         designs, queue and material bar send what the stock menu does.
/// </summary>
[TestOf(typeof(KsTguiLatheWindow))]
public sealed class KsTguiLatheWindowTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    private static readonly EntProtoId LatheProto = "Autolathe";
    private static readonly EntProtoId Human = "MobHuman";
    private static readonly ProtoId<MaterialPrototype> SteelMaterial = "Steel";
    private static readonly ProtoId<MaterialPrototype> GlassMaterial = "Glass";

    private async Task<EntityUid> OpenLathe()
    {
        var testMap = await Pair.CreateTestMap();
        EntityUid lathe = default;
        EntityUid user = default;
        await Server.WaitPost(() =>
        {
            lathe = SEntMan.SpawnEntity(LatheProto, new EntityCoordinates(testMap.Grid, new Vector2(0.5f, 0.5f)));
            user = SEntMan.SpawnEntity(Human, new EntityCoordinates(testMap.Grid, new Vector2(0.5f, 1.5f)));
            Server.PlayerMan.SetAttachedEntity(ServerSession!, user);

            var materials = SEntMan.System<SharedMaterialStorageSystem>();
            Assert.That(materials.TryChangeMaterialAmount(lathe, SteelMaterial, 100000), Is.True, "The lathe should take steel.");
            Assert.That(materials.TryChangeMaterialAmount(lathe, GlassMaterial, 100000), Is.True, "The lathe should take glass.");

            SEntMan.System<SharedUserInterfaceSystem>().OpenUi(lathe, LatheUiKey.Key, user);
        });
        await Pair.RunTicksSync(15);
        return lathe;
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task TheBuiOpensTheThemesWindow(bool tgui)
    {
        await OverrideCVar(Side.Client, KsCCVars.TguiStyle, tgui);
        await Pair.RunTicksSync(2);

        await OpenLathe();

        await Client.WaitAssertion(() =>
        {
            var windows = Client.ResolveDependency<IUserInterfaceManager>().WindowRoot.Children.ToList();
            Assert.That(windows.OfType<KsTguiLatheWindow>().Any(), Is.EqualTo(tgui));
            Assert.That(windows.OfType<LatheMenu>().Any(), Is.EqualTo(!tgui));
        });
    }

    /// <summary>
    ///     Fires a button's press as a click would, which a headless test has no mouse for.
    /// </summary>
    private static void Press(BaseButton button)
    {
        var field = typeof(BaseButton).GetField("OnPressed", BindingFlags.Instance | BindingFlags.NonPublic)!;
        ((Action<BaseButton.ButtonEventArgs>?)field.GetValue(button))?.Invoke(new BaseButton.ButtonEventArgs(button, null!));
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

    private static int QueuedItems(LatheComponent lathe)
    {
        return lathe.Queue.Sum(batch => batch.ItemsRequested);
    }

    [Test]
    public async Task EveryActionReachesTheServer()
    {
        await OverrideCVar(Side.Client, KsCCVars.TguiStyle, true);
        await Pair.RunTicksSync(2);

        var lathe = await OpenLathe();

        KsTguiLatheWindow window = null!;
        await Client.WaitPost(() =>
        {
            window = Client.ResolveDependency<IUserInterfaceManager>().WindowRoot.Children.OfType<KsTguiLatheWindow>().Single();
        });

        // Designs and categories.
        List<ContainerButton> titles = null!;
        await Client.WaitPost(() =>
        {
            titles = Descendants<ContainerButton>(window).Where(button => button is not KsTguiTabItem && button is not Button).ToList();
            Assert.That(titles, Is.Not.Empty, "The lathe should list designs.");

            var tabs = Descendants<KsTguiTabItem>(window).ToList();
            Assert.That(tabs, Has.Count.GreaterThan(1), "There should be All Designs and at least one category.");

            // A category narrows the list; All widens it again.
            Press(tabs[1]);
        });

        await Client.WaitPost(() =>
        {
            var tabs = Descendants<KsTguiTabItem>(window).ToList();
            var narrowed = Descendants<ContainerButton>(window).Count(button => button is not KsTguiTabItem && button is not Button);
            Assert.That(narrowed, Is.LessThanOrEqualTo(titles.Count));
            Assert.That(tabs[1].Selected, Is.True);
            Press(tabs[0]);
        });

        // Queue a design, then x5, through the real BUI.
        await Client.WaitPost(() =>
        {
            var first = Descendants<ContainerButton>(window).First(button => button is not KsTguiTabItem && button is not Button && !button.Disabled);
            Press(first);
        });
        await Pair.RunTicksSync(10);

        var afterFirst = 0;
        await Server.WaitPost(() =>
        {
            var component = SEntMan.GetComponent<LatheComponent>(lathe);
            afterFirst = QueuedItems(component) + (component.CurrentRecipe != null ? 1 : 0);
            Assert.That(afterFirst, Is.GreaterThan(0), "Clicking a design should queue it.");
        });

        await Client.WaitPost(() =>
        {
            var times = Descendants<KsTguiButton>(window).First(button => button.Text == "×5" && !button.Disabled);
            Press(times);
        });
        await Pair.RunTicksSync(10);

        await Server.WaitPost(() =>
        {
            var component = SEntMan.GetComponent<LatheComponent>(lathe);
            Assert.That(QueuedItems(component) + (component.CurrentRecipe != null ? 1 : 0), Is.GreaterThan(afterFirst), "x5 should queue more.");
        });

        // Queue buttons: delete the queued batch.
        var queuedBefore = 0;
        await Server.WaitPost(() => queuedBefore = SEntMan.GetComponent<LatheComponent>(lathe).Queue.Count);
        await Client.WaitPost(() =>
        {
            var rows = Descendants<KsTguiLatheQueueRow>(window).ToList();
            Assert.That(rows, Is.Not.Empty, "The queue should show its batches.");
            Assert.That(rows, Has.Count.EqualTo(queuedBefore));
            Press(Descendants<KsTguiButton>(rows[0]).Last());
        });
        await Pair.RunTicksSync(10);
        await Server.WaitPost(() =>
        {
            Assert.That(SEntMan.GetComponent<LatheComponent>(lathe).Queue.Count, Is.LessThan(queuedBefore), "Delete should remove the batch.");
        });

        // Material eject.
        var steelBefore = 0;
        SharedMaterialStorageSystem materials = null!;
        await Server.WaitPost(() =>
        {
            materials = SEntMan.System<SharedMaterialStorageSystem>();
            steelBefore = materials.GetMaterialAmount(lathe, SteelMaterial);
        });
        await Client.WaitPost(() =>
        {
            var eject = Descendants<KsTguiLatheMaterials>(window).Single();
            var buttons = Descendants<KsTguiButton>(eject).Where(button => button.Text == "×1").ToList();
            Assert.That(buttons, Is.Not.Empty, "The material bar should offer ejecting.");
            Press(buttons[0]);
        });
        await Pair.RunTicksSync(10);
        await Server.WaitPost(() =>
        {
            Assert.That(materials.GetMaterialAmount(lathe, SteelMaterial) + materials.GetMaterialAmount(lathe, GlassMaterial),
                Is.LessThan(steelBefore + 100000), "Ejecting a sheet should take material out.");
        });
    }
}
