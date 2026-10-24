#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Client._KS14.PredictedSpawning;
using Content.IntegrationTests.Tests.Interaction;
using Content.Shared._KS14.PredictedSpawning;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Client.GameObjects;
using Robust.Shared;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Input;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Events;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._KS14.PredictedSpawning;

/// <summary>
///     User-mode predicted spawns (<see cref="KsSharedPredictedSpawnSystem"/>): the user's client keeps its own copy
///         for its whole life, and the server's copy is hidden and inert on that client only.
///
///     The pooled client runs a couple of ticks ahead of the server, so every client tick here re-predicts the ticks
///         since the last server state and then predicts one new one. That is the re-prediction a real client does,
///         and what both the "deleted on the next reset" and the "stepped too fast" bugs come from.
/// </summary>
[TestOf(typeof(KsSharedPredictedSpawnSystem))]
public sealed class KsPredictedSpawnTest : InteractionTest
{
    protected override string PlayerPrototype => "MobHuman"; // combat mode and hands, to fire a gun

    private const string TestEffect = "KsPredictedSpawnTestEffect";
    private const string TestGun = "KsPredictedSpawnTestGun";
    private const string MobHuman = "MobHuman";
    private const string RealPistol = "WeaponPistolMk58"; // a magazine, a chambered round and an ejected casing
    private const string TestWall = "KsPredictedSpawnTestWall";
    private const string TestOverlapper = "KsPredictedSpawnTestOverlapper";
    private const string BulletImpactEffect = "BulletImpactEffect";

    private readonly EntProtoId _testEffect = TestEffect;
    private readonly EntProtoId _testWall = TestWall;
    private readonly EntProtoId _testOverlapper = TestOverlapper;
    private static readonly EntProtoId WallSolid = "WallSolid";
    private static readonly EntProtoId BulletPistol = "BulletPistol";
    private readonly EntProtoId _mobHuman = MobHuman;

    [TestPrototypes]
    private const string Prototypes = $@"
- type: entity
  id: {TestEffect}
  components:
  - type: Sprite
  - type: PointLight
  - type: Physics
    bodyType: Dynamic
    bodyStatus: InAir
    linearDamping: 0
    angularDamping: 0
  - type: Fixtures
    fixtures:
      fix1:
        shape:
          !type:PhysShapeCircle
          radius: 0.1
        hard: false
  - type: TimedDespawn
    lifetime: 30

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

- type: entity
  id: {TestWall}
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

- type: entity
  id: {TestOverlapper}
  components:
  - type: Physics
    bodyType: Dynamic
    bodyStatus: InAir
  - type: Fixtures
    fixtures:
      fix1:
        shape:
          !type:PhysShapeCircle
          radius: 0.3
        hard: false
        layer:
        - Impassable
        mask:
        - Impassable
  - type: TimedDespawn
    lifetime: 0.5
";

    /// <summary>
    ///     Off the 1x1 test grid, so nothing touches the effect and no tile friction slows it.
    /// </summary>
    private MapCoordinates SpaceCoordinates(float x) => new(MapData.MapCoords.Position + new Vector2(x, 20f), MapData.MapId);

    #region Server copy

    /// <summary>
    ///     The server's copy of a spawn made for this client's player is hidden, its light switched off, its physics
    ///         not predicted, and it refuses every collision here. A spawn made for anyone else, or for no one, is a
    ///         normal one: shown, predicted, colliding.
    /// </summary>
    [Test]
    public async Task ServerCopyIsHiddenOnlyForItsUser()
    {
        var otherUserUid = await SpawnServerOnly(MobHuman, SpaceCoordinates(-10f));

        EntityUid ownUid = default, otherUid = default, noUserUid = default;
        await Server.WaitPost(() =>
        {
            var spawnSystem = SEntMan.System<KsSharedPredictedSpawnSystem>();
            ownUid = spawnSystem.PredictedSpawn(_testEffect, SpaceCoordinates(0f), user: SPlayer);
            otherUid = spawnSystem.PredictedSpawn(_testEffect, SpaceCoordinates(2f), user: otherUserUid);
            noUserUid = spawnSystem.PredictedSpawn(_testEffect, SpaceCoordinates(4f));
        });

        await RunTicks(10);

        await Client.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                AssertShown(ToClient(ownUid), shown: false, "the server's copy of this player's own spawn");
                AssertShown(ToClient(otherUid), shown: true, "a spawn made for another user");
                AssertShown(ToClient(noUserUid), shown: true, "a spawn made for no user");
            });
        });
    }

    private void AssertShown(EntityUid clientUid, bool shown, string what)
    {
        Assert.That(CEntMan.EntityExists(clientUid), $"{what} never reached the client");

        Assert.That(CEntMan.GetComponent<SpriteComponent>(clientUid).Visible, Is.EqualTo(shown),
            $"{what}: sprite visible should be {shown}");
        Assert.That(CEntMan.GetComponent<PointLightComponent>(clientUid).Enabled, Is.EqualTo(shown),
            $"{what}: light enabled should be {shown}");
        Assert.That(CEntMan.GetComponent<PhysicsComponent>(clientUid).Predict, Is.EqualTo(shown),
            $"{what}: physics predicted should be {shown}");

        var physicsComponent = CEntMan.GetComponent<PhysicsComponent>(clientUid);
        var fixture = CEntMan.GetComponent<FixturesComponent>(clientUid).Fixtures.Values.First();
        var preventCollideEvent = new PreventCollideEvent(clientUid, clientUid, physicsComponent, physicsComponent, fixture, fixture);
        CEntMan.EventBus.RaiseLocalEvent(clientUid, ref preventCollideEvent);
        Assert.That(preventCollideEvent.Cancelled, Is.EqualTo(!shown), $"{what}: collisions refused should be {!shown}");
    }

    #endregion

    #region Kept copy

    /// <summary>
    ///     THE regression test for the vanishing bullet. A user-mode spawn the client made itself outlives many
    ///         applied server states, and moves at its own velocity: once per real tick, not once per re-prediction.
    ///
    ///     The control, an ordinary predicted spawn made at the same moment, is gone after the same ticks: that is
    ///         what proves states were applied and re-predicted in between at all, so the survival is not vacuous.
    /// </summary>
    [Test]
    public async Task KeptCopySurvivesStatesAndMovesAtItsOwnSpeed()
    {
        const int ticks = 30;
        var velocity = new Vector2(2f, 0f);

        EntityUid keptUid = default, controlUid = default;
        var startPosition = Vector2.Zero;

        await Client.WaitPost(() =>
        {
            var spawnSystem = CEntMan.System<KsSharedPredictedSpawnSystem>();
            var physicsSystem = CEntMan.System<SharedPhysicsSystem>();

            keptUid = spawnSystem.PredictedSpawn(_testEffect, SpaceCoordinates(0f), user: CPlayer);
            controlUid = spawnSystem.PredictedSpawn(_testEffect, SpaceCoordinates(3f));

            physicsSystem.SetLinearVelocity(keptUid, velocity);
            physicsSystem.SetLinearVelocity(controlUid, velocity);
            startPosition = CEntMan.System<SharedTransformSystem>().GetWorldPosition(keptUid);
        });

        await RunTicks(ticks);

        await Client.WaitAssertion(() =>
        {
            Assert.That(CEntMan.EntityExists(controlUid), Is.False,
                "an ordinary predicted spawn survived, so no state reset happened in between and this proves nothing");
            Assert.That(CEntMan.EntityExists(keptUid),
                "the kept copy was deleted by a state reset, as an ordinary predicted spawn is");
            Assert.That(CEntMan.HasComponent<KsKeptPredictedSpawnComponent>(keptUid));

            var tickPeriod = (float)Client.ResolveDependency<IGameTiming>().TickPeriod.TotalSeconds;
            var travelled = CEntMan.System<SharedTransformSystem>().GetWorldPosition(keptUid).X - startPosition.X;
            var expected = velocity.X * ticks * tickPeriod;

            // Two ticks of slack either way, for the ticks at the edges of RunTicks; re-prediction stepping it would
            //      be several times too far.
            Assert.That(travelled, Is.EqualTo(expected).Within(velocity.X * 2f * tickPeriod),
                $"the kept copy travelled {travelled}m in {ticks} ticks at {velocity.X}m/s, expected {expected}m");
        });
    }

    #endregion

    #region Gun

    /// <summary>
    ///     A shot the client fires itself, through the same request a real client sends. After the server's state
    ///         for the shot has arrived, the shooter still sees exactly one bullet - its own - and the server's copy
    ///         is there but hidden.
    /// </summary>
    [Test]
    public async Task FiredBulletStaysVisibleForTheShooter()
    {
        var gunNetEntity = await PlaceInHands(TestGun);
        await SetCombatMode(true);
        await RunSeconds(2f); // the pickup cooldown

        var targetCoordinates = SEntMan.GetNetCoordinates(new EntityCoordinates(MapData.MapUid, MapData.MapCoords.Position + new Vector2(40f, 0.5f)));

        await Client.WaitPost(() =>
        {
            CEntMan.RaisePredictiveEvent(new RequestShootEvent
            {
                Gun = gunNetEntity,
                Coordinates = targetCoordinates,
            });
        });

        // While the shot is still being re-predicted, each re-prediction spawns the bullet again. Those copies must
        //      be gone by the end of their tick, or the shooter sees a second bullet for as long as it is re-predicted.
        for (var tick = 0; tick < 4; tick++)
        {
            await RunTicks(1);
            await Client.WaitAssertion(() =>
            {
                var clientSideBullets = FindBullets(CEntMan, CPlayer).Where(uid => CEntMan.IsClientSide(uid)).ToList();
                Assert.That(clientSideBullets, Has.Count.EqualTo(1),
                    $"{clientSideBullets.Count} client-side bullets {tick + 1} ticks after the shot, expected only the kept one");
            });
        }

        // Well past the round trip: the server has fired, and its bullet's state has reached the client.
        await RunTicks(6);

        var serverBulletUid = EntityUid.Invalid;
        await Server.WaitAssertion(() =>
        {
            var serverBullets = FindBullets(SEntMan, SPlayer);
            Assert.That(serverBullets, Has.Count.EqualTo(1), "the server did not fire exactly one bullet");
            serverBulletUid = serverBullets.Single();
        });

        await Client.WaitAssertion(() =>
        {
            var clientBullets = FindBullets(CEntMan, CPlayer);
            var keptBullets = clientBullets.Where(uid => CEntMan.HasComponent<KsKeptPredictedSpawnComponent>(uid)).ToList();

            Assert.That(keptBullets, Has.Count.EqualTo(1),
                "the shooter's client does not have exactly one kept bullet of its own");
            Assert.That(CEntMan.GetComponent<SpriteComponent>(keptBullets.Single()).Visible,
                "the shooter's own bullet is not visible");

            var serverBulletOnClientUid = ToClient(serverBulletUid);
            Assert.That(CEntMan.EntityExists(serverBulletOnClientUid), "the server's bullet never reached the client");
            Assert.That(CEntMan.GetComponent<SpriteComponent>(serverBulletOnClientUid).Visible, Is.False,
                "the server's bullet is visible to the shooter alongside their own");

            // The kept bullet, the server's hidden one, and nothing else: no re-predicted throwaway left behind.
            Assert.That(clientBullets, Has.Count.EqualTo(2), "stray bullets on the shooter's client");
        });
    }

    /// <summary>
    ///     What a shot meets, relative to the shooter, which fires along +x.
    /// </summary>
    public enum ShotLayout
    {
        /// <summary>Nothing at all.</summary>
        Nothing,
        /// <summary>One wall, hit square on.</summary>
        Wall,
        /// <summary>Two walls, the shot along the seam between them, so the bullet touches both at once.</summary>
        WallSeam,
        /// <summary>The same, close enough that the re-predicted throwaways are spawned overlapping them.</summary>
        WallSeamPointBlank,
        /// <summary>A mob inside the fly-by radius from the start.</summary>
        MobNear,
        /// <summary>A mob well outside it.</summary>
        MobFar,
        /// <summary>A mob with a wall a little behind it.</summary>
        MobBeforeWall,
        /// <summary>A row of real walls, anchored on a station-like grid the shooter stands on.</summary>
        GridWallRow,
        /// <summary>A mob standing on that grid, with the wall row behind it.</summary>
        GridMobBeforeWallRow,
    }

    /// <summary>
    ///     A shot the client fires itself, hitting what <paramref name="layout"/> puts in its way, with server states
    ///         applied every tick. The state reset (<c>ClientGameStateManager.ResetPredictedEntities</c>) refreshes the
    ///         contacts of every predicted body, and the kept bullet is one; its hit used to land there, and the
    ///         hit's immediate deletion of a client-side entity pulled the rest of its contacts out from under that
    ///         pass (<c>KeyNotFoundException: Entity 0 does not have a component of type TransformComponent</c>).
    /// </summary>
    [Test]
    public async Task FiredBulletHitsWithoutBreakingTheStateReset([Values] ShotLayout layout, [Values(TestGun, RealPistol)] string gun)
    {
        // The client predicts this many ticks past the server, so it re-predicts every frame, as a real client does.
        var clientConfigurationManager = Client.ResolveDependency<IConfigurationManager>();
        await Client.WaitPost(() => clientConfigurationManager.SetCVar(CVars.NetPredictTickBias, 4));
        var gunNetEntity = await PlaceInHands(gun);
        if (gun == RealPistol)
            await UseInHand(); // racks a round into the empty chamber
        await SetCombatMode(true);
        await RunSeconds(2f); // the pickup cooldown

        var shooterPosition = Vector2.Zero;
        await Server.WaitPost(() =>
        {
            shooterPosition = SEntMan.System<SharedTransformSystem>().GetWorldPosition(SPlayer);

            void SpawnAt(EntProtoId prototype, float x, float y) =>
                SEntMan.SpawnEntity(prototype, new MapCoordinates(shooterPosition + new Vector2(x, y), MapData.MapId));

            switch (layout)
            {
                case ShotLayout.Wall:
                    SpawnAt(_testWall, 4f, 0f);
                    break;
                case ShotLayout.WallSeam:
                    SpawnAt(_testWall, 4f, 0.5f);
                    SpawnAt(_testWall, 4f, -0.5f);
                    break;
                case ShotLayout.WallSeamPointBlank:
                    SpawnAt(_testWall, 1.2f, 0.5f);
                    SpawnAt(_testWall, 1.2f, -0.5f);
                    break;
                case ShotLayout.MobNear:
                    SpawnAt(_mobHuman, 2f, 0f);
                    break;
                case ShotLayout.MobFar:
                    SpawnAt(_mobHuman, 8f, 0f);
                    break;
                case ShotLayout.GridWallRow:
                case ShotLayout.GridMobBeforeWallRow:
                    var gridEntity = MapData.Grid;
                    var plating = new Tile(TileMan[Plating].TileId);
                    for (var tileX = -2; tileX <= 10; tileX++)
                    {
                        for (var tileY = -4; tileY <= 4; tileY++)
                            MapSystem.SetTile(gridEntity, new Vector2i(tileX, tileY), plating);
                    }

                    for (var tileY = -4; tileY <= 4; tileY++)
                        SEntMan.SpawnEntity(WallSolid, new EntityCoordinates(gridEntity, new Vector2(7.5f, tileY + 0.5f)));

                    if (layout == ShotLayout.GridMobBeforeWallRow)
                        SEntMan.SpawnEntity(_mobHuman, new EntityCoordinates(gridEntity, new Vector2(4.5f, 0.5f)));
                    break;
                case ShotLayout.MobBeforeWall:
                    SpawnAt(_mobHuman, 5f, 0f);
                    SpawnAt(_testWall, 7f, 0.5f);
                    SpawnAt(_testWall, 7f, -0.5f);
                    break;
            }
        });

        await RunTicks(5); // the targets reach the client

        var targetCoordinates = SEntMan.GetNetCoordinates(new EntityCoordinates(MapData.MapUid, shooterPosition + new Vector2(40f, 0f)));
        await Client.WaitPost(() =>
        {
            CEntMan.RaisePredictiveEvent(new RequestShootEvent
            {
                Gun = gunNetEntity,
                Coordinates = targetCoordinates,
            });
        });

        // A tick at a time, so a failure names the tick it happened on.
        var sawKeptBullet = false;
        for (var tick = 0; tick < 60; tick++)
        {
            await RunTicks(1);
            await Client.WaitPost(() =>
            {
                sawKeptBullet |= FindBullets(CEntMan, CPlayer).Any(uid => CEntMan.HasComponent<KsKeptPredictedSpawnComponent>(uid));
            });
        }

        // Point blank, the bullet is spent within the tick it is fired on, so it is never seen between ticks.
        if (layout != ShotLayout.WallSeamPointBlank)
            Assert.That(sawKeptBullet, "the shot never made a kept bullet, so this proves nothing");

        if (layout == ShotLayout.Nothing)
            return;

        await Client.WaitAssertion(() =>
        {
            var keptBullets = FindBullets(CEntMan, CPlayer)
                .Where(uid => CEntMan.HasComponent<KsKeptPredictedSpawnComponent>(uid))
                .ToList();
            Assert.That(keptBullets, Is.Empty, "the shooter's own bullet flew through what it should have hit");
        });
    }

    /// <summary>
    ///     THE regression test for <c>KeyNotFoundException: Entity 0 does not have a component of type
    ///         TransformComponent</c> in <c>PhysicsSystem.UpdateIsTouching</c>, under <c>ResetContacts</c>.
    ///
    ///     A kept bullet hit from inside the state reset. The player walks, so the client predicts them ahead of the
    ///         last server state, and a kept bullet lies on the server's position. Every applied state puts the player
    ///         back there before re-predicting them forward, and <c>PhysicsSystem.ResetContacts</c> raises the
    ///         start of that collision before any tick runs. The hit deletes the bullet, and deleting a client-side
    ///         entity destroys its contacts at once, including ones further down the list that pass is still
    ///         walking: their pooled, cleared copies are what it then reads entity 0 from.
    ///
    ///     The offsets are where the bullet lies relative to the server's position. Further ahead, the predicted
    ///         player reaches it first, in an ordinary tick, and the hit never lands in the reset.
    /// </summary>
    [Test]
    public async Task KeptProjectileHitByTheStateReset([Values(-0.3f, -0.15f, 0f)] float offset)
    {
        // Far enough ahead of the server that the walking player's predicted and server positions are well apart.
        var clientConfigurationManager = Client.ResolveDependency<IConfigurationManager>();
        await Client.WaitPost(() => clientConfigurationManager.SetCVar(CVars.NetPredictTickBias, 8));

        await Server.WaitPost(() =>
        {
            var plating = new Tile(TileMan[Plating].TileId);
            for (var tileX = -2; tileX <= 40; tileX++)
            {
                for (var tileY = -2; tileY <= 2; tileY++)
                    MapSystem.SetTile(MapData.Grid, new Vector2i(tileX, tileY), plating);
            }
        });
        await AddGravity();

        await SetMovementKey(DirectionFlag.East, BoundKeyState.Down);
        await RunSeconds(1f);

        var serverPosition = new MapCoordinates();
        await Server.WaitPost(() => serverPosition = SEntMan.System<SharedTransformSystem>().GetMapCoordinates(SPlayer));

        EntityUid keptUid = default;
        await Client.WaitPost(() =>
        {
            var bulletCoordinates = new MapCoordinates(serverPosition.Position + new Vector2(offset, 0f), serverPosition.MapId);
            keptUid = CEntMan.System<KsSharedPredictedSpawnSystem>().PredictedSpawn(BulletPistol, bulletCoordinates, user: CPlayer);
        });

        // A tick at a time, so a failure names the tick it happened on.
        for (var tick = 0; tick < 30; tick++)
        {
            await RunTicks(1);
        }

        await SetMovementKey(DirectionFlag.East, BoundKeyState.Up);
        await RunTicks(1);

        await Client.WaitAssertion(() =>
        {
            Assert.That(CEntMan.Deleted(keptUid), "the walking player never reached the kept bullet, so this proves nothing");
        });
    }

    /// <summary>
    ///     A kept copy despawning (its own <c>TimedDespawn</c>) while it overlaps something, with states applied
    ///         every tick around it.
    /// </summary>
    [Test]
    public async Task KeptCopyDespawnsWhileOverlapping()
    {
        await Server.WaitPost(() =>
        {
            SEntMan.SpawnEntity(_testWall, SpaceCoordinates(0.5f));
            SEntMan.SpawnEntity(_testWall, SpaceCoordinates(-0.5f));
        });
        await RunTicks(5);

        EntityUid keptUid = default;
        await Client.WaitPost(() =>
        {
            var spawnSystem = CEntMan.System<KsSharedPredictedSpawnSystem>();
            keptUid = spawnSystem.PredictedSpawn(_testOverlapper, SpaceCoordinates(0f), user: CPlayer);
            CEntMan.System<SharedPhysicsSystem>().SetLinearVelocity(keptUid, new Vector2(0f, 0.1f));
        });

        for (var tick = 0; tick < 60; tick++)
        {
            await RunTicks(1);
        }

        await Client.WaitAssertion(() =>
        {
            Assert.That(CEntMan.EntityExists(keptUid), Is.False, "the kept copy never despawned");
        });
    }

    /// <summary>
    ///     The kept bullet is matched to the server's, pulled onto it when the server's goes somewhere else (here
    ///         moved sideways by the server, as a lag-compensated or reflected shot would be), and deleted with it.
    /// </summary>
    [Test]
    public async Task KeptBulletFollowsAndDiesWithTheServersCopy()
    {
        var (keptUid, serverBulletUid) = await FireAlongX(new Vector2(40f, 0f));

        await Client.WaitAssertion(() =>
        {
            Assert.That(CEntMan.GetComponent<KsKeptPredictedSpawnComponent>(keptUid).ServerCopy, Is.EqualTo(ToClient(serverBulletUid)),
                "the kept bullet was not matched to the server's");
        });

        await Server.WaitPost(() =>
        {
            var transformSystem = SEntMan.System<SharedTransformSystem>();
            transformSystem.SetWorldPosition(serverBulletUid, transformSystem.GetWorldPosition(serverBulletUid) + new Vector2(0f, 1.5f));
        });
        await RunSeconds(0.6f);

        await Client.WaitAssertion(() =>
        {
            var transformSystem = CEntMan.System<SharedTransformSystem>();
            var keptY = transformSystem.GetWorldPosition(keptUid).Y;
            var serverY = transformSystem.GetWorldPosition(ToClient(serverBulletUid)).Y;
            Assert.That(keptY, Is.EqualTo(serverY).Within(0.1f), "the kept bullet was not pulled onto the server's");
        });

        await Server.WaitPost(() => SEntMan.QueueDeleteEntity(serverBulletUid));
        await RunTicks(5);

        await Client.WaitAssertion(() =>
        {
            Assert.That(CEntMan.EntityExists(keptUid), Is.False, "the kept bullet outlived the server's");
        });
    }

    /// <summary>
    ///     The shooter's own bullet plays its impact the moment it hits, without waiting for the server. When it hit
    ///         the same wall the server's did, that is the only impact; when it hit a wall only this client has, the
    ///         server's hit on the wall behind plays as well.
    /// </summary>
    [Test]
    public async Task ImpactPlaysAtOnceAndOnlyOnce([Values] bool clientOnlyWall)
    {
        // Far enough ahead of the server that the shooter's hit clearly comes first.
        var clientConfigurationManager = Client.ResolveDependency<IConfigurationManager>();
        await Client.WaitPost(() => clientConfigurationManager.SetCVar(CVars.NetPredictTickBias, 4));

        var shooterPosition = Vector2.Zero;
        await Server.WaitPost(() =>
        {
            shooterPosition = SEntMan.System<SharedTransformSystem>().GetWorldPosition(SPlayer);
            SEntMan.SpawnEntity(_testWall, new MapCoordinates(shooterPosition + new Vector2(6f, 0f), MapData.MapId));
        });
        await RunTicks(5);
        if (clientOnlyWall)
        {
            await Client.WaitPost(() =>
                CEntMan.SpawnEntity(_testWall, new MapCoordinates(shooterPosition + new Vector2(3f, 0f), MapData.MapId)));
        }

        var impactXs = new Dictionary<EntityUid, float>();
        int? firstImpactTick = null, serverHitTick = null;
        var gunNetEntity = await PrepareGun();
        await Shoot(gunNetEntity, shooterPosition + new Vector2(40f, 0f));

        var serverFired = false;
        for (var tick = 0; tick < 60; tick++)
        {
            await RunTicks(1);

            var currentTick = tick;
            await Server.WaitPost(() =>
            {
                var serverBullets = FindBullets(SEntMan, SPlayer).Count;
                serverFired |= serverBullets > 0;
                if (serverFired && serverBullets == 0)
                    serverHitTick ??= currentTick;
            });
            await Client.WaitPost(() =>
            {
                var transformSystem = CEntMan.System<SharedTransformSystem>();
                var enumerator = CEntMan.AllEntityQueryEnumerator<MetaDataComponent>();
                while (enumerator.MoveNext(out var uid, out var metaDataComponent))
                {
                    if (metaDataComponent.EntityPrototype?.ID != BulletImpactEffect || impactXs.ContainsKey(uid))
                        continue;

                    impactXs[uid] = transformSystem.GetWorldPosition(uid).X - shooterPosition.X;
                    firstImpactTick ??= currentTick;
                }
            });
        }

        var impacts = string.Join(", ", impactXs.Values);
        Assert.Multiple(() =>
        {
            Assert.That(serverHitTick, Is.Not.Null, "the server's bullet never hit");
            Assert.That(firstImpactTick ?? int.MaxValue, Is.LessThan(serverHitTick ?? 0), "the shooter's first impact waited for the server's hit");
            if (clientOnlyWall)
            {
                Assert.That(impactXs.Values, Has.Exactly(1).LessThan(4.5f), $"not one impact where the shooter's bullet hit; impacts at {impacts}");
                Assert.That(impactXs.Values, Has.Exactly(1).GreaterThan(4.5f), $"not one impact where the server's bullet hit; impacts at {impacts}");
            }
            else
            {
                Assert.That(impactXs.Values, Has.Count.EqualTo(1).And.Exactly(1).GreaterThan(4.5f), $"the one hit did not play exactly one impact; impacts at {impacts}");
            }
        });
    }

    private async Task<NetEntity> PrepareGun()
    {
        var gunNetEntity = await PlaceInHands(TestGun);
        await SetCombatMode(true);
        await RunSeconds(2f); // the pickup cooldown
        return gunNetEntity;
    }

    private async Task Shoot(NetEntity gunNetEntity, Vector2 worldTarget)
    {
        var targetCoordinates = SEntMan.GetNetCoordinates(new EntityCoordinates(MapData.MapUid, worldTarget));
        await Client.WaitPost(() =>
        {
            CEntMan.RaisePredictiveEvent(new RequestShootEvent
            {
                Gun = gunNetEntity,
                Coordinates = targetCoordinates,
            });
        });
    }

    /// <summary>
    ///     Fires the test gun at <paramref name="offset"/> from the shooter and waits past the round trip.
    /// </summary>
    private async Task<(EntityUid KeptUid, EntityUid ServerBulletUid)> FireAlongX(Vector2 offset)
    {
        var gunNetEntity = await PrepareGun();
        var shooterPosition = Vector2.Zero;
        await Server.WaitPost(() => shooterPosition = SEntMan.System<SharedTransformSystem>().GetWorldPosition(SPlayer));
        await Shoot(gunNetEntity, shooterPosition + offset);
        await RunTicks(10);

        var serverBulletUid = EntityUid.Invalid;
        await Server.WaitAssertion(() => serverBulletUid = FindBullets(SEntMan, SPlayer).Single());

        var keptUid = EntityUid.Invalid;
        await Client.WaitAssertion(() =>
            keptUid = FindBullets(CEntMan, CPlayer).Single(uid => CEntMan.HasComponent<KsKeptPredictedSpawnComponent>(uid)));

        return (keptUid, serverBulletUid);
    }

    private static List<EntityUid> FindBullets(IEntityManager entityManager, EntityUid shooterUid)
    {
        var bullets = new List<EntityUid>();
        var enumerator = entityManager.AllEntityQueryEnumerator<ProjectileComponent>();
        while (enumerator.MoveNext(out var uid, out var projectileComponent))
        {
            if (projectileComponent.Shooter == shooterUid)
                bullets.Add(uid);
        }

        return bullets;
    }

    #endregion

    private async Task<EntityUid> SpawnServerOnly(string prototype, MapCoordinates coordinates)
    {
        var uid = EntityUid.Invalid;
        await Server.WaitPost(() => uid = SEntMan.SpawnEntity(prototype, coordinates));
        return uid;
    }
}
