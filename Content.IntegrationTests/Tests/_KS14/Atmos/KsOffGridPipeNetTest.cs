#nullable enable
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Atmos.Piping.EntitySystems;
using Content.Server.NodeContainer.EntitySystems;
using Content.Server.NodeContainer.Nodes;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Content.Shared.Coordinates;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.Atmos;

/// <summary>
///     A map whose grids have no <see cref="GridAtmosphereComponent"/> (they take the map's immutable atmosphere) still runs
///         its pipe nets and atmos devices, against the map's air.
/// </summary>
[TestOf(typeof(AtmosphereSystem))]
[TestOf(typeof(AtmosDeviceSystem))]
public sealed class KsOffGridPipeNetTest : GameTest
{
    private static readonly EntProtoId PipePrototype = "GasPipeStraight";
    private static readonly EntProtoId PassiveVentPrototype = "GasPassiveVent";

    private const float RoomPressure = Atmospherics.OneAtmosphere;
    private const float PipePressure = 4f * Atmospherics.OneAtmosphere;
    private const float SettleSeconds = 20f;

    private sealed record Site(EntityUid MapUid, Entity<MapGridComponent> Grid);

    /// <summary>
    ///     A map with a standard atmosphere, and a strip of floor on a grid that has none of its own.
    /// </summary>
    private async Task<Site> BuildSite()
    {
        var server = Pair.Server;
        Site site = default!;

        await server.WaitPost(() =>
        {
            var mapSystem = server.EntMan.System<SharedMapSystem>();
            var mapUid = mapSystem.CreateMap(out var mapId);
            var grid = mapSystem.CreateGridEntity(mapId);
            for (var y = -4; y < 4; y++)
            {
                mapSystem.SetTile(grid, new Vector2i(0, y), new Tile(1));
            }

            var air = new GasMixture(Atmospherics.CellVolume) { Temperature = Atmospherics.T20C };
            air.SetMoles(Gas.Nitrogen, RoomPressure * Atmospherics.CellVolume / (Atmospherics.R * Atmospherics.T20C));
            server.EntMan.System<AtmosphereSystem>().SetMapAtmosphere(mapUid, space: false, air);

            // The premise: the grid is not simulated.
            if (server.EntMan.HasComponent<GridAtmosphereComponent>(grid.Owner))
                server.EntMan.RemoveComponent<GridAtmosphereComponent>(grid.Owner);
            site = new Site(mapUid, grid);
        });

        return site;
    }

    private async Task WaitSeconds(float seconds)
    {
        await Pair.Server.WaitRunTicks((int)(seconds * Pair.Server.CfgMan.GetCVar(Robust.Shared.CVars.NetTickrate)));
    }

    /// <summary>
    ///     A burning mixture sealed in a pipe burns: only a pipe net that is updated reacts its gas.
    /// </summary>
    [Test]
    public async Task OffGridPipeNetIsUpdated()
    {
        var server = Pair.Server;
        var site = await BuildSite();
        EntityUid pipeUid = default;

        await server.WaitPost(() =>
        {
            pipeUid = server.EntMan.SpawnEntity(PipePrototype, site.Grid.Owner.ToCoordinates(0, 0));
        });
        await server.WaitRunTicks(5);

        var plasmaBefore = 0f;
        await server.WaitPost(() =>
        {
            Assert.That(server.EntMan.HasComponent<GridAtmosphereComponent>(site.Grid.Owner), Is.False, "The grid should have no atmosphere.");
            Assert.That(server.System<NodeContainerSystem>().TryGetNode(pipeUid, "pipe", out PipeNode? node));

            var air = node!.Air;
            air.Temperature = 1500f;
            air.SetMoles(Gas.Oxygen, 40f);
            air.SetMoles(Gas.Plasma, 20f);
            plasmaBefore = air.GetMoles(Gas.Plasma);
        });

        await WaitSeconds(SettleSeconds);

        await server.WaitAssertion(() =>
        {
            Assert.That(server.System<NodeContainerSystem>().TryGetNode(pipeUid, "pipe", out PipeNode? node));
            Assert.That(node!.Air.GetMoles(Gas.Plasma), Is.LessThan(plasmaBefore),
                "The pipe net on a grid with no atmosphere never reacted its gas: it isn't being updated.");
        });
    }

    /// <summary>
    ///     A passive vent on such a grid equalises its pipe with the map's air, rather than with space or not at all.
    /// </summary>
    [Test]
    public async Task OffGridPassiveVentEqualisesWithTheMap()
    {
        var server = Pair.Server;
        var site = await BuildSite();
        EntityUid pipeUid = default;

        await server.WaitPost(() =>
        {
            server.EntMan.SpawnEntity(PassiveVentPrototype, site.Grid.Owner.ToCoordinates(0, 0));
            pipeUid = server.EntMan.SpawnEntity(PipePrototype, site.Grid.Owner.ToCoordinates(0, -1));
        });
        await server.WaitRunTicks(5);

        await server.WaitPost(() =>
        {
            Assert.That(server.EntMan.HasComponent<GridAtmosphereComponent>(site.Grid.Owner), Is.False, "The grid should have no atmosphere.");
            Assert.That(server.System<NodeContainerSystem>().TryGetNode(pipeUid, "pipe", out PipeNode? node));

            var air = node!.Air;
            air.Temperature = Atmospherics.T20C;
            air.SetMoles(Gas.Nitrogen, PipePressure * air.Volume / (Atmospherics.R * air.Temperature));
        });

        await WaitSeconds(SettleSeconds);

        await server.WaitAssertion(() =>
        {
            Assert.That(server.System<NodeContainerSystem>().TryGetNode(pipeUid, "pipe", out PipeNode? node));
            var pressure = node!.Air.Pressure;

            // Space would leave next to nothing; an unprocessed vent leaves all of it.
            Assert.That(pressure, Is.LessThan(PipePressure * 0.5f), "The vent never vented: off-grid devices aren't processed.");
            Assert.That(pressure, Is.EqualTo(RoomPressure).Within(RoomPressure * 0.1f), "The vent didn't settle on the map's air.");
        });
    }
}
