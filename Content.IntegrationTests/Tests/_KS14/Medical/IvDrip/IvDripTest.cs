using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Pair;
using Content.Shared._KS14.Medical.IvDrip;
using Content.Shared.Actions;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.FixedPoint;
using Content.Shared.Inventory;
using NUnit.Framework;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._KS14.Medical.IvDrip;

/// <summary>
///     Behaviour of worn IV drips: when they inject, how much, and whether the hotbar action agrees with
///         what the pump is actually doing.
/// </summary>
/// <remarks>
///     Runs on prototypes defined here rather than on the shipped drip, so retuning a real drip cannot
///         quietly change what these tests mean. The wearer is a bare entity with a human inventory
///         template and an injectable solution - a full human mob would drag in a bloodstream that
///         metabolises the injected reagent mid-test.
/// </remarks>
[TestFixture]
[TestOf(typeof(SharedIvDripSystem))]
public sealed class IvDripTest : GameTest
{
    // Server-only. The test wearer carries a human inventory template without a sprite, which the
    // client's clothing system logs an error over, and nothing here needs a client.
    public override PoolSettings PoolSettings => PsDisconnected;

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: BaseItem
  id: KsTestIvDrip
  name: test iv drip
  components:
  - type: Clothing
    slots:
    - neck
  - type: IvDrip
    injectionAmount: 5
    injectionInterval: 1
    spillOnWearerAttacked: true
    spillAmount: 5
  - type: SolutionManager
    solutions: null
    solutionData:
      ivDrip:
        maxVol: 100

# Cannot be reconfigured through its window, so the clamping path can be tested against a refusal.
- type: entity
  parent: KsTestIvDrip
  id: KsTestIvDripFixed
  components:
  - type: IvDrip
    canSetInjectionAmount: false
    canSetInjectionInterval: false

- type: entity
  id: KsTestIvDripWearer
  name: test iv drip wearer
  components:
  - type: Inventory
    templateId: human
  - type: ContainerContainer
  - type: SolutionManager
    solutions: null
    solutionData:
      testBlood:
        maxVol: 100
  - type: InjectableSolution
    solution: testBlood
";

    private const string DripProto = "KsTestIvDrip";
    private const string FixedDripProto = "KsTestIvDripFixed";
    private const string WearerProto = "KsTestIvDripWearer";

    private const string NeckSlot = "neck";
    private const string DripSolution = "ivDrip";
    private const string WearerSolution = "testBlood";
    private const string TestReagent = "Water";

    /// <summary>One second of simulation, which every interval here is a whole multiple of.</summary>
    private const int TicksPerSecond = 30;

    /// <summary>
    ///     Spawns a wearer with a filled drip already equipped.
    /// </summary>
    private async Task<(EntityUid WearerUid, EntityUid DripUid)> SetUpWornDrip(string dripProto = DripProto)
    {
        var server = Pair.Server;
        var entityManager = server.EntMan;

        var wearerUid = EntityUid.Invalid;
        var dripUid = EntityUid.Invalid;

        await server.WaitAssertion(() =>
        {
            var inventorySystem = entityManager.System<InventorySystem>();
            var solutionContainerSystem = entityManager.System<SharedSolutionContainerSystem>();

            wearerUid = entityManager.Spawn(WearerProto);
            dripUid = entityManager.Spawn(dripProto);

            Assert.That(solutionContainerSystem.TryGetSolution(dripUid, DripSolution, out var dripSolutionEntity, out _),
                "Test drip has no drip solution.");
            solutionContainerSystem.TryAddReagent(dripSolutionEntity!.Value, TestReagent, FixedPoint2.New(100), out _);

            Assert.That(inventorySystem.TryEquip(wearerUid, dripUid, NeckSlot, silent: true, force: true),
                "Could not equip the test drip onto the test wearer.");
        });

        return (wearerUid, dripUid);
    }

    /// <summary>
    ///     How much of the test reagent has made it into the wearer.
    /// </summary>
    private FixedPoint2 GetWearerVolume(EntityUid wearerUid)
    {
        var solutionContainerSystem = Pair.Server.EntMan.System<SharedSolutionContainerSystem>();
        return solutionContainerSystem.TryGetSolution(wearerUid, WearerSolution, out _, out var solution)
            ? solution.Volume
            : FixedPoint2.Zero;
    }

    [Test]
    public async Task TestDisabledIvDripDoesNotInject()
    {
        var server = Pair.Server;
        var (wearerUid, _) = await SetUpWornDrip();

        await server.WaitRunTicks(TicksPerSecond * 2);

        await server.WaitAssertion(() =>
        {
            Assert.That(GetWearerVolume(wearerUid), Is.EqualTo(FixedPoint2.Zero),
                "A drip that was never switched on injected anyway.");
        });
    }

    [Test]
    public async Task TestEnabledIvDripInjectsItsWearer()
    {
        var server = Pair.Server;
        var entityManager = server.EntMan;
        var (wearerUid, dripUid) = await SetUpWornDrip();

        await server.WaitAssertion(() =>
        {
            var ivDripSystem = entityManager.System<SharedIvDripSystem>();
            ivDripSystem.SetInjectionEnabled((dripUid, entityManager.GetComponent<IvDripComponent>(dripUid)), true);
        });

        await server.WaitRunTicks(TicksPerSecond + TicksPerSecond / 2);

        await server.WaitAssertion(() =>
        {
            Assert.That(GetWearerVolume(wearerUid), Is.EqualTo(FixedPoint2.New(5)),
                "One interval of a 5u drip did not move exactly one 5u dose into the wearer.");
        });
    }

    /// <summary>
    ///     The window and the hotbar action must not be able to disagree about whether the pump is running.
    /// </summary>
    /// <remarks>
    ///     Regression test: enabling through the window used to write the field directly and skip
    ///         <c>SetToggled</c>, leaving the action button un-toggled while injection ran.
    /// </remarks>
    [Test]
    public async Task TestEnablingIvDripTogglesItsAction()
    {
        var server = Pair.Server;
        var entityManager = server.EntMan;
        var (_, dripUid) = await SetUpWornDrip();

        await server.WaitAssertion(() =>
        {
            var actionsSystem = entityManager.System<SharedActionsSystem>();
            var ivDripSystem = entityManager.System<SharedIvDripSystem>();
            var ivDripComponent = entityManager.GetComponent<IvDripComponent>(dripUid);

            Assert.That(ivDripComponent.ToggleActionEntity, Is.Not.Null,
                "Equipping the drip did not grant its toggle action.");

            ivDripSystem.SetInjectionEnabled((dripUid, ivDripComponent), true);

            var actionEntity = actionsSystem.GetAction(ivDripComponent.ToggleActionEntity);
            Assert.That(actionEntity, Is.Not.Null, "The granted toggle action could not be resolved.");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(ivDripComponent.InjectionEnabled, "Injection did not start.");
                Assert.That(actionEntity!.Value.Comp.Toggled,
                    "Injection started but the hotbar action stayed un-toggled.");
            }

            ivDripSystem.SetInjectionEnabled((dripUid, ivDripComponent), false);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(ivDripComponent.InjectionEnabled, Is.False, "Injection did not stop.");
                Assert.That(actionsSystem.GetAction(ivDripComponent.ToggleActionEntity)!.Value.Comp.Toggled, Is.False,
                    "Injection stopped but the hotbar action stayed toggled.");
            }
        });
    }

    [Test]
    public async Task TestUnequippingIvDripStopsInjectionAndTakesTheAction()
    {
        var server = Pair.Server;
        var entityManager = server.EntMan;
        var (wearerUid, dripUid) = await SetUpWornDrip();

        await server.WaitAssertion(() =>
        {
            var inventorySystem = entityManager.System<InventorySystem>();
            var ivDripSystem = entityManager.System<SharedIvDripSystem>();
            var ivDripComponent = entityManager.GetComponent<IvDripComponent>(dripUid);

            ivDripSystem.SetInjectionEnabled((dripUid, ivDripComponent), true);
            Assert.That(inventorySystem.TryUnequip(wearerUid, NeckSlot, silent: true, force: true),
                "Could not unequip the test drip.");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(ivDripComponent.WearerUid, Is.Null, "The drip still thinks it is worn.");
                Assert.That(ivDripComponent.ToggleActionEntity, Is.Null,
                    "The toggle action was not taken back on unequip.");
            }
        });

        var volumeAtUnequip = FixedPoint2.Zero;
        await server.WaitAssertion(() => volumeAtUnequip = GetWearerVolume(wearerUid));

        await server.WaitRunTicks(TicksPerSecond * 2);

        await server.WaitAssertion(() =>
        {
            Assert.That(GetWearerVolume(wearerUid), Is.EqualTo(volumeAtUnequip),
                "An unworn drip kept injecting its former wearer.");
        });
    }

    [Test]
    public async Task TestIvDripDoesNotInjectPastItsContents()
    {
        var server = Pair.Server;
        var entityManager = server.EntMan;
        var (wearerUid, dripUid) = await SetUpWornDrip();

        await server.WaitAssertion(() =>
        {
            var ivDripSystem = entityManager.System<SharedIvDripSystem>();
            var solutionContainerSystem = entityManager.System<SharedSolutionContainerSystem>();
            var ivDripComponent = entityManager.GetComponent<IvDripComponent>(dripUid);

            // Leave less in the drip than one dose.
            Assert.That(solutionContainerSystem.TryGetSolution(dripUid, DripSolution, out var dripSolutionEntity, out _));
            solutionContainerSystem.SplitSolution(dripSolutionEntity!.Value, FixedPoint2.New(98));

            ivDripSystem.SetInjectionEnabled((dripUid, ivDripComponent), true);
        });

        await server.WaitRunTicks(TicksPerSecond * 3);

        await server.WaitAssertion(() =>
        {
            Assert.That(GetWearerVolume(wearerUid), Is.EqualTo(FixedPoint2.New(2)),
                "The drip injected more than it had left.");
        });
    }

    [Test]
    public async Task TestIvDripClampsConfigurationFromItsWindow()
    {
        var server = Pair.Server;
        var entityManager = server.EntMan;
        var (_, dripUid) = await SetUpWornDrip();

        await server.WaitAssertion(() =>
        {
            var ivDripSystem = entityManager.System<SharedIvDripSystem>();
            var ivDripComponent = entityManager.GetComponent<IvDripComponent>(dripUid);

            ivDripSystem.SetInjectionAmount((dripUid, ivDripComponent), FixedPoint2.New(9999));
            ivDripSystem.SetInjectionInterval((dripUid, ivDripComponent), 9999f);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(ivDripComponent.InjectionAmount, Is.EqualTo(ivDripComponent.MaximumInjectionAmount),
                    "An out-of-range dose was not clamped to the maximum.");
                Assert.That(ivDripComponent.InjectionInterval, Is.EqualTo(ivDripComponent.MaximumInjectionInterval),
                    "An out-of-range interval was not clamped to the maximum.");
            }
        });
    }

    [Test]
    public async Task TestFixedIvDripRefusesConfiguration()
    {
        var server = Pair.Server;
        var entityManager = server.EntMan;
        var (_, dripUid) = await SetUpWornDrip(FixedDripProto);

        await server.WaitAssertion(() =>
        {
            var ivDripSystem = entityManager.System<SharedIvDripSystem>();
            var ivDripComponent = entityManager.GetComponent<IvDripComponent>(dripUid);
            var originalAmount = ivDripComponent.InjectionAmount;
            var originalInterval = ivDripComponent.InjectionInterval;

            ivDripSystem.SetInjectionAmount((dripUid, ivDripComponent), FixedPoint2.New(1));
            ivDripSystem.SetInjectionInterval((dripUid, ivDripComponent), 2f);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(ivDripComponent.InjectionAmount, Is.EqualTo(originalAmount),
                    "A drip that cannot be reconfigured accepted a new dose.");
                Assert.That(ivDripComponent.InjectionInterval, Is.EqualTo(originalInterval),
                    "A drip that cannot be reconfigured accepted a new interval.");
            }
        });
    }
}
