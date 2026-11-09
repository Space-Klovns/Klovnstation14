#nullable enable
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server._KS14.NPC.Squad;
using Content.Shared._KS14.CCVar;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
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
    ///     When shooting: A holds fire for B in the way, or on top of it, behind as well as in front, but not for B some way
    ///         behind. Nor for a squadmate its shot would pass through: a projectile from a <c>KsFactionCollisionShooter</c>.
    /// </summary>
    [TestCase(3, 0, true)] // in the way
    [TestCase(11, 0, true)] // right behind what it shoots at
    [TestCase(-3, 0, false)] // some way behind A
    [TestCase(0, 4, false)] // off to the side
    public async Task TestASquadmateInTheWayHoldsTheShot(int bX, int bY, bool held)
    {
        var (entManager, gridUid, aUid, _) = await SetUpSquad(new Vector2i(bX, bY));

        await Pair.Server.WaitAssertion(() =>
        {
            var fireLaneSystem = entManager.System<NpcSquadFireLaneSystem>();
            var transformSystem = entManager.System<SharedTransformSystem>();
            var from = transformSystem.GetMapCoordinates(aUid);
            var to = transformSystem.ToMapCoordinates(new EntityCoordinates(gridUid, ThreatPosition));

            Assert.That(fireLaneSystem.IsFriendInLineOfFire(aUid, from, to), Is.EqualTo(held));

            // A projectile that goes through its own side holds for nobody.
            entManager.AddComponent<Content.Shared._KS14.FactionCollision.KsFactionCollisionShooterComponent>(aUid);
            Assert.That(fireLaneSystem.IsFriendInLineOfFire(aUid, from, to), Is.False, "a shot through its own side hits nobody");
        });
    }

    /// <summary>
    ///     B just behind A, overlapping it: a bullet reaches back behind where it is spawned, and a shot from A hits B the
    ///         moment it is fired. Seen live, at 0.6 tiles. The lane rule used to look only in front.
    /// </summary>
    [Test]
    public async Task TestASquadmateOnTopHoldsTheShot()
    {
        var (entManager, gridUid, aUid, bUid) = await SetUpSquad(new Vector2i(-3, 0));

        await Pair.Server.WaitAssertion(() =>
        {
            var transformSystem = entManager.System<SharedTransformSystem>();
            transformSystem.SetCoordinates(bUid, new EntityCoordinates(gridUid, new Vector2(-0.1f, 0.5f)));

            Assert.That(entManager.System<NpcSquadFireLaneSystem>().IsFriendInLineOfFire(aUid,
                transformSystem.GetMapCoordinates(aUid),
                transformSystem.ToMapCoordinates(new EntityCoordinates(gridUid, ThreatPosition))), Is.True);
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
