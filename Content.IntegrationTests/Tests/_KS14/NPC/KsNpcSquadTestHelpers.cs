#nullable enable
using System.Collections.Generic;
using System.Numerics;
using Content.Server.NPC.Pathfinding;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.NPC;

/// <summary>
///     Shared grid building and test prototypes for the NPC squad tests.
/// </summary>
public static class KsNpcSquadTestHelpers
{
    public const string SyndicateMob = "KsSquadTestMobSyndicate";
    public const string NanoTrasenMob = "KsSquadTestMobNanoTrasen";
    public const string WallHuggerMob = "KsSquadTestMobWallHugger";
    public const string FollowerMob = "KsSquadTestMobFollower";
    public const string SharingLeaderMob = "KsSquadTestMobSharingLeader";
    public const string SharedKey = "KsSquadTestSharedKey";
    public const string OpenFloorMob = "KsSquadTestMobOpenFloor";
    public const string TestCrowbar = "KsTestCrowbar";
    public const string TestJawsOfLife = "KsTestJawsOfLife";
    public const string TestAccessBreaker = "KsTestAccessBreaker";

    /// <summary>
    ///     A Syndicate mob with perception but no squad, so nothing a squadmate sees makes it alert: for testing its
    ///         own reaction time.
    /// </summary>
    public const string LonerMob = "KsSquadTestMobLoner";

    /// <summary>
    ///     HTN is present but disabled: squads require an NPC, but these must stay exactly where they are put.
    ///         Hard mob fixtures, so room analysis sees walls the way a real mob does.
    /// </summary>
    [TestPrototypes]
    public const string Prototypes = @"
- type: entity
  id: KsSquadTestMobSyndicate
  components:
  - type: Physics
    bodyType: KinematicController
  - type: Fixtures
    fixtures:
      fix1:
        shape: !type:PhysShapeCircle
          radius: 0.35
        density: 185
        mask:
        - MobMask
        layer:
        - MobLayer
  - type: MobState
  - type: MobThresholds
    thresholds:
      0: Alive
      100: Critical
      200: Dead
  - type: Damageable
    damageContainer: Biological
  - type: HTN
    enabled: false
    rootTask:
      task: IdleCompound
  - type: NpcFactionMember
    factions:
    - Syndicate
  - type: NpcSquadMember
  - type: NpcSensors
  - type: NpcPerception

- type: entity
  id: KsSquadTestMobLoner
  components:
  - type: Physics
    bodyType: KinematicController
  - type: Fixtures
    fixtures:
      fix1:
        shape: !type:PhysShapeCircle
          radius: 0.35
        density: 185
        mask:
        - MobMask
        layer:
        - MobLayer
  - type: MobState
  - type: MobThresholds
    thresholds:
      0: Alive
      100: Critical
      200: Dead
  - type: Damageable
    damageContainer: Biological
  - type: HTN
    enabled: false
    rootTask:
      task: IdleCompound
  - type: NpcFactionMember
    factions:
    - Syndicate
  - type: NpcPerception

- type: entity
  parent: KsSquadTestMobSyndicate
  id: KsSquadTestMobNanoTrasen
  components:
  - type: NpcFactionMember
    factions:
    - NanoTrasen

- type: entity
  parent: KsSquadTestMobSyndicate
  id: KsSquadTestMobFollower
  components:
  - type: NpcSquadMember
    canLead: false

- type: entity
  parent: KsSquadTestMobSyndicate
  id: KsSquadTestMobSharingLeader
  components:
  - type: NpcSquadMember
    sharedBlackboardKeys:
    - KsSquadTestSharedKey

- type: entity
  parent: KsSquadTestMobSyndicate
  id: KsSquadTestMobWallHugger
  components:
  - type: NpcSquadMember
    cover:
      wallPreference: 1

- type: entity
  parent: KsSquadTestMobSyndicate
  id: KsSquadTestMobOpenFloor
  components:
  - type: NpcSquadMember
    cover:
      wallPreference: -1

# Real tools, with what the door tests' assertions rely on about them spelled out: which doors each one pries, and
#   that the breaker breaks access.
- type: entity
  parent: Crowbar
  id: KsTestCrowbar
  components:
  - type: Prying
    pryPowered: false

- type: entity
  parent: JawsOfLife
  id: KsTestJawsOfLife
  components:
  - type: Prying
    pryPowered: true

- type: entity
  parent: AccessBreaker
  id: KsTestAccessBreaker
  components:
  - type: Emag
    emagType: Access
";

    /// <summary>
    ///     Replaces the test map's grid with one covering the given tile rectangle, inclusive.
    /// </summary>
    public static Entity<MapGridComponent> MakeGrid(
        IEntityManager entManager,
        ITileDefinitionManager tileDefinitionManager,
        MapId mapId,
        EntityUid oldGridUid,
        Vector2i min,
        Vector2i max)
    {
        var mapSystem = entManager.System<SharedMapSystem>();
        entManager.DeleteEntity(oldGridUid);

        var grid = mapSystem.CreateGridEntity(mapId);
        var plating = new Tile(tileDefinitionManager["Plating"].TileId);
        var tiles = new List<(Vector2i, Tile)>();

        for (var x = min.X; x <= max.X; x++)
        {
            for (var y = min.Y; y <= max.Y; y++)
            {
                tiles.Add((new Vector2i(x, y), plating));
            }
        }

        mapSystem.SetTiles(grid.Owner, grid.Comp, tiles);
        return grid;
    }

    /// <summary>
    ///     Runs ticks until the pathfinding navmesh covers every tile of the rectangle, inclusive. A new grid's
    ///         navmesh is only built on the pathfinding system's own cooldown (about half a second), and until then
    ///         <c>GetPoly</c> is null everywhere on it, which room analysis reads as walls all round: an NPC spawned
    ///         early cannot see that any squad is within walking distance. Fails the test if it never appears.
    /// </summary>
    public static async Task WaitForNavmesh(Content.IntegrationTests.Pair.TestPair pair, EntityUid gridUid, Vector2i min, Vector2i max, int maxTicks = 300)
    {
        var entManager = pair.Server.ResolveDependency<IEntityManager>();
        var pathfindingSystem = entManager.System<PathfindingSystem>();
        var mapSystem = entManager.System<SharedMapSystem>();
        var missingTile = min;

        bool Covered()
        {
            if (!entManager.TryGetComponent<MapGridComponent>(gridUid, out var gridComponent))
                return false;

            for (var x = min.X; x <= max.X; x++)
            {
                for (var y = min.Y; y <= max.Y; y++)
                {
                    var tile = new Vector2i(x, y);
                    var coordinates = new EntityCoordinates(gridUid, mapSystem.TileCenterToVector((gridUid, gridComponent), tile));

                    if (pathfindingSystem.GetPoly(coordinates) != null)
                        continue;

                    missingTile = tile;
                    return false;
                }
            }

            return true;
        }

        for (var i = 0; i < maxTicks; i++)
        {
            var covered = false;
            await pair.Server.WaitPost(() => covered = Covered());

            if (covered)
                return;

            await pair.RunTicksSync(1);
        }

        Assert.Fail($"the navmesh of {entManager.ToPrettyString(gridUid)} still had no poly at tile {missingTile} " +
            $"after {maxTicks} ticks (tick {pair.Server.Timing.CurTick})");
    }

    public static EntityUid SpawnAt(IEntityManager entManager, string prototype, EntityUid gridUid, int x, int y)
    {
        return entManager.SpawnEntity(prototype, new EntityCoordinates(gridUid, new Vector2(x + 0.5f, y + 0.5f)));
    }

    /// <summary>
    ///     Spawns a door that runs without a power network, as a door on a powered station would: test grids have no
    ///         power, and an unpowered airlock opens for nobody by hand. Powered within a tick or two.
    /// </summary>
    public static EntityUid SpawnPoweredDoorAt(IEntityManager entManager, string prototype, EntityUid gridUid, int x, int y)
    {
        var doorUid = SpawnAt(entManager, prototype, gridUid, x, y);
        entManager.System<Content.Shared.Power.EntitySystems.SharedPowerReceiverSystem>().SetNeedsPower(doorUid, false);
        return doorUid;
    }
}
