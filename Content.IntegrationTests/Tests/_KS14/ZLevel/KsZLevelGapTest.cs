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

        // Slow, so the crossing is still going on while the dropped item falls off it.
        var elevatorUid = await CreateElevator(shaft, floor: 0, secondsPerDepth: 20f);

        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();

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
}
