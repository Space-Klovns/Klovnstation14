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

    /// <summary>
    ///     That a passenger riding a platform across a gap is still sent the floor underneath them.
    /// </summary>
    /// <remarks>
    ///     A rider stands on the gap map, which is on no stack, so the whole world below them reaches them
    ///         through one thing only: the mirror subscriber
    ///         <see cref="Content.Server._KS14.ZLevel.KsZLevelPvsSystem"/> parks on the z-level below. Ask
    ///         that question by walking the stack directly and a gap answers "there is nothing below me",
    ///         the subscriber is taken away, and the floor the rider can plainly see receding under their
    ///         feet is drawn empty for the length of the ride - no error, no log, just an abandoned station.
    ///     The whole point of anchoring a gap is that the navigation API answers it about that floor, so
    ///         this is the test that the PVS mirror actually goes through the API.
    /// </remarks>
    [Test]
    public async Task TestRiderIsStillSentTheFloorBelow()
    {
        await OverrideCVar(Side.Server, CVars.NetPVS, true);

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
        var lowerZLevelUid = EntityUid.Invalid;

        await server.WaitPost(() =>
        {
            var lowerMapUid = mapSystem.CreateMap(out var lowerMapId);
            entManager.EnsureComponent<KsZLevelComponent>(lowerMapUid);

            var upperMapUid = mapSystem.CreateMap(out _);
            entManager.EnsureComponent<KsZLevelComponent>(upperMapUid);
            zLevelSystem.AddZLevelDirectlyAbove(lowerMapUid, upperMapUid);

            lowerZLevelUid = lowerMapUid;

            var elevator = mapSystem.CreateGridEntity(lowerMapId);
            mapSystem.SetTile(elevator.Owner, elevator.Comp, Vector2i.Zero, tile);
            transformSystem.SetWorldPosition(elevator.Owner, ShaftPosition);

            var elevatorComponent = entManager.EnsureComponent<ZLevelElevatorComponent>(elevator.Owner);

            // Slow, so the rider is still mid-crossing by the time the assertions run.
            elevatorComponent.SecondsPerDepth = 60f;
            elevatorComponent.DwellTime = TimeSpan.Zero;

            elevatorUid = elevator.Owner;

            // Standing on the platform's own tile, so grid traversal parents them to it and they ride along.
            var riderUid = entManager.SpawnEntity(null, new MapCoordinates(ShaftPosition, lowerMapId));
            server.PlayerMan.SetAttachedEntity(session, riderUid);
        });

        await Pair.RunTicksSync(3);

        await server.WaitPost(() =>
            elevatorSystem.TryStartAscent((elevatorUid, entManager.GetComponent<ZLevelElevatorComponent>(elevatorUid))));

        // Long enough for the mirror sweep, which runs on its own interval rather than every tick.
        await Pair.RunTicksSync(30);

        await server.WaitAssertion(() =>
        {
            var riderMapUid = entManager.GetComponent<TransformComponent>(session!.AttachedEntity!.Value).MapUid;

            Assert.That(entManager.HasComponent<KsZLevelGapComponent>(riderMapUid), Is.True,
                "the rider has to actually be on the gap, or this is not testing a crossing at all");
        });

        // Spawned only now, with the rider already off the ground and looking down at the floor below.
        //      Spawning it before the ride would prove nothing: leaving PVS detaches an entity on the client
        //      rather than deleting it, so one the client had already been told about answers TryGetEntity
        //      forever afterwards whether it is still being sent or not. One that appears mid-ride can only
        //      arrive if the mirror is genuinely still parked on that floor.
        var landmarkNetEntity = NetEntity.Invalid;
        await server.WaitPost(() =>
        {
            var lowerMapId = entManager.GetComponent<MapComponent>(lowerZLevelUid).MapId;
            landmarkNetEntity = entManager.GetNetEntity(
                entManager.SpawnEntity(null, new MapCoordinates(ShaftPosition, lowerMapId)));
        });

        await Pair.RunTicksSync(30);

        var client = Pair.Client;
        var clientEntManager = client.ResolveDependency<IEntityManager>();

        await client.WaitAssertion(() =>
            Assert.That(clientEntManager.TryGetEntity(landmarkNetEntity, out _), Is.True,
                "the floor below a rider has to keep being sent for the whole ride - a gap is on no stack, so only resolving it to the z-level it is anchored to keeps the mirror parked there"));
    }
}
