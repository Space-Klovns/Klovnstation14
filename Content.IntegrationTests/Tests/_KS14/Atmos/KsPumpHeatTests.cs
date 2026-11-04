#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Client._KS14.Atmos.UI;
using Content.Client.Atmos.UI;
using Content.Client.Message;
using Content.Client.UserInterface.Controls;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server._KS14.Atmos;
using Content.Server.Atmos.Piping.Binary.Components;
using Content.Server.NodeContainer.EntitySystems;
using Content.Server.NodeContainer.Nodes;
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Shared._KS14.Atmos;
using Content.Shared._KS14.CCVar;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Piping.Binary.Components;
using Content.Shared.Atmos.Components;
using Content.Shared.Coordinates;
using Content.Shared.Power.Components;
using Robust.Shared.Configuration;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._KS14.Atmos;

/// <summary>
///     What a compressing device does with its heat: recovering most of it, paying for the rest in load, and doing that on
///         a real APC without ever browning it out.
/// </summary>
[TestOf(typeof(KsPumpHeatSystem))]
public sealed class KsPumpHeatTests : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    // The APC has a real APC's ramp and limits, since those are what a load has to stay inside. Its big battery is
    //     charged by nothing, so a test can drain it.
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: KsTestPumpApc
  components:
  - type: Battery
    netsync: false
    maxCharge: 10000000
    startingCharge: 10000000
  - type: PowerNetworkBattery
    maxChargeRate: 5000
    maxSupply: 10000
    supplyRampTolerance: 1000
    supplyRampRate: 500
  - type: BatteryCharger
    voltage: Medium
  - type: BatteryDischarger
    voltage: Apc
  - type: Apc
    voltage: Apc
  - type: NodeContainer
    nodes:
      input:
        !type:CableDeviceNode
        nodeGroupID: MVPower
      output:
        !type:CableDeviceNode
        nodeGroupID: Apc
  - type: Transform
    anchored: true
  - type: UserInterface
    interfaces:
      enum.ApcUiKey.Key:
        type: ApcBoundUserInterface
  - type: AccessReader
    access: [['Engineering']]

- type: entity
  id: KsTestPumpDevice
  components:
  - type: Transform
    anchored: true
  - type: ApcPowerReceiver
    powerLoad: 200
  - type: ExtensionCableReceiver
";

    private readonly EntProtoId _apcPrototype = "KsTestPumpApc";
    private readonly EntProtoId _devicePrototype = "KsTestPumpDevice";
    private static readonly EntProtoId ApcExtensionPrototype = "CableApcExtension";
    private static readonly EntProtoId VolumePumpPrototype = "GasVolumePump";

    private const float IdleLoad = 200f;
    private const float HeatPerSample = 1e7f;
    private const int TicksPerSecond = 30;

    private sealed record Site(EntityUid MapUid, Entity<MapGridComponent> Grid, EntityUid ApcUid, PowerNetworkBatteryComponent ApcBattery);

    /// <summary>
    ///     A strip of floor and an APC with its extension cable, charged and ready, reaching everything on the strip.
    /// </summary>
    private async Task<Site> BuildSite()
    {
        var server = Pair.Server;
        var entityManager = server.EntMan;
        Site site = default!;

        await server.WaitPost(() =>
        {
            var mapSystem = entityManager.System<SharedMapSystem>();
            var mapUid = mapSystem.CreateMap(out var mapId);
            var grid = mapSystem.CreateGridEntity(mapId);
            for (var x = 0; x < 32; x++)
            {
                mapSystem.SetTile(grid, new Vector2i(x, 0), new Tile(1));
            }

            var apcUid = entityManager.SpawnEntity(_apcPrototype, grid.Owner.ToCoordinates(0, 0));
            var extensionUid = entityManager.SpawnEntity(ApcExtensionPrototype, grid.Owner.ToCoordinates(0, 0));
            entityManager.System<ExtensionCableSystem>().SetProviderTransferRange(extensionUid, 32);

            site = new Site(mapUid, grid, apcUid, entityManager.GetComponent<PowerNetworkBatteryComponent>(apcUid));
        });

        return site;
    }

    private EntityUid SpawnDevice(Site site, int x)
    {
        var entityManager = Pair.Server.EntMan;
        var deviceUid = entityManager.SpawnEntity(_devicePrototype, site.Grid.Owner.ToCoordinates(x, 0));
        entityManager.System<ExtensionCableSystem>().SetReceiverReceptionRange(deviceUid, 32);
        return deviceUid;
    }

    /// <summary>
    ///     What a device would record from a burst of hard work: enough, whatever the tuning, to want its maximum load.
    /// </summary>
    private void Work(EntityUid deviceUid)
    {
        Pair.Server.System<KsPumpHeatSystem>().Record(deviceUid, HeatPerSample, HeatPerSample, HeatPerSample);
    }

    private float MaximumLoad()
    {
        return IdleLoad * (1f + Pair.Server.CfgMan.GetCVar(KsCCVars.AtmosPumpMaxLoadMultiplier));
    }

    private float LoadOf(EntityUid deviceUid)
    {
        return Pair.Server.EntMan.GetComponent<ApcPowerReceiverComponent>(deviceUid).Load;
    }

    private async Task RunWorking(IReadOnlyList<EntityUid> devices, int ticks, Action? everyTick = null)
    {
        var server = Pair.Server;
        for (var tick = 0; tick < ticks; tick++)
        {
            await server.WaitPost(() =>
            {
                foreach (var deviceUid in devices)
                {
                    Work(deviceUid);
                }
            });
            await server.WaitRunTicks(1);
            if (everyTick != null)
                await server.WaitPost(everyTick);
        }
    }

    [Test]
    public async Task LoadClimbsWithWorkAndFallsBackToIdle()
    {
        var server = Pair.Server;
        var site = await BuildSite();
        EntityUid deviceUid = default;

        await server.WaitPost(() => deviceUid = SpawnDevice(site, 3));
        await server.WaitRunTicks(TicksPerSecond);

        await RunWorking([deviceUid], TicksPerSecond * 30);
        await server.WaitAssertion(() => Assert.That(LoadOf(deviceUid), Is.EqualTo(MaximumLoad()).Within(1f),
            "A device working flat out didn't climb to its maximum load."));

        await server.WaitRunTicks(TicksPerSecond * 120);
        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(LoadOf(deviceUid), Is.EqualTo(IdleLoad), "A device that stopped working didn't fall back to idle.");
                Assert.That(server.EntMan.HasComponent<KsPumpHeatComponent>(deviceUid), Is.False, "An idle device kept paying for the component.");
            }
        });
    }

    /// <summary>
    ///     A little more work a little more load: the load tracks what's billed, not only a cap.
    /// </summary>
    [Test]
    public async Task LoadScalesWithTheWork()
    {
        var server = Pair.Server;
        var site = await BuildSite();
        var scale = server.ResolveDependency<IConfigurationManager>().GetCVar(Content.Shared.CCVar.CCVars.AtmosHeatScale);
        const float billedWatts = 300f;
        EntityUid deviceUid = default;

        await server.WaitPost(() => deviceUid = SpawnDevice(site, 3));
        await server.WaitRunTicks(TicksPerSecond);

        for (var tick = 0; tick < TicksPerSecond * 60; tick++)
        {
            await server.WaitPost(() => server.System<KsPumpHeatSystem>().Record(deviceUid,
                billedWatts * scale / TicksPerSecond, 0f, 0f));
            await server.WaitRunTicks(1);
        }

        await server.WaitAssertion(() => Assert.That(LoadOf(deviceUid), Is.EqualTo(IdleLoad + billedWatts).Within(billedWatts * 0.1f),
            "The load isn't its idle plus what the device is billed."));
    }

    /// <summary>
    ///     The reason the load climbs as it does: a dozen pumps starting together on one APC, climbing faster between them
    ///         than its supply ramps, and nothing on it may lose power for even a tick. A control device that does no work
    ///         stands in for the lights.
    /// </summary>
    [Test]
    public async Task ManyDevicesStartingTogetherNeverBrownOutTheApc()
    {
        var server = Pair.Server;
        var site = await BuildSite();
        var devices = new List<EntityUid>();
        EntityUid lightUid = default;

        await server.WaitPost(() =>
        {
            for (var x = 2; x < 14; x++)
            {
                devices.Add(SpawnDevice(site, x));
            }

            lightUid = SpawnDevice(site, 20);
        });

        // All of them connecting at once is its own step in the load. The APC has to have caught up with that first.
        await server.WaitRunTicks(TicksPerSecond * 6);
        await server.WaitAssertion(() => Assert.That(server.EntMan.GetComponent<ApcPowerReceiverComponent>(lightUid).Powered,
            "The APC never powered its devices, so this proves nothing."));

        var outages = 0;
        await RunWorking(devices, TicksPerSecond * 25, () =>
        {
            if (!server.EntMan.GetComponent<ApcPowerReceiverComponent>(lightUid).Powered)
                outages++;
        });

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(devices.Sum(LoadOf), Is.GreaterThan(devices.Count * IdleLoad * 1.5f), "The devices barely climbed, so this proves nothing.");
                Assert.That(outages, Is.Zero, "The APC browned out as the devices climbed.");
            }
        });
    }

    /// <summary>
    ///     Far more devices than the APC can sustain stop at what it can, instead of draining it until everything on it dies.
    /// </summary>
    [Test]
    public async Task LoadStopsAtWhatTheApcCanSustain()
    {
        var server = Pair.Server;
        var site = await BuildSite();
        var devices = new List<EntityUid>();

        await server.WaitPost(() =>
        {
            for (var x = 2; x < 16; x++)
            {
                devices.Add(SpawnDevice(site, x));
            }
        });
        await server.WaitRunTicks(TicksPerSecond * 2);

        var peak = 0f;
        await RunWorking(devices, TicksPerSecond * 40, () => peak = MathF.Max(peak, devices.Sum(LoadOf)));

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(devices.Count * MaximumLoad(), Is.GreaterThan(site.ApcBattery.MaxChargeRate), "The devices can't overload the APC, so this proves nothing.");
                Assert.That(peak, Is.LessThan(site.ApcBattery.MaxChargeRate), "The devices climbed past what the APC can sustain.");
            }
        });
    }

    [Test]
    public async Task RaisedLoadComesOffAnApcThatIsRunningOut()
    {
        var server = Pair.Server;
        var site = await BuildSite();
        EntityUid deviceUid = default;

        await server.WaitPost(() => deviceUid = SpawnDevice(site, 3));
        await server.WaitRunTicks(TicksPerSecond);
        await RunWorking([deviceUid], TicksPerSecond * 20);

        await server.WaitPost(() =>
        {
            var batteryComponent = server.EntMan.GetComponent<BatteryComponent>(site.ApcUid);
            server.System<BatterySystem>().SetCharge((site.ApcUid, batteryComponent), batteryComponent.MaxCharge * 0.1f);
        });

        await RunWorking([deviceUid], TicksPerSecond * 20);
        await server.WaitAssertion(() => Assert.That(LoadOf(deviceUid), Is.EqualTo(IdleLoad).Within(1f),
            "A device kept its raised load on an APC low on charge."));
    }

    /// <summary>
    ///     On a real pump: the work that doesn't stay in the gas goes to the room and the recovered share, nothing is lost,
    ///         and a device off the grid recovers nothing.
    /// </summary>
    [Test]
    public async Task ThePumpsWorkAddsUp([Values(true, false)] bool onTheGrid)
    {
        var server = Pair.Server;
        var entityManager = server.EntMan;
        var site = await BuildSite();
        var transferThermoSystem = server.System<KsTransferThermoSystem>();
        var recovery = server.CfgMan.GetCVar(KsCCVars.AtmosPumpHeatRecovery);
        var keptFraction = server.CfgMan.GetCVar(KsCCVars.AtmosPumpCompressionHeatFraction);
        EntityUid deviceUid = default;

        await server.WaitPost(() =>
        {
            deviceUid = SpawnDevice(site, 3);
            if (!onTheGrid)
                entityManager.System<PowerReceiverSystem>().SetNeedsPower(deviceUid, false);
        });
        await server.WaitRunTicks(TicksPerSecond);

        await server.WaitAssertion(() =>
        {
            var packet = new GasMixture(200f) { Temperature = Atmospherics.T20C };
            packet.AdjustMoles(Gas.Nitrogen, 10f);
            var room = new GasMixture(Atmospherics.CellVolume) { Temperature = Atmospherics.T20C };
            room.AdjustMoles(Gas.Nitrogen, 100f);

            transferThermoSystem.Apply(packet, Atmospherics.OneAtmosphere, Atmospherics.MaxOutputPressure, doesWork: true, deviceUid: deviceUid, heatSink: room);

            var pumpHeat = entityManager.GetComponent<KsPumpHeatComponent>(deviceUid);
            var total = pumpHeat.PendingGasHeat + pumpHeat.PendingRoomHeat + pumpHeat.PendingRecoveredHeat;
            var unkept = total * (1f - keptFraction);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(total, Is.GreaterThan(0f), "The pump did no work, so this proves nothing.");
                Assert.That(pumpHeat.PendingGasHeat, Is.EqualTo(keptFraction * total).Within(total * 0.001f), "The gas didn't keep its share.");
                Assert.That(pumpHeat.PendingRecoveredHeat, Is.EqualTo(onTheGrid ? recovery * unkept : 0f).Within(total * 0.001f),
                    "The wrong share came back as electricity.");
                Assert.That(pumpHeat.PendingRoomHeat, Is.EqualTo(unkept - (onTheGrid ? recovery * unkept : 0f)).Within(total * 0.001f),
                    "The room didn't get the rest.");
            }
        });
    }

    /// <summary>
    ///     A pump in vacuum has nowhere to shed its heat, but still recovers its share rather than keeping it all.
    /// </summary>
    [Test]
    public async Task AVacuumPumpStillRecovers()
    {
        var server = Pair.Server;
        var site = await BuildSite();
        EntityUid deviceUid = default;

        await server.WaitPost(() => deviceUid = SpawnDevice(site, 3));
        await server.WaitRunTicks(TicksPerSecond);

        await server.WaitAssertion(() =>
        {
            var packet = new GasMixture(200f) { Temperature = Atmospherics.T20C };
            packet.AdjustMoles(Gas.Nitrogen, 10f);

            server.System<KsTransferThermoSystem>().Apply(packet, Atmospherics.OneAtmosphere, Atmospherics.MaxOutputPressure,
                doesWork: true, deviceUid: deviceUid, heatSink: GasMixture.SpaceGas);

            var pumpHeat = server.EntMan.GetComponent<KsPumpHeatComponent>(deviceUid);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(pumpHeat.PendingRecoveredHeat, Is.GreaterThan(0f), "A pump in vacuum recovered nothing.");
                Assert.That(pumpHeat.PendingRoomHeat, Is.Zero, "Vacuum took a pump's heat.");
            }
        });
    }

    /// <summary>
    ///     A map saved while a pump works keeps its real idle load, or every reload would ratchet it upward.
    /// </summary>
    [Test]
    public async Task ASaveMidLoadKeepsTheIdleLoad()
    {
        var server = Pair.Server;
        var entityManager = server.EntMan;
        var site = await BuildSite();
        var mapLoader = server.System<MapLoaderSystem>();
        var path = new ResPath($"/{nameof(ASaveMidLoadKeepsTheIdleLoad)}.yml");
        EntityUid deviceUid = default;

        await server.WaitPost(() => deviceUid = SpawnDevice(site, 3));
        await server.WaitRunTicks(TicksPerSecond);
        await RunWorking([deviceUid], TicksPerSecond * 10);

        var raised = 0f;
        await server.WaitAssertion(() =>
        {
            raised = LoadOf(deviceUid);
            Assert.That(raised, Is.GreaterThan(IdleLoad + 10f), "The load never rose, so this proves nothing.");
            Assert.That(mapLoader.TrySaveMap(entityManager.GetComponent<MapComponent>(site.MapUid).MapId, path));
        });

        await server.WaitAssertion(() =>
        {
            Assert.That(mapLoader.TryLoadMap(path, out var loaded, out _));

            var query = entityManager.EntityQueryEnumerator<KsPumpHeatComponent, TransformComponent>();
            var found = 0;
            while (query.MoveNext(out _, out var pumpHeat, out var transformComponent))
            {
                if (transformComponent.MapUid != loaded!.Value.Owner)
                    continue;

                found++;
                Assert.That(pumpHeat.IdleLoad, Is.EqualTo(IdleLoad), "The save lost the device's idle load.");
            }

            Assert.That(found, Is.EqualTo(1), "The saved device lost its component.");
        });
    }

    /// <summary>
    ///     A real pump, on a real APC, pushing air from one atmosphere into three as fast as it goes.
    /// </summary>
    [Test]
    public async Task ARealPumpPaysForItsWorkAndSparesItsRoom()
    {
        var server = Pair.Server;
        var entityManager = server.EntMan;
        var site = await BuildSite();
        var nodeContainerSystem = server.System<NodeContainerSystem>();
        var recovery = server.CfgMan.GetCVar(KsCCVars.AtmosPumpHeatRecovery);
        var keptFraction = server.CfgMan.GetCVar(KsCCVars.AtmosPumpCompressionHeatFraction);
        const float deltaTime = 0.5f;
        EntityUid pumpUid = default;

        await server.WaitPost(() =>
        {
            // Air around it to shed heat into: in vacuum, what isn't recovered would stay in the gas.
            var air = new GasMixture(Atmospherics.CellVolume) { Temperature = Atmospherics.T20C };
            air.SetMoles(Gas.Nitrogen, Atmospherics.NitrogenMolesStandard + Atmospherics.OxygenMolesStandard);
            entityManager.System<Content.Server.Atmos.EntitySystems.AtmosphereSystem>().SetMapAtmosphere(site.MapUid, space: false, air);

            pumpUid = entityManager.SpawnEntity(VolumePumpPrototype, site.Grid.Owner.ToCoordinates(3, 0));
            entityManager.System<ExtensionCableSystem>().SetReceiverReceptionRange(pumpUid, 32);

            var pump = entityManager.GetComponent<GasVolumePumpComponent>(pumpUid);
            pump.Enabled = true;
            pump.TransferRate = pump.MaxTransferRate;
        });
        await server.WaitRunTicks(TicksPerSecond);

        for (var update = 0; update < 40; update++)
        {
            await server.WaitPost(() =>
            {
                Assert.That(nodeContainerSystem.TryGetNodes(pumpUid, "inlet", "outlet", out PipeNode? inlet, out PipeNode? outlet));
                FillWithNitrogen(inlet!.Air, Atmospherics.OneAtmosphere);
                FillWithNitrogen(outlet!.Air, 3f * Atmospherics.OneAtmosphere);

                var ev = new AtmosDeviceUpdateEvent(deltaTime, null, null);
                entityManager.EventBus.RaiseLocalEvent(pumpUid, ref ev);
            });
            await server.WaitRunTicks((int)(TicksPerSecond * deltaTime));
        }

        await server.WaitAssertion(() =>
        {
            var pumpHeat = entityManager.GetComponent<KsPumpHeatComponent>(pumpUid);
            var total = pumpHeat.GasPower + pumpHeat.RoomPower + pumpHeat.RecoveredPower;
            var withoutRecovery = pumpHeat.RoomPower + pumpHeat.RecoveredPower;

            TestContext.Out.WriteLine($"gas {pumpHeat.GasPower:0} W, room {pumpHeat.RoomPower:0} W, recovered {pumpHeat.RecoveredPower:0} W, " +
                                      $"load {LoadOf(pumpUid):0} W, unrecovered room would be {withoutRecovery:0} W");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(total, Is.GreaterThan(0f), "The pump did no work.");
                Assert.That(pumpHeat.RoomPower, Is.EqualTo((1f - recovery) * withoutRecovery).Within(total * 0.01f), "The room didn't get what's left of the heat.");
                Assert.That(pumpHeat.GasPower, Is.EqualTo(keptFraction * total).Within(total * 0.01f), "The gas didn't keep its share.");
                Assert.That(LoadOf(pumpUid), Is.GreaterThan(IdleLoad + 100f), "Working hard didn't raise the pump's load.");
                Assert.That(LoadOf(pumpUid), Is.LessThanOrEqualTo(MaximumLoad() + 1f), "The pump went past its maximum load.");
            }
        });
    }

    private static void FillWithNitrogen(GasMixture mixture, float pressure)
    {
        mixture.Clear();
        mixture.Temperature = Atmospherics.T20C;
        mixture.AdjustMoles(Gas.Nitrogen, pressure * mixture.Volume / (Atmospherics.R * Atmospherics.T20C));
    }

    /// <summary>
    ///     The pump windows have a fixed size, so the footnote has to fit even at its longest, or it pushes something out.
    /// </summary>
    [Test]
    public async Task TheFootnoteFitsTheFixedSizeWindows()
    {
        var client = Pair.Client;
        var localizationManager = client.ResolveDependency<ILocalizationManager>();
        var windows = new List<(FancyWindow Window, KsPumpHeatFootnote Footnote)>();

        await client.WaitPost(() =>
        {
            var volumeWindow = new GasVolumePumpWindow();
            var pressureWindow = new GasPressurePumpWindow();
            windows.Add((volumeWindow, volumeWindow.HeatFootnote));
            windows.Add((pressureWindow, pressureWindow.HeatFootnote));

            var longest = localizationManager.GetString("ks-pump-heat-footnote", ("text", localizationManager.GetString(
                "ks-pump-heat-footnote-recovering", ("gas", 888888f), ("room", 888888f), ("recovered", 888888f))));

            foreach (var (window, footnote) in windows)
            {
                footnote.SetMarkup(longest);
                window.OpenCentered();
            }
        });
        await client.WaitRunTicks(5);

        await client.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                foreach (var (window, _) in windows)
                {
                    window.Measure(new Vector2(window.SetWidth, float.PositiveInfinity));
                    Assert.That(window.DesiredSize.Y, Is.LessThanOrEqualTo(window.SetHeight), $"{window.GetType().Name} is too short for its footnote.");
                }
            }

            foreach (var (window, _) in windows)
            {
                window.Dispose();
            }
        });
    }

    /// <summary>
    ///     The window's line reads the device's own numbers, and stays quiet when it isn't working.
    /// </summary>
    [Test]
    public async Task TheFootnoteSaysWhereTheHeatIsGoing()
    {
        var server = Pair.Server;
        var client = Pair.Client;
        var site = await BuildSite();
        EntityUid deviceUid = default;

        await server.WaitPost(() => deviceUid = SpawnDevice(site, 3));
        await server.WaitRunTicks(TicksPerSecond);
        await RunWorking([deviceUid], TicksPerSecond * 3);
        await Pair.RunTicksSync(10);

        var serverGas = 0f;
        await server.WaitPost(() => serverGas = server.EntMan.GetComponent<KsPumpHeatComponent>(deviceUid).GasPower);

        await client.WaitAssertion(() =>
        {
            var clientDevice = client.EntMan.GetEntity(server.EntMan.GetNetEntity(deviceUid));
            var pumpHeat = client.EntMan.GetComponent<KsPumpHeatComponent>(clientDevice);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(pumpHeat.GasPower, Is.GreaterThan(0f), "The client never heard about the device's heat.");
                Assert.That(pumpHeat.GasPower, Is.EqualTo(serverGas).Within(serverGas * 0.5f), "The client's numbers aren't the server's.");
            }

            var footnote = new KsPumpHeatFootnote { Device = clientDevice };
            footnote.Refresh();
            var working = footnote.GetMessage();

            footnote.Device = null;
            footnote.Refresh();
            var idle = footnote.GetMessage();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(working, Is.Not.EqualTo(idle), "A working device's footnote reads as an idle one's.");
                Assert.That(working, Does.Contain("W"), "The footnote doesn't give a power.");
            }

            footnote.Dispose();
        });
    }
}
