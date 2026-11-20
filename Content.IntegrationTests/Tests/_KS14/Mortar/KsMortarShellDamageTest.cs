#nullable enable
using System;
using System.Collections.Generic;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests._KS14.FieldCommander;
using Content.Server.Explosion.EntitySystems;
using Content.Shared._RMC14.Vendors;
using Content.Shared.Armor;
using Content.Shared.Clothing.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Explosion;
using Content.Shared.FixedPoint;
using Content.Shared.Inventory;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Trigger.Components.Effects;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.Mortar;

/// <summary>
///     Content: a mortar shell landing square on a defender in the armour every defender takes from the Field Commander
///         vendor puts them into crit, early in the crit window and well short of death; one landing a tile off hurts them
///         less, and the same hit on a defender without the armour hurts more.
/// </summary>
[TestFixture]
public sealed class KsMortarShellDamageTest : GameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    private static readonly EntProtoId MortarShell = "KsMortarShell";
    private static readonly EntProtoId DefenderMob = "MobHuman";

    /// <summary>
    ///     The base gear every defender takes with their first loadout (<c>KsColMarTechFieldCommanderNt</c>); its armour
    ///         is whatever in it has an <see cref="ArmorComponent"/>.
    /// </summary>
    private static readonly EntProtoId BaseGearBundle = "KsScenarioVendorBundleFieldCommanderNtBase";

    /// <summary>
    ///     How far into the crit window, at most, a direct hit on an armoured defender may land: roughly its first third.
    /// </summary>
    private const float MaxWindowFraction = 1f / 3f;

    [Test]
    public async Task DirectHitCritsAnArmouredDefenderWithRoomToSpare()
    {
        var testMap = await Pair.CreateTestMap();
        var explosionSystem = SEntMan.System<ExplosionSystem>();
        var damageableSystem = SEntMan.System<DamageableSystem>();
        var mobThresholdSystem = SEntMan.System<MobThresholdSystem>();
        var mobStateSystem = SEntMan.System<MobStateSystem>();

        ExplosionOnTriggerComponent trigger = default!;
        EntityUid armouredUid = default, offCentreUid = default, unarmouredUid = default;
        var blasts = new List<MapCoordinates>();
        var armourCoefficient = 1f;

        await Server.WaitPost(() =>
        {
            Assert.That(SProtoMan.Index(MortarShell).TryGetComponent(out ExplosionOnTriggerComponent? shellTrigger, SEntMan.ComponentFactory), Is.True,
                "precondition: the shell goes off as it lands");
            trigger = shellTrigger!;

            // Three blasts far enough apart that none reaches another's target.
            var radius = explosionSystem.IntensityToRadius(trigger.TotalIntensity, trigger.IntensitySlope, trigger.MaxTileIntensity);
            var apart = (int)MathF.Ceiling(2f * radius) + 4;
            KsFieldCommanderTestHelpers.FillFloor(Server, testMap, halfExtent: 2 * apart + 2);

            armouredUid = SpawnDefender(testMap, 0, 0, armoured: true);
            offCentreUid = SpawnDefender(testMap, apart + 1, 0, armoured: true);
            unarmouredUid = SpawnDefender(testMap, 2 * apart, 0, armoured: false);

            for (var i = 0; i < 3; i++)
                blasts.Add(new MapCoordinates(new Vector2(i * apart + 0.5f, 0.5f), testMap.MapId));

            var resistance = new GetExplosionResistanceEvent(trigger.ExplosionType.Id);
            SEntMan.EventBus.RaiseLocalEvent(armouredUid, ref resistance);
            armourCoefficient = resistance.DamageCoefficient;
            Assert.That(armourCoefficient, Is.LessThan(1f), "precondition: the vendor's armour resists explosions");
        });

        var before = new Dictionary<EntityUid, FixedPoint2>();
        await Server.WaitPost(() =>
        {
            foreach (var uid in new[] { armouredUid, offCentreUid, unarmouredUid })
                before[uid] = damageableSystem.GetTotalDamage(uid);

            foreach (var blast in blasts)
            {
                explosionSystem.QueueExplosion(blast, trigger.ExplosionType.Id, trigger.TotalIntensity, trigger.IntensitySlope, trigger.MaxTileIntensity,
                    cause: null, maxTileBreak: trigger.MaxTileBreak, canCreateVacuum: false);
            }
        });

        // Read as soon as the blasts land, before any fire they light has burned for long: the blast is what is tuned here.
        var dealt = new Dictionary<EntityUid, float>();
        for (var tick = 0; tick < 120 && dealt.Count < 3; tick++)
        {
            await Pair.RunTicksSync(1);
            await Server.WaitPost(() =>
            {
                foreach (var uid in new[] { armouredUid, offCentreUid, unarmouredUid })
                {
                    var damage = (float)(damageableSystem.GetTotalDamage(uid) - before[uid]);
                    if (!dealt.ContainsKey(uid) && damage > 1f)
                        dealt[uid] = damage;
                }
            });
        }

        await Server.WaitAssertion(() =>
        {
            Assert.That(dealt.ContainsKey(armouredUid) && dealt.ContainsKey(offCentreUid) && dealt.ContainsKey(unarmouredUid), Is.True,
                "every blast should have hurt its defender");

            Assert.That(mobThresholdSystem.TryGetThresholdForState(armouredUid, MobState.Critical, out var critical), Is.True);
            Assert.That(mobThresholdSystem.TryGetThresholdForState(armouredUid, MobState.Dead, out var dead), Is.True);
            var criticalAt = (float)critical!.Value;
            var deadAt = (float)dead!.Value;
            var limit = criticalAt + MaxWindowFraction * (deadAt - criticalAt);

            TestContext.Out.WriteLine($"armour coefficient {armourCoefficient:0.###}; crit at {criticalAt}, dead at {deadAt}; " +
                $"direct hit {dealt[armouredUid]:0.#} armoured, {dealt[unarmouredUid]:0.#} unarmoured; a tile off {dealt[offCentreUid]:0.#} armoured");

            Assert.Multiple(() =>
            {
                Assert.That(dealt[armouredUid], Is.GreaterThanOrEqualTo(criticalAt), "a direct hit should put an armoured defender into crit");
                Assert.That(dealt[armouredUid], Is.LessThanOrEqualTo(limit),
                    $"a direct hit should leave an armoured defender in the first third of the crit window (at most {limit})");
                Assert.That(mobStateSystem.IsCritical(armouredUid), Is.True, "the armoured defender should be in crit");
                Assert.That(mobStateSystem.IsDead(armouredUid), Is.False, "the armoured defender should not be dead");
                Assert.That(dealt[offCentreUid], Is.LessThan(dealt[armouredUid]), "a shell a tile off should hurt less than a direct hit");
                Assert.That(dealt[unarmouredUid], Is.GreaterThan(dealt[armouredUid]), "the armour should take some of the blast");
            });
        });
    }

    private EntityUid SpawnDefender(TestMapData testMap, int x, int y, bool armoured)
    {
        var uid = SEntMan.SpawnEntity(DefenderMob, new EntityCoordinates(testMap.Grid.Owner, x + 0.5f, y + 0.5f));
        if (!armoured)
            return uid;

        var inventorySystem = SEntMan.System<InventorySystem>();
        Assert.That(SProtoMan.Index(BaseGearBundle).TryGetComponent(out CMVendorBundleComponent? bundle, SEntMan.ComponentFactory), Is.True,
            "precondition: the base gear is a bundle");

        var worn = 0;
        foreach (var itemId in bundle!.Bundle)
        {
            if (!SProtoMan.Index(itemId).TryGetComponent<ArmorComponent>(out _, SEntMan.ComponentFactory))
                continue;

            var itemUid = SEntMan.SpawnEntity(itemId, SEntMan.GetComponent<TransformComponent>(uid).Coordinates);
            var clothing = SEntMan.GetComponent<ClothingComponent>(itemUid);
            Assert.That(inventorySystem.TryGetSlots(uid, out var slots), Is.True);

            var equipped = false;
            foreach (var slot in slots!)
            {
                if ((slot.SlotFlags & clothing.Slots) == 0)
                    continue;

                equipped = inventorySystem.TryEquip(uid, itemUid, slot.Name, silent: true, force: true);
                if (equipped)
                    break;
            }

            Assert.That(equipped, Is.True, $"{itemId} should be worn");
            worn++;
        }

        Assert.That(worn, Is.GreaterThan(0), "precondition: the base gear has armour in it");
        return uid;
    }
}
