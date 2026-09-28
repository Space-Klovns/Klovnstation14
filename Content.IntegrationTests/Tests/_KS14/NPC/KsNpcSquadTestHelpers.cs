#nullable enable
using System.Collections.Generic;
using System.Numerics;
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
  - type: NpcReactionTime

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

    public static EntityUid SpawnAt(IEntityManager entManager, string prototype, EntityUid gridUid, int x, int y)
    {
        return entManager.SpawnEntity(prototype, new EntityCoordinates(gridUid, new Vector2(x + 0.5f, y + 0.5f)));
    }
}
