#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Tests.Interaction;
using Content.Server._KS14.Weapons.Hitscan;
using Content.Shared._KS14.Weapons.Hitscan;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Physics;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._KS14.Weapons;

/// <summary>
///     A player's hitscan shot is taken against where each mob was when their client fired
///         (<see cref="KsHitscanLagCompensationSystem"/>), not where the server has it when the shot arrives.
///
///     Layout, with the shooter at the origin firing east, the target's old place on the line and its new one off it:
///     <code>
///         . . . . . N .     N - now: where the target stands when the shot arrives
///         S . . W . T .     T - then: where it was, the lag before; W - a wall, in one test
///     </code>
/// </summary>
[TestOf(typeof(KsHitscanLagCompensationSystem))]
public sealed class KsHitscanLagCompensationTest : InteractionTest
{
    protected override string PlayerPrototype => "MobHuman"; // combat mode and hands, to fire a gun

    private const string TestLaser = "KsHitscanLagCompensationTestLaser";
    private const string TestRay = "KsHitscanLagCompensationTestRay";
    private const string TestMob = "KsHitscanLagCompensationTestMob";
    private const string TestWall = "KsHitscanLagCompensationTestWall";

    private readonly EntProtoId _testMob = TestMob;
    private readonly EntProtoId _testWall = TestWall;

    /// <summary>
    ///     How long ago the shooter is taken to have fired. The target has been off the line for less than this, and was
    ///         on it for longer.
    /// </summary>
    private static readonly TimeSpan Lag = TimeSpan.FromMilliseconds(500);

    private static readonly Vector2 ThenOffset = new(5f, 0f);
    private static readonly Vector2 NowOffset = new(5f, 3f);
    private const float RayLength = 20f;

    [TestPrototypes]
    private const string Prototypes = $@"
- type: entity
  id: {TestRay}
  components:
  - type: HitscanAmmo
  - type: HitscanBasicRaycast
  - type: HitscanBasicDamage
    damage:
      types:
        Piercing: 10

- type: entity
  id: {TestLaser}
  components:
  - type: Item
  - type: Gun
    fireRate: 2
    minAngle: 0
    maxAngle: 0
    selectedMode: SemiAuto
    availableModes:
    - SemiAuto
  - type: BasicEntityAmmoProvider
    proto: {TestRay}
    capacity: 10
    count: 10

- type: entity
  id: {TestMob}
  components:
  - type: Physics
    bodyType: Dynamic
    bodyStatus: InAir
  - type: Fixtures
    fixtures:
      fix1:
        shape:
          !type:PhysShapeCircle
          radius: 0.35
        hard: true
        layer:
        - MobLayer
        mask:
        - MobMask
  - type: Damageable
  - type: Injurable
  - type: LagCompensation

- type: entity
  id: {TestWall}
  components:
  - type: Physics
    bodyType: Static
  - type: Fixtures
    fixtures:
      fix1:
        shape:
          !type:PhysShapeAabb
          bounds: ""-0.5,-0.5,0.5,0.5""
        hard: true
        layer:
        - WallLayer
        mask:
        - FullTileMask
";

    /// <summary>
    ///     Spawns the target and has it stand on the line for longer than <see cref="Lag"/>, then puts it off the line for
    ///         a part of it: on the server it is off the line now, and was on it the lag before.
    /// </summary>
    /// <returns>The shooter's position, and the target.</returns>
    private async Task<(Vector2 ShooterPosition, EntityUid TargetUid)> MoveTargetOffTheLine()
    {
        var shooterPosition = Vector2.Zero;
        var targetUid = EntityUid.Invalid;
        await Server.WaitPost(() =>
        {
            shooterPosition = Transform.GetWorldPosition(SPlayer);
            targetUid = SEntMan.SpawnEntity(_testMob, new MapCoordinates(shooterPosition + ThenOffset, MapData.MapId));
        });

        var ticksPerSecond = (float)Server.ResolveDependency<IGameTiming>().TickRate;
        var lagTicks = (int)MathF.Ceiling((float)Lag.TotalSeconds * ticksPerSecond);

        // A position is recorded on every move, and rewinding takes the first one at or after (now - lag): the target
        //      has to keep moving on the line to leave a history that has it there. A hair either side of it is a move.
        for (var tick = 0; tick < lagTicks * 2; tick++)
        {
            var nudge = tick % 2 == 0 ? 0.01f : -0.01f;
            await Server.WaitPost(() =>
                Transform.SetWorldPosition(targetUid, shooterPosition + ThenOffset + new Vector2(0f, nudge)));
            await RunTicks(1);
        }

        await Server.WaitPost(() => Transform.SetWorldPosition(targetUid, shooterPosition + NowOffset));
        await RunTicks(lagTicks / 3);

        return (shooterPosition, targetUid);
    }

    /// <summary>
    ///     Casts a ray from <paramref name="origin"/> along <paramref name="direction"/> on the server, and corrects what
    ///         it crossed for a shooter who fired <paramref name="lag"/> ago.
    /// </summary>
    private List<RayCastResults> CastCompensated(Vector2 origin, Vector2 direction, TimeSpan lag)
    {
        var collisionMask = (int)(CollisionGroup.Impassable | CollisionGroup.MobLayer);
        var ray = new CollisionRay(origin, direction, collisionMask);
        var results = SEntMan.System<SharedPhysicsSystem>().IntersectRay(MapData.MapId, ray, RayLength, SPlayer, returnOnFirstHit: false).ToList();

        var ev = new KsModifyHitscanRaycastResultsEvent(SPlayer, MapData.MapId, origin, direction, RayLength, collisionMask, results);
        SEntMan.System<KsHitscanLagCompensationSystem>().Compensate(ref ev, lag);
        return results;
    }

    /// <summary>
    ///     A ray along the line the target has left crosses it where it was, for a shooter that far behind, and does not
    ///         for one who is not behind at all. A ray at where it stands now is the other way round.
    /// </summary>
    [Test]
    public async Task RayCrossesTheTargetWhereItWas()
    {
        var (shooterPosition, targetUid) = await MoveTargetOffTheLine();

        await Server.WaitAssertion(() =>
        {
            var alongTheLine = Vector2.UnitX;
            var atWhereItIsNow = Vector2.Normalize(NowOffset);

            var then = CastCompensated(shooterPosition, alongTheLine, Lag);
            Assert.That(then.Any(result => result.HitEntity == targetUid), "a lagging shooter's ray along the line the target was on did not cross it");

            var hit = then.First(result => result.HitEntity == targetUid);
            Assert.That(hit.Distance, Is.EqualTo(ThenOffset.X).Within(1f), "the target was crossed, but not where it was");

            Assert.That(CastCompensated(shooterPosition, alongTheLine, TimeSpan.Zero).Any(result => result.HitEntity == targetUid), Is.False,
                "a shooter with no lag crossed the target on a line it has left");

            Assert.That(CastCompensated(shooterPosition, atWhereItIsNow, TimeSpan.Zero).Any(result => result.HitEntity == targetUid),
                "a shooter with no lag did not cross the target where it stands, so this proves nothing");
            Assert.That(CastCompensated(shooterPosition, atWhereItIsNow, Lag).Any(result => result.HitEntity == targetUid), Is.False,
                "a lagging shooter's ray crossed the target where it stands now, where that shooter has yet to see it");
        });
    }

    /// <summary>
    ///     What was in the way then is in the way still: a wall between the shooter and where the target was comes first.
    /// </summary>
    [Test]
    public async Task WallBeforeWhereTheTargetWasComesFirst()
    {
        var (shooterPosition, targetUid) = await MoveTargetOffTheLine();

        await Server.WaitAssertion(() =>
        {
            var wallUid = SEntMan.SpawnEntity(_testWall, new MapCoordinates(shooterPosition + new Vector2(3f, 0f), MapData.MapId));

            var results = CastCompensated(shooterPosition, Vector2.UnitX, Lag);
            Assert.That(results.Any(result => result.HitEntity == targetUid), "the target was not crossed where it was, so this proves nothing");
            Assert.That(results[0].HitEntity, Is.EqualTo(wallUid), "the wall in front of where the target was is not the nearest thing crossed");
        });
    }

    /// <summary>
    ///     The whole path: a player fires a hitscan gun along the line the target has left. Taken as fired the lag ago, the
    ///         shot damages it; taken as fired now, it does not.
    /// </summary>
    [Test]
    public async Task PlayersShotHitsWhereTheTargetWas([Values] bool lagging)
    {
        var gunNetEntity = await PlaceInHands(TestLaser);
        await SetCombatMode(true);
        await RunSeconds(2f); // the pickup cooldown

        var compensationSystem = SEntMan.System<KsHitscanLagCompensationSystem>();
        await Server.WaitPost(() => compensationSystem.LagDurationOverride = lagging ? Lag : TimeSpan.Zero);

        try
        {
            var (shooterPosition, targetUid) = await MoveTargetOffTheLine();

            var aimCoordinates = SEntMan.GetNetCoordinates(new EntityCoordinates(MapData.MapUid, shooterPosition + new Vector2(RayLength, 0f)));
            await Client.WaitPost(() =>
            {
                CEntMan.RaisePredictiveEvent(new RequestShootEvent
                {
                    Gun = gunNetEntity,
                    Coordinates = aimCoordinates,
                });
            });

            // The shot reaches the server within the part of the lag the target has been off the line for.
            var ticksPerSecond = (float)Server.ResolveDependency<IGameTiming>().TickRate;
            await RunTicks((int)((float)Lag.TotalSeconds * ticksPerSecond / 3f));

            var damage = FixedPoint2.Zero;
            await Server.WaitPost(() => damage = SEntMan.System<DamageableSystem>().GetTotalDamage(targetUid));

            if (lagging)
                Assert.That(damage, Is.GreaterThan(FixedPoint2.Zero), "a lagging player's shot along the line the target was on did not hit it");
            else
                Assert.That(damage, Is.EqualTo(FixedPoint2.Zero), "a shot taken as fired now hit a target that has left its line");
        }
        finally
        {
            await Server.WaitPost(() => compensationSystem.LagDurationOverride = null);
        }
    }
}
