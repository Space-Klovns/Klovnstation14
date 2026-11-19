#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.NPC.Targeting;
using Content.Server._KS14.Perks.Tank;
using Content.Server.NPC;
using Content.Server.NPC.Systems;
using Content.Shared._KS14.Perks;
using Content.Shared._KS14.Perks.Tank;
using Content.Shared._RMC14.Vendors;
using Content.Shared.Actions;
using Content.Shared.Actions.Components;
using Content.Shared.Alert;
using Content.Shared.Body.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Movement.Components;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.Perks;

/// <summary>
///     The Tank perk's abilities, each on its own test perk: the auto-injector on going critical and its cooldown, the
///         living shield soaking damage until it breaks, Draw Fire making NPCs prefer the holder for a while, and a
///         vendor entry only a perk's holder can take.
/// </summary>
[TestFixture]
[TestOf(typeof(KsCritAutoInjectorSystem))]
[TestOf(typeof(KsLivingShieldSystem))]
[TestOf(typeof(KsDrawFireSystem))]
[TestOf(typeof(KsTargetPriorityCon))]
[TestOf(typeof(SharedCMAutomatedVendorSystem))]
public sealed class KsPerkTankTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Dirty = true };

    // Ids that exist only in the test prototypes below.
    private readonly EntProtoId _body = "KsTestPerkTankBody";
    private readonly EntProtoId _armouredBody = "KsTestPerkTankArmouredBody";
    private readonly EntProtoId _armour = "KsTestPerkTankArmour";
    private readonly EntProtoId _laserGun = "KsTestPerkTankLaserGun";
    private readonly EntProtoId _vendor = "KsTestPerkTankVendor";
    private readonly EntProtoId _vendorItem = "KsTestPerkTankVendorItem";
    private readonly ProtoId<AlertPrototype> _alert = "KsTestPerkTankAlert";
    private readonly ProtoId<KsPerkPrototype> _injectorPerk = "KsTestPerkTankInjector";
    private readonly ProtoId<KsPerkPrototype> _shieldPerk = "KsTestPerkTankShield";
    private readonly ProtoId<KsPerkPrototype> _drawFirePerk = "KsTestPerkTankDrawFire";
    private readonly ProtoId<KsPerkPrototype> _vendorPerk = "KsTestPerkTankVendorPerk";
    private const string TargetsQuery = "KsTestPerkTankTargets";

    // What the test perk's injector is filled with, and what hurts the test body: no tuning of theirs is relied on.
    private static readonly ProtoId<ReagentPrototype> Water = "Water";
    private static readonly ProtoId<DamageTypePrototype> Blunt = "Blunt";

    [TestPrototypes]
    private const string Prototypes = @"
- type: alert
  id: KsTestPerkTankAlert
  icons:
  - sprite: _KS14/Interface/Perks/perks.rsi
    state: tank
  name: ks-perk-tank-name
  description: ks-perk-tank-desc

- type: ksPerk
  id: KsTestPerkTankInjector
  name: ks-perk-tank-name
  description: ks-perk-tank-desc
  icon: { sprite: _KS14/Interface/Perks/perks.rsi, state: tank }
  alert: KsTestPerkTankAlert
  components:
  - type: KsCritAutoInjectorAbility
    cooldown: 2
    cooldownAlert: KsTestPerkTankAlert
    injection:
      reagents:
      - ReagentId: Water
        Quantity: 10

- type: entity
  id: KsTestPerkTankShieldAction
  components:
  - type: Action
  - type: InstantAction
    event: !type:KsLivingShieldActionEvent

- type: ksPerk
  id: KsTestPerkTankShield
  name: ks-perk-tank-name
  description: ks-perk-tank-desc
  icon: { sprite: _KS14/Interface/Perks/perks.rsi, state: tank }
  components:
  - type: KsLivingShieldAbility
    capacity: 50
    walkModifier: 0.5
    sprintModifier: 0.5
    cooldown: 5
  actions:
  - KsTestPerkTankShieldAction

- type: entity
  id: KsTestPerkTankDrawFireAction
  components:
  - type: Action
  - type: InstantAction
    event: !type:KsDrawFireActionEvent

- type: ksPerk
  id: KsTestPerkTankDrawFire
  name: ks-perk-tank-name
  description: ks-perk-tank-desc
  icon: { sprite: _KS14/Interface/Perks/perks.rsi, state: tank }
  components:
  - type: KsDrawFireAbility
    duration: 1
    priority: 1
  actions:
  - KsTestPerkTankDrawFireAction

- type: ksPerk
  id: KsTestPerkTankVendorPerk
  name: ks-perk-tank-name
  description: ks-perk-tank-desc
  icon: { sprite: _KS14/Interface/Perks/perks.rsi, state: tank }

- type: utilityQuery
  id: KsTestPerkTankTargets
  query:
  - !type:ComponentQuery
    components:
    - type: Speech
  considerations:
  - !type:TargetDistanceCon
    curve: !type:QuadraticCurve
      slope: -1
      exponent: 1
      yOffset: 1
      xOffset: 0
  - !type:KsTargetPriorityCon
    defaultPriority: 0.1
    curve: !type:QuadraticCurve
      slope: 1
      exponent: 1
      yOffset: 0
      xOffset: 0

- type: entity
  id: KsTestPerkTankBody
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
      50: Critical
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
  - type: Alerts
  - type: Actions
  - type: MovementSpeedModifier
  - type: Speech

- type: entity
  parent: KsTestPerkTankBody
  id: KsTestPerkTankArmouredBody
  components:
  - type: Sprite
  - type: InventorySlots
  - type: Inventory
    templateId: human
  - type: ContainerContainer

- type: entity
  id: KsTestPerkTankArmour
  components:
  - type: Item
  - type: Clothing
    slots:
    - outerClothing
  - type: Armor
    modifiers:
      coefficients:
        Blunt: 0.5

- type: entity
  parent: BasicHitscan
  id: KsTestPerkTankLaser
  categories: [ HideSpawnMenu ]
  components:
  - type: HitscanBasicDamage
    damage:
      types:
        Heat: 10

- type: entity
  parent: BaseItem
  id: KsTestPerkTankLaserGun
  components:
  - type: Gun
    fireRate: 100
    selectedMode: SemiAuto
    availableModes:
    - SemiAuto
  - type: BasicEntityAmmoProvider
    proto: KsTestPerkTankLaser
    capacity: 5

- type: entity
  parent: BaseItem
  id: KsTestPerkTankVendorItem

- type: entity
  id: KsTestPerkTankVendor
  components:
  - type: UserInterface
    interfaces:
      enum.CMAutomatedVendorUI.Key:
        type: CMAutomatedVendorBui
  - type: CMAutomatedVendor
    sections:
    - name: Perk
      entries:
      - id: KsTestPerkTankVendorItem
        amount: 5
        ksRequiredPerk: KsTestPerkTankVendorPerk
";

    private int Ticks(float seconds) => (int)MathF.Ceiling(seconds * (float)Pair.Server.ResolveDependency<IGameTiming>().TickRate);

    private async Task<(TestMapData Map, EntityUid BodyUid)> SpawnBody(ProtoId<KsPerkPrototype>? perk, float x = 0.5f, EntProtoId? body = null)
    {
        var server = Pair.Server;
        var testMap = await Pair.CreateTestMap();
        EntityUid bodyUid = default;
        await server.WaitPost(() =>
        {
            bodyUid = server.EntMan.SpawnAtPosition(body ?? _body, new EntityCoordinates(testMap.Grid, x, 0.5f));
            if (perk is { } perkId)
                Assert.That(server.System<SharedKsPerkSystem>().TryAddPerk(bodyUid, perkId), "the test perk should be given");
        });
        await Pair.RunTicksSync(2);
        return (testMap, bodyUid);
    }

    // A float: the test body bleeds a little of what is in its blood when hurt.
    private float BloodWater(EntityUid bodyUid)
    {
        var entMan = Pair.Server.EntMan;
        var bloodstream = entMan.GetComponent<BloodstreamComponent>(bodyUid);
        Entity<Content.Shared.Chemistry.Components.SolutionComponent>? solutionEntity = null;
        Assert.That(entMan.System<SharedSolutionContainerSystem>().ResolveSolution(bodyUid, bloodstream.BloodSolutionName, ref solutionEntity, out var solution),
            "the body should have a bloodstream");
        return (float)solution!.GetTotalPrototypeQuantity(Water);
    }

    private void Hurt(EntityUid uid, float amount)
    {
        var damage = new DamageSpecifier(Pair.Server.ProtoMan.Index(Blunt), FixedPoint2.New(amount));
        Pair.Server.System<DamageableSystem>().TryChangeDamage(uid, damage);
    }

    private Entity<ActionComponent> GetAction(EntityUid bodyUid)
    {
        return Pair.Server.System<SharedActionsSystem>().GetActions(bodyUid).Single();
    }

    private void PerformAction(EntityUid bodyUid)
    {
        Pair.Server.System<SharedActionsSystem>().PerformAction(bodyUid, GetAction(bodyUid), predicted: false);
    }

    [Test]
    public async Task AutoInjectorFiresOnGoingCriticalThenWaitsOutItsCooldown()
    {
        var server = Pair.Server;
        var (_, bodyUid) = await SpawnBody(_injectorPerk);
        var injector = server.EntMan.GetComponent<KsCritAutoInjectorAbilityComponent>(bodyUid);
        var dose = (float)injector.Injection.Volume; // water only, in the test perk
        var mobState = server.EntMan.GetComponent<MobStateComponent>(bodyUid);

        await server.WaitAssertion(() =>
        {
            Assert.That(BloodWater(bodyUid), Is.Zero);
            Hurt(bodyUid, 60);
            Assert.That(mobState.CurrentState, Is.EqualTo(MobState.Critical), "the body should be critical");
            Assert.That(BloodWater(bodyUid), Is.EqualTo(dose).Within(0.5f), "going critical should inject the dose");
            Assert.That(server.System<AlertsSystem>().TryGetAlertState(bodyUid, server.ProtoMan.Index(_alert).AlertKey, out var alertState) &&
                        alertState.Cooldown != null, "the perk's alert should show the cooldown");

            // Up and down again at once: still cooling down.
            Hurt(bodyUid, -60);
            Assert.That(mobState.CurrentState, Is.EqualTo(MobState.Alive));
            Hurt(bodyUid, 60);
            Assert.That(BloodWater(bodyUid), Is.EqualTo(dose).Within(0.5f), "a second crit inside the cooldown should inject nothing");
            Hurt(bodyUid, -60);
        });

        await Pair.RunTicksSync(Ticks((float)injector.Cooldown.TotalSeconds + 0.5f));

        await server.WaitAssertion(() =>
        {
            Hurt(bodyUid, 60);
            Assert.That(BloodWater(bodyUid), Is.EqualTo(dose * 2).Within(1f), "after the cooldown going critical should inject again");
        });
    }

    [Test]
    public async Task LivingShieldSoaksDamageSlowsAndBlocksFiringThenBreaksIntoItsCooldown()
    {
        var server = Pair.Server;
        var entMan = server.EntMan;
        var (testMap, bodyUid) = await SpawnBody(_shieldPerk);
        var shield = entMan.GetComponent<KsLivingShieldAbilityComponent>(bodyUid);
        var movement = entMan.GetComponent<MovementSpeedModifierComponent>(bodyUid);
        var gunUid = await SpawnAtPosition(_laserGun, new EntityCoordinates(testMap.Grid, 0.5f, 0.5f));
        var baseWalk = movement.CurrentWalkSpeed;

        await server.WaitAssertion(() => PerformAction(bodyUid));
        await Pair.RunTicksSync(1);

        await server.WaitAssertion(() =>
        {
            Assert.That(shield.Raised, "the action should raise the shield");
            Assert.That(movement.CurrentWalkSpeed, Is.EqualTo(baseWalk * shield.WalkModifier).Within(0.01f), "the raised shield should slow its holder");

            var gun = new Entity<GunComponent>(gunUid, entMan.GetComponent<GunComponent>(gunUid));
            Assert.That(server.System<SharedGunSystem>().AttemptShoot(bodyUid, gun, new EntityCoordinates(testMap.Grid, 5f, 0.5f)), Is.False,
                "the holder can't fire with the shield up");

            // Less than it holds: all of it soaked.
            Hurt(bodyUid, shield.Capacity / 2f);
            Assert.That(server.System<DamageableSystem>().GetTotalDamage(bodyUid), Is.EqualTo(FixedPoint2.Zero), "the shield should soak a hit it can hold");
            Assert.That(shield.Raised, "and stay up");

            // More than is left: it soaks what it can, lets the rest through and breaks.
            Hurt(bodyUid, shield.Capacity);
            Assert.That(server.System<DamageableSystem>().GetTotalDamage(bodyUid), Is.EqualTo(FixedPoint2.New(shield.Capacity / 2f)),
                "what the shield couldn't hold should get through");
            Assert.That(shield.Raised, Is.False, "the spent shield should break");
            Assert.That(movement.CurrentWalkSpeed, Is.EqualTo(baseWalk).Within(0.01f), "and stop slowing its holder");

            var action = GetAction(bodyUid);
            Assert.That(action.Comp.Cooldown?.End, Is.GreaterThan(server.ResolveDependency<IGameTiming>().CurTime), "breaking should start the cooldown");
        });
    }

    [Test]
    public async Task LivingShieldWearsDownByWhatGetsPastArmour()
    {
        var server = Pair.Server;
        var entMan = server.EntMan;
        var (testMap, bodyUid) = await SpawnBody(_shieldPerk, body: _armouredBody);
        var shield = entMan.GetComponent<KsLivingShieldAbilityComponent>(bodyUid);

        await server.WaitAssertion(() =>
        {
            var armourUid = entMan.SpawnAtPosition(_armour, new EntityCoordinates(testMap.Grid, 0.5f, 0.5f));
            Assert.That(server.System<Content.Shared.Inventory.InventorySystem>().TryEquip(bodyUid, armourUid, "outerClothing", silent: true, force: true),
                "the armour should be put on");
            PerformAction(bodyUid);
            Assert.That(shield.Raised, "the action should raise the shield");
        });
        await Pair.RunTicksSync(1);

        await server.WaitAssertion(() =>
        {
            var armour = entMan.GetComponent<Content.Shared.Armor.ArmorComponent>(entMan.GetComponent<Content.Shared.Inventory.InventoryComponent>(bodyUid)
                .Containers.Single(container => container.ID == "outerClothing").ContainedEntity!.Value);
            var coefficient = armour.Modifiers.Coefficients[Blunt];
            Assert.That(coefficient, Is.LessThan(1f), "the test armour should stop some of the hit");

            // A hit the shield can hold even unarmoured: it should spend only what the armour lets through.
            var hit = shield.Capacity / 2f;
            Hurt(bodyUid, hit);
            Assert.That(server.System<DamageableSystem>().GetTotalDamage(bodyUid), Is.EqualTo(FixedPoint2.Zero), "the shield should soak what gets past the armour");
            Assert.That(shield.Remaining, Is.EqualTo(shield.Capacity - hit * coefficient).Within(0.01f),
                "the shield should wear down by what the armour let through, not by the whole hit");
        });
    }

    [Test]
    public async Task DrawFireMakesTheHolderTheTargetThenPutsItsPriorityBack()
    {
        var server = Pair.Server;
        var entMan = server.EntMan;
        var (testMap, npcUid) = await SpawnBody(null);
        EntityUid nearUid = default;
        EntityUid tankUid = default;

        await server.WaitPost(() =>
        {
            nearUid = entMan.SpawnAtPosition(_body, new EntityCoordinates(testMap.Grid, 2.5f, 0.5f));
            tankUid = entMan.SpawnAtPosition(_body, new EntityCoordinates(testMap.Grid, 5.5f, 0.5f));
            Assert.That(server.System<SharedKsPerkSystem>().TryAddPerk(tankUid, _drawFirePerk), "the test perk should be given");
            // A priority it had already, to be put back afterwards.
            entMan.AddComponent<KsNpcTargetPriorityComponent>(tankUid).Priority = 0.05f;
        });
        await Pair.RunTicksSync(2);

        EntityUid Best()
        {
            var blackboard = new NPCBlackboard();
            blackboard.SetValue(NPCBlackboard.Owner, npcUid);
            var scores = server.System<NPCUtilitySystem>().GetEntities(blackboard, TargetsQuery, bestOnly: false).Entities;
            return scores.Where(pair => pair.Key != npcUid).MaxBy(pair => pair.Value).Key;
        }

        await server.WaitAssertion(() =>
        {
            Assert.That(Best(), Is.EqualTo(nearUid), "without a taunt the nearer target should be preferred");
            PerformAction(tankUid);
            Assert.That(Best(), Is.EqualTo(tankUid), "Draw Fire should make the holder the preferred target");
        });

        var drawFire = entMan.GetComponent<KsDrawFireAbilityComponent>(tankUid);
        await Pair.RunTicksSync(Ticks((float)drawFire.Duration.TotalSeconds + 0.5f));

        await server.WaitAssertion(() =>
        {
            Assert.That(Best(), Is.EqualTo(nearUid), "once it ends the nearer target should be preferred again");
            Assert.That(entMan.GetComponent<KsNpcTargetPriorityComponent>(tankUid).Priority, Is.EqualTo(0.05f),
                "the priority the holder had before should be put back");
        });
    }

    [Test]
    public async Task PerkGatedVendorEntryIsOnlyForItsHolder()
    {
        var server = Pair.Server;
        var entMan = server.EntMan;
        var (testMap, bodyUid) = await SpawnBody(null);
        EntityUid vendorUid = default;
        await server.WaitPost(() => vendorUid = entMan.SpawnAtPosition(_vendor, new EntityCoordinates(testMap.Grid, 1.5f, 0.5f)));
        await Pair.RunTicksSync(2);

        int CountVended()
        {
            var count = 0;
            var query = entMan.AllEntityQueryEnumerator<MetaDataComponent>();
            while (query.MoveNext(out var metaData))
            {
                if (metaData.EntityPrototype?.ID == _vendorItem.Id)
                    count++;
            }

            return count;
        }

        void Vend() => entMan.EventBus.RaiseLocalEvent(vendorUid, new CMVendorVendBuiMsg(0, 0, new List<int>())
        {
            Actor = bodyUid,
            UiKey = CMAutomatedVendorUI.Key,
        });

        await server.WaitPost(Vend);
        await Pair.RunTicksSync(2);
        await server.WaitAssertion(() => Assert.That(CountVended(), Is.Zero, "someone without the perk should get nothing"));

        await server.WaitPost(() =>
        {
            Assert.That(server.System<SharedKsPerkSystem>().TryAddPerk(bodyUid, _vendorPerk), "the test perk should be given");
            Vend();
        });
        await Pair.RunTicksSync(2);
        await server.WaitAssertion(() => Assert.That(CountVended(), Is.EqualTo(1), "the perk's holder should get the entry"));
    }

    /// <summary>
    ///     The partial (Prototypes/_KS14/NPCs/Targeting/Partials) adds the priority consideration to the public target
    ///         queries and keeps what upstream gave them.
    /// </summary>
    [TestCase("NearbyGunTargets")]
    [TestCase("NearbyMeleeTargets")]
    public async Task PublicTargetQueriesWeighTargetPriority(string queryId)
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var query = Pair.Server.ProtoMan.Index<Content.Server.NPC.Queries.UtilityQueryPrototype>(queryId);
            Assert.That(query.Considerations.OfType<KsTargetPriorityCon>().Count(), Is.EqualTo(1), $"{queryId} should weigh target priority once");
            Assert.That(query.Considerations.OfType<Content.Server.NPC.Queries.Considerations.TargetDistanceCon>(), Is.Not.Empty,
                $"{queryId} should keep upstream's considerations");
        });
    }
}
