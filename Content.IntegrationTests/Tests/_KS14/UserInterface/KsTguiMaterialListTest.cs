#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Threading.Tasks;
using Content.Client._KS14.Tgui.Controls;
using Content.Client._KS14.Tgui.Windows.Materials;
using Content.Client.Materials.UI;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server.Power.EntitySystems;
using Content.Shared._KS14.CCVar;
using Content.Shared.Materials;
using Content.Shared.Materials.OreSilo;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.UserInterface;

/// <summary>
///     The material list the lathe and the silo share, and the silo's tgui window: equal cells that no amount can push around,
///         entries kept across changes, and the BUI choosing the window.
/// </summary>
[TestOf(typeof(KsTguiMaterialList))]
public sealed class KsTguiMaterialListTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    private static readonly EntProtoId SiloProto = "MachineMaterialSilo";
    private static readonly EntProtoId LatheProto = "Autolathe";
    private static readonly EntProtoId Human = "MobHuman";
    private static readonly ProtoId<MaterialPrototype> GlassMaterial = "Glass";
    private static readonly ProtoId<MaterialPrototype> SteelMaterial = "Steel";
    private static readonly ProtoId<MaterialPrototype> PlasticMaterial = "Plastic";

    /// <summary>
    ///     Far wider as text than a cell: a thousand million units is ten million sheets.
    /// </summary>
    private const int Huge = 1_000_000_000;

    private const int Small = 300;

    private EntityUid _silo;
    private EntityUid _lathe;

    private async Task OpenSilo(bool tgui = true, int glass = Small, int steel = Small, bool withLathe = false)
    {
        await OverrideCVar(Side.Client, KsCCVars.TguiStyle, tgui);
        await Pair.RunTicksSync(2);

        var testMap = await Pair.CreateTestMap();
        await Server.WaitPost(() =>
        {
            _silo = SEntMan.SpawnEntity(SiloProto, new EntityCoordinates(testMap.Grid, new Vector2(0.5f, 0.5f)));
            var user = SEntMan.SpawnEntity(Human, new EntityCoordinates(testMap.Grid, new Vector2(0.5f, 1.5f)));
            Server.PlayerMan.SetAttachedEntity(ServerSession!, user);

            // It only lists machines it could reach, and it reaches nothing unpowered.
            SEntMan.System<PowerReceiverSystem>().SetNeedsPower(_silo, false);

            // Before the window opens, which is when the silo works out who it lists.
            if (withLathe)
                _lathe = SEntMan.SpawnEntity(LatheProto, new EntityCoordinates(testMap.Grid, new Vector2(2.5f, 0.5f)));

            var materials = SEntMan.System<SharedMaterialStorageSystem>();
            if (glass > 0)
                Assert.That(materials.TryChangeMaterialAmount(_silo, GlassMaterial, glass), Is.True);
            if (steel > 0)
                Assert.That(materials.TryChangeMaterialAmount(_silo, SteelMaterial, steel), Is.True);
        });

        // Power is worked out on a tick, and the list is made once, as the window opens.
        await Pair.RunTicksSync(5);

        await Server.WaitPost(() =>
        {
            var actor = Server.PlayerMan.GetSessionById(ServerSession!.UserId).AttachedEntity!.Value;
            SEntMan.System<SharedUserInterfaceSystem>().OpenUi(_silo, OreSiloUiKey.Key, actor);
        });
        await Pair.RunTicksSync(15);
    }

    private Task Change(ProtoId<MaterialPrototype> material, int amount)
    {
        return Server.WaitPost(() => Assert.That(SEntMan.System<SharedMaterialStorageSystem>().TryChangeMaterialAmount(_silo, material, amount), Is.True));
    }

    private KsTguiOreSiloWindow Window()
    {
        return Client.ResolveDependency<IUserInterfaceManager>().WindowRoot.Children.OfType<KsTguiOreSiloWindow>().Single();
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

    private static KsTguiMaterialEntry Entry(Control window, ProtoId<MaterialPrototype> material)
    {
        return Descendants<KsTguiMaterialEntry>(window).Single(entry => entry.Material == material);
    }

    private static void Press(BaseButton button)
    {
        var field = typeof(BaseButton).GetField("OnPressed", BindingFlags.Instance | BindingFlags.NonPublic)!;
        ((Action<BaseButton.ButtonEventArgs>?)field.GetValue(button))?.Invoke(new BaseButton.ButtonEventArgs(button, null!));
    }

    /// <summary>
    ///     A number that grows wider used to push the entries after it along. Compared at every step of the climb, and when it
    ///         has settled, in both directions. Where a cell landed is only answered by a real layout.
    /// </summary>
    [Test]
    public async Task NeighboursStayPutWhileAnAmountGrows()
    {
        await OpenSilo();

        // Glass sorts before steel, so each one is the other's neighbour once.
        foreach (var (grower, neighbour) in new[] { (GlassMaterial, SteelMaterial), (SteelMaterial, GlassMaterial) })
        {
            var neighbourBefore = Vector2.Zero;
            var growerBefore = Vector2.Zero;
            await Client.WaitPost(() =>
            {
                neighbourBefore = Entry(Window(), neighbour).GlobalPosition;
                growerBefore = Entry(Window(), grower).GlobalPosition;
            });

            await Change(grower, Huge);

            for (var step = 0; step < 30; step++)
            {
                await Pair.RunTicksSync(5);
                await Client.WaitAssertion(() =>
                {
                    using (Assert.EnterMultipleScope())
                    {
                        Assert.That(Entry(Window(), neighbour).GlobalPosition, Is.EqualTo(neighbourBefore), $"{neighbour} moved when {grower} grew, at step {step}.");
                        Assert.That(Entry(Window(), grower).GlobalPosition, Is.EqualTo(growerBefore), $"{grower}'s own cell moved, at step {step}.");
                    }
                });
            }

            await Client.WaitAssertion(() =>
            {
                var number = Entry(Window(), grower).AmountNumber;
                Assert.That(number.Animating, Is.False, "It should have arrived by now.");
                Assert.That(number.Displayed, Is.GreaterThan(1_000_000f));
            });
        }
    }

    /// <summary>
    ///     Hit-tested at the middle of the cell, over the number and the icon, as the mouse would be.
    /// </summary>
    [Test]
    public async Task HoveringAnEntryShowsTheMaterialsName()
    {
        await OpenSilo();

        await Client.WaitAssertion(() =>
        {
            var uiManager = Client.ResolveDependency<IUserInterfaceManager>();
            var prototypes = Client.ResolveDependency<IPrototypeManager>();

            foreach (var material in new[] { GlassMaterial, SteelMaterial })
            {
                var entry = Entry(Window(), material);
                var name = Robust.Shared.Localization.Loc.GetString(prototypes.Index(material).Name);
                Assert.That(name, Is.Not.Empty);

                var hit = uiManager.MouseGetControl(new ScreenCoordinates(entry.GlobalPixelPosition + entry.PixelSize / 2f, entry.Window!.Id));
                Assert.That(hit, Is.SameAs(entry), $"The mouse over {material} reaches {hit}, which can't show its tooltip.");
                Assert.That(hit!.ToolTip, Does.Contain(name));
                Assert.That(hit.ToolTip, Does.Contain(entry.AmountNumber.Text), "The tooltip should give the amount too.");
            }
        });
    }

    [Test]
    public async Task EntriesAreKeptWhenAnAmountChangesOrAMaterialComesAndGoes()
    {
        await OpenSilo();

        KsTguiMaterialEntry glass = null!;
        KsTguiMaterialEntry steel = null!;
        await Client.WaitPost(() =>
        {
            glass = Entry(Window(), GlassMaterial);
            steel = Entry(Window(), SteelMaterial);
        });

        await Change(GlassMaterial, 4000);
        await Pair.RunTicksSync(15);
        await Client.WaitAssertion(() =>
        {
            Assert.That(Entry(Window(), GlassMaterial), Is.SameAs(glass), "An amount changed, so its control should stay.");
            Assert.That(Entry(Window(), SteelMaterial), Is.SameAs(steel));
            Assert.That(glass.AmountNumber.Value, Is.EqualTo(43f).Within(0.01f));
        });

        // A new material goes in its place by name, and nothing else is rebuilt.
        await Change(PlasticMaterial, 2000);
        await Pair.RunTicksSync(15);
        await Client.WaitAssertion(() =>
        {
            Assert.That(Entry(Window(), GlassMaterial), Is.SameAs(glass));
            Assert.That(Entry(Window(), SteelMaterial), Is.SameAs(steel));

            var names = Descendants<KsTguiMaterialEntry>(Window()).Select(entry => entry.MaterialName).ToList();
            var sorted = names.OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase).ToList();
            Assert.That(names, Is.EqualTo(sorted), "The entries should stay in order of name.");
            Assert.That(names, Has.Count.EqualTo(3));
        });

        // And one that runs out goes alone.
        await Change(PlasticMaterial, -2000);
        await Pair.RunTicksSync(15);
        await Client.WaitAssertion(() =>
        {
            Assert.That(Descendants<KsTguiMaterialEntry>(Window()).Count(), Is.EqualTo(2));
            Assert.That(Entry(Window(), GlassMaterial), Is.SameAs(glass));
            Assert.That(Entry(Window(), SteelMaterial), Is.SameAs(steel));
        });
    }

    /// <summary>
    ///     On the way from 120 to 105, 110.15 is too wide for the cell and loses its first digit to the clip: <c>.10.15 sheets</c>.
    ///         tg's format is three significant figures, which is what keeps a moving number the width of a settled one.
    /// </summary>
    [TestCase(110.15f, "110 sheets")]
    [TestCase(12.34f, "12.3 sheets")]
    [TestCase(1.234f, "1.23 sheets")]
    [TestCase(99999.4f, "99,999 sheets")]
    public async Task AnAmountIsThreeFiguresAndFitsItsCell(float sheets, string expected)
    {
        await OpenSilo();

        await Client.WaitAssertion(() =>
        {
            var entry = Entry(Window(), SteelMaterial);
            var text = entry.AmountNumber.Format!(sheets);
            Assert.That(text, Is.EqualTo(expected));

            var unclipped = new Label { Text = text };
            Window().AddChild(unclipped);
            unclipped.ForceRunStyleUpdate();
            unclipped.Measure(new Vector2(float.PositiveInfinity, float.PositiveInfinity));
            Assert.That(unclipped.DesiredSize.X, Is.LessThanOrEqualTo(entry.Size.X), "The clip would cut into the number.");
        });
    }

    [Test]
    public async Task NothingCountsUpAsTheWindowOpensButALaterMaterialDoes()
    {
        await OpenSilo(glass: 500_000, steel: 500_000);

        await Client.WaitAssertion(() =>
        {
            foreach (var material in new[] { GlassMaterial, SteelMaterial })
            {
                var number = Entry(Window(), material).AmountNumber;
                Assert.That(number.Animating, Is.False, $"{material} was eased on opening.");
                Assert.That(number.Displayed, Is.EqualTo(number.Value));
            }
        });

        await Change(PlasticMaterial, 500_000);
        await Pair.RunTicksSync(3);
        await Client.WaitAssertion(() =>
        {
            var number = Entry(Window(), PlasticMaterial).AmountNumber;
            Assert.That(number.Value, Is.GreaterThan(1000f));
            Assert.That(number.Displayed, Is.LessThan(number.Value), "A material that arrives later should ease in.");
        });
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task TheSilosBuiOpensTheThemesWindow(bool tgui)
    {
        await OpenSilo(tgui);

        await Client.WaitAssertion(() =>
        {
            var windows = Client.ResolveDependency<IUserInterfaceManager>().WindowRoot.Children.ToList();
            Assert.That(windows.OfType<KsTguiOreSiloWindow>().Any(), Is.EqualTo(tgui));
            Assert.That(windows.OfType<OreSiloMenu>().Any(), Is.EqualTo(!tgui));
        });
    }

    [Test]
    public async Task PressingAMachineLinksItToTheSilo()
    {
        await OpenSilo(withLathe: true);

        await Client.WaitPost(() =>
        {
            var buttons = Descendants<KsTguiButton>(Window()).Where(button => !Descendants<KsTguiMaterialEntry>(Window()).Any(entry => Descendants<KsTguiButton>(entry).Contains(button))).ToList();
            Assert.That(buttons, Has.Count.EqualTo(1), "The machine in reach should be listed once. ");
            Press(buttons[0]);
        });
        await Pair.RunTicksSync(15);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.GetComponent<OreSiloClientComponent>(_lathe).Silo, Is.EqualTo(_silo), "Pressing the machine should have linked it.");
        });
    }
}
