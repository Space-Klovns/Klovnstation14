#nullable enable
using System.Collections.Generic;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.ZLevel.Elevators;
using Content.Server.DeviceLinking.Systems;
using Content.Shared._KS14.ZLevel;
using Content.Shared._KS14.ZLevel.Elevators;
using Content.Shared._KS14.ZLevel.Transit;
using Content.Shared.Interaction;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.ZLevel;

/// <summary>
///     Elevators: that they move, that they stop where they were called, that they clear the shaft, and -
///         most of all - that they do not clear anything else.
/// </summary>
public sealed class KsZLevelElevatorTest : GameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    [TestPrototypes]
    private const string Prototypes = @"
# Something to hang the two elevator ports off. Bare on purpose: the component is meant to go on whatever
#   an installation already has - a door on the lift, a shutter in the shaft - so nothing else is needed.
- type: entity
  id: KsElevatorTestSignalSource
  name: test elevator signal source
  components:
  - type: Transform
  - type: ZLevelElevatorSignal

# Stands in for the door an installation would wire the ports to. Two distinct sink ports, so the test can
#   tell a stop from a departure rather than merely counting.
- type: entity
  id: KsElevatorTestSignalSink
  name: test elevator signal sink
  components:
  - type: Transform
  - type: DeviceLinkSink
    ports:
    - Open
    - Close
  - type: TestListener
";

    /// <summary>
    ///     Short, so a leg takes a handful of ticks rather than a handful of seconds, and the fixture is
    ///         still asserting the real clock-driven path rather than a special case.
    /// </summary>
    private const float TestSecondsPerDepth = 0.2f;

    /// <summary>
    ///     Each landing is floored at these two tiles, and deliberately not at <see cref="ShaftTile"/> -
    ///         the shaft column is open space on every floor, which is what an elevator needs to pass.
    /// </summary>
    private static readonly Vector2i[] LandingTiles = [new(0, 0), new(1, 0)];

    private static readonly Vector2i ShaftTile = new(2, 0);

    /// <summary>The world position the elevator grid's single tile occupies on every floor.</summary>
    private static readonly Vector2 ShaftPosition = new(2f, 0f);

    /// <summary>The centre of the shaft, for putting something in the elevator's way.</summary>
    private static readonly Vector2 ShaftCentre = new(2.5f, 0.5f);

    /// <summary>
    ///     A stack of floors, bottom-most first, each with a landing grid and an open shaft.
    /// </summary>
    private sealed record TestShaft(List<EntityUid> ZLevels, List<Entity<MapGridComponent>> Landings)
    {
        public EntityUid Bottom => ZLevels[0];
        public EntityUid Top => ZLevels[^1];
    }

    /// <summary>
    ///     Builds a stack of <paramref name="floorCount"/> linked z-levels, each floored except for the
    ///         shaft column.
    /// </summary>
    private async Task<TestShaft> CreateShaft(int floorCount, float depth = 1f)
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var tileDefinitionManager = server.ResolveDependency<ITileDefinitionManager>();
        var mapSystem = entManager.System<SharedMapSystem>();
        var zLevelSystem = entManager.System<KsZLevelSystem>();

        var tile = new Tile(tileDefinitionManager["Plating"].TileId);
        TestShaft? shaft = null;

        await server.WaitPost(() =>
        {
            var zLevels = new List<EntityUid>();
            var landings = new List<Entity<MapGridComponent>>();

            for (var floor = 0; floor < floorCount; floor++)
            {
                var mapUid = mapSystem.CreateMap(out var mapId);
                var landing = mapSystem.CreateGridEntity(mapId);

                foreach (var landingTile in LandingTiles)
                    mapSystem.SetTile(landing.Owner, landing.Comp, landingTile, tile);

                var zLevelComponent = entManager.EnsureComponent<KsZLevelComponent>(mapUid);

                // Built bottom-up, so each new floor goes directly above the one before it.
                if (floor > 0)
                    zLevelSystem.AddZLevelDirectlyAbove(zLevels[floor - 1], mapUid);

                zLevelSystem.SetDepth((mapUid, zLevelComponent), depth);

                zLevels.Add(mapUid);
                landings.Add(landing);
            }

            shaft = new TestShaft(zLevels, landings);
        });

        Assert.That(shaft, Is.Not.Null, "the test shaft was never built, so nothing below it can be trusted");
        return shaft!;
    }

    /// <summary>
    ///     Puts a single-tile elevator grid in the shaft on the given floor.
    /// </summary>
    private async Task<EntityUid> CreateElevator(TestShaft shaft, int floor)
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

            // The grid's own tile is at its local origin, so placing the grid places the tile.
            transformSystem.SetWorldPosition(elevator.Owner, ShaftPosition);

            var elevatorComponent = entManager.EnsureComponent<ZLevelElevatorComponent>(elevator.Owner);
            elevatorComponent.SecondsPerDepth = TestSecondsPerDepth;
            elevatorComponent.DwellTime = TimeSpan.Zero;

            elevatorUid = elevator.Owner;
        });

        // Grid fixtures are built off the tiles, and both Smimsh and the footprint sweep need them.
        await Pair.RunTicksSync(3);

        return elevatorUid;
    }

    /// <summary>
    ///     Runs ticks until the elevator has nothing left to do, and reports whether it got there.
    /// </summary>
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
    public async Task TestElevatorAscendsOneFloor()
    {
        var shaft = await CreateShaft(2);
        var elevatorUid = await CreateElevator(shaft, floor: 0);

        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var elevatorSystem = entManager.System<ZLevelElevatorSystem>();
        var transformSystem = entManager.System<SharedTransformSystem>();

        var started = false;
        await server.WaitPost(() =>
            started = elevatorSystem.TryStartAscent((elevatorUid, entManager.GetComponent<ZLevelElevatorComponent>(elevatorUid))));

        Assert.That(started, Is.True, "an elevator with a z-level above it should be able to set off");
        Assert.That(await RunUntilIdle(entManager, elevatorUid), Is.True, "the elevator never finished its leg");

        await server.WaitAssertion(() =>
        {
            Assert.That(MapOf(entManager, elevatorUid), Is.EqualTo(shaft.Top),
                "an ascending elevator has to end up on the z-level above the one it left");

            Assert.That(transformSystem.GetWorldPosition(elevatorUid), Is.EqualTo(ShaftPosition),
                "changing floor must not move the elevator horizontally");
        });
    }

    [Test]
    public async Task TestElevatorDescendsOneFloor()
    {
        var shaft = await CreateShaft(2);
        var elevatorUid = await CreateElevator(shaft, floor: 1);

        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var elevatorSystem = entManager.System<ZLevelElevatorSystem>();

        await server.WaitPost(() =>
            elevatorSystem.TryStartDescent((elevatorUid, entManager.GetComponent<ZLevelElevatorComponent>(elevatorUid))));

        Assert.That(await RunUntilIdle(entManager, elevatorUid), Is.True, "the elevator never finished its leg");

        await server.WaitAssertion(() =>
            Assert.That(MapOf(entManager, elevatorUid), Is.EqualTo(shaft.Bottom),
                "a descending elevator has to end up on the z-level below the one it left"));
    }

    [Test]
    public async Task TestRefusesTheEndsOfTheStack()
    {
        var shaft = await CreateShaft(2);
        var topElevatorUid = await CreateElevator(shaft, floor: 1);

        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var elevatorSystem = entManager.System<ZLevelElevatorSystem>();

        await server.WaitAssertion(() =>
        {
            var elevator = (topElevatorUid, entManager.GetComponent<ZLevelElevatorComponent>(topElevatorUid));

            Assert.That(elevatorSystem.TryStartAscent(elevator), Is.False,
                "the top of a stack is a ceiling, so there is nowhere above to go");
            Assert.That(entManager.HasComponent<ActiveZLevelElevatorComponent>(topElevatorUid), Is.False,
                "a refused departure must not leave the elevator marked as travelling");
        });
    }

    [Test]
    public async Task TestTravelTimeScalesWithDepth()
    {
        // A z-level twice as deep is twice as far to cross, exactly as it is for something falling.
        var shaft = await CreateShaft(2, depth: 2f);
        var elevatorUid = await CreateElevator(shaft, floor: 0);

        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var elevatorSystem = entManager.System<ZLevelElevatorSystem>();

        await server.WaitPost(() =>
            elevatorSystem.TryStartAscent((elevatorUid, entManager.GetComponent<ZLevelElevatorComponent>(elevatorUid))));

        await server.WaitAssertion(() =>
        {
            var activeComponent = entManager.GetComponent<ActiveZLevelElevatorComponent>(elevatorUid);
            var duration = (activeComponent.EndTime - activeComponent.StartTime).TotalSeconds;

            Assert.That(duration, Is.EqualTo(2f * TestSecondsPerDepth).Within(0.001d),
                "a leg has to take the crossed z-level's Depth times SecondsPerDepth");
        });
    }

    [Test]
    public async Task TestGibsWhatIsInTheShaft()
    {
        // Descending, and onto the bottom of the stack, because a mob left standing in an open shaft
        //      anywhere else simply falls out of it before the elevator ever gets there. The bottom of a
        //      stack is a floor, so that is the one place something can stand in a shaft and stay put.
        var shaft = await CreateShaft(2);
        var elevatorUid = await CreateElevator(shaft, floor: 1);

        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var elevatorSystem = entManager.System<ZLevelElevatorSystem>();

        var victimUid = EntityUid.Invalid;

        await server.WaitPost(() =>
        {
            var bottomMapId = entManager.GetComponent<MapComponent>(shaft.Bottom).MapId;
            victimUid = entManager.SpawnEntity("MobHuman", new MapCoordinates(ShaftCentre, bottomMapId));
        });

        await Pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(entManager.EntityExists(victimUid), Is.True,
                "the victim has to still be there before the elevator moves, or this proves nothing");
            Assert.That(MapOf(entManager, victimUid), Is.EqualTo(shaft.Bottom),
                "the victim has to have settled in the shaft on the bottom floor");
        });

        await server.WaitPost(() =>
            elevatorSystem.TryStartDescent((elevatorUid, entManager.GetComponent<ZLevelElevatorComponent>(elevatorUid))));

        Assert.That(await RunUntilIdle(entManager, elevatorUid), Is.True, "the elevator never finished its leg");
        await Pair.RunTicksSync(3);

        await server.WaitAssertion(() =>
            Assert.That(entManager.EntityExists(victimUid), Is.False,
                "anything loose in the shaft should have been gibbed by the arriving elevator"));
    }

    [Test]
    public async Task TestDoesNotGibRiders()
    {
        var shaft = await CreateShaft(2);
        var elevatorUid = await CreateElevator(shaft, floor: 0);

        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var elevatorSystem = entManager.System<ZLevelElevatorSystem>();

        var riderUid = EntityUid.Invalid;

        await server.WaitPost(() =>
            riderUid = entManager.SpawnEntity("MobHuman", new EntityCoordinates(elevatorUid, 0.5f, 0.5f)));

        // Grid traversal has to have settled and parented the rider to the elevator, or this would be
        //      testing that an entity on the wrong map survives, which proves nothing.
        await Pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
            Assert.That(entManager.GetComponent<TransformComponent>(riderUid).GridUid, Is.EqualTo(elevatorUid),
                "the rider has to be parented to the elevator before it moves, or it is not a rider"));

        await server.WaitPost(() =>
            elevatorSystem.TryStartAscent((elevatorUid, entManager.GetComponent<ZLevelElevatorComponent>(elevatorUid))));

        Assert.That(await RunUntilIdle(entManager, elevatorUid), Is.True, "the elevator never finished its leg");
        await Pair.RunTicksSync(3);

        await server.WaitAssertion(() =>
        {
            Assert.That(entManager.EntityExists(riderUid), Is.True,
                "a passenger standing on the elevator must survive the trip");
            Assert.That(MapOf(entManager, riderUid), Is.EqualTo(shaft.Top),
                "a passenger has to come along to the floor the elevator went to");
        });
    }

    [Test]
    public async Task TestDoesNotGibTheLanding()
    {
        var shaft = await CreateShaft(2);
        var elevatorUid = await CreateElevator(shaft, floor: 0);

        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var elevatorSystem = entManager.System<ZLevelElevatorSystem>();
        var mapSystem = entManager.System<SharedMapSystem>();

        var wallUid = EntityUid.Invalid;
        var upperLanding = shaft.Landings[1];

        await server.WaitPost(() =>
            // Anchored to the upper landing, beside the shaft - a shaft wall, in other words.
            wallUid = entManager.SpawnEntity("WallSolid", new EntityCoordinates(upperLanding.Owner, 1.5f, 0.5f)));

        await Pair.RunTicksSync(3);

        await server.WaitPost(() =>
            elevatorSystem.TryStartAscent((elevatorUid, entManager.GetComponent<ZLevelElevatorComponent>(elevatorUid))));

        Assert.That(await RunUntilIdle(entManager, elevatorUid), Is.True, "the elevator never finished its leg");
        await Pair.RunTicksSync(3);

        await server.WaitAssertion(() =>
        {
            Assert.That(entManager.EntityExists(wallUid), Is.True,
                "a wall anchored to the landing beside the shaft is not in the elevator's way, and must survive");

            foreach (var landingTile in LandingTiles)
            {
                Assert.That(
                    mapSystem.GetTileRef(upperLanding.Owner, upperLanding.Comp, landingTile).Tile.IsEmpty,
                    Is.False,
                    "the landing's own floor is not in the shaft, so arriving must not cut it away");
            }
        });
    }

    [Test]
    public async Task TestForcesThroughObstructingTiles()
    {
        var shaft = await CreateShaft(2);
        var elevatorUid = await CreateElevator(shaft, floor: 0);

        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var tileDefinitionManager = server.ResolveDependency<ITileDefinitionManager>();
        var elevatorSystem = entManager.System<ZLevelElevatorSystem>();
        var mapSystem = entManager.System<SharedMapSystem>();

        var upperLanding = shaft.Landings[1];

        // Somebody has plated over the shaft on the floor above. The elevator shears through rather than
        //      refusing, so that this cannot be used to trap anyone inside one.
        await server.WaitPost(() =>
            mapSystem.SetTile(
                upperLanding.Owner,
                upperLanding.Comp,
                ShaftTile,
                new Tile(tileDefinitionManager["Plating"].TileId)));

        await Pair.RunTicksSync(3);

        await server.WaitPost(() =>
            elevatorSystem.TryStartAscent((elevatorUid, entManager.GetComponent<ZLevelElevatorComponent>(elevatorUid))));

        Assert.That(await RunUntilIdle(entManager, elevatorUid), Is.True, "the elevator never finished its leg");
        await Pair.RunTicksSync(3);

        await server.WaitAssertion(() =>
        {
            Assert.That(MapOf(entManager, elevatorUid), Is.EqualTo(shaft.Top),
                "a plated-over shaft must not stop the elevator");

            Assert.That(
                mapSystem.GetTileRef(upperLanding.Owner, upperLanding.Comp, ShaftTile).Tile.IsEmpty,
                Is.True,
                "the tile in the elevator's way should have been cut out of the landing");
        });
    }

    [Test]
    public async Task TestServesCallsInSweepOrder()
    {
        // Floors 1..4, elevator at 1. Called to 3 and then 2, it must still stop at 2 first: a lift serves
        //      what is on the way before what was asked for first.
        var shaft = await CreateShaft(4);
        var elevatorUid = await CreateElevator(shaft, floor: 0);

        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var elevatorSystem = entManager.System<ZLevelElevatorSystem>();

        var visited = new List<EntityUid>();
        var dwelledOnFloorTwo = false;

        await server.WaitPost(() =>
        {
            var elevatorComponent = entManager.GetComponent<ZLevelElevatorComponent>(elevatorUid);

            // Long enough that a stop lasts several ticks, so the sampling below cannot miss one. Without
            //      this the path alone would prove nothing: an elevator climbing to floor 3 passes floor 2
            //      whether it was called there or not, so what is actually under test is that it *stops*.
            elevatorComponent.DwellTime = TimeSpan.FromSeconds(0.3);

            var elevator = (elevatorUid, elevatorComponent);
            elevatorSystem.TryCallToZLevel(elevator, shaft.ZLevels[2]);
            elevatorSystem.TryCallToZLevel(elevator, shaft.ZLevels[1]);
        });

        // Sampled per tick rather than from an event, so the assertion is about where the elevator actually
        //      was rather than about what it announced.
        for (var tick = 0; tick < 300; tick++)
        {
            await Pair.RunTicksSync(1);

            var reachedEnd = false;
            await server.WaitPost(() =>
            {
                var mapUid = MapOf(entManager, elevatorUid);

                // The floor under the lift, which mid-crossing is the gap's anchor rather than the gap
                //      itself. What is under test is the order of the floors, and a lift on its way
                //      between two of them has not reached the next one yet.
                if (entManager.TryGetComponent<KsZLevelGapComponent>(mapUid, out var gapComponent))
                    mapUid = gapComponent.LowerZLevel;

                if (visited.Count == 0 || visited[^1] != mapUid)
                    visited.Add(mapUid);

                var isDwelling =
                    entManager.TryGetComponent<ActiveZLevelElevatorComponent>(elevatorUid, out var activeComponent) &&
                    activeComponent.State == ZLevelElevatorState.Dwelling;

                if (isDwelling && mapUid == shaft.ZLevels[1])
                    dwelledOnFloorTwo = true;

                reachedEnd = !entManager.HasComponent<ActiveZLevelElevatorComponent>(elevatorUid) &&
                             mapUid == shaft.ZLevels[2];
            });

            if (reachedEnd)
                break;
        }

        Assert.Multiple(() =>
        {
            Assert.That(visited, Is.EqualTo(new List<EntityUid>
            {
                shaft.ZLevels[0],
                shaft.ZLevels[1],
                shaft.ZLevels[2],
            }), "the elevator has to climb one z-level at a time rather than jumping straight to the top");

            Assert.That(dwelledOnFloorTwo, Is.True,
                "floor 2 was called while the lift was on its way to floor 3, so it has to stop there on the way past");

            Assert.That(entManager.GetComponent<ZLevelElevatorComponent>(elevatorUid).CalledZLevels,
                Is.Empty,
                "every call should have been served by the time the elevator came to rest");
        });
    }

    [Test]
    public async Task TestTurnsRoundOnceNothingIsLeftAbove()
    {
        var shaft = await CreateShaft(3);
        var elevatorUid = await CreateElevator(shaft, floor: 1);

        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var elevatorSystem = entManager.System<ZLevelElevatorSystem>();

        await server.WaitPost(() =>
        {
            var elevator = (elevatorUid, entManager.GetComponent<ZLevelElevatorComponent>(elevatorUid));

            // Both directions are called at once. The lift finishes its upward sweep before turning round,
            //      so the bottom floor is served last even though it is the same distance away.
            elevatorSystem.TryCallToZLevel(elevator, shaft.ZLevels[2]);
            elevatorSystem.TryCallToZLevel(elevator, shaft.ZLevels[0]);
        });

        var reachedTop = false;
        for (var tick = 0; tick < 300 && !reachedTop; tick++)
        {
            await Pair.RunTicksSync(1);
            await server.WaitPost(() => reachedTop = MapOf(entManager, elevatorUid) == shaft.ZLevels[2]);
        }

        Assert.That(reachedTop, Is.True, "the elevator should have finished its upward sweep first");
        Assert.That(await RunUntilIdle(entManager, elevatorUid), Is.True, "the elevator never came to rest");

        await server.WaitAssertion(() =>
            Assert.That(MapOf(entManager, elevatorUid), Is.EqualTo(shaft.ZLevels[0]),
                "having run out of calls above it, the elevator should have turned round and served the one below"));
    }

    [Test]
    public async Task TestSurvivesItsGridBeingDeletedMidLeg()
    {
        var shaft = await CreateShaft(2);
        var elevatorUid = await CreateElevator(shaft, floor: 0);

        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var elevatorSystem = entManager.System<ZLevelElevatorSystem>();

        await server.WaitPost(() =>
            elevatorSystem.TryStartAscent((elevatorUid, entManager.GetComponent<ZLevelElevatorComponent>(elevatorUid))));

        await server.WaitPost(() => entManager.DeleteEntity(elevatorUid));

        // Nothing to assert beyond the pair staying up: an unhandled throw out of the elevator update would
        //      fail the test on its own, and that is precisely the regression worth guarding.
        await Pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
            Assert.That(entManager.EntityExists(elevatorUid), Is.False,
                "the elevator grid was deleted, so nothing should have brought it back"));
    }

    [Test]
    public async Task TestCallButtonCallsTheLiftToItsOwnFloor()
    {
        var shaft = await CreateShaft(2);
        var elevatorUid = await CreateElevator(shaft, floor: 0);

        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var interactionSystem = entManager.System<SharedInteractionSystem>();

        var buttonUid = EntityUid.Invalid;
        var userUid = EntityUid.Invalid;
        var upperLanding = shaft.Landings[1];

        await server.WaitPost(() =>
        {
            // On the upper landing, beside the shaft - the floor the lift is not on.
            buttonUid = entManager.SpawnEntity("KsElevatorCallButton", new EntityCoordinates(upperLanding.Owner, 1.5f, 0.5f));
            userUid = entManager.SpawnEntity("MobHuman", new EntityCoordinates(upperLanding.Owner, 0.5f, 0.5f));
        });

        await Pair.RunTicksSync(5);

        await server.WaitPost(() => interactionSystem.InteractionActivate(userUid, buttonUid));

        Assert.That(await RunUntilIdle(entManager, elevatorUid), Is.True, "the elevator never came to rest");

        await server.WaitAssertion(() =>
            Assert.That(MapOf(entManager, elevatorUid), Is.EqualTo(shaft.Top),
                "a call button has to bring the lift to the floor the button itself is on"));
    }

    /// <summary>
    ///     That an elevator sitting out its dwell at a floor does not still report itself as travelling.
    /// </summary>
    /// <remarks>
    ///     A dwell keeps <see cref="ActiveZLevelElevatorComponent"/> attached, so "is the component there"
    ///         answers yes for several seconds after the lift has physically arrived, opened its doors and
    ///         crushed whatever was standing where it landed. Everything a player can see or press used to
    ///         ask exactly that question, so for the length of every dwell the panel read "Descending"
    ///         about a lift that was demonstrably parked, and a call button on that floor queued a call
    ///         instead of saying it was already here.
    ///     Pinned against the stop signal rather than against the state on its own, because the bug was
    ///         never that the lift was in the wrong state - it was that two things disagreed. Once the
    ///         doors have been told to open, nothing else may still be claiming the lift is on its way.
    /// </remarks>
    [Test]
    public async Task TestDwellingAtAFloorDoesNotReportAsTravelling()
    {
        var shaft = await CreateShaft(2);
        var elevatorUid = await CreateElevator(shaft, floor: 0);

        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var elevatorSystem = entManager.System<ZLevelElevatorSystem>();
        var deviceLinkSystem = entManager.System<DeviceLinkSystem>();
        var listenerSystem = entManager.System<KsZLevelTestListenerSystem>();

        listenerSystem.Reset();

        await server.WaitPost(() =>
        {
            // Long enough that the dwell spans many ticks, so the sampling below lands inside it. The
            //      reported symptom is precisely that this window is not instantaneous.
            entManager.GetComponent<ZLevelElevatorComponent>(elevatorUid).DwellTime = TimeSpan.FromSeconds(2);

            // Riding the lift, so the emitter resolves to the one it is standing on. Its stop port is what
            //      an installation would hang its doors off.
            var sourceUid = entManager.SpawnEntity("KsElevatorTestSignalSource", new EntityCoordinates(elevatorUid, 0.5f, 0.5f));
            var sinkUid = entManager.SpawnEntity("KsElevatorTestSignalSink", new EntityCoordinates(elevatorUid, 0.5f, 0.5f));

            deviceLinkSystem.SaveLinks(
                null,
                sourceUid,
                sinkUid,
                [("KsElevatorStopped", "Open"), ("KsElevatorMoving", "Close")]
            );
        });

        await Pair.RunTicksSync(3);

        // Called to the floor above rather than simply sent there: only a floor it was actually called to
        //      earns a dwell, and the dwell is the whole of what is under test.
        await server.WaitPost(() =>
            elevatorSystem.TryCallToZLevel(
                (elevatorUid, entManager.GetComponent<ZLevelElevatorComponent>(elevatorUid)),
                shaft.ZLevels[1]));

        var observedDwell = false;

        for (var tick = 0; tick < 300; tick++)
        {
            await Pair.RunTicksSync(1);

            var finished = false;

            await server.WaitAssertion(() =>
            {
                if (!entManager.TryGetComponent<ActiveZLevelElevatorComponent>(elevatorUid, out var activeComponent))
                {
                    finished = true;
                    return;
                }

                if (activeComponent.State != ZLevelElevatorState.Dwelling)
                    return;

                observedDwell = true;

                Assert.That(MapOf(entManager, elevatorUid), Is.EqualTo(shaft.ZLevels[1]),
                    "a dwelling lift is standing on a floor, not on a gap");

                Assert.That(listenerSystem.SignalsReceived, Does.Contain("Open"),
                    "the stop signal has already gone out by the time a dwell starts - the doors are opening");

                Assert.That(elevatorSystem.IsTravelling(elevatorUid), Is.False,
                    "so nothing may still report the lift as travelling: the panel says Descending over an open door, and a call button on this floor queues a call instead of saying it is already here");
            });

            if (finished)
                break;
        }

        Assert.That(observedDwell, Is.True,
            "the lift never dwelled, so this asserted nothing - it has to be called to a floor, not merely sent past one");
    }

    [Test]
    public async Task TestSignalsOnDepartureAndOnStopping()
    {
        var shaft = await CreateShaft(2);
        var elevatorUid = await CreateElevator(shaft, floor: 0);

        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var elevatorSystem = entManager.System<ZLevelElevatorSystem>();
        var deviceLinkSystem = entManager.System<DeviceLinkSystem>();
        var listenerSystem = entManager.System<KsZLevelTestListenerSystem>();

        listenerSystem.Reset();

        await server.WaitPost(() =>
        {
            // Both ride the elevator, so the emitter resolves to the lift it is standing on.
            var sourceUid = entManager.SpawnEntity("KsElevatorTestSignalSource", new EntityCoordinates(elevatorUid, 0.5f, 0.5f));
            var sinkUid = entManager.SpawnEntity("KsElevatorTestSignalSink", new EntityCoordinates(elevatorUid, 0.5f, 0.5f));

            deviceLinkSystem.SaveLinks(
                null,
                sourceUid,
                sinkUid,
                [("KsElevatorStopped", "Open"), ("KsElevatorMoving", "Close")]
            );
        });

        await Pair.RunTicksSync(3);

        await server.WaitPost(() =>
            elevatorSystem.TryStartAscent((elevatorUid, entManager.GetComponent<ZLevelElevatorComponent>(elevatorUid))));

        Assert.That(await RunUntilIdle(entManager, elevatorUid), Is.True, "the elevator never came to rest");
        await Pair.RunTicksSync(3);

        await server.WaitAssertion(() =>
            // Departure first, so that a door wired to it has the whole journey to finish closing, and the
            //      stop only once the lift has actually arrived.
            Assert.That(listenerSystem.SignalsReceived, Is.EqualTo(new List<string> { "Close", "Open" }),
                "an elevator has to signal that it is leaving before it goes, and that it has stopped once it arrives"));
    }
}
