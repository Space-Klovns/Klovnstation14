#nullable enable
using System;
using System.Collections.Generic;
using Content.IntegrationTests.Fixtures;
using Content.Shared._KS14.CCVar;
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
using Robust.UnitTesting;

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

    /// <summary>
    ///     A nitrous oxide loop in space. Its saturation pressure at room temperature is past a pressure pump's cap, and its
    ///         critical temperature barely above it, so its condenser radiates to space rather than to a room.
    /// </summary>
    /// <remarks>
    ///     A radiator in space condenses two ways, either enough alone: by its own walls' latent transfer, and by
    ///         radiating the vapor past its dew point for the next net's phase step to condense.
    /// </remarks>
    [Test]
    public async Task NitrousOxideLoopInSpaceCoolsASealedRoom()
    {
        var server = Pair.Server;
        Assert.That(server.System<KsFluidmosPropertiesSystem>().TryGetProperties(NitrousOxideReagent, out var nitrousOxide));

        const float evaporatorPressure = 300f;
        var loop = await BuildLoop(Gas.NitrousOxide,
            nitrousOxide!,
            condenserCount: 6,
            sealedCondenserRoom: false,
            sealedPumpRoom: false,
            chargePressure: 1500f,
            evaporatorPressure: evaporatorPressure,
            compressorRate: 100f);

        var startMoles = 0f;
        await server.WaitPost(() => startMoles = loop.RefrigerantMoles());
        var endRoom = await loop.Run(samples: 40, secondsPerSample: 15f);

        var evaporatingTemperature = KsPhaseMath.BoilingPointAt(nitrousOxide!.VaporizationEntropy, nitrousOxide.BoilingPoint, evaporatorPressure);
        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(loop.RefrigerantMoles(), Is.EqualTo(startMoles).Within(1e-3f * startMoles), "The loop gained or lost refrigerant.");
                Assert.That(loop.SettledFlow, Is.GreaterThan(0f), "No liquid reached the valve once the loop settled: it's running on vapor alone.");
                Assert.That(endRoom, Is.LessThan(0.5f * (Atmospherics.T20C + evaporatingTemperature)),
                    "The sealed room didn't cool even halfway to the refrigerant's evaporating temperature.");
            }
        });
    }

    /// <summary>
    ///     The first law across a whole loop: with the pump, the condenser and the evaporator each in a sealed room, the
    ///         condenser's room gains what the evaporator's room loses plus the pump's work, less what the refrigerant
    ///         itself kept. The pump's room takes the share of that work its gas doesn't keep, which gives the work.
    /// </summary>
    /// <remarks>
    ///     Throttled vapor's Joule–Thomson change is the one place atmos makes or loses energy by design, a few percent
    ///         of what the refrigerant carries, hence the tolerance. Everything else (radiator walls, the phase step,
    ///         flash, the pump) must conserve it.
    /// </remarks>
    [Test]
    public async Task LoopEnergyBalances()
    {
        var server = Pair.Server;
        var configurationManager = server.ResolveDependency<Robust.Shared.Configuration.IConfigurationManager>();
        Assert.That(server.System<KsFluidmosPropertiesSystem>().TryGetProperties(AmmoniaReagent, out var ammonia));

        var loop = await BuildLoop(Gas.Ammonia,
            ammonia!,
            condenserCount: 4,
            sealedCondenserRoom: true,
            sealedPumpRoom: true,
            chargePressure: 500f,
            evaporatorPressure: 150f,
            compressorRate: 100f);

        float evaporatorRoomEnergy = 0f, condenserRoomEnergy = 0f, pumpRoomEnergy = 0f, loopEnergy = 0f;
        await server.WaitPost(() =>
        {
            evaporatorRoomEnergy = loop.RoomEnergy(loop.EvaporatorRoom);
            condenserRoomEnergy = loop.RoomEnergy(loop.CondenserRoom);
            pumpRoomEnergy = loop.RoomEnergy(loop.PumpRoom);
            loopEnergy = loop.LoopEnergy();
        });

        await loop.Run(samples: 12, secondsPerSample: 15f);

        await server.WaitAssertion(() =>
        {
            var keptFraction = Math.Clamp(configurationManager.GetCVar(KsCCVars.AtmosPumpCompressionHeatFraction), 0f, 1f);
            Assert.That(keptFraction, Is.LessThan(1f), "The pump keeps all its heat, so its room can't measure its work.");

            var absorbed = evaporatorRoomEnergy - loop.RoomEnergy(loop.EvaporatorRoom);
            var rejected = loop.RoomEnergy(loop.CondenserRoom) - condenserRoomEnergy;
            var shed = loop.RoomEnergy(loop.PumpRoom) - pumpRoomEnergy;
            var work = shed / (1f - keptFraction);
            var stored = loop.LoopEnergy() - loopEnergy;
            var imbalance = absorbed + work - rejected - shed - stored;
            TestContext.Out.WriteLine($"absorbed {absorbed:0} J, work {work:0} J, rejected {rejected:0} J, shed {shed:0} J, " +
                                      $"stored {stored:0} J, imbalance {imbalance:0} J");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(absorbed, Is.GreaterThan(0f), "The evaporator's room didn't lose heat.");
                Assert.That(work, Is.GreaterThan(0f), "The pump did no work.");
                // Against what passes through the refrigerant: the work its gas keeps, not what the pump sheds beside it.
                Assert.That(MathF.Abs(imbalance), Is.LessThan(0.1f * (absorbed + keptFraction * work)),
                    "Energy into the loop (its evaporator's heat and its pump's work) isn't what came out of it and what it kept.");
            }
        });
    }

    /// <summary>
    ///     A Linde loop: nitrogen compressed, cooled to ambient, cooled further by its own returning gas, and throttled,
    ///         chills itself a little more each pass by Joule–Thomson until part of it liquefies.
    /// </summary>
    /// <remarks>
    ///     <para>No device moves heat from one pipe to another, so the counterflow exchanger is a column of one-tile sealed
    ///         chambers of plasma, each holding a radiator from either stream: the high-pressure stream flows down through
    ///         them north to south, and the returning stream snakes up through them east and west. One well-mixed chamber
    ///         would only bring both streams to its own temperature; a column of them is what lets the cold end run away
    ///         from the warm one.</para>
    ///
    ///     <para>Layout, x to the right and y up, for n chambers at y = 1, 3, ... 2n - 1, the warmest on top. Column x = 0 flows
    ///         south: corner (y = 2n + 6), the compressor (2n + 5), four aftercooler radiators on lattice under the map's
    ///         293 K air, the chambers, the valve (-1) and a corner (-2). The return runs east to x = 2, up into the coldest
    ///         chamber, snakes up through each through x = -2 and 2, and back across the top.</para>
    /// </remarks>
    [Test]
    public async Task LindeLoopLiquefiesNitrogen()
    {
        var server = Pair.Server;
        var entityManager = server.EntMan;
        var atmosphereSystem = server.System<AtmosphereSystem>();
        var nodeContainerSystem = server.System<NodeContainerSystem>();
        var mapSystem = server.System<SharedMapSystem>();
        var gameTiming = server.ResolveDependency<IGameTiming>();
        Assert.That(server.System<KsFluidmosPropertiesSystem>().TryGetProperties(NitrogenReagent, out var nitrogen));
        var testMap = await Pair.CreateTestMap();
        var grid = testMap.Grid;

        const int chambers = 8;
        const float chargePressure = 3000f;
        const float throttledPressure = 500f;
        const float compressorRate = 100f;
        var top = 2 * chambers - 1;

        var chamberFloor = new List<Vector2i>();
        for (var i = 0; i < chambers; i++)
            chamberFloor.Add(new Vector2i(0, 2 * i + 1));

        EntityUid pumpUid = default;
        EntityUid valveUid = default;
        var deviceUids = new List<EntityUid>();

        await server.WaitPost(() =>
        {
            var air = new GasMixture(Atmospherics.CellVolume) { Temperature = Atmospherics.T20C };
            air.SetMoles(Gas.Oxygen, Atmospherics.OxygenMolesStandard);
            air.SetMoles(Gas.Nitrogen, Atmospherics.NitrogenMolesStandard);
            atmosphereSystem.SetMapAtmosphere(testMap.MapUid, space: false, air);

            var walls = new HashSet<Vector2i>();
            foreach (var chamber in chamberFloor)
            {
                for (var x = -1; x <= 1; x++)
                for (var y = chamber.Y - 1; y <= chamber.Y + 1; y++)
                {
                    if (x != 0 || y != chamber.Y)
                        walls.Add(new Vector2i(x, y));
                }
            }

            var lattice = new Tile(server.ResolveDependency<ITileDefinitionManager>()["Lattice"].TileId);
            for (var x = -3; x <= 3; x++)
            for (var y = -3; y <= top + 8; y++)
            {
                var indices = new Vector2i(x, y);
                var floor = walls.Contains(indices) || chamberFloor.Contains(indices);
                mapSystem.SetTile(grid, indices, floor ? testMap.Tile.Tile : lattice);
            }

            EntityUid Spawn(EntProtoId prototype, int x, int y, Angle rotation)
            {
                var uid = entityManager.SpawnAtPosition(prototype, mapSystem.GridTileToLocal(grid, grid.Comp, new Vector2i(x, y)), rotation);
                deviceUids.Add(uid);
                return uid;
            }

            var down = Angle.Zero;
            var across = Angle.FromDegrees(90);

            foreach (var wall in walls)
                entityManager.SpawnAtPosition(WallPrototype, mapSystem.GridTileToLocal(grid, grid.Comp, wall), down);

            // The high-pressure side, flowing south: compressor, aftercooler, the chambers' radiators, the valve.
            Spawn(CornerPrototype, 0, top + 7, down);
            pumpUid = Spawn(PumpPrototype, 0, top + 6, down);
            for (var y = top + 5; y >= top + 2; y--)
                Spawn(RadiatorPrototype, 0, y, down);
            for (var y = top + 1; y >= 0; y--)
                Spawn(y % 2 == 1 ? RadiatorPrototype : PipePrototype, 0, y, down);
            valveUid = Spawn(ValvePrototype, 0, -1, down);
            Spawn(CornerPrototype, 0, -2, down);

            // The return: across and up into the coldest chamber, then up through each, alternating sides.
            Spawn(PipePrototype, 1, -2, across);
            Spawn(CornerPrototype, 2, -2, down);
            Spawn(PipePrototype, 2, -1, down);
            Spawn(PipePrototype, 2, 0, down);
            for (var i = 0; i < chambers; i++)
            {
                var y = 2 * i + 1;
                // Entered from the east on even steps, from the west on odd ones; leaves by the other side.
                var exitSide = i % 2 == 0 ? -1 : 1;
                Spawn(PipePrototype, -exitSide, y, across);
                Spawn(RadiatorPrototype, 0, y, across);
                Spawn(PipePrototype, exitSide, y, across);
                Spawn(CornerPrototype, 2 * exitSide, y, down);
                if (i == 0)
                    Spawn(CornerPrototype, 2, y, down);

                var riseTo = i == chambers - 1 ? top + 7 : y + 2;
                for (var rise = y + 1; rise < riseTo; rise++)
                    Spawn(PipePrototype, 2 * exitSide, rise, down);
                Spawn(CornerPrototype, 2 * exitSide, riseTo, down);
            }

            Spawn(PipePrototype, 1, top + 7, across);
            server.System<SharedPowerReceiverSystem>().SetNeedsPower(pumpUid, false);
        });

        await server.WaitRunTicks(30);

        var nets = new List<GasMixture>();
        await server.WaitPost(() =>
        {
            foreach (var chamber in chamberFloor)
            {
                var tileAir = atmosphereSystem.GetTileMixture(grid.Owner, null, chamber, excite: true);
                Assert.That(tileAir, Is.Not.Null, $"Chamber {chamber} has no air.");
                tileAir!.Clear();
                tileAir.Temperature = Atmospherics.T20C;
                tileAir.SetMoles(Gas.Plasma, Atmospherics.OneAtmosphere * tileAir.Volume / (Atmospherics.R * Atmospherics.T20C));
            }

            foreach (var uid in deviceUids)
            {
                if (!entityManager.TryGetComponent(uid, out Content.Shared.NodeContainer.NodeContainerComponent? nodeContainer))
                    continue;

                foreach (var node in nodeContainer.Nodes.Values)
                {
                    // By reference: a mixture's equality compares its contents, and every net starts out the same.
                    if (node is PipeNode { Air: { } nodeAir } && !nets.Exists(net => ReferenceEquals(net, nodeAir)))
                        nets.Add(nodeAir);
                }
            }

            foreach (var net in nets)
            {
                net.Clear();
                net.Temperature = Atmospherics.T20C;
                net.AdjustMoles(Gas.Nitrogen, chargePressure * net.Volume / (Atmospherics.R * Atmospherics.T20C));
            }

            Assert.That(nodeContainerSystem.TryGetNode(pumpUid, "inlet", out PipeNode? pumpInlet));
            Assert.That(nodeContainerSystem.TryGetNode(valveUid, "outlet", out PipeNode? valveOutlet));
            Assert.That(nets, Has.Count.EqualTo(4 + 2 * chambers + 2), "The loop isn't one closed circuit of the expected nets.");

            var pump = entityManager.GetComponent<GasVolumePumpComponent>(pumpUid);
            pump.Enabled = true;
            pump.TransferRate = compressorRate;

            entityManager.EventBus.RaiseLocalEvent(valveUid, new Content.Shared._KS14.Fluidmos.Devices.KsExpansionValveSetAutomaticMessage(true));
            entityManager.EventBus.RaiseLocalEvent(valveUid, new Content.Shared._KS14.Fluidmos.Devices.KsExpansionValveSetPressureMessage(throttledPressure));
        });

        GasMixture ValveOutlet()
        {
            nodeContainerSystem.TryGetNode(valveUid, "outlet", out PipeNode? node);
            return node!.Air;
        }

        GasMixture ValveInlet()
        {
            nodeContainerSystem.TryGetNode(valveUid, "inlet", out PipeNode? node);
            return node!.Air;
        }

        float NitrogenMoles()
        {
            var moles = 0f;
            foreach (var net in nets)
            {
                var condensedUnits = (net.Fluid?.LiquidUnits ?? 0f) + (net.Fluid?.SolidUnits ?? 0f);
                moles += net.GetMoles(Gas.Nitrogen) + condensedUnits * nitrogen!.MolesPerUnit;
            }

            return moles;
        }

        float LiquidUnits()
        {
            var units = 0f;
            foreach (var net in nets)
                units += net.Fluid?.LiquidUnits ?? 0f;
            return units;
        }

        var startMoles = 0f;
        await server.WaitPost(() => startMoles = NitrogenMoles());

        // An hour, in minutes.
        var ticksPerSample = (int)(60f * gameTiming.TickRate);
        for (var minute = 1; minute <= 60; minute++)
        {
            await server.WaitRunTicks(ticksPerSample);
            await server.WaitPost(() =>
            {
                var chamberText = "";
                foreach (var chamber in chamberFloor)
                    chamberText += $"{atmosphereSystem.GetTileMixture(grid.Owner, null, chamber)!.Temperature:0} ";
                var valve = entityManager.GetComponent<KsExpansionValveComponent>(valveUid);
                TestContext.Out.WriteLine($"t={minute,2} min | valve in {ValveInlet().Pressure:0} kPa {ValveInlet().Temperature:0.0} K, " +
                                          $"out {ValveOutlet().Pressure:0} kPa {ValveOutlet().Temperature:0.0} K, opening {valve.Opening:0.00} | " +
                                          $"chambers (coldest first) {chamberText}| liquid {LiquidUnits():0.0}u");
            });
        }

        await server.WaitAssertion(() =>
        {
            var outlet = ValveOutlet();
            var landing = KsPhaseMath.BoilingPointAt(nitrogen!.VaporizationEntropy, nitrogen.BoilingPoint, outlet.Pressure);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(NitrogenMoles(), Is.EqualTo(startMoles).Within(1e-3f * startMoles), "The loop gained or lost nitrogen.");
                Assert.That(LiquidUnits(), Is.GreaterThan(1f), "The loop never liquefied any of its nitrogen.");
                Assert.That(outlet.Temperature, Is.EqualTo(landing).Within(1f), "The throttled nitrogen isn't boiling at its pressure.");
            }
        });
    }

    private static readonly ProtoId<ReagentPrototype> NitrogenReagent = "Nitrogen";
    private static readonly ProtoId<ReagentPrototype> NitrousOxideReagent = "NitrousOxide";

    /// <summary>
    ///     A refrigeration loop on a grid in space, with its evaporator in a sealed room and its pump and condenser each in
    ///         one too or out on lattice.
    /// </summary>
    /// <remarks>
    ///     Layout, x to the right and y up, for n condensers. Column x = 0 flows south: corner (y = n + 6), the pump
    ///         (n + 4), the condensers (n + 2 down to 3), the valve (1), corner (-2). Row y = -2 runs east to x = 6, which
    ///         flows north through the evaporator room's wall, five evaporators (0 to 4) and the other wall, and row
    ///         y = n + 6 returns west. Rooms are x -1..1 for the pump (y = n + 4) and the condenser (3 to n + 2), and x 5..7,
    ///         y 0..4 for the evaporator; the rows between and around them are their walls.
    /// </remarks>
    private async Task<RefrigerationLoop> BuildLoop(Gas refrigerant,
        KsFluidReagentProperties properties,
        int condenserCount,
        bool sealedCondenserRoom,
        bool sealedPumpRoom,
        float chargePressure,
        float evaporatorPressure,
        float compressorRate)
    {
        var server = Pair.Server;
        var entityManager = server.EntMan;
        var mapSystem = server.System<SharedMapSystem>();
        var nodeContainerSystem = server.System<NodeContainerSystem>();
        var testMap = await Pair.CreateTestMap();
        var grid = testMap.Grid;
        var loop = new RefrigerationLoop(server, grid.Owner, refrigerant, properties);

        var n = condenserCount;
        var rooms = new List<(Box2i Bounds, List<Vector2i> Floor, float Pressure)>();
        void AddRoom(Box2i bounds, List<Vector2i> floor, float pressure)
        {
            for (var x = bounds.Left + 1; x < bounds.Right; x++)
            for (var y = bounds.Bottom + 1; y < bounds.Top; y++)
                floor.Add(new Vector2i(x, y));
            rooms.Add((bounds, floor, pressure));
        }

        // The rooms the loop dumps heat into hold dense air, so the heat moves their temperature less.
        const float heatBathPressure = 20f * Atmospherics.OneAtmosphere;
        AddRoom(new Box2i(4, -1, 8, 5), loop.EvaporatorRoom, Atmospherics.OneAtmosphere);
        if (sealedCondenserRoom)
            AddRoom(new Box2i(-2, 2, 2, n + 3), loop.CondenserRoom, heatBathPressure);
        if (sealedPumpRoom)
            AddRoom(new Box2i(-2, n + 3, 2, n + 5), loop.PumpRoom, heatBathPressure);

        EntityUid pumpUid = default;
        EntityUid valveUid = default;
        var condenserUids = new List<EntityUid>();
        var evaporatorUids = new List<EntityUid>();

        await server.WaitPost(() =>
        {
            var lattice = new Tile(server.ResolveDependency<ITileDefinitionManager>()["Lattice"].TileId);
            for (var x = -3; x <= 9; x++)
            for (var y = -3; y <= n + 7; y++)
            {
                var indices = new Vector2i(x, y);
                var inRoom = false;
                foreach (var room in rooms)
                    inRoom |= room.Bounds.Contains(indices);
                mapSystem.SetTile(grid, indices, inRoom ? testMap.Tile.Tile : lattice);
            }

            EntityUid Spawn(EntProtoId prototype, int x, int y, Angle rotation)
            {
                return entityManager.SpawnAtPosition(prototype, mapSystem.GridTileToLocal(grid, grid.Comp, new Vector2i(x, y)), rotation);
            }

            var down = Angle.Zero;
            var up = Angle.FromDegrees(180);
            var across = Angle.FromDegrees(90);

            var walls = new HashSet<Vector2i>();
            foreach (var room in rooms)
            {
                for (var x = room.Bounds.Left; x <= room.Bounds.Right; x++)
                for (var y = room.Bounds.Bottom; y <= room.Bounds.Top; y++)
                {
                    if (x == room.Bounds.Left || x == room.Bounds.Right || y == room.Bounds.Bottom || y == room.Bounds.Top)
                        walls.Add(new Vector2i(x, y));
                }
            }

            foreach (var wall in walls)
                Spawn(WallPrototype, wall.X, wall.Y, down);

            // The high side, flowing south.
            Spawn(CornerPrototype, 0, n + 6, down);
            Spawn(PipePrototype, 0, n + 5, down);
            pumpUid = Spawn(PumpPrototype, 0, n + 4, down);
            Spawn(PipePrototype, 0, n + 3, down);
            for (var y = n + 2; y >= 3; y--)
                condenserUids.Add(Spawn(RadiatorPrototype, 0, y, down));
            Spawn(PipePrototype, 0, 2, down);
            valveUid = Spawn(ValvePrototype, 0, 1, down);
            Spawn(PipePrototype, 0, 0, down);
            Spawn(PipePrototype, 0, -1, down);
            Spawn(CornerPrototype, 0, -2, down);

            // The low side: across, up through the evaporator's room, and back.
            for (var x = 1; x <= 5; x++)
                Spawn(PipePrototype, x, -2, across);
            Spawn(CornerPrototype, 6, -2, down);
            Spawn(PipePrototype, 6, -1, down);
            for (var y = 0; y <= 4; y++)
                evaporatorUids.Add(Spawn(RadiatorPrototype, 6, y, up));
            for (var y = 5; y <= n + 5; y++)
                Spawn(PipePrototype, 6, y, down);
            Spawn(CornerPrototype, 6, n + 6, down);
            for (var x = 1; x <= 5; x++)
                Spawn(PipePrototype, x, n + 6, across);

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
            var atmosphereSystem = server.System<AtmosphereSystem>();
            foreach (var room in rooms)
            {
                foreach (var indices in room.Floor)
                {
                    var tileAir = atmosphereSystem.GetTileMixture(grid.Owner, null, indices, excite: true);
                    Assert.That(tileAir, Is.Not.Null, $"Room tile {indices} has no air.");
                    tileAir!.Clear();
                    tileAir.Temperature = Atmospherics.T20C;
                    var scale = room.Pressure / Atmospherics.OneAtmosphere;
                    tileAir.SetMoles(Gas.Oxygen, scale * Atmospherics.OxygenMolesStandard);
                    tileAir.SetMoles(Gas.Nitrogen, scale * Atmospherics.NitrogenMolesStandard);
                }
            }

            // Every net, in flow order from the pump's discharge.
            loop.Nets.Add(Node(pumpUid, "outlet").Air);
            foreach (var uid in condenserUids)
                loop.Nets.Add(Node(uid, "outlet").Air);
            loop.Nets.Add(Node(valveUid, "outlet").Air);
            foreach (var uid in evaporatorUids)
                loop.Nets.Add(Node(uid, "outlet").Air);

            foreach (var net in loop.Nets)
            {
                net.Clear();
                net.Temperature = Atmospherics.T20C;
                net.AdjustMoles(refrigerant, chargePressure * net.Volume / (Atmospherics.R * Atmospherics.T20C));
            }

            Assert.That(Node(evaporatorUids[^1], "outlet").Air, Is.SameAs(Node(pumpUid, "inlet").Air), "The evaporator doesn't return to the pump.");
            Assert.That(Node(condenserUids[^1], "outlet").Air, Is.SameAs(Node(valveUid, "inlet").Air), "The condenser doesn't feed the valve.");
            Assert.That(Node(valveUid, "outlet").Air, Is.SameAs(Node(evaporatorUids[0], "inlet").Air), "The valve doesn't feed the evaporator.");

            var pump = entityManager.GetComponent<GasVolumePumpComponent>(pumpUid);
            pump.Enabled = true;
            pump.TransferRate = compressorRate;

            entityManager.EventBus.RaiseLocalEvent(valveUid, new Content.Shared._KS14.Fluidmos.Devices.KsExpansionValveSetAutomaticMessage(true));
            entityManager.EventBus.RaiseLocalEvent(valveUid, new Content.Shared._KS14.Fluidmos.Devices.KsExpansionValveSetPressureMessage(evaporatorPressure));
        });

        loop.ValveUid = valveUid;
        return loop;
    }

    /// <summary>
    ///     A built loop: its nets in flow order from the pump's discharge, its rooms, and what they hold.
    /// </summary>
    private sealed class RefrigerationLoop(
        RobustIntegrationTest.ServerIntegrationInstance server,
        EntityUid gridUid,
        Gas refrigerant,
        KsFluidReagentProperties properties)
    {
        public readonly List<GasMixture> Nets = new();
        public readonly List<Vector2i> EvaporatorRoom = new();
        public readonly List<Vector2i> CondenserRoom = new();
        public readonly List<Vector2i> PumpRoom = new();
        public EntityUid ValveUid;

        /// <summary>The valve's mean flow over the last third of <see cref="Run"/>.</summary>
        public float SettledFlow;

        private AtmosphereSystem AtmosphereSystem => server.System<AtmosphereSystem>();

        public float RoomTemperature(List<Vector2i> room)
        {
            var sum = 0f;
            foreach (var indices in room)
                sum += AtmosphereSystem.GetTileMixture(gridUid, null, indices)!.Temperature;
            return sum / room.Count;
        }

        /// <summary>The room's air's heat, in atmos's unscaled C T.</summary>
        public float RoomEnergy(List<Vector2i> room)
        {
            var energy = 0f;
            foreach (var indices in room)
            {
                var tileAir = AtmosphereSystem.GetTileMixture(gridUid, null, indices)!;
                energy += AtmosphereSystem.GetHeatCapacity(tileAir, applyScaling: false) * tileAir.Temperature;
            }

            return energy;
        }

        /// <summary>
        ///     The refrigerant's energy, as the phase step keeps it: C T, plus the latent heat its vapor carries, less the
        ///         fusion heat its solid gave up.
        /// </summary>
        public float LoopEnergy()
        {
            var energy = 0f;
            foreach (var net in Nets)
            {
                energy += AtmosphereSystem.GetHeatCapacity(net, applyScaling: false) * net.Temperature;
                energy += properties.LatentHeat * net.GetMoles(refrigerant) / properties.MolesPerUnit;
                energy -= properties.FusionHeat * (net.Fluid?.SolidUnits ?? 0f);
            }

            return energy;
        }

        public float RefrigerantMoles()
        {
            var moles = 0f;
            foreach (var net in Nets)
            {
                var condensedUnits = (net.Fluid?.LiquidUnits ?? 0f) + (net.Fluid?.SolidUnits ?? 0f);
                moles += net.GetMoles(refrigerant) + condensedUnits * properties.MolesPerUnit;
            }

            return moles;
        }

        /// <summary>
        ///     Runs the loop, logging it every sample, and averages the valve's flow over the last third.
        /// </summary>
        /// <returns>The evaporator room's temperature at the end.</returns>
        public async Task<float> Run(int samples, float secondsPerSample)
        {
            var ticksPerSample = (int)(secondsPerSample * server.ResolveDependency<IGameTiming>().TickRate);
            var settledFrom = 2 * samples / 3;
            var settledCount = (float)(samples - settledFrom + 1);
            var endRoom = 0f;
            SettledFlow = 0f;
            for (var sample = 1; sample <= samples; sample++)
            {
                await server.WaitRunTicks(ticksPerSample);
                await server.WaitPost(() =>
                {
                    var valve = server.EntMan.GetComponent<KsExpansionValveComponent>(ValveUid);
                    endRoom = RoomTemperature(EvaporatorRoom);
                    var text = "";
                    foreach (var net in Nets)
                        text += $"[{net.Pressure:0} kPa {net.Temperature:0.0} K {net.Fluid?.LiquidUnits ?? 0f:0}u {net.Fluid?.SolidUnits ?? 0f:0}s] ";
                    var condenserRoom = CondenserRoom.Count > 0 ? $" condenser room {RoomTemperature(CondenserRoom):0.0} K" : "";
                    var pumpRoom = PumpRoom.Count > 0 ? $" pump room {RoomTemperature(PumpRoom):0.0} K" : "";
                    TestContext.Out.WriteLine($"t={sample * secondsPerSample,4}s room {endRoom:0.0} K{condenserRoom}{pumpRoom} | " +
                                              $"valve {valve.Opening:0.00}, {valve.FlowRate:0} u/s | nets {text}");

                    if (sample >= settledFrom)
                        SettledFlow += valve.FlowRate / settledCount;
                });
            }

            return endRoom;
        }
    }
}
