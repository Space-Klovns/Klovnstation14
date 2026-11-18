#nullable enable
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.NPC.HTN.PrimitiveTasks.Operators.Combat;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Systems;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Trigger.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.UnitTesting.Pool;
using static Content.IntegrationTests.Tests._KS14.NPC.KsNpcSquadTestHelpers;

namespace Content.IntegrationTests.Tests._KS14.FieldCommander;

/// <summary>
///     A suicide drone closes on its target, arms itself beside it (<see cref="KsDetonateOperator"/>) and goes off,
///         hurting the target.
/// </summary>
[TestFixture]
[TestOf(typeof(KsDetonateOperator))]
public sealed class KsFieldCommanderDroneTest : GameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    // Ids that exist only in the test prototypes below.
    private readonly EntProtoId _drone = "KsTestDroneDrone";
    private readonly EntProtoId _target = "KsTestDroneTarget";
    private const string ArmRangeKey = "DroneArmRange";
    private const string Root = "KsTestDroneRoot";

    // The private module's drone: run only where it is present.
    private readonly EntProtoId _realDrone = "KsFcMobAiDrone";

    [TestPrototypes]
    private const string Prototypes = @"
- type: utilityQuery
  id: KsTestDroneTargets
  query:
  - !type:KsScenarioFactionMemberQuery
    factions:
    - Nanotrasen
  considerations:
  - !type:TargetIsAliveCon
    curve: !type:BoolCurve
  - !type:KsTargetDistanceUnboundedCon
    curve: !type:QuadraticCurve

- type: htnCompound
  id: KsTestDroneRoot
  branches:
  - tasks:
    - !type:HTNPrimitiveTask
      operator: !type:UtilityOperator
        proto: KsTestDroneTargets
    - !type:HTNPrimitiveTask
      operator: !type:MoveToOperator
        pathfindInPlanning: true
        removeKeyOnFinish: false
        targetKey: TargetCoordinates
        pathfindKey: TargetPathfind
        rangeKey: DroneArmRange
        shutdownState: TaskFinished
    - !type:HTNPrimitiveTask
      preconditions:
      - !type:TargetInRangePrecondition
        targetKey: Target
        rangeKey: DroneArmRange
      operator: !type:KsDetonateOperator

- type: entity
  parent: BaseMob
  id: KsTestDroneDrone
  components:
  - type: Fixtures
    fixtures:
      fix1:
        shape: !type:PhysShapeCircle
          radius: 0.25
        density: 30
        mask:
        - FlyingMobMask
        layer:
        - FlyingMobLayer
  - type: NpcFactionMember
    factions:
    - Syndicate
  - type: HTN
    enabled: false
    rootTask:
      task: IdleCompound
    blackboard:
      DroneArmRange: !type:Single
        1.5
  - type: MovementSpeedModifier
    baseWalkSpeed: 4
    baseSprintSpeed: 4
  - type: TimerTrigger
    delay: 0.5
    popup: null
  - type: Explosive
    explosionType: Default
    maxIntensity: 10
    intensitySlope: 5
    totalIntensity: 20
    maxTileBreak: 0
    canCreateVacuum: false
  - type: ExplodeOnTrigger
    keysIn:
    - timer

- type: entity
  id: KsTestDroneTarget
  components:
  - type: MobState
  - type: MobThresholds
    thresholds:
      0: Alive
      1000: Dead
  - type: Damageable
  - type: Injurable
    damageContainer: Biological
  - type: ScenarioFactionMember
    id: Nanotrasen
";

    private static readonly Vector2i Min = new(-15, -3);
    private static readonly Vector2i Max = new(15, 3);

    [Test]
    public async Task DroneDetonatesAtItsTarget([Values] bool real)
    {
        if (real && !SProtoMan.HasIndex(_realDrone))
            Assert.Ignore("the drone is in the private module");

        var map = await Pair.CreateTestMap();
        EntityUid gridUid = default;
        await Server.WaitPost(() =>
        {
            gridUid = MakeGrid(SEntMan, Server.ResolveDependency<ITileDefinitionManager>(), map.MapId, map.Grid, Min, Max).Owner;
            GiveGravity(SEntMan, gridUid);
        });
        await WaitForNavmesh(Pair, gridUid, Min, Max);

        EntityUid droneUid = default;
        EntityUid targetUid = default;
        var armRange = 0f;

        await Server.WaitPost(() =>
        {
            droneUid = SEntMan.SpawnEntity(real ? _realDrone : _drone, new EntityCoordinates(gridUid, new Vector2(-10.5f, 0.5f)));
            targetUid = SEntMan.SpawnEntity(_target, new EntityCoordinates(gridUid, new Vector2(8.5f, 0.5f)));

            var htnComponent = SEntMan.GetComponent<HTNComponent>(droneUid);
            armRange = htnComponent.Blackboard.GetValueOrDefault<float>(ArmRangeKey, SEntMan);
            if (!real)
                htnComponent.RootTask = new HTNCompoundTask { Task = Root };

            SEntMan.System<HTNSystem>().SetHTNEnabled((droneUid, htnComponent), true);
            SEntMan.System<NPCSystem>().WakeNPC(droneUid, htnComponent);
        });

        Assert.That(armRange, Is.GreaterThan(0f), "precondition: the drone has an arming range");

        float? armedDistance = null;
        var gone = false;
        var transformSystem = SEntMan.System<SharedTransformSystem>();
        for (var i = 0; i < 240 && !gone; i++)
        {
            await RunSeconds(0.25f);
            await Server.WaitPost(() =>
            {
                if (SEntMan.Deleted(droneUid) || SEntMan.IsQueuedForDeletion(droneUid))
                {
                    gone = true;
                    return;
                }

                if (armedDistance == null && SEntMan.HasComponent<ActiveTimerTriggerComponent>(droneUid))
                {
                    armedDistance = Vector2.Distance(transformSystem.GetWorldPosition(droneUid),
                        transformSystem.GetWorldPosition(targetUid));
                }
            });
        }

        // Let the blast play out over the tiles.
        await RunSeconds(1f);

        Assert.That(armedDistance, Is.Not.Null, "the drone should have armed itself");
        Assert.That(armedDistance!.Value, Is.LessThanOrEqualTo(armRange + 0.5f), "it should arm beside its target, not on the way");
        Assert.That(gone, Is.True, "the drone should have gone off");

        await Server.WaitAssertion(() =>
        {
            var damage = SEntMan.System<DamageableSystem>().GetTotalDamage((targetUid, SEntMan.GetComponent<DamageableComponent>(targetUid)));
            Assert.That(damage, Is.GreaterThan(FixedPoint2.Zero), "the blast should have hurt the target");
        });
    }
}
