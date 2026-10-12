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
