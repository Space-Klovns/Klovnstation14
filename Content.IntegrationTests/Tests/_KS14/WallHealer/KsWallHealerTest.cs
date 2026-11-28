#nullable enable
using Content.IntegrationTests.Fixtures;
using Content.Shared._KS14.WallHealer;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Stacks;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.WallHealer;

[TestFixture]
[TestOf(typeof(KsWallHealerSystem))]
public sealed class KsWallHealerTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: KsTestWallHealer
  components:
  - type: Transform
    anchored: true
  - type: ContainerContainer
    containers:
      supplies: !type:Container
  - type: KsWallHealer
    pools:
    - !type:KsWallHealerDamagePool
      capacity: 20
      damageTypes:
      - Piercing
      - Slash
    - !type:KsWallHealerBloodPool
      capacity: 20
      targetBloodLevel: 0.9
    treatmentFraction: 0.25
    treatmentInterval: 0.5
    selfInsertDelay: 0.1
    rechargeInterval: 1
    rechargeFraction: 0.5

- type: entity
  parent: KsTestWallHealer
  id: KsTestWallHealerRequiresId
  components:
  - type: KsWallHealer
    requiresIdCard: true

- type: entity
  parent: PassengerIDCard
  id: KsTestAllowanceIdCard
  components:
  - type: KsWallHealerAllowance
    limit: 3
    recoveryTime: 30
";

    private readonly EntProtoId _healerProto = "KsTestWallHealer";
    private readonly EntProtoId _requiresIdHealerProto = "KsTestWallHealerRequiresId";
    private readonly EntProtoId _allowanceCardProto = "KsTestAllowanceIdCard";
    private static readonly EntProtoId HumanProto = "MobHuman";
    private static readonly EntProtoId IdCardProto = "PassengerIDCard";
    private static readonly EntProtoId GauzeProto = "Gauze";
    private static readonly ProtoId<DamageTypePrototype> Piercing = "Piercing";
    private static readonly ProtoId<DamageTypePrototype> Slash = "Slash";
    private static readonly ProtoId<DamageTypePrototype> Heat = "Heat";

    private int SecondsToTicks(float seconds)
    {
        return (int)MathF.Ceiling(seconds * (float)Pair.Server.ResolveDependency<IGameTiming>().TickRate) + 1;
    }

    private Entity<KsWallHealerComponent> Healer(EntityUid uid)
    {
        return (uid, Pair.Server.EntMan.GetComponent<KsWallHealerComponent>(uid));
    }

    private DamageSpecifier Damage(EntityUid uid)
    {
        var entityManager = Pair.Server.EntMan;
        return entityManager.System<DamageableSystem>().GetPositiveDamage((uid, entityManager.GetComponent<DamageableComponent>(uid)));
    }

    private static FixedPoint2 DamageOf(DamageSpecifier damage, ProtoId<DamageTypePrototype> type)
    {
        return damage.DamageDict.TryGetValue(type, out var value) ? value : FixedPoint2.Zero;
    }

    /// <summary>
    ///     A healer and a patient beside it, the patient damaged.
    /// </summary>
    private async Task<(EntityUid Healer, EntityUid Patient)> SpawnHealerAndPatient(EntProtoId healerProto, DamageSpecifier damage)
    {
        var server = Pair.Server;
        var testMap = await Pair.CreateTestMap();
        EntityUid healerUid = default;
        EntityUid patientUid = default;

        await server.WaitPost(() =>
        {
            healerUid = server.EntMan.SpawnEntity(healerProto, new EntityCoordinates(testMap.Grid, 1.5f, 0.5f));
            patientUid = server.EntMan.SpawnEntity(HumanProto, new EntityCoordinates(testMap.Grid, 0.5f, 0.5f));
            server.EntMan.System<DamageableSystem>().ChangeDamage(patientUid, damage, ignoreResistances: true);
        });

        await Pair.RunTicksSync(1);
        return (healerUid, patientUid);
    }

    private async Task InsertOwnHand(EntityUid healerUid, EntityUid patientUid)
    {
        var server = Pair.Server;
        await server.WaitPost(() =>
            Assert.That(server.EntMan.System<KsWallHealerSystem>().TryStartInsert(Healer(healerUid), patientUid, patientUid), Is.True));

        // Until it goes in, with the insert delay as no more than a lower bound.
        var maxTicks = SecondsToTicks((float)Healer(healerUid).Comp.SelfInsertDelay.TotalSeconds + 5f);
        var ticks = 0;
        var inserted = false;
        for (; ticks < maxTicks && !inserted; ticks++)
        {
            await Pair.RunTicksSync(1);
            await server.WaitPost(() => inserted = Healer(healerUid).Comp.Patient == patientUid);
        }

        await server.WaitAssertion(() =>
            Assert.That(Healer(healerUid).Comp.Patient, Is.EqualTo(patientUid), "the patient's hand never went in"));
    }

    private async Task WaitOneTreatment(EntityUid healerUid)
    {
        await Pair.RunTicksSync(SecondsToTicks((float)Healer(healerUid).Comp.TreatmentInterval.TotalSeconds));
    }

    /// <summary>
    ///     Each treatment takes a share of each pool's capacity, split across the pool's damage types by how much of each
    ///         the patient has, leaves damage outside the pool alone, and restores blood.
    /// </summary>
    [Test]
    public async Task TreatmentHealsAcrossPoolTypesAndDrainsThePools()
    {
        var server = Pair.Server;
        var damage = new DamageSpecifier { DamageDict = { [Piercing] = 10, [Slash] = 30, [Heat] = 10 } };
        var (healerUid, patientUid) = await SpawnHealerAndPatient(_healerProto, damage);
        var bloodstreamSystem = server.EntMan.System<SharedBloodstreamSystem>();

        var levelBefore = 0f;
        await server.WaitPost(() =>
        {
            bloodstreamSystem.TryModifyBloodLevel(patientUid, -150);
            levelBefore = bloodstreamSystem.GetBloodLevel(patientUid);
        });

        await InsertOwnHand(healerUid, patientUid);

        await server.WaitAssertion(() =>
        {
            // A new patient is treated straight away.
            var healer = Healer(healerUid);
            var perTreatment = healer.Comp.Pools[0].Capacity * healer.Comp.TreatmentFraction;
            var after = Damage(patientUid);

            Assert.That((float)(DamageOf(after, Piercing) + DamageOf(after, Slash)), Is.EqualTo(40f - perTreatment).Within(0.02f));
            Assert.That((float)DamageOf(after, Piercing), Is.EqualTo(10f - perTreatment * 0.25f).Within(0.02f), "split by the share of each type");
            Assert.That(DamageOf(after, Heat), Is.EqualTo(FixedPoint2.New(10)), "heat isn't in the pool");
            Assert.That(healer.Comp.PoolAmounts[0], Is.EqualTo(healer.Comp.Pools[0].Capacity - perTreatment).Within(0.02f));

            Assert.That(bloodstreamSystem.GetBloodLevel(patientUid), Is.GreaterThan(levelBefore), "no blood was restored");
            Assert.That(healer.Comp.PoolAmounts[1], Is.LessThan(healer.Comp.Pools[1].Capacity), "the blood pool wasn't drawn on");
        });

        await WaitOneTreatment(healerUid);

        await server.WaitAssertion(() =>
        {
            var healer = Healer(healerUid);
            var perTreatment = healer.Comp.Pools[0].Capacity * healer.Comp.TreatmentFraction;
            var after = Damage(patientUid);
            Assert.That((float)(DamageOf(after, Piercing) + DamageOf(after, Slash)), Is.EqualTo(40f - 2 * perTreatment).Within(0.04f), "no second treatment");
        });
    }

    /// <summary>
    ///     Blood only comes back up to the pool's target level, however full the pool is.
    /// </summary>
    [Test]
    public async Task BloodIsOnlyRestoredUpToTheTarget()
    {
        var server = Pair.Server;
        var (healerUid, patientUid) = await SpawnHealerAndPatient(_healerProto, new DamageSpecifier());
        var bloodstreamSystem = server.EntMan.System<SharedBloodstreamSystem>();

        var target = 0f;
        await server.WaitPost(() =>
        {
            target = ((KsWallHealerBloodPool)Healer(healerUid).Comp.Pools[1]).TargetBloodLevel;
            // Just under the target, by less than one treatment gives.
            var bloodstreamComponent = server.EntMan.GetComponent<Content.Shared.Body.Components.BloodstreamComponent>(patientUid);
            var volume = (float)bloodstreamComponent.BloodReferenceSolution.Volume;
            bloodstreamSystem.TryModifyBloodLevel(patientUid, FixedPoint2.New(-(1f - target) * volume - 1f));
        });

        await InsertOwnHand(healerUid, patientUid);
        await WaitOneTreatment(healerUid);

        await server.WaitAssertion(() =>
            Assert.That(bloodstreamSystem.GetBloodLevel(patientUid), Is.EqualTo(target).Within(0.01f)));
    }

    /// <summary>
    ///     A station that requires an ID card does nothing for a patient without one, and treats them once they have one.
    /// </summary>
    [Test]
    public async Task RequiresIdCardRefusesPatientsWithoutOne()
    {
        var server = Pair.Server;
        var damage = new DamageSpecifier { DamageDict = { [Piercing] = 20 } };
        var (healerUid, patientUid) = await SpawnHealerAndPatient(_requiresIdHealerProto, damage);

        await InsertOwnHand(healerUid, patientUid);
        await WaitOneTreatment(healerUid);

        await server.WaitAssertion(() =>
        {
            Assert.That(DamageOf(Damage(patientUid), Piercing), Is.EqualTo(FixedPoint2.New(20)), "treated without an ID card");
            Assert.That(Healer(healerUid).Comp.PoolAmounts[0], Is.EqualTo(Healer(healerUid).Comp.Pools[0].Capacity));
        });

        await server.WaitPost(() =>
        {
            var cardUid = server.EntMan.SpawnEntity(IdCardProto, server.EntMan.GetComponent<TransformComponent>(patientUid).Coordinates);
            Assert.That(server.EntMan.System<InventorySystem>().TryEquip(patientUid, cardUid, "id", silent: true, force: true), Is.True);
        });

        await WaitOneTreatment(healerUid);

        await server.WaitAssertion(() =>
            Assert.That(DamageOf(Damage(patientUid), Piercing), Is.LessThan(FixedPoint2.New(20)), "not treated with an ID card"));
    }

    /// <summary>
    ///     The ID card's allowance caps treatment from every station, and recovers with time.
    /// </summary>
    [Test]
    public async Task IdAllowanceLimitsTreatmentAcrossStationsAndRecovers()
    {
        var server = Pair.Server;
        var damage = new DamageSpecifier { DamageDict = { [Piercing] = 30 } };
        var (healerUid, patientUid) = await SpawnHealerAndPatient(_healerProto, damage);

        EntityUid otherHealerUid = default;
        var limit = 0f;
        TimeSpan recoveryTime = default;
        await server.WaitPost(() =>
        {
            var cardUid = server.EntMan.SpawnEntity(_allowanceCardProto, server.EntMan.GetComponent<TransformComponent>(patientUid).Coordinates);
            Assert.That(server.EntMan.System<InventorySystem>().TryEquip(patientUid, cardUid, "id", silent: true, force: true), Is.True);

            var allowanceComponent = server.EntMan.GetComponent<KsWallHealerAllowanceComponent>(cardUid);
            limit = allowanceComponent.Limit;
            recoveryTime = allowanceComponent.RecoveryTime;
            var perTreatment = Healer(healerUid).Comp.Pools[0].Capacity * Healer(healerUid).Comp.TreatmentFraction;
            Assert.That(perTreatment, Is.GreaterThan(limit), "the test needs a treatment bigger than the allowance");

            otherHealerUid = server.EntMan.SpawnEntity(_healerProto, new EntityCoordinates(server.EntMan.GetComponent<TransformComponent>(healerUid).Coordinates.EntityId, 0.5f, 1.5f));
        });

        // Over this test's few seconds an allowance recovering over the test card's long recovery time barely moves.
        const float recoveryTolerance = 0.25f;

        await InsertOwnHand(healerUid, patientUid);

        await server.WaitAssertion(() =>
            Assert.That((float)DamageOf(Damage(patientUid), Piercing), Is.EqualTo(30f - limit).Within(recoveryTolerance), "the allowance didn't cap the treatment"));

        await WaitOneTreatment(healerUid);

        await server.WaitAssertion(() =>
            Assert.That((float)DamageOf(Damage(patientUid), Piercing), Is.EqualTo(30f - limit).Within(recoveryTolerance), "treated past a spent allowance"));

        // Another station draws on the same card.
        await server.WaitPost(() => server.EntMan.System<KsWallHealerSystem>().StopTreatment(Healer(healerUid)));
        await InsertOwnHand(otherHealerUid, patientUid);

        var spentDamage = FixedPoint2.Zero;
        await server.WaitAssertion(() =>
        {
            spentDamage = DamageOf(Damage(patientUid), Piercing);
            Assert.That((float)spentDamage, Is.EqualTo(30f - limit).Within(recoveryTolerance), "another station ignored the spent allowance");
        });

        // A tenth of the recovery time brings back a tenth of the limit.
        await Pair.RunTicksSync(SecondsToTicks((float)recoveryTime.TotalSeconds / 10f));

        await server.WaitAssertion(() =>
            Assert.That((float)DamageOf(Damage(patientUid), Piercing), Is.LessThan((float)spentDamage - limit / 20f), "the allowance never recovered"));
    }

    /// <summary>
    ///     Walking off takes the hand out, and with nobody using it the station recharges.
    /// </summary>
    [Test]
    public async Task MovingAwayEndsTreatmentAndTheStationRecharges()
    {
        var server = Pair.Server;
        var damage = new DamageSpecifier { DamageDict = { [Piercing] = 30 } };
        var (healerUid, patientUid) = await SpawnHealerAndPatient(_healerProto, damage);

        await InsertOwnHand(healerUid, patientUid);

        var drained = 0f;
        await server.WaitPost(() =>
        {
            drained = Healer(healerUid).Comp.PoolAmounts[0];
            var transformSystem = server.EntMan.System<SharedTransformSystem>();
            var coordinates = server.EntMan.GetComponent<TransformComponent>(patientUid).Coordinates;
            transformSystem.SetCoordinates(patientUid, coordinates.Offset(new System.Numerics.Vector2(0f, 4f)));
        });

        await Pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(Healer(healerUid).Comp.Patient, Is.Null, "the hand stayed in after walking off");
            Assert.That(Healer(healerUid).Comp.LastPatient, Is.EqualTo(patientUid));
            Assert.That(drained, Is.LessThan(Healer(healerUid).Comp.Pools[0].Capacity), "nothing was drained to recharge");
        });

        await Pair.RunTicksSync(SecondsToTicks((float)Healer(healerUid).Comp.RechargeInterval.TotalSeconds));

        await server.WaitAssertion(() =>
            Assert.That(Healer(healerUid).Comp.PoolAmounts[0], Is.GreaterThan(drained), "the station never recharged"));
    }

    /// <summary>
    ///     Taking a supply hands over one of a stocked stack and leaves the rest of it in the station.
    /// </summary>
    [Test]
    public async Task TakingASupplyHandsOverOneOfTheStack()
    {
        var server = Pair.Server;
        var (healerUid, userUid) = await SpawnHealerAndPatient(_healerProto, new DamageSpecifier());
        const int stocked = 3;

        await server.WaitPost(() =>
        {
            var stackUid = server.EntMan.SpawnEntity(GauzeProto, server.EntMan.GetComponent<TransformComponent>(healerUid).Coordinates);
            server.EntMan.System<SharedStackSystem>().SetCount(stackUid, stocked);
            var containerSystem = server.EntMan.System<SharedContainerSystem>();
            var container = containerSystem.GetContainer(healerUid, Healer(healerUid).Comp.SupplyContainerId);
            Assert.That(containerSystem.Insert(stackUid, container), Is.True);
        });

        await server.WaitPost(() =>
            Assert.That(server.EntMan.System<KsWallHealerSystem>().TryTakeSupply(Healer(healerUid), userUid), Is.True));

        await Pair.RunTicksSync(1);

        await server.WaitAssertion(() =>
        {
            var wallHealerSystem = server.EntMan.System<KsWallHealerSystem>();
            var heldUid = server.EntMan.System<SharedHandsSystem>().GetActiveItem(userUid);
            Assert.That(heldUid, Is.Not.Null, "nothing was handed over");
            Assert.That(server.EntMan.System<SharedStackSystem>().GetCount(heldUid!.Value), Is.EqualTo(1));
            Assert.That(wallHealerSystem.GetSupplyCount(Healer(healerUid)), Is.EqualTo(stocked - 1));
        });
    }
}
