#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Client.Popups;
using Content.IntegrationTests.Tests._KS14.Damage;
using Content.IntegrationTests.Tests._KS14.Weapons;
using Content.IntegrationTests.Tests.Movement;
using Content.Shared._KS14.Perks.Support;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Prototypes;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared;
using Robust.Shared.Audio;
using Robust.Shared.Configuration;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Input;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.Perks;

/// <summary>
///     The Support perk's ammo resupply, walked over by a player: their own client takes it at once, with the refill, the
///         counter, one sound and one popup, and the server's take agrees. Taking it on the server alone, as before, is
///         <see cref="KsPerkSupportTest.KillDropsAPickupThatRefillsAFriendsGun"/>.
/// </summary>
[TestOf(typeof(KsAmmoPickupSystem))]
public sealed class KsAmmoPickupPredictionTest : MovementTest
{
    private const string Mob = "KsAmmoPickupPredictionTestMob";
    private const string Rifle = "KsAmmoPickupPredictionTestRifle";
    private const string Pickup = "KsAmmoPickupPredictionTestPickup";

    protected override string PlayerPrototype => Mob;

    private readonly ProtoId<NpcFactionPrototype> _foes = "KsAmmoPickupPredictionTestFoes";

    [TestPrototypes]
    private const string Prototypes = $@"
- type: npcFaction
  id: KsAmmoPickupPredictionTestSide

- type: npcFaction
  id: KsAmmoPickupPredictionTestFoes

- type: entity
  parent: MobHuman
  id: {Mob}
  components:
  - type: NpcFactionMember
    factions:
    - KsAmmoPickupPredictionTestSide

- type: entity
  id: {Pickup}
  components:
  - type: KsAmmoPickup
    radius: 0.6
    checkInterval: 0.1
    pickupSound: /Audio/Effects/beep1.ogg

- type: entity
  id: KsAmmoPickupPredictionTestCartridge
  categories: [ HideSpawnMenu ]
  components:
  - type: CartridgeAmmo
    proto: BulletPistol
    deleteOnSpawn: true

- type: entity
  parent: BaseItem
  id: KsAmmoPickupPredictionTestMagazine
  components:
  - type: BallisticAmmoProvider
    proto: KsAmmoPickupPredictionTestCartridge
    capacity: 8
    whitelist:
      components:
      - CartridgeAmmo
  - type: ContainerContainer
    containers:
      ballistic-ammo: !type:Container

- type: entity
  parent: BaseItem
  id: {Rifle}
  components:
  - type: AmmoCounter
  - type: Gun
    selectedMode: SemiAuto
    availableModes:
    - SemiAuto
  - type: MagazineAmmoProvider
  - type: ItemSlots
    slots:
      gun_magazine:
        name: Magazine
        startingItem: KsAmmoPickupPredictionTestMagazine
        whitelist:
          components:
          - BallisticAmmoProvider
  - type: ContainerContainer
    containers:
      gun_magazine: !type:ContainerSlot
";

    private int _ticksPerCheck;

    /// <summary>
    ///     A rifle in the player's hand with one round left in its magazine; the magazine and its capacity.
    /// </summary>
    private async Task<(NetEntity Gun, EntityUid Magazine, int Capacity)> HoldAlmostEmptyRifle()
    {
        var gunNetEntity = await PlaceInHands(Rifle);
        EntityUid magazineUid = default;
        var capacity = 0;
        await Server.WaitPost(() =>
        {
            var slot = (ContainerSlot)SEntMan.System<SharedContainerSystem>().GetContainer(ToServer(gunNetEntity), SharedGunSystem.MagazineSlot);
            magazineUid = slot.ContainedEntity!.Value;
            var magazine = SEntMan.GetComponent<BallisticAmmoProviderComponent>(magazineUid);
            capacity = magazine.Capacity;
            SGun.SetBallisticUnspawned((magazineUid, magazine), 1);
        });
        await RunTicks(5);
        return (gunNetEntity, magazineUid, capacity);
    }

    /// <summary>
    ///     A pickup <paramref name="tilesEast"/> of the player, for <paramref name="faction"/>, or the player's side.
    /// </summary>
    private async Task<EntityUid> SpawnPickup(float tilesEast, ProtoId<NpcFactionPrototype>? faction = null)
    {
        var pickupUid = await SpawnEntity(Pickup, SEntMan.GetCoordinates(PlayerCoords).Offset(new Vector2(tilesEast, 0f)));
        await Server.WaitPost(() =>
        {
            var factions = faction is { } id
                ? new[] { id }
                : SEntMan.GetComponent<NpcFactionMemberComponent>(SPlayer).Factions.ToArray();
            var pickupComponent = SEntMan.GetComponent<KsAmmoPickupComponent>(pickupUid);
            SEntMan.System<KsAmmoPickupSystem>().SetFactions((pickupUid, pickupComponent), factions);
            _ticksPerCheck = (int)Math.Ceiling(pickupComponent.CheckInterval.TotalSeconds * STiming.TickRate);
        });
        await RunTicks(2);
        return pickupUid;
    }

    private int ServerRounds(EntityUid magazineUid)
    {
        var magazine = SEntMan.GetComponent<BallisticAmmoProviderComponent>(magazineUid);
        return magazine.Entities.Count + magazine.UnspawnedCount;
    }

    private int ClientRounds(EntityUid serverMagazineUid)
    {
        var magazine = CEntMan.GetComponent<BallisticAmmoProviderComponent>(ToClient(serverMagazineUid));
        return magazine.Entities.Count + magazine.UnspawnedCount;
    }

    private bool ServerHas(EntityUid pickupUid) => SEntMan.EntityExists(pickupUid) && !SEntMan.IsQueuedForDeletion(pickupUid);

    /// <summary>
    ///     The player walks onto a pickup. Their client shows the full magazine on its counter while the server's is still
    ///         short, so before the server's state; after it both agree, the pickup is gone from both, and the player saw
    ///         one popup and heard one sound, their client's, however many times the take was re-predicted.
    /// </summary>
    [Test]
    public async Task WalkingOnItRefillsAtOnceOnTheClientAndTheServerAgrees()
    {
        // The client predicts well ahead of the last server state, so the take is re-predicted many times over.
        var clientConfigurationManager = Client.ResolveDependency<IConfigurationManager>();
        await Client.WaitPost(() => clientConfigurationManager.SetCVar(CVars.NetPredictTickBias, 4));

        var (gunNetEntity, magazineUid, capacity) = await HoldAlmostEmptyRifle();
        var clientGunUid = ToClient(gunNetEntity);
        var pickupUid = await SpawnPickup(2f);
        var pickupNetEntity = SEntMan.GetNetEntity(pickupUid);

        string soundPath = default!;
        await Server.WaitPost(() =>
            soundPath = ((SoundPathSpecifier)SEntMan.GetComponent<KsAmmoPickupComponent>(pickupUid).PickupSound!).Path.ToString());
        var popupText = Client.ResolveDependency<ILocalizationManager>()
            .GetString(SEntMan.GetComponent<KsAmmoPickupComponent>(pickupUid).PickupPopup);

        await Client.WaitAssertion(() => Assert.That(KsAmmoCounterStateTest.CounterShows(CEntMan, clientGunUid), Is.EqualTo(1),
            "the counter should start at the one round left"));

        // Popups only live a moment, so each one seen is kept, at the most times it was repeated.
        var popupsSeen = new Dictionary<PopupSystem.WorldPopupLabel, int>();
        void CollectPopups()
        {
            foreach (var label in CEntMan.System<PopupSystem>().WorldLabels.Where(label => label.Text == popupText))
                popupsSeen[label] = label.Repeats;
        }

        var clientHeard = new Dictionary<EntityUid, KsDamagedSoundTest.HeardSound>();
        var serverHeard = new Dictionary<EntityUid, KsDamagedSoundTest.HeardSound>();
        var clientAheadOfServer = false;
        var serverTook = false;
        await SetMovementKey(DirectionFlag.East, BoundKeyState.Down);
        for (var tick = 0; tick < 200 && !serverTook; tick++)
        {
            await RunTicks(1);
            var clientCount = 0;
            await Client.WaitPost(() =>
            {
                clientCount = KsAmmoCounterStateTest.CounterShows(CEntMan, clientGunUid);
                KsDamagedSoundTest.CollectSounds(CEntMan, MapId, soundPath, clientHeard);
                CollectPopups();
            });
            await Server.WaitPost(() =>
            {
                KsDamagedSoundTest.CollectSounds(SEntMan, MapId, soundPath, serverHeard);
                if (clientCount == capacity && ServerRounds(magazineUid) < capacity)
                    clientAheadOfServer = true;
                serverTook = !ServerHas(pickupUid);
            });
        }

        await SetMovementKey(DirectionFlag.East, BoundKeyState.Up);
        Assert.That(serverTook, "the server should have taken the pickup");

        // Long enough for the server's take, and its popup, to reach the client.
        for (var tick = 0; tick < 20; tick++)
        {
            await RunTicks(1);
            await Client.WaitPost(() =>
            {
                KsDamagedSoundTest.CollectSounds(CEntMan, MapId, soundPath, clientHeard);
                CollectPopups();
            });
        }

        await Server.WaitPost(() => KsDamagedSoundTest.CollectSounds(SEntMan, MapId, soundPath, serverHeard));

        await Client.WaitAssertion(() =>
        {
            var popups = popupsSeen.Values.Sum();
            Assert.Multiple(() =>
            {
                Assert.That(clientAheadOfServer, "the client's counter should show the refill before the server has made it");
                Assert.That(ServerRounds(magazineUid), Is.EqualTo(capacity), "the server should have filled the magazine");
                Assert.That(ClientRounds(magazineUid), Is.EqualTo(capacity), "the client should agree with the server's magazine");
                Assert.That(KsAmmoCounterStateTest.CounterShows(CEntMan, clientGunUid), Is.EqualTo(capacity), "the counter should show the full magazine");
                Assert.That(CEntMan.TryGetEntity(pickupNetEntity, out var clientPickupUid) && CEntMan.EntityExists(clientPickupUid),
                    Is.False, "the pickup should be gone on the client too");
                Assert.That(clientHeard, Has.Count.EqualTo(1), "the player should hear the pickup once, however often it was re-predicted");
                Assert.That(clientHeard.Values.All(sound => sound.ClientSide), "the player should hear their client's sound, not the server's");
                Assert.That(serverHeard.Values.Select(sound => sound.ExcludedEntity), Is.All.EqualTo(SPlayer),
                    "the server's sound should leave out the player, whose client predicted it");
                Assert.That(popups, Is.EqualTo(1), "the player should see one popup, not their client's and the server's");
            });
        });
    }

    /// <summary>
    ///     A pickup for another side, walked over with a gun to fill, and one of the player's own, stood on with empty
    ///         hands, both stay, and nothing is filled, on the server or (predicted) on the client. Picking a gun up, the
    ///         player standing on their side's pickup takes it, so it was in reach all along.
    /// </summary>
    [Test]
    public async Task AFoeOrAnEmptyHandedFriendLeavesIt()
    {
        var (gunNetEntity, magazineUid, _) = await HoldAlmostEmptyRifle();
        var foesPickupUid = await SpawnPickup(1f, _foes);
        var clientGunUid = ToClient(gunNetEntity);

        await Move(DirectionFlag.East, 1f);
        await RunTicks(_ticksPerCheck * 3);

        var foesPickupNetEntity = SEntMan.GetNetEntity(foesPickupUid);
        await Server.WaitAssertion(() =>
        {
            Assert.That(ServerHas(foesPickupUid), "another side's pickup should stay on the server");
            Assert.That(ServerRounds(magazineUid), Is.EqualTo(1), "another side's pickup should fill nothing on the server");
        });
        await Client.WaitAssertion(() =>
        {
            var clientPickupUid = CEntMan.GetEntity(foesPickupNetEntity);
            Assert.That(CEntMan.EntityExists(clientPickupUid) && !CEntMan.IsQueuedForDeletion(clientPickupUid) &&
                        CEntMan.GetComponent<TransformComponent>(clientPickupUid).MapID == MapId,
                "another side's pickup should stay on the client");
            Assert.That(ClientRounds(magazineUid), Is.EqualTo(1), "another side's pickup should fill nothing on the client");
            Assert.That(KsAmmoCounterStateTest.CounterShows(CEntMan, clientGunUid), Is.EqualTo(1));
        });

        // Empty-handed, on the player's own side's pickup.
        await DeleteHeldEntity();
        await Server.WaitPost(() => SEntMan.DeleteEntity(foesPickupUid));
        var ownPickupUid = await SpawnEntity(Pickup, Transform.GetMoverCoordinates(SPlayer));
        await Server.WaitPost(() => SEntMan.System<KsAmmoPickupSystem>().SetFactions(
            (ownPickupUid, SEntMan.GetComponent<KsAmmoPickupComponent>(ownPickupUid)),
            SEntMan.GetComponent<NpcFactionMemberComponent>(SPlayer).Factions));
        await RunTicks(_ticksPerCheck * 3);

        var ownPickupNetEntity = SEntMan.GetNetEntity(ownPickupUid);
        await Server.WaitAssertion(() => Assert.That(ServerHas(ownPickupUid), "an empty-handed friend should leave it on the server"));
        await Client.WaitAssertion(() =>
        {
            var clientPickupUid = CEntMan.GetEntity(ownPickupNetEntity);
            Assert.That(CEntMan.EntityExists(clientPickupUid) && !CEntMan.IsQueuedForDeletion(clientPickupUid) &&
                        CEntMan.GetComponent<TransformComponent>(clientPickupUid).MapID == MapId,
                "an empty-handed friend should leave it on the client");
        });

        // The control: with a gun to fill, the same player on the same spot takes it.
        var (_, newMagazineUid, newCapacity) = await HoldAlmostEmptyRifle();
        await RunTicks(_ticksPerCheck * 3);
        await Server.WaitAssertion(() =>
        {
            Assert.That(ServerHas(ownPickupUid), Is.False, "with a gun to fill, the player standing on it should take it");
            Assert.That(ServerRounds(newMagazineUid), Is.EqualTo(newCapacity));
        });
    }
}
