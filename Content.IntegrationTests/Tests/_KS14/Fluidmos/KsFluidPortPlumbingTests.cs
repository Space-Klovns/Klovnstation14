#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.Fluidmos;
using Content.Server._KS14.Fluidmos.Devices;
using Content.Server._Starlight.Plumbing.Nodes;
using Content.Server.NodeContainer.EntitySystems;
using Content.Server.NodeContainer.Nodes;
using Content.Shared._KS14.Fluidmos;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Content.Shared.NodeContainer;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.Fluidmos;

/// <summary>
///     The fluid port is where Fluidmos meets plumbing: injecting, its tank fills from the plumbing network and feeds the
///         pipe; extracting, it fills from the pipe and offers itself to the network. Its mode gates which, so it never
///         pulls from itself.
/// </summary>
[TestOf(typeof(KsFluidPortSystem))]
public sealed class KsFluidPortPlumbingTests : GameTest
{
    private static readonly ProtoId<ReagentPrototype> WaterReagent = "Water";
    private static readonly KsFluidReagentKey WaterKey = new(WaterReagent, null);
    private static readonly ProtoId<ReagentPrototype> BloodReagent = "Blood";

    private static readonly EntProtoId FluidPortPrototype = "KsFluidPort";
    private static readonly EntProtoId PlumbingInputPrototype = "PlumbingInput";
    private static readonly EntProtoId PlumbingTankPrototype = "PlumbingTank";

    private const string PortSolution = "fluidport";

    /// <summary>
    ///     A port at the origin, and a plumbing machine on the tile north of it, where the port's plumbing connects. A tank
    ///         is turned round, so its inlet faces the port.
    /// </summary>
    private async Task<(EntityUid Port, EntityUid Machine)> SpawnPortAndMachine(EntProtoId machinePrototype, KsFluidPortMode mode)
    {
        var server = Pair.Server;
        var testMap = await Pair.CreateTestMap();
        EntityUid portUid = default;
        EntityUid machineUid = default;

        await server.WaitPost(() =>
        {
            var mapSystem = server.System<SharedMapSystem>();
            var north = testMap.Tile.GridIndices + Vector2i.Up;
            mapSystem.SetTile(testMap.Grid, north, testMap.Tile.Tile);

            portUid = server.EntMan.SpawnEntity(FluidPortPrototype, testMap.GridCoords);
            var machineCoordinates = mapSystem.GridTileToLocal(testMap.Grid, testMap.Grid.Comp, north);
            machineUid = server.EntMan.SpawnEntity(machinePrototype, machineCoordinates);
            if (machinePrototype == PlumbingTankPrototype)
                server.System<SharedTransformSystem>().SetLocalRotation(machineUid, Angle.FromDegrees(180));

            var port = server.EntMan.GetComponent<KsFluidPortComponent>(portUid);
            server.System<KsFluidPortSystem>().SetMode((portUid, port), mode);
        });

        await server.WaitRunTicks(5);
        return (portUid, machineUid);
    }

    private PipeNode Pipe(EntityUid portUid)
    {
        Assert.That(Pair.Server.System<NodeContainerSystem>().TryGetNode(portUid, "pipe", out PipeNode? pipe), "The fluid port has no pipe node.");
        pipe!.Air.Temperature = Atmospherics.T20C;
        return pipe;
    }

    private Solution PortTank(EntityUid portUid)
    {
        Assert.That(Pair.Server.System<SharedSolutionContainerSystem>().TryGetSolution(portUid, PortSolution, out _, out var solution));
        return solution!;
    }

    private Solution MachineSolution(EntityUid machineUid, string name)
    {
        Assert.That(Pair.Server.System<SharedSolutionContainerSystem>().TryGetSolution(machineUid, name, out _, out var solution));
        return solution!;
    }

    private static void UpdatePort(IEntityManager entityManager, EntityUid portUid, float deltaTime)
    {
        var ev = new AtmosDeviceUpdateEvent(deltaTime, null, null);
        entityManager.EventBus.RaiseLocalEvent(portUid, ref ev);
    }

    /// <summary>
    ///     Long enough for every plumbing device to have pulled at least once.
    /// </summary>
    private async Task RunPlumbing()
    {
        var server = Pair.Server;
        await server.WaitRunTicks((int)(5f * server.ResolveDependency<IGameTiming>().TickRate));
    }

    [Test]
    public async Task PortHasBothItsPipeAndItsPlumbingNode()
    {
        var server = Pair.Server;
        var testMap = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var portUid = server.EntMan.SpawnEntity(FluidPortPrototype, testMap.GridCoords);
            var nodes = server.EntMan.GetComponent<NodeContainerComponent>(portUid).Nodes;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(nodes.TryGetValue("pipe", out var pipe) && pipe is PipeNode, "The fluid port lost its pipe node.");
                Assert.That(nodes.TryGetValue("plumbing", out var plumbing) && plumbing is PlumbingNode, "The fluid port has no plumbing node.");
            }
        });
    }

    [Test]
    public async Task InjectingPortFeedsPlumbingIntoThePipe()
    {
        var server = Pair.Server;
        var (portUid, inputUid) = await SpawnPortAndMachine(PlumbingInputPrototype, KsFluidPortMode.Inject);

        await server.WaitPost(() =>
        {
            MachineSolution(inputUid, "input").AddReagent(WaterReagent, FixedPoint2.New(50));
            Pipe(portUid).Air.AdjustMoles(Gas.Nitrogen, 10f);
        });
        await RunPlumbing();

        await server.WaitAssertion(() =>
        {
            Assert.That((float)PortTank(portUid).GetTotalPrototypeQuantity(WaterReagent), Is.GreaterThan(0f),
                "The injecting port didn't pull from the plumbing network.");

            var pipe = Pipe(portUid);
            UpdatePort(server.EntMan, portUid, deltaTime: 10f);
            Assert.That(pipe.Air.Fluid?.TryGetAmount(WaterKey, out var water) == true && water.Liquid > 0f,
                "Plumbed water didn't reach the pipe.");
        });
    }

    [Test]
    public async Task ExtractingPortIsPulledFromByPlumbing()
    {
        var server = Pair.Server;
        var (portUid, tankUid) = await SpawnPortAndMachine(PlumbingTankPrototype, KsFluidPortMode.Extract);

        await server.WaitPost(() =>
        {
            var pipe = Pipe(portUid);
            pipe.Air.AdjustMoles(Gas.Nitrogen, 10f);
            pipe.Air.Fluid = new KsFluidMixture();
            pipe.Air.Fluid.AddLiquid(WaterKey, 100f);
            UpdatePort(server.EntMan, portUid, deltaTime: 5f);
        });
        await RunPlumbing();

        await server.WaitAssertion(() =>
        {
            Assert.That((float)MachineSolution(tankUid, "tank").GetTotalPrototypeQuantity(WaterReagent), Is.GreaterThan(0f),
                "Plumbing couldn't pull the pipe's liquid from an extracting port.");
        });
    }

    [Test]
    public async Task ExtractingPortDrainsThePipeOnlyUntilItsTankIsFull()
    {
        var server = Pair.Server;
        var testMap = await Pair.CreateTestMap();
        EntityUid portUid = default;
        await server.WaitPost(() => portUid = server.EntMan.SpawnEntity(FluidPortPrototype, testMap.GridCoords));
        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            var port = server.EntMan.GetComponent<KsFluidPortComponent>(portUid);
            server.System<KsFluidPortSystem>().SetMode((portUid, port), KsFluidPortMode.Extract);

            var tank = PortTank(portUid);
            var pipeUnits = 3f * (float)tank.MaxVolume;
            var pipe = Pipe(portUid);
            pipe.Air.AdjustMoles(Gas.Nitrogen, 10f);
            pipe.Air.Fluid = new KsFluidMixture();
            pipe.Air.Fluid.AddLiquid(WaterKey, pipeUnits);

            // Far longer than filling the tank takes at the port's rate.
            for (var i = 0; i < 10; i++)
            {
                UpdatePort(server.EntMan, portUid, deltaTime: (float)tank.MaxVolume / port.Rate);
            }

            using (Assert.EnterMultipleScope())
            {
                Assert.That(tank.Volume, Is.EqualTo(tank.MaxVolume), "The tank didn't fill.");
                Assert.That(pipe.Air.Fluid!.LiquidUnits, Is.EqualTo(pipeUnits - (float)tank.MaxVolume).Within(0.05f),
                    "The pipe drained past what the tank could hold.");
            }
        });
    }

    [Test]
    public async Task InjectingPortCannotBePulledFrom()
    {
        var server = Pair.Server;
        var (portUid, tankUid) = await SpawnPortAndMachine(PlumbingTankPrototype, KsFluidPortMode.Inject);

        await server.WaitPost(() =>
        {
            // A full pipe, so the port keeps its tank rather than injecting it.
            Pipe(portUid).Air.AdjustMoles(Gas.Nitrogen, 2f * Atmospherics.MaxOutputPressure * NodeVolume(portUid) / (Atmospherics.R * Atmospherics.T20C));
            PortTank(portUid).AddReagent(WaterReagent, FixedPoint2.New(50));
        });
        await RunPlumbing();

        await server.WaitAssertion(() =>
        {
            Assert.That((float)MachineSolution(tankUid, "tank").GetTotalPrototypeQuantity(WaterReagent), Is.Zero,
                "Plumbing pulled from an injecting port, which would let it pull from itself.");
        });
    }

    [Test]
    public async Task ExtractingPortRefusesInboundPulls()
    {
        var server = Pair.Server;
        var (portUid, inputUid) = await SpawnPortAndMachine(PlumbingInputPrototype, KsFluidPortMode.Extract);

        await server.WaitPost(() =>
        {
            MachineSolution(inputUid, "input").AddReagent(WaterReagent, FixedPoint2.New(50));
            Pipe(portUid).Air.AdjustMoles(Gas.Nitrogen, 10f);
        });
        await RunPlumbing();

        await server.WaitAssertion(() =>
        {
            Assert.That((float)PortTank(portUid).GetTotalPrototypeQuantity(WaterReagent), Is.Zero,
                "An extracting port pulled from the plumbing network.");
        });
    }

    /// <summary>
    ///     Plumbing tanks are plain inlets and outlets on the duct network, with no pipe of their own: a pipe reaches one
    ///         only through a port. An extracting port feeding a tank keeps draining its pipe past its own tank's size,
    ///         and a second, injecting port drawing on that tank puts the liquid back into another pipe.
    /// </summary>
    [Test]
    public async Task PipeToPlumbingTankToPipeRoundTrips()
    {
        var server = Pair.Server;
        var testMap = await Pair.CreateTestMap();
        var propertiesSystem = server.System<KsFluidmosPropertiesSystem>();
        EntityUid extractingUid = default;
        EntityUid tankUid = default;
        EntityUid injectingUid = default;

        await server.WaitPost(() =>
        {
            var air = new GasMixture(Atmospherics.CellVolume) { Temperature = Atmospherics.T20C };
            air.SetMoles(Gas.Oxygen, Atmospherics.OxygenMolesStandard);
            air.SetMoles(Gas.Nitrogen, Atmospherics.NitrogenMolesStandard);
            server.System<Content.Server.Atmos.EntitySystems.AtmosphereSystem>().SetMapAtmosphere(testMap.MapUid, space: false, air);
            server.EntMan.EnsureComponent<GridAtmosphereComponent>(testMap.Grid.Owner);

            var mapSystem = server.System<SharedMapSystem>();
            var transformSystem = server.System<SharedTransformSystem>();
            var tankTile = testMap.Tile.GridIndices + Vector2i.Up;
            var injectingTile = tankTile + Vector2i.Up;
            mapSystem.SetTile(testMap.Grid, tankTile, testMap.Tile.Tile);
            mapSystem.SetTile(testMap.Grid, injectingTile, testMap.Tile.Tile);

            extractingUid = server.EntMan.SpawnEntity(FluidPortPrototype, testMap.GridCoords);
            tankUid = server.EntMan.SpawnEntity(PlumbingTankPrototype, mapSystem.GridTileToLocal(testMap.Grid, testMap.Grid.Comp, tankTile));
            injectingUid = server.EntMan.SpawnEntity(FluidPortPrototype, mapSystem.GridTileToLocal(testMap.Grid, testMap.Grid.Comp, injectingTile));
            // Turned so the tank's inlet faces the extracting port and the injecting port's plumbing faces the tank.
            transformSystem.SetLocalRotation(tankUid, Angle.FromDegrees(180));
            transformSystem.SetLocalRotation(injectingUid, Angle.FromDegrees(180));

            var fluidPortSystem = server.System<KsFluidPortSystem>();
            fluidPortSystem.SetMode((extractingUid, server.EntMan.GetComponent<KsFluidPortComponent>(extractingUid)), KsFluidPortMode.Extract);
            fluidPortSystem.SetMode((injectingUid, server.EntMan.GetComponent<KsFluidPortComponent>(injectingUid)), KsFluidPortMode.Inject);
        });
        await server.WaitRunTicks(5);

        float startUnits = 0f;
        await server.WaitPost(() =>
        {
            var tankCapacity = (float)PortTank(extractingUid).MaxVolume;
            startUnits = 4f * tankCapacity;
            var source = Pipe(extractingUid);
            source.Air.AdjustMoles(Gas.Nitrogen, Atmospherics.OneAtmosphere * source.Air.Volume / (Atmospherics.R * Atmospherics.T20C));
            source.Air.Fluid = new KsFluidMixture();
            source.Air.Fluid.AddLiquid(WaterKey, startUnits);
            var sink = Pipe(injectingUid);
            sink.Air.AdjustMoles(Gas.Nitrogen, Atmospherics.OneAtmosphere * sink.Air.Volume / (Atmospherics.R * Atmospherics.T20C));
        });

        // Long enough to move well past one port tank's worth at the port's rate.
        var port = server.EntMan.GetComponent<KsFluidPortComponent>(extractingUid);
        var seconds = 3f * (float)PortTank(extractingUid).MaxVolume / port.Rate;
        await server.WaitRunTicks((int)(seconds * server.ResolveDependency<IGameTiming>().TickRate));

        await server.WaitAssertion(() =>
        {
            Assert.That(propertiesSystem.TryGetProperties(WaterReagent, out var water));
            float PipeWater(EntityUid portUid)
            {
                var pipe = Pipe(portUid);
                var liquid = pipe.Air.Fluid?.TryGetAmount(WaterKey, out var amount) == true ? amount.Liquid + amount.Solid : 0f;
                return liquid + pipe.Air.GetMoles(Gas.WaterVapor) / water!.MolesPerUnit;
            }

            var drained = startUnits - PipeWater(extractingUid);
            var arrived = PipeWater(injectingUid);
            var held = (float)PortTank(extractingUid).GetTotalPrototypeQuantity(WaterReagent)
                       + (float)MachineSolution(tankUid, "tank").GetTotalPrototypeQuantity(WaterReagent)
                       + (float)PortTank(injectingUid).GetTotalPrototypeQuantity(WaterReagent);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(drained, Is.GreaterThan((float)PortTank(extractingUid).MaxVolume),
                    "The plumbing tank didn't keep the extracting port draining past its own tank.");
                Assert.That(arrived, Is.GreaterThan(0f), "The tank's liquid never reached the second pipe.");
                Assert.That(PipeWater(extractingUid) + held + arrived, Is.EqualTo(startUnits).Within(1f),
                    "Water was made or lost between the pipes.");
            }
        });
    }

    [Test]
    public async Task BloodKeepsItsDnaThroughPlumbingAndThePipe()
    {
        var server = Pair.Server;
        var (portUid, inputUid) = await SpawnPortAndMachine(PlumbingInputPrototype, KsFluidPortMode.Inject);
        var blood = new ReagentId(BloodReagent, new List<ReagentData> { new DnaData { DNA = "GATTACA" } });
        const float units = 10f;
        EntityUid consumerUid = default;

        // Plumbing -> port -> pipe.
        await server.WaitPost(() =>
        {
            MachineSolution(inputUid, "input").AddReagent(blood, FixedPoint2.New(units));
            Pipe(portUid).Air.AdjustMoles(Gas.Nitrogen, 10f);
        });
        await RunPlumbing();

        await server.WaitPost(() =>
        {
            UpdatePort(server.EntMan, portUid, deltaTime: 10f);
            Assert.That(Pipe(portUid).Air.Fluid?.TryGetAmount(KsFluidReagentKey.From(blood), out var piped) == true && piped.Liquid >= units - 0.02f,
                "Plumbed blood reached the pipe without its DNA.");

            // Pipe -> port -> plumbing: a tank takes the input's place, and the port turns round.
            server.EntMan.DeleteEntity(inputUid);
            var northCoordinates = server.EntMan.GetComponent<TransformComponent>(portUid).Coordinates.Offset(Vector2i.Up);
            consumerUid = server.EntMan.SpawnEntity(PlumbingTankPrototype, northCoordinates);
            server.System<SharedTransformSystem>().SetLocalRotation(consumerUid, Angle.FromDegrees(180));
            server.System<KsFluidPortSystem>().SetMode((portUid, server.EntMan.GetComponent<KsFluidPortComponent>(portUid)), KsFluidPortMode.Extract);
            UpdatePort(server.EntMan, portUid, deltaTime: 10f);
        });
        await RunPlumbing();

        await server.WaitAssertion(() =>
        {
            var consumer = MachineSolution(consumerUid, "tank");
            using (Assert.EnterMultipleScope())
            {
                Assert.That((float)consumer.GetReagentQuantity(blood), Is.EqualTo(units).Within(0.02f),
                    "Blood that crossed plumbing, the pipe and plumbing again lost its DNA.");
                Assert.That((float)consumer.GetReagentQuantity(new ReagentId(BloodReagent, null)), Is.Zero, "Some of the blood was made generic.");
            }
        });
    }

    private float NodeVolume(EntityUid portUid)
    {
        return Pipe(portUid).Air.Volume;
    }
}
