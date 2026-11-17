#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.NPC.Dangers;
using Content.Server.Atmos.EntitySystems;
using Content.Server.NPC.Components;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Pathfinding;
using Content.Server.NPC.Systems;
using Content.Shared._KS14.Atmos.ChemicalFire;
using Content.Shared._KS14.CCVar;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Content.Shared.Trigger.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.UnitTesting.Pool;
using static Content.IntegrationTests.Tests._KS14.NPC.KsNpcSquadTestHelpers;

namespace Content.IntegrationTests.Tests._KS14.NPC;

/// <summary>
///     Paths steer clear of tiles posing a danger an NPC avoids, and every other NPC ignores them.
/// </summary>
public sealed class KsNpcTileDangerTest : GameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    private const string Avoider = "KsTileDangerTestAvoider";
    private const string Walker = "KsTileDangerTestWalker";
    private const string Burner = "KsTileDangerTestBurner";
    private const string Fire = "KsTileDangerTestFire";

    /// <summary>
    ///     Long enough for a new navmesh chunk.
    /// </summary>
    private const int NavmeshTicks = 90;

    /// <summary>
    ///     Longer than dangers are kept for before they are worked out again.
    /// </summary>
    private const int RefreshTicks = 20;

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: KsSquadTestMobSyndicate
  id: KsTileDangerTestWalker
  components:
  - type: InputMover
  - type: MobMover
  - type: MovementSpeedModifier

- type: entity
  parent: KsTileDangerTestWalker
  id: KsTileDangerTestAvoider
  components:
  - type: NpcAvoidsTileDanger
    dangers: Fire

- type: entity
  parent: KsTileDangerTestWalker
  id: KsTileDangerTestBlastAvoider
  components:
  - type: NpcAvoidsTileDanger
    dangers: Fire, Blast

- type: entity
  parent: KsTileDangerTestBlastAvoider
  id: KsTileDangerTestFlashAvoider
  components:
  - type: NpcAvoidsTileDanger
    dangers: Fire, Blast, Flash

- type: entity
  parent: KsTileDangerTestFlashAvoider
  id: KsTileDangerTestFlashProof
  components:
  - type: FlashImmunity

- type: entity
  id: KsTileDangerTestFlashbang
  components:
  - type: TimerTrigger
    delay: 1000
  - type: FlashOnTrigger
    keysIn:
    - timer
    range: 3

- type: entity
  id: KsTileDangerTestGrenade
  components:
  - type: TimerTrigger
    delay: 1000
  - type: Explosive
    explosionType: Default
    totalIntensity: 30
    intensitySlope: 5
    maxIntensity: 5

- type: entity
  id: KsTileDangerTestFire
  components:
  - type: NpcTileDanger
    dangers: Fire

- type: entity
  parent: KsTileDangerTestAvoider
  id: KsTileDangerTestBurner
  components:
  - type: Flammable
    canResistFire: true
    damage:
      types:
        Heat: 1.5
  - type: StandingState
  - type: StatusEffectContainer

# The branch a root needs, with something else to be getting on with below it.
- type: htnCompound
  id: KsTileDangerTestRoot
  branches:
  - preconditions:
    - !type:StandingInDangerPrecondition
    tasks:
    - !type:HTNPrimitiveTask
      operator: !type:TacticalPositionOperator
        key: TestExit
        keyCoordinates: TestExitCoordinates
        referenceCoordinatesKey: OwnerCoordinates
        maxRange: 4
        distanceCurve: !type:PresetCurve
          preset: TargetDistance
    - !type:HTNPrimitiveTask
      operator: !type:MoveToOperator
        targetKey: TestExitCoordinates
        pathfindKey: TestExitPathfind
        rangeKey: MovementRangeClose
  - preconditions:
    - !type:KeyExistsPrecondition
      key: TestGoal
    tasks:
    - !type:HTNPrimitiveTask
      operator: !type:MoveToOperator
        targetKey: TestGoal
        pathfindKey: TestGoalPathfind
        rangeKey: MovementRangeClose
        removeKeyOnFinish: false
";

    /// <summary>
    ///     A fire in the way is gone round by an NPC that avoids it. Every other NPC walks straight through it: ignoring
    ///         dangers is the default.
    /// </summary>
    [TestCase(Avoider, false)]
    [TestCase(Walker, true)]
    public async Task TestFireIsGoneRound(string mob, bool crosses)
    {
        var (entManager, gridUid) = await SetUpGrid();
        EntityUid npcUid = default;

        await Pair.Server.WaitPost(() =>
        {
            npcUid = SpawnAt(entManager, mob, gridUid, 0, 0);
            SpawnFireLine(entManager, gridUid, 2, -1, 1);
        });

        await Pair.RunTicksSync(NavmeshTicks);

        var path = await FindPath(entManager, npcUid, new EntityCoordinates(gridUid, new Vector2(4.5f, 0.5f)));

        Assert.That(path.Result, Is.EqualTo(PathResult.Path));
        Assert.That(path.Path.Any(poly => IsOnFireLine(poly, 2, -1, 1)), Is.EqualTo(crosses));
    }

    /// <summary>
    ///     A primed grenade's reach is gone round by an NPC that avoids blasts, rather than walked up to; one not yet primed
    ///         is nothing to go round. See <c>PathfindingSystem.Klovn.Blast.cs</c>.
    /// </summary>
    [TestCase(true)]
    [TestCase(false)]
    public async Task TestPrimedGrenadeIsGoneRound(bool primed)
    {
        var (entManager, gridUid) = await SetUpGrid(size: 10);
        EntityUid npcUid = default;

        await Pair.Server.WaitPost(() =>
        {
            npcUid = SpawnAt(entManager, "KsTileDangerTestBlastAvoider", gridUid, -5, 0);
            var grenadeUid = SpawnAt(entManager, "KsTileDangerTestGrenade", gridUid, 0, 0);

            if (primed)
                Assert.That(entManager.System<TriggerSystem>().ActivateTimerTrigger(grenadeUid), "the fuse should light");
        });

        await Pair.RunTicksSync(NavmeshTicks);
        await Pair.Server.WaitPost(() => entManager.System<PathfindingSystem>().RefreshTileDangersNow());

        var path = await FindPath(entManager, npcUid, new EntityCoordinates(gridUid, new Vector2(5.5f, 0.5f)));

        Assert.That(path.Result, Is.EqualTo(PathResult.Path));
        Assert.That(path.Path.Any(poly => IsOnFireLine(poly, 0, -1, 1)), Is.EqualTo(!primed),
            primed ? "a lit grenade's reach should be gone round" : "an unlit grenade is nothing to go round");
    }

    /// <summary>
    ///     A primed flash's range is gone round too, by one it could blind; one it cannot (flash immunity) walks on through.
    /// </summary>
    [TestCase("KsTileDangerTestFlashAvoider", false)]
    [TestCase("KsTileDangerTestFlashProof", true)]
    public async Task TestPrimedFlashIsGoneRoundByThoseItBlinds(string mob, bool crosses)
    {
        var (entManager, gridUid) = await SetUpGrid(size: 10);
        EntityUid npcUid = default;

        await Pair.Server.WaitPost(() =>
        {
            npcUid = SpawnAt(entManager, mob, gridUid, -5, 0);
            var flashUid = SpawnAt(entManager, "KsTileDangerTestFlashbang", gridUid, 0, 0);
            Assert.That(entManager.System<TriggerSystem>().ActivateTimerTrigger(flashUid), "the fuse should light");
        });

        await Pair.RunTicksSync(NavmeshTicks);
        await Pair.Server.WaitPost(() => entManager.System<PathfindingSystem>().RefreshTileDangersNow());

        var path = await FindPath(entManager, npcUid, new EntityCoordinates(gridUid, new Vector2(5.5f, 0.5f)));

        Assert.That(path.Result, Is.EqualTo(PathResult.Path));
        Assert.That(path.Path.Any(poly => IsOnFireLine(poly, 0, -1, 1)), Is.EqualTo(crosses),
            crosses ? "a flash it is immune to is nothing to go round" : "a lit flash's range should be gone round");
    }

    /// <summary>
    ///     Only the sorts of danger it lists: one that fire cannot hurt leaves fire out, and so crosses it, whatever else it
    ///         avoids.
    /// </summary>
    [Test]
    public async Task TestOtherSortsOfDangerAreIgnored()
    {
        var (entManager, gridUid) = await SetUpGrid();
        EntityUid npcUid = default;

        await Pair.Server.WaitPost(() =>
        {
            npcUid = SpawnAt(entManager, Avoider, gridUid, 0, 0);
            entManager.GetComponent<NpcAvoidsTileDangerComponent>(npcUid).Dangers = (NpcTileDangerKind) (1 << 1); // anything but fire
            SpawnFireLine(entManager, gridUid, 2, -1, 1);
        });

        await Pair.RunTicksSync(NavmeshTicks);

        var path = await FindPath(entManager, npcUid, new EntityCoordinates(gridUid, new Vector2(4.5f, 0.5f)));
        Assert.That(path.Path.Any(poly => IsOnFireLine(poly, 2, -1, 1)), "fire is not what it avoids");
    }

    /// <summary>
    ///     Danger is a cost, not a wall: with no other way, it crosses rather than being stranded.
    /// </summary>
    [Test]
    public async Task TestFireIsCrossedIfItIsTheOnlyWay()
    {
        var (entManager, gridUid) = await SetUpGrid();
        EntityUid npcUid = default;

        await Pair.Server.WaitPost(() =>
        {
            npcUid = SpawnAt(entManager, Avoider, gridUid, 0, 0);

            for (var y = -6; y <= 6; y++)
            {
                SpawnAt(entManager, y == 0 ? Fire : TestWall, gridUid, 2, y);
            }
        });

        await Pair.RunTicksSync(NavmeshTicks);

        var path = await FindPath(entManager, npcUid, new EntityCoordinates(gridUid, new Vector2(4.5f, 0.5f)));

        Assert.That(path.Result, Is.EqualTo(PathResult.Path), "a fire should not strand it");
        Assert.That(path.Path.Any(poly => IsOnFireLine(poly, 2, 0, 0)), "it should have gone through");
    }

    /// <summary>
    ///     Live, with the branch that gets it out of a danger on top: crossing is not standing. The fire is too deep to cross
    ///         between two replans, so turned back by the first, for as long as the way back is the nearest way out, it would
    ///         go back and forth forever.
    /// </summary>
    [Test]
    public async Task TestCrossingAFireGetsAcross()
    {
        var (entManager, gridUid) = await SetUpGrid();
        EntityUid npcUid = default;

        await Pair.Server.WaitPost(() =>
        {
            npcUid = SpawnAt(entManager, Avoider, gridUid, -5, 0);

            for (var x = -3; x <= 3; x++)
            {
                for (var y = -6; y <= 6; y++)
                {
                    SpawnAt(entManager, y == 0 ? Fire : TestWall, gridUid, x, y);
                }
            }
        });

        await Pair.RunTicksSync(NavmeshTicks);
        await Pair.Server.WaitPost(() =>
        {
            var htnComponent = StartRoot(entManager, npcUid);
            htnComponent.Blackboard.SetValue("TestGoal", new EntityCoordinates(gridUid, new Vector2(5.5f, 0.5f)));
        });

        var position = Vector2.Zero;

        for (var i = 0; i < 300 && position.X < 4.5f; i += 5)
        {
            await Pair.RunTicksSync(5);
            await Pair.Server.WaitPost(() => position = entManager.GetComponent<TransformComponent>(npcUid).LocalPosition);
        }

        Assert.That(position.X, Is.GreaterThanOrEqualTo(4.5f), "it never got across the fire");
    }

    /// <summary>
    ///     Live: a fire that starts under it is left, whatever it was doing, for the nearest tile that is not burning. Every
    ///         other NPC stays where it is.
    /// </summary>
    [TestCase(Avoider, true)]
    [TestCase(Walker, false)]
    public async Task TestFireUnderItIsLeft(string mob, bool leaves)
    {
        var (entManager, gridUid) = await SetUpGrid();
        EntityUid npcUid = default;

        await Pair.Server.WaitPost(() => npcUid = SpawnAt(entManager, mob, gridUid, 0, 0));
        await Pair.RunTicksSync(NavmeshTicks);

        await Pair.Server.WaitPost(() =>
        {
            StartRoot(entManager, npcUid);
            SpawnAt(entManager, Fire, gridUid, 0, 0);
        });

        await Pair.RunTicksSync(90);

        await Pair.Server.WaitAssertion(() =>
        {
            var tile = entManager.GetComponent<TransformComponent>(npcUid).LocalPosition.Floored();
            Assert.That(tile != Vector2i.Zero, Is.EqualTo(leaves), $"it is on {tile}");
        });
    }

    /// <summary>
    ///     Stopped on a danger counts, however it got there, for one that avoids it. Following a path across one does not.
    /// </summary>
    [Test]
    public async Task TestWhatCountsAsStandingInDanger()
    {
        var (entManager, gridUid) = await SetUpGrid();
        var steeringSystem = entManager.System<NPCSteeringSystem>();
        var pathfindingSystem = entManager.System<PathfindingSystem>();
        EntityUid avoiderUid = default, walkerUid = default;

        await Pair.Server.WaitPost(() =>
        {
            avoiderUid = SpawnAt(entManager, Avoider, gridUid, 0, 0);
            walkerUid = SpawnAt(entManager, Walker, gridUid, 3, 3);
            SpawnAt(entManager, Fire, gridUid, 0, 0);
            SpawnAt(entManager, Fire, gridUid, 3, 3);
        });

        await Pair.RunTicksSync(NavmeshTicks);

        await Pair.Server.WaitAssertion(() =>
        {
            Assert.That(steeringSystem.IsStandingInDanger(avoiderUid), "with no steering at all, it is stopped");
            Assert.That(steeringSystem.IsStandingInDanger(walkerUid), Is.False, "this one avoids nothing");

            var steeringComponent = steeringSystem.Register(avoiderUid, new EntityCoordinates(gridUid, new Vector2(4.5f, 0.5f)));
            Assert.That(steeringSystem.IsStandingInDanger(avoiderUid), "steering with no path yet is not going anywhere");

            steeringComponent.CurrentPath.Enqueue(pathfindingSystem.GetPoly(new EntityCoordinates(gridUid, new Vector2(1.5f, 0.5f)))!);
            Assert.That(steeringSystem.IsStandingInDanger(avoiderUid), Is.False, "following a path, it is on its way across");

            steeringComponent.CanSeek = false;
            Assert.That(steeringSystem.IsStandingInDanger(avoiderUid), "juking, it is going nowhere, path or no path");
            steeringComponent.CanSeek = true;

            steeringComponent.CurrentPath.Clear();
            steeringComponent.PathfindToken = new System.Threading.CancellationTokenSource();
            Assert.That(steeringSystem.IsStandingInDanger(avoiderUid), Is.False, "waiting on a path, it is on its way too");
            steeringComponent.PathfindToken = null;

            steeringComponent.Status = SteeringStatus.NoPath;
            Assert.That(steeringSystem.IsStandingInDanger(avoiderUid), "steering has given up");

            steeringSystem.Unregister(avoiderUid);
        });
    }

    /// <summary>
    ///     Live: a goal in a fire, like a hostile standing in one, is walked up to and no further, from far enough to need a
    ///         path and from so close that none is asked for. Every other NPC walks right in.
    /// </summary>
    [TestCase(Avoider, 0, false)]
    [TestCase(Avoider, 3, false)]
    [TestCase(Walker, 0, true)]
    public async Task TestGoalInAFireIsStoppedShortOf(string mob, int startX, bool entersFire)
    {
        var (entManager, gridUid) = await SetUpGrid();
        var fire = new Box2(4, 0, 5, 1);
        EntityUid npcUid = default;

        await Pair.Server.WaitPost(() =>
        {
            npcUid = SpawnAt(entManager, mob, gridUid, startX, 0);
            SpawnAt(entManager, Fire, gridUid, 4, 0);
        });

        await Pair.RunTicksSync(NavmeshTicks);

        await Pair.Server.WaitPost(() =>
        {
            entManager.System<NPCSteeringSystem>().Register(npcUid, new EntityCoordinates(gridUid, fire.Center));
            entManager.System<NPCSystem>().WakeNPC(npcUid, entManager.GetComponent<HTNComponent>(npcUid));
        });

        var position = Vector2.Zero;
        var entered = false;

        for (var i = 0; i < 150; i += 2)
        {
            await Pair.RunTicksSync(2);
            await Pair.Server.WaitPost(() => position = entManager.GetComponent<TransformComponent>(npcUid).LocalPosition);
            entered |= fire.Contains(position);
        }

        Assert.That(entered, Is.EqualTo(entersFire), $"it ended up at {position}");

        if (!entersFire)
        {
            await Pair.Server.WaitAssertion(() =>
                Assert.That(entManager.GetComponent<NPCSteeringComponent>(npcUid).Status, Is.EqualTo(SteeringStatus.InRange)));
            Assert.That(position.X, Is.GreaterThan(2.5f), "it should have got as close as it could");
        }
    }

    /// <summary>
    ///     Live: a juke picks its own way, with no path to keep it off a fire, so steering does. Ringed by fire, it stays put.
    ///         Every other NPC steps out.
    /// </summary>
    [TestCase(Avoider, false)]
    [TestCase(Walker, true)]
    public async Task TestJukeDoesNotStepOntoFire(string mob, bool stepsOut)
    {
        var (entManager, gridUid) = await SetUpGrid();
        EntityUid npcUid = default;

        await Pair.Server.WaitPost(() =>
        {
            npcUid = SpawnAt(entManager, mob, gridUid, 0, 0);

            for (var x = -1; x <= 1; x++)
            {
                for (var y = -1; y <= 1; y++)
                {
                    if (x != 0 || y != 0)
                        SpawnAt(entManager, Fire, gridUid, x, y);
                }
            }
        });

        await Pair.RunTicksSync(NavmeshTicks);

        await Pair.Server.WaitPost(() =>
        {
            entManager.EnsureComponent<NPCJukeComponent>(npcUid).JukeType = JukeType.AdjacentTile;
            entManager.System<NPCSteeringSystem>().Register(npcUid, entManager.GetComponent<TransformComponent>(npcUid).Coordinates);
            entManager.System<NPCSystem>().WakeNPC(npcUid, entManager.GetComponent<HTNComponent>(npcUid));
        });

        var steppedOut = false;

        for (var i = 0; i < 90; i += 2)
        {
            await Pair.RunTicksSync(2);
            await Pair.Server.WaitPost(() =>
                steppedOut |= entManager.GetComponent<TransformComponent>(npcUid).LocalPosition.Floored() != Vector2i.Zero);
        }

        Assert.That(steppedOut, Is.EqualTo(stepsOut));
    }

    /// <summary>
    ///     A path leaves a gap beside a fire where that is a short way round: hugging one, a mob at a run swings over the edge
    ///         of its tile on the turn. With no such cost, it is the shortest way.
    /// </summary>
    [TestCase(2f, false)]
    [TestCase(1f, true)]
    public async Task TestFireIsGivenRoom(float marginCost, bool hugs)
    {
        var (entManager, gridUid) = await SetUpGrid();
        EntityUid npcUid = default;

        await Pair.Server.WaitPost(() =>
        {
            npcUid = SpawnAt(entManager, Avoider, gridUid, 0, 0);
            entManager.GetComponent<NpcAvoidsTileDangerComponent>(npcUid).MarginCost = marginCost;
            SpawnFireLine(entManager, gridUid, 2, -1, 1);
        });

        await Pair.RunTicksSync(NavmeshTicks);

        var path = await FindPath(entManager, npcUid, new EntityCoordinates(gridUid, new Vector2(4.5f, 0.5f)));
        var besideFire = new Box2(1, -2, 4, 3);

        Assert.That(path.Path.Any(poly => besideFire.Contains(poly.Box.Center)), Is.EqualTo(hugs));
    }

    /// <summary>
    ///     The room round a fire does not stop at the edge of its chunk: this one is at the corner of four.
    /// </summary>
    [Test]
    public async Task TestRoomRoundAFireCrossesChunks()
    {
        var (entManager, gridUid) = await SetUpGrid();
        var pathfindingSystem = entManager.System<PathfindingSystem>();

        await Pair.Server.WaitPost(() => SpawnAt(entManager, Fire, gridUid, -1, -1));
        await Pair.RunTicksSync(NavmeshTicks);

        await Pair.Server.WaitAssertion(() =>
        {
            pathfindingSystem.RefreshTileDangersNow();

            bool IsNear(int x, int y)
            {
                var poly = pathfindingSystem.GetPoly(new EntityCoordinates(gridUid, new Vector2(x + 0.5f, y + 0.5f)));
                Assert.That(poly, Is.Not.Null);
                return pathfindingSystem.IsNearDanger(poly!, NpcTileDangerKind.Fire);
            }

            Assert.That(IsNear(-1, -1), "the fire itself");
            Assert.That(IsNear(-2, -2), "next to it, in its chunk");
            Assert.That(IsNear(0, -1), "next to it, in the chunk across");
            Assert.That(IsNear(-1, 0), "next to it, in the chunk above");
            Assert.That(IsNear(0, 0), "next to it, in the chunk across its corner");
            Assert.That(IsNear(1, 1), Is.False, "two tiles off");
            Assert.That(IsNear(-3, -1), Is.False, "two tiles off");
        });
    }

    /// <summary>
    ///     A search going by dangers can cost more nodes than one that does not, and runs out sooner. That must not be what
    ///         leaves an NPC with no path: three of those and steering gives up on it, stranded by a fire it could have
    ///         walked through. It searches again as if there were no danger.
    /// </summary>
    [Test]
    public async Task TestRunningOutOfNodesStillFindsAPath()
    {
        var (entManager, gridUid) = await SetUpGrid();
        EntityUid npcUid = default;

        await Pair.Server.WaitPost(() =>
        {
            npcUid = SpawnAt(entManager, Avoider, gridUid, -4, 0);
            SpawnFireLine(entManager, gridUid, 2, -6, 6);
        });

        await Pair.RunTicksSync(NavmeshTicks);

        var configurationManager = Pair.Server.ResolveDependency<IConfigurationManager>();
        var nodeLimit = configurationManager.GetCVar(KsCCVars.NpcPathNodeLimit);
        await Pair.Server.WaitPost(() => configurationManager.SetCVar(KsCCVars.NpcPathNodeLimit, 16));

        try
        {
            var path = await FindPath(entManager, npcUid, new EntityCoordinates(gridUid, new Vector2(4.5f, 0.5f)));
            Assert.That(path.Result, Is.EqualTo(PathResult.Path));
        }
        finally
        {
            await Pair.Server.WaitPost(() => configurationManager.SetCVar(KsCCVars.NpcPathNodeLimit, nodeLimit));
        }
    }

    /// <summary>
    ///     A fire across open floor, a good way off: going round it takes looking at about the area between the two, which
    ///         is more than the usual node limit once the walk is some tiles long. The search for it must have room, not
    ///         give up on the fire (and walk the NPC into it) while the NPC is still too far away to have it in sight.
    /// </summary>
    [Test]
    public async Task TestFireAFewTilesAheadIsGoneRoundWithoutGivingUpOnIt()
    {
        var (entManager, gridUid) = await SetUpGrid(14);
        EntityUid npcUid = default;

        await Pair.Server.WaitPost(() =>
        {
            npcUid = SpawnAt(entManager, Avoider, gridUid, -7, 0);
            SpawnFireLine(entManager, gridUid, 6, -2, 2);
        });

        await Pair.RunTicksSync(NavmeshTicks);

        var path = await FindPath(entManager, npcUid, new EntityCoordinates(gridUid, new Vector2(12.5f, 0.5f)));

        Assert.Multiple(() =>
        {
            Assert.That(path.Result, Is.EqualTo(PathResult.Path));
            Assert.That(path.KsIgnoredDangers, Is.False, "it should have found its way round the fire, not given up on it");
            Assert.That(path.Path.Any(poly => new Box2(6, -2, 7, 3).Contains(poly.Box.Center)), Is.False, "and not through it");
        });
    }

    /// <summary>
    ///     Once a fire goes out, the tile is a tile again.
    /// </summary>
    [Test]
    public async Task TestFireThatHasGoneOutIsNotGoneRound()
    {
        var (entManager, gridUid) = await SetUpGrid();
        EntityUid npcUid = default;
        var fires = new List<EntityUid>();

        // All in one chunk, room round it included, so that the new fire below gets back the very array this one used.
        await Pair.Server.WaitPost(() =>
        {
            npcUid = SpawnAt(entManager, Avoider, gridUid, 0, 2);
            fires.AddRange(SpawnFireLine(entManager, gridUid, 2, 1, 3));
        });

        await Pair.RunTicksSync(NavmeshTicks);

        var target = new EntityCoordinates(gridUid, new Vector2(4.5f, 2.5f));
        var burning = await FindPath(entManager, npcUid, target);
        Assert.That(burning.Path.Any(poly => IsOnFireLine(poly, 2, 1, 3)), Is.False, "it should go round while it burns");

        await Pair.Server.WaitPost(() =>
        {
            foreach (var fireUid in fires)
            {
                entManager.DeleteEntity(fireUid);
            }
        });

        await Pair.RunTicksSync(RefreshTicks);

        var burntOut = await FindPath(entManager, npcUid, target);
        Assert.That(burntOut.Path.Any(poly => IsOnFireLine(poly, 2, 1, 3)), "with the fire out, the straight way is the way");

        // Well off the way, but sharing the chunk where the fire was: none of that fire may come back with it.
        await Pair.Server.WaitPost(() => SpawnAt(entManager, Fire, gridUid, 3, 5));
        await Pair.RunTicksSync(RefreshTicks);

        var relit = await FindPath(entManager, npcUid, target);
        Assert.That(relit.Path.Any(poly => IsOnFireLine(poly, 2, 1, 3)), "only the new fire is burning");
    }

    /// <summary>
    ///     A gas fire is atmospherics' own, with no entity to carry a component, and counts all the same.
    /// </summary>
    [Test]
    public async Task TestGasFireIsADanger()
    {
        var server = Pair.Server;
        var entManager = server.EntMan;
        var testMap = await Pair.CreateTestMap();
        var mapSystem = entManager.System<SharedMapSystem>();
        var fireTile = new Vector2i(0, 0);
        EntityUid npcUid = default;

        await server.WaitPost(() =>
        {
            for (var x = -1; x <= 1; x++)
            {
                for (var y = -1; y <= 1; y++)
                {
                    var tile = new Vector2i(x, y);
                    mapSystem.SetTile(testMap.Grid.Owner, testMap.Grid.Comp, tile, testMap.Tile.Tile);
                    if (tile != fireTile)
                        entManager.SpawnEntity(TestWall, mapSystem.GridTileToLocal(testMap.Grid.Owner, testMap.Grid.Comp, tile));
                }
            }

            entManager.EnsureComponent<GridAtmosphereComponent>(testMap.Grid.Owner);
            npcUid = entManager.SpawnEntity(Avoider, mapSystem.GridTileToLocal(testMap.Grid.Owner, testMap.Grid.Comp, fireTile));
        });

        await server.WaitRunTicks(NavmeshTicks);

        var atmosphereSystem = entManager.System<AtmosphereSystem>();

        await server.WaitPost(() =>
        {
            var mixture = atmosphereSystem.GetTileMixture((testMap.Grid.Owner, null, null), null, fireTile, excite: true);
            Assert.That(mixture, Is.Not.Null);
            mixture!.Clear();
            mixture.Temperature = Atmospherics.T20C;
            mixture.SetMoles(Gas.Oxygen, 500f);
            mixture.SetMoles(Gas.Plasma, 500f);
            atmosphereSystem.HotspotExpose((testMap.Grid.Owner, null), fireTile, 1000f, 500f, soh: true);
        });

        await server.WaitRunTicks(RefreshTicks);

        await server.WaitAssertion(() =>
        {
            var pathfindingSystem = entManager.System<PathfindingSystem>();
            Assert.That(atmosphereSystem.HasGasHotspot((testMap.Grid.Owner, null), fireTile), "the test needs the tile to be burning");

            var dangers = pathfindingSystem.GetAvoidedDangers(npcUid);
            var poly = pathfindingSystem.GetPoly(mapSystem.GridTileToLocal(testMap.Grid.Owner, testMap.Grid.Comp, fireTile));

            Assert.That(dangers, Is.EqualTo(NpcTileDangerKind.Fire));
            Assert.That(poly, Is.Not.Null);
            Assert.That(pathfindingSystem.IsDangerous(poly!, dangers));
        });
    }

    /// <summary>
    ///     Live: chasing something in plain view usually takes a straight line and no path at all, which would go right
    ///         through the fire.
    /// </summary>
    [Test]
    public async Task TestStraightLineChaseGoesRoundFire()
    {
        var (entManager, gridUid) = await SetUpGrid();
        EntityUid npcUid = default, targetUid = default;

        await Pair.Server.WaitPost(() =>
        {
            npcUid = SpawnAt(entManager, Avoider, gridUid, 0, 0);
            targetUid = SpawnAt(entManager, SyndicateMob, gridUid, 5, 0);
            SpawnFireLine(entManager, gridUid, 2, -1, 1);
        });

        await Pair.RunTicksSync(NavmeshTicks);
        await Walk(entManager, npcUid, new EntityCoordinates(targetUid, Vector2.Zero), 240, new Box2(2, -1, 3, 2), 4f);
    }

    /// <summary>
    ///     Live: a fire that starts ahead of it, after its path was found, is gone round too. Paths are not otherwise looked at
    ///         again until they run out.
    /// </summary>
    [Test]
    public async Task TestFireStartingOnThePathIsGoneRound()
    {
        var (entManager, gridUid) = await SetUpGrid(14);
        EntityUid npcUid = default;

        await Pair.Server.WaitPost(() => npcUid = SpawnAt(entManager, Avoider, gridUid, -12, 0));
        await Pair.RunTicksSync(NavmeshTicks);

        var target = new EntityCoordinates(gridUid, new Vector2(12.5f, 0.5f));
        await Pair.Server.WaitPost(() =>
        {
            entManager.System<NPCSteeringSystem>().Register(npcUid, target);
            entManager.System<NPCSystem>().WakeNPC(npcUid, entManager.GetComponent<HTNComponent>(npcUid));
        });

        await Pair.RunTicksSync(15);

        await Pair.Server.WaitAssertion(() =>
            Assert.That(entManager.GetComponent<NPCSteeringComponent>(npcUid).CurrentPath, Is.Not.Empty, "it should have a path before the fire starts"));

        await Pair.Server.WaitPost(() => SpawnFireLine(entManager, gridUid, 6, -2, 2));
        await Walk(entManager, npcUid, target, 300, new Box2(6, -2, 7, 3), 9f);
    }

    /// <summary>
    ///     Live: so is one that starts right beside it, which it would otherwise walk past within a graze of.
    /// </summary>
    [Test]
    public async Task TestFireStartingBesideThePathIsGivenRoom()
    {
        var (entManager, gridUid) = await SetUpGrid(14);
        EntityUid npcUid = default;

        await Pair.Server.WaitPost(() => npcUid = SpawnAt(entManager, Avoider, gridUid, -12, 0));
        await Pair.RunTicksSync(NavmeshTicks);

        var target = new EntityCoordinates(gridUid, new Vector2(12.5f, 0.5f));
        await Pair.Server.WaitPost(() =>
        {
            entManager.System<NPCSteeringSystem>().Register(npcUid, target);
            entManager.System<NPCSystem>().WakeNPC(npcUid, entManager.GetComponent<HTNComponent>(npcUid));
        });

        await Pair.RunTicksSync(15);

        await Pair.Server.WaitPost(() => SpawnFireLine(entManager, gridUid, 6, 1, 3));
        await Walk(entManager, npcUid, target, 300, new Box2(5, 0, 8, 5), 9f);
    }

    /// <summary>
    ///     Live: a fire the path had to go through when it was found is no news. Looking for another path every time it comes
    ///         in sight along it would have every NPC near a long fire asking for a path twice a second, for the same one.
    /// </summary>
    [Test]
    public async Task TestFireThePathCrossesIsNotLookedForAgain()
    {
        var (entManager, gridUid) = await SetUpGrid(10);
        EntityUid npcUid = default;

        await Pair.Server.WaitPost(() =>
        {
            npcUid = SpawnAt(entManager, Avoider, gridUid, -8, 0);

            for (var y = -10; y <= 9; y++)
            {
                SpawnAt(entManager, y == 0 ? Fire : TestWall, gridUid, 2, y);
            }
        });

        await Pair.RunTicksSync(NavmeshTicks);
        await Pair.Server.WaitPost(() =>
        {
            entManager.System<NPCSteeringSystem>().Register(npcUid, new EntityCoordinates(gridUid, new Vector2(6.5f, 0.5f)));
            entManager.System<NPCSystem>().WakeNPC(npcUid, entManager.GetComponent<HTNComponent>(npcUid));
        });

        await Pair.RunTicksSync(15);

        Queue<PathPoly> path = default!;
        await Pair.Server.WaitAssertion(() =>
        {
            path = entManager.GetComponent<NPCSteeringComponent>(npcUid).CurrentPath;
            Assert.That(path.Any(poly => IsOnFireLine(poly, 2, 0, 0)), "the test needs the path through the fire");
        });

        // Two danger checks, well short of the fire.
        await Pair.RunTicksSync(30);

        await Pair.Server.WaitAssertion(() =>
        {
            var steeringComponent = entManager.GetComponent<NPCSteeringComponent>(npcUid);
            Assert.That(entManager.GetComponent<TransformComponent>(npcUid).LocalPosition.X, Is.LessThan(1f), "the test needs it still on its way");
            Assert.That(steeringComponent.CurrentPath, Is.SameAs(path), "it should not have found its path again");
        });
    }

    /// <summary>
    ///     Nowhere to stand, for one that avoids it: a position is not picked on a burning tile. Every other NPC is offered it.
    /// </summary>
    [TestCase(Avoider, false)]
    [TestCase(Walker, true)]
    public async Task TestFireIsNoPlaceToStand(string mob, bool offered)
    {
        var (entManager, gridUid) = await SetUpGrid();
        EntityUid npcUid = default;

        await Pair.Server.WaitPost(() =>
        {
            npcUid = SpawnAt(entManager, mob, gridUid, 0, 0);
            SpawnFireLine(entManager, gridUid, 2, -1, 1);
        });

        await Pair.RunTicksSync(NavmeshTicks);

        var candidates = await GetCandidates(entManager, npcUid);
        Assert.That(candidates, Is.Not.Empty);
        Assert.That(candidates.Any(poly => IsOnFireLine(poly, 2, -1, 1)), Is.EqualTo(offered));
    }

    /// <summary>
    ///     Nor is the tile next to it, while there is anywhere else, or it would get out of a fire only to stand beside it.
    /// </summary>
    [Test]
    public async Task TestBesideFireIsNoPlaceToStand()
    {
        var (entManager, gridUid) = await SetUpGrid();
        EntityUid npcUid = default;

        await Pair.Server.WaitPost(() =>
        {
            npcUid = SpawnAt(entManager, Avoider, gridUid, 0, 0);
            SpawnFireLine(entManager, gridUid, 2, -1, 1);
        });

        await Pair.RunTicksSync(NavmeshTicks);

        var candidates = await GetCandidates(entManager, npcUid);
        var besideFire = new Box2(1, -2, 4, 3);

        Assert.That(candidates, Is.Not.Empty);
        Assert.That(candidates.Any(poly => besideFire.Contains(poly.Box.Center)), Is.False);
    }

    /// <summary>
    ///     With nowhere else, beside a fire will do: better than no spot at all.
    /// </summary>
    [Test]
    public async Task TestBesideFireWillDoIfThereIsNothingElse()
    {
        var (entManager, gridUid) = await SetUpGrid();
        EntityUid npcUid = default;

        await Pair.Server.WaitPost(() =>
        {
            npcUid = SpawnAt(entManager, Avoider, gridUid, 0, 0);
            SpawnAt(entManager, Fire, gridUid, 1, 0);

            for (var x = -1; x <= 2; x++)
            {
                for (var y = -1; y <= 1; y++)
                {
                    if (y != 0 || x == -1 || x == 2)
                        SpawnAt(entManager, TestWall, gridUid, x, y);
                }
            }
        });

        await Pair.RunTicksSync(NavmeshTicks);

        Assert.That(await GetCandidates(entManager, npcUid), Is.Not.Empty);
    }

    /// <summary>
    ///     Live, the two together: set alight on a fire, it gets off it, and only then rolls.
    /// </summary>
    [Test]
    public async Task TestBurningNpcGetsOffTheFireBeforeRolling()
    {
        var (entManager, gridUid) = await SetUpGrid();
        var flammableSystem = entManager.System<FlammableSystem>();
        var timing = Pair.Server.ResolveDependency<Robust.Shared.Timing.IGameTiming>();
        EntityUid npcUid = default;

        await Pair.Server.WaitPost(() => npcUid = SpawnAt(entManager, Burner, gridUid, 0, 0));
        await Pair.RunTicksSync(NavmeshTicks);

        await Pair.Server.WaitPost(() =>
        {
            SpawnAt(entManager, Fire, gridUid, 0, 0);

            // Held at this, with no air here for it to burn in.
            var flammableComponent = entManager.GetComponent<FlammableComponent>(npcUid);
            flammableComponent.NextUpdate = timing.CurTime + System.TimeSpan.FromHours(1);
            flammableSystem.AdjustFireStacks(npcUid, 3f, flammableComponent, ignite: true);

            StartRoot(entManager, npcUid);
        });

        var rolled = false;

        for (var i = 0; i < 150 && !rolled; i += 2)
        {
            await Pair.RunTicksSync(2);

            await Pair.Server.WaitAssertion(() =>
            {
                rolled = entManager.GetComponent<FlammableComponent>(npcUid).Resisting;
                var tile = entManager.GetComponent<TransformComponent>(npcUid).LocalPosition.Floored();

                if (rolled)
                    Assert.That(tile, Is.Not.EqualTo(Vector2i.Zero), "it rolled on the fire");
            });
        }

        Assert.That(rolled, "it never put itself out");
    }

    /// <summary>
    ///     Anything with <see cref="ChemicalFireComponent"/> is a fire to NPCs, found by what it is rather than by name.
    /// </summary>
    [Test]
    public async Task TestEveryChemicalFireIsAFireDanger()
    {
        var factory = Pair.Server.ResolveDependency<IComponentFactory>();
        var found = 0;

        await Pair.Server.WaitAssertion(() =>
        {
            foreach (var proto in Pair.Server.ProtoMan.EnumeratePrototypes<EntityPrototype>())
            {
                if (proto.Abstract || !proto.TryGetComponent<ChemicalFireComponent>(out _, factory))
                    continue;

                found++;
                Assert.That(proto.TryGetComponent<NpcTileDangerComponent>(out var tileDangerComponent, factory), $"{proto.ID} is no danger to NPCs");
                Assert.That(tileDangerComponent!.Dangers & NpcTileDangerKind.Fire, Is.EqualTo(NpcTileDangerKind.Fire), proto.ID);
            }
        });

        Assert.That(found, Is.Not.Zero);
    }

    /// <summary>
    ///     Sets it steering, if it is not already, and has it walk to within <paramref name="reach"/> of x = 0, or further,
    ///         never setting foot in <paramref name="keepOut"/>.
    /// </summary>
    private async Task Walk(IEntityManager entManager, EntityUid npcUid, EntityCoordinates target, int ticks, Box2 keepOut, float reach)
    {
        var transformSystem = entManager.System<SharedTransformSystem>();

        await Pair.Server.WaitPost(() =>
        {
            entManager.System<NPCSteeringSystem>().Register(npcUid, target);
            entManager.System<NPCSystem>().WakeNPC(npcUid, entManager.GetComponent<HTNComponent>(npcUid));
        });

        var position = Vector2.Zero;

        for (var i = 0; i < ticks; i += 2)
        {
            await Pair.RunTicksSync(2);

            await Pair.Server.WaitAssertion(() =>
            {
                position = entManager.GetComponent<TransformComponent>(npcUid).LocalPosition;
                Assert.That(keepOut.Contains(position), Is.False, $"it walked into {keepOut}, at {position}");
            });

            if (position.X >= reach)
                return;
        }

        Assert.Fail($"it never got past the fire: it is at {position}");
    }

    private static HTNComponent StartRoot(IEntityManager entManager, EntityUid npcUid)
    {
        var htnComponent = entManager.GetComponent<HTNComponent>(npcUid);
        htnComponent.RootTask = new HTNCompoundTask { Task = "KsTileDangerTestRoot" };
        entManager.System<HTNSystem>().SetHTNEnabled((npcUid, htnComponent), true);
        entManager.System<NPCSystem>().WakeNPC(npcUid, htnComponent);
        return htnComponent;
    }

    private List<EntityUid> SpawnFireLine(IEntityManager entManager, EntityUid gridUid, int x, int fromY, int toY)
    {
        var fires = new List<EntityUid>();

        for (var y = fromY; y <= toY; y++)
        {
            fires.Add(SpawnAt(entManager, Fire, gridUid, x, y));
        }

        return fires;
    }

    private static bool IsOnFireLine(PathPoly poly, int x, int fromY, int toY)
    {
        var center = poly.Box.Center;
        return (int) MathF.Floor(center.X) == x && center.Y >= fromY && center.Y < toY + 1;
    }

    private async Task<List<PathPoly>> GetCandidates(IEntityManager entManager, EntityUid npcUid)
    {
        System.Threading.Tasks.Task<List<PathPoly>> candidatesTask = default!;
        await Pair.Server.WaitPost(() => candidatesTask = entManager.System<PathfindingSystem>().GetTacticalCandidates(npcUid,
            entManager.GetComponent<TransformComponent>(npcUid).Coordinates, 4f, 64, default));

        for (var i = 0; i < 120 && !candidatesTask.IsCompleted; i++)
        {
            await Pair.RunTicksSync(1);
        }

        return await candidatesTask;
    }

    private async Task<PathResultEvent> FindPath(IEntityManager entManager, EntityUid npcUid, EntityCoordinates target)
    {
        System.Threading.Tasks.Task<PathResultEvent> pathTask = default!;
        await Pair.Server.WaitPost(() => pathTask = entManager.System<PathfindingSystem>().GetPath(npcUid,
            entManager.GetComponent<TransformComponent>(npcUid).Coordinates,
            target,
            0.5f,
            default));

        for (var i = 0; i < 120 && !pathTask.IsCompleted; i++)
        {
            await Pair.RunTicksSync(1);
        }

        Assert.That(pathTask.IsCompletedSuccessfully, "the path request never finished");
        return await pathTask;
    }

    private async Task<(IEntityManager EntManager, EntityUid GridUid)> SetUpGrid(int size = 6)
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var tileDefinitionManager = server.ResolveDependency<ITileDefinitionManager>();
        var map = await Pair.CreateTestMap();
        EntityUid gridUid = default;

        await server.WaitPost(() =>
        {
            gridUid = MakeGrid(entManager, tileDefinitionManager, map.MapId, map.Grid, new Vector2i(-size, -size), new Vector2i(size, size)).Owner;
        });

        return (entManager, gridUid);
    }
}
