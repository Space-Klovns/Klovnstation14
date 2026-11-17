#nullable enable
using System.Collections.Generic;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.Damage;

/// <summary>
///     <see cref="DamageContactsComponent.ExclusiveGroup"/>: sources of one group hurt an entity touching several of them
///         like one, sources of different groups each hurt it, and leaving the one that was hurting it for another hands
///         the damage over. Each source here deals a damage type of its own, so what hurt the walker shows in what it has.
/// </summary>
[TestFixture]
[TestOf(typeof(DamageContactsSystem))]
public sealed class KsDamageContactsGroupTest : GameTest
{
    private const string SlashType = "Slash";
    private const string BluntType = "Blunt";
    private static readonly ProtoId<DamageTypePrototype> Slash = SlashType;
    private static readonly ProtoId<DamageTypePrototype> Blunt = BluntType;

    // Ids that exist only in the test prototypes below.
    private readonly EntProtoId _slashWire = "KsDamageContactsGroupTestSlashWire";
    private readonly EntProtoId _bluntWire = "KsDamageContactsGroupTestBluntWire";
    private readonly EntProtoId _bluntOther = "KsDamageContactsGroupTestBluntOther";
    private readonly EntProtoId _slashUngrouped = "KsDamageContactsGroupTestSlashUngrouped";
    private readonly EntProtoId _bluntUngrouped = "KsDamageContactsGroupTestBluntUngrouped";
    private readonly EntProtoId _smallSlashWire = "KsDamageContactsGroupTestSmallSlashWire";
    private readonly EntProtoId _walker = "KsDamageContactsGroupTestWalker";

    // Wide sources, so the walker is in all of them wherever they are put; the small one it can step out of.
    [TestPrototypes]
    private const string Prototypes = $@"
- type: entity
  abstract: true
  id: KsDamageContactsGroupTestBase
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

- type: entity
  parent: KsDamageContactsGroupTestBase
  id: KsDamageContactsGroupTestSlashWire
  components:
  - type: DamageContacts
    exclusiveGroup: wire
    damage:
      types:
        {SlashType}: 1

- type: entity
  parent: KsDamageContactsGroupTestBase
  id: KsDamageContactsGroupTestBluntWire
  components:
  - type: DamageContacts
    exclusiveGroup: wire
    damage:
      types:
        {BluntType}: 1

- type: entity
  parent: KsDamageContactsGroupTestBase
  id: KsDamageContactsGroupTestBluntOther
  components:
  - type: DamageContacts
    exclusiveGroup: other
    damage:
      types:
        {BluntType}: 1

- type: entity
  parent: KsDamageContactsGroupTestBase
  id: KsDamageContactsGroupTestSlashUngrouped
  components:
  - type: DamageContacts
    damage:
      types:
        {SlashType}: 1

- type: entity
  parent: KsDamageContactsGroupTestBase
  id: KsDamageContactsGroupTestBluntUngrouped
  components:
  - type: DamageContacts
    damage:
      types:
        {BluntType}: 1

- type: entity
  parent: KsDamageContactsGroupTestSlashWire
  id: KsDamageContactsGroupTestSmallSlashWire
  components:
  - type: Fixtures
    fixtures:
      fix1:
        hard: false
        shape:
          !type:PhysShapeAabb
          bounds: ""-1,-1,1,1""
        layer:
        - MidImpassable

- type: entity
  id: KsDamageContactsGroupTestWalker
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

    private EntityUid _walkerUid;
    private EntityCoordinates _origin;

    private async Task SpawnWalkerIn(params EntProtoId[] sources)
    {
        var testMap = await Pair.CreateTestMap();
        _origin = testMap.GridCoords;

        await Server.WaitPost(() =>
        {
            foreach (var source in sources)
            {
                SSpawnAtPosition(source, _origin);
            }

            _walkerUid = SSpawnAtPosition(_walker, _origin);

            // A body at rest falls asleep, and a sleeping body's contacts are never updated.
            var physicsComponent = SComp<PhysicsComponent>(_walkerUid);
            var physicsSystem = Server.System<SharedPhysicsSystem>();
            physicsSystem.SetSleepingAllowed(_walkerUid, physicsComponent, false);
            physicsSystem.WakeBody(_walkerUid, body: physicsComponent);
        });
    }

    private async Task<(FixedPoint2 Slash, FixedPoint2 Blunt)> Damage()
    {
        (FixedPoint2, FixedPoint2) damage = default;
        await Server.WaitPost(() =>
        {
            var damageDict = Server.System<DamageableSystem>().GetAllDamage(_walkerUid).DamageDict;
            damage = (damageDict.GetValueOrDefault(Slash), damageDict.GetValueOrDefault(Blunt));
        });
        return damage;
    }

    [Test]
    public async Task SourcesOfOneGroupHurtLikeOne()
    {
        await SpawnWalkerIn(_slashWire, _bluntWire);
        await RunSeconds(3.5f);

        var (slash, blunt) = await Damage();
        Assert.Multiple(() =>
        {
            Assert.That(slash + blunt, Is.GreaterThan(FixedPoint2.Zero), "touching the sources should hurt");
            Assert.That(slash == FixedPoint2.Zero || blunt == FixedPoint2.Zero,
                $"only one of two sources of a group should hurt at once, but both did (slash {slash}, blunt {blunt})");
        });
    }

    [Test]
    public async Task SourcesOfDifferentGroupsBothHurt()
    {
        await SpawnWalkerIn(_slashWire, _bluntOther);
        await RunSeconds(3.5f);

        var (slash, blunt) = await Damage();
        Assert.Multiple(() =>
        {
            Assert.That(slash, Is.GreaterThan(FixedPoint2.Zero), "the source of one group should hurt");
            Assert.That(blunt, Is.EqualTo(slash), "the source of another group should hurt just as often");
        });
    }

    /// <summary>
    ///     Upstream's behaviour, kept: of the sources with no group, one hurts.
    /// </summary>
    [Test]
    public async Task SourcesWithNoGroupHurtLikeOne()
    {
        await SpawnWalkerIn(_slashUngrouped, _bluntUngrouped);
        await RunSeconds(3.5f);

        var (slash, blunt) = await Damage();
        Assert.Multiple(() =>
        {
            Assert.That(slash + blunt, Is.GreaterThan(FixedPoint2.Zero), "touching the sources should hurt");
            Assert.That(slash == FixedPoint2.Zero || blunt == FixedPoint2.Zero,
                $"only one of two sources with no group should hurt at once, but both did (slash {slash}, blunt {blunt})");
        });
    }

    /// <summary>
    ///     In a small piece, then in a wide one of its group as well: the small one goes on being the one that hurts.
    ///         Stepping out of it while still in the wide one, the wide one takes over, with its own damage.
    /// </summary>
    [Test]
    public async Task LeavingOnePieceForAnotherOfItsGroupKeepsTheDamageGoing()
    {
        await SpawnWalkerIn(_smallSlashWire);
        await RunSeconds(1.5f);
        await Server.WaitPost(() => SSpawnAtPosition(_bluntWire, _origin));
        await RunSeconds(2f);

        var (slashInBoth, bluntInBoth) = await Damage();
        Assert.Multiple(() =>
        {
            Assert.That(slashInBoth, Is.GreaterThan(FixedPoint2.Zero), "the piece touched first should hurt");
            Assert.That(bluntInBoth, Is.EqualTo(FixedPoint2.Zero), "a second piece of its group should add nothing");
        });

        await Server.WaitPost(() =>
            Server.System<SharedTransformSystem>().SetCoordinates(_walkerUid, _origin.Offset(new Vector2(5f, 0f))));
        await RunSeconds(0.5f); // for the contact to end
        var (slashOnLeaving, _) = await Damage();
        await RunSeconds(2.5f);

        var (slashAfter, bluntAfter) = await Damage();
        Assert.Multiple(() =>
        {
            Assert.That(bluntAfter, Is.GreaterThan(FixedPoint2.Zero), "the piece still touched should take over");
            Assert.That(slashAfter, Is.EqualTo(slashOnLeaving), "the piece that was left should stop hurting");
        });
    }
}
