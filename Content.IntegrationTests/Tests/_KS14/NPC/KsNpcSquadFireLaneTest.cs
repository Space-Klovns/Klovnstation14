#nullable enable
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server._KS14.NPC.Squad;
using Content.Shared._KS14.CCVar;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using System.Linq;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Systems;
using Robust.UnitTesting.Pool;
using static Content.IntegrationTests.Tests._KS14.NPC.KsNpcSquadTestHelpers;

namespace Content.IntegrationTests.Tests._KS14.NPC;

/// <summary>
///     Squad fire lanes, both ways: a spot whose line to what it covers runs through a teammate is penalised,
///         and so is a spot standing in a teammate's line. Two squadmates, A at (0, 0) and B at (3, 0), with the
///         squad's threat east of both at (10, 0), so B's line of fire runs east along y = 0.5.
/// </summary>
public sealed class KsNpcSquadFireLaneTest : GameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    private const int SquadUpdateTicks = 90;

    private static readonly Vector2 ThreatPosition = new(10.5f, 0.5f);

    [Test]
    public async Task TestOwnLaneThroughTeammateIsPenalised()
    {
        var penalty = await GetPenaltyForA(candidate: new Vector2(0.5f, 0.5f), aimAtThreat: true);
        Assert.That(penalty, Is.LessThan(1f), "A's line to the threat runs straight through B");
    }

    [Test]
    public async Task TestStandingInTeammateLaneIsPenalised()
    {
        var penalty = await GetPenaltyForA(candidate: new Vector2(6.5f, 0.5f), aimAtThreat: false);
        Assert.That(penalty, Is.LessThan(1f), "the spot is between B and the threat B is covering");
    }

    /// <summary>
    ///     Control: a spot clear of B and of B's line, whose own line misses B, is not penalised at all.
    /// </summary>
    [Test]
    public async Task TestClearLaneIsNotPenalised()
    {
        var penalty = await GetPenaltyForA(candidate: new Vector2(0.5f, 4.5f), aimAtThreat: true);
        Assert.That(penalty, Is.EqualTo(1f));
    }

    /// <summary>
    ///     With the cvar off, even a lane straight through a teammate costs nothing.
    /// </summary>
    [Test]
    public async Task TestDisabledByCVar()
    {
        await OverrideCVar(Side.Server, KsCCVars.NpcSquadFireLanes, false);

        var penalty = await GetPenaltyForA(candidate: new Vector2(0.5f, 0.5f), aimAtThreat: true);
        Assert.That(penalty, Is.EqualTo(1f));
    }

    /// <summary>
    ///     A teammate on top of the spot, covering some other way, is in its line whichever way it faces: a shot from there
    ///         starts inside it. Mobs that walk through each other stand so often. It used to count only in front.
    /// </summary>
    [Test]
    public void TestATeammateOnTheSpotIsPenalised()
    {
        var teammate = new Vector2(-0.1f, 0f);
        var lanes = new[] { new NpcFireLane(teammate, new Vector2(-0.1f, 10f)) };

        Assert.That(NpcSquadFireLaneSystem.GetPenalty(Vector2.Zero, new Vector2(10f, 0f), new[] { teammate }, lanes),
            Is.LessThan(1f));

        // Control: the same, a few tiles apart, is clear.
        var apart = new Vector2(-3f, 0f);
        Assert.That(NpcSquadFireLaneSystem.GetPenalty(Vector2.Zero, new Vector2(10f, 0f), new[] { apart }, new[] { new NpcFireLane(apart, new Vector2(-3f, 10f)) }),
            Is.EqualTo(1f));
    }

    /// <summary>
    ///     When shooting, with a dead straight gun at a target 10 tiles east: a friend is held for in the way, past the target
    ///         (a miss flies on), and where a round starts: on top of the shooter, or just behind it, as a round reaches back
    ///         behind where it is spawned. Not for one some way behind, off to the side, or just beside it.
    /// </summary>
    [TestCase(3f, 0f, true)] // in the way
    [TestCase(12.5f, 0f, true)] // past what it shoots at, where a miss goes
    [TestCase(-3f, 0f, false)] // some way behind
    [TestCase(0f, 4f, false)] // off to the side
    [TestCase(0.1f, 0.1f, true)] // on top of it
    [TestCase(-0.6f, 0f, true)] // just behind: the round reaches back into it
    [TestCase(-0.8f, 0f, false)] // a little further behind: it does not
    [TestCase(0f, 0.55f, false)] // just beside: a round goes past
    public async Task TestAFriendWhereARoundGoesHoldsTheShot(float friendX, float friendY, bool held)
    {
        var (entManager, gridUid, shooterUid) = await SetUpShooter(StraightGun);
        await SpawnFriend(entManager, gridUid, friendX, friendY);

        await Pair.Server.WaitAssertion(() =>
            Assert.That(IsShotClear(entManager, gridUid, shooterUid), Is.EqualTo(!held)));
    }

    /// <summary>
    ///     A friend past the target, behind a wall the round stops at, is safe.
    /// </summary>
    [Test]
    public async Task TestAFriendPastAWallIsClear()
    {
        var (entManager, gridUid, shooterUid) = await SetUpShooter(StraightGun);
        await SpawnFriend(entManager, gridUid, 12.5f, 0f);
        await Pair.Server.WaitPost(() => SpawnAt(entManager, "WallSolid", gridUid, 11, 0));
        await Pair.RunTicksSync(1);

        await Pair.Server.WaitAssertion(() =>
            Assert.That(IsShotClear(entManager, gridUid, shooterUid), Is.True));
    }

    /// <summary>
    ///     A friend off the line, at range: clear of a dead straight gun, but inside the cone of one that sprays.
    /// </summary>
    [TestCase(StraightGun, false)]
    [TestCase(SprayGun, true)]
    public async Task TestTheGunsSpreadCounts(string gun, bool held)
    {
        var (entManager, gridUid, shooterUid) = await SetUpShooter(gun);
        await SpawnFriend(entManager, gridUid, 8f, 1.2f);

        await Pair.Server.WaitAssertion(() =>
            Assert.That(IsShotClear(entManager, gridUid, shooterUid), Is.EqualTo(!held)));
    }

    /// <summary>
    ///     A friend just off the line, walking into it: held for where it will be by the time the round gets there.
    /// </summary>
    [TestCase(4f, true)]
    [TestCase(0f, false)]
    public async Task TestAFriendWalkingIntoTheWayHoldsTheShot(float speed, bool held)
    {
        var (entManager, gridUid, shooterUid) = await SetUpShooter(StraightGun);
        var friendUid = await SpawnFriend(entManager, gridUid, 5f, -0.8f);

        await Pair.Server.WaitAssertion(() =>
        {
            entManager.System<SharedPhysicsSystem>().SetLinearVelocity(friendUid, new Vector2(0f, speed));
            Assert.That(IsShotClear(entManager, gridUid, shooterUid), Is.EqualTo(!held));
        });
    }

    /// <summary>
    ///     A friend in the way of the aim: with no room off it, no shot; with room, the nearest clear direction, one that
    ///         misses the friend, and lands within that room of the aim.
    /// </summary>
    [Test]
    public async Task TestAClearDirectionIsFoundBesideAFriend()
    {
        var (entManager, gridUid, shooterUid) = await SetUpShooter(StraightGun);
        await SpawnFriend(entManager, gridUid, 3f, 0f);

        await Pair.Server.WaitAssertion(() =>
        {
            var fireLaneSystem = entManager.System<NpcSquadFireLaneSystem>();
            var transformSystem = entManager.System<SharedTransformSystem>();
            var from = transformSystem.GetMapCoordinates(shooterUid);
            var aim = transformSystem.ToMapCoordinates(new EntityCoordinates(gridUid, ThreatPosition));

            Assert.That(fireLaneSystem.TryGetClearShot(shooterUid, from, aim, tolerance: 0f, out _), Is.False, "straight at it is through the friend");
            Assert.That(fireLaneSystem.TryGetClearShot(shooterUid, from, aim, tolerance: 3f, out var shot), Is.True, "beside it is not");

            Assert.That((shot.Position - aim.Position).Length(), Is.GreaterThan(0f).And.LessThanOrEqualTo(3f));
            Assert.That(fireLaneSystem.IsShotClear(shooterUid, from, shot), Is.True, "and a round that way misses the friend");
        });
    }

    /// <summary>
    ///     A round that passes through its own side (a projectile from a <c>KsFactionCollisionShooter</c>) holds for nobody.
    /// </summary>
    [Test]
    public async Task TestARoundThroughItsOwnSideHoldsForNobody()
    {
        var (entManager, gridUid, shooterUid) = await SetUpShooter(StraightGun);
        await SpawnFriend(entManager, gridUid, 3f, 0f);

        await Pair.Server.WaitAssertion(() =>
        {
            entManager.AddComponent<Content.Shared._KS14.FactionCollision.KsFactionCollisionShooterComponent>(shooterUid);
            Assert.That(IsShotClear(entManager, gridUid, shooterUid), Is.True);
        });
    }

    /// <summary>
    ///     Live: the rest of a burst goes off by itself, after its first round. A friend that steps into the way meanwhile is
    ///         held for, round by round, and never hit.
    /// </summary>
    [Test]
    public async Task TestTheRestOfABurstHoldsForAFriendThatStepsIn()
    {
        var (entManager, gridUid, shooterUid) = await SetUpShooter(BurstGun);
        var friendUid = await SpawnFriend(entManager, gridUid, 3f, 4f);
        var probe = entManager.System<KsFriendlyFireProbeSystem>();
        var gunSystem = entManager.System<Content.Server.Weapons.Ranged.Systems.GunSystem>();
        var ammoAtStart = 0;

        await Pair.RunTicksSync(30); // for the gun to be ready, once in hand

        await Pair.Server.WaitPost(() =>
        {
            entManager.EnsureComponent<Content.Shared.NPC.ActiveNPCComponent>(shooterUid);
            probe.Hits.Clear();

            Assert.That(gunSystem.TryGetGun(shooterUid, out var gunEntity));
            ammoAtStart = GetAmmo(entManager, gunEntity.Owner);
            Assert.That(gunSystem.AttemptShoot(shooterUid, gunEntity, new EntityCoordinates(gridUid, ThreatPosition)), "the first round should go");
        });

        // Once the first round is past where it steps in, and before the next goes: a quarter second apart.
        await Pair.RunTicksSync(5);
        await Pair.Server.WaitPost(() =>
            entManager.System<SharedTransformSystem>().SetCoordinates(friendUid, new EntityCoordinates(gridUid, new Vector2(2.5f, 0.5f))));

        await Pair.RunTicksSync(30);

        await Pair.Server.WaitAssertion(() =>
        {
            Assert.That(gunSystem.TryGetGun(shooterUid, out var gunEntity));
            Assert.That(GetAmmo(entManager, gunEntity.Owner), Is.EqualTo(ammoAtStart - 1), "only the first round, before the friend stepped in");
            Assert.That(probe.Hits.Count(hit => hit.Target == friendUid), Is.Zero, "and the friend is never hit");
        });
    }

    /// <summary>
    ///     Holding fire counts once it has gone on for long enough where it stands, starts again where it moves to, and ends
    ///         with a clear shot.
    /// </summary>
    [Test]
    public async Task TestHoldingFireCountsWhereItStands()
    {
        var (entManager, gridUid, aUid, _) = await SetUpSquad(new Vector2i(3, 0));
        var fireLaneSystem = entManager.System<NpcSquadFireLaneSystem>();

        await Pair.Server.WaitAssertion(() =>
        {
            Assert.That(fireLaneSystem.NoteHeldFire(aUid), Is.False, "not yet");
            Assert.That(fireLaneSystem.IsHoldingFire(aUid), Is.False);
        });

        await Pair.RunSeconds((float) NpcSquadFireLaneSystem.HoldFireTime.TotalSeconds + 0.1f);

        await Pair.Server.WaitAssertion(() =>
        {
            Assert.That(fireLaneSystem.NoteHeldFire(aUid), Is.True, "held long enough where it stands");
            Assert.That(fireLaneSystem.IsHoldingFire(aUid), Is.True);

            entManager.System<SharedTransformSystem>().SetCoordinates(aUid, new EntityCoordinates(gridUid, new Vector2(0.5f, 2.5f)));
            Assert.That(fireLaneSystem.NoteHeldFire(aUid), Is.False, "somewhere new, the count starts again");

            fireLaneSystem.NoteClearShot(aUid);
            Assert.That(fireLaneSystem.IsHoldingFire(aUid), Is.False, "a clear shot ends it");
        });
    }

    private const string StraightGun = "KsFireLaneTestStraightGun";
    private const string SprayGun = "KsFireLaneTestSprayGun";
    private const string BurstGun = "KsFireLaneTestBurstGun";

    /// <summary>
    ///     A shooter, and guns: one dead straight, one with a 20 degree spread, and one that fires bursts of three.
    /// </summary>
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: KsFireLaneTestShooter
  components:
  - type: Damageable # the gun fires the rest of a burst as whoever holds it only if that can be hurt, as a mob can
  - type: Hands
  - type: ComplexInteraction
  - type: MobState
  - type: CombatMode
  - type: NpcFactionMember
    factions:
    - Syndicate

- type: entity
  id: KsFireLaneTestStraightGun
  components:
  - type: Item
  - type: Gun
    fireRate: 4
    minAngle: 0
    maxAngle: 0
    selectedMode: SemiAuto
    availableModes:
    - SemiAuto
  - type: BasicEntityAmmoProvider
    proto: BulletPistol
    capacity: 100
    count: 100

- type: entity
  parent: KsFireLaneTestStraightGun
  id: KsFireLaneTestSprayGun
  components:
  - type: Gun
    fireRate: 4
    minAngle: 20
    maxAngle: 20
    selectedMode: SemiAuto
    availableModes:
    - SemiAuto

- type: entity
  parent: KsFireLaneTestStraightGun
  id: KsFireLaneTestBurstGun
  components:
  - type: Gun
    fireRate: 4
    burstFireRate: 4
    shotsPerBurst: 3
    minAngle: 0
    maxAngle: 0
    selectedMode: Burst
    availableModes:
    - Burst
";

    private async Task<(IEntityManager EntManager, EntityUid GridUid, EntityUid ShooterUid)> SetUpShooter(string gun)
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var tileDefinitionManager = server.ResolveDependency<ITileDefinitionManager>();
        var map = await Pair.CreateTestMap();
        EntityUid gridUid = default, shooterUid = default;

        await server.WaitPost(() =>
        {
            gridUid = MakeGrid(entManager, tileDefinitionManager, map.MapId, map.Grid, new Vector2i(-5, -5), new Vector2i(16, 5)).Owner;
            shooterUid = SpawnAt(entManager, "KsFireLaneTestShooter", gridUid, 0, 0);

            var handsSystem = entManager.System<SharedHandsSystem>();
            handsSystem.AddHand(shooterUid, "right", HandLocation.Right);

            var gunUid = entManager.SpawnEntity(gun, entManager.GetComponent<TransformComponent>(shooterUid).Coordinates);
            Assert.That(handsSystem.TryPickup(shooterUid, gunUid, "right"));
            handsSystem.TrySetActiveHand(shooterUid, "right");
        });

        return (entManager, gridUid, shooterUid);
    }

    /// <summary>
    ///     A friend of the shooter's, <paramref name="x"/>, <paramref name="y"/> tiles from its middle; on the next tick, so the
    ///         checks see it.
    /// </summary>
    private async Task<EntityUid> SpawnFriend(IEntityManager entManager, EntityUid gridUid, float x, float y)
    {
        EntityUid friendUid = default;
        await Pair.Server.WaitPost(() =>
            friendUid = entManager.SpawnEntity(SyndicateMob, new EntityCoordinates(gridUid, new Vector2(0.5f + x, 0.5f + y))));
        await Pair.RunTicksSync(1);
        return friendUid;
    }

    private static bool IsShotClear(IEntityManager entManager, EntityUid gridUid, EntityUid shooterUid)
    {
        var transformSystem = entManager.System<SharedTransformSystem>();
        return entManager.System<NpcSquadFireLaneSystem>().IsShotClear(shooterUid,
            transformSystem.GetMapCoordinates(shooterUid),
            transformSystem.ToMapCoordinates(new EntityCoordinates(gridUid, ThreatPosition)));
    }

    private static int GetAmmo(IEntityManager entManager, EntityUid gunUid)
    {
        var ev = new Content.Shared.Weapons.Ranged.Events.GetAmmoCountEvent();
        entManager.EventBus.RaiseLocalEvent(gunUid, ref ev);
        return ev.Count;
    }

    private async Task<(IEntityManager EntManager, EntityUid GridUid, EntityUid AUid, EntityUid BUid)> SetUpSquad(Vector2i bAt)
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var tileDefinitionManager = server.ResolveDependency<ITileDefinitionManager>();
        var map = await Pair.CreateTestMap();
        EntityUid gridUid = default, aUid = default, bUid = default;

        await server.WaitPost(() =>
        {
            gridUid = MakeGrid(entManager, tileDefinitionManager, map.MapId, map.Grid, new Vector2i(-5, -5), new Vector2i(12, 5)).Owner;
            aUid = SpawnAt(entManager, SyndicateMob, gridUid, 0, 0);
            bUid = SpawnAt(entManager, SyndicateMob, gridUid, bAt.X, bAt.Y);
        });

        await Pair.RunTicksSync(SquadUpdateTicks);

        await server.WaitAssertion(() =>
            Assert.That(entManager.System<NpcSquadSystem>().TryGetSquad(aUid, out var squad) && squad.Value.Comp.Members.Count == 2,
                "A and B should share a squad"));

        return (entManager, gridUid, aUid, bUid);
    }

    private async Task<float> GetPenaltyForA(Vector2 candidate, bool aimAtThreat)
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var tileDefinitionManager = server.ResolveDependency<ITileDefinitionManager>();
        var squadSystem = entManager.System<NpcSquadSystem>();
        var fireLaneSystem = entManager.System<NpcSquadFireLaneSystem>();
        var transformSystem = entManager.System<SharedTransformSystem>();
        var map = await Pair.CreateTestMap();

        EntityUid gridUid = default;
        EntityUid aUid = default;

        await server.WaitPost(() =>
        {
            gridUid = MakeGrid(entManager, tileDefinitionManager, map.MapId, map.Grid, new Vector2i(-5, -5), new Vector2i(12, 5)).Owner;
            aUid = SpawnAt(entManager, SyndicateMob, gridUid, 0, 0);
            SpawnAt(entManager, SyndicateMob, gridUid, 3, 0);
        });

        await Pair.RunTicksSync(SquadUpdateTicks);

        var penalty = 0f;
        await server.WaitPost(() =>
        {
            Assert.That(squadSystem.TryGetSquad(aUid, out var squad) && squad.Value.Comp.Members.Count == 2,
                "A and B should share a squad");

            var threatCoordinates = new EntityCoordinates(gridUid, ThreatPosition);
            squadSystem.ReportThreat(aUid, threatCoordinates);

            penalty = fireLaneSystem.GetFireLanePenalty(
                aUid,
                transformSystem.ToMapCoordinates(new EntityCoordinates(gridUid, candidate)),
                aimAtThreat ? transformSystem.ToMapCoordinates(threatCoordinates) : null);
        });

        return penalty;
    }
}
