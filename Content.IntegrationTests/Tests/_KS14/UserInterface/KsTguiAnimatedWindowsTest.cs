#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.Client._KS14.Tgui.Controls;
using Content.Client._KS14.Tgui.Windows.Atmos;
using Content.Client._KS14.Tgui.Windows.Chem;
using Content.Client._KS14.Tgui.Windows.Lathe;
using Content.Client._KS14.Tgui.Windows.Materials;
using Content.Client.Cargo.UI;
using Content.Client.Medical.Cryogenics;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server.NodeContainer.EntitySystems;
using Content.Server.NodeContainer.Nodes;
using Content.Shared._KS14.Atmos;
using Content.Shared._KS14.CCVar;
using Content.Shared.Atmos.Components;
using Content.Shared.Atmos.Piping.Binary.Components;
using Content.Shared.Atmos.Piping.Unary.Components;
using Content.Shared.Chemistry;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.FixedPoint;
using Content.Shared.Lathe;
using Content.Shared.Materials;
using Robust.Client.UserInterface;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.UserInterface;

/// <summary>
///     The numbers that move in tg's windows move in ours, and the gauges and status they read reach the client. A window
///         rebuilds its rows on each state, so the row tests check the number survives that rather than only that one exists.
/// </summary>
[TestOf(typeof(KsTguiGasCanisterWindow))]
public sealed class KsTguiAnimatedWindowsTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    private static readonly EntProtoId PressurePump = "GasPressurePump";
    private static readonly EntProtoId Canister = "AirCanister";
    private static readonly EntProtoId Tank = "OxygenTank";
    private static readonly EntProtoId Thermomachine = "GasThermoMachineFreezer";
    private static readonly EntProtoId Lathe = "Autolathe";
    private static readonly EntProtoId Human = "MobHuman";
    private static readonly ProtoId<MaterialPrototype> SteelMaterial = "Steel";
    private static readonly ProtoId<ReagentPrototype> WaterReagent = "Water";
    private static readonly ProtoId<ReagentPrototype> OxygenReagent = "Oxygen";

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

    private async Task<EntityUid> SpawnUser(EntityCoordinates coordinates)
    {
        EntityUid user = default;
        await Server.WaitPost(() =>
        {
            user = SEntMan.SpawnEntity(Human, coordinates);
            Server.PlayerMan.SetAttachedEntity(ServerSession!, user);
        });
        return user;
    }

    private T OpenWindow<T>() where T : Control
    {
        return Client.ResolveDependency<IUserInterfaceManager>().WindowRoot.Children.OfType<T>().Single();
    }

    [Test]
    public async Task CanisterDialReadsToItsLimitInTgsBands()
    {
        await Client.WaitPost(() =>
        {
            var window = new KsTguiGasCanisterWindow();
            window.SetCanisterLimit(1000f);
            var gauge = window.CanisterGauge;

            Assert.That(gauge.MaxValue, Is.EqualTo(1000f));
            Assert.That(gauge.AlertAfter, Is.EqualTo(700f).Within(0.01f));

            window.SetCanisterPressure(600f);
            Assert.That(gauge.MatchingRange(), Is.EqualTo(KsTguiColor.Good));
            Assert.That(gauge.AlertActive, Is.False);

            window.SetCanisterPressure(800f);
            Assert.That(gauge.MatchingRange(), Is.EqualTo(KsTguiColor.Average));
            Assert.That(gauge.AlertActive, Is.True);

            window.SetCanisterPressure(900f);
            Assert.That(gauge.MatchingRange(), Is.EqualTo(KsTguiColor.Bad));
            window.Dispose();
        });
    }

    [Test]
    public async Task TankDialTurnsAtItsLeakAndFragmentPressures()
    {
        await Client.WaitPost(() =>
        {
            var window = new KsTguiGasCanisterWindow();
            window.SetTankLimits(1000f, 2000f);
            var gauge = window.TankGauge;

            Assert.That(gauge.MaxValue, Is.EqualTo(2300f).Within(0.01f), "tg's dial runs 15% past the pressure that destroys the tank.");
            Assert.That(gauge.AlertAfter, Is.EqualTo(1000f));

            window.SetTankPressure(900f);
            Assert.That(gauge.MatchingRange(), Is.EqualTo(KsTguiColor.Good));
            Assert.That(gauge.AlertActive, Is.False);

            window.SetTankPressure(1500f);
            Assert.That(gauge.MatchingRange(), Is.EqualTo(KsTguiColor.Average));
            Assert.That(gauge.AlertActive, Is.True);

            window.SetTankPressure(2100f);
            Assert.That(gauge.MatchingRange(), Is.EqualTo(KsTguiColor.Bad));
            window.Dispose();
        });
    }

    /// <summary>
    ///     Doubles each limit on the server only. The client has the prototype's values anyway, so without this a limit that
    ///         was never sent would look as if it had arrived.
    /// </summary>
    [Test]
    public async Task CanisterLimitsComeFromTheServer()
    {
        await OverrideCVar(Side.Client, KsCCVars.TguiStyle, true);
        await Pair.RunTicksSync(2);

        var testMap = await Pair.CreateTestMap();
        var user = await SpawnUser(new EntityCoordinates(testMap.Grid, new Vector2(0.5f, 1.5f)));

        var canisterOverpressure = 0f;
        var tankSafety = 0f;
        var tankOverpressure = 0f;
        await Server.WaitPost(() =>
        {
            var canisterUid = SEntMan.SpawnEntity(Canister, new EntityCoordinates(testMap.Grid, new Vector2(0.5f, 0.5f)));
            var tankUid = SEntMan.SpawnEntity(Tank, new EntityCoordinates(testMap.Grid, new Vector2(1.5f, 0.5f)));
            var canisterComponent = SEntMan.GetComponent<GasCanisterComponent>(canisterUid);
            var tankComponent = SEntMan.GetComponent<GasTankComponent>(tankUid);

            canisterComponent.Overpressure *= 2f;
            canisterOverpressure = canisterComponent.Overpressure;
            tankComponent.SafetyPressure *= 2f;
            tankComponent.Overpressure *= 2f;
            tankSafety = tankComponent.SafetyPressure;
            tankOverpressure = tankComponent.Overpressure;
            SEntMan.Dirty(canisterUid, canisterComponent);
            SEntMan.Dirty(tankUid, tankComponent);

            Assert.That(SEntMan.System<ItemSlotsSystem>().TryInsert(canisterUid, canisterComponent.GasTankSlot, tankUid, user: null), Is.True);
            SEntMan.System<SharedUserInterfaceSystem>().OpenUi(canisterUid, GasCanisterUiKey.Key, user);
        });
        await Pair.RunTicksSync(15);

        await Client.WaitAssertion(() =>
        {
            var window = OpenWindow<KsTguiGasCanisterWindow>();
            Assert.That(window.CanisterGauge.MaxValue, Is.EqualTo(canisterOverpressure));
            Assert.That(window.TankGauge.AlertAfter, Is.EqualTo(tankSafety));
            Assert.That(window.TankGauge.MaxValue, Is.EqualTo(tankOverpressure * 1.15f).Within(0.01f));
        });
    }

    [Test]
    public async Task ThermomachineShowsTheGasItIsWorkingOn()
    {
        await OverrideCVar(Side.Client, KsCCVars.TguiStyle, true);
        await Pair.RunTicksSync(2);

        var testMap = await Pair.CreateTestMap();
        var user = await SpawnUser(new EntityCoordinates(testMap.Grid, new Vector2(0.5f, 1.5f)));

        EntityUid machineUid = default;
        SharedUserInterfaceSystem userInterfaceSystem = null!;
        var expectedTemperature = 0f;
        var expectedPressure = 0f;

        void RunUpdate()
        {
            var update = new AtmosDeviceUpdateEvent(0.5f, null, null);
            SEntMan.EventBus.RaiseLocalEvent(machineUid, ref update);
        }

        await Server.WaitPost(() =>
        {
            userInterfaceSystem = SEntMan.System<SharedUserInterfaceSystem>();
            machineUid = SEntMan.SpawnEntity(Thermomachine, new EntityCoordinates(testMap.Grid, new Vector2(0.5f, 0.5f)));

            RunUpdate();
            Assert.That(
                userInterfaceSystem.TryGetUiState<KsGasThermomachineStatusState>(machineUid, ThermomachineUiKey.Key, out _),
                Is.False,
                "Nobody is looking, so nothing should be sent.");

            userInterfaceSystem.OpenUi(machineUid, ThermomachineUiKey.Key, user);
        });
        await Pair.RunTicksSync(10);

        await Server.WaitPost(() =>
        {
            var thermoMachine = SEntMan.GetComponent<GasThermoMachineComponent>(machineUid);
            Assert.That(SEntMan.System<NodeContainerSystem>().TryGetNode(machineUid, thermoMachine.InletName, out PipeNode? inlet), Is.True);

            inlet!.Air.Temperature = 345.5f;
            expectedTemperature = inlet.Air.Temperature;
            expectedPressure = inlet.Air.Pressure;
            RunUpdate();
        });
        await Pair.RunTicksSync(10);

        await Client.WaitAssertion(() =>
        {
            var window = OpenWindow<KsTguiGasThermomachineWindow>();
            Assert.That(expectedTemperature, Is.GreaterThan(0f), "The test should have a real number to look for.");
            Assert.That(window.GasTemperature.Value, Is.EqualTo(expectedTemperature));
            Assert.That(window.GasPressure.Value, Is.EqualTo(expectedPressure));
        });
    }

    [Test]
    public async Task ThermomachineStatusEasesAndKeepsTwoDecimals()
    {
        await Client.WaitPost(() =>
        {
            var window = new KsTguiGasThermomachineWindow();
            window.SetGasStatus(293.15f, 101.325f);
            Assert.That(window.GasTemperature.Text, Is.EqualTo((293.15f).ToString("F2") + " K"));

            window.SetGasStatus(300f, 110f);
            Assert.That(window.GasTemperature.Animating, Is.True);
            Assert.That(window.GasPressure.Animating, Is.True);
            window.Dispose();
        });
    }

    /// <summary>
    ///     Through the real bound user interface, which is where the first value comes from. Few enough ticks that a number
    ///         easing up from 0 wouldn't have got there.
    /// </summary>
    [Test]
    public async Task PumpOpensOnItsPressure()
    {
        await OverrideCVar(Side.Client, KsCCVars.TguiStyle, true);
        await Pair.RunTicksSync(2);

        var testMap = await Pair.CreateTestMap();
        var user = await SpawnUser(new EntityCoordinates(testMap.Grid, new Vector2(0.5f, 1.5f)));

        await Server.WaitPost(() =>
        {
            var pumpUid = SEntMan.SpawnEntity(PressurePump, new EntityCoordinates(testMap.Grid, new Vector2(0.5f, 0.5f)));
            SEntMan.GetComponent<GasPressurePumpComponent>(pumpUid).TargetPressure = 4321f;
            SEntMan.Dirty(pumpUid, SEntMan.GetComponent<GasPressurePumpComponent>(pumpUid));
            SEntMan.System<SharedUserInterfaceSystem>().OpenUi(pumpUid, GasPressurePumpUiKey.Key, user);
        });
        await Pair.RunTicksSync(6);

        await Client.WaitAssertion(() =>
        {
            Assert.That(OpenWindow<KsTguiGasPressurePumpWindow>().PressureInput.ShownText, Does.StartWith("4321"));
        });
    }

    /// <summary>
    ///     tg sets <c>animated</c> on these inputs, so a value from outside eases and doesn't jump.
    /// </summary>
    [Test]
    public async Task AtmosInputsEaseToAValueFromOutside()
    {
        await Client.WaitPost(() =>
        {
            void Check(KsTguiNumberInput input)
            {
                input.SetValue(10f);
                var before = input.ShownText;
                input.SetValue(20f);
                Assert.That(input.ShownText, Is.EqualTo(before), "A new value should only start the number moving.");
            }

            using var pressurePump = new KsTguiGasPressurePumpWindow();
            Check(pressurePump.PressureInput);
            using var volumePump = new KsTguiGasVolumePumpWindow();
            Check(volumePump.RateInput);
            using var filter = new KsTguiGasFilterWindow();
            Check(filter.RateInput);
            using var mixer = new KsTguiGasMixerWindow();
            Check(mixer.PressureInput);
            Check(mixer.NodeOneInput);
            Check(mixer.NodeTwoInput);
            using var thermomachine = new KsTguiGasThermomachineWindow();
            Check(thermomachine.TemperatureInput);
        });
    }

    private static ChemMasterBoundUserInterfaceState MasterState(ContainerInfo input, float bufferUnits)
    {
        return new ChemMasterBoundUserInterfaceState(
            ChemMasterMode.Transfer,
            ChemMasterSortingType.Alphabetical,
            input,
            null,
            [new ReagentQuantity(WaterReagent.Id, FixedPoint2.New(bufferUnits))],
            FixedPoint2.New(bufferUnits),
            selectedPillType: 2,
            pillDosageLimit: 50,
            updateLabel: false,
            ChemMasterDrawSource.Internal,
            valveOpen: false);
    }

    private static ContainerInfo Beaker(float units)
    {
        return new ContainerInfo("beaker", FixedPoint2.New(units), FixedPoint2.New(100))
        {
            Reagents = new List<ReagentQuantity> { new(WaterReagent.Id, FixedPoint2.New(units)) },
        };
    }

    [Test]
    public async Task ChemMasterOpensOnItsAmountsAndCountsUpOnlyWhatArrivesLater()
    {
        await Client.WaitPost(() =>
        {
            var window = new KsTguiChemMasterWindow();
            window.UpdateState(MasterState(Beaker(30f), 7f));

            foreach (var number in Descendants<KsTguiAnimatedNumber>(window))
            {
                Assert.That(number.Animating, Is.False, $"{number.Text} is counting up as the window opens.");
            }

            var input = Beaker(30f);
            input.Reagents!.Add(new ReagentQuantity(OxygenReagent.Id, FixedPoint2.New(5)));
            window.UpdateState(MasterState(input, 7f));

            var arrived = Descendants<KsTguiAnimatedNumber>(window).Single(number => number.Value == 5f);
            Assert.That(arrived.Displayed, Is.EqualTo(0f), "A reagent that arrives in an open window counts up from 0, as tg's.");
            window.Dispose();
        });
    }

    [Test]
    public async Task ChemMasterRowsKeepTheirNumberAcrossAState()
    {
        await Client.WaitPost(() =>
        {
            var window = new KsTguiChemMasterWindow();
            window.UpdateState(MasterState(Beaker(30f), 7f));

            var rowsBefore = Descendants<KsTguiAnimatedNumber>(window).Where(number => number.Text!.EndsWith('u')).ToList();
            var bufferRow = rowsBefore.Single(number => number.Value == 7f && number.Parent!.ChildCount > 2);
            var beakerRow = rowsBefore.Single(number => number.Value == 30f && number.Parent!.ChildCount > 2);

            window.UpdateState(MasterState(Beaker(40f), 9f));

            Assert.That(bufferRow.IsInsideTree || bufferRow.Parent != null, Is.True, "The buffer's row should still hold its number.");
            Assert.That(bufferRow.Value, Is.EqualTo(9f), "The same control should take the new amount.");
            Assert.That(beakerRow.Value, Is.EqualTo(40f));
            Assert.That(bufferRow.Animating, Is.True);
            Assert.That(beakerRow.Animating, Is.True);
            window.Dispose();
        });
    }

    private static ReagentDispenserBoundUserInterfaceState DispenserState(ContainerInfo? beaker)
    {
        return new ReagentDispenserBoundUserInterfaceState(beaker, null, [], ReagentDispenserDispenseAmount.U10, valveOpen: false);
    }

    [Test]
    public async Task DispenserBeakerEases()
    {
        await Client.WaitPost(() =>
        {
            var window = new KsTguiReagentDispenserWindow();
            window.UpdateState(DispenserState(Beaker(20f)));

            var bar = Descendants<KsTguiProgressBar>(window).Single();
            Assert.That(bar.ShownText, Is.EqualTo("20/100"), "Nothing should count up from 0 as the window opens.");

            var row = Descendants<KsTguiAnimatedNumber>(window).Single(number => number.Value == 20f && number.Parent is not KsTguiProgressBar);
            Assert.That(row.Displayed, Is.EqualTo(20f));
            Assert.That(row.Animating, Is.False);

            window.UpdateState(DispenserState(Beaker(50f)));
            Assert.That(row.Value, Is.EqualTo(50f), "The same control should take the new amount.");
            Assert.That(row.Parent, Is.Not.Null);
            Assert.That(row.Animating, Is.True, "A change while it is open should ease.");

            window.UpdateState(DispenserState(null));
            Assert.That(bar.ShownText, Is.Empty, "An empty slot shows no amount.");

            window.UpdateState(DispenserState(Beaker(20f)));
            Assert.That(bar.ShownText, Is.EqualTo("0/100"), "A beaker put in while the window is open fills from 0, as tg's.");
            window.Dispose();
        });
    }

    [Test]
    public async Task LatheMaterialKeepsItsNumberAcrossARebuild()
    {
        await OverrideCVar(Side.Client, KsCCVars.TguiStyle, true);
        await Pair.RunTicksSync(2);

        var testMap = await Pair.CreateTestMap();
        var user = await SpawnUser(new EntityCoordinates(testMap.Grid, new Vector2(0.5f, 1.5f)));

        EntityUid latheUid = default;
        SharedMaterialStorageSystem materialStorageSystem = null!;
        await Server.WaitPost(() =>
        {
            materialStorageSystem = SEntMan.System<SharedMaterialStorageSystem>();
            latheUid = SEntMan.SpawnEntity(Lathe, new EntityCoordinates(testMap.Grid, new Vector2(0.5f, 0.5f)));
            Assert.That(materialStorageSystem.TryChangeMaterialAmount(latheUid, SteelMaterial, 10000), Is.True);
            SEntMan.System<SharedUserInterfaceSystem>().OpenUi(latheUid, LatheUiKey.Key, user);
        });
        await Pair.RunTicksSync(15);

        KsTguiAnimatedNumber number = null!;
        var valueBefore = 0f;
        await Client.WaitPost(() =>
        {
            var materials = Descendants<KsTguiMaterialList>(OpenWindow<KsTguiLatheWindow>()).Single();
            number = Descendants<KsTguiAnimatedNumber>(materials).Single();
            valueBefore = number.Value;
        });

        await Server.WaitPost(() =>
        {
            Assert.That(materialStorageSystem.TryChangeMaterialAmount(latheUid, SteelMaterial, 10000), Is.True);
        });
        await Pair.RunTicksSync(15);

        await Client.WaitAssertion(() =>
        {
            var materials = Descendants<KsTguiMaterialList>(OpenWindow<KsTguiLatheWindow>()).Single();
            Assert.That(Descendants<KsTguiAnimatedNumber>(materials).Single(), Is.SameAs(number), "The rebuilt counter should hold the same number.");
            Assert.That(number.Value, Is.EqualTo(valueBefore * 2f).Within(0.01f));
        });
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task StockWindowsEaseOnlyUnderTgui(bool tgui)
    {
        await OverrideCVar(Side.Client, KsCCVars.TguiStyle, tgui);
        await Pair.RunTicksSync(2);

        await Client.WaitPost(() =>
        {
            var pallet = new CargoPalletMenu();
            var appraisal = Descendants<KsTguiAnimatedNumber>(pallet).Single();
            pallet.SetAppraisal(100);
            pallet.SetAppraisal(200);
            Assert.That(appraisal.Animating, Is.EqualTo(tgui));
            pallet.Dispose();

            var cryo = new CryoPodWindow();
            var temperature = Descendants<KsTguiAnimatedNumber>(cryo).Single();
            cryo.KsShowTemperature(300f);
            cryo.KsShowTemperature(250f);
            Assert.That(temperature.Animating, Is.EqualTo(tgui));

            cryo.KsShowTemperature(null);
            Assert.That(temperature.Animating, Is.False);
            Assert.That(temperature.Text, Is.Not.Empty);
            cryo.Dispose();
        });
    }
}
