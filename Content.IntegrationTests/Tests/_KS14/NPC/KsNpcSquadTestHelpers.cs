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
  # Every setting spelled out, not left to its default: a test must not change meaning because a default did.
  #   AlwaysPushInheritance on cover and tactics means a test mob overriding one of them keeps the rest.
  - type: NpcSquadMember
    maxSquadSize: 4
    assimilationThreshold: 2
    joinRange: 12
    canLead: true
    cover:
      maxRoomTiles: 150
      hallwayWidth: 3
      minStandoff: 1.5
      idealStandoff: 3
      maxStandoff: 6
      funnelAngle: 15
      minPreferredAngle: 25
      maxPreferredAngle: 65
      exposureAvoidRange: 2.5
      wallPreference: 0.25
      planLifetime: 15
      threatMoveTolerance: 3
      claimClearanceRadius: 1.5
      threatObjectiveLifetime: 40
      killZoneAvoidance: 0
    tactics:
      huntStartAge: 6
      watchTime: 4
      huntTimeout: 25
      stageTimeout: 6
      entryTimeout: 5
      breachTimeout: 10
      holdAreaTime: 8
      canStackUp: true
      stageDistance: 1.5
      stageArriveRange: 1
      maxStageDistance: 16
      entranceTargetPreference: 2
      entryDepth: 1.5
      searchRadius: 6
      maxSearchPoints: 10
      searchCoverage: 1
      clearRange: 7
      searchPointTimeout: 12
      cautiousHuntThreshold: 30
      cautiousRegroupDistance: 3.5
      regroupDistance: 8
      regroupRange: 2.5
      regroupQuietTime: 20
  - type: NpcSensors
  - type: NpcPerception
    updateInterval: 0.2
    memoryTime: 30
    concealedMemoryTime: 60
    reactionTime: 0.6
    reactionForgetTime: 2
    darknessReactionScale: 1
    alertMarker: OpInCombat
    squadAlertWindow: 20
    proximityRange: 2.5
    minimumLightLevel: 0.03
    revealSpeed: 4
    darkTrackTime: 2.5
    trackSpeed: 0.5
    deadReckoningTime: 3
    suspicionRange: 1.25
    calloutInterval: 1
  - type: NpcDoorUser
    breachWhenBlocked: true
    forgetAfter: 120
    blockedForgetAfter: 30

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
    updateInterval: 0.2
    memoryTime: 30
    concealedMemoryTime: 60
    reactionTime: 0.6
    reactionForgetTime: 2
    darknessReactionScale: 1
    alertMarker: OpInCombat
    squadAlertWindow: 20
    proximityRange: 2.5
    minimumLightLevel: 0.03
    revealSpeed: 4
    darkTrackTime: 2.5
    trackSpeed: 0.5
    deadReckoningTime: 3
    suspicionRange: 1.25
    calloutInterval: 1
  - type: NpcDoorUser
    breachWhenBlocked: true
    forgetAfter: 120
    blockedForgetAfter: 30

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

# Real tools, with what the door tests rely on about them spelled out: what pries what, how fast, how many charges.
- type: entity
  parent: Crowbar
  id: KsTestCrowbar
  components:
  - type: Prying
    enabled: true
    pryPowered: false
    force: false
    speedModifier: 1

- type: entity
  parent: JawsOfLife
  id: KsTestJawsOfLife
  components:
  - type: Prying
    pryPowered: true
    force: false
    speedModifier: 1.5

- type: entity
  parent: AccessBreaker
  id: KsTestAccessBreaker
  components:
  - type: Emag
    emagType: Access
    emagImmuneTag: AccessBreakerImmune
  - type: LimitedCharges
    maxCharges: 3
  - type: AutoRecharge
    rechargeDuration: 90
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
