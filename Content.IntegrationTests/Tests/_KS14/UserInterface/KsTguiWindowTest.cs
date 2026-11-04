#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.Client._KS14.Atmos.UI;
using Content.Client._KS14.Chemistry.UI;
using Content.Client._KS14.UserInterface.Tgui;
using Content.Client.Atmos.EntitySystems;
using Content.Client.Chemistry.UI;
using Content.IntegrationTests.Fixtures;
using Content.Shared._KS14.Chemistry.InfiniteDispenser;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Piping.Binary.Components;
using Content.Shared.Atmos.Piping.Unary.Components;
using Content.Shared.Chemistry;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Content.Shared.Power.Components;
using Content.Shared.Storage;
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
///     The tgui ports: every window fits what it shows at the size it opens at, a real canister opens the tgui window
///         and keeps it in step with the server, and only the chem dispenser of the reagent dispensers opens one.
/// </summary>
[TestOf(typeof(KsTguiWindow))]
public sealed class KsTguiWindowTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    private static readonly EntProtoId AirCanister = "AirCanister";
    private static readonly EntProtoId ChemDispenser = "ChemDispenser";
    private static readonly EntProtoId SodaDispenser = "SodaDispenser";
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
            var window = OpenWindow<KsTguiGasCanisterWindow>("the canister's interface opened the tgui window");
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
            var window = OpenWindow<KsTguiGasCanisterWindow>("the canister's window stayed open");
            Assert.That(window.FindControl<KsTguiButton>("ValveButton").Text,
                Is.EqualTo(loc.GetString("ks-tgui-atmos-canister-valve-open")), "the window shows it open");
            window.Close();
        });
    }

    /// <summary>
    ///     The soda dispenser shares the chem dispenser's machine and upstream interface, but only the chem dispenser is
    ///         ported: it opens the tgui window, with a button for each reagent it makes, and the soda one the upstream
    ///         window.
    /// </summary>
    [Test]
    public async Task OnlyTheChemDispenserOpensTheTguiWindow()
    {
        var testMap = await Pair.CreateTestMap();
        EntityUid chemDispenser = default;
        await Server.WaitPost(() =>
        {
            chemDispenser = SEntMan.SpawnEntity(ChemDispenser, testMap.GridCoords);
            var sodaDispenser = SEntMan.SpawnEntity(SodaDispenser, testMap.GridCoords);
            var actor = SEntMan.SpawnEntity(MobHuman, testMap.GridCoords);
            Server.PlayerMan.SetAttachedEntity(ServerSession!, actor);

            var ui = Server.System<UserInterfaceSystem>();
            foreach (var dispenser in new[] { chemDispenser, sodaDispenser })
            {
                // Nothing powers the test map. Opening directly skips the power check, but a power change would close it.
                SEntMan.RemoveComponent<ActivatableUIRequiresPowerComponent>(dispenser);
                ui.TryOpenUi(dispenser, ReagentDispenserUiKey.Key, actor);
            }
        });
        await Pair.RunTicksSync(10);

        await Client.WaitAssertion(() =>
        {
            // DIAG
            var diagUi = Client.System<SharedUserInterfaceSystem>();
            foreach (var w in Client.ResolveDependency<IUserInterfaceManager>().WindowRoot.Children)
                TestContext.Out.WriteLine($"DIAG window {w.GetType().Name} open={(w as BaseWindow)?.IsOpen}");
            foreach (var q in CEntMan.EntityQuery<Robust.Shared.GameObjects.UserInterfaceComponent>(true))
            {
                foreach (var (key, data) in q.Interfaces)
                    TestContext.Out.WriteLine($"DIAG ent {CEntMan.ToPrettyString(q.Owner)} key {key} type {data.ClientType} open={diagUi.IsUiOpen(q.Owner, key)}");
            }
            var tgui = OpenWindow<KsTguiReagentDispenserWindow>("the chem dispenser opened the tgui window");
            var upstream = OpenWindow<ReagentDispenserWindow>("the soda dispenser opened the upstream window");

            var reagents = CEntMan.GetComponent<KsInfiniteReagentDispenserComponent>(ToClientUid(chemDispenser)).Reagents;
            Assert.That(tgui.FindControl<GridContainer>("InfiniteList").ChildCount, Is.EqualTo(reagents.Count),
                "with a button for each reagent the chem dispenser makes");

            tgui.Close();
            upstream.Close();
        });
    }

    private T OpenWindow<T>(string message) where T : BaseWindow
    {
        var windows = Client.ResolveDependency<IUserInterfaceManager>().WindowRoot.Children
            .OfType<T>()
            .Where(window => window.IsOpen)
            .ToArray();

        Assert.That(windows, Has.Length.EqualTo(1), message);
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

        var reagents = new List<ReagentQuantity>
        {
            new(new ReagentId(Water, null), FixedPoint2.New(30)),
            new(new ReagentId(Ethanol, null), FixedPoint2.New(12.5f)),
            new(new ReagentId(Bicaridine, null), FixedPoint2.New(5)),
            new(new ReagentId(Dylovene, null), FixedPoint2.New(2)),
        };

        // The chem dispenser's own reagents, and a few extra jugs put in it.
        var dispenser = new KsTguiReagentDispenserWindow();
        Assert.That(Client.ResolveDependency<IPrototypeManager>().Index(ChemDispenser)
            .TryGetComponent<KsInfiniteReagentDispenserComponent>(out var infinite, Client.ResolveDependency<IComponentFactory>()),
            "precondition: the chem dispenser makes its own reagents");
        dispenser.SetInfiniteReagents(infinite!.Reagents);
        var jugs = new List<ReagentInventoryItem>();
        for (var i = 0; i < 6; i++)
        {
            jugs.Add(new ReagentInventoryItem(new ItemStorageLocation(Angle.Zero, new Vector2i(i, 0)),
                $"reagent number {i}", FixedPoint2.New(300 - i), new Color(i / 6f, 0.6f, 1f - i / 6f)));
        }
        dispenser.UpdateState(new ReagentDispenserBoundUserInterfaceState(
            new ContainerInfo("large beaker", FixedPoint2.New(49.5f), FixedPoint2.New(100)) { Reagents = reagents },
            null, jugs, ReagentDispenserDispenseAmount.U10, valveOpen: false));
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
