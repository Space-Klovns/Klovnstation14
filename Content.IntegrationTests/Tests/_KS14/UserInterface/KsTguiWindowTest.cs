#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.Client._KS14.Atmos.UI;
using Content.Client._KS14.Chemistry.UI;
using Content.Client._KS14.UserInterface.Tgui;
using Content.Client.Atmos.EntitySystems;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Shared._KS14.CCVar;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Content.Shared.Atmos.Piping.Binary.Components;
using Content.Shared.Atmos.Piping.Portable.Components;
using Content.Shared.Atmos.Piping.Unary.Components;
using Content.Shared.Chemistry;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Content.Shared.Storage;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Server.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.UserInterface;

/// <summary>
///     The tgui ports: every window fits what it shows at the size it opens at, the global restyle follows its cvar,
///         and a real canister opens the tgui window and keeps it in step with the server.
/// </summary>
[TestOf(typeof(KsTguiWindow))]
public sealed class KsTguiWindowTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    private static readonly EntProtoId AirCanister = "AirCanister";
    private static readonly EntProtoId MobHuman = "MobHuman";
    private static readonly ProtoId<ReagentPrototype> Water = "Water";
    private static readonly ProtoId<ReagentPrototype> Ethanol = "Ethanol";
    private static readonly ProtoId<ReagentPrototype> Bicaridine = "Bicaridine";
    private static readonly ProtoId<ReagentPrototype> Dylovene = "Dylovene";

    /// <summary>
    ///     Opens each ported window with busy, realistic contents and checks its default size leaves room for all of it.
    ///         A squeezed control is laid out small and simply vanishes, so this compares the window against what its
    ///         content column asks for with unlimited room.
    /// </summary>
    [Test]
    public async Task EveryPortedWindowFitsItsContentsAtItsDefaultSize()
    {
        var windows = new List<KsTguiWindow>();
        await Client.WaitPost(() =>
        {
            windows.AddRange(MakePopulatedWindows());
            foreach (var window in windows)
            {
                window.OpenCentered();
            }
        });

        // A window's contents take a few passes to settle.
        await Pair.RunTicksSync(5);

        try
        {
            await Client.WaitAssertion(() =>
            {
                Assert.Multiple(() =>
                {
                    foreach (var window in windows)
                    {
                        var name = window.GetType().Name;
                        window.Contents.Measure(Vector2Helpers.Infinity);
                        var needed = window.Contents.DesiredSize + new Vector2(0f, KsTguiTheme.TitleBarHeight);

                        Assert.That(window.IsOpen, Is.True, $"{name} opened");
                        Assert.That(window.Size.X, Is.GreaterThanOrEqualTo(needed.X - 0.5f), $"{name} is wide enough for its contents");
                        Assert.That(window.Size.Y, Is.GreaterThanOrEqualTo(needed.Y - 0.5f), $"{name} is tall enough for its contents");
                    }
                });
            });
        }
        finally
        {
            await Client.WaitPost(() =>
            {
                foreach (var window in windows)
                {
                    window.Close();
                }
            });
        }
    }

    /// <summary>
    ///     The global restyle lands on top of the Baron sheet for a stock button, and switching it off brings Baron back
    ///         without reopening anything.
    /// </summary>
    [Test]
    public async Task GlobalRestyleFollowsItsCVar()
    {
        Button button = null!;
        DefaultWindow window = null!;
        await Client.WaitPost(() =>
        {
            button = new Button { Text = "Test" };
            window = new DefaultWindow();
            window.Contents.AddChild(button);
            window.OpenCentered();
        });
        await Pair.RunTicksSync(3);

        await Client.WaitAssertion(() =>
        {
            Assert.That(button.TryGetStyleProperty<StyleBox>(ContainerButton.StylePropertyStyleBox, out var box), Is.True,
                "the button has a box from the sheet");
            Assert.That(box, Is.TypeOf<StyleBoxFlat>(), "it's tgui's flat button while the theme is on");
            Assert.That(((StyleBoxFlat)box).BackgroundColor, Is.EqualTo(KsTguiTheme.Background(KsTguiTheme.Primary)),
                "in tgui's default button blue");
        });

        await OverrideCVar(Side.Client, KsCCVars.TguiTheme, false);
        await Pair.RunTicksSync(3);

        await Client.WaitAssertion(() =>
        {
            Assert.That(button.TryGetStyleProperty<StyleBox>(ContainerButton.StylePropertyStyleBox, out var box), Is.True,
                "the button still has a box from the sheet");
            Assert.That(box, Is.Not.TypeOf<StyleBoxFlat>(), "and it's the Baron texture again once the theme is off");
            window.Close();
        });
    }

    /// <summary>
    ///     The server's echo of a value must not be reported back as the user's change, or every state update would
    ///         send another message.
    /// </summary>
    [Test]
    public async Task NumberInputOnlyReportsTheUsersValues()
    {
        await Client.WaitAssertion(() =>
        {
            var input = new KsTguiNumberInput { MinValue = 0f, MaxValue = 100f, Step = 5f };
            var reported = new List<float>();
            input.OnValueChanged += reported.Add;

            input.Value = 250f;
            Assert.That(input.Value, Is.EqualTo(100f), "a value from state is clamped to the range");
            Assert.That(reported, Is.Empty, "and isn't reported as a change");

            input.SubmitValue(-20f);
            Assert.That(input.Value, Is.EqualTo(0f), "a submitted value is clamped");
            Assert.That(reported, Is.EqualTo(new[] { 0f }), "and reported once");
        });
    }

    /// <summary>
    ///     A real canister opens the tgui window through the upstream interface, titled after the canister, and the
    ///         valve button follows the server when the valve is opened through the interface's message.
    /// </summary>
    [Test]
    public async Task CanisterOpensTheTguiWindowAndFollowsTheServer()
    {
        var testMap = await Pair.CreateTestMap();
        EntityUid canister = default;
        await Server.WaitPost(() =>
        {
            canister = SEntMan.SpawnEntity(AirCanister, testMap.GridCoords);
            var actor = SEntMan.SpawnEntity(MobHuman, testMap.GridCoords);
            Server.PlayerMan.SetAttachedEntity(ServerSession!, actor);
            Server.System<UserInterfaceSystem>().TryOpenUi(canister, GasCanisterUiKey.Key, actor);
        });
        await Pair.RunTicksSync(10);

        var loc = Client.ResolveDependency<ILocalizationManager>();
        await Client.WaitAssertion(() =>
        {
            var window = OpenCanisterWindow();
            Assert.That(window.Title, Is.EqualTo(CEntMan.GetComponent<MetaDataComponent>(ToClientUid(canister)).EntityName),
                "titled after the canister");
            Assert.That(window.FindControl<KsTguiButton>("ValveButton").Text,
                Is.EqualTo(loc.GetString("ks-tgui-atmos-canister-valve-closed")), "a new canister's valve shows closed");
        });

        await Client.WaitPost(() => Client.System<SharedUserInterfaceSystem>()
            .ClientSendUiMessage(ToClientUid(canister), GasCanisterUiKey.Key, new GasCanisterChangeReleaseValveMessage(true)));
        await Pair.RunTicksSync(10);

        await Server.WaitAssertion(() => Assert.That(SEntMan.GetComponent<GasCanisterComponent>(canister).ReleaseValveOpen, Is.True,
            "precondition: the server opened the valve"));
        await Client.WaitAssertion(() =>
        {
            var window = OpenCanisterWindow();
            Assert.That(window.FindControl<KsTguiButton>("ValveButton").Text,
                Is.EqualTo(loc.GetString("ks-tgui-atmos-canister-valve-open")), "the window shows it open");
            window.Close();
        });
    }

    private KsTguiGasCanisterWindow OpenCanisterWindow()
    {
        var windows = Client.ResolveDependency<IUserInterfaceManager>().WindowRoot.Children
            .OfType<KsTguiGasCanisterWindow>()
            .Where(window => window.IsOpen)
            .ToArray();

        Assert.That(windows, Has.Length.EqualTo(1), "the canister's interface opened the tgui window");
        return windows[0];
    }

    /// <summary>
    ///     One of each ported window, filled the way a busy machine would fill it. Runs on the client thread.
    /// </summary>
    private IEnumerable<KsTguiWindow> MakePopulatedWindows()
    {
        var pressurePump = new KsTguiGasPressurePumpWindow { Title = "gas pump" };
        pressurePump.MaxPressure = Atmospherics.MaxOutputPressure;
        pressurePump.SetPumpStatus(true);
        pressurePump.SetOutputPressure(Atmospherics.OneAtmosphere);
        yield return pressurePump;

        var volumePump = new KsTguiGasVolumePumpWindow { Title = "volumetric gas pump" };
        volumePump.SetPumpStatus(true);
        volumePump.SetTransferRate(Atmospherics.MaxTransferRate);
        yield return volumePump;

        var filter = new KsTguiGasFilterWindow { Title = "gas filter" };
        filter.PopulateGasList(Client.System<AtmosphereSystem>().Gases);
        filter.SetFilterStatus(true);
        filter.SetTransferRate(Atmospherics.MaxTransferRate);
        filter.SetFilteredGases(new HashSet<Gas> { Gas.Oxygen, Gas.Plasma });
        yield return filter;

        var mixer = new KsTguiGasMixerWindow { Title = "gas mixer" };
        mixer.SetMixerStatus(true);
        mixer.SetOutputPressure(Atmospherics.MaxOutputPressure);
        mixer.SetNodePercentages(0.25f);
        yield return mixer;

        var thermomachine = new KsTguiGasThermomachineWindow();
        thermomachine.SetActive(true);
        thermomachine.SetTemperature(Atmospherics.T20C);
        yield return thermomachine;

        var heater = new KsTguiSpaceHeaterWindow();
        heater.UpdateState(new SpaceHeaterBoundUserInterfaceState(Atmospherics.T0C, Atmospherics.T0C + 100f, Atmospherics.T20C,
            true, SpaceHeaterMode.Auto, SpaceHeaterPowerLevel.Medium));
        yield return heater;

        var canister = new KsTguiGasCanisterWindow();
        canister.SetCanisterLabel("air canister");
        canister.SetCanisterPressure(4500f);
        canister.SetPortStatus(true);
        canister.SetReleasePressureRange(Atmospherics.OneAtmosphere / 10f, 10f * Atmospherics.OneAtmosphere);
        canister.SetReleasePressure(Atmospherics.OneAtmosphere);
        canister.SetReleaseValve(true);
        canister.SetTankLabel("emergency oxygen tank");
        canister.SetTankPressure(2000f);
        canister.SetShieldSettingsVisible(true);
        canister.UpdateShieldState(enabled: true, dischargeRate: 50f, lastCharge: 600f, maxCharge: 1000f);
        yield return canister;

        var tank = new KsTguiGasTankWindow();
        tank.SetTitle("emergency oxygen tank");
        tank.UpdateState(new GasTankBoundUserInterfaceState { TankPressure = 2500f });
        tank.Update(canConnectInternals: true, internalsConnected: true, outputPressure: 21.3f);
        yield return tank;

        var reagents = new List<ReagentQuantity>
        {
            new(new ReagentId(Water, null), FixedPoint2.New(30)),
            new(new ReagentId(Ethanol, null), FixedPoint2.New(12.5f)),
            new(new ReagentId(Bicaridine, null), FixedPoint2.New(5)),
            new(new ReagentId(Dylovene, null), FixedPoint2.New(2)),
        };

        var dispenser = new KsTguiReagentDispenserWindow();
        var inventory = new List<ReagentInventoryItem>();
        for (var i = 0; i < 24; i++)
        {
            inventory.Add(new ReagentInventoryItem(new ItemStorageLocation(Angle.Zero, new Vector2i(i, 0)),
                $"reagent number {i}", FixedPoint2.New(300 - i), new Color(i / 24f, 0.6f, 1f - i / 24f)));
        }
        dispenser.UpdateState(new ReagentDispenserBoundUserInterfaceState(
            new ContainerInfo("large beaker", FixedPoint2.New(49.5f), FixedPoint2.New(100)) { Reagents = reagents },
            null, inventory, ReagentDispenserDispenseAmount.U10, valveOpen: false));
        yield return dispenser;

        var chemMaster = new KsTguiChemMasterWindow { Title = "ChemMaster 4000" };
        chemMaster.UpdateState(new ChemMasterBoundUserInterfaceState(
            ChemMasterMode.Transfer,
            ChemMasterSortingType.Alphabetical,
            new ContainerInfo("large beaker", FixedPoint2.New(49.5f), FixedPoint2.New(100)) { Reagents = reagents },
            new ContainerInfo("pill canister", FixedPoint2.Zero, FixedPoint2.New(10)) { Entities = new List<(string Id, FixedPoint2 Quantity)>() },
            reagents,
            FixedPoint2.New(49.5f),
            selectedPillType: 3,
            pillDosageLimit: 20,
            updateLabel: true,
            ChemMasterDrawSource.Internal,
            valveOpen: true));
        yield return chemMaster;
    }
}
