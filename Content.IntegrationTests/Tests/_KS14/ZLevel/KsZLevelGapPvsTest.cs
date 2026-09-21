#nullable enable
using System.Collections.Generic;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server._KS14.ZLevel.Elevators;
using Content.Shared._KS14.ZLevel;
using Content.Shared._KS14.ZLevel.Elevators;
using Content.Shared._KS14.ZLevel.Transit;
using Robust.Shared;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.ZLevel;

/// <summary>
///     That everything on a gap map actually reaches the client.
/// </summary>
/// <remarks>
///     A gap is on no z-level stack, so none of the ordinary machinery sends it: the player is not standing
///         on it, and <see cref="Content.Server._KS14.ZLevel.KsZLevelPvsSystem"/> mirrors the z-level below
///         the player, which a gap never is. Only the override in
///         <see cref="Content.Server._KS14.ZLevel.Transit.KsZLevelGapSystem"/> does, and if it is put on the
///         wrong entity everything on the gap silently stops being transmitted - which looks exactly like a
///         rendering bug and is not one.
///     Every pooled pair runs with net.pvs off, which sends every entity to every client, so these tests
///         turn it back on. Without that they would pass no matter what the code did.
/// </remarks>
public sealed class KsZLevelGapPvsTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true };

    private static readonly Vector2 ShaftPosition = new(2f, 0f);

    /// <summary>
    ///     Two linked z-levels with the player on the upper one, and an elevator part way up the gap between
    ///         them.
    /// </summary>
    private async Task<EntityUid> SetUpCrossingAsync()
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var tileDefinitionManager = server.ResolveDependency<ITileDefinitionManager>();
        var mapSystem = entManager.System<SharedMapSystem>();
        var transformSystem = entManager.System<SharedTransformSystem>();
        var zLevelSystem = entManager.System<KsZLevelSystem>();
        var elevatorSystem = entManager.System<ZLevelElevatorSystem>();

        var session = Pair.Player;
        Assert.That(session, Is.Not.Null, "this test needs a connected session for anything to replicate to");

        var tile = new Tile(tileDefinitionManager["Plating"].TileId);
        var elevatorUid = EntityUid.Invalid;

        await server.WaitPost(() =>
        {
            var zLevels = new List<EntityUid>();
            for (var floor = 0; floor < 2; floor++)
            {
                var mapUid = mapSystem.CreateMap(out _);
                entManager.EnsureComponent<KsZLevelComponent>(mapUid);

                if (floor > 0)
                    zLevelSystem.AddZLevelDirectlyAbove(zLevels[0], mapUid);

                zLevels.Add(mapUid);
            }

            // On the upper floor, looking down the shaft at the elevator coming up.
            var upperMapId = entManager.GetComponent<MapComponent>(zLevels[1]).MapId;
            var viewerUid = entManager.SpawnEntity(null, new MapCoordinates(ShaftPosition, upperMapId));
            server.PlayerMan.SetAttachedEntity(session, viewerUid);

            var lowerMapId = entManager.GetComponent<MapComponent>(zLevels[0]).MapId;
            var elevator = mapSystem.CreateGridEntity(lowerMapId);
            mapSystem.SetTile(elevator.Owner, elevator.Comp, Vector2i.Zero, tile);
            transformSystem.SetWorldPosition(elevator.Owner, ShaftPosition);

            var elevatorComponent = entManager.EnsureComponent<ZLevelElevatorComponent>(elevator.Owner);

            // Slow, so the crossing is still going on by the time the assertions run.
            elevatorComponent.SecondsPerDepth = 60f;
            elevatorComponent.DwellTime = TimeSpan.Zero;

            elevatorUid = elevator.Owner;
        });

        await Pair.RunTicksSync(3);

        await server.WaitPost(() =>
            elevatorSystem.TryStartAscent((elevatorUid, entManager.GetComponent<ZLevelElevatorComponent>(elevatorUid))));

        // Long enough for the PVS sweep, which runs on its own interval rather than every tick.
        await Pair.RunTicksSync(30);

        return elevatorUid;
    }

    [Test]
    public async Task TestGapContentsReachTheClient()
    {
        await OverrideCVar(Side.Server, CVars.NetPVS, true);

        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();

        var elevatorUid = await SetUpCrossingAsync();

        var platformNetEntity = NetEntity.Invalid;
        var fallerNetEntity = NetEntity.Invalid;
        var unrelatedNetEntity = NetEntity.Invalid;

        await server.WaitPost(() =>
        {
            var gapUid = entManager.GetComponent<TransformComponent>(elevatorUid).MapUid!.Value;
            Assert.That(entManager.HasComponent<KsZLevelGapComponent>(gapUid), Is.True,
                "the elevator has to be mid-crossing, or this is not testing a gap at all");

            platformNetEntity = entManager.GetNetEntity(elevatorUid);

            // Deliberately well clear of the platform's own tile. Anything standing on it is parented to
            //      the grid by traversal and so was covered even by the old grid-level override; the case
            //      that was never transmitted is the one parented to the gap *map*, a sibling of the
            //      platform rather than a child - something thrown off it, or falling past its edge.
            var gapMapId = entManager.GetComponent<MapComponent>(gapUid).MapId;
            fallerNetEntity = entManager.GetNetEntity(
                entManager.SpawnEntity(null, new MapCoordinates(new Vector2(20f, 20f), gapMapId)));

            // The control. Nothing sends this, so if it arrives then PVS is not actually filtering and the
            //      two assertions above would pass however the overrides were wired.
            var unrelatedMapUid = entManager.System<SharedMapSystem>().CreateMap(out var unrelatedMapId);
            unrelatedNetEntity = entManager.GetNetEntity(
                entManager.SpawnEntity(null, new MapCoordinates(Vector2.Zero, unrelatedMapId)));
            _ = unrelatedMapUid;
        });

        await Pair.RunTicksSync(30);

        var client = Pair.Client;
        var clientEntManager = client.ResolveDependency<IEntityManager>();

        await client.WaitAssertion(() =>
        {
            Assert.That(clientEntManager.TryGetEntity(platformNetEntity, out _), Is.True,
                "a platform mid-crossing has to reach the client, or the ride is invisible");

            Assert.That(clientEntManager.TryGetEntity(fallerNetEntity, out _), Is.True,
                "and so does anything falling past it - it is on the gap map, which nothing else sends");

            Assert.That(clientEntManager.TryGetEntity(unrelatedNetEntity, out _), Is.False,
                "pvs has to actually be filtering, or neither assertion above proves anything");
        });
    }
}
