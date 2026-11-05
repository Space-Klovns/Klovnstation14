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
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
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
  id: KsTileDangerTestFire
  components:
  - type: NpcTileDanger
    dangers: Fire
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
                SpawnAt(entManager, y == 0 ? Fire : "WallSolid", gridUid, 2, y);
            }
        });

        await Pair.RunTicksSync(NavmeshTicks);

        var path = await FindPath(entManager, npcUid, new EntityCoordinates(gridUid, new Vector2(4.5f, 0.5f)));

        Assert.That(path.Result, Is.EqualTo(PathResult.Path), "a fire should not strand it");
        Assert.That(path.Path.Any(poly => IsOnFireLine(poly, 2, 0, 0)), "it should have gone through");
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

        await Pair.Server.WaitPost(() =>
        {
            npcUid = SpawnAt(entManager, Avoider, gridUid, 0, 0);
            fires.AddRange(SpawnFireLine(entManager, gridUid, 2, -1, 1));
        });

        await Pair.RunTicksSync(NavmeshTicks);

        var target = new EntityCoordinates(gridUid, new Vector2(4.5f, 0.5f));
        var burning = await FindPath(entManager, npcUid, target);
        Assert.That(burning.Path.Any(poly => IsOnFireLine(poly, 2, -1, 1)), Is.False, "it should go round while it burns");

        await Pair.Server.WaitPost(() =>
        {
            foreach (var fireUid in fires)
            {
                entManager.DeleteEntity(fireUid);
            }
        });

        await Pair.RunTicksSync(RefreshTicks);

        var burntOut = await FindPath(entManager, npcUid, target);
        Assert.That(burntOut.Path.Any(poly => IsOnFireLine(poly, 2, -1, 1)), "with the fire out, the straight way is the way");
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
                        entManager.SpawnEntity("WallSolid", mapSystem.GridTileToLocal(testMap.Grid.Owner, testMap.Grid.Comp, tile));
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
        await Walk(entManager, npcUid, new EntityCoordinates(targetUid, Vector2.Zero), 240, 2, -1, 1, 4f);
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
        await Walk(entManager, npcUid, target, 300, 6, -2, 2, 9f);
    }

    /// <summary>
    ///     Nowhere to stand, for one that avoids it: a position is not picked on a burning tile. Every other NPC is offered it.
    /// </summary>
    [TestCase(Avoider, false)]
    [TestCase(Walker, true)]
    public async Task TestFireIsNoPlaceToStand(string mob, bool offered)
    {
        var (entManager, gridUid) = await SetUpGrid();
        var pathfindingSystem = entManager.System<PathfindingSystem>();
        EntityUid npcUid = default;

        await Pair.Server.WaitPost(() =>
        {
            npcUid = SpawnAt(entManager, mob, gridUid, 0, 0);
            SpawnFireLine(entManager, gridUid, 2, -1, 1);
        });

        await Pair.RunTicksSync(NavmeshTicks);

        System.Threading.Tasks.Task<List<PathPoly>> candidatesTask = default!;
        await Pair.Server.WaitPost(() => candidatesTask = pathfindingSystem.GetTacticalCandidates(npcUid,
            entManager.GetComponent<TransformComponent>(npcUid).Coordinates, 4f, 64, default));

        for (var i = 0; i < 120 && !candidatesTask.IsCompleted; i++)
        {
            await Pair.RunTicksSync(1);
        }

        var candidates = await candidatesTask;
        Assert.That(candidates, Is.Not.Empty);
        Assert.That(candidates.Any(poly => IsOnFireLine(poly, 2, -1, 1)), Is.EqualTo(offered));
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
    ///         never setting foot on the fire.
    /// </summary>
    private async Task Walk(IEntityManager entManager, EntityUid npcUid, EntityCoordinates target, int ticks, int x, int fromY, int toY, float reach)
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
                Assert.That((int) MathF.Floor(position.X) == x && position.Y >= fromY && position.Y < toY + 1, Is.False, $"it walked into the fire, at {position}");
            });

            if (position.X >= reach)
                return;
        }

        Assert.Fail($"it never got past the fire: it is at {position}");
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
