#nullable enable
using Content.IntegrationTests.Fixtures;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Robust.Shared.GameObjects;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.Damage;

/// <summary>
///     <see cref="DamageContactsComponent.SelfDamageRatio"/>: a source wears by what it actually deals, so it breaks after
///         dealing a set total, however well armoured whoever it cuts is. Without the ratio it never wears.
/// </summary>
[TestFixture]
[TestOf(typeof(DamageContactsSystem))]
public sealed class KsDamageContactsSelfDamageTest : GameTest
{
    // Ids that exist only in the test prototypes below.
    private readonly EntProtoId _wearingSource = "KsDamageContactsSelfDamageTestWearing";
    private readonly EntProtoId _lastingSource = "KsDamageContactsSelfDamageTestLasting";
    private readonly EntProtoId _walker = "KsDamageContactsSelfDamageTestWalker";
    private readonly EntProtoId _armouredWalker = "KsDamageContactsSelfDamageTestArmouredWalker";

    // The wearing source takes its wear past a modifier set that would otherwise shrug all of it off.
    [TestPrototypes]
    private const string Prototypes = @"
- type: damageModifierSet
  id: KsDamageContactsSelfDamageTestImmune
  coefficients:
    Structural: 0

- type: damageModifierSet
  id: KsDamageContactsSelfDamageTestHalfSlash
  coefficients:
    Slash: 0.5

- type: entity
  id: KsDamageContactsSelfDamageTestLasting
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
    damage:
      types:
        Slash: 2
  - type: Damageable
    damageContainer: StructuralInorganic
    damageModifierSet: KsDamageContactsSelfDamageTestImmune
  - type: Injurable
    damageContainer: StructuralInorganic
  - type: Destructible
    thresholds:
    - trigger:
        !type:DamageTrigger
        damage: 5
      behaviors:
      - !type:DoActsBehavior
        acts: [ ""Destruction"" ]

- type: entity
  parent: KsDamageContactsSelfDamageTestLasting
  id: KsDamageContactsSelfDamageTestWearing
  components:
  - type: DamageContacts
    selfDamageRatio: 0.5
    selfDamageType: Structural

- type: entity
  id: KsDamageContactsSelfDamageTestWalker
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

- type: entity
  parent: KsDamageContactsSelfDamageTestWalker
  id: KsDamageContactsSelfDamageTestArmouredWalker
  components:
  - type: Damageable
    damageModifierSet: KsDamageContactsSelfDamageTestHalfSlash
";

    private EntityUid _sourceUid;
    private EntityUid _walkerUid;

    private async Task SpawnIn(EntProtoId source, EntProtoId walker)
    {
        var testMap = await Pair.CreateTestMap();
        await Server.WaitPost(() =>
        {
            _sourceUid = SSpawnAtPosition(source, testMap.GridCoords);
            _walkerUid = SSpawnAtPosition(walker, testMap.GridCoords);

            // A body at rest falls asleep, and a sleeping body's contacts are never updated.
            var physicsComponent = SComp<PhysicsComponent>(_walkerUid);
            var physicsSystem = Server.System<SharedPhysicsSystem>();
            physicsSystem.SetSleepingAllowed(_walkerUid, physicsComponent, false);
            physicsSystem.WakeBody(_walkerUid, body: physicsComponent);
        });
    }

    /// <summary>
    ///     Runs until the source is gone, up to <paramref name="seconds"/>, and returns whether it went and what the
    ///         walker had taken by then.
    /// </summary>
    private async Task<(bool Broke, FixedPoint2 Dealt)> RunUntilBroken(float seconds)
    {
        var ticks = (int)(seconds * Server.Timing.TickRate);
        var broke = false;
        var dealt = FixedPoint2.Zero;

        for (var tick = 0; tick < ticks && !broke; tick++)
        {
            await Pair.RunTicksSync(1);
            await Server.WaitPost(() =>
            {
                broke = SEntMan.Deleted(_sourceUid);
                dealt = Server.System<DamageableSystem>().GetTotalDamage(_walkerUid);
            });
        }

        return (broke, dealt);
    }

    /// <summary>
    ///     What the source has to deal to break: its destruction threshold over its ratio. Read from the prototypes, not
    ///         restated.
    /// </summary>
    private async Task<FixedPoint2> DealtToBreak()
    {
        var dealtToBreak = FixedPoint2.Zero;
        await Server.WaitPost(() =>
        {
            var ratio = SComp<DamageContactsComponent>(_sourceUid).SelfDamageRatio!.Value;
            var threshold = Server.System<Content.Shared.Destructible.SharedDestructibleSystem>().DestroyedAt(_sourceUid);
            dealtToBreak = threshold / ratio;
        });
        return dealtToBreak;
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ASourceBreaksAfterDealingItsTotal(bool armoured)
    {
        await SpawnIn(_wearingSource, armoured ? _armouredWalker : _walker);
        var dealtToBreak = await DealtToBreak();
        var perTick = FixedPoint2.Zero;
        await Server.WaitPost(() => perTick = SComp<DamageContactsComponent>(_sourceUid).Damage.GetTotal());

        // Long enough for an armoured walker, which takes less a tick, several times over.
        var (broke, dealt) = await RunUntilBroken((float)(dealtToBreak / perTick) * 6f + 5f);

        Assert.Multiple(() =>
        {
            Assert.That(broke, "the source should break once it has dealt its total");
            // It breaks on the tick that takes it to its total, which may carry it a tick's damage past.
            Assert.That(dealt, Is.InRange(dealtToBreak, dealtToBreak + perTick),
                "it should break after dealing its total, whatever the armour of what it cut");
        });
    }

    [Test]
    public async Task ASourceWithoutTheRatioNeverWears()
    {
        await SpawnIn(_lastingSource, _walker);
        var (broke, dealt) = await RunUntilBroken(8f);

        var sourceDamage = FixedPoint2.Zero;
        await Server.WaitPost(() => sourceDamage = Server.System<DamageableSystem>().GetTotalDamage(_sourceUid));

        Assert.Multiple(() =>
        {
            Assert.That(dealt, Is.GreaterThan(FixedPoint2.Zero), "the source should hurt whoever is in it");
            Assert.That(broke, Is.False, "a source without the ratio should not break");
            Assert.That(sourceDamage, Is.EqualTo(FixedPoint2.Zero), "nor take any damage for what it deals");
        });
    }
}
