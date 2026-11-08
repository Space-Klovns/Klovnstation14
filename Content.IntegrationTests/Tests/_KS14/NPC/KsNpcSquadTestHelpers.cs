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

    // Stand-ins for the walls, windows, doors and kit the NPC tests used to take from the game's own prototypes, so that
    //   retuning a wall, a gun or a door cannot break a test of something else. Each has only what the tests rely on.
    public const string TestWall = "KsNpcTestWall";
    public const string TestWindow = "KsNpcTestWindow";
    public const string TestTable = "KsNpcTestTable";
    public const string TestAirlock = "KsNpcTestAirlock";
    public const string TestSecurityAirlock = "KsNpcTestSecurityAirlock";
    public const string TestShutters = "KsNpcTestShutters";
    public const string TestBlastDoor = "KsNpcTestBlastDoor";
    public const string TestBelt = "KsNpcTestBelt";
    public const string TestPistol = "KsNpcTestPistol";
    public const string TestMagazine = "KsNpcTestMagazine";
    public const string TestMedipen = "KsNpcTestMedipen";
    public const string TestSmoke = "KsNpcTestSmoke";
    public const string TestOxygenTank = "KsNpcTestOxygenTank";
    public const string TestLocker = "KsNpcTestLocker";

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

# Solid, opaque and airtight, tagged Wall, and it breaks.
- type: entity
  parent: BaseStructure
  id: KsNpcTestWall
  components:
  - type: Tag
    tags:
    - Wall
  - type: Physics
    bodyType: Static
  - type: Fixtures
    fixtures:
      fix1:
        shape: !type:PhysShapeAabb
          bounds: ""-0.5,-0.5,0.5,0.5""
        density: 1000
        mask:
        - FullTileMask
        layer:
        - WallLayer
  - type: Occluder
  - type: Airtight
  - type: Damageable
  - type: Destructible
    thresholds:
    - trigger: !type:DamageTrigger
        damage: 400
      behaviors:
      - !type:DoActsBehavior
        acts: [ Destruction ]

# Stops a bullet and a throw, does not block sight, airtight, and has the Window tag room analysis looks for.
- type: entity
  parent: BaseStructure
  id: KsNpcTestWindow
  components:
  - type: Tag
    tags:
    - Window
  - type: Physics
    bodyType: Static
  - type: Fixtures
    fixtures:
      fix1:
        shape: !type:PhysShapeAabb
          bounds: ""-0.5,-0.5,0.5,0.5""
        mask:
        - FullTileMask
        layer:
        - GlassLayer
  - type: Airtight
  - type: Damageable
  - type: Destructible
    thresholds:
    - trigger: !type:DamageTrigger
        damage: 50
      behaviors:
      - !type:DoActsBehavior
        acts: [ Destruction ]

# Something thrown goes over it; a walker does not.
- type: entity
  parent: BaseStructure
  id: KsNpcTestTable
  components:
  - type: Fixtures
    fixtures:
      fix1:
        shape: !type:PhysShapeAabb
          bounds: ""-0.40,-0.30,0.40,0.45""
        density: 80
        mask:
        - TableMask
        layer:
        - TableLayer

# An airlock that anyone can open, which runs on power: pried by a crowbar only while unpowered, by jaws of life either
#   way, and it bolts down.
- type: entity
  parent: BaseStructure
  id: KsNpcTestAirlock
  components:
  - type: Physics
    bodyType: Static
  - type: Fixtures
    fixtures:
      fix1:
        shape: !type:PhysShapeAabb
          bounds: ""-0.49,-0.49,0.49,0.49""
        density: 100
        mask:
        - FullTileMask
        layer:
        - AirlockLayer
  - type: Door
  - type: Airlock
  - type: DoorBolt
  - type: AccessReader
  - type: ApcPowerReceiver
    powerLoad: 20
  - type: NavMapDoor
  - type: Appearance
  - type: Airtight
    noAirWhenFullyAirBlocked: false
  - type: Occluder
  - type: Damageable
  - type: Destructible
    thresholds:
    - trigger: !type:DamageTrigger
        damage: 500
      behaviors:
      - !type:DoActsBehavior
        acts: [ Destruction ]

# The same, but for Security.
- type: entity
  parent: KsNpcTestAirlock
  id: KsNpcTestSecurityAirlock
  components:
  - type: AccessReader
    access: [ [ Security ] ]

# Never opens by hand, and a crowbar pries it.
- type: entity
  parent: BaseShutter
  id: KsNpcTestShutters
  components:
  - type: Occluder

# Never opens by hand, and nothing pries it.
- type: entity
  parent: BaseShutter
  id: KsNpcTestBlastDoor
  components:
  - type: Occluder
  - type: Door
    canPry: false

- type: entity
  parent: ClothingBeltStorageBase
  id: KsNpcTestBelt

- type: entity
  id: KsNpcTestBullet
  components:
  - type: Projectile
    damage:
      types:
        Piercing: 20
  - type: Physics
  - type: Fixtures
    fixtures:
      projectile:
        shape: !type:PhysShapeCircle
          radius: 0.1
        mask:
        - Impassable
        - BulletImpassable

- type: entity
  id: KsNpcTestCartridge
  components:
  - type: CartridgeAmmo
    proto: KsNpcTestBullet

- type: entity
  parent: BaseItem
  id: KsNpcTestMagazine
  components:
  - type: Item
    size: Small
  - type: BallisticAmmoProvider
    mayTransfer: true
    proto: KsNpcTestCartridge
    capacity: 14
    whitelist:
      components:
      - CartridgeAmmo
  - type: ContainerContainer
    containers:
      ballistic-ammo: !type:Container

# A pistol with a magazine slot, a chamber and a loaded magazine, in the shape the squad's top-up and the reload tasks
#   look for.
- type: entity
  parent: BaseItem
  id: KsNpcTestPistol
  components:
  - type: Item
    size: Small
  - type: Gun
    fireRate: 5
    selectedMode: SemiAuto
    availableModes:
    - SemiAuto
  - type: GunExecutionWhitelist
  - type: ChamberMagazineAmmoProvider
  - type: ItemSlots
    slots:
      gun_magazine:
        name: Magazine
        startingItem: KsNpcTestMagazine
        whitelist:
          components:
          - BallisticAmmoProvider
      gun_chamber:
        name: Chamber
        whitelist:
          components:
          - CartridgeAmmo
  - type: ContainerContainer
    containers:
      gun_magazine: !type:ContainerSlot
      gun_chamber: !type:ContainerSlot

# Heals whoever it is used on, as a medipen does: a single-use injector of a stabilising reagent.
- type: entity
  parent: BaseItem
  id: KsNpcTestMedipen
  components:
  - type: Item
    size: Tiny
  - type: NpcHealingItem
  - type: Injector
    solutionName: hypospray
    currentTransferAmount: null
    activeModeProtoId: HyposprayInjectMode
    allowedModes:
    - HyposprayInjectMode
  - type: Solution
    id: hypospray
    solution:
      maxVol: 20
      reagents:
      - ReagentId: Epinephrine
        Quantity: 15
  - type: UseDelay
    delay: 0.5

# A cloud of smoke that blocks sight, as the real one does, and carries whatever it is started with.
- type: entity
  id: KsNpcTestSmoke
  components:
  - type: Transform
    anchored: true
  - type: Physics
  - type: Fixtures
    fixtures:
      fix1:
        hard: false
        shape: !type:PhysShapeAabb
          bounds: ""-0.4,-0.4,0.4,0.4""
        mask:
        - ItemMask
        layer:
        - SlipLayer
  - type: Smoke
  - type: Appearance
  - type: Occluder
  - type: Solution
    id: solutionArea
    solution:
      maxVol: 600
      canReact: false

# A tank of breathable oxygen that internals can draw on, with the use delay of a real one.
- type: entity
  parent: BaseItem
  id: KsNpcTestOxygenTank
  components:
  - type: Item
    size: Small
  - type: UseDelay
    delays:
      gasTank:
        length: 1.0
  - type: AtmosDevice
    requireAnchored: false
    joinSystem: true
  - type: GasTank
    releasePressure: 21.3
    tankLowPressure: 30.0
    air:
      volume: 0.66
      moles:
        Oxygen: 0.270782035
      temperature: 293.15

# A closed container something can be put in and shut away inside.
- type: entity
  parent: BaseStructureDynamic
  id: KsNpcTestLocker
  components:
  - type: Physics
  - type: Fixtures
    fixtures:
      fix1:
        shape: !type:PhysShapeAabb
          bounds: ""-0.25,-0.48,0.25,0.48""
        density: 75
        mask:
        - MachineMask
        layer:
        - MachineLayer
  - type: EntityStorage
  - type: ContainerContainer
    containers:
      entity_storage: !type:Container
  - type: Damageable

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
