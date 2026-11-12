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
using Content.Shared._KS14.SPAM;
using Content.Shared._KS14.Tgui;
using Content.Shared.Lathe;
using Content.Shared.Materials;
using Content.Client.Lathe;
using Robust.Client.UserInterface;
using Robust.Shared.Maths;
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
    private static readonly EntProtoId HyperAutolatheProto = "AutolatheHyperConvection";
    private static readonly EntProtoId ProtolatheProto = "Protolathe";
    private static readonly EntProtoId Human = "MobHuman";
    private static readonly ProtoId<MaterialPrototype> SteelMaterial = "Steel";
    private static readonly ProtoId<MaterialPrototype> GlassMaterial = "Glass";

    private async Task<EntityUid> OpenLathe(EntProtoId? prototype = null, bool fill = true)
    {
        var testMap = await Pair.CreateTestMap();
        EntityUid lathe = default;
        EntityUid user = default;
        await Server.WaitPost(() =>
        {
            lathe = SEntMan.SpawnEntity(prototype ?? LatheProto, new EntityCoordinates(testMap.Grid, new Vector2(0.5f, 0.5f)));
            user = SEntMan.SpawnEntity(Human, new EntityCoordinates(testMap.Grid, new Vector2(0.5f, 1.5f)));
            Server.PlayerMan.SetAttachedEntity(ServerSession!, user);

            var materials = SEntMan.System<SharedMaterialStorageSystem>();
            // An ore processor takes only ore.
            if (fill)
            {
                Assert.That(materials.TryChangeMaterialAmount(lathe, SteelMaterial, 100000), Is.True, "The lathe should take steel.");
                Assert.That(materials.TryChangeMaterialAmount(lathe, GlassMaterial, 100000), Is.True, "The lathe should take glass.");
            }

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

        // The Fabricator layout: the Autolathe's queue is not there at all while it is empty.
        await OpenLathe(ProtolatheProto);

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

        var lathe = await OpenLathe(ProtolatheProto);

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

    private KsTguiLatheWindow Window()
    {
        return Client.ResolveDependency<IUserInterfaceManager>().WindowRoot.Children.OfType<KsTguiLatheWindow>().Single();
    }

    private async Task UseTgui()
    {
        await OverrideCVar(Side.Client, KsCCVars.TguiStyle, true);
        await Pair.RunTicksSync(2);
    }

    /// <summary>
    ///     Sets what the lathe holds and its limit exactly, whatever it held.
    /// </summary>
    private async Task SetStorage(EntityUid lathe, int? limit, params (ProtoId<MaterialPrototype> Material, int Volume)[] stored)
    {
        await Server.WaitPost(() =>
        {
            var materials = SEntMan.System<SharedMaterialStorageSystem>();
            var component = SEntMan.GetComponent<MaterialStorageComponent>(lathe);
            component.StorageLimit = null;

            foreach (var material in new[] { SteelMaterial, GlassMaterial, PlasticMaterial })
            {
                var wanted = stored.Where(entry => entry.Material == material).Select(entry => entry.Volume).FirstOrDefault();
                var delta = wanted - materials.GetMaterialAmount(lathe, material);
                if (delta != 0)
                    Assert.That(materials.TryChangeMaterialAmount(lathe, material, delta), Is.True);
            }

            component.StorageLimit = limit;
            SEntMan.Dirty(lathe, component);
        });
        await Pair.RunTicksSync(15);
    }

    private static readonly ProtoId<MaterialPrototype> PlasticMaterial = "Plastic";

    [TestCase("Autolathe", true)]
    [TestCase("AutolatheHyperConvection", true)]
    [TestCase("Protolathe", false)]
    public async Task OnlyAutolathesGetTheTotalMaterialsLayout(string prototype, bool autolathe)
    {
        await UseTgui();
        await OpenLathe(prototype);

        await Client.WaitAssertion(() =>
        {
            var window = Window();
            Assert.That(window.IsAutolatheLayout, Is.EqualTo(autolathe));
            Assert.That(Descendants<KsTguiLatheMaterialSummary>(window).Any(), Is.EqualTo(autolathe), "Total Materials is the Autolathe's.");
            Assert.That(Descendants<KsTguiMaterialList>(window).Any(), Is.EqualTo(!autolathe), "The material bar is the Fabricator's.");
            if (autolathe)
                Assert.That(window.SetSize.X, Is.EqualTo(670f), "tg's Autolathe window is 670 wide.");
        });
    }

    /// <summary>
    ///     tg: value materialtotal over materialsmax, good from 85%, average from 25%, bad below, text in sheets.
    /// </summary>
    [TestCase(9000, "Good", "90/100 sheets")]
    [TestCase(8500, "Good", "85/100 sheets")]
    [TestCase(5000, "Average", "50/100 sheets")]
    [TestCase(2500, "Average", "25/100 sheets")]
    [TestCase(1000, "Bad", "10/100 sheets")]
    public async Task TotalBarShowsHowFullTheStorageIs(int volume, string range, string text)
    {
        await UseTgui();
        var lathe = await OpenLathe();
        await SetStorage(lathe, 10000, (SteelMaterial, volume));

        await Client.WaitAssertion(() =>
        {
            var bar = Descendants<KsTguiLatheMaterialSummary>(Window()).Single().TotalBar;
            Assert.That(bar.Value, Is.EqualTo((float)volume));
            Assert.That(bar.MaxValue, Is.EqualTo(10000f));
            Assert.That(bar.ShownText, Is.EqualTo(text));
            Assert.That(bar.ResolvedColor, Is.EqualTo(Enum.Parse<KsTguiColor>(range)));
        });
    }

    /// <summary>
    ///     The autolathe has no limit of its own, so there is no maximum to be a fraction of.
    /// </summary>
    [Test]
    public async Task WithoutALimitTheTotalBarIsFullAndHasNoRange()
    {
        await UseTgui();
        var lathe = await OpenLathe();
        await SetStorage(lathe, null, (SteelMaterial, 1000), (GlassMaterial, 250));

        await Client.WaitAssertion(() =>
        {
            var bar = Descendants<KsTguiLatheMaterialSummary>(Window()).Single().TotalBar;
            Assert.That(bar.Value, Is.EqualTo(1250f));
            Assert.That(bar.MaxValue, Is.EqualTo(1250f));
            Assert.That(bar.ShownText, Is.EqualTo("12.5 sheets"));
            Assert.That(bar.ResolvedColor, Is.EqualTo(KsTguiColor.Default));
        });
    }

    [Test]
    public async Task MaterialsCollapsibleListsStoredMaterialsAndEjectsThem()
    {
        await UseTgui();
        var lathe = await OpenLathe();

        // Nothing stored: tg shows no Collapsible.
        await SetStorage(lathe, 10000);
        await Client.WaitAssertion(() =>
            Assert.That(Descendants<KsTguiLatheMaterialSummary>(Window()).Single().MaterialsCollapsible.Visible, Is.False));

        var steelSheet = 0;
        var glassSheet = 0;
        await Server.WaitPost(() =>
        {
            var materials = SEntMan.System<SharedMaterialStorageSystem>();
            steelSheet = materials.GetSheetVolume(SProtoMan.Index(SteelMaterial));
            glassSheet = materials.GetSheetVolume(SProtoMan.Index(GlassMaterial));
        });

        // Ten sheets of steel, three of glass, and less than a sheet of plastic.
        var plasticSheet = 100;
        await SetStorage(lathe, 100000, (SteelMaterial, steelSheet * 10), (GlassMaterial, glassSheet * 3), (PlasticMaterial, plasticSheet / 2));

        await Client.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                var summary = Descendants<KsTguiLatheMaterialSummary>(Window()).Single();
                Assert.That(summary.MaterialsCollapsible.Visible, Is.True);
                Assert.That(summary.Rows.Keys, Is.EquivalentTo(new[] { SteelMaterial, GlassMaterial, PlasticMaterial }));

                // Rows are labelled with the capitalised name, as tg's capitalize.
                foreach (var row in summary.Rows.Values)
                {
                    var name = Robust.Shared.Localization.Loc.GetString(CProtoMan.Index(row.Material).Name);
                    Assert.That(row.Item.Label, Is.EqualTo(char.ToUpperInvariant(name[0]) + name[1..]));
                    Assert.That(row.Bar.Mirrored, Is.True);
                    Assert.That(row.Bar.BackgroundColor, Is.EqualTo(CProtoMan.Index(row.Material).Color), "The track is the material's colour.");
                    Assert.That(row.Bar.Color, Is.EqualTo(KsTguiColor.Black));
                }

                var steel = summary.Rows[SteelMaterial];
                Assert.That(steel.EjectButtons.Select(button => button.Sheets), Is.EqualTo(new[] { 1, 5, 10 }), "x1, x5 and all, sorted.");
                Assert.That(steel.EjectButtons.All(button => !button.Button.Disabled), Is.True);
                Assert.That(steel.EjectButtons.Select(button => button.Button.Text), Is.EqualTo(new[] { "x1", "x5", "x10" }));
                Assert.That(steel.Bar.Value, Is.EqualTo(summary.TotalBar.MaxValue - steelSheet * 10), "The fill is max - amount.");
                Assert.That(steel.Bar.MaxValue, Is.EqualTo(summary.TotalBar.MaxValue));

                var glass = summary.Rows[GlassMaterial];
                Assert.That(glass.EjectButtons.Select(button => button.Sheets), Is.EqualTo(new[] { 1, 3, 5 }), "Three sheets sort between x1 and x5.");
                Assert.That(glass.EjectButtons.Select(button => button.Button.Disabled), Is.EqualTo(new[] { false, false, true }));

                var plastic = summary.Rows[PlasticMaterial];
                Assert.That(plastic.EjectButtons.Select(button => button.Sheets), Is.EqualTo(new[] { 1, 5 }), "No x0 under a sheet.");
                Assert.That(plastic.EjectButtons.All(button => button.Button.Disabled), Is.True, "Not even one sheet.");
            });
        });

        // Eject x5 of the steel: the same message the Fabricator's entries send.
        var steelBefore = 0;
        await Server.WaitPost(() => steelBefore = SEntMan.System<SharedMaterialStorageSystem>().GetMaterialAmount(lathe, SteelMaterial));
        await Client.WaitPost(() =>
        {
            var steel = Descendants<KsTguiLatheMaterialSummary>(Window()).Single().Rows[SteelMaterial];
            Press(steel.EjectButtons.Single(button => button.Sheets == 5).Button);
        });
        await Pair.RunTicksSync(15);

        await Server.WaitAssertion(() =>
        {
            var steelAfter = SEntMan.System<SharedMaterialStorageSystem>().GetMaterialAmount(lathe, SteelMaterial);
            Assert.That(steelBefore - steelAfter, Is.EqualTo(steelSheet * 5), "Five sheets should leave the lathe.");
        });
    }

    [Test]
    public async Task SearchingTitlesTheResultsAndListsEveryCategoryByName()
    {
        await UseTgui();
        await OpenLathe();

        await Client.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                var window = Window();
                var all = Robust.Shared.Localization.Loc.GetString("ks-tgui-lathe-all-designs");
                Assert.That(window.DesignsTitle, Is.EqualTo(all));

                var searchBar = Descendants<KsTguiSearchBar>(window).Single();
                Assert.That(searchBar.PlaceHolder, Is.EqualTo(Robust.Shared.Localization.Loc.GetString("ks-tgui-lathe-search")));
                Assert.That(searchBar.PlaceHolder, Is.EqualTo("Search all designs..."));

                // The search is in a Section of its own inside the titled one.
                var sections = new List<KsTguiSection>();
                for (Control? parent = searchBar.Parent; parent != null; parent = parent.Parent)
                {
                    if (parent is KsTguiSection section)
                        sections.Add(section);
                }
                Assert.That(sections, Has.Count.GreaterThanOrEqualTo(2));
                Assert.That(sections[0].Title, Is.Null, "The search's own section has no title.");
                Assert.That(sections[1].Title, Is.EqualTo(all));

                // A category, then a search that reaches past it.
                var tabs = Descendants<KsTguiFabricatorTab>(window).ToList();
                Press(tabs[1]);
                var categoryRows = Descendants<KsTguiLatheRecipeRow>(window).Select(row => row.RecipeId.Id).ToList();
                Assert.That(window.DesignsTitle, Is.Not.EqualTo(all));
                var categoryTitle = window.DesignsTitle;

                const string query = "e";
                searchBar.Input.SetText(query, invokeEvent: true);
                Assert.That(window.DesignsTitle, Is.EqualTo(Robust.Shared.Localization.Loc.GetString("ks-tgui-lathe-results", ("query", query))));
                Assert.That(window.DesignsTitle, Does.Contain("Results for"));

                var lathe = CEntMan.System<LatheSystem>();
                var expected = window.Recipes
                    .Where(recipe => lathe.GetRecipeName(recipe).ToLowerInvariant().Contains(query))
                    .OrderBy(recipe => lathe.GetRecipeName(recipe))
                    .Select(recipe => recipe.Id)
                    .ToList();
                var shown = Descendants<KsTguiLatheRecipeRow>(window).Select(row => row.RecipeId.Id).ToList();

                Assert.That(shown, Is.EqualTo(expected), "Every matching design, sorted by name.");
                Assert.That(shown.Except(categoryRows), Is.Not.Empty, "The search should reach designs outside the selected category.");

                // Clearing it brings the category back.
                searchBar.Input.SetText(string.Empty, invokeEvent: true);
                Assert.That(window.DesignsTitle, Is.EqualTo(categoryTitle));
                Assert.That(Descendants<KsTguiLatheRecipeRow>(window).Select(row => row.RecipeId.Id), Is.EqualTo(categoryRows));
            });
        });
    }

    [Test]
    public async Task ChangingCategoryClearsTheSearch()
    {
        await UseTgui();
        await OpenLathe();

        await Client.WaitAssertion(() =>
        {
            var window = Window();
            var searchBar = Descendants<KsTguiSearchBar>(window).Single();
            var tabs = Descendants<KsTguiFabricatorTab>(window).ToList();

            searchBar.Input.SetText("e", invokeEvent: true);
            Assert.That(window.DesignsTitle, Does.Contain("Results for"));

            // The category already selected (All) is left alone, as tg's early return.
            Press(tabs[0]);
            Assert.That(searchBar.Input.Text, Is.EqualTo("e"));

            Press(tabs[2]);
            Assert.That(searchBar.Input.Text, Is.Empty, "A new category clears the search.");
            Assert.That(window.DesignsTitle, Does.Not.Contain("Results for"));
            Assert.That(tabs[2].Selected, Is.True);

            // And selecting the same one again, with a search typed, keeps it.
            searchBar.Input.SetText("e", invokeEvent: true);
            Press(tabs[2]);
            Assert.That(searchBar.Input.Text, Is.EqualTo("e"));
        });
    }

    /// <summary>
    ///     tg's <c>multiplier: 1</c>, with no amount box to say otherwise.
    /// </summary>
    [Test]
    public async Task ClickingADesignsTitleQueuesOneAndThereIsNoAmountInput()
    {
        await UseTgui();
        await OpenLathe();

        await Client.WaitPost(() =>
        {
            var window = Window();
            Assert.That(Descendants<KsTguiNumberInput>(window), Is.Empty, "tg has no amount input.");
            Assert.That(Descendants<Label>(window).Any(label => label.Text == Robust.Shared.Localization.Loc.GetString("lathe-menu-amount")), Is.False);

            var sent = new List<(string Recipe, int Amount)>();
            window.RecipeQueueAction += (recipe, amount) => sent.Add((recipe, amount));

            var row = Descendants<KsTguiLatheRecipeRow>(window).First(row => row.CanBuild(1));
            Press(Title(row));
            Assert.That(sent, Is.EqualTo(new[] { (row.RecipeId.Id, 1) }));
        });
    }

    /// <summary>
    ///     The Autolathe layout keeps the queue out of the way while it is empty.
    /// </summary>
    [Test]
    public async Task AutolatheQueueShowsOnlyWhileThereIsSomethingInIt()
    {
        await UseTgui();
        await OpenLathe();

        await Client.WaitAssertion(() => Assert.That(Window().QueueShown, Is.False));

        await Client.WaitPost(() => Window().SetQueueInfo(Window().Recipes.First()));
        await Client.WaitAssertion(() => Assert.That(Window().QueueShown, Is.True, "Something is printing."));

        await Client.WaitPost(() => Window().SetQueueInfo(null));
        await Client.WaitAssertion(() => Assert.That(Window().QueueShown, Is.False));
    }

    private static UIBox2 BoxOf(Control control)
    {
        return UIBox2.FromDimensions(control.GlobalPosition, control.Size);
    }

    private static bool Intersects(UIBox2 first, UIBox2 second)
    {
        const float tolerance = 0.5f;
        return first.Left < second.Right - tolerance && second.Left < first.Right - tolerance
            && first.Top < second.Bottom - tolerance && second.Top < first.Bottom - tolerance;
    }

    private static bool Contains(UIBox2 outer, UIBox2 inner)
    {
        const float tolerance = 0.5f;
        return inner.Left >= outer.Left - tolerance && inner.Top >= outer.Top - tolerance
            && inner.Right <= outer.Right + tolerance && inner.Bottom <= outer.Bottom + tolerance;
    }

    /// <summary>
    ///     The window laid out at the size it opens at, with designs, a queue and (on an autolathe) the materials open: nothing
    ///         leaves the window and the queue sits clear of the designs. The list is the part that gives way.
    ///     <see cref="KsWindowContentsFitTest"/> measures a window as built, with no state in it, so an empty design list and no
    ///         queue never showed this.
    /// </summary>
    [TestCase("Autolathe", true)]
    [TestCase("Autolathe", false)]
    [TestCase("Protolathe", false)]
    public async Task NothingLeavesTheWindowOrOverlaps(string prototype, bool materialsOpen)
    {
        await UseTgui();
        await OpenLathe(prototype);

        await Client.WaitPost(() =>
        {
            var window = Window();
            Assert.That(Descendants<KsTguiLatheRecipeRow>(window), Is.Not.Empty);

            // A queue, and something printing.
            var recipes = window.Recipes;
            window.SetQueueInfo(recipes[0]);
            window.PopulateQueueList(new List<LatheRecipeBatch>
            {
                new(recipes[1], 0, 5), new(recipes[2], 0, 1), new(recipes[3], 0, 2), new(recipes[4], 0, 1),
            });

            foreach (var summary in Descendants<KsTguiLatheMaterialSummary>(window))
                summary.MaterialsCollapsible.Open = materialsOpen;

        });

        // Let the interface lay itself out with the new state, then lay it out again at the opening size.
        await Pair.RunTicksSync(5);

        await Client.WaitAssertion(() =>
        {
            var window = Window();
            window.InvalidateMeasure();
            var size = window.SetSize;
            window.Measure(size);
            window.Arrange(UIBox2.FromDimensions(Vector2.Zero, size));

            var contents = BoxOf(window.ContentsContainer);
            var sections = Descendants<KsTguiSection>(window).Where(section => section.Visible).ToList();
            Assert.That(sections, Is.Not.Empty);

            Assert.Multiple(() =>
            {
                foreach (var section in sections)
                    Assert.That(Contains(contents, BoxOf(section)), Is.True, $"Section '{section.Title}' {BoxOf(section)} is outside the window's contents {contents}.");

                var row = Descendants<KsTguiLatheRecipeRow>(window).First();
                var rowSections = new List<KsTguiSection>();
                for (Control? parent = row.Parent; parent != null; parent = parent.Parent)
                {
                    if (parent is KsTguiSection section)
                        rowSections.Add(section);
                }

                var queue = sections.FirstOrDefault(section => section.Title == Robust.Shared.Localization.Loc.GetString("lathe-menu-queue-title"));
                Assert.That(queue, Is.Not.Null);
                Assert.That(queue!.Size.Y, Is.GreaterThanOrEqualTo(140f), "The queue was squeezed below the room it needs. " + string.Join("; ", sections.Select(section => $"{section.Title}={BoxOf(section)}")));
                foreach (var designs in rowSections)
                    Assert.That(Intersects(BoxOf(queue!), BoxOf(designs)), Is.False, $"The queue {BoxOf(queue!)} overlaps '{designs.Title}' {BoxOf(designs)}.");

                // The list still has room to show something.
                Assert.That(BoxOf(rowSections[0]).Height, Is.GreaterThan(40f), "The design list was squeezed out.");

                // Every section the window has lies clear of its siblings: the Fabricator's bar is one of them.
                var materials = sections.FirstOrDefault(section => section.Title == Robust.Shared.Localization.Loc.GetString("lathe-menu-materials-title"));
                if (!window.IsAutolatheLayout)
                {
                    Assert.That(materials, Is.Not.Null);
                    Assert.That(materials!.Size.Y, Is.GreaterThan(30f), "The material bar was squeezed to nothing.");
                    Assert.That(Intersects(BoxOf(materials), BoxOf(queue!)), Is.False);
                    Assert.That(Intersects(BoxOf(materials), BoxOf(rowSections[^1])), Is.False);
                }
            });
        });
    }

    /// <summary>
    ///     tg's DesignBrowser puts the search and the list in two Sections, each the first child of its own Stack.Item: the
    ///         search's bottom padding (.66em), the Stack's 6px gap and the list's top padding (.66em) less the 6px a first
    ///         nested Section is pulled up come to 15.84px between the search bar and the first design. Both layouts.
    /// </summary>
    [TestCase("Autolathe")]
    [TestCase("Protolathe")]
    public async Task TheListStartsWhereTgsDoesUnderTheSearch(string prototype)
    {
        await UseTgui();
        await OpenLathe(prototype);

        await Client.WaitAssertion(() =>
        {
            var window = Window();
            window.InvalidateMeasure();
            var size = window.SetSize;
            window.Measure(size);
            window.Arrange(UIBox2.FromDimensions(Vector2.Zero, size));

            var searchBar = Descendants<KsTguiSearchBar>(window).Single();
            var row = Descendants<KsTguiLatheRecipeRow>(window).First();
            var gap = BoxOf(row).Top - BoxOf(searchBar).Bottom;
            Assert.That(gap, Is.EqualTo(15.84f).Within(1.1f), $"Search bar {BoxOf(searchBar)}, first design {BoxOf(row)}.");
        });
    }

    /// <summary>
    ///     The right-hand buttons line up in columns whatever N the [Max: N] label holds: it is as wide as [Max: 50] always, and
    ///         its text starts at the same place.
    /// </summary>
    [TestCase("Protolathe")]
    [TestCase("Autolathe")]
    public async Task TheAmountButtonsLineUpOnEveryRow(string prototype)
    {
        await UseTgui();
        await OpenLathe(prototype);

        await Client.WaitAssertion(() =>
        {
            var window = Window();
            var rows = Descendants<KsTguiLatheRecipeRow>(window).Take(4).ToList();
            Assert.That(rows, Has.Count.EqualTo(4));

            var amounts = new[] { 0, 6, 15, 50 };
            for (var i = 0; i < rows.Count; i++)
            {
                var recipe = window.Recipes[i];
                var proto = Client.ProtoMan.Index(recipe);
                rows[i].SetRecipe(proto, _ => [], true, true, true, amounts[i], () => new Control());
            }

            window.InvalidateMeasure();
            var size = window.SetSize;
            window.Measure(size);
            window.Arrange(UIBox2.FromDimensions(Vector2.Zero, size));

            Assert.Multiple(() =>
            {
                foreach (var row in rows.Skip(1))
                {
                    Assert.That(row.FiveButton.GlobalPosition.X, Is.EqualTo(rows[0].FiveButton.GlobalPosition.X).Within(0.01f), "x5");
                    Assert.That(row.TenButton.GlobalPosition.X, Is.EqualTo(rows[0].TenButton.GlobalPosition.X).Within(0.01f), "x10");
                    Assert.That(row.MaxButton.GlobalPosition.X, Is.EqualTo(rows[0].MaxButton.GlobalPosition.X).Within(0.01f), "Max");
                    Assert.That(row.MaxHost.Size.X, Is.EqualTo(rows[0].MaxHost.Size.X).Within(0.01f));
                }
            });
        });
    }

    /// <summary>
    ///     A smelter makes what it makes at once, so in place of the queue its window shows the mining points it holds, and
    ///         keeps them up to date.
    /// </summary>
    [TestCase(true)]
    [TestCase(false)]
    public async Task ASmelterShowsItsPointsWhereTheQueueIs(bool tgui)
    {
        await OverrideCVar(Side.Client, KsCCVars.TguiStyle, tgui);
        await Pair.RunTicksSync(2);

        var processor = await OpenLathe("OreProcessor", fill: false);

        async Task SetPoints(float points)
        {
            await Server.WaitPost(() =>
            {
                var smelterComponent = SEntMan.GetComponent<KsSPAMSmelterComponent>(processor);
                smelterComponent.Unclaimed = points;
                SEntMan.Dirty(processor, smelterComponent);
            });
            await Pair.RunTicksSync(10);
        }

        bool Shows(string text)
        {
            var windows = Client.ResolveDependency<IUserInterfaceManager>().WindowRoot.Children;
            Control window = tgui ? windows.OfType<KsTguiLatheWindow>().Single() : windows.OfType<LatheMenu>().Single();
            return tgui
                ? Descendants<KsTguiLabeledListItem>(window).Any(item => item.Visible && item.Text == text)
                : Descendants<Label>(window).Any(label => label.Visible && label.Text == text);
        }

        string Expected(int points) => tgui
            ? points.ToString()
            : Robust.Shared.Localization.Loc.GetString("ks-spam-smelter-ui-points", ("points", points));

        await SetPoints(42f);
        await Client.WaitAssertion(() =>
        {
            Assert.That(Shows(Expected(42)), Is.True, "The window must show the points the smelter holds.");
            if (tgui)
                Assert.That(Client.ResolveDependency<IUserInterfaceManager>().WindowRoot.Children.OfType<KsTguiLatheWindow>().Single().QueueShown, Is.True,
                    "A smelter's section must show even with nothing queued.");
        });

        await SetPoints(0f);
        await Client.WaitAssertion(() => Assert.That(Shows(Expected(0)), Is.True, "The window must follow the points as they are claimed."));
    }
}
