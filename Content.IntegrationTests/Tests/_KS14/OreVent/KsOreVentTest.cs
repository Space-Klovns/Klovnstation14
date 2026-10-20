#nullable enable
using System.Numerics;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Shared._KS14.OreVent;
using Content.Shared._KS14.OreVent.Drone;
using Content.Shared._KS14.OreWell;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Spawners;
using Robust.Shared.Timing;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.OreVent;

/// <summary>
///     Ore vents: the tapping process (start, success, the two ways to fail), the scanner interaction that starts it, and
///         the drone that watches over it.
/// </summary>
[TestFixture]
[TestOf(typeof(OreVentSystem))]
public sealed class KsOreVentTest : GameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    private readonly EntProtoId _vent = "KsTestOreVent";
    private readonly EntProtoId _ventWithDrone = "KsTestOreVentWithDrone";
    private readonly EntProtoId _undiscoveredVent = "KsTestOreVentUndiscovered";
    private readonly EntProtoId _scanner = "KsTestOreVentScanner";
    private readonly EntProtoId _notAScanner = "KsTestOreVentNotAScanner";
    private static readonly EntProtoId Human = "MobHuman";
    private static readonly EntProtoId Wall = "WallSolid";

    [TestPrototypes]
    private const string Prototypes = @"
- type: oreWellSetting
  id: KsTestOreVentWell
  totalResourceRateRange: 0.5,0.5
  resourceCountRange: 1,1
  possibleResourceTypes:
  - SteelOre

# The process entity that exists while a vent is being tapped.
- type: entity
  id: KsTestOreVentProcess

- type: entity
  id: KsTestOreVentDrone
  components:
  - type: Appearance
  - type: OreVentDrone

- type: entity
  id: KsTestOreVent
  components:
  - type: Transform
    anchored: true
  - type: Appearance
  - type: DoAfter
  - type: OreVent
    clearingDuration: 0.3
    clearingIterations: 3
    clearRadius: 3
    extractionDuration: 1
    tappingProcessEntityProto: KsTestOreVentProcess
    oreWellSettingId: KsTestOreVentWell
  - type: Explosive
    explosionType: OreVentClearing
    maxIntensity: 100
    totalIntensity: 80
    intensitySlope: 5
    canCreateVacuum: false
    repeatable: true
    deleteAfterExplosion: false

- type: entity
  parent: KsTestOreVent
  id: KsTestOreVentWithDrone
  components:
  - type: OreVent
    droneEntityProto: KsTestOreVentDrone

# Not scanned yet, so it cannot be tapped yet.
- type: entity
  parent: KsTestOreVent
  id: KsTestOreVentUndiscovered
  components:
  - type: KsScanDiscoverable
    trueName: discovered test vent

- type: entity
  id: KsTestOreVentScanner
  components:
  - type: Item
  - type: KsDiscoveringScanner

- type: entity
  id: KsTestOreVentNotAScanner
  components:
  - type: Item
";

    private EntityCoordinates _ventCoordinates;
    private EntityUid _grid;

    private async Task SetUpMapAsync()
    {
        var testMap = await Pair.CreateTestMap();
        _grid = testMap.Grid;
        _ventCoordinates = new EntityCoordinates(_grid, new Vector2(0.5f, 0.5f));
    }

    private async Task<EntityUid> SpawnVentAsync(EntProtoId prototype)
    {
        EntityUid vent = default;
        await Server.WaitPost(() => vent = SEntMan.SpawnEntity(prototype, _ventCoordinates));
        return vent;
    }

    private async Task RunSecondsAsync(float seconds)
    {
        var tickRate = (float)Server.ResolveDependency<IGameTiming>().TickRate;
        await Pair.RunTicksSync((int)(tickRate * seconds));
    }

    private OreVentComponent Component(EntityUid vent) => SEntMan.GetComponent<OreVentComponent>(vent);

    private async Task StartTappingAsync(EntityUid vent)
    {
        await Server.WaitPost(() => SEntMan.System<OreVentSystem>().StartTapping(vent));
        await Pair.RunTicksSync(2);
    }

    /// <summary>
    ///     A human holding <paramref name="itemPrototype"/>, standing next to the vent. The blasts do structural
    ///         damage, which a human does not take.
    /// </summary>
    private async Task<(EntityUid User, EntityUid Item)> SpawnUserHoldingAsync(EntProtoId itemPrototype)
    {
        EntityUid user = default;
        EntityUid item = default;
        await Server.WaitPost(() =>
        {
            user = SEntMan.SpawnEntity(Human, new EntityCoordinates(_grid, new Vector2(0.5f, 1.5f)));
            item = SEntMan.SpawnEntity(itemPrototype, new EntityCoordinates(_grid, new Vector2(0.5f, 1.5f)));
            Assert.That(SEntMan.System<SharedHandsSystem>().TryPickupAnyHand(user, item), Is.True, "precondition: the user holds the item");
        });

        return (user, item);
    }

    /// <returns>Whether the interaction was handled, i.e. whether the vent took it.</returns>
    private async Task<bool> UseOnVentAsync(EntityUid user, EntityUid item, EntityUid vent)
    {
        var handled = false;
        await Server.WaitPost(() => handled = SEntMan.System<SharedInteractionSystem>().InteractUsing(user, item, vent, new EntityCoordinates(_grid, new Vector2(0.5f, 0.5f)), checkCanInteract: false, checkCanUse: false));
        await Pair.RunTicksSync(2);
        return handled;
    }

    [Test]
    public async Task StartingTheTappingMarksTheVentAndSpawnsItsProcessEntity()
    {
        await SetUpMapAsync();
        var vent = await SpawnVentAsync(_vent);

        await StartTappingAsync(vent);

        await Server.WaitAssertion(() =>
        {
            var component = Component(vent);
            Assert.That(component.BeingTapped, Is.True);
            Assert.That(component.Tapped, Is.False, "tapping has only just started");
            Assert.That(component.TappingProcessEntityUid, Is.Not.Null);
            Assert.That(SEntMan.EntityExists(component.TappingProcessEntityUid), Is.True);
            Assert.That(SEntMan.HasComponent<ActiveOreWellComponent>(vent), Is.False, "no ore well until it succeeds");
        });
    }

    [Test]
    public async Task TappingThatRunsItsCourseSucceedsAndCreatesAnOreWell()
    {
        await SetUpMapAsync();
        var vent = await SpawnVentAsync(_vent);
        await StartTappingAsync(vent);

        EntityUid process = default;
        await Server.WaitPost(() => process = Component(vent).TappingProcessEntityUid!.Value);

        // Not done yet: the extraction takes a second.
        await RunSecondsAsync(0.5f);
        await Server.WaitAssertion(() =>
        {
            Assert.That(Component(vent).Tapped, Is.False, "halfway through, it must not be tapped");
            Assert.That(Component(vent).BeingTapped, Is.True);
        });

        await RunSecondsAsync(1f);
        await Server.WaitAssertion(() =>
        {
            var component = Component(vent);
            Assert.That(component.Tapped, Is.True);
            Assert.That(component.BeingTapped, Is.False);
            Assert.That(component.TappingProcessEntityUid, Is.Null);
            Assert.That(SEntMan.Deleted(process), Is.True, "the process entity goes away once tapping is over");

            Assert.That(SEntMan.TryGetComponent<ActiveOreWellComponent>(vent, out var well), Is.True, "success makes an ore well");
            Assert.That(well!.ResourceTypes, Is.Not.Empty);
            Assert.That(well.SettingId.Id, Is.EqualTo("KsTestOreVentWell"), "and from the vent's own setting");
        });
    }

    [Test]
    public async Task CancellingTheTappingLeavesTheVentUntappedWithNoOreWell()
    {
        await SetUpMapAsync();
        var vent = await SpawnVentAsync(_vent);
        await StartTappingAsync(vent);

        EntityUid process = default;
        await Server.WaitPost(() => process = Component(vent).TappingProcessEntityUid!.Value);

        await Server.WaitPost(() => SEntMan.System<OreVentSystem>().CancelTapping(vent));

        // Run past the time the extraction would have finished, as a do-after that kept going would succeed then.
        await RunSecondsAsync(2f);

        await Server.WaitAssertion(() =>
        {
            var component = Component(vent);
            Assert.That(component.BeingTapped, Is.False);
            Assert.That(component.Tapped, Is.False, "cancelled, so it must never become tapped");
            Assert.That(SEntMan.HasComponent<ActiveOreWellComponent>(vent), Is.False);
            Assert.That(SEntMan.Deleted(process), Is.True);
        });
    }

    [Test]
    public async Task ADroneArrivesAndLeavesWhenTappingSucceeds()
    {
        await SetUpMapAsync();
        var vent = await SpawnVentAsync(_ventWithDrone);
        await StartTappingAsync(vent);

        EntityUid drone = default;
        await Server.WaitAssertion(() =>
        {
            Assert.That(Component(vent).DroneEntityUid, Is.Not.Null, "tapping brings a drone");
            drone = Component(vent).DroneEntityUid!.Value;
            Assert.That(SEntMan.GetComponent<OreVentDroneComponent>(drone).VentUid, Is.EqualTo(vent));
        });

        await RunSecondsAsync(1.5f);

        await Server.WaitAssertion(() =>
        {
            Assert.That(Component(vent).Tapped, Is.True);
            Assert.That(Component(vent).DroneEntityUid, Is.Null);
            Assert.That(SEntMan.GetComponent<OreVentDroneComponent>(drone).VentUid, Is.Null, "the drone lets go of the vent");
            Assert.That(SEntMan.HasComponent<TimedDespawnComponent>(drone), Is.True, "and is on its way out");
        });
    }

    [Test]
    public async Task DestroyingTheDroneMidTappingFailsTheTapping()
    {
        await SetUpMapAsync();
        var vent = await SpawnVentAsync(_ventWithDrone);
        await StartTappingAsync(vent);

        EntityUid process = default;
        await Server.WaitPost(() =>
        {
            process = Component(vent).TappingProcessEntityUid!.Value;
            SEntMan.DeleteEntity(Component(vent).DroneEntityUid!.Value);
        });

        await RunSecondsAsync(2f);

        await Server.WaitAssertion(() =>
        {
            var component = Component(vent);
            Assert.That(component.BeingTapped, Is.False);
            Assert.That(component.Tapped, Is.False, "without the drone the tapping fails");
            Assert.That(SEntMan.HasComponent<ActiveOreWellComponent>(vent), Is.False);
            Assert.That(SEntMan.Deleted(process), Is.True);
        });
    }

    [Test]
    public async Task ScanningADiscoveredVentClearsTheAreaThenTapsIt()
    {
        await SetUpMapAsync();
        var vent = await SpawnVentAsync(_vent);
        var (user, scanner) = await SpawnUserHoldingAsync(_scanner);

        EntityUid wall = default;
        await Server.WaitPost(() => wall = SEntMan.SpawnEntity(Wall, new EntityCoordinates(_grid, new Vector2(1.5f, 0.5f))));

        await UseOnVentAsync(user, scanner, vent);

        // The do-after takes 0.3 seconds and then the three iterations of the clearing run for 0.2 more.
        await RunSecondsAsync(0.35f);
        await Server.WaitAssertion(() => Assert.That(Component(vent).DoingClearing, Is.True, "precondition: clearing started"));

        await RunSecondsAsync(1f);
        await Server.WaitAssertion(() =>
        {
            var component = Component(vent);
            Assert.That(component.DoingClearing, Is.False, "clearing is over");
            Assert.That(component.BeingTapped || component.Tapped, Is.True, "and the tapping followed it");
            Assert.That(SEntMan.Deleted(wall), Is.True, "the clearing blasts the structures around the vent");
        });

        await RunSecondsAsync(1.5f);
        await Server.WaitAssertion(() =>
        {
            Assert.That(Component(vent).Tapped, Is.True);
            Assert.That(SEntMan.HasComponent<ActiveOreWellComponent>(vent), Is.True);
        });
    }

    [Test]
    public async Task ScanningFromFarAwayStillClearsOnlyWhatIsNearTheVent()
    {
        await SetUpMapAsync();
        var vent = await SpawnVentAsync(_vent);
        var (user, scanner) = await SpawnUserHoldingAsync(_scanner);

        // Well outside the clear radius of 3.
        EntityUid farWall = default;
        await Server.WaitPost(() => farWall = SEntMan.SpawnEntity(Wall, new EntityCoordinates(_grid, new Vector2(9.5f, 0.5f))));

        await UseOnVentAsync(user, scanner, vent);
        await RunSecondsAsync(2f);

        await Server.WaitAssertion(() =>
        {
            Assert.That(Component(vent).BeingTapped || Component(vent).Tapped, Is.True, "precondition: the clearing happened");
            Assert.That(SEntMan.Deleted(farWall), Is.False, "the control: a wall beyond the radius survives");
        });
    }

    [Test]
    public async Task ScanningWithSomethingThatIsNotAScannerDoesNothing()
    {
        await SetUpMapAsync();
        var vent = await SpawnVentAsync(_vent);
        var (user, notScanner) = await SpawnUserHoldingAsync(_notAScanner);

        await UseOnVentAsync(user, notScanner, vent);
        await RunSecondsAsync(1.5f);

        await Server.WaitAssertion(() =>
        {
            var component = Component(vent);
            Assert.That(component.DoingClearing, Is.False);
            Assert.That(component.BeingTapped, Is.False);
            Assert.That(component.Tapped, Is.False);
        });
    }

    [Test]
    public async Task AnUndiscoveredVentCannotBeTappedUntilItIsScannedOnce()
    {
        await SetUpMapAsync();
        var vent = await SpawnVentAsync(_undiscoveredVent);
        var (user, scanner) = await SpawnUserHoldingAsync(_scanner);

        // The first scan only discovers it.
        await UseOnVentAsync(user, scanner, vent);
        await RunSecondsAsync(1.5f);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<Content.Shared._KS14.ScanDiscoverable.Base.KsScanDiscoverableComponent>(vent), Is.False, "precondition: now discovered");
            Assert.That(Component(vent).DoingClearing || Component(vent).BeingTapped || Component(vent).Tapped, Is.False, "discovering is not tapping");
        });

        // The second one starts the work.
        await UseOnVentAsync(user, scanner, vent);
        await RunSecondsAsync(0.5f);
        await Server.WaitAssertion(() => Assert.That(Component(vent).DoingClearing, Is.True));
    }

    [Test]
    public async Task AnAlreadyTappedVentIsNotTappedAgain()
    {
        await SetUpMapAsync();
        var vent = await SpawnVentAsync(_vent);
        await StartTappingAsync(vent);
        await RunSecondsAsync(1.5f);
        await Server.WaitAssertion(() => Assert.That(Component(vent).Tapped, Is.True, "precondition"));

        var (user, scanner) = await SpawnUserHoldingAsync(_scanner);
        var handled = await UseOnVentAsync(user, scanner, vent);
        await RunSecondsAsync(1f);

        await Server.WaitAssertion(() =>
        {
            Assert.That(handled, Is.False, "the vent does not take the interaction");
            Assert.That(Component(vent).DoingClearing, Is.False, "no second clearing");
            Assert.That(Component(vent).BeingTapped, Is.False, "no second tapping");
        });
    }

    [Test]
    public async Task AVentThatIsBeingTappedRefusesAnotherScan()
    {
        await SetUpMapAsync();
        var vent = await SpawnVentAsync(_vent);
        await StartTappingAsync(vent);

        var (user, scanner) = await SpawnUserHoldingAsync(_scanner);
        var handled = await UseOnVentAsync(user, scanner, vent);
        await RunSecondsAsync(0.5f);

        await Server.WaitAssertion(() =>
        {
            Assert.That(handled, Is.False, "the vent does not take the interaction");
            Assert.That(Component(vent).DoingClearing, Is.False, "a vent that is already being tapped does not start clearing");
            Assert.That(Component(vent).BeingTapped, Is.True);
        });
    }
}
