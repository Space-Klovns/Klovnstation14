#nullable enable
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.UnitTesting.Pool;
using static Content.IntegrationTests.Tests._KS14.NPC.KsNpcSquadTestHelpers;

namespace Content.IntegrationTests.Tests._KS14.NPC;

/// <summary>
///     An NPC whose path runs through a bump-open airlock opens it and walks through, rather than standing in
///         front of it waiting to bump it open. A shutter, which only its button opens, stays shut.
/// </summary>
public sealed class KsNpcDoorTraversalTest : GameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    private const string TargetKey = "KsDoorTestTarget";

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: Airlock
  id: KsDoorTestAirlock
  components:
  - type: ApcPowerReceiver
    needsPower: false

- type: entity
  parent: ShuttersNormal
  id: KsDoorTestShutter
  components:
  - type: ApcPowerReceiver
    needsPower: false

- type: htnCompound
  id: KsDoorTestMoveCompound
  branches:
  - preconditions:
    - !type:KeyExistsPrecondition
      key: KsDoorTestTarget
    tasks:
    - !type:HTNPrimitiveTask
      operator: !type:MoveToOperator
        targetKey: KsDoorTestTarget
        pathfindKey: KsDoorTestPathfind
        rangeKey: MovementRangeClose
        removeKeyOnFinish: false # a failed move keeps retrying rather than planning with no target

- type: entity
  id: KsDoorTestWalker
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
  - type: InputMover
  - type: MobMover
  - type: MovementSpeedModifier
  - type: MobState
  - type: MobThresholds
    thresholds:
      0: Alive
      100: Dead
  - type: Damageable
    damageContainer: Biological
  - type: HTN
    rootTask:
      task: KsDoorTestMoveCompound
";

    [Test]
    public async Task TestNpcOpensBumpOpenAirlock()
    {
        var position = await WalkThrough("KsDoorTestAirlock");
        Assert.That(position.X, Is.GreaterThan(5f), $"the NPC never got through the airlock; it is at {position}");
    }

    /// <summary>
    ///     Control: shutters open from a button, never by hand, so an NPC must not open one either.
    /// </summary>
    [Test]
    public async Task TestNpcDoesNotOpenShutter()
    {
        var position = await WalkThrough("KsDoorTestShutter");
        Assert.That(position.X, Is.LessThan(4f), $"the NPC got through a shutter; it is at {position}");
    }

    /// <summary>
    ///     Sets a walker at x = 1 heading for x = 8, with a wall at x = 4 whose only gap is
    ///         <paramref name="doorPrototype"/>, and returns where it ends up.
    /// </summary>
    private async Task<Vector2> WalkThrough(string doorPrototype)
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var tileDefinitionManager = server.ResolveDependency<ITileDefinitionManager>();
        var npcSystem = entManager.System<NPCSystem>();
        var transformSystem = entManager.System<SharedTransformSystem>();
        var map = await Pair.CreateTestMap();

        EntityUid gridUid = default;

        // A wall across the grid at x = 4, whose only gap is the door.
        await server.WaitPost(() =>
        {
            gridUid = MakeGrid(entManager, tileDefinitionManager, map.MapId, map.Grid, new Vector2i(-3, -4), new Vector2i(10, 4)).Owner;

            for (var y = -4; y <= 4; y++)
            {
                SpawnAt(entManager, y == 0 ? doorPrototype : "WallSolid", gridUid, 4, y);
            }
        });

        // Let the navmesh build before the walker starts pathing.
        await Pair.RunTicksSync(90);

        EntityUid walkerUid = default;
        await server.WaitPost(() =>
        {
            walkerUid = SpawnAt(entManager, "KsDoorTestWalker", gridUid, 1, 0);

            var htnComponent = entManager.GetComponent<HTNComponent>(walkerUid);
            htnComponent.Blackboard.SetValue(TargetKey, new EntityCoordinates(gridUid, new Vector2(8.5f, 0.5f)));
            htnComponent.Blackboard.SetValue(NPCBlackboard.NavInteract, true);
            npcSystem.WakeNPC(walkerUid, htnComponent);
        });

        await Pair.RunTicksSync(600);

        var position = Vector2.Zero;
        await server.WaitPost(() => position = transformSystem.GetWorldPosition(walkerUid));
        return position;
    }
}
