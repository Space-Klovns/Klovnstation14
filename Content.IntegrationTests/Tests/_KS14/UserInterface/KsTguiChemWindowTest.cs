#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Threading.Tasks;
using Content.Client._KS14.Tgui.Controls;
using Content.Client._KS14.Tgui.Windows.Chem;
using Content.Client.Chemistry.UI;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Shared._KS14.CCVar;
using Content.Shared.Chemistry;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Content.Shared.Storage;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.UserInterface;

/// <summary>
///     The reagent dispenser's and the ChemMaster's BUIs open the tgui window under the tgui theme and the stock one
///         otherwise, and each tgui window's controls send what the stock window's do.
/// </summary>
[TestOf(typeof(KsTguiReagentDispenserWindow))]
public sealed class KsTguiChemWindowTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    private static readonly EntProtoId Dispenser = "ChemDispenser";
    private static readonly EntProtoId ChemMaster = "ChemMaster";
    private static readonly EntProtoId Human = "MobHuman";
    private static readonly ProtoId<ReagentPrototype> WaterReagent = "Water";

    [TestCase(true)]
    [TestCase(false)]
    public async Task TheBuisOpenTheThemesWindows(bool tgui)
    {
        await OverrideCVar(Side.Client, KsCCVars.TguiStyle, tgui);
        await Pair.RunTicksSync(2);

        var testMap = await Pair.CreateTestMap();
        EntityUid dispenser = default;
        EntityUid chemMaster = default;
        EntityUid user = default;
        await Server.WaitPost(() =>
        {
            dispenser = SEntMan.SpawnEntity(Dispenser, new EntityCoordinates(testMap.Grid, new Vector2(0.5f, 0.5f)));
            chemMaster = SEntMan.SpawnEntity(ChemMaster, new EntityCoordinates(testMap.Grid, new Vector2(2.5f, 0.5f)));
            user = SEntMan.SpawnEntity(Human, new EntityCoordinates(testMap.Grid, new Vector2(1.5f, 1.5f)));
            Server.PlayerMan.SetAttachedEntity(ServerSession!, user);
            var uiSystem = SEntMan.System<SharedUserInterfaceSystem>();
            uiSystem.OpenUi(dispenser, ReagentDispenserUiKey.Key, user);
            uiSystem.OpenUi(chemMaster, ChemMasterUiKey.Key, user);
        });
        await Pair.RunTicksSync(10);

        await Client.WaitAssertion(() =>
        {
            var windows = Client.ResolveDependency<IUserInterfaceManager>().WindowRoot.Children.ToList();
            Assert.That(windows.OfType<KsTguiReagentDispenserWindow>().Any(), Is.EqualTo(tgui));
            Assert.That(windows.OfType<ReagentDispenserWindow>().Any(), Is.EqualTo(!tgui));
            Assert.That(windows.OfType<KsTguiChemMasterWindow>().Any(), Is.EqualTo(tgui));
            Assert.That(windows.OfType<ChemMasterWindow>().Any(), Is.EqualTo(!tgui));
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
    public async Task EveryDispenserActionSendsItsMessage()
    {
        KsTguiReagentDispenserWindow window = null!;
        var log = new List<string>();
        var jug = new ItemStorageLocation(Angle.Zero, new Vector2i(1, 2));

        await Client.WaitPost(() =>
        {
            window = new KsTguiReagentDispenserWindow();
            window.OnAmountPressed += amount => log.Add($"amount {(int)amount}");
            window.OnDispenseReagentButtonPressed += location => log.Add($"dispense {location.Position}");
            window.OnEjectJugButtonPressed += location => log.Add($"jug {location.Position}");
            window.OnEjectPressed += message => log.Add($"eject {message.TryEject} {message.TryInsert}");
            window.OnClearPressed += () => log.Add("clear");
            window.OnToggleValveButtonPressed += () => log.Add("valve");

            var inventory = new List<ReagentInventoryItem>
            {
                new(jug, "Water", FixedPoint2.New(50), Color.Blue),
                new(new ItemStorageLocation(Angle.Zero, new Vector2i(3, 2)), "Empty", FixedPoint2.Zero, Color.Red),
            };
            var container = new ContainerInfo("beaker", FixedPoint2.New(20), FixedPoint2.New(100))
            {
                Reagents = [new ReagentQuantity(WaterReagent.Id, FixedPoint2.New(20))],
            };
            window.UpdateState(new ReagentDispenserBoundUserInterfaceState(
                container, null, inventory, ReagentDispenserDispenseAmount.U10, valveOpen: true));
        });

        await Client.WaitPost(() =>
        {
            Assert.That(window.AmountButtons, Has.Count.EqualTo(9));
            Assert.That(window.AmountButtons[ReagentDispenserDispenseAmount.U10].Selected, Is.True);
            Assert.That(window.AmountButtons[ReagentDispenserDispenseAmount.U5].Selected, Is.False);
            Assert.That(window.EjectButton.Filled, Is.True);

            foreach (var (amount, button) in window.AmountButtons)
            {
                Press(button);
                Assert.That(log.Last(), Is.EqualTo($"amount {(int)amount}"));
            }

            var cardButtons = Descendants<KsTguiButton>(window.ReagentGrid).ToList();
            Assert.That(cardButtons, Has.Count.EqualTo(4), "A dispense and a jug eject button per reagent.");

            // Sorted by label: Empty (sold out) first, then Water.
            Assert.That(cardButtons[0].Disabled, Is.True);
            Assert.That(cardButtons[2].Disabled, Is.False);

            log.Clear();
            Press(cardButtons[2]);
            Press(cardButtons[3]);
            Press(window.EjectButton);
            Press(window.ClearButton);
            Press(window.ValveButton);
            Assert.That(log, Is.EqualTo(new[]
            {
                $"dispense {jug.Position}", $"jug {jug.Position}", "eject True False", "clear", "valve",
            }));

            window.UpdateState(new ReagentDispenserBoundUserInterfaceState(
                null, null, new List<ReagentInventoryItem>(), ReagentDispenserDispenseAmount.U1, valveOpen: false));
            Assert.That(window.EjectButton.Filled, Is.False, "An empty slot offers Insert instead of a disabled Eject.");
            Assert.That(window.ClearButton.Disabled, Is.True);
            window.Dispose();
        });
    }

    [Test]
    public async Task EveryChemMasterActionSendsItsMessage()
    {
        KsTguiChemMasterWindow window = null!;
        var log = new List<string>();
        var water = new ReagentId(WaterReagent.Id, null);

        await Client.WaitPost(() =>
        {
            window = new KsTguiChemMasterWindow();
            window.OnInputEjectPressed += message => log.Add($"input eject {message.SlotId}");
            window.OnOutputEjectPressed += message => log.Add($"output eject {message.SlotId}");
            window.OnToggleValveButtonPressed += () => log.Add("valve");
            window.OnModePressed += mode => log.Add($"mode {mode}");
            window.OnSortPressed += () => log.Add("sort");
            window.OnDrawSourcePressed += source => log.Add($"source {source}");
            window.OnPillTypePressed += type => log.Add($"pill type {type}");
            window.OnCreatePillsPressed += (dosage, number, label) => log.Add($"pills {dosage} {number} {label}");
            window.OnCreateBottlePressed += (dosage, label) => log.Add($"bottle {dosage} {label}");
            window.OnReagentButtonPressed += (id, amount, isBuffer) => log.Add($"reagent {id.Prototype} {amount} {isBuffer}");

            var input = new ContainerInfo("beaker", FixedPoint2.New(30), FixedPoint2.New(100))
            {
                Reagents = [new ReagentQuantity(WaterReagent.Id, FixedPoint2.New(30))],
            };
            var output = new ContainerInfo("bottle", FixedPoint2.Zero, FixedPoint2.New(30))
            {
                Reagents = [],
            };
            window.UpdateState(new ChemMasterBoundUserInterfaceState(
                ChemMasterMode.Transfer,
                ChemMasterSortingType.Alphabetical,
                input,
                output,
                [new ReagentQuantity(WaterReagent.Id, FixedPoint2.New(10))],
                FixedPoint2.New(10),
                selectedPillType: 2,
                pillDosageLimit: 50,
                updateLabel: true,
                ChemMasterDrawSource.Internal,
                valveOpen: false));
        });

        await Client.WaitPost(() =>
        {
            Assert.That(window.BufferTransferButton.Selected, Is.True);
            Assert.That(window.BufferDiscardButton.Selected, Is.False);
            Assert.That(window.OutputBufferDraw.Selected, Is.True);
            Assert.That(window.PillTypeButtons, Has.Length.EqualTo((int)SharedChemMaster.PillTypes));
            Assert.That(window.PillTypeButtons[2].Selected, Is.True);
            Assert.That(window.LabelLine, Is.Not.Empty, "The label follows the reagent.");

            Press(window.InputEjectButton);
            Press(window.OutputEjectButton);
            Press(window.ValveButton);
            Press(window.BufferTransferButton);
            Press(window.BufferDiscardButton);
            Press(window.SortButton);
            Press(window.OutputBufferDraw);
            Press(window.OutputBeakerDraw);
            Press(window.PillTypeButtons[5]);
            Press(window.CreatePillButton);
            Press(window.CreateBottleButton);

            Assert.That(log, Is.EqualTo(new[]
            {
                $"input eject {SharedChemMaster.InputSlotName}", $"output eject {SharedChemMaster.OutputSlotName}", "valve", $"mode {ChemMasterMode.Transfer}", $"mode {ChemMasterMode.Discard}",
                "sort", $"source {ChemMasterDrawSource.Internal}", $"source {ChemMasterDrawSource.External}", "pill type 5",
                $"pills {(uint)window.PillDosage.Value} {(uint)window.PillNumber.Value} {window.LabelLine}",
                $"bottle {(uint)window.BottleDosage.Value} {window.LabelLine}",
            }));

            // One input row and one buffer row, ten amounts each.
            var amounts = Descendants<KsTguiButton>(window)
                .Where(button => button.Compact)
                .ToList();
            Assert.That(amounts, Has.Count.EqualTo(20));

            log.Clear();
            foreach (var button in amounts)
            {
                Press(button);
            }

            Assert.That(log, Has.Count.EqualTo(20));
            Assert.That(log.Take(10).All(entry => entry.EndsWith("False")), Is.True, "The input container's rows come from the beaker.");
            Assert.That(log.Skip(10).All(entry => entry.EndsWith("True")), Is.True, "The buffer's rows come from the buffer.");
            Assert.That(log[0], Is.EqualTo($"reagent {water.Prototype} {ChemMasterReagentAmount.U1} False"));
            Assert.That(log[9], Does.Contain(ChemMasterReagentAmount.All.ToString()));
            window.Dispose();
        });
    }

    /// <summary>
    ///     ChemDispenser.tsx is <c>Window.Content scrollable</c>: the whole content scrolls inside a column that is always
    ///         there, the status and dispense sections are their own height, and only the beaker section fills what is left.
    /// </summary>
    [TestCase(2, false)]
    [TestCase(60, true)]
    public async Task TheWholeContentScrollsAndOnlyTheBeakerFills(int reagents, bool overflows)
    {
        KsTguiReagentDispenserWindow window = null!;
        await Client.WaitPost(() =>
        {
            window = new KsTguiReagentDispenserWindow();
            window.Open();
            var inventory = new List<ReagentInventoryItem>();
            for (var i = 0; i < reagents; i++)
                inventory.Add(new ReagentInventoryItem(new ItemStorageLocation(Angle.Zero, new Vector2i(i, 0)), $"Reagent {i:000}", FixedPoint2.New(50), Color.Blue));

            window.UpdateState(new ReagentDispenserBoundUserInterfaceState(
                null, null, inventory, ReagentDispenserDispenseAmount.U10, valveOpen: false));
        });
        await Pair.RunTicksSync(3);

        await Client.WaitAssertion(() =>
        {
            window.InvalidateMeasure();
            var size = window.SetSize;
            window.Measure(size);
            window.Arrange(UIBox2.FromDimensions(Vector2.Zero, size));

            var scroll = Descendants<KsTguiScrollContainer>(window).First();
            Assert.That(scroll.ReserveVertical, Is.True, "overflow-y: scroll reserves the bar's column.");
            Assert.That(scroll.VerticalBarShown, Is.True);
            Assert.That(scroll.VerticalOverflows, Is.EqualTo(overflows));

            var sections = Descendants<KsTguiSection>(window).ToList();
            var dispense = sections.Single(section => section.Title == Robust.Shared.Localization.Loc.GetString("reagent-dispenser-window-amount-to-dispense-label"));
            var beaker = sections.Single(section => section.Title == Robust.Shared.Localization.Loc.GetString("ks-tgui-chem-beaker"));

            Assert.Multiple(() =>
            {
                // Not squeezed or scrolled inside: the section is as tall as its contents.
                Assert.That(dispense.Size.Y, Is.GreaterThanOrEqualTo(dispense.DesiredSize.Y - 0.5f), "The dispense section is its own height.");
                Assert.That(dispense.VerticalExpand, Is.False, "tg's Dispense section does not fill.");
                Assert.That(beaker.VerticalExpand, Is.True, "tg's Beaker section fills.");
                if (!overflows)
                    Assert.That(beaker.GlobalPosition.Y + beaker.Size.Y, Is.EqualTo(scroll.GlobalPosition.Y + scroll.Size.Y - 6f).Within(1f), "The beaker takes what is left.");
            });

            window.Close();
        });
    }
}
