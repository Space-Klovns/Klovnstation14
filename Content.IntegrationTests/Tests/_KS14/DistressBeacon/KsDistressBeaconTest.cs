using System;
using System.Threading.Tasks;
using Content.IntegrationTests.Tests.Interaction;
using Content.Server._KS14.FieldCommander;
using Content.Shared._KS14.DistressBeacon;
using Content.Shared.Clothing;
using Content.Shared.Clothing.Components;
using Content.Shared.Inventory;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.DistressBeacon;

/// <summary>
///     A beacon worn on the neck follows its wearer: critical, dead or neither, as they change state and as the beacon
///         is put on and taken off, with its light and its worn sprite in step.
/// </summary>
[TestFixture]
[TestOf(typeof(KsDistressBeaconSystem))]
public sealed class KsDistressBeaconTest : InteractionTest
{
    protected override string PlayerPrototype => "MobHuman"; // an inventory with a neck slot, and a mob state

    private const string NeckSlot = "neck";

    private readonly EntProtoId _beacon = "KsDistressBeacon";

    private EntityUid SpawnAndWear()
    {
        var beaconUid = SEntMan.SpawnEntity(_beacon, SEntMan.GetCoordinates(PlayerCoords));
        Assert.That(SEntMan.System<InventorySystem>().TryEquip(SPlayer, beaconUid, NeckSlot, silent: true, force: true), Is.True);
        return beaconUid;
    }

    private KsDistressBeaconState State(EntityUid beaconUid)
        => SEntMan.GetComponent<KsDistressBeaconComponent>(beaconUid).State;

    private bool LightOn(EntityUid beaconUid)
        => SEntMan.System<SharedPointLightSystem>().TryGetLight(beaconUid, out var lightComponent) && lightComponent.Enabled;

    private void SetMobState(MobState mobState)
        => SEntMan.System<MobStateSystem>().ChangeMobState(SPlayer, mobState);

    [Test]
    public async Task BeaconFollowsItsWearer()
    {
        EntityUid beaconUid = default;
        await Server.WaitPost(() => beaconUid = SpawnAndWear());

        await Server.WaitAssertion(() =>
        {
            Assert.That(State(beaconUid), Is.EqualTo(KsDistressBeaconState.Off), "A beacon on a healthy wearer was on.");
            Assert.That(LightOn(beaconUid), Is.False);
        });

        await Server.WaitPost(() => SetMobState(MobState.Critical));
        await Server.WaitAssertion(() =>
        {
            Assert.That(State(beaconUid), Is.EqualTo(KsDistressBeaconState.Critical), "A critical wearer didn't turn the beacon to critical.");
            Assert.That(LightOn(beaconUid), Is.True, "The light wasn't on for a critical wearer.");
            Assert.That(SEntMan.GetComponent<ClothingComponent>(beaconUid).EquippedPrefix, Is.Not.Null, "The worn sprite wasn't lit.");
        });

        await Server.WaitPost(() => SetMobState(MobState.Alive));
        await Server.WaitAssertion(() =>
        {
            Assert.That(State(beaconUid), Is.EqualTo(KsDistressBeaconState.Off), "A revived wearer didn't turn the beacon off.");
            Assert.That(LightOn(beaconUid), Is.False);
            Assert.That(SEntMan.GetComponent<ClothingComponent>(beaconUid).EquippedPrefix, Is.Null);
        });

        await Server.WaitPost(() => SetMobState(MobState.Dead));
        await Server.WaitAssertion(() =>
        {
            Assert.That(State(beaconUid), Is.EqualTo(KsDistressBeaconState.Dead), "A dead wearer didn't turn the beacon to dead.");
            Assert.That(LightOn(beaconUid), Is.True);
        });

        // The client's copy has the state, and so lights its icon.
        await RunTicks(5);
        await Client.WaitAssertion(() =>
        {
            var clientBeaconUid = ToClient(beaconUid);
            Assert.That(CEntMan.GetComponent<KsDistressBeaconComponent>(clientBeaconUid).State, Is.EqualTo(KsDistressBeaconState.Dead));
        });

        await Server.WaitPost(() => SEntMan.System<InventorySystem>().TryUnequip(SPlayer, NeckSlot, force: true));
        await Server.WaitAssertion(() =>
        {
            Assert.That(State(beaconUid), Is.EqualTo(KsDistressBeaconState.Off), "Taking the beacon off didn't turn it off.");
            Assert.That(LightOn(beaconUid), Is.False);
        });
    }

    [Test]
    public async Task BeaconPutOnSomeoneAlreadyDownIsOn()
    {
        await Server.WaitPost(() => SetMobState(MobState.Critical));

        EntityUid beaconUid = default;
        await Server.WaitPost(() => beaconUid = SpawnAndWear());

        await Server.WaitAssertion(() =>
        {
            Assert.That(State(beaconUid), Is.EqualTo(KsDistressBeaconState.Critical), "A beacon put on a critical wearer stayed off.");
        });
    }

    [Test]
    public async Task BeaconKeepsBeepingWhileDown()
    {
        EntityUid beaconUid = default;
        await Server.WaitPost(() => beaconUid = SpawnAndWear());
        await Server.WaitPost(() => SetMobState(MobState.Critical));
        await RunTicks(1); // the player is healthy, so any longer and the mob state would right itself

        // It beeped as soon as its wearer went down, and booked the next one a critical interval on.
        await Server.WaitAssertion(() =>
        {
            var beaconComponent = SEntMan.GetComponent<KsDistressBeaconComponent>(beaconUid);
            var curTime = Server.Timing.CurTime;
            Assert.That(State(beaconUid), Is.EqualTo(KsDistressBeaconState.Critical));
            Assert.That(beaconComponent.NextBeep, Is.GreaterThan(curTime), "The beacon never beeped.");
            Assert.That(beaconComponent.NextBeep, Is.LessThanOrEqualTo(curTime + beaconComponent.CritBeepInterval));
        });
    }

    [Test]
    public async Task GivingABeaconFillsAnEmptyNeckOnly()
    {
        var beaconSystem = SEntMan.System<KsFieldCommanderBeaconSystem>();
        var inventorySystem = SEntMan.System<InventorySystem>();

        await Server.WaitAssertion(() =>
        {
            Assert.That(beaconSystem.GiveBeacon(SPlayer), Is.True, "No beacon went on an empty neck.");
            Assert.That(inventorySystem.TryGetSlotEntity(SPlayer, NeckSlot, out var neckUid), Is.True);
            Assert.That(SEntMan.HasComponent<KsDistressBeaconComponent>(neckUid), Is.True);

            Assert.That(beaconSystem.GiveBeacon(SPlayer), Is.False, "A second beacon went on an occupied neck.");
        });
    }
}
