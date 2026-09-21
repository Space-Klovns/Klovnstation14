#nullable enable
using System.Collections.Generic;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.ZLevel.Elevators;
using Content.Server._KS14.ZLevel.Transit;
using Content.Shared._KS14.ZLevel;
using Content.Shared._KS14.ZLevel.Elevators;
using Content.Shared._KS14.ZLevel.Physics;
using Content.Shared._KS14.ZLevel.Transit;
using Content.Shared.Atmos.Components;
using Content.Shared.Gravity;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.ZLevel;

/// <summary>
///     The maps that sit between z-levels: that a grid crossing one is on a real place with air and gravity,
///         that the stack it is crossing is left exactly as it was, and that the map never outlives the
///         crossing.
/// </summary>
/// <remarks>
///     The stack-is-untouched assertions are the load-bearing ones. A gap is deliberately not a member of the
///         stack it bridges, because putting one in would renumber every floor above it mid-ride, offer
///         itself to elevators as somewhere to travel to, and take the place of a real z-level in the
///         one-level budgets that audio leak, light leak and the PVS mirror all work to. None of those fail
///         loudly, so they are pinned here instead.
/// </remarks>
public sealed class KsZLevelGapTest : GameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    [TestPrototypes]
    private const string Prototypes = @"
# Something with a body to drop down a shaft. Bare beyond that: what is under test is where it ends up,
#   not what it is.
- type: entity
  id: KsGapTestFaller
  name: test faller
  components:
  - type: Transform
  - type: Physics
    bodyType: Dynamic
  - type: Fixtures
";

    private const float TestSecondsPerDepth = 0.4f;

    private static readonly Vector2 ShaftPosition = new(2f, 0f);

    private sealed record TestShaft(List<EntityUid> ZLevels);

    /// <summary>
    ///     A stack of bare linked z-levels. No landings: nothing here is about what an elevator lands on.
    /// </summary>
    private async Task<TestShaft> CreateShaft(int floorCount, float depth = 1f)
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var mapSystem = entManager.System<SharedMapSystem>();
        var zLevelSystem = entManager.System<KsZLevelSystem>();

        var zLevels = new List<EntityUid>();

        await server.WaitPost(() =>
        {
            for (var floor = 0; floor < floorCount; floor++)
            {
                var mapUid = mapSystem.CreateMap(out _);
                var zLevelComponent = entManager.EnsureComponent<KsZLevelComponent>(mapUid);

                if (floor > 0)
                    zLevelSystem.AddZLevelDirectlyAbove(zLevels[floor - 1], mapUid);

                zLevelSystem.SetDepth((mapUid, zLevelComponent), depth);
                zLevels.Add(mapUid);
            }
        });

        return new TestShaft(zLevels);
    }

    private async Task<EntityUid> CreateElevator(TestShaft shaft, int floor, float secondsPerDepth = TestSecondsPerDepth)
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var tileDefinitionManager = server.ResolveDependency<ITileDefinitionManager>();
        var mapSystem = entManager.System<SharedMapSystem>();
        var transformSystem = entManager.System<SharedTransformSystem>();

        var tile = new Tile(tileDefinitionManager["Plating"].TileId);
        var elevatorUid = EntityUid.Invalid;

        await server.WaitPost(() =>
        {
            var mapId = entManager.GetComponent<MapComponent>(shaft.ZLevels[floor]).MapId;
            var elevator = mapSystem.CreateGridEntity(mapId);
            mapSystem.SetTile(elevator.Owner, elevator.Comp, Vector2i.Zero, tile);
            transformSystem.SetWorldPosition(elevator.Owner, ShaftPosition);

            var elevatorComponent = entManager.EnsureComponent<ZLevelElevatorComponent>(elevator.Owner);
            elevatorComponent.SecondsPerDepth = secondsPerDepth;
            elevatorComponent.DwellTime = TimeSpan.Zero;

            elevatorUid = elevator.Owner;
        });

        await Pair.RunTicksSync(3);
        return elevatorUid;
    }

    /// <summary>
    ///     Sends the elevator up and stops a few ticks in, while it is still crossing.
    /// </summary>
    private async Task<Entity<KsZLevelGapComponent>> StartAscentIntoGap(EntityUid elevatorUid)
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var elevatorSystem = entManager.System<ZLevelElevatorSystem>();

        await server.WaitPost(() =>
            elevatorSystem.TryStartAscent((elevatorUid, entManager.GetComponent<ZLevelElevatorComponent>(elevatorUid))));

        await Pair.RunTicksSync(2);

        Entity<KsZLevelGapComponent>? gapEntity = null;
        await server.WaitPost(() =>
        {
            var mapUid = MapOf(entManager, elevatorUid);
            if (entManager.TryGetComponent<KsZLevelGapComponent>(mapUid, out var gapComponent))
                gapEntity = (mapUid, gapComponent);
        });

        Assert.That(gapEntity, Is.Not.Null,
            "an elevator part way through a leg has to be on a gap map, or none of the rest of this means anything");

        return gapEntity!.Value;
    }

    private async Task<bool> RunUntilIdle(IEntityManager entManager, EntityUid elevatorUid, int maxTicks = 300)
    {
        var ticks = 0;
        while (ticks < maxTicks &&
               entManager.EntityExists(elevatorUid) &&
               entManager.HasComponent<ActiveZLevelElevatorComponent>(elevatorUid))
        {
            await Pair.RunTicksSync(1);
            ticks++;
        }

        return ticks < maxTicks;
    }

    private static EntityUid MapOf(IEntityManager entManager, EntityUid uid)
    {
        return entManager.GetComponent<TransformComponent>(uid).MapUid ?? EntityUid.Invalid;
    }

    [Test]
    public async Task TestCrossingLeavesTheStackUntouched()
    {
        var shaft = await CreateShaft(3);
        var elevatorUid = await CreateElevator(shaft, floor: 0);

        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var zLevelSystem = entManager.System<KsZLevelSystem>();

        var depthsBefore = new List<float>();
        await server.WaitPost(() =>
        {
            foreach (var zLevelUid in shaft.ZLevels)
                depthsBefore.Add(entManager.GetComponent<KsZLevelComponent>(zLevelUid).Depth);
        });

        await StartAscentIntoGap(elevatorUid);

        await server.WaitAssertion(() =>
        {
            var stackEntities = new List<Entity<KsZLevelComponent>>();
            zLevelSystem.TryGetStack(shaft.ZLevels[0], stackEntities);

            Assert.That(stackEntities.ConvertAll(static entity => entity.Owner), Is.EqualTo(shaft.ZLevels),
                "a gap map must not join the stack it is crossing - it would show up as a floor and renumber every floor above it");

            for (var floor = 0; floor < shaft.ZLevels.Count; floor++)
            {
                Assert.That(entManager.GetComponent<KsZLevelComponent>(shaft.ZLevels[floor]).Depth,
                    Is.EqualTo(depthsBefore[floor]),
                    "a crossing must not repartition the depths of the levels it is between");
            }
        });
    }

    [Test]
    public async Task TestGapAnswersStackQuestionsAboutItsAnchor()
    {
        var shaft = await CreateShaft(3);
        var elevatorUid = await CreateElevator(shaft, floor: 1);

        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var zLevelSystem = entManager.System<KsZLevelSystem>();

        var gapEntity = await StartAscentIntoGap(elevatorUid);

        await server.WaitAssertion(() =>
        {
            var progress = gapEntity.Comp.Progress;
            Assert.That(progress, Is.GreaterThan(0f).And.LessThan(1f),
                "the elevator has to actually be part way up the gap for the rest of this to be measuring anything");

            // This is what audio leak and light leak both attenuate on. If it were wrong, a rider would hear
            //      the floor they just left at the wrong distance and nothing would report an error.
            Assert.That(
                zLevelSystem.TryGetDepthBelow(gapEntity.Owner, gapEntity.Comp.LowerZLevel, out var depth, out var crossings),
                Is.True,
                "the floor below a gap has to be reachable from it, or a rider is cut off from the world");

            Assert.That(depth, Is.EqualTo(progress * gapEntity.Comp.TotalDepth).Within(0.0001f),
                "the distance down to the anchor is how far up the gap the crossing has got");

            Assert.That(crossings, Is.Zero,
                "a gap has no floor plane of its own, so crossing it must not spend anyone's leak budget");

            // And the other way round, which is what someone on the floor above uses to hear the lift
            //      coming up at them. The two have to add up to the whole gap, or a crossing would be in
            //      two places at once depending on who was listening.
            Assert.That(
                zLevelSystem.TryGetDepthBelow(gapEntity.Comp.UpperZLevel, gapEntity.Owner, out var depthFromAbove, out var crossingsFromAbove),
                Is.True,
                "a crossing has to be audible from the floor it is rising towards");

            Assert.That(depthFromAbove + depth, Is.EqualTo(gapEntity.Comp.TotalDepth).Within(0.0001f),
                "the distances to either end of a gap have to add up to the gap");

            Assert.That(crossingsFromAbove, Is.EqualTo(1),
                "the upper z-level's own floor plane lies between it and anything in the gap below it");

            Assert.That(zLevelSystem.GetStackIndex(gapEntity.Owner),
                Is.EqualTo(zLevelSystem.GetStackIndex(gapEntity.Comp.LowerZLevel)),
                "floor numbering has to be unchanged mid-ride");

            Assert.That(zLevelSystem.AreInSameStack(gapEntity.Owner, shaft.ZLevels[2]), Is.True,
                "a crossing is still in the shaft it set out in, or its calls and controllers stop reaching it");

            Assert.That(zLevelSystem.TryGetZLevelBelow(gapEntity.Owner, out var belowEntity), Is.True);
            Assert.That(belowEntity!.Value.Owner, Is.EqualTo(gapEntity.Comp.LowerZLevel),
                "the z-level below a gap is its own anchor - the floor something stepping off would land on");

            Assert.That(zLevelSystem.TryGetZLevelAbove(gapEntity.Owner, out var aboveEntity), Is.True);
            Assert.That(aboveEntity!.Value.Owner, Is.EqualTo(gapEntity.Comp.UpperZLevel),
                "the z-level above a gap is the one it is reaching towards");
        });
    }

    [Test]
    public async Task TestGapCarriesAirAndGravity()
    {
        var shaft = await CreateShaft(2);

        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();

        // Given to the upper z-level, because that is the one a gap copies from - it is a hole in its floor.
        await server.WaitPost(() =>
        {
            var gravityComponent = entManager.EnsureComponent<GravityComponent>(shaft.ZLevels[1]);
            gravityComponent.Enabled = true;
            gravityComponent.Inherent = true;

            entManager.EnsureComponent<MapAtmosphereComponent>(shaft.ZLevels[1]);
        });

        var elevatorUid = await CreateElevator(shaft, floor: 0);
        var gapEntity = await StartAscentIntoGap(elevatorUid);

        await server.WaitAssertion(() =>
        {
            Assert.That(entManager.TryGetComponent<GravityComponent>(gapEntity.Owner, out var gravityComponent), Is.True,
                "a gap without gravity has everyone aboard floating for the length of every ride");
            Assert.That(gravityComponent!.Enabled, Is.True);

            Assert.That(entManager.HasComponent<MapAtmosphereComponent>(gapEntity.Owner), Is.True,
                "a gap without an atmosphere vents the grid's edge tiles to space on every crossing");
        });
    }

    [Test]
    public async Task TestLandingDestroysTheGap()
    {
        var shaft = await CreateShaft(2);
        var elevatorUid = await CreateElevator(shaft, floor: 0);

        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();

        var gapEntity = await StartAscentIntoGap(elevatorUid);

        Assert.That(await RunUntilIdle(entManager, elevatorUid), Is.True, "the elevator never finished its leg");

        await server.WaitAssertion(() =>
        {
            Assert.That(MapOf(entManager, elevatorUid), Is.EqualTo(shaft.ZLevels[1]),
                "the elevator has to end up on the floor it was heading for, not on the gap");

            Assert.That(entManager.Deleted(gapEntity.Owner), Is.True,
                "a gap outlasting its crossing is a dead map for the rest of the round");
        });
    }

    [Test]
    public async Task TestDeletingTheElevatorDestroysTheGap()
    {
        var shaft = await CreateShaft(2);
        var elevatorUid = await CreateElevator(shaft, floor: 0);

        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();

        var gapEntity = await StartAscentIntoGap(elevatorUid);

        // Players will do this, one way or another.
        await server.WaitPost(() => entManager.DeleteEntity(elevatorUid));
        await Pair.RunTicksSync(3);

        await server.WaitAssertion(() =>
            Assert.That(entManager.Deleted(gapEntity.Owner), Is.True,
                "an elevator destroyed mid-flight has to take its gap with it"));
    }

    [Test]
    public async Task TestStoppingMidLegLandsOnTheNearerFloor()
    {
        var shaft = await CreateShaft(2);
        var elevatorUid = await CreateElevator(shaft, floor: 0);

        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var elevatorSystem = entManager.System<ZLevelElevatorSystem>();

        var gapEntity = await StartAscentIntoGap(elevatorUid);

        var wasNearerToBottom = false;
        await server.WaitPost(() =>
        {
            wasNearerToBottom = gapEntity.Comp.Progress < 0.5f;
            elevatorSystem.StopElevator((elevatorUid, entManager.GetComponent<ZLevelElevatorComponent>(elevatorUid)));
        });

        await Pair.RunTicksSync(3);

        await server.WaitAssertion(() =>
        {
            Assert.That(entManager.Deleted(gapEntity.Owner), Is.True,
                "an elevator cannot be left parked on a gap - nobody could ever reach it");

            Assert.That(MapOf(entManager, elevatorUid),
                Is.EqualTo(wasNearerToBottom ? shaft.ZLevels[0] : shaft.ZLevels[1]),
                "a crossing abandoned part way through finishes towards whichever floor it was nearer");
        });
    }

    [Test]
    public async Task TestTwoElevatorsInOneGapAreIndependent()
    {
        var shaft = await CreateShaft(2);
        var firstUid = await CreateElevator(shaft, floor: 0);

        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var transformSystem = entManager.System<SharedTransformSystem>();
        var zLevelSystem = entManager.System<KsZLevelSystem>();

        var secondUid = await CreateElevator(shaft, floor: 0);

        // Out of the first one's way, so neither is flattening the other.
        await server.WaitPost(() => transformSystem.SetWorldPosition(secondUid, new Vector2(8f, 0f)));
        await Pair.RunTicksSync(1);

        var firstGapEntity = await StartAscentIntoGap(firstUid);

        // Sent a good while later, so the two are genuinely at different heights in the same gap.
        await Pair.RunTicksSync(4);
        var secondGapEntity = await StartAscentIntoGap(secondUid);

        await server.WaitAssertion(() =>
        {
            Assert.That(firstGapEntity.Owner, Is.Not.EqualTo(secondGapEntity.Owner),
                "two grids crossing the same gap need a gap map each, or they cannot be at different heights");

            Assert.That(firstGapEntity.Comp.Progress, Is.GreaterThan(secondGapEntity.Comp.Progress),
                "the elevator that set off first has to be further up");

            var stackEntities = new List<Entity<KsZLevelComponent>>();
            zLevelSystem.TryGetStack(shaft.ZLevels[0], stackEntities);

            Assert.That(stackEntities.ConvertAll(static entity => entity.Owner), Is.EqualTo(shaft.ZLevels),
                "two crossings at once still must not add anything to the stack");
        });

        Assert.That(await RunUntilIdle(entManager, firstUid), Is.True);
        Assert.That(await RunUntilIdle(entManager, secondUid), Is.True);

        await server.WaitAssertion(() =>
        {
            Assert.That(MapOf(entManager, firstUid), Is.EqualTo(shaft.ZLevels[1]));
            Assert.That(MapOf(entManager, secondUid), Is.EqualTo(shaft.ZLevels[1]),
                "both have to arrive, however much their crossings overlapped");
        });
    }

    [Test]
    public async Task TestRiderCrossesWithTheElevator()
    {
        var shaft = await CreateShaft(2);
        var elevatorUid = await CreateElevator(shaft, floor: 0);

        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();

        var riderUid = EntityUid.Invalid;
        await server.WaitPost(() =>
            riderUid = entManager.SpawnEntity("MobHuman", new EntityCoordinates(elevatorUid, 0.5f, 0.5f)));

        await Pair.RunTicksSync(3);

        await server.WaitAssertion(() =>
            Assert.That(entManager.GetComponent<TransformComponent>(riderUid).GridUid, Is.EqualTo(elevatorUid),
                "the rider has to be aboard before it sets off, or this is not testing a rider"));

        var gapEntity = await StartAscentIntoGap(elevatorUid);

        await server.WaitAssertion(() =>
        {
            Assert.That(entManager.EntityExists(riderUid), Is.True, "the rider must survive the crossing");

            Assert.That(MapOf(entManager, riderUid), Is.EqualTo(gapEntity.Owner),
                "a rider is carried onto the gap with the grid - which is what puts their camera on it");

            Assert.That(entManager.GetComponent<TransformComponent>(riderUid).GridUid, Is.EqualTo(elevatorUid),
                "and stays parented to the elevator throughout, rather than being dropped onto the map");
        });
    }

    [Test]
    public async Task TestFallingLandsOnACrossingElevator()
    {
        // Covers the crossing path specifically: a faller dropping through the floor plane above is put
        //      into the topmost slice of the airspace below, which is the gap, and then lands on the
        //      platform because the gap's own plane is solid there. It passes with the obstruction event
        //      unsubscribed entirely, because that event is for the other way in -
        //      TestAPlatformRisingPastAFallerCatchesThem is the one that pins it.

        // Three floors so there is a gap above the middle one for the lift to be in while something falls
        //      through that same gap from the top floor.
        var shaft = await CreateShaft(3);

        // Slow, so the lift is unambiguously still crossing when the faller gets down to it. A fall that
        //      arrived after it had landed would pass this test for the wrong reason.
        var elevatorUid = await CreateElevator(shaft, floor: 1, secondsPerDepth: 20f);

        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();

        var gapEntity = await StartAscentIntoGap(elevatorUid);

        var fallerUid = EntityUid.Invalid;
        await server.WaitPost(() =>
        {
            // Dropped from the top floor, directly over the lift, falling fast enough to reach it.
            var topMapId = entManager.GetComponent<MapComponent>(shaft.ZLevels[2]).MapId;
            fallerUid = entManager.SpawnEntity("KsGapTestFaller", new MapCoordinates(ShaftPosition + new Vector2(0.5f, 0.5f), topMapId));
        });

        var physicsSystem = entManager.System<KsZLevelPhysicsSystem>();
        await server.WaitPost(() =>
            physicsSystem.TryStartTransit(fallerUid, initialVerticalVelocity: -4f));

        // Long enough to cross into the middle z-level's gap and reach the lift in it.
        for (var tick = 0; tick < 120; tick++)
        {
            await Pair.RunTicksSync(1);

            var landed = false;
            await server.WaitPost(() =>
                landed = !entManager.HasComponent<KsZLevelTransitComponent>(fallerUid));

            if (landed)
                break;
        }

        await server.WaitAssertion(() =>
        {
            Assert.That(entManager.EntityExists(fallerUid), Is.True);

            Assert.That(MapOf(entManager, fallerUid), Is.EqualTo(gapEntity.Owner),
                "something dropped onto a crossing elevator has to land on it rather than fall through it");

            Assert.That(entManager.GetComponent<TransformComponent>(fallerUid).GridUid, Is.EqualTo(elevatorUid),
                "and end up riding it, which is grid traversal doing its ordinary job once it is on the right map");
        });
    }

    [Test]
    public async Task TestFallingPastAnEmptyGapIsStillAFall()
    {
        var shaft = await CreateShaft(3);

        // Slow, so the lift is unambiguously still crossing when the faller gets down to it. A fall that
        //      arrived after it had landed would pass this test for the wrong reason.
        var elevatorUid = await CreateElevator(shaft, floor: 1, secondsPerDepth: 20f);

        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();

        var gapEntity = await StartAscentIntoGap(elevatorUid);

        var fallerUid = EntityUid.Invalid;
        await server.WaitPost(() =>
        {
            // Well clear of the lift's single tile, so the gap is empty where this comes down.
            var topMapId = entManager.GetComponent<MapComponent>(shaft.ZLevels[2]).MapId;
            fallerUid = entManager.SpawnEntity("KsGapTestFaller", new MapCoordinates(new Vector2(20f, 20f), topMapId));
        });

        var physicsSystem = entManager.System<KsZLevelPhysicsSystem>();
        await server.WaitPost(() =>
            physicsSystem.TryStartTransit(fallerUid, initialVerticalVelocity: -4f));

        await Pair.RunTicksSync(60);

        await server.WaitAssertion(() =>
            Assert.That(MapOf(entManager, fallerUid), Is.Not.EqualTo(gapEntity.Owner),
                "a gap is mostly empty space - falling past the edge of a lift has to stay a fall"));
    }

    [Test]
    public async Task TestElevatorRestoresTheFloorItCutThrough()
    {
        var shaft = await CreateShaft(2);

        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var tileDefinitionManager = server.ResolveDependency<ITileDefinitionManager>();
        var mapSystem = entManager.System<SharedMapSystem>();
        var elevatorSystem = entManager.System<ZLevelElevatorSystem>();

        var tile = new Tile(tileDefinitionManager["Plating"].TileId);

        // The bottom of a shaft is ordinarily floored rather than left open to space, and that pit floor is
        //      exactly what the elevator has to cut through to get down there.
        Entity<MapGridComponent> pit = default;
        await server.WaitPost(() =>
        {
            var bottomMapId = entManager.GetComponent<MapComponent>(shaft.ZLevels[0]).MapId;
            pit = mapSystem.CreateGridEntity(bottomMapId);
            mapSystem.SetTile(pit.Owner, pit.Comp, Vector2i.Zero, tile);

            // A second tile outside the shaft, because a grid whose last tile is removed is deleted
            //      outright and there would be nothing left to restore. A real pit is part of the station
            //      grid, so this is the representative case rather than a convenience.
            mapSystem.SetTile(pit.Owner, pit.Comp, new Vector2i(1, 0), tile);
            entManager.System<SharedTransformSystem>().SetWorldPosition(pit.Owner, ShaftPosition);
        });

        await Pair.RunTicksSync(3);

        var elevatorUid = await CreateElevator(shaft, floor: 1);

        await server.WaitPost(() =>
            elevatorSystem.TryStartDescent((elevatorUid, entManager.GetComponent<ZLevelElevatorComponent>(elevatorUid))));

        Assert.That(await RunUntilIdle(entManager, elevatorUid), Is.True, "the elevator never finished its leg");

        await server.WaitAssertion(() =>
        {
            Assert.That(MapOf(entManager, elevatorUid), Is.EqualTo(shaft.ZLevels[0]));

            // Cut, not shared. Two floors in one tile would collide, and grid traversal would parent a
            //      passenger to whichever it found first rather than reliably to the lift.
            Assert.That(mapSystem.GetTileRef(pit.Owner, pit.Comp!, Vector2i.Zero).Tile.IsEmpty, Is.True,
                "the elevator has to cut the pit floor out while it is standing in it");
        });

        await server.WaitPost(() =>
            elevatorSystem.TryStartAscent((elevatorUid, entManager.GetComponent<ZLevelElevatorComponent>(elevatorUid))));

        Assert.That(await RunUntilIdle(entManager, elevatorUid), Is.True, "the elevator never finished its leg");

        await server.WaitAssertion(() =>
        {
            Assert.That(entManager.EntityExists(pit.Owner), Is.True, "the pit grid must survive being cut");

            Assert.That(mapSystem.GetTileRef(pit.Owner, pit.Comp!, Vector2i.Zero).Tile, Is.EqualTo(tile),
                "and get its floor back when the elevator leaves, rather than being left as space for the round");
        });
    }

    [Test]
    public async Task TestFallingInAShaftAcceleratesUnderTheStationsGravity()
    {
        var shaft = await CreateShaft(2);

        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var tileDefinitionManager = server.ResolveDependency<ITileDefinitionManager>();
        var mapSystem = entManager.System<SharedMapSystem>();
        var physicsSystem = entManager.System<KsZLevelPhysicsSystem>();

        var tile = new Tile(tileDefinitionManager["Plating"].TileId);
        var fallerUid = EntityUid.Invalid;

        await server.WaitPost(() =>
        {
            var topMapId = entManager.GetComponent<MapComponent>(shaft.ZLevels[1]).MapId;

            // A station whose gravity lives on its grid rather than on its map, which is the ordinary
            //      arrangement, with a shaft cut through it. The tile the faller drops down is deliberately
            //      left empty - that hole is the whole point.
            var station = mapSystem.CreateGridEntity(topMapId);
            for (var x = 0; x < 4; x++)
            {
                if (x == 2)
                    continue;

                mapSystem.SetTile(station.Owner, station.Comp, new Vector2i(x, 0), tile);
            }

            var gravityComponent = entManager.EnsureComponent<GravityComponent>(station.Owner);
            gravityComponent.Enabled = true;

            fallerUid = entManager.SpawnEntity("KsGapTestFaller", new MapCoordinates(new Vector2(2.5f, 0.5f), topMapId));
        });

        await Pair.RunTicksSync(3);

        const float initialVelocity = -0.2f;
        await server.WaitPost(() => physicsSystem.TryStartTransit(fallerUid, initialVerticalVelocity: initialVelocity));

        await Pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(entManager.TryGetComponent<KsZLevelTransitComponent>(fallerUid, out var transitComponent), Is.True,
                "five ticks of a slow fall should not have reached the bottom yet");

            // Without this, a shaft has no gravity anywhere in it - TryFindGridAt wants a tile and a shaft
            //      is a hole - so anything dropped down one drifts at whatever speed it set off with.
            Assert.That(transitComponent!.VerticalVelocity, Is.LessThan(initialVelocity),
                "a fall down a shaft has to accelerate under the gravity of the station the shaft is cut through");
        });
    }

    [Test]
    public async Task TestSomethingLeftOnAGapFallsOffIt()
    {
        var shaft = await CreateShaft(2);

        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();

        // Nothing falls anywhere without gravity, and a bare test z-level has none. Set before the crossing
        //      starts, because that is when the gap copies its environment.
        await server.WaitPost(() =>
        {
            foreach (var zLevelUid in shaft.ZLevels)
                entManager.EnsureComponent<GravityComponent>(zLevelUid).Enabled = true;
        });

        // Slow, so the crossing is still going on while the dropped item falls off it.
        var elevatorUid = await CreateElevator(shaft, floor: 0, secondsPerDepth: 20f);

        var gapEntity = await StartAscentIntoGap(elevatorUid);

        var droppedUid = EntityUid.Invalid;
        await server.WaitPost(() =>
        {
            // On the gap itself rather than on the platform - thrown clear of it, or walked off the edge.
            var gapMapId = entManager.GetComponent<MapComponent>(gapEntity.Owner).MapId;
            droppedUid = entManager.SpawnEntity("KsGapTestFaller", new MapCoordinates(new Vector2(20f, 20f), gapMapId));
        });

        await Pair.RunTicksSync(3);

        await server.WaitAssertion(() =>
            Assert.That(MapOf(entManager, droppedUid), Is.Not.EqualTo(gapEntity.Owner),
                "a gap is a slice of air with no floor - something dropped onto one has to keep falling, not hang there until the map is deleted under it"));

        await server.WaitAssertion(() =>
            Assert.That(MapOf(entManager, droppedUid), Is.EqualTo(shaft.ZLevels[0]),
                "and it falls onto the z-level the gap is anchored to"));
    }

    /// <summary>
    ///     That a leg which announces itself and then fails to start does not announce itself at all.
    /// </summary>
    /// <remarks>
    ///     Departing and stopping are a pair: the first starts the travelling hum, fires the moving port and
    ///         tells whatever is wired to it that the lift has gone, and the second is the only thing that
    ///         ever undoes any of it. A leg that raised the first and then could not enter the gap would
    ///         leave all of that latched on with nothing left alive to raise the other half, so the lift
    ///         sits at its floor humming for the rest of the round.
    ///     The failure is reached the way the code itself expects it to be: a stack member that is not a
    ///         map. A stack is replicated as net entities and rebuilt wholesale, so a member briefly being
    ///         something other than a map is a state the navigation code is already written to survive.
    /// </remarks>
    [Test]
    public async Task TestAFailedDepartureIsNotAnnounced()
    {
        var shaft = await CreateShaft(1);
        var elevatorUid = await CreateElevator(shaft, floor: 0);

        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var zLevelSystem = entManager.System<KsZLevelSystem>();
        var elevatorSystem = entManager.System<ZLevelElevatorSystem>();

        await server.WaitPost(() =>
        {
            // A z-level with no map under it. TryGetAdjacentZLevel will happily offer it as the floor above,
            //      and entering a gap against it is what cannot be done.
            var impostorUid = entManager.SpawnEntity(
                null,
                new MapCoordinates(Vector2.Zero, entManager.GetComponent<MapComponent>(shaft.ZLevels[0]).MapId));

            entManager.EnsureComponent<KsZLevelComponent>(impostorUid);
            zLevelSystem.AddZLevelDirectlyAbove(shaft.ZLevels[0], impostorUid);
        });

        await Pair.RunTicksSync(1);

        var started = true;
        await server.WaitPost(() =>
            started = elevatorSystem.TryStartAscent((elevatorUid, entManager.GetComponent<ZLevelElevatorComponent>(elevatorUid))));

        await Pair.RunTicksSync(3);

        await server.WaitAssertion(() =>
        {
            Assert.That(started, Is.False,
                "there is no map above to cross to, so the leg cannot have started");

            var elevatorComponent = entManager.GetComponent<ZLevelElevatorComponent>(elevatorUid);

            Assert.That(elevatorComponent.MovementAudioUid, Is.Null,
                "and a leg that never started must not have left the travelling hum running - nothing will ever stop it");

            Assert.That(entManager.HasComponent<ActiveZLevelElevatorComponent>(elevatorUid), Is.False,
                "nor may it be left looking like it is travelling");

            Assert.That(MapOf(entManager, elevatorUid), Is.EqualTo(shaft.ZLevels[0]),
                "and it stays on the floor it was standing on");
        });
    }

    /// <summary>
    ///     That a platform rising through the airspace a faller is already in catches them.
    /// </summary>
    /// <remarks>
    ///     There are two quite different ways to end up standing on a moving platform, and only one of them
    ///         is the crossing loop. A faller who dropped in through the floor plane above was put into the
    ///         gap's slice on the way in, so the platform is simply the floor of the slice they are already
    ///         falling down - no gap-specific code runs at all.
    ///     This is the other one: a faller who never crossed a plane, because they were launched off this
    ///         very floor and are coming back down to it. They are on the anchor's own map for the whole
    ///         arc, the platform is on a map of its own, and the two share no space to collide in. Only
    ///         <see cref="Content.Shared._KS14.ZLevel.Physics.KsZLevelTransitObstructionEvent"/> connects
    ///         them, and with it unwired the faller drops straight through a solid platform and keeps
    ///         going.
    ///     Driven through the gap system rather than through an elevator, so the platform sits at a known
    ///         altitude instead of wherever a leg's clock has got to.
    /// </remarks>
    [Test]
    public async Task TestAPlatformRisingPastAFallerCatchesThem()
    {
        var shaft = await CreateShaft(2, depth: 1f);

        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var tileDefinitionManager = server.ResolveDependency<ITileDefinitionManager>();
        var mapSystem = entManager.System<SharedMapSystem>();
        var transformSystem = entManager.System<SharedTransformSystem>();
        var gapSystem = entManager.System<KsZLevelGapSystem>();
        var physicsSystem = entManager.System<KsZLevelPhysicsSystem>();

        // Without gravity the faller never comes back down, and the arc is the whole point.
        await server.WaitPost(() =>
            entManager.EnsureComponent<GravityComponent>(shaft.ZLevels[0]).Enabled = true);

        Entity<KsZLevelGapComponent>? gapEntity = null;

        await server.WaitPost(() =>
        {
            var lowerMapId = entManager.GetComponent<MapComponent>(shaft.ZLevels[0]).MapId;

            var platform = mapSystem.CreateGridEntity(lowerMapId);
            mapSystem.SetTile(
                platform.Owner,
                platform.Comp,
                Vector2i.Zero,
                new Tile(tileDefinitionManager["Plating"].TileId));

            transformSystem.SetWorldPosition(platform.Owner, ShaftPosition);

            // Low enough that the faller's arc clears it, high enough to be unmistakably off the floor.
            gapSystem.TryEnterGap(
                platform.Owner,
                (shaft.ZLevels[0], entManager.GetComponent<KsZLevelComponent>(shaft.ZLevels[0])),
                (shaft.ZLevels[1], entManager.GetComponent<KsZLevelComponent>(shaft.ZLevels[1])),
                progress: 0.15f,
                out gapEntity);
        });

        Assert.That(gapEntity, Is.Not.Null, "the platform has to be on a gap for there to be anything to catch on");

        var fallerUid = EntityUid.Invalid;
        await server.WaitPost(() =>
        {
            // On the anchor's own map, over the platform's tile, and thrown upward rather than dropped in
            //      from above - so it never crosses a floor plane and never enters the gap's slice.
            var lowerMapId = entManager.GetComponent<MapComponent>(shaft.ZLevels[0]).MapId;
            fallerUid = entManager.SpawnEntity(
                "KsGapTestFaller",
                new MapCoordinates(ShaftPosition + new Vector2(0.5f, 0.5f), lowerMapId));

            // Apex is well above the platform at this gravity, and well below the floor plane overhead.
            physicsSystem.TryStartTransit(fallerUid, initialVerticalVelocity: 1.5f);
        });

        await server.WaitAssertion(() =>
            Assert.That(MapOf(entManager, fallerUid), Is.EqualTo(shaft.ZLevels[0]),
                "the faller starts in the anchor's airspace - if it is already on the gap then the crossing loop put it there and this tests nothing"));

        for (var tick = 0; tick < 240; tick++)
        {
            await Pair.RunTicksSync(1);

            var landed = false;
            await server.WaitPost(() =>
                landed = !entManager.HasComponent<KsZLevelTransitComponent>(fallerUid));

            if (landed)
                break;
        }

        await server.WaitAssertion(() =>
        {
            Assert.That(entManager.EntityExists(fallerUid), Is.True);

            Assert.That(entManager.HasComponent<KsZLevelTransitComponent>(fallerUid), Is.False,
                "the faller has to have come to rest on something within the time allowed");

            Assert.That(MapOf(entManager, fallerUid), Is.EqualTo(gapEntity!.Value.Owner),
                "a platform rising past a faller has to catch them - unwired, they fall through it and come to rest on the floor below instead");
        });
    }

    /// <summary>
    ///     That two crossings over one z-level divide its airspace correctly even when they measured that
    ///         airspace differently.
    /// </summary>
    /// <remarks>
    ///     Each gap captures the anchor's Depth at the moment it is created, so two departures either side
    ///         of an admin retuning that depth hold different TotalDepths - and from then on their progress
    ///         numbers and their real altitudes are two different orderings. The slice arithmetic reads them
    ///         ascending by altitude to find each slice's ceiling, so taking the order from progress instead
    ///         hands the lower slice a ceiling beneath its own floor.
    ///     A slice of negative height is not a cosmetic problem: Depth is what the fall integration divides
    ///         by and what the render passes accumulate, and the whole reason MinimumDepth exists is that
    ///         both inverted when it went the wrong way.
    /// </remarks>
    [Test]
    public async Task TestSlicesAreOrderedByAltitudeNotProgress()
    {
        var shaft = await CreateShaft(2, depth: 1f);

        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var tileDefinitionManager = server.ResolveDependency<ITileDefinitionManager>();
        var mapSystem = entManager.System<SharedMapSystem>();
        var transformSystem = entManager.System<SharedTransformSystem>();
        var zLevelSystem = entManager.System<KsZLevelSystem>();
        var gapSystem = entManager.System<KsZLevelGapSystem>();

        var tile = new Tile(tileDefinitionManager["Plating"].TileId);

        Entity<KsZLevelGapComponent>? lowerGapEntity = null;
        Entity<KsZLevelGapComponent>? upperGapEntity = null;

        await server.WaitPost(() =>
        {
            var lowerMapId = entManager.GetComponent<MapComponent>(shaft.ZLevels[0]).MapId;
            var upperEntity = (shaft.ZLevels[1], entManager.GetComponent<KsZLevelComponent>(shaft.ZLevels[1]));

            Entity<KsZLevelComponent> anchorEntity =
                (shaft.ZLevels[0], entManager.GetComponent<KsZLevelComponent>(shaft.ZLevels[0]));

            // Captured against a Depth of 1 and sent most of the way up it, so altitude 0.8.
            var firstGrid = mapSystem.CreateGridEntity(lowerMapId);
            mapSystem.SetTile(firstGrid.Owner, firstGrid.Comp, Vector2i.Zero, tile);
            transformSystem.SetWorldPosition(firstGrid.Owner, new Vector2(2f, 0f));
            gapSystem.TryEnterGap(firstGrid.Owner, anchorEntity, upperEntity, progress: 0.8f, out lowerGapEntity);

            // Retuned between the two departures, so the second captures four times the airspace.
            zLevelSystem.SetDepth(shaft.ZLevels[0], 4f);

            // Barely off the ground by its own reckoning, and yet at altitude 1.2 - above the other one.
            var secondGrid = mapSystem.CreateGridEntity(lowerMapId);
            mapSystem.SetTile(secondGrid.Owner, secondGrid.Comp, Vector2i.Zero, tile);
            transformSystem.SetWorldPosition(secondGrid.Owner, new Vector2(6f, 0f));
            gapSystem.TryEnterGap(secondGrid.Owner, anchorEntity, upperEntity, progress: 0.3f, out upperGapEntity);
        });

        await Pair.RunTicksSync(1);

        await server.WaitAssertion(() =>
        {
            Assert.That(lowerGapEntity, Is.Not.Null);
            Assert.That(upperGapEntity, Is.Not.Null);

            var lowerAltitude = SharedKsZLevelGapSystem.GetPlaneAltitude(lowerGapEntity!.Value);
            var upperAltitude = SharedKsZLevelGapSystem.GetPlaneAltitude(upperGapEntity!.Value);

            Assert.That(lowerAltitude, Is.LessThan(upperAltitude),
                "the setup only means anything while the two orderings disagree - progress says the opposite of altitude here");

            var lowerSliceDepth = entManager.GetComponent<KsZLevelComponent>(lowerGapEntity.Value.Owner).Depth;
            var upperSliceDepth = entManager.GetComponent<KsZLevelComponent>(upperGapEntity.Value.Owner).Depth;

            // Ordered by altitude, the lower slice runs up to the crossing above it and the upper one up to
            //      the ceiling. Ordered by progress the two are swapped and the arithmetic goes negative.
            Assert.That(lowerSliceDepth, Is.EqualTo(upperAltitude - lowerAltitude).Within(0.01f),
                "the lower slice owns the air between itself and the crossing above it");

            Assert.That(upperSliceDepth, Is.GreaterThan(KsZLevelSystem.MinimumDepth),
                "and the upper one owns what is left up to the ceiling - never a slice clamped off the bottom, which is what a negative one becomes");
        });
    }
}
