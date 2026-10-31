#nullable enable
using System.Collections.Generic;
using Content.IntegrationTests.Fixtures;
using Content.Shared._KS14.Fluidmos;
using Content.Server._KS14.Fluidmos.Devices;
using Content.Server.Atmos.EntitySystems;
using Content.Server.NodeContainer.EntitySystems;
using Content.Server.NodeContainer.Nodes;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Piping.Binary.Components;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Power.EntitySystems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._KS14.Fluidmos;

/// <summary>
///     Whole refrigeration loops built from real devices on a real grid and run by atmos, not driven by hand.
/// </summary>
[TestOf(typeof(KsExpansionValveSystem))]
public sealed class KsFluidmosCycleTests : GameTest
{
    private static readonly ProtoId<ReagentPrototype> AmmoniaReagent = "Ammonia";

    private static readonly EntProtoId PipePrototype = "GasPipeStraight";
    private static readonly EntProtoId CornerPrototype = "GasPipeFourway";
    private static readonly EntProtoId PumpPrototype = "GasVolumePump";
    private static readonly EntProtoId RadiatorPrototype = "HeatExchanger";
    private static readonly EntProtoId ValvePrototype = "KsExpansionValve";
    private static readonly EntProtoId WallPrototype = "WallSolid";

    /// <summary>
    ///     An ammonia loop. The compressor and condenser stand outside, on lattice under the map's immutable 293 K air; the
    ///         evaporator is in a sealed room of ordinary air, which it should cool.
    /// </summary>
    /// <remarks>
    ///     Layout, x to the right and y up. Column x = 0 flows south: corner, pump, four condenser radiators, valve,
    ///         pipe, corner. Row y = -2 runs east to x = 6, which flows north through the room's wall, five evaporator
    ///         radiators and the other wall, and row y = 6 returns west to the pump. The room's floor is x 5..7, y 0..4.
    /// </remarks>
    [Test]
    public async Task AmmoniaLoopCoolsASealedRoom()
    {
        var server = Pair.Server;
        var entityManager = server.EntMan;
        var atmosphereSystem = server.System<AtmosphereSystem>();
        var nodeContainerSystem = server.System<NodeContainerSystem>();
        var mapSystem = server.System<SharedMapSystem>();
        var gameTiming = server.ResolveDependency<IGameTiming>();
        Assert.That(server.System<KsFluidmosPropertiesSystem>().TryGetProperties(AmmoniaReagent, out var ammonia));
        var testMap = await Pair.CreateTestMap();
        var grid = testMap.Grid;

        const float chargePressure = 500f;
        const float evaporatorPressure = 150f;
        const float compressorRate = 100f;
        var roomFloor = new List<Vector2i>();
        for (var x = 5; x <= 7; x++)
        for (var y = 0; y <= 4; y++)
            roomFloor.Add(new Vector2i(x, y));

        var nets = new List<GasMixture>();
        EntityUid pumpUid = default;
        EntityUid valveUid = default;
        var condenserUids = new List<EntityUid>();
        var evaporatorUids = new List<EntityUid>();

        await server.WaitPost(() =>
        {
            var air = new GasMixture(Atmospherics.CellVolume) { Temperature = Atmospherics.T20C };
            air.SetMoles(Gas.Oxygen, Atmospherics.OxygenMolesStandard);
            air.SetMoles(Gas.Nitrogen, Atmospherics.NitrogenMolesStandard);
            atmosphereSystem.SetMapAtmosphere(testMap.MapUid, space: false, air);

            var lattice = new Tile(server.ResolveDependency<ITileDefinitionManager>()["Lattice"].TileId);
            for (var x = -1; x <= 9; x++)
            for (var y = -3; y <= 7; y++)
            {
                var indices = new Vector2i(x, y);
                var inRoom = x >= 4 && x <= 8 && y >= -1 && y <= 5;
                mapSystem.SetTile(grid, indices, inRoom ? testMap.Tile.Tile : lattice);
            }

            EntityUid Spawn(EntProtoId prototype, int x, int y, Angle rotation)
            {
                return entityManager.SpawnAtPosition(prototype, mapSystem.GridTileToLocal(grid, grid.Comp, new Vector2i(x, y)), rotation);
            }

            var down = Angle.Zero;
            var up = Angle.FromDegrees(180);
            var across = Angle.FromDegrees(90);

            // The room's walls.
            for (var x = 4; x <= 8; x++)
            for (var y = -1; y <= 5; y++)
            {
                if (x == 4 || x == 8 || y == -1 || y == 5)
                    Spawn(WallPrototype, x, y, down);
            }

            // The high side, flowing south.
            Spawn(CornerPrototype, 0, 6, down);
            pumpUid = Spawn(PumpPrototype, 0, 5, down);
            for (var y = 4; y >= 1; y--)
                condenserUids.Add(Spawn(RadiatorPrototype, 0, y, down));
            valveUid = Spawn(ValvePrototype, 0, 0, down);
            Spawn(PipePrototype, 0, -1, down);
            Spawn(CornerPrototype, 0, -2, down);

            // The low side: across, up through the room, and back.
            for (var x = 1; x <= 5; x++)
                Spawn(PipePrototype, x, -2, across);
            Spawn(CornerPrototype, 6, -2, down);
            Spawn(PipePrototype, 6, -1, down);
            for (var y = 0; y <= 4; y++)
                evaporatorUids.Add(Spawn(RadiatorPrototype, 6, y, up));
            Spawn(PipePrototype, 6, 5, down);
            Spawn(CornerPrototype, 6, 6, down);
            for (var x = 1; x <= 5; x++)
                Spawn(PipePrototype, x, 6, across);

            server.System<SharedPowerReceiverSystem>().SetNeedsPower(pumpUid, false);
        });

        await server.WaitRunTicks(30);

        PipeNode Node(EntityUid uid, string name)
        {
            Assert.That(nodeContainerSystem.TryGetNode(uid, name, out PipeNode? node), $"{uid} has no {name} node.");
            return node!;
        }

        await server.WaitPost(() =>
        {
            // The room starts at ordinary air, and the loop is charged with ammonia vapor below its saturation pressure.
            foreach (var indices in roomFloor)
            {
                var tileAir = atmosphereSystem.GetTileMixture(grid.Owner, null, indices, excite: true);
                Assert.That(tileAir, Is.Not.Null, $"Room tile {indices} has no air.");
                tileAir!.Clear();
                tileAir.Temperature = Atmospherics.T20C;
                tileAir.SetMoles(Gas.Oxygen, Atmospherics.OxygenMolesStandard);
                tileAir.SetMoles(Gas.Nitrogen, Atmospherics.NitrogenMolesStandard);
            }

            // Every net, in flow order from the pump's discharge.
            nets.Add(Node(pumpUid, "outlet").Air);
            foreach (var uid in condenserUids)
                nets.Add(Node(uid, "outlet").Air);
            nets.Add(Node(valveUid, "outlet").Air);
            foreach (var uid in evaporatorUids)
                nets.Add(Node(uid, "outlet").Air);

            foreach (var net in nets)
            {
                net.Clear();
                net.Temperature = Atmospherics.T20C;
                net.AdjustMoles(Gas.Ammonia, chargePressure * net.Volume / (Atmospherics.R * Atmospherics.T20C));
            }

            // The loop is closed: the last evaporator feeds the pump.
            Assert.That(Node(evaporatorUids[^1], "outlet").Air, Is.SameAs(Node(pumpUid, "inlet").Air), "The evaporator doesn't return to the pump.");
            Assert.That(Node(condenserUids[^1], "outlet").Air, Is.SameAs(Node(valveUid, "inlet").Air), "The condenser doesn't feed the valve.");
            Assert.That(Node(valveUid, "outlet").Air, Is.SameAs(Node(evaporatorUids[0], "inlet").Air), "The valve doesn't feed the evaporator.");

            var pump = entityManager.GetComponent<GasVolumePumpComponent>(pumpUid);
            pump.Enabled = true;
            pump.TransferRate = compressorRate;

            entityManager.EventBus.RaiseLocalEvent(valveUid, new Content.Shared._KS14.Fluidmos.Devices.KsExpansionValveSetAutomaticMessage(true));
            entityManager.EventBus.RaiseLocalEvent(valveUid, new Content.Shared._KS14.Fluidmos.Devices.KsExpansionValveSetPressureMessage(evaporatorPressure));
        });

        float RoomTemperature()
        {
            var sum = 0f;
            foreach (var indices in roomFloor)
                sum += atmosphereSystem.GetTileMixture(grid.Owner, null, indices)!.Temperature;
            return sum / roomFloor.Count;
        }

        float AmmoniaMoles()
        {
            var moles = 0f;
            foreach (var net in nets)
            {
                var condensedUnits = (net.Fluid?.LiquidUnits ?? 0f) + (net.Fluid?.SolidUnits ?? 0f);
                moles += net.GetMoles(Gas.Ammonia) + condensedUnits * ammonia!.MolesPerUnit;
            }

            return moles;
        }

        string Describe()
        {
            var text = "";
            foreach (var net in nets)
                text += $"[{net.Pressure:0} kPa {net.Temperature:0.0} K {net.Fluid?.LiquidUnits ?? 0f:0}u] ";
            return text;
        }

        // Ten minutes, sampled every fifteen seconds; the last third is the steady state the loop settles into.
        const int samples = 40;
        const int settledFrom = 2 * samples / 3;
        var ticksPerSample = (int)(15f * gameTiming.TickRate);
        var startMoles = 0f;
        var endMoles = 0f;
        var endRoom = 0f;
        var settledFlow = 0f;
        var settledEvaporatorPressure = 0f;
        var settledCondenserPressure = 0f;

        await server.WaitPost(() => startMoles = AmmoniaMoles());
        for (var sample = 1; sample <= samples; sample++)
        {
            await server.WaitRunTicks(ticksPerSample);
            await server.WaitPost(() =>
            {
                var valve = entityManager.GetComponent<KsExpansionValveComponent>(valveUid);
                endRoom = RoomTemperature();
                endMoles = AmmoniaMoles();
                TestContext.Out.WriteLine($"t={sample * 15,4}s room {endRoom:0.0} K | valve {valve.Opening:0.00}, {valve.FlowRate:0} u/s | nets {Describe()}");

                if (sample < settledFrom)
                    return;

                const float settledCount = samples - settledFrom + 1;
                settledFlow += valve.FlowRate / settledCount;
                settledEvaporatorPressure += Node(valveUid, "outlet").Air.Pressure / settledCount;
                settledCondenserPressure += Node(condenserUids[0], "outlet").Air.Pressure / settledCount;
            });
        }

        var evaporatingTemperature = KsPhaseMath.BoilingPointAt(ammonia!.VaporizationEntropy, ammonia.BoilingPoint, evaporatorPressure);
        var ambientSaturationPressure = KsPhaseMath.SaturationPressure(ammonia.VaporizationEntropy, ammonia.BoilingPoint, Atmospherics.T20C);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(endMoles, Is.EqualTo(startMoles).Within(1e-3f * startMoles), "The loop gained or lost refrigerant.");
            Assert.That(settledFlow, Is.GreaterThan(0f), "No liquid reached the valve once the loop settled: it's running on vapor alone.");
            Assert.That(settledCondenserPressure, Is.GreaterThan(ambientSaturationPressure),
                "The condenser condenses at its surroundings' temperature, leaving its liquid no margin to reach the valve.");
            Assert.That(settledEvaporatorPressure, Is.EqualTo(evaporatorPressure).Within(0.1f * evaporatorPressure),
                "The valve didn't hold its evaporator pressure.");
            Assert.That(endRoom, Is.LessThan(0.5f * (Atmospherics.T20C + evaporatingTemperature)),
                "The sealed room didn't cool even halfway to the refrigerant's evaporating temperature.");
        }
    }
}
