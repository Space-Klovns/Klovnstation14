#nullable enable
using System;
using System.Collections.Generic;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.WeldedParts;
using Content.Shared.Damage.Systems;
using Content.Shared.Movement.Components;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Collision.Shapes;
using Robust.Shared.Physics.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.UnitTesting.Pool;
using static Content.IntegrationTests.Tests._KS14.NPC.KsNpcSquadTestHelpers;

namespace Content.IntegrationTests.Tests._KS14.Vehicles;

/// <summary>
///     A tank: a hull (<c>VehicleTankBody</c>) with a turret (<c>VehicleTankTurret</c>) welded on by
///         <see cref="KsWeldedPartsComponent"/>. The turret takes on a hostile anywhere in its vision, whichever way it and
///         the hull face, holds one target while another becomes the nearer, and its rounds leave through the hull. The hull
///         collides with walls and gets through a two-tile gap with the turret on, and, driven by
///         <c>KsVehicleApproachCompound</c>, closes to its approach range rather than stopping on sight 10 tiles out.
/// </summary>
[TestFixture]
[TestOf(typeof(KsWeldedPartsSystem))]
public sealed class KsTankTest : GameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    // Ids that exist only in the test prototypes below.
    private readonly EntProtoId _gunHull = "KsTankTestGunHull";
    private readonly EntProtoId _gunTurret = "KsTankTestGunTurret";
    private readonly EntProtoId _approachHull = "KsTankTestApproachHull";
    private readonly EntProtoId _gapHull = "KsTankTestGapHull";
    private readonly EntProtoId _hostile = "KsTankTestHostile";

    /// <summary>
    ///     The gun turret's own vision and reach (its blackboard below). Its hostiles stand beyond 10 tiles, which is where a
    ///         turret without <c>AggroVisionRadius</c> lost them once it had a target.
    /// </summary>
    private const float TurretRangedRange = 14f;

    private const float FarHostileDistance = 13f;

    /// <summary>
    ///     The approach hull's <c>ApproachRange</c> (below), well inside the 10 tiles stopOnLineOfSight stops at.
    /// </summary>
    private const float ApproachRange = 4f;

    private const float GapApproachRange = 2f;

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: KsSquadTestMobNanoTrasen
  id: KsTankTestHostile
  components:
  - type: Injurable
  - type: MobThresholds
    thresholds:
      0: Alive
      1000: Critical
      2000: Dead

- type: entity
  parent: VehicleTankTurret
  id: KsTankTestInertTurret
  components:
  - type: Physics
    bodyType: Dynamic

- type: entity
  parent: KsTankTestInertTurret
  id: KsTankTestGunTurret
  components:
  - type: KsTargetLockOn
    lockOnTime: 1
    loseLockGrace: 0.5
    keepTargetUntilLost: true
  - type: HTN
    rootTask:
      task: EnergyTurretCompound
    blackboard:
      RotateSpeed: !type:Single
        1
      RangedRange: !type:Single
        14
      VisionRadius: !type:Single
        16
      AggroVisionRadius: !type:Single
        16

- type: entity
  parent: VehicleTankBody
  id: KsTankTestGunHull
  components:
  - type: NpcFactionMember
    factions:
    - Syndicate
  - type: KsWeldedParts
    parts:
    - prototype: KsTankTestGunTurret
      angularStiffness: 0.01

- type: utilityQuery
  id: KsTankTestHostiles
  query:
  - !type:NearbyHostilesQuery
  considerations:
  - !type:TargetIsAliveCon
    curve: !type:BoolCurve

- type: htnCompound
  id: KsTankTestDriveCompound
  branches:
  - tasks:
    - !type:HTNPrimitiveTask
      operator: !type:UtilityOperator
        proto: KsTankTestHostiles
    - !type:HTNCompoundTask
      task: KsVehicleApproachCompound
  - tasks:
    - !type:HTNPrimitiveTask
      operator: !type:SetFloatOperator
        targetKey: IdleTime
        amount: 1
    - !type:HTNPrimitiveTask
      operator: !type:WaitOperator
        key: IdleTime

- type: entity
  parent: VehicleTankBody
  id: KsTankTestApproachHull
  components:
  - type: MobMover
  - type: NpcFactionMember
    factions:
    - Syndicate
  - type: MovementSpeedModifier
    baseWalkSpeed: 2.5
    baseSprintSpeed: 2.5
    baseAcceleration: 1
    baseFriction: 1
    baseWeightlessModifier: 0
  - type: HTN
    rootTask:
      task: KsTankTestDriveCompound
    blackboard:
      VisionRadius: !type:Single
        40
      AggroVisionRadius: !type:Single
        40
      ApproachRange: !type:Single
        4
  - type: KsWeldedParts
    parts:
    - prototype: KsTankTestInertTurret
      angularStiffness: 0.01

- type: entity
  parent: KsTankTestApproachHull
  id: KsTankTestGapHull
  components:
  - type: HTN
    rootTask:
      task: KsTankTestDriveCompound
    blackboard:
      VisionRadius: !type:Single
        40
      AggroVisionRadius: !type:Single
        40
      ApproachRange: !type:Single
        2
";

    private float TickRate()
    {
        return (float)Server.ResolveDependency<IGameTiming>().TickRate;
    }

    private static int AmmoCount(IEntityManager entManager, EntityUid gunUid)
    {
        var ev = new GetAmmoCountEvent();
        entManager.EventBus.RaiseLocalEvent(gunUid, ref ev);
        return ev.Count;
    }

    private EntityUid SpawnOn(string prototype, EntityUid gridUid, Vector2 position)
    {
        return SEntMan.SpawnEntity(prototype, new EntityCoordinates(gridUid, position));
    }

    private Vector2 WorldPosition(EntityUid uid)
    {
        return SEntMan.System<SharedTransformSystem>().GetWorldPosition(uid);
    }

    /// <summary>
    ///     A gun hull at the middle of an open grid, its turret welded on, and hostiles where asked. With
    ///         <paramref name="facingAway"/>, hull and turret are turned to face straight away from the first hostile.
    /// </summary>
    private async Task<(EntityUid Hull, EntityUid Turret, EntityUid GridUid, List<EntityUid> Hostiles)> SetUpGunTank(
        bool facingAway,
        params Vector2[] hostileOffsets)
    {
        var map = await Pair.CreateTestMap();
        EntityUid gridUid = default, hullUid = default;
        var hostiles = new List<EntityUid>();

        await Server.WaitPost(() =>
        {
            gridUid = MakeGrid(SEntMan, Server.ResolveDependency<ITileDefinitionManager>(), map.MapId, map.Grid,
                new Vector2i(-20, -6), new Vector2i(20, 6)).Owner;
            // Weightless, a gun's recoil pushes its shooter, and the turret drags the hull along.
            GiveGravity(SEntMan, gridUid);
            hullUid = SpawnOn(_gunHull, gridUid, new Vector2(0.5f, 0.5f));
        });

        await Pair.RunTicksSync(2);

        EntityUid turretUid = default;
        await Server.WaitPost(() =>
        {
            var parts = SEntMan.System<KsWeldedPartsSystem>().GetParts(hullUid);
            Assert.That(parts, Has.Count.EqualTo(1), "the hull should come with its turret");
            turretUid = parts[0];

            foreach (var offset in hostileOffsets)
                hostiles.Add(SpawnOn(_hostile, gridUid, new Vector2(0.5f, 0.5f) + offset));

            if (facingAway)
            {
                var transformSystem = SEntMan.System<SharedTransformSystem>();
                var away = (WorldPosition(hullUid) - WorldPosition(hostiles[0])).ToWorldAngle();
                transformSystem.SetWorldRotation(hullUid, away);
                transformSystem.SetWorldRotation(turretUid, away);
            }
        });

        return (hullUid, turretUid, gridUid, hostiles);
    }

    private float TotalDamage(EntityUid uid)
    {
        return (float)SEntMan.System<DamageableSystem>().GetTotalDamage(uid);
    }

    /// <summary>
    ///     The turret fires at a hostile beyond 10 tiles and within its reach, turned there from facing the other way if
    ///         asked, and the round reaches the hostile through the hull it starts inside, hurting neither part.
    /// </summary>
    [TestCase(false)]
    [TestCase(true)]
    public async Task TurretEngagesAFarHostileThroughItsHull(bool facingAway)
    {
        Assert.That(FarHostileDistance, Is.GreaterThan(10f).And.LessThan(TurretRangedRange));

        var (hullUid, turretUid, _, hostiles) = await SetUpGunTank(facingAway, new Vector2(FarHostileDistance, 0f));
        var hostileUid = hostiles[0];
        var startAmmo = 0;
        await Server.WaitPost(() => startAmmo = AmmoCount(SEntMan, turretUid));
        Assert.That(startAmmo, Is.GreaterThan(0));

        var tickRate = TickRate();
        var hit = false;
        var fired = false;

        for (var tick = 0; tick < (int)(30f * tickRate) && !hit; tick++)
        {
            await Pair.RunTicksSync(1);
            await Server.WaitPost(() =>
            {
                fired |= AmmoCount(SEntMan, turretUid) < startAmmo;
                hit = TotalDamage(hostileUid) > 0f;
            });
        }

        Assert.That(fired, Is.True, $"the turret should fire at a hostile {FarHostileDistance} tiles off");
        Assert.That(hit, Is.True, "its rounds should reach the hostile");

        await Server.WaitAssertion(() =>
        {
            Assert.That(TotalDamage(hullUid), Is.Zero, "its rounds should pass through its own hull");
            Assert.That(TotalDamage(turretUid), Is.Zero, "nor hurt the turret");
        });
    }

    /// <summary>
    ///     Two hostiles on either side, each in turn the nearer, faster than the lock-on takes: the turret keeps the one it
    ///         has, locks on and fires, rather than starting over on the other each time.
    /// </summary>
    [Test]
    public async Task TurretKeepsItsTargetWhileTheNearestAlternates()
    {
        const float east = 12f;
        const float west = 12.6f;
        var (_, turretUid, gridUid, hostiles) = await SetUpGunTank(false, new Vector2(east, 0f), new Vector2(-west, 0f));
        var startAmmo = 0;
        await Server.WaitPost(() => startAmmo = AmmoCount(SEntMan, turretUid));

        var tickRate = TickRate();
        var swapTicks = (int)(0.8f * tickRate);
        var fired = false;
        var eastNearer = true;

        for (var tick = 0; tick < (int)(30f * tickRate) && !fired; tick++)
        {
            if (tick % swapTicks == 0)
            {
                eastNearer = !eastNearer;
                var eastDistance = eastNearer ? east : east + 1.2f;
                await Server.WaitPost(() => SEntMan.System<SharedTransformSystem>().SetCoordinates(hostiles[0],
                    new EntityCoordinates(gridUid, new Vector2(0.5f + eastDistance, 0.5f))));
            }

            await Pair.RunTicksSync(1);
            await Server.WaitPost(() => fired = AmmoCount(SEntMan, turretUid) < startAmmo);
        }

        Assert.That(fired, Is.True, "the turret should keep one target and fire at it while the nearer of two alternates");
    }

    /// <summary>
    ///     Driven at a hostile on open ground, the hull closes to within its approach range, not stopping where it first has
    ///         it in sight.
    /// </summary>
    [Test]
    public async Task HullClosesToItsApproachRange()
    {
        var map = await Pair.CreateTestMap();
        EntityUid gridUid = default, hullUid = default, hostileUid = default;
        var min = new Vector2i(-14, -6);
        var max = new Vector2i(14, 6);

        await Server.WaitPost(() =>
        {
            gridUid = MakeGrid(SEntMan, Server.ResolveDependency<ITileDefinitionManager>(), map.MapId, map.Grid, min, max).Owner;
            GiveGravity(SEntMan, gridUid);
        });

        await WaitForNavmesh(Pair, gridUid, min, max);

        await Server.WaitPost(() =>
        {
            hostileUid = SpawnAt(SEntMan, _hostile, gridUid, 11, 0);
            hullUid = SpawnAt(SEntMan, _approachHull, gridUid, -11, 0);
        });

        var tickRate = TickRate();
        var closest = float.MaxValue;
        var still = 0;
        var last = Vector2.Zero;
        var walkSpeed = 0f;
        await Server.WaitPost(() => walkSpeed = SEntMan.GetComponent<MovementSpeedModifierComponent>(hullUid).BaseWalkSpeed);

        // Once under way, slowing to a crawl short of its range is a stop: what stopOnLineOfSight did, stopping on sight 10
        //   tiles out, then setting off again on every replan and creeping the rest of the way in fits and starts.
        var underWay = false;
        var stops = 0;
        var stopped = false;

        for (var tick = 0; tick < (int)(60f * tickRate) && still < (int)(3f * tickRate); tick++)
        {
            await Pair.RunTicksSync(1);
            await Server.WaitPost(() =>
            {
                var position = WorldPosition(hullUid);
                var distance = (WorldPosition(hostileUid) - position).Length();
                closest = MathF.Min(closest, distance);
                still = (position - last).Length() < 0.001f ? still + 1 : 0;
                last = position;

                var speed = SEntMan.GetComponent<PhysicsComponent>(hullUid).LinearVelocity.Length();
                underWay |= speed > walkSpeed * 0.5f;
                var crawling = underWay && speed < walkSpeed * 0.25f && distance > ApproachRange;
                if (crawling && !stopped)
                    stops++;
                stopped = crawling;
            });
        }

        var final = 0f;
        await Server.WaitPost(() => final = (WorldPosition(hostileUid) - WorldPosition(hullUid)).Length());

        Assert.That(closest, Is.LessThanOrEqualTo(ApproachRange),
            $"the hull should close to its approach range of {ApproachRange} (it got no nearer than {closest:0.0})");
        Assert.That(stops, Is.Zero, "it should drive on until it is in range, not stop short of it");
        Assert.That(final, Is.GreaterThan(1.5f), "it should hold off, not drive into the hostile");
    }

    /// <summary>
    ///     A wall with a two-tile gap between the hull and a hostile: the hull, turret on, goes round through the gap and never
    ///         overlaps a wall tile on the way.
    /// </summary>
    [Test]
    public async Task HullGetsThroughATwoTileGapWithoutEnteringWalls()
    {
        var map = await Pair.CreateTestMap();
        EntityUid gridUid = default, hullUid = default, hostileUid = default;
        var min = new Vector2i(-12, -8);
        var max = new Vector2i(12, 8);
        var wallTiles = new List<Vector2i>();

        await Server.WaitPost(() =>
        {
            gridUid = MakeGrid(SEntMan, Server.ResolveDependency<ITileDefinitionManager>(), map.MapId, map.Grid, min, max).Owner;
            GiveGravity(SEntMan, gridUid);

            for (var y = min.Y; y <= max.Y; y++)
            {
                if (y is 2 or 3)
                    continue;

                SpawnAt(SEntMan, TestWall, gridUid, 0, y);
                wallTiles.Add(new Vector2i(0, y));
            }
        });

        await WaitForNavmeshSettled(Pair, gridUid);

        await Server.WaitPost(() =>
        {
            hostileUid = SpawnAt(SEntMan, _hostile, gridUid, 6, -4);
            hullUid = SpawnAt(SEntMan, _gapHull, gridUid, -6, -4);
        });

        var radius = 0f;
        await Server.WaitPost(() =>
        {
            foreach (var fixture in SEntMan.GetComponent<FixturesComponent>(hullUid).Fixtures.Values)
            {
                if (fixture.Hard && fixture.Shape is PhysShapeCircle circle)
                    radius = MathF.Max(radius, circle.Radius);
            }
        });
        Assert.That(radius, Is.GreaterThan(0f));

        var tickRate = TickRate();
        var closest = float.MaxValue;
        var deepest = 0f;
        var deepestAt = Vector2.Zero;

        for (var tick = 0; tick < (int)(90f * tickRate) && closest > GapApproachRange; tick++)
        {
            await Pair.RunTicksSync(1);
            await Server.WaitPost(() =>
            {
                // Grid-local, the grid being unrotated at the origin: wall tile (0, y) spans [0, 1] x [y, y + 1].
                var position = WorldPosition(hullUid);
                closest = MathF.Min(closest, (WorldPosition(hostileUid) - position).Length());

                foreach (var tile in wallTiles)
                {
                    var nearest = Vector2.Clamp(position, new Vector2(tile.X, tile.Y), new Vector2(tile.X + 1, tile.Y + 1));
                    var depth = radius - (position - nearest).Length();
                    if (depth > deepest)
                    {
                        deepest = depth;
                        deepestAt = position;
                    }
                }
            });
        }

        // Contact resolution lets a body sink in by a hair (the solver's slop) before pushing it back out.
        Assert.That(deepest, Is.LessThan(0.05f), $"the hull should not enter a wall tile (it sank {deepest:0.00} in, at {deepestAt})");
        Assert.That(closest, Is.LessThanOrEqualTo(GapApproachRange),
            $"the hull should get through the gap to its hostile (it got no nearer than {closest:0.0})");
    }
}
