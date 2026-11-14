using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Content.Server._KS14.Despawn;
using Content.Server.GameTicking;
using Content.Shared._KS14.Dissolve;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.IntegrationTests.Tests.Interaction;
using Content.Shared.Weapons.Ranged.Components;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.Despawn;

/// <summary>
///     While a litter rule is active, spent casings, loose guns and dead Field Commander units dissolve away after their
///         delay, unless the gun is picked up or lying on a table, and player bodies stay.
/// </summary>
[TestFixture]
[TestOf(typeof(KsLitterDespawnSystem))]
public sealed class KsLitterDespawnTest : InteractionTest
{
    protected override string PlayerPrototype => "MobHuman"; // hands, and a mob state

    // Ids that exist only in the test prototypes below. Instance fields: the YAML linter checks only static ones.
    private readonly EntProtoId _rule = "KsTestLitterRule";
    private readonly EntProtoId _gun = "KsTestLitterGun";
    private readonly EntProtoId _ballisticGun = "KsTestLitterBallisticGun";
    private readonly EntProtoId _table = "KsTestLitterTable";
    private readonly EntProtoId _unit = "KsTestLitterUnit";

    // Short, so a test waits seconds rather than minutes. The rule's own defaults are what the real prototype sets.
    private const float CasingDelay = 2f;
    private const float ItemDelay = 3f;
    private const float BodyDelay = 3f;

    /// <summary>
    ///     The dissolve (0.2 s here) and the linger after it.
    /// </summary>
    private const float DissolveSlack = 1.5f;

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: BaseGameRule
  id: KsTestLitterRule
  components:
  - type: KsLitterDespawnRule
    casingDelay: 2
    itemDelay: 3
    bodyDelay: 3
    dissolveDuration: 0.2

- type: entity
  parent: BaseItem
  id: KsTestLitterGun
  components:
  - type: Gun

- type: entity
  parent: BaseItem
  id: KsTestLitterBallisticGun
  components:
  - type: Gun
    fireRate: 100
  - type: BallisticAmmoProvider
    proto: CartridgePistol
    capacity: 5
    unspawnedCount: 5

- type: entity
  id: KsTestLitterTable
  components:
  - type: Transform
    anchored: true
  - type: PlaceableSurface

- type: entity
  id: KsTestLitterUnit
  components:
  - type: MobState
  - type: KsFieldCommanderUnit
";

    private void StartRule()
    {
        Assert.That(SEntMan.System<GameTicker>().StartGameRule(_rule, out _), Is.True);
    }

    private bool Gone(EntityUid uid)
        => SEntMan.Deleted(uid);

    [Test]
    public async Task DroppedGunDissolves()
    {
        await Server.WaitPost(StartRule);
        var gunUid = SEntMan.GetEntity(await PlaceInHands(_gun));
        await RunSeconds(1f); // the pickup cooldown
        await Drop();

        await RunSeconds(ItemDelay / 2f);
        Assert.That(Gone(gunUid), Is.False, "The gun dissolved before its delay.");
        await RunSeconds(ItemDelay / 2f + DissolveSlack);
        Assert.That(Gone(gunUid), Is.True, "A gun left on the floor never dissolved.");
    }

    [Test]
    public async Task GunDoesNotDissolveWithoutTheRule()
    {
        var gunUid = SEntMan.GetEntity(await PlaceInHands(_gun));
        await RunSeconds(1f);
        await Drop();

        await RunSeconds(ItemDelay + DissolveSlack);
        Assert.That(Gone(gunUid), Is.False, "A gun dissolved with no litter rule active.");
    }

    [Test]
    public async Task GunOnATableStays()
    {
        await Server.WaitPost(StartRule);
        await Server.WaitPost(() => SEntMan.SpawnEntity(_table, SEntMan.GetCoordinates(PlayerCoords)));
        var gunUid = SEntMan.GetEntity(await PlaceInHands(_gun));
        await RunSeconds(1f);
        await Drop();

        await RunSeconds(ItemDelay + DissolveSlack);
        Assert.That(Gone(gunUid), Is.False, "A gun lying on a table dissolved.");
        Assert.That(SEntMan.HasComponent<KsDissolveComponent>(gunUid), Is.False);
    }

    [Test]
    public async Task GunPickedUpBeforeItsDelayStays()
    {
        await Server.WaitPost(StartRule);
        var gunNetUid = await PlaceInHands(_gun);
        var gunUid = SEntMan.GetEntity(gunNetUid);
        await RunSeconds(1f);
        await Drop();

        await RunSeconds(ItemDelay / 2f);
        await Pickup(gunNetUid, deleteHeld: false);

        await RunSeconds(ItemDelay + DissolveSlack);
        Assert.That(Gone(gunUid), Is.False, "A gun in someone's hand dissolved.");
    }

    [Test]
    public async Task SpentCasingDissolves()
    {
        await Server.WaitPost(StartRule);
        var gunUid = SEntMan.GetEntity(await PlaceInHands(_ballisticGun));
        await RunSeconds(1f);
        await AttemptShoot(TargetCoords);

        var casings = new List<EntityUid>();
        await Server.WaitPost(() =>
        {
            var query = SEntMan.AllEntityQueryEnumerator<CartridgeAmmoComponent>();
            while (query.MoveNext(out var cartridgeUid, out var cartridgeComponent))
            {
                if (cartridgeComponent.Spent)
                    casings.Add(cartridgeUid);
            }
        });
        Assert.That(casings, Is.Not.Empty, "The shot left no spent casing to watch.");

        await RunSeconds(CasingDelay + DissolveSlack);
        Assert.That(casings.All(Gone), Is.True, "A spent casing never dissolved.");
        Assert.That(Gone(gunUid), Is.False, "The gun in the player's hand dissolved with the casing.");
    }

    [Test]
    public async Task DeadUnitDissolvesAndRevivedOneStays()
    {
        await Server.WaitPost(StartRule);
        EntityUid deadUid = default;
        EntityUid revivedUid = default;
        await Server.WaitPost(() =>
        {
            var coordinates = SEntMan.GetCoordinates(PlayerCoords);
            deadUid = SEntMan.SpawnEntity(_unit, coordinates);
            revivedUid = SEntMan.SpawnEntity(_unit, coordinates);
            var mobStateSystem = SEntMan.System<MobStateSystem>();
            mobStateSystem.ChangeMobState(deadUid, MobState.Dead);
            mobStateSystem.ChangeMobState(revivedUid, MobState.Dead);
            mobStateSystem.ChangeMobState(revivedUid, MobState.Alive);
        });

        await RunSeconds(BodyDelay + DissolveSlack);
        Assert.That(Gone(deadUid), Is.True, "A dead unit never dissolved.");
        Assert.That(Gone(revivedUid), Is.False, "A unit that came back to life dissolved.");
    }

    [Test]
    public async Task DeadPlayerBodyStays()
    {
        await Server.WaitPost(StartRule);

        // A player in one of the commander's units, and a unit nobody plays beside them: only being played tells them apart.
        var unplayedUid = EntityUid.Invalid;
        await Server.WaitPost(() =>
        {
            SEntMan.EnsureComponent<Content.Server._KS14.FieldCommander.KsFieldCommanderUnitComponent>(SPlayer);
            unplayedUid = SEntMan.SpawnEntity(_unit, SEntMan.GetCoordinates(PlayerCoords));

            var mobStateSystem = SEntMan.System<MobStateSystem>();
            mobStateSystem.ChangeMobState(SPlayer, MobState.Dead);
            mobStateSystem.ChangeMobState(unplayedUid, MobState.Dead);
        });

        await RunSeconds(BodyDelay + DissolveSlack);
        Assert.That(Gone(unplayedUid), Is.True, "A dead unit nobody plays never dissolved.");
        Assert.That(Gone(SPlayer), Is.False, "A player's body dissolved.");
    }
}
