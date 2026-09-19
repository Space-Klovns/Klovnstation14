using System.Linq;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Pair;
using Content.Server._KS14.Medical.IvDrip;
using Content.Shared._KS14.Medical.IvDrip;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Fluids.Components;
using Content.Shared.Inventory;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._KS14.Medical.IvDrip;

/// <summary>
///     When a hit on the wearer tears a worn IV drip's line out.
/// </summary>
/// <remarks>
///     These are regression tests as much as behaviour tests. Spillage used to hang off
///         <c>DamageModifyEvent</c>, which is the armour hook: it is raised before the damage is known to
///         land, is skipped entirely when the damage ignores resistances, and fires at full strength even
///         when armour goes on to absorb the whole hit. It also ran on the client, where the solution was
///         split out of the drip but no puddle could be made.
/// </remarks>
[TestFixture]
[TestOf(typeof(IvDripSpillageSystem))]
public sealed class IvDripSpillageTest : GameTest
{
    // Server-only. The test wearer carries a human inventory template without a sprite, which the
    // client's clothing system logs an error over, and nothing here needs a client.
    public override PoolSettings PoolSettings => PsDisconnected;

    [TestPrototypes]
    private const string Prototypes = @"
- type: damageModifierSet
  id: KsTestBluntImmune
  coefficients:
    Blunt: 0

- type: entity
  parent: BaseItem
  id: KsTestSpillingIvDrip
  name: test spilling iv drip
  components:
  - type: Clothing
    slots:
    - neck
  - type: IvDrip
    spillOnWearerAttacked: true
    spillAmount: 5
  - type: SolutionManager
    solutions: null
    solutionData:
      ivDrip:
        maxVol: 100

- type: entity
  parent: KsTestSpillingIvDrip
  id: KsTestNonSpillingIvDrip
  components:
  - type: IvDrip
    spillOnWearerAttacked: false

- type: entity
  id: KsTestIvDripSpillWearer
  name: test iv drip spill wearer
  components:
  - type: Inventory
    templateId: human
  - type: ContainerContainer
  - type: Damageable
  - type: Injurable
    damageContainer: Biological

# Takes no Blunt at all, so a Blunt hit is fully absorbed before any damage is dealt.
- type: entity
  parent: KsTestIvDripSpillWearer
  id: KsTestIvDripSpillWearerArmoured
  components:
  - type: Damageable
    damageModifierSet: KsTestBluntImmune
  - type: Injurable
    damageContainer: Biological
";

    private const string SpillingDripProto = "KsTestSpillingIvDrip";
    private const string NonSpillingDripProto = "KsTestNonSpillingIvDrip";
    private const string WearerProto = "KsTestIvDripSpillWearer";
    private const string ArmouredWearerProto = "KsTestIvDripSpillWearerArmoured";

    private const string NeckSlot = "neck";
    private const string DripSolution = "ivDrip";
    private const string TestReagent = "Water";

    private static readonly FixedPoint2 StartingVolume = FixedPoint2.New(100);

    /// <summary>
    ///     Spawns a wearer on a grid with a filled drip equipped, and hits them with the given damage.
    /// </summary>
    /// <returns>How much is left in the drip afterwards, and whether a puddle appeared.</returns>
    private async Task<(FixedPoint2 RemainingVolume, bool Puddled)> DamageDripWearer(
        DamageSpecifier damage,
        bool ignoreResistances = false,
        string dripProto = SpillingDripProto,
        string wearerProto = WearerProto)
    {
        var pair = Pair;
        var server = pair.Server;
        var entityManager = server.EntMan;

        var testMap = await pair.CreateTestMap();

        var remainingVolume = FixedPoint2.Zero;
        var puddled = false;

        await server.WaitAssertion(() =>
        {
            var damageableSystem = entityManager.System<DamageableSystem>();
            var inventorySystem = entityManager.System<InventorySystem>();
            var solutionContainerSystem = entityManager.System<SharedSolutionContainerSystem>();

            var wearerUid = entityManager.SpawnAtPosition(wearerProto, testMap.GridCoords);
            var dripUid = entityManager.SpawnAtPosition(dripProto, testMap.GridCoords);

            Assert.That(solutionContainerSystem.TryGetSolution(dripUid, DripSolution, out var dripSolutionEntity, out _),
                "Test drip has no drip solution.");
            solutionContainerSystem.TryAddReagent(dripSolutionEntity!.Value, TestReagent, StartingVolume, out _);

            Assert.That(inventorySystem.TryEquip(wearerUid, dripUid, NeckSlot, silent: true, force: true),
                "Could not equip the test drip onto the test wearer.");

            damageableSystem.TryChangeDamage(wearerUid, damage, ignoreResistances: ignoreResistances);

            Assert.That(solutionContainerSystem.TryGetSolution(dripUid, DripSolution, out _, out var dripSolution));
            remainingVolume = dripSolution!.Volume;

            puddled = entityManager.EntityQuery<PuddleComponent>(true).Any();
        });

        return (remainingVolume, puddled);
    }

    private static DamageSpecifier Damage(string damageType, int amount)
    {
        var damage = new DamageSpecifier();
        damage.DamageDict.Add(damageType, FixedPoint2.New(amount));
        return damage;
    }

    [Test]
    public async Task TestBruteDamageSpillsTheDrip()
    {
        var (remainingVolume, puddled) = await DamageDripWearer(Damage("Blunt", 10));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(remainingVolume, Is.EqualTo(StartingVolume - FixedPoint2.New(5)),
                "A brute hit on the wearer did not spill one spillAmount out of the drip.");
            Assert.That(puddled, "The spilled solution did not become a puddle.");
        }
    }

    /// <summary>
    ///     Regression: <c>DamageModifyEvent</c> is skipped entirely for damage that ignores resistances,
    ///         so a drip could never be spilled by it.
    /// </summary>
    [Test]
    public async Task TestBruteDamageIgnoringResistancesStillSpillsTheDrip()
    {
        var (remainingVolume, puddled) = await DamageDripWearer(Damage("Blunt", 10), ignoreResistances: true);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(remainingVolume, Is.EqualTo(StartingVolume - FixedPoint2.New(5)),
                "A resistance-ignoring brute hit did not spill the drip.");
            Assert.That(puddled, "The spilled solution did not become a puddle.");
        }
    }

    /// <summary>
    ///     Regression: <c>DamageModifyEvent</c> fires before armour is known to have absorbed the hit, so
    ///         a fully blocked attack used to spill the drip at full strength.
    /// </summary>
    [Test]
    public async Task TestFullyAbsorbedDamageDoesNotSpillTheDrip()
    {
        var (remainingVolume, puddled) = await DamageDripWearer(
            Damage("Blunt", 10),
            wearerProto: ArmouredWearerProto);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(remainingVolume, Is.EqualTo(StartingVolume),
                "A hit the wearer was completely immune to still spilled the drip.");
            Assert.That(puddled, Is.False, "A fully absorbed hit still made a puddle.");
        }
    }

    [Test]
    public async Task TestUnlistedDamageTypeDoesNotSpillTheDrip()
    {
        var (remainingVolume, puddled) = await DamageDripWearer(Damage("Heat", 10));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(remainingVolume, Is.EqualTo(StartingVolume),
                "Heat damage spilled a drip that only lists brute damage types.");
            Assert.That(puddled, Is.False, "Heat damage made a puddle out of the drip.");
        }
    }

    [Test]
    public async Task TestDripThatDoesNotSpillIsLeftAlone()
    {
        var (remainingVolume, puddled) = await DamageDripWearer(
            Damage("Blunt", 10),
            dripProto: NonSpillingDripProto);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(remainingVolume, Is.EqualTo(StartingVolume),
                "A drip with spillOnWearerAttacked off spilled anyway.");
            Assert.That(puddled, Is.False, "A drip with spillOnWearerAttacked off made a puddle.");
        }
    }
}
