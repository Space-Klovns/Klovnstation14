#nullable enable
using System.Collections.Generic;
using System.Numerics;
using Content.IntegrationTests.Tests.Interaction;
using Content.Shared._KS14.Damage.Pending;
using Content.Shared._KS14.FactionCollision;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Prototypes;
using Content.Shared.NPC.Systems;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.FactionCollision;

/// <summary>
///     A player whose shots pass through their own side (<see cref="KsFactionCollisionShooterComponent"/>) sees their own
///         bullet do what the server's does: hit whoever is not on their side, and pass through whoever is.
/// </summary>
/// <remarks>
///     A client is not sent anyone's NPC factions, and has those of each entity's prototype. Its copy of the player's
///         shot went by those: through every enemy of the same prototype as the player, which the server's then hit, and
///         into friends the server's passed through.
/// </remarks>
[TestOf(typeof(KsFactionCollisionSystem))]
public sealed class KsFactionCollisionPredictionTest : InteractionTest
{
    protected override string PlayerPrototype => "MobHuman"; // combat mode and hands, to fire a gun

    private const string TestGun = "KsFactionCollisionPredictionTestGun";
    private const string TestFriend = "KsFactionCollisionPredictionTestFriend";
    private const string TestOwnSide = "KsFactionCollisionPredictionTestOwnSide";
    private const string TestOtherSide = "KsFactionCollisionPredictionTestOtherSide";

    private readonly EntProtoId _testFriend = TestFriend;
    private readonly ProtoId<NpcFactionPrototype> _ownSide = TestOwnSide;
    private readonly ProtoId<NpcFactionPrototype> _otherSide = TestOtherSide;

    [TestPrototypes]
    private const string Prototypes = $@"
- type: npcFaction
  id: {TestOwnSide}

- type: npcFaction
  id: {TestOtherSide}

- type: entity
  id: {TestGun}
  components:
  - type: Item
  - type: Gun
    fireRate: 2
    minAngle: 0
    maxAngle: 0
    selectedMode: SemiAuto
    availableModes:
    - SemiAuto
  - type: BasicEntityAmmoProvider
    proto: CartridgePistol
    capacity: 10
    count: 10

# On the other side by its prototype, which is all a client knows of its factions.
- type: entity
  id: {TestFriend}
  components:
  - type: Physics
    bodyType: Static
  - type: Fixtures
    fixtures:
      fix1:
        shape:
          !type:PhysShapeAabb
          bounds: ""-0.5,-0.5,0.5,0.5""
        hard: true
        layer:
        - WallLayer
        mask:
        - FullTileMask
  - type: Damageable
  - type: Injurable
  - type: NpcFactionMember
    factions:
    - {TestOtherSide}
";

    /// <summary>
    ///     How far from the player a target is put.
    /// </summary>
    private const float TargetDistance = 4f;

    private NetEntity _gunNetEntity;

    private async Task ArmPlayer()
    {
        // The client predicts well past the last server state, so every state reset re-predicts, as a real client does.
        var clientConfigurationManager = Client.ResolveDependency<IConfigurationManager>();
        await Client.WaitPost(() => clientConfigurationManager.SetCVar(CVars.NetPredictTickBias, 4));

        _gunNetEntity = await PlaceInHands(TestGun);
        await SetCombatMode(true);
        await RunSeconds(2f); // the pickup cooldown
    }

    private async Task<EntityUid> SpawnTarget(EntProtoId prototype)
    {
        var targetUid = EntityUid.Invalid;
        await Server.WaitPost(() =>
        {
            var shooterPosition = SEntMan.System<SharedTransformSystem>().GetWorldPosition(SPlayer);
            targetUid = SEntMan.SpawnEntity(prototype, new MapCoordinates(shooterPosition + new Vector2(TargetDistance, 0f), MapData.MapId));
        });
        await RunTicks(5); // it reaches the client
        return targetUid;
    }

    private async Task SetSide(EntityUid serverUid, ProtoId<NpcFactionPrototype> side)
    {
        await Server.WaitPost(() =>
        {
            var factionSystem = SEntMan.System<NpcFactionSystem>();
            factionSystem.ClearFactions(serverUid, dirty: false);
            factionSystem.AddFaction(serverUid, side);
        });
    }

    /// <summary>
    ///     Whether the client, going by the factions it has, which are their prototypes', would count the two as on one side.
    /// </summary>
    private async Task<bool> ClientFactionsOverlap(EntityUid firstClientUid, EntityUid secondClientUid)
    {
        var overlap = false;
        await Client.WaitPost(() =>
        {
            overlap = CEntMan.TryGetComponent(firstClientUid, out NpcFactionMemberComponent? first) &&
                      CEntMan.System<NpcFactionSystem>().IsMemberOfAny(secondClientUid, first.Factions);
        });
        return overlap;
    }

    /// <summary>
    ///     Fires once past <paramref name="targetUid"/> and watches the shot out.
    /// </summary>
    /// <returns>
    ///     What the server's shot dealt it; the most the client showed as pending for it meanwhile, which is what its own
    ///         copy of the shot dealt; and whether that copy flew on past it.
    /// </returns>
    private async Task<(FixedPoint2 ServerDealt, FixedPoint2 ClientPending, bool ClientCopyFlewPast)> Shoot(EntityUid targetUid)
    {
        var clientTargetUid = ToClient(targetUid);
        var shooterPosition = Vector2.Zero;
        var serverBefore = FixedPoint2.Zero;
        await Server.WaitPost(() =>
        {
            shooterPosition = SEntMan.System<SharedTransformSystem>().GetWorldPosition(SPlayer);
            serverBefore = SEntMan.System<DamageableSystem>().GetTotalDamage(targetUid);
        });

        // Shots still in the air from before are not this one.
        var earlierProjectiles = new HashSet<EntityUid>();
        await Client.WaitPost(() =>
        {
            var projectiles = CEntMan.EntityQueryEnumerator<ProjectileComponent>();
            while (projectiles.MoveNext(out var projectileUid, out _))
            {
                earlierProjectiles.Add(projectileUid);
            }
        });

        var aimCoordinates = SEntMan.GetNetCoordinates(new EntityCoordinates(MapData.MapUid, shooterPosition + new Vector2(TargetDistance * 10f, 0f)));
        await Client.WaitPost(() =>
        {
            CEntMan.RaisePredictiveEvent(new RequestShootEvent
            {
                Gun = _gunNetEntity,
                Coordinates = aimCoordinates,
            });
        });

        var serverAfter = serverBefore;
        var clientPending = FixedPoint2.Zero;
        var clientCopyFlewPast = false;

        // Until the gun may fire again, which is long after the shot has got there on both sides.
        var ticks = (int)Server.ResolveDependency<Robust.Shared.Timing.IGameTiming>().TickRate;
        for (var tick = 0; tick < ticks; tick++)
        {
            await RunTicks(1);
            await Server.WaitPost(() => serverAfter = SEntMan.System<DamageableSystem>().GetTotalDamage(targetUid));
            await Client.WaitPost(() =>
            {
                var pending = CEntMan.System<KsPendingDamageSystem>().GetPendingDamage(clientTargetUid);
                if (pending > clientPending)
                    clientPending = pending;

                var transformSystem = CEntMan.System<SharedTransformSystem>();
                var projectiles = CEntMan.EntityQueryEnumerator<ProjectileComponent, TransformComponent>();
                while (projectiles.MoveNext(out var projectileUid, out _, out var projectileTransform))
                {
                    // The client's own copy of the shot, not the server's, which it hides.
                    if (CEntMan.IsClientSide(projectileUid) &&
                        !earlierProjectiles.Contains(projectileUid) &&
                        (transformSystem.GetWorldPosition(projectileTransform) - shooterPosition).Length() > TargetDistance + 1.5f)
                        clientCopyFlewPast = true;
                }
            });
        }

        return (serverAfter - serverBefore, clientPending, clientCopyFlewPast);
    }

    /// <summary>
    ///     An enemy of the same prototype as the player, so one the client's own factions for the two say is a friend, is hit
    ///         by the client's copy of the shot as by the server's.
    /// </summary>
    [Test]
    public async Task ClientsShotHitsAnEnemyItsPrototypeSaysIsAFriend()
    {
        await ArmPlayer();

        var enemyUid = await SpawnTarget(PlayerPrototype);
        await SetSide(enemyUid, _otherSide);
        await SetSide(SPlayer, _ownSide);
        await Server.WaitPost(() => SEntMan.EnsureComponent<KsFactionCollisionShooterComponent>(SPlayer));
        await RunTicks(5);

        Assert.That(await ClientFactionsOverlap(CPlayer, ToClient(enemyUid)),
            "precondition: by the factions the client has, the two are on one side, or this proves nothing");

        var (serverDealt, clientPending, clientCopyFlewPast) = await Shoot(enemyUid);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(serverDealt, Is.GreaterThan(FixedPoint2.Zero), "precondition: the server's shot hit the enemy");
            Assert.That(clientPending, Is.GreaterThan(FixedPoint2.Zero), "the client's copy of the shot did not hit the enemy");
            Assert.That(clientCopyFlewPast, Is.False, "the client's copy of the shot flew on through the enemy");
        }
    }

    /// <summary>
    ///     A friend the client's own factions say is an enemy is passed through by the client's copy of the shot as by the
    ///         server's, from the moment the two are on one side, and hit again once they no longer are: what a client was
    ///         told of everyone is told again when its player changes sides.
    /// </summary>
    [Test]
    public async Task ClientsShotPassesThroughAFriendUntilTheyChangeSides()
    {
        await ArmPlayer();

        // There before the player is on any side, so what the client was first told of it is out of date after.
        var friendUid = await SpawnTarget(_testFriend);
        await SetSide(friendUid, _ownSide);
        await Server.WaitPost(() => SEntMan.EnsureComponent<KsFactionCollisionShooterComponent>(SPlayer));
        await SetSide(SPlayer, _ownSide);
        await RunTicks(5);

        Assert.That(await ClientFactionsOverlap(CPlayer, ToClient(friendUid)), Is.False,
            "precondition: by the factions the client has, the two are on different sides, or this proves nothing");

        var (serverDealt, clientPending, clientCopyFlewPast) = await Shoot(friendUid);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(serverDealt, Is.EqualTo(FixedPoint2.Zero), "precondition: the server's shot passed through the friend");
            Assert.That(clientPending, Is.EqualTo(FixedPoint2.Zero), "the client's copy of the shot hit the friend");
            Assert.That(clientCopyFlewPast, "the client's copy of the shot stopped at the friend");
        }

        await SetSide(SPlayer, _otherSide);
        await RunTicks(5);

        (serverDealt, clientPending, clientCopyFlewPast) = await Shoot(friendUid);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(serverDealt, Is.GreaterThan(FixedPoint2.Zero), "precondition: the server's shot hit the one no longer a friend");
            Assert.That(clientPending, Is.GreaterThan(FixedPoint2.Zero), "the client's copy of the shot did not hit the one no longer a friend");
            Assert.That(clientCopyFlewPast, Is.False, "the client's copy of the shot flew on through the one no longer a friend");
        }
    }
}
