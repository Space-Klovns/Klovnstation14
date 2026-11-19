#nullable enable
using Content.IntegrationTests.Fixtures;
using Content.Shared._Mono.ArmorPlate;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Inventory;
using Content.Shared.Movement.Components;
using Content.Shared.Storage;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Battlefield14.Armor;

/// <summary>
///     Armor plates in a worn vest: the first plate in absorbs its share of the wearer's damage, wears down for it and
///         sets their speed, and the next plate takes over when it is gone.
/// </summary>
[TestFixture]
[TestOf(typeof(ArmorPlateSystem))]
public sealed class BF14ArmorPlateTest : GameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: BF14TestPlateVest
  components:
  - type: Item
    size: Huge
  - type: Clothing
    slots: [ OUTERCLOTHING ]
  - type: Storage
    maxItemSize: Normal
    grid:
    - 0,0,3,1
  - type: ArmorPlateHolder

- type: entity
  id: BF14TestHeavyPlate
  components:
  - type: Item
    size: Small
  - type: Damageable
  - type: Injurable
  - type: ArmorPlateItem
    walkSpeedModifier: 0.8
    sprintSpeedModifier: 0.8
    staminaDamageMultiplier: 0
    absorptionRatios:
      Piercing: 0.5
    damageMultipliers:
      Piercing: 2

- type: entity
  id: BF14TestLightPlate
  components:
  - type: Item
    size: Small
  - type: Damageable
  - type: Injurable
  - type: ArmorPlateItem
    staminaDamageMultiplier: 0
";

    private static readonly EntProtoId Human = "MobHuman";
    private static readonly ProtoId<DamageTypePrototype> Piercing = "Piercing";
    private readonly EntProtoId _vest = "BF14TestPlateVest";
    private readonly EntProtoId _heavyPlate = "BF14TestHeavyPlate";
    private readonly EntProtoId _lightPlate = "BF14TestLightPlate";

    [Test]
    public async Task ActivePlateAbsorbsAndHandsOver()
    {
        var server = Pair.Server;
        var testMap = await Pair.CreateTestMap();
        var inventorySystem = server.System<InventorySystem>();
        var containerSystem = server.System<SharedContainerSystem>();
        var damageableSystem = server.System<DamageableSystem>();

        EntityUid humanUid = default;
        EntityUid vestUid = default;
        EntityUid heavyPlateUid = default;
        EntityUid lightPlateUid = default;

        await server.WaitAssertion(() =>
        {
            var origin = new EntityCoordinates(testMap.MapUid, 0, 0);
            humanUid = SSpawnAtPosition(Human, origin);
            vestUid = SSpawnAtPosition(_vest, origin);
            Assert.That(inventorySystem.TryEquip(humanUid, vestUid, "outerClothing", silent: true, force: true), Is.True);

            var storageContainer = containerSystem.GetContainer(vestUid, StorageComponent.ContainerId);
            heavyPlateUid = SSpawnAtPosition(_heavyPlate, origin);
            lightPlateUid = SSpawnAtPosition(_lightPlate, origin);
            Assert.That(containerSystem.Insert(heavyPlateUid, storageContainer), Is.True);
            Assert.That(containerSystem.Insert(lightPlateUid, storageContainer), Is.True);

            Assert.That(SEntMan.GetComponent<ArmorPlateHolderComponent>(vestUid).ActivePlate, Is.EqualTo(heavyPlateUid),
                "the first plate in is not the active one");
            Assert.That(SEntMan.GetComponent<MovementSpeedModifierComponent>(humanUid).WalkSpeedModifier, Is.EqualTo(0.8f).Within(0.001f),
                "the active plate did not slow its wearer");

            // Half of it absorbed; the plate takes that half twice over.
            var shot = new DamageSpecifier();
            shot.DamageDict.Add(Piercing, FixedPoint2.New(20));
            damageableSystem.TryChangeDamage(humanUid, shot, out var dealt, ignoreResistances: true, origin: vestUid);
            Assert.That(dealt.GetTotal(), Is.EqualTo(FixedPoint2.New(10)), "the plate did not absorb half the piercing damage");
            var plateDamageable = SEntMan.GetComponent<DamageableComponent>(heavyPlateUid);
            Assert.That(damageableSystem.GetPositiveDamage((heavyPlateUid, plateDamageable)).GetTotal(), Is.EqualTo(FixedPoint2.New(20)),
                "the plate was not worn down by what it absorbed");

            // No origin, such as bleeding: past the plates.
            damageableSystem.TryChangeDamage(humanUid, shot, out var unsourced, ignoreResistances: true);
            Assert.That(unsourced.GetTotal(), Is.EqualTo(FixedPoint2.New(20)), "a plate absorbed damage with no origin");

            SEntMan.DeleteEntity(heavyPlateUid);
            Assert.That(SEntMan.GetComponent<ArmorPlateHolderComponent>(vestUid).ActivePlate, Is.EqualTo(lightPlateUid),
                "the next plate did not take over when the first was gone");
            Assert.That(SEntMan.GetComponent<MovementSpeedModifierComponent>(humanUid).WalkSpeedModifier, Is.EqualTo(1f).Within(0.001f),
                "the wearer stayed slowed after the heavy plate was gone");
        });
    }
}
