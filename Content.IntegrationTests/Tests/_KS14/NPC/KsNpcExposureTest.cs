#nullable enable
using System.Collections.Generic;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.NPC.Exposure;
using Content.Server._KS14.NPC.HTN.PrimitiveTasks.Operators;
using Content.Server._KS14.NPC.Perception;
using Content.Server.NPC;
using Content.Server.NPC.Queries.Curves;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.UnitTesting.Pool;
using static Content.IntegrationTests.Tests._KS14.NPC.KsNpcSquadTestHelpers;

namespace Content.IntegrationTests.Tests._KS14.NPC;

/// <summary>
///     Exposure (<see cref="NpcExposureSystem"/>): how much of a threat's approach - where it is, and where it could
///         step to - can see a spot. The scene is a wall with the threat south of it: one spot just round the wall's
///         east end, hidden from where the threat stands but not from a few steps east of it, and one deep behind the
///         wall's middle, hidden from all of it.
/// </summary>
public sealed class KsNpcExposureTest : GameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    private static readonly Vector2 ThreatPosition = new(-2.5f, -3.5f);
    private static readonly Vector2 RoundTheCorner = new(1.5f, 1.5f);
    private static readonly Vector2 DeepCover = new(-2.5f, 2.5f);

    /// <summary>
    ///     Both spots are out of the threat's sight where it stands. Only the one deep behind the wall stays out of it
    ///         when the threat steps out: the one round the corner is seen from part of its approach.
    /// </summary>
    [Test]
    public async Task TestRoundTheCornerIsMoreExposedThanDeepCover()
    {
        var (entManager, gridUid, walkerUid) = await SetUpWall();
        var exposureSystem = entManager.System<NpcExposureSystem>();
        var lineOfSightSystem = entManager.System<NpcLineOfSightSystem>();
        var transformSystem = entManager.System<SharedTransformSystem>();

        await Pair.Server.WaitAssertion(() =>
        {
            MapCoordinates At(Vector2 position) => transformSystem.ToMapCoordinates(new EntityCoordinates(gridUid, position));

            Assert.Multiple(() =>
            {
                Assert.That(lineOfSightSystem.InLineOfSight(At(ThreatPosition), At(RoundTheCorner), 15f), Is.False,
                    "the spot round the corner should be hidden from where the threat stands");
                Assert.That(lineOfSightSystem.InLineOfSight(At(ThreatPosition), At(DeepCover), 15f), Is.False,
                    "the spot deep behind the wall should be hidden from where the threat stands");
            });

            var probes = new List<MapCoordinates>();
            exposureSystem.GetApproachProbes(walkerUid, new EntityCoordinates(gridUid, ThreatPosition), reach: 4, maxProbes: 12, probes);

            var roundTheCorner = exposureSystem.GetExposure(At(RoundTheCorner), probes, 15f);
            var deepCover = exposureSystem.GetExposure(At(DeepCover), probes, 15f);

            Assert.Multiple(() =>
            {
                Assert.That(probes, Has.Count.EqualTo(12), "the threat and eleven places it could step to");
                Assert.That(roundTheCorner, Is.GreaterThan(0f), "a few steps east, the threat sees round the corner");
                Assert.That(deepCover, Is.Zero, "nowhere a few steps away sees through the wall");
            });
        });
    }

    /// <summary>
    ///     A retreat-style search - away from the threat's sight, near where it started - that weighs exposure picks
    ///         somewhere hidden from the threat's whole approach. Without it, the nearest spot out of the threat's
    ///         sight wins: the one just round the corner.
    /// </summary>
    [TestCase(true)]
    [TestCase(false)]
    public async Task TestRetreatPrefersDeepCover(bool weighExposure)
    {
        var (entManager, gridUid, walkerUid) = await SetUpWall();
        var tacticalPositionOperator = new TacticalPositionOperator
        {
            ReferenceCoordinatesKey = "KsTestReference",
            MaxRange = 6f,
            AvoidFireLanes = false,
            LosReferenceCoordinatesKey = "KsTestThreat",
            LosRadius = 15f,
            LosCurve = new InverseBoolCurve(),
            ExposureReferenceCoordinatesKey = weighExposure ? "KsTestThreat" : null,
            ExposureReach = 4,
            ExposureCurve = new QuadraticCurve { Exponent = 2f },
            // Clearly prefers being near: without exposure, the hidden spot nearest the start wins.
            DistanceCurve = new QuadraticCurve { Slope = -1f, Exponent = 1f, YOffset = 1f },
        };

        System.Threading.Tasks.Task<(bool Valid, Dictionary<string, object>? Effects)> planTask = default!;

        await Pair.Server.WaitPost(() =>
        {
            entManager.EntitySysManager.DependencyCollection.InjectDependencies(tacticalPositionOperator, oneOff: true);

            var blackboard = new NPCBlackboard();
            blackboard.SetValue(NPCBlackboard.Owner, walkerUid);
            blackboard.SetValue("VisionRadius", 10f);
            blackboard.SetValue("KsTestReference", new EntityCoordinates(gridUid, RoundTheCorner));
            blackboard.SetValue("KsTestThreat", new EntityCoordinates(gridUid, ThreatPosition));
            planTask = tacticalPositionOperator.Plan(blackboard, default);
        });

        for (var i = 0; i < 120 && !planTask.IsCompleted; i++)
        {
            await Pair.RunTicksSync(1);
        }

        Assert.That(planTask.IsCompletedSuccessfully, "the position search never finished");
        var (valid, effects) = await planTask;
        Assert.That(valid, "a position should be found");

        await Pair.Server.WaitAssertion(() =>
        {
            var exposureSystem = entManager.System<NpcExposureSystem>();
            var transformSystem = entManager.System<SharedTransformSystem>();
            var chosen = (EntityCoordinates) effects![tacticalPositionOperator.KeyCoordinates];

            var probes = new List<MapCoordinates>();
            exposureSystem.GetApproachProbes(walkerUid, new EntityCoordinates(gridUid, ThreatPosition), reach: 4, maxProbes: 12, probes);
            var exposure = exposureSystem.GetExposure(transformSystem.ToMapCoordinates(chosen), probes, 15f);

            if (weighExposure)
                Assert.That(exposure, Is.Zero, $"a search weighing exposure picked {chosen.Position}, seen from part of the approach");
            else
                Assert.That(exposure, Is.GreaterThan(0f), $"without exposure, the nearest hidden spot - round the corner - should win, not {chosen.Position}");
        });
    }

    /// <summary>
    ///     Exposure checks come out of a budget shared by every NPC and refilled each tick: what one search spends, the
    ///         next on the same tick does not have.
    /// </summary>
    [Test]
    public async Task TestExposureBudgetIsSpentAndRefilled()
    {
        await OverrideCVar(Content.IntegrationTests.Fixtures.Attributes.Side.Server, Content.Shared._KS14.CCVar.KsCCVars.NpcExposureRayBudget, 30);
        var (entManager, gridUid, walkerUid) = await SetUpWall();
        var exposureSystem = entManager.System<NpcExposureSystem>();
        var transformSystem = entManager.System<SharedTransformSystem>();
        var probes = new List<MapCoordinates>();

        await Pair.Server.WaitAssertion(() =>
        {
            exposureSystem.GetApproachProbes(walkerUid, new EntityCoordinates(gridUid, ThreatPosition), reach: 4, maxProbes: 12, probes);
            var spot = transformSystem.ToMapCoordinates(new EntityCoordinates(gridUid, DeepCover));

            Assert.That(exposureSystem.CanAfford(24), "a fresh tick has the whole budget");
            exposureSystem.GetExposure(spot, probes, 15f);
            exposureSystem.GetExposure(spot, probes, 15f);
            Assert.Multiple(() =>
            {
                Assert.That(exposureSystem.GetRaysLeft(), Is.EqualTo(6), "two spots, twelve probes each");
                Assert.That(exposureSystem.CanAfford(12), Is.False, "not enough left for a third");
            });
        });

        await Pair.RunTicksSync(1);

        await Pair.Server.WaitAssertion(() =>
            Assert.That(exposureSystem.GetRaysLeft(), Is.EqualTo(30), "the next tick has the whole budget again"));
    }

    /// <summary>
    ///     A search weighing exposure with no budget left either gives up, to try again later, or picks without
    ///         weighing it - the nearest hidden spot, round the corner - as it is set up to.
    /// </summary>
    [TestCase(true)]
    [TestCase(false)]
    public async Task TestOverBudgetSearchDefersOrDoesWithout(bool defer)
    {
        await OverrideCVar(Content.IntegrationTests.Fixtures.Attributes.Side.Server, Content.Shared._KS14.CCVar.KsCCVars.NpcExposureRayBudget, 0);
        var (entManager, gridUid, walkerUid) = await SetUpWall();
        var tacticalPositionOperator = new TacticalPositionOperator
        {
            ReferenceCoordinatesKey = "KsTestReference",
            MaxRange = 6f,
            AvoidFireLanes = false,
            LosReferenceCoordinatesKey = "KsTestThreat",
            LosRadius = 15f,
            LosCurve = new InverseBoolCurve(),
            ExposureReferenceCoordinatesKey = "KsTestThreat",
            ExposureCurve = new QuadraticCurve { Exponent = 2f },
            DistanceCurve = new QuadraticCurve { Slope = -1f, Exponent = 1f, YOffset = 1f },
            DeferWhenOverBudget = defer,
        };

        System.Threading.Tasks.Task<(bool Valid, Dictionary<string, object>? Effects)> planTask = default!;

        await Pair.Server.WaitPost(() =>
        {
            entManager.EntitySysManager.DependencyCollection.InjectDependencies(tacticalPositionOperator, oneOff: true);

            var blackboard = new NPCBlackboard();
            blackboard.SetValue(NPCBlackboard.Owner, walkerUid);
            blackboard.SetValue("VisionRadius", 10f);
            blackboard.SetValue("KsTestReference", new EntityCoordinates(gridUid, RoundTheCorner));
            blackboard.SetValue("KsTestThreat", new EntityCoordinates(gridUid, ThreatPosition));
            planTask = tacticalPositionOperator.Plan(blackboard, default);
        });

        for (var i = 0; i < 120 && !planTask.IsCompleted; i++)
        {
            await Pair.RunTicksSync(1);
        }

        Assert.That(planTask.IsCompletedSuccessfully, "the position search never finished");
        var (valid, effects) = await planTask;

        if (defer)
        {
            Assert.That(valid, Is.False, "a search that can wait should give up for now");
            return;
        }

        Assert.That(valid, "a search that cannot wait should still pick somewhere");

        await Pair.Server.WaitAssertion(() =>
        {
            var exposureSystem = entManager.System<NpcExposureSystem>();
            var transformSystem = entManager.System<SharedTransformSystem>();
            var chosen = (EntityCoordinates) effects![tacticalPositionOperator.KeyCoordinates];

            var probes = new List<MapCoordinates>();
            exposureSystem.GetApproachProbes(walkerUid, new EntityCoordinates(gridUid, ThreatPosition), reach: 4, maxProbes: 12, probes);
            Assert.That(exposureSystem.GetExposure(transformSystem.ToMapCoordinates(chosen), probes, 15f), Is.GreaterThan(0f),
                $"without exposure weighed, the nearest hidden spot - round the corner - should win, not {chosen.Position}");
        });
    }

    /// <summary>
    ///     Open floor with a wall along y = 0 from x = -9 to 0, and a mob to walk and plan as, parked out of the way.
    /// </summary>
    private async Task<(IEntityManager EntManager, EntityUid GridUid, EntityUid WalkerUid)> SetUpWall()
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var tileDefinitionManager = server.ResolveDependency<ITileDefinitionManager>();
        var map = await Pair.CreateTestMap();
        EntityUid gridUid = default;
        EntityUid walkerUid = default;

        await server.WaitPost(() =>
        {
            gridUid = MakeGrid(entManager, tileDefinitionManager, map.MapId, map.Grid, new Vector2i(-10, -10), new Vector2i(10, 10)).Owner;

            for (var x = -9; x <= 0; x++)
            {
                SpawnAt(entManager, "WallSolid", gridUid, x, 0);
            }

            walkerUid = SpawnAt(entManager, SyndicateMob, gridUid, 1, 1);
        });

        // For the navmesh to build.
        await Pair.RunTicksSync(90);

        return (entManager, gridUid, walkerUid);
    }
}
