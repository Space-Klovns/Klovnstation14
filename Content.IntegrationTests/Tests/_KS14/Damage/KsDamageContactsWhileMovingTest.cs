#nullable enable
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Robust.Shared.GameObjects;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._KS14.Damage;

/// <summary>
///     <see cref="DamageContactsComponent.OnlyWhileMoving"/>: standing still in the source is safe, moving through it hurts,
///         and the first step after standing still hurts at once. Without it, contact damage is upstream's.
/// </summary>
[TestFixture]
[TestOf(typeof(DamageContactsSystem))]
public sealed class KsDamageContactsWhileMovingTest : GameTest
{
    private const string MovingOnlySource = "KsDamageContactsTestMovingOnlySource";
    private const string AlwaysSource = "KsDamageContactsTestAlwaysSource";
    private const string Walker = "KsDamageContactsTestWalker";

    private readonly EntProtoId _movingOnlySource = MovingOnlySource;
    private readonly EntProtoId _alwaysSource = AlwaysSource;
    private readonly EntProtoId _walker = Walker;

    // A wide source, so the walker stays inside it however far it goes. Weightless and undamped, the walker keeps
    //     whatever velocity the test gives it.
    [TestPrototypes]
    private const string Prototypes = $@"
- type: entity
  id: {MovingOnlySource}
  components:
  - type: Physics
    bodyType: Static
  - type: Fixtures
    fixtures:
      fix1:
        hard: false
        shape:
          !type:PhysShapeAabb
          bounds: ""-20,-20,20,20""
        layer:
        - MidImpassable
  - type: DamageContacts
    onlyWhileMoving: true
    damage:
      types:
        Slash: 1

- type: entity
  parent: {MovingOnlySource}
  id: {AlwaysSource}
  components:
  - type: DamageContacts
    onlyWhileMoving: false

- type: entity
  id: {Walker}
  components:
  - type: Physics
    bodyType: Dynamic
  - type: Fixtures
    fixtures:
      fix1:
        hard: false
        density: 10
        shape:
          !type:PhysShapeCircle
          radius: 0.3
        mask:
        - MidImpassable
  - type: Damageable
  - type: Injurable
";

    private static readonly Vector2 WalkingVelocity = new(0.5f, 0f);

    private EntityUid _walkerUid;

    private async Task SpawnInSource(EntProtoId source)
    {
        var testMap = await Pair.CreateTestMap();
        await Server.WaitPost(() =>
        {
            SSpawnAtPosition(source, testMap.GridCoords);
            _walkerUid = SSpawnAtPosition(_walker, testMap.GridCoords);

            // A body at rest falls asleep, and a sleeping body's contacts are never updated.
            var physicsComponent = SComp<PhysicsComponent>(_walkerUid);
            var physicsSystem = Server.System<SharedPhysicsSystem>();
            physicsSystem.SetSleepingAllowed(_walkerUid, physicsComponent, false);
            physicsSystem.WakeBody(_walkerUid, body: physicsComponent);
        });
        await Pair.RunTicksSync(2);
        await Server.WaitAssertion(() =>
            Assert.That(SEntMan.HasComponent<DamagedByContactComponent>(_walkerUid), "the walker should be touching the source"));
    }

    /// <summary>
    ///     Runs the server for <paramref name="time"/> with the walker held at <paramref name="velocity"/>, as walking or
    ///         standing still would, and returns its damage at the end.
    /// </summary>
    private Task<FixedPoint2> Hold(Vector2 velocity, TimeSpan time)
    {
        return Hold(velocity, ticks: (int)Math.Ceiling(time.TotalSeconds * Server.ResolveDependency<IGameTiming>().TickRate));
    }

    private async Task<FixedPoint2> Hold(Vector2 velocity, int ticks)
    {
        for (var tick = 0; tick < ticks; tick++)
        {
            await Server.WaitPost(() => Server.System<SharedPhysicsSystem>().SetLinearVelocity(_walkerUid, velocity));
            await Server.WaitRunTicks(1);
        }

        return await Damage();
    }

    private async Task<FixedPoint2> Damage()
    {
        var damage = FixedPoint2.Zero;
        await Server.WaitPost(() => damage = Server.System<DamageableSystem>().GetTotalDamage(_walkerUid));
        return damage;
    }

    [Test]
    public async Task StandingStillIsSafeAndTheFirstStepHurtsAtOnce()
    {
        await SpawnInSource(_movingOnlySource);

        var stillDamage = await Hold(Vector2.Zero, TimeSpan.FromSeconds(3));
        Assert.That(stillDamage, Is.EqualTo(FixedPoint2.Zero), "standing still in the source should be safe");

        var firstStepDamage = await Hold(WalkingVelocity, ticks: 1);
        Assert.That(firstStepDamage, Is.GreaterThan(FixedPoint2.Zero), "the first step after standing still should hurt at once");

        var walkedDamage = await Hold(WalkingVelocity, TimeSpan.FromSeconds(2.5));
        Assert.That(walkedDamage, Is.GreaterThan(firstStepDamage), "walking on through the source should keep hurting");

        var stoppedDamage = await Hold(Vector2.Zero, TimeSpan.FromSeconds(2));
        Assert.That(stoppedDamage, Is.EqualTo(walkedDamage), "stopping in the source should stop the damage");

        var restartDamage = await Hold(WalkingVelocity, ticks: 1);
        Assert.That(restartDamage, Is.GreaterThan(stoppedDamage),
            "the first step after standing still for over a second should hurt at once");
    }

    [Test]
    public async Task WithoutTheSwitchStandingStillStillHurts()
    {
        await SpawnInSource(_alwaysSource);

        var stillDamage = await Hold(Vector2.Zero, TimeSpan.FromSeconds(3));
        Assert.That(stillDamage, Is.GreaterThan(FixedPoint2.Zero), "a source without the switch should hurt whoever stands in it");
    }
}
