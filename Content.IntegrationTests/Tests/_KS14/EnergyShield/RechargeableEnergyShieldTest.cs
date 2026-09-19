using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Pair;
using Content.Shared._KS14.EnergyShield;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Item.ItemToggle;
using Content.Shared.Power.Components;
using Content.Shared.Power.EntitySystems;
using NUnit.Framework;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._KS14.EnergyShield;

/// <summary>
///     The battery-backed energy shield: hits drain charge, a flat battery switches it off and keeps it
///         off, and a recharge lets it come back.
/// </summary>
/// <remarks>
///     Deactivation is driven entirely by <c>ChargeChangedEvent</c>. An earlier version also re-checked
///         the same condition every tick and, when the shield was already off, hand-raised a synthetic
///         <c>ItemToggledEvent</c> - a toggle that never happened, delivered to every subscriber.
/// </remarks>
[TestFixture]
[TestOf(typeof(SharedRechargeableEnergyShieldSystem))]
public sealed class RechargeableEnergyShieldTest : GameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: BaseItem
  id: KsTestRechargeableEnergyShield
  name: test rechargeable energy shield
  components:
  - type: ItemToggle
  - type: Damageable
  - type: Injurable
    damageContainer: Shield
  - type: Battery
    maxCharge: 100
    startingCharge: 100
  - type: RechargeableEnergyShield

# Drains twice as fast, so the ratio datafield is exercised rather than assumed to be one.
- type: entity
  parent: KsTestRechargeableEnergyShield
  id: KsTestRechargeableEnergyShieldFragile
  components:
  - type: RechargeableEnergyShield
    damageToChargeRatio: 2

- type: entity
  parent: KsTestRechargeableEnergyShield
  id: KsTestRechargeableEnergyShieldRecharging
  components:
  - type: BatterySelfRecharger
    autoRechargeRate: 30
";

    private const string ShieldProto = "KsTestRechargeableEnergyShield";
    private const string FragileShieldProto = "KsTestRechargeableEnergyShieldFragile";
    private const string RechargingShieldProto = "KsTestRechargeableEnergyShieldRecharging";

    private const int TicksPerSecond = 30;

    private static DamageSpecifier Damage(int amount)
    {
        var damage = new DamageSpecifier();
        damage.DamageDict.Add("Blunt", FixedPoint2.New(amount));
        return damage;
    }

    [Test]
    public async Task TestDamageDrainsShieldCharge()
    {
        var server = Pair.Server;
        var entityManager = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var batterySystem = entityManager.System<SharedBatterySystem>();
            var damageableSystem = entityManager.System<DamageableSystem>();

            var shieldUid = entityManager.Spawn(ShieldProto);
            var fragileUid = entityManager.Spawn(FragileShieldProto);

            damageableSystem.TryChangeDamage(shieldUid, Damage(10), ignoreResistances: true);
            damageableSystem.TryChangeDamage(fragileUid, Damage(10), ignoreResistances: true);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(batterySystem.GetCharge(shieldUid), Is.EqualTo(90f).Within(0.01f),
                    "Ten damage did not drain ten charge at a one-to-one ratio.");
                Assert.That(batterySystem.GetCharge(fragileUid), Is.EqualTo(80f).Within(0.01f),
                    "damageToChargeRatio was not applied to the drain.");
            }
        });
    }

    [Test]
    public async Task TestHealingDoesNotRefundCharge()
    {
        var server = Pair.Server;
        var entityManager = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var batterySystem = entityManager.System<SharedBatterySystem>();
            var damageableSystem = entityManager.System<DamageableSystem>();

            var shieldUid = entityManager.Spawn(ShieldProto);
            damageableSystem.TryChangeDamage(shieldUid, Damage(10), ignoreResistances: true);
            damageableSystem.TryChangeDamage(shieldUid, Damage(-10), ignoreResistances: true);

            Assert.That(batterySystem.GetCharge(shieldUid), Is.EqualTo(90f).Within(0.01f),
                "Healing the shield credited charge back; recharging is the self-recharger's job.");
        });
    }

    [Test]
    public async Task TestFlatShieldDeactivatesAndRefusesToActivate()
    {
        var server = Pair.Server;
        var entityManager = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var damageableSystem = entityManager.System<DamageableSystem>();
            var itemToggleSystem = entityManager.System<ItemToggleSystem>();

            var shieldUid = entityManager.Spawn(ShieldProto);

            Assert.That(itemToggleSystem.TryActivate(shieldUid), "A fully charged shield refused to switch on.");

            damageableSystem.TryChangeDamage(shieldUid, Damage(100), ignoreResistances: true);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(itemToggleSystem.IsActivated(shieldUid), Is.False,
                    "A shield drained to zero charge stayed switched on.");
                Assert.That(itemToggleSystem.TryActivate(shieldUid), Is.False,
                    "A shield with a flat battery agreed to switch back on.");
            }
        });
    }

    [Test]
    public async Task TestRechargedShieldCanBeActivatedAgain()
    {
        var server = Pair.Server;
        var entityManager = server.EntMan;

        var shieldUid = EntityUid.Invalid;

        await server.WaitAssertion(() =>
        {
            var damageableSystem = entityManager.System<DamageableSystem>();
            var itemToggleSystem = entityManager.System<ItemToggleSystem>();

            shieldUid = entityManager.Spawn(RechargingShieldProto);
            damageableSystem.TryChangeDamage(shieldUid, Damage(100), ignoreResistances: true);

            Assert.That(itemToggleSystem.TryActivate(shieldUid), Is.False,
                "A shield with a flat battery agreed to switch back on.");
        });

        await server.WaitRunTicks(TicksPerSecond);

        await server.WaitAssertion(() =>
        {
            var batterySystem = entityManager.System<SharedBatterySystem>();
            var itemToggleSystem = entityManager.System<ItemToggleSystem>();

            Assert.That(batterySystem.GetCharge(shieldUid), Is.GreaterThan(0f),
                "The self-recharger put nothing back into the shield.");
            Assert.That(itemToggleSystem.TryActivate(shieldUid),
                "A recharged shield still refused to switch on.");
        });
    }
}
