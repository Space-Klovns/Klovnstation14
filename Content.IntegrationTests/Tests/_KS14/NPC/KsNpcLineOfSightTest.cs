#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.NPC.Perception;
using Content.Shared.Doors.Components;
using Robust.Shared.EntitySerialization;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Utility;
using Robust.UnitTesting.Pool;
using static Content.IntegrationTests.Tests._KS14.NPC.KsNpcSquadTestHelpers;

namespace Content.IntegrationTests.Tests._KS14.NPC;

/// <summary>
///     <see cref="NpcLineOfSightSystem"/> walks the tiles a ray crosses instead of querying the occluder tree. It must
///         give the same answer the tree does, for every ray, so these cast thousands of them across a real station and
///         compare the two: rays between tile centres, which run diagonally through tile corners; rays from anywhere on
///         a tile; and rays lying exactly along tile edges, the cases a walk most easily misses a tile on.
/// </summary>
public sealed class KsNpcLineOfSightTest : GameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    private const string MapPath = "/Maps/box.yml";
    private const float Range = 15f;

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: KsLosTestLooseOccluder
  components:
  - type: Occluder

- type: entity
  id: KsLosTestWideOccluder
  components:
  - type: Transform
    anchored: true
  - type: Occluder
    polygon:
    - ""-1.5,-0.5""
    - ""1.5,-0.5""
    - ""1.5,0.5""
    - ""-1.5,0.5""
";

    [Test]
    public async Task TestTileWalkAgreesWithOccluderTree()
    {
        var (mapUid, gridUid, floorTiles) = await LoadStation();
        var lineOfSightSystem = SEntMan.System<NpcLineOfSightSystem>();
        var occluderSystem = SEntMan.System<OccluderSystem>();
        var random = new System.Random(42);

        try
        {
            await Server.WaitAssertion(() =>
            {
                CompareRays(lineOfSightSystem, gridUid, floorTiles, random, "with every door as the map has it");

                // Every door the other way, as doors toggle their occluders when they open and shut. Read live, so the walk
                //      must see the change at once.
                var doorEnumerator = SEntMan.EntityQueryEnumerator<DoorComponent, OccluderComponent>();
                while (doorEnumerator.MoveNext(out var doorUid, out _, out var occluderComponent))
                {
                    occluderSystem.SetEnabled(doorUid, !occluderComponent.Enabled, occluderComponent);
                }

                CompareRays(lineOfSightSystem, gridUid, floorTiles, random, "with every door toggled");
            });
        }
        finally
        {
            await Server.WaitPost(() => SEntMan.DeleteEntity(mapUid));
        }
    }

    /// <summary>
    ///     A sight field - everything that could block sight from one viewpoint, gathered once - answers exactly as a
    ///         ray from that viewpoint does, for targets at every distance and direction, before and after every door
    ///         is toggled.
    /// </summary>
    [Test]
    public async Task TestSightFieldAgreesWithRays()
    {
        var (mapUid, gridUid, floorTiles) = await LoadStation();
        var lineOfSightSystem = SEntMan.System<NpcLineOfSightSystem>();
        var occluderSystem = SEntMan.System<OccluderSystem>();
        var transformSystem = SEntMan.System<SharedTransformSystem>();
        var random = new System.Random(7);
        const float fieldRange = 12f;

        try
        {
            await Server.WaitAssertion(() =>
            {
                MapCoordinates At(Vector2 position) => transformSystem.ToMapCoordinates(new EntityCoordinates(gridUid, position));

                void Compare(string situation)
                {
                    var field = new NpcSightField();
                    var disagreements = new List<string>();
                    var clearCount = 0;
                    var blockedCount = 0;
                    var builtCount = 0;

                    for (var i = 0; i < 300; i++)
                    {
                        var originTile = floorTiles[random.Next(floorTiles.Count)];
                        var originLocal = originTile + new Vector2((float) random.NextDouble(), (float) random.NextDouble());
                        var origin = At(originLocal);
                        lineOfSightSystem.BuildSightField(origin, fieldRange, field);
                        if (!field.Fallback)
                            builtCount++;

                        for (var j = 0; j < 60; j++)
                        {
                            // Out to a little past the range, so the range is checked too; half to tile centres.
                            var targetLocal = originLocal + new Vector2((float) random.NextDouble() * 28f - 14f, (float) random.NextDouble() * 28f - 14f);
                            if (j % 2 == 0)
                                targetLocal = new Vector2(MathF.Floor(targetLocal.X) + 0.5f, MathF.Floor(targetLocal.Y) + 0.5f);

                            var target = At(targetLocal);
                            var byField = lineOfSightSystem.InLineOfSight(field, target);
                            var byRay = lineOfSightSystem.InLineOfSight(origin, target, fieldRange);

                            if (byRay)
                                clearCount++;
                            else
                                blockedCount++;

                            if (byField != byRay && disagreements.Count < 10)
                                disagreements.Add($"{origin.Position} -> {target.Position}: field says {byField}, ray says {byRay}");
                        }
                    }

                    Assert.Multiple(() =>
                    {
                        Assert.That(disagreements, Is.Empty, $"{situation}, a sight field should agree with a ray on every target");
                        Assert.That(builtCount, Is.GreaterThan(250), $"{situation}, nearly every field on the station should be built, not fall back to rays");
                        Assert.That(clearCount, Is.GreaterThan(1000), $"{situation}, plenty of targets should be in sight, or the comparison proves little");
                        Assert.That(blockedCount, Is.GreaterThan(1000), $"{situation}, plenty of targets should be out of sight, or the comparison proves little");
                    });
                }

                Compare("with every door as the map has it");

                var doorEnumerator = SEntMan.EntityQueryEnumerator<DoorComponent, OccluderComponent>();
                while (doorEnumerator.MoveNext(out var doorUid, out _, out var occluderComponent))
                {
                    occluderSystem.SetEnabled(doorUid, !occluderComponent.Enabled, occluderComponent);
                }

                Compare("with every door toggled");
            });
        }
        finally
        {
            await Server.WaitPost(() => SEntMan.DeleteEntity(mapUid));
        }
    }

    [Test]
    public async Task TestIrregularOccludersBlockTheWalk()
    {
        var (mapUid, gridUid, floorTiles) = await LoadStation();
        var lineOfSightSystem = SEntMan.System<NpcLineOfSightSystem>();
        var transformSystem = SEntMan.System<SharedTransformSystem>();

        try
        {

            // An open stretch of floor three tiles wide and five long, so nothing but the test occluders is in the way.
            var floorSet = floorTiles.ToHashSet();
            var corridor = floorTiles.First(tile =>
                Enumerable.Range(-2, 5).All(dy => Enumerable.Range(-1, 3).All(dx => floorSet.Contains(tile + new Vector2i(dx, dy)))));

            MapCoordinates At(Vector2i tile, float dx = 0f)
            {
                return transformSystem.ToMapCoordinates(new EntityCoordinates(gridUid, new Vector2(tile.X + 0.5f + dx, tile.Y + 0.5f)));
            }

            // Straight up the left-hand column, past the corridor's middle row.
            var below = corridor + new Vector2i(-1, -2);
            var above = corridor + new Vector2i(-1, 2);

            await Server.WaitAssertion(() =>
            {
                Assert.That(lineOfSightSystem.InLineOfSight(At(below), At(above), Range), Is.True,
                    "the corridor should be clear before anything is put in it");

                // Loose, on the corridor's left-hand column: in no tile's anchored list, so the walk cannot find it there.
                var looseUid = SEntMan.SpawnEntity("KsLosTestLooseOccluder", new EntityCoordinates(gridUid, new Vector2(corridor.X - 0.5f, corridor.Y + 0.5f)));
                Assert.Multiple(() =>
                {
                    Assert.That(SEntMan.HasComponent<NpcIrregularOccluderComponent>(looseUid), Is.True,
                        "an unanchored occluder should be marked as one the walk cannot find");
                    Assert.That(lineOfSightSystem.InLineOfSight(At(below), At(above), Range), Is.False,
                        "an unanchored occluder in the way should block the walk");
                    Assert.That(lineOfSightSystem.InLineOfSightByOccluderTree(At(below), At(above), Range), Is.False,
                        "and the occluder tree agrees");
                });

                SEntMan.DeleteEntity(looseUid);
                Assert.That(lineOfSightSystem.InLineOfSight(At(below), At(above), Range), Is.True,
                    "the corridor should be clear again once it is gone");

                // Anchored on the middle column, but three tiles wide: it reaches into the left-hand column from a tile the
                //      ray never crosses.
                var wideUid = SEntMan.SpawnEntity("KsLosTestWideOccluder", new EntityCoordinates(gridUid, new Vector2(corridor.X + 0.5f, corridor.Y + 0.5f)));
                Assert.Multiple(() =>
                {
                    Assert.That(SEntMan.GetComponent<TransformComponent>(wideUid).Anchored, Is.True,
                        "the wide occluder should be anchored, or this is the loose case again");
                    Assert.That(SEntMan.HasComponent<NpcIrregularOccluderComponent>(wideUid), Is.True,
                        "an anchored occluder reaching past its tile should be marked as one the walk cannot find");
                    Assert.That(lineOfSightSystem.InLineOfSight(At(below), At(above), Range), Is.False,
                        "an occluder reaching across the ray from another tile should block the walk");
                    Assert.That(lineOfSightSystem.InLineOfSightByOccluderTree(At(below), At(above), Range), Is.False,
                        "and the occluder tree agrees");
                });

                SEntMan.DeleteEntity(wideUid);
            });
        }
        finally
        {
            await Server.WaitPost(() => SEntMan.DeleteEntity(mapUid));
        }
    }

    /// <summary>
    ///     A ray lying exactly along a tile edge is blocked by a wall on its way, and by nothing else on that line. The
    ///         occluder tree gets the second half wrong: its ray test gives every box on the line a NaN or zero distance,
    ///         so walls behind the ray's start and past its end block it too.
    /// </summary>
    [Test]
    public async Task TestEdgeRayIsNotBlockedByDistantWalls()
    {
        var tileDefinitionManager = Server.ResolveDependency<ITileDefinitionManager>();
        var lineOfSightSystem = SEntMan.System<NpcLineOfSightSystem>();
        var transformSystem = SEntMan.System<SharedTransformSystem>();
        var map = await Pair.CreateTestMap();
        var gridUid = EntityUid.Invalid;

        await Server.WaitPost(() =>
        {
            gridUid = MakeGrid(SEntMan, tileDefinitionManager, map.MapId, map.Grid, new Vector2i(-10, -10), new Vector2i(10, 10)).Owner;

            // On the line x = 0, the edge between the columns either side of it: one wall behind where the ray starts,
            //      one past where the first ray ends.
            SpawnAt(SEntMan, "WallSolid", gridUid, 0, -5);
            SpawnAt(SEntMan, "WallSolid", gridUid, 0, 5);
        });

        await Pair.RunTicksSync(5);

        await Server.WaitAssertion(() =>
        {
            MapCoordinates At(float x, float y) => transformSystem.ToMapCoordinates(new EntityCoordinates(gridUid, new Vector2(x, y)));

            Assert.Multiple(() =>
            {
                Assert.That(lineOfSightSystem.InLineOfSight(At(0f, 0.5f), At(0f, 3.5f), Range), Is.True,
                    "nothing lies between the ends of a ray along a tile edge, so it should be clear");
                Assert.That(lineOfSightSystem.InLineOfSight(At(0f, 0.5f), At(0f, 8.5f), Range), Is.False,
                    "a ray along a tile edge that runs past a wall's face should be blocked by it");
            });
        });
    }

    /// <summary>
    ///     Loads the station, and lists its floor tiles with nothing anchored on them: where NPCs stand and look from.
    ///         The caller deletes the map when done with it.
    /// </summary>
    private async Task<(EntityUid MapUid, EntityUid GridUid, List<Vector2i> FloorTiles)> LoadStation()
    {
        var mapSystem = SEntMan.System<SharedMapSystem>();
        var mapUid = EntityUid.Invalid;
        var gridUid = EntityUid.Invalid;
        var floorTiles = new List<Vector2i>();

        await Server.WaitPost(() =>
        {
            var options = DeserializationOptions.Default with { InitializeMaps = true };
            Assert.That(SEntMan.System<MapLoaderSystem>().TryLoadMap(new ResPath(MapPath), out var map, out var grids, options), "map should load");
            mapUid = map!.Value.Owner;

            // The station: the grid with the most tiles.
            var station = grids!.MaxBy(grid => mapSystem.GetAllTiles(grid, grid.Comp).Count());
            gridUid = station.Owner;

            foreach (var tileRef in mapSystem.GetAllTiles(station, station.Comp))
            {
                if (!mapSystem.GetAnchoredEntitiesEnumerator(station, station.Comp, tileRef.GridIndices).MoveNext(out _))
                    floorTiles.Add(tileRef.GridIndices);
            }
        });

        // The occluder tree takes its moves on the next update; let everything settle first.
        await Pair.RunTicksSync(5);
        return (mapUid, gridUid, floorTiles);
    }

    private void CompareRays(NpcLineOfSightSystem lineOfSightSystem, EntityUid gridUid, List<Vector2i> floorTiles, System.Random random, string situation)
    {
        var transformSystem = SEntMan.System<SharedTransformSystem>();
        var disagreements = new List<string>();
        var clearCount = 0;
        var blockedCount = 0;

        MapCoordinates At(Vector2 position)
        {
            return transformSystem.ToMapCoordinates(new EntityCoordinates(gridUid, position));
        }

        void Compare(Vector2 from, Vector2 to, string kind)
        {
            var walked = lineOfSightSystem.InLineOfSight(At(from), At(to), Range);
            var queried = lineOfSightSystem.InLineOfSightByOccluderTree(At(from), At(to), Range);

            if (walked)
                clearCount++;
            else
                blockedCount++;

            if (walked != queried && disagreements.Count < 10)
                disagreements.Add($"{kind} {from} -> {to}: walk says {walked}, tree says {queried}");
        }

        Vector2i PickWithin(Vector2i from)
        {
            while (true)
            {
                var candidate = floorTiles[random.Next(floorTiles.Count)];
                var distance = (candidate - from).Length;
                if (distance >= 1 && distance <= 12)
                    return candidate;
            }
        }

        for (var i = 0; i < 3000; i++)
        {
            var from = floorTiles[random.Next(floorTiles.Count)];
            var to = PickWithin(from);
            var centre = new Vector2(0.5f, 0.5f);

            Compare(from + centre, to + centre, "centre to centre");

            var jitterFrom = new Vector2((float) random.NextDouble(), (float) random.NextDouble());
            var jitterTo = new Vector2((float) random.NextDouble(), (float) random.NextDouble());
            Compare(from + jitterFrom, to + jitterTo, "anywhere to anywhere");

            // Along the tile's bottom edge, and up its left-hand one: a hair inside the tile, where the walk must still
            //      take in the tiles across the edge. Not on the edge itself, where the tree is wrong - see
            //      NpcLineOfSightSystem.SegmentTouches - and TestEdgeRayIsNotBlockedByDistantWalls covers the walk.
            const float edge = 1e-4f;
            Compare(new Vector2(from.X + 0.5f, from.Y + edge), new Vector2(from.X + 0.5f + (to.X - from.X), from.Y + edge), "along a row edge");
            Compare(new Vector2(from.X + edge, from.Y + 0.5f), new Vector2(from.X + edge, from.Y + 0.5f + (to.Y - from.Y)), "along a column edge");

            // Exactly through tile corners, at 45 degrees.
            var steps = random.Next(2, 8);
            var diagonal = new Vector2(random.Next(2) == 0 ? -1 : 1, random.Next(2) == 0 ? -1 : 1);
            Compare(from + centre, from + centre + diagonal * steps, "through corners");
        }

        Assert.Multiple(() =>
        {
            Assert.That(disagreements, Is.Empty, $"{situation}, the tile walk and the occluder tree should agree on every ray");
            Assert.That(clearCount, Is.GreaterThan(1000), $"{situation}, plenty of the rays should be clear, or the comparison proves little");
            Assert.That(blockedCount, Is.GreaterThan(1000), $"{situation}, plenty of the rays should be blocked, or the comparison proves little");
        });
    }
}
