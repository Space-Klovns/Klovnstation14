#nullable enable
using Content.Client.Overlays;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.Perks.Healing;
using Content.Shared._KS14.MedicalBeam;
using Content.Shared._KS14.Perks;
using Content.Shared._KS14.Perks.Healing;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Content.Shared.Body.Components;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.Perks;

/// <summary>
///     The Healing perk's passive abilities, each on its own test perk: a faster metabolism, the built-in medical
///         HUD, lifesteal, and no bleeding.
/// </summary>
[TestFixture]
[TestOf(typeof(KsMetabolismRateSystem))]
[TestOf(typeof(KsLifestealSystem))]
[TestOf(typeof(KsNoBleedingSystem))]
public sealed class KsPerkHealingTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    // Ids that exist only in the test prototypes below.
    private readonly EntProtoId _body = "KsTestPerkHealingBody";
    private readonly EntProtoId _otherBody = "KsTestPerkHealingOtherBody";
    private readonly EntProtoId _beamGun = "KsTestPerkHealingBeamGun";
    private readonly ProtoId<KsPerkPrototype> _metabolismPerk = "KsTestPerkHealingMetabolism";
    private readonly ProtoId<KsPerkPrototype> _hudPerk = "KsTestPerkHealingHud";
    private readonly ProtoId<KsPerkPrototype> _lifestealPerk = "KsTestPerkHealingLifesteal";
    private readonly ProtoId<KsPerkPrototype> _noBleedingPerk = "KsTestPerkHealingNoBleeding";

    private static readonly ProtoId<DamageTypePrototype> Blunt = "Blunt";
    private static readonly ProtoId<DamageTypePrototype> Slash = "Slash";

    [TestPrototypes]
    private const string Prototypes = @"
- type: npcFaction
  id: KsTestPerkHealingSideA

- type: npcFaction
  id: KsTestPerkHealingSideB

- type: ksPerk
  id: KsTestPerkHealingMetabolism
  name: ks-perk-healing-name
  description: ks-perk-healing-desc
  icon: { sprite: _KS14/Interface/Perks/perks.rsi, state: healing }
  components:
  - type: KsMetabolismRateAbility
    rateMultiplier: 2

- type: ksPerk
  id: KsTestPerkHealingHud
  name: ks-perk-healing-name
  description: ks-perk-healing-desc
  icon: { sprite: _KS14/Interface/Perks/perks.rsi, state: healing }
  components:
  - type: ShowHealthBars
  - type: ShowHealthIcons

- type: ksPerk
  id: KsTestPerkHealingLifesteal
  name: ks-perk-healing-name
  description: ks-perk-healing-desc
  icon: { sprite: _KS14/Interface/Perks/perks.rsi, state: healing }
  components:
  - type: KsLifestealAbility
    fraction: 0.5

- type: ksPerk
  id: KsTestPerkHealingNoBleeding
  name: ks-perk-healing-name
  description: ks-perk-healing-desc
  icon: { sprite: _KS14/Interface/Perks/perks.rsi, state: healing }
  components:
  - type: KsNoBleedingAbility

- type: entity
  id: KsTestPerkHealingBody
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
      500: Critical
      1000: Dead
  - type: Damageable
  - type: Injurable
    damageContainer: Biological
  - type: SolutionContainerManager
  - type: Bloodstream
    bloodlossDamage:
      types:
        Bloodloss: 1
    bloodlossHealDamage:
      types:
        Bloodloss: -1
  - type: Eye
  - type: Alerts
  - type: NpcFactionMember
    factions:
    - KsTestPerkHealingSideA

- type: entity
  parent: KsTestPerkHealingBody
  id: KsTestPerkHealingOtherBody
  components:
  - type: NpcFactionMember
    factions:
    - KsTestPerkHealingSideB

- type: entity
  parent: BaseItem
  id: KsTestPerkHealingBeamGun
  components:
  - type: Gun
    fireRate: 10
    selectedMode: SemiAuto
    availableModes:
    - SemiAuto
  - type: KsMedicalBeamGun
    maxRange: 5
    heal:
      Brute: 1
";

    private async Task<(TestMapData Map, EntityUid BodyUid)> SpawnBody(ProtoId<KsPerkPrototype>? perk)
    {
        var server = Pair.Server;
        var testMap = await Pair.CreateTestMap();
        EntityUid bodyUid = default;
        await server.WaitPost(() =>
        {
            bodyUid = server.EntMan.SpawnAtPosition(_body, new EntityCoordinates(testMap.Grid, 0.5f, 0.5f));
            if (perk is { } perkId)
                Assert.That(server.System<SharedKsPerkSystem>().TryAddPerk(bodyUid, perkId), "the test perk should be given");
        });
        await Pair.RunTicksSync(2);
        return (testMap, bodyUid);
    }

    private DamageSpecifier Damage(ProtoId<DamageTypePrototype> type, float amount)
    {
        return new DamageSpecifier(Pair.Server.ProtoMan.Index(type), FixedPoint2.New(amount));
    }

    private float TotalDamage(EntityUid uid)
    {
        return (float)Pair.Server.System<DamageableSystem>().GetTotalDamage(uid);
    }

    [Test]
    public async Task MetabolismRunsFasterWhileHeld()
    {
        var server = Pair.Server;
        var (_, bodyUid) = await SpawnBody(_metabolismPerk);
        var bloodstream = server.EntMan.GetComponent<BloodstreamComponent>(bodyUid);
        var rate = server.EntMan.GetComponent<KsMetabolismRateAbilityComponent>(bodyUid).RateMultiplier;

        Assert.That(bloodstream.UpdateIntervalMultiplier, Is.EqualTo(1f / rate).Within(0.001f),
            "the metabolism's interval should shrink by the rate");

        await server.WaitPost(() => server.System<SharedKsPerkSystem>().RemovePerk(bodyUid, _metabolismPerk));
        await Pair.RunTicksSync(1);

        Assert.That(bloodstream.UpdateIntervalMultiplier, Is.EqualTo(1f).Within(0.001f),
            "taking the perk away should put the metabolism back");
    }

    [Test]
    public async Task MedicalHudShowsForThePlayerHoldingIt()
    {
        var server = Pair.Server;
        var client = Pair.Client;
        var (_, bodyUid) = await SpawnBody(_hudPerk);

        await server.WaitPost(() => server.PlayerMan.SetAttachedEntity(Pair.Player!, bodyUid));
        await Pair.RunTicksSync(5);

        await client.WaitAssertion(() =>
        {
            Assert.That(client.System<ShowHealthBarsSystem>().IsActive, "the health bars should show");
            Assert.That(client.System<ShowHealthIconsSystem>().IsActive, "the health icons should show");
        });

        await server.WaitPost(() => server.System<SharedKsPerkSystem>().RemovePerk(bodyUid, _hudPerk));
        await Pair.RunTicksSync(5);

        await client.WaitAssertion(() =>
        {
            Assert.That(client.System<ShowHealthBarsSystem>().IsActive, Is.False, "the health bars should go with the perk");
            Assert.That(client.System<ShowHealthIconsSystem>().IsActive, Is.False, "the health icons should go with the perk");
        });
    }

    [Test]
    public async Task LifestealHealsByAShareOfDamageDealtToEnemiesOnly()
    {
        var server = Pair.Server;
        var entMan = server.EntMan;
        var (testMap, bodyUid) = await SpawnBody(_lifestealPerk);
        var fraction = entMan.GetComponent<KsLifestealAbilityComponent>(bodyUid).Fraction;

        await server.WaitAssertion(() =>
        {
            var damageableSystem = server.System<DamageableSystem>();
            var enemyUid = entMan.SpawnAtPosition(_otherBody, new EntityCoordinates(testMap.Grid, 2.5f, 0.5f));
            var friendUid = entMan.SpawnAtPosition(_body, new EntityCoordinates(testMap.Grid, 3.5f, 0.5f));

            damageableSystem.TryChangeDamage(bodyUid, Damage(Blunt, 100));
            var hurt = TotalDamage(bodyUid);

            damageableSystem.TryChangeDamage(enemyUid, Damage(Blunt, 40), origin: bodyUid);
            var dealt = TotalDamage(enemyUid);
            Assert.That(dealt, Is.GreaterThan(0f));
            Assert.That(TotalDamage(bodyUid), Is.EqualTo(hurt - dealt * fraction).Within(0.05f),
                "hurting an enemy should heal the holder by the fraction");

            var afterEnemy = TotalDamage(bodyUid);
            damageableSystem.TryChangeDamage(friendUid, Damage(Blunt, 40), origin: bodyUid);
            Assert.That(TotalDamage(bodyUid), Is.EqualTo(afterEnemy).Within(0.01f), "hurting a friend should heal nothing");

            damageableSystem.TryChangeDamage(enemyUid, Damage(Blunt, 40));
            Assert.That(TotalDamage(bodyUid), Is.EqualTo(afterEnemy).Within(0.01f), "someone else's damage should heal nothing");

            // An enemy the holder is healing with a medical beam: hurting it heals the holder nothing.
            var handsSystem = server.System<SharedHandsSystem>();
            var gunUid = entMan.SpawnAtPosition(_beamGun, new EntityCoordinates(testMap.Grid, 0.5f, 0.5f));
            entMan.EnsureComponent<HandsComponent>(bodyUid);
            handsSystem.AddHand(bodyUid, "hand", HandLocation.Middle);
            Assert.That(handsSystem.TryPickupAnyHand(bodyUid, gunUid, checkActionBlocker: false), "the holder should take the beam gun");
            server.System<SharedGunSystem>().AttemptShoot(bodyUid, (gunUid, entMan.GetComponent<GunComponent>(gunUid)),
                entMan.GetComponent<TransformComponent>(enemyUid).Coordinates, enemyUid);
            Assert.That(entMan.GetComponent<KsMedicalBeamGunComponent>(gunUid).Target, Is.EqualTo(enemyUid), "the beam should be on the enemy");

            var beforeBeamed = TotalDamage(bodyUid);
            damageableSystem.TryChangeDamage(enemyUid, Damage(Blunt, 40), origin: bodyUid);
            Assert.That(TotalDamage(bodyUid), Is.EqualTo(beforeBeamed).Within(0.01f), "hurting a mob you are healing with a beam should heal you nothing");
        });
    }

    [Test]
    public async Task NoBleedingWhileHeld()
    {
        var server = Pair.Server;
        var entMan = server.EntMan;
        var (testMap, bodyUid) = await SpawnBody(_noBleedingPerk);
        EntityUid controlUid = default;

        await server.WaitAssertion(() =>
        {
            var damageableSystem = server.System<DamageableSystem>();
            controlUid = entMan.SpawnAtPosition(_body, new EntityCoordinates(testMap.Grid, 2.5f, 0.5f));

            damageableSystem.TryChangeDamage(controlUid, Damage(Slash, 40));
            damageableSystem.TryChangeDamage(bodyUid, Damage(Slash, 40));

            Assert.That(entMan.GetComponent<BloodstreamComponent>(controlUid).BleedAmount, Is.GreaterThan(0f),
                "the control should bleed from the same wound");
            Assert.That(entMan.GetComponent<BloodstreamComponent>(bodyUid).BleedAmount, Is.Zero, "the holder should not bleed");
        });
    }
}
