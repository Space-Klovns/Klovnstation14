#nullable enable
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Server.Weapons.Ranged.Systems;
using Content.Shared.Damage.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Timing;
using Robust.UnitTesting.Pool;
using static Content.IntegrationTests.Tests._KS14.NPC.KsNpcSquadTestHelpers;

namespace Content.IntegrationTests.Tests._KS14.Projectiles;

/// <summary>
///     What looking ahead of a round (<c>KsRayCollisionSystem</c>) must not lose: whoever the round starts inside, with
///         something else within its first step, and whatever is within a step of the thing that turned it round.
/// </summary>
public sealed class KsRayCollisionTest : GameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    private const string Dart = "KsRayCollisionTestDart";
    private const string Plate = "KsRayCollisionTestPlate";
    private const string Barrier = "KsRayCollisionTestBarrier";
    private const string Mirror = "KsRayCollisionTestMirror";
    private const string Body = "KsRayCollisionTestBody";
    private const string Shooter = "KsRayCollisionTestShooter";

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: KsRayCollisionTestShooter

# Not used up by what it hits, so that what it does after can be seen.
- type: entity
  id: KsRayCollisionTestDart
  components:
  - type: Physics
    bodyType: Dynamic
  - type: Fixtures
    fixtures:
      projectile:
        shape:
          !type:PhysShapeAabb
          bounds: -0.1,-0.1,0.1,0.1
        hard: false
        mask:
        - Impassable
        - BulletImpassable
  - type: Projectile
    deleteOnCollide: false
    damage:
      types:
        Blunt: 5
  - type: Reflective

# A tenth of a tile deep, across the way a round comes.
- type: entity
  id: KsRayCollisionTestPlate
  components:
  - type: Physics
    bodyType: Static
  - type: Fixtures
    fixtures:
      fix1:
        shape:
          !type:PhysShapeAabb
          bounds: -0.4,-0.05,0.4,0.05
        layer:
        - BulletImpassable
  - type: Damageable
  - type: Injurable

# Hard, in a round's way, and with nothing to damage.
- type: entity
  id: KsRayCollisionTestBarrier
  components:
  - type: Physics
    bodyType: Static
  - type: Fixtures
    fixtures:
      fix1:
        shape:
          !type:PhysShapeAabb
          bounds: -0.4,-0.05,0.4,0.05
        layer:
        - BulletImpassable

- type: entity
  parent: KsRayCollisionTestPlate
  id: KsRayCollisionTestMirror
  components:
  - type: Reflect
    reflectProb: 1
    spread: 0.0001 # as good as straight back; not 0, which NextAngle's min < max debug assert refuses

- type: entity
  id: KsRayCollisionTestBody
  components:
  - type: Physics
    bodyType: Static
  - type: Fixtures
    fixtures:
      fix1:
        shape:
          !type:PhysShapeCircle
          radius: 0.35
        layer:
        - BulletImpassable
  - type: Damageable
  - type: Injurable
";

    /// <summary>
    ///     A round fired from inside someone - they are stood on the shooter - with a wall within its first step hits them, not
    ///         the wall: a ray from inside a shape never finds it, so the wall was the first thing on the way, and took the
    ///         round before the contact with whoever it started in was looked at.
    /// </summary>
    [Test]
    public async Task TestRoundHitsWhoItStartsInsideBeforeTheWallBehindThem()
    {
        var entManager = Server.EntMan;
        var tileDefinitionManager = Server.ResolveDependency<ITileDefinitionManager>();
        var map = await Pair.CreateTestMap();
        EntityUid shooterUid = default, dartUid = default, bodyUid = default, wallUid = default, gridUid = default;
        var muzzle = new Vector2(0.5f, 0.5f);

        await Server.WaitPost(() =>
        {
            gridUid = MakeGrid(entManager, tileDefinitionManager, map.MapId, map.Grid, new Vector2i(-3, -6), new Vector2i(3, 6)).Owner;
            shooterUid = entManager.SpawnAtPosition(Shooter, new EntityCoordinates(gridUid, muzzle));
            // The muzzle is inside them, and the wall is half a tile on: well within the round's first step.
            bodyUid = entManager.SpawnAtPosition(Body, new EntityCoordinates(gridUid, muzzle + new Vector2(0f, 0.2f)));
            wallUid = entManager.SpawnAtPosition(Plate, new EntityCoordinates(gridUid, muzzle + new Vector2(0f, 0.5f)));
        });

        await Pair.RunTicksSync(5);

        // Made as it is shot, as a gun makes its round: one lying there beforehand, with no shooter yet, is a projectile
        //      touching them, and physics has it hit them where it lies before it is ever shot.
        await Server.WaitPost(() =>
        {
            dartUid = entManager.SpawnAtPosition(Dart, new EntityCoordinates(gridUid, muzzle));
            entManager.System<GunSystem>().ShootProjectile(dartUid, new Vector2(0f, 1f), Vector2.Zero, gunUid: null, user: shooterUid);
        });

        await Pair.RunTicksSync(10);

        await Server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(GetDamage(entManager, bodyUid), Is.GreaterThan(0f), "whoever the round started inside should be hit");
                Assert.That(GetDamage(entManager, wallUid), Is.Zero, "and not the wall behind them, which the round never got to");
            });
        });
    }

    /// <summary>
    ///     A round turned round by what it hits, hits what is within a step of there on its way back: the step physics moves
    ///         it from where it was turned is looked along too. It was not, and the round stepped clean over anything thin
    ///         enough. Here that is whoever shot it, which it passed through on its way out.
    /// </summary>
    [Test]
    public async Task TestReflectedRoundHitsWhatIsWithinAStepOfTheMirror()
    {
        var entManager = Server.EntMan;
        var tileDefinitionManager = Server.ResolveDependency<ITileDefinitionManager>();
        var timing = Server.ResolveDependency<IGameTiming>();
        var map = await Pair.CreateTestMap();
        EntityUid shooterUid = default, dartUid = default, gridUid = default;

        await Server.WaitPost(() =>
        {
            gridUid = MakeGrid(entManager, tileDefinitionManager, map.MapId, map.Grid, new Vector2i(-3, -8), new Vector2i(3, 4)).Owner;

            // The mirror's near face is at 0, and the shooter's plate a quarter of a tile short of it. The round goes a tile a
            //      tick, and physics moves it in two halves of that (its substeps, at the usual tick rate): from the mirror it
            //      is next at 0.5 and then 1 back, and at neither does it touch a plate a tenth of a tile deep at 0.25.
            entManager.SpawnAtPosition(Mirror, new EntityCoordinates(gridUid, new Vector2(0.5f, 0.05f)));
            shooterUid = entManager.SpawnAtPosition(Plate, new EntityCoordinates(gridUid, new Vector2(0.5f, -0.25f)));
            dartUid = entManager.SpawnAtPosition(Dart, new EntityCoordinates(gridUid, new Vector2(0.5f, -3.3f)));
        });

        await Pair.RunTicksSync(5);

        await Server.WaitPost(() =>
            entManager.System<GunSystem>().ShootProjectile(dartUid,
                new Vector2(0f, 1f),
                Vector2.Zero,
                gunUid: null,
                user: shooterUid,
                speed: timing.TickRate)); // a tile a tick

        await Pair.RunTicksSync(15);

        await Server.WaitAssertion(() =>
        {
            Assert.That(entManager.GetComponent<TransformComponent>(dartUid).LocalPosition.Y, Is.LessThan(-1f),
                "the mirror should have turned the round back");
            Assert.That(GetDamage(entManager, shooterUid), Is.GreaterThan(0f),
                "the round should hit its shooter on the way back, not step over them");
        });
    }

    /// <summary>
    ///     A round is used up by something hard with nothing to damage (no <c>DamageableComponent</c>), and does not go on
    ///         through it into what is behind.
    /// </summary>
    [Test]
    public async Task TestRoundIsSpentOnWhatCannotBeDamaged()
    {
        var entManager = Server.EntMan;
        var tileDefinitionManager = Server.ResolveDependency<ITileDefinitionManager>();
        var map = await Pair.CreateTestMap();
        EntityUid shooterUid = default, dartUid = default, behindUid = default, gridUid = default;

        await Server.WaitPost(() =>
        {
            gridUid = MakeGrid(entManager, tileDefinitionManager, map.MapId, map.Grid, new Vector2i(-3, -6), new Vector2i(3, 6)).Owner;
            shooterUid = entManager.SpawnAtPosition(Shooter, new EntityCoordinates(gridUid, new Vector2(0.5f, -2.5f)));
            entManager.SpawnAtPosition(Barrier, new EntityCoordinates(gridUid, new Vector2(0.5f, 0f)));
            behindUid = entManager.SpawnAtPosition(Plate, new EntityCoordinates(gridUid, new Vector2(0.5f, 1.5f)));
        });

        await Pair.RunTicksSync(5);

        await Server.WaitPost(() =>
        {
            dartUid = entManager.SpawnAtPosition(Dart, new EntityCoordinates(gridUid, new Vector2(0.5f, -2.5f)));
            entManager.System<GunSystem>().ShootProjectile(dartUid, new Vector2(0f, 1f), Vector2.Zero, gunUid: null, user: shooterUid);
        });

        await Pair.RunTicksSync(15);

        await Server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(entManager.GetComponent<Content.Shared.Projectiles.ProjectileComponent>(dartUid).ProjectileSpent,
                    "the round should be spent on the barrier");
                Assert.That(GetDamage(entManager, behindUid), Is.Zero, "and what is behind the barrier untouched");
            });
        });
    }

    /// <summary>
    ///     A round turned back onto something it has hit before hits it again: between two mirrors it goes back and forth, and
    ///         does not pass through the first on its second time there for having hit it once already.
    /// </summary>
    [Test]
    public async Task TestRoundTurnedBackHitsTheSameThingAgain()
    {
        var entManager = Server.EntMan;
        var tileDefinitionManager = Server.ResolveDependency<ITileDefinitionManager>();
        var timing = Server.ResolveDependency<IGameTiming>();
        var map = await Pair.CreateTestMap();
        EntityUid shooterUid = default, dartUid = default, gridUid = default;

        await Server.WaitPost(() =>
        {
            gridUid = MakeGrid(entManager, tileDefinitionManager, map.MapId, map.Grid, new Vector2i(-3, -8), new Vector2i(3, 8)).Owner;

            // Facing each other, 1.6 apart: the near faces are at 0 and -1.6.
            entManager.SpawnAtPosition(Mirror, new EntityCoordinates(gridUid, new Vector2(0.5f, 0.05f)));
            entManager.SpawnAtPosition(Mirror, new EntityCoordinates(gridUid, new Vector2(0.5f, -1.65f)));
            shooterUid = entManager.SpawnAtPosition(Shooter, new EntityCoordinates(gridUid, new Vector2(0.5f, -0.6f)));
        });

        await Pair.RunTicksSync(5);

        await Server.WaitPost(() =>
        {
            dartUid = entManager.SpawnAtPosition(Dart, new EntityCoordinates(gridUid, new Vector2(0.5f, -0.6f)));
            entManager.System<GunSystem>().ShootProjectile(dartUid,
                new Vector2(0f, 1f),
                Vector2.Zero,
                gunUid: null,
                user: shooterUid,
                speed: timing.TickRate); // a tile a tick: several times each way in the ticks below
        });

        for (var i = 0; i < 12; i++)
        {
            await Pair.RunTicksSync(1);
            await Server.WaitAssertion(() =>
                Assert.That(entManager.GetComponent<TransformComponent>(dartUid).LocalPosition.Y, Is.InRange(-1.9f, 0.2f),
                    "the round should stay between the mirrors, turned back by each every time it gets there"));
        }
    }

    private static float GetDamage(IEntityManager entManager, EntityUid uid)
    {
#pragma warning disable CS0618 // a hit at all is what is asked, not how much
        return entManager.System<DamageableSystem>().GetTotalDamage(uid).Float();
#pragma warning restore CS0618
    }
}
