#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Threading.Tasks;
using Content.Client._KS14.Tgui.Controls;
using Content.Client._KS14.Tgui.Windows.Lathe;
using Content.Client._KS14.Tgui.Windows.Materials;
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

    /// <summary>
    ///     The design itself: the row's only container button that isn't a tgui button.
    /// </summary>
    private static ContainerButton Title(Control row)
    {
        return Descendants<ContainerButton>(row).Single(button => button is not Button);
    }

    private static int QueuedItems(LatheComponent lathe)
    {
        return lathe.Queue.Sum(batch => batch.ItemsRequested);
    }

    [Test]
    public async Task EmptyQueueNoticeShowsOnlyWhenNothingIsPrinting()
    {
        await OverrideCVar(Side.Client, KsCCVars.TguiStyle, true);
        await Pair.RunTicksSync(2);

        await OpenLathe();

        KsTguiLatheWindow window = null!;
        var emptyText = string.Empty;
        await Client.WaitPost(() =>
        {
            window = Client.ResolveDependency<IUserInterfaceManager>().WindowRoot.Children.OfType<KsTguiLatheWindow>().Single();
            emptyText = Robust.Shared.Localization.Loc.GetString("ks-tgui-lathe-queue-empty");
        });

        bool NoticeShown() => Descendants<Label>(window).Single(label => label.Text == emptyText).Visible;

        await Client.WaitAssertion(() => Assert.That(NoticeShown(), Is.True, "Nothing is printing and nothing is queued."));

        // The lathe has taken the only design off its queue and is printing it: the queue is empty, but so is no claim.
        await Client.WaitPost(() =>
        {
            window.PopulateQueueList(new List<LatheRecipeBatch>());
            window.SetQueueInfo(window.Recipes.First());
        });
        await Client.WaitAssertion(() => Assert.That(NoticeShown(), Is.False, "A design is printing, so the queue is not empty."));

        // And once it is done, nothing is left.
        await Client.WaitPost(() => window.SetQueueInfo(null));
        await Client.WaitAssertion(() => Assert.That(NoticeShown(), Is.True, "The print finished and the queue is empty."));
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
            titles = Descendants<KsTguiLatheRecipeRow>(window).Select(row => Title(row)).ToList();
            Assert.That(titles, Is.Not.Empty, "The lathe should list designs.");

            var tabs = Descendants<KsTguiFabricatorTab>(window).ToList();
            Assert.That(tabs, Has.Count.GreaterThan(1), "There should be All Designs and at least one category.");

            // A category narrows the list; All widens it again.
            Press(tabs[1]);
        });

        await Client.WaitPost(() =>
        {
            var tabs = Descendants<KsTguiFabricatorTab>(window).ToList();
            var narrowed = Descendants<KsTguiLatheRecipeRow>(window).Count();
            Assert.That(narrowed, Is.LessThanOrEqualTo(titles.Count));
            Assert.That(tabs[1].Selected, Is.True);
            Press(tabs[0]);
        });

        // Queue a design, then x5, through the real BUI.
        await Client.WaitPost(() =>
        {
            var first = Descendants<KsTguiLatheRecipeRow>(window).First(row => row.CanBuild(1));
            Press(Title(first));
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
            var row = Descendants<KsTguiLatheRecipeRow>(window).First(row => row.CanBuild(5));
            Press(Descendants<KsTguiButton>(row).First(button => button.Text == "×5"));
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
    }

    /// <summary>
    ///     On a lathe with nothing queued, so that printing can't account for the material going.
    /// </summary>
    [Test]
    public async Task MaterialEjectTakesOutOneSheet()
    {
        await OverrideCVar(Side.Client, KsCCVars.TguiStyle, true);
        await Pair.RunTicksSync(2);

        var lathe = await OpenLathe();

        var steelBefore = 0;
        var glassBefore = 0;
        SharedMaterialStorageSystem materials = null!;
        await Server.WaitPost(() =>
        {
            materials = SEntMan.System<SharedMaterialStorageSystem>();
            steelBefore = materials.GetMaterialAmount(lathe, SteelMaterial);
            glassBefore = materials.GetMaterialAmount(lathe, GlassMaterial);
        });

        await Client.WaitPost(() =>
        {
            var window = Client.ResolveDependency<IUserInterfaceManager>().WindowRoot.Children.OfType<KsTguiLatheWindow>().Single();
            var eject = Descendants<KsTguiMaterialList>(window).Single();
            Press(Descendants<KsTguiButton>(eject).First(button => button.Text == "×1"));
        });
        await Pair.RunTicksSync(10);

        await Server.WaitAssertion(() =>
        {
            var steelTaken = steelBefore - materials.GetMaterialAmount(lathe, SteelMaterial);
            var glassTaken = glassBefore - materials.GetMaterialAmount(lathe, GlassMaterial);
            var steelSheet = materials.GetSheetVolume(SProtoMan.Index(SteelMaterial));
            var glassSheet = materials.GetSheetVolume(SProtoMan.Index(GlassMaterial));

            Assert.That(
                steelTaken == steelSheet && glassTaken == 0 || steelTaken == 0 && glassTaken == glassSheet,
                Is.True,
                $"One sheet of one material should go; {steelTaken} steel and {glassTaken} glass went.");
        });
    }

    /// <summary>
    ///     tg's fabricator: hovering a design, or its x5 or x10, shows what that many prints take, one material sprite with its
    ///         sheets under it each, coloured by whether the machine has enough.
    /// </summary>
    [Test]
    public async Task HoveringADesignShowsWhatItCosts()
    {
        await OverrideCVar(Side.Client, KsCCVars.TguiStyle, true);
        await Pair.RunTicksSync(2);

        await OpenLathe();

        await Client.WaitPost(() =>
        {
            var window = Client.ResolveDependency<IUserInterfaceManager>().WindowRoot.Children.OfType<KsTguiLatheWindow>().Single();
            var row = Descendants<KsTguiLatheRecipeRow>(window).First(row => row.CanBuild(10));

            var once = CostNumbers(Title(row));
            var ten = CostNumbers(Descendants<KsTguiButton>(row).First(button => button.Text == "×10"));

            Assert.That(once, Is.Not.Empty, "A design's tooltip should list its materials.");
            Assert.That(ten, Has.Count.EqualTo(once.Count));
            for (var i = 0; i < once.Count; i++)
                Assert.That(ten[i], Is.EqualTo(once[i] * 10f).Within(once[i] * 10f * 0.01f), "x10's tooltip should cost ten prints.");
        });
    }

    private static List<float> CostNumbers(Control button)
    {
        var tooltip = button.TooltipSupplier!.Invoke(button);
        Assert.That(tooltip, Is.Not.Null);

        var sequence = Descendants<KsTguiMaterialCostSequence>(tooltip!).Single();
        Assert.That(Descendants<TextureRect>(sequence).Count(), Is.EqualTo(Descendants<Label>(sequence).Count()), "Each cost has its sprite.");
        return Descendants<Label>(sequence).Select(label => float.Parse(label.Text!, System.Globalization.CultureInfo.InvariantCulture)).ToList();
    }

    [Test]
    public void CostColoursFollowWhatTheMachineHas()
    {
        Assert.Multiple(() =>
        {
            Assert.That(KsTguiMaterialCostSequence.AmountColor(100, 200), Is.EqualTo(KsTguiMaterialCostSequence.Plenty));
            Assert.That(KsTguiMaterialCostSequence.AmountColor(100, 199), Is.EqualTo(KsTguiMaterialCostSequence.OnlyOnce));
            Assert.That(KsTguiMaterialCostSequence.AmountColor(100, 100), Is.EqualTo(KsTguiMaterialCostSequence.OnlyOnce));
            Assert.That(KsTguiMaterialCostSequence.AmountColor(100, 99), Is.EqualTo(KsTguiMaterialCostSequence.NotEnough));
        });
    }

    /// <summary>
    ///     tg's onClick is <c>canPrint &amp;&amp; act(...)</c>: a design the lathe can't make stays hoverable, greyed out, and a
    ///         click on it does nothing.
    /// </summary>
    [Test]
    public async Task ADesignThatCantBeMadeQueuesNothing()
    {
        await OverrideCVar(Side.Client, KsCCVars.TguiStyle, true);
        await Pair.RunTicksSync(2);

        var lathe = await OpenLathe();

        await Client.WaitPost(() =>
        {
            var window = Client.ResolveDependency<IUserInterfaceManager>().WindowRoot.Children.OfType<KsTguiLatheWindow>().Single();
            var row = Descendants<KsTguiLatheRecipeRow>(window).First(row => !row.CanBuild(1));
            var title = Title(row);

            Assert.That(title.Disabled, Is.False, "It should still hover.");
            Assert.That(title.Modulate.A, Is.LessThan(1f), "It should be greyed out.");
            Assert.That(Descendants<KsTguiButton>(row).First(button => button.Text == "×5").Strikethrough, Is.True, "Its amounts should be struck through.");

            // The server would refuse it anyway, so what matters is that nothing is sent.
            var sent = 0;
            window.RecipeQueueAction += (_, _) => sent++;
            Press(title);
            Press(Descendants<KsTguiButton>(row).First(button => button.Text == "×5"));
            Assert.That(sent, Is.Zero, "A click on a design that can't be made should send nothing.");
        });
        await Pair.RunTicksSync(10);

        await Server.WaitAssertion(() =>
        {
            var component = SEntMan.GetComponent<LatheComponent>(lathe);
            Assert.That(QueuedItems(component) + (component.CurrentRecipe != null ? 1 : 0), Is.Zero);
        });
    }

    /// <summary>
    ///     tg's <c>maxmult</c>, worked out from the recipe and what the machine holds, not from the window's own code.
    /// </summary>
    private int ExpectedMax(KsTguiLatheRecipeRow row, EntityUid clientLathe)
    {
        var recipe = CProtoMan.Index(row.RecipeId);
        var component = CEntMan.GetComponent<LatheComponent>(clientLathe);
        var storage = CEntMan.System<SharedMaterialStorageSystem>();

        var most = 50;
        foreach (var (material, perPrint) in recipe.Materials)
        {
            var needed = SharedLatheSystem.AdjustMaterial(perPrint, recipe.ApplyMaterialDiscount, component.MaterialUseMultiplier);
            most = Math.Min(most, storage.GetMaterialAmount(clientLathe, material) / needed);
        }

        return most;
    }

    private async Task SetSteel(EntityUid lathe, int amount)
    {
        await Server.WaitPost(() =>
        {
            var materials = SEntMan.System<SharedMaterialStorageSystem>();
            var current = materials.GetMaterialAmount(lathe, SteelMaterial);
            Assert.That(materials.TryChangeMaterialAmount(lathe, SteelMaterial, amount - current), Is.True);
        });
        await Pair.RunTicksSync(10);
    }

    /// <summary>
    ///     tg's <c>[Max: N]</c>: N is how many the materials allow, at most 50, whatever the lathe holds.
    /// </summary>
    [Test]
    public async Task MaxButtonCountsWhatTheMaterialsAllow()
    {
        await OverrideCVar(Side.Client, KsCCVars.TguiStyle, true);
        await Pair.RunTicksSync(2);

        var lathe = await OpenLathe();

        async Task Check(string situation, Action<List<KsTguiLatheRecipeRow>> extra)
        {
            await Client.WaitPost(() =>
            {
                var window = Client.ResolveDependency<IUserInterfaceManager>().WindowRoot.Children.OfType<KsTguiLatheWindow>().Single();
                window.PopulateRecipes();
                var rows = Descendants<KsTguiLatheRecipeRow>(window).ToList();
                Assert.That(rows, Is.Not.Empty);

                foreach (var row in rows)
                {
                    Assert.That(row.MaxAmount, Is.EqualTo(ExpectedMax(row, window.Entity)), $"{situation}: {row.RecipeId}");
                    var label = Descendants<KsTguiInputButton>(row).Single().Text;
                    Assert.That(label, Does.Contain(row.MaxAmount.ToString()), "The button should read the number.");
                }

                extra(rows);
            });
        }

        // Plenty: the cap, not the stock, is the limit.
        await Check("plenty", rows => Assert.That(rows.Any(row => row.MaxAmount == 50), Is.True, "A cheap design should hit the cap of 50."));

        // A little: some designs fall short of the cap, and the number follows the stock.
        await SetSteel(lathe, 1000);
        await Check("a little", rows =>
        {
            Assert.That(rows.Any(row => row.MaxAmount is > 0 and < 50), Is.True, "1000 steel should limit some design below the cap.");
        });

        // None: whatever needs steel can't be made.
        await SetSteel(lathe, 0);
        await Check("none", rows => Assert.That(rows.Any(row => row.MaxAmount == 0), Is.True, "Designs that need steel should read 0."));
    }

    /// <summary>
    ///     A number committed in <c>[Max: N]</c> queues that many; with N at 0 nothing is sent.
    /// </summary>
    [Test]
    public async Task CommittingAMaxAmountQueuesThatMany()
    {
        await OverrideCVar(Side.Client, KsCCVars.TguiStyle, true);
        await Pair.RunTicksSync(2);

        var lathe = await OpenLathe();

        var sent = new List<(string Recipe, int Amount)>();
        await Client.WaitPost(() =>
        {
            var window = Client.ResolveDependency<IUserInterfaceManager>().WindowRoot.Children.OfType<KsTguiLatheWindow>().Single();
            window.RecipeQueueAction += (recipe, amount) => sent.Add((recipe, amount));

            var row = Descendants<KsTguiLatheRecipeRow>(window).First(row => row.MaxAmount >= 7);
            var input = Descendants<KsTguiInputButton>(row).Single();
            input.BeginEdit();
            input.SetEditText("7");
            input.Finish(cancel: false);

            Assert.That(sent, Is.EqualTo(new[] { (row.RecipeId.Id, 7) }), "Seven should be queued.");

            // Typing the same number again is another commit.
            input.BeginEdit();
            input.SetEditText("7");
            input.Finish(cancel: false);
            Assert.That(sent, Has.Count.EqualTo(2));
            sent.Clear();
        });
        await Pair.RunTicksSync(10);

        await Server.WaitAssertion(() =>
        {
            var component = SEntMan.GetComponent<LatheComponent>(lathe);
            Assert.That(QueuedItems(component) + (component.CurrentRecipe != null ? 1 : 0), Is.GreaterThanOrEqualTo(7), "The server should hold the queued prints.");
        });

        // Nothing left to print with: N is 0 and a commit does nothing.
        await SetSteel(lathe, 0);
        await Client.WaitPost(() =>
        {
            var window = Client.ResolveDependency<IUserInterfaceManager>().WindowRoot.Children.OfType<KsTguiLatheWindow>().Single();
            window.PopulateRecipes();
            var row = Descendants<KsTguiLatheRecipeRow>(window).First(row => row.MaxAmount == 0);
            var input = Descendants<KsTguiInputButton>(row).Single();
            input.BeginEdit();
            input.SetEditText("7");
            input.Finish(cancel: false);
            Assert.That(sent, Is.Empty, "[Max: 0] should send nothing.");
        });
    }

    [Test]
    public async Task CategoriesShowTheirCountsAndTheSelectedOne()
    {
        await OverrideCVar(Side.Client, KsCCVars.TguiStyle, true);
        await Pair.RunTicksSync(2);

        await OpenLathe();

        await Client.WaitPost(() =>
        {
            var window = Client.ResolveDependency<IUserInterfaceManager>().WindowRoot.Children.OfType<KsTguiLatheWindow>().Single();
            var tabs = Descendants<KsTguiFabricatorTab>(window).ToList();
            var rows = Descendants<KsTguiLatheRecipeRow>(window).Count();

            Assert.That(tabs[0].Selected, Is.True);
            Assert.That(tabs.Skip(1).All(tab => !tab.Selected));
            Assert.That(Descendants<Label>(tabs[0]).Last().Text, Is.EqualTo($"({rows})"), "All Designs counts every design, in brackets as tg's.");

            Press(tabs[1]);
            Assert.That(tabs[1].Selected && !tabs[0].Selected, Is.True);
        });
    }
}
