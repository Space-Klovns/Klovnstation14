#nullable enable
using System.Collections.Generic;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.NPC.Exposure;
using Content.Server._KS14.NPC.HTN.PrimitiveTasks.Operators;
using Content.Server._KS14.NPC.Perception;
using Content.Server._KS14.NPC.Systems;
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
    ///     A firing-spot search, as the operative's: out in the open, east of the wall's end with the threat to the
    ///         west, every spot that sees the threat is seen from almost all of its approach, so exposure alone keeps it
    ///         where it stands. Weighing <see cref="TacticalPositionOperator.CoverStepCurve"/>, it takes a spot at the
    ///         wall's end that still sees the threat, with floor out of its sight a step away.
    /// </summary>
    [TestCase(true)]
    [TestCase(false)]
    public async Task TestFiringSpotTakesTheCorner(bool coverStep)
    {
        var (entManager, gridUid, walkerUid) = await SetUpWall();
        var standing = new Vector2(4.5f, -3.5f);
        var tacticalPositionOperator = new TacticalPositionOperator
        {
            ReferenceCoordinatesKey = "KsTestReference",
            MaxRange = 6f,
            MaxCandidates = 48,
            AvoidFireLanes = false,
            DistanceCurve = new QuadraticCurve { Slope = -1f, Exponent = 1f, YOffset = 1f },
            LosReferenceCoordinatesKey = "KsTestThreat",
            LosRadius = 9f,
            LosCurve = new BoolCurve(),
            CoverStepCurve = coverStep ? new QuadraticCurve { Slope = 0.7f, YOffset = 0.3f } : null,
            ExposureReferenceCoordinatesKey = "KsTestThreat",
            ExposureReach = 3,
            ExposureProbes = 10,
            ExposureCurve = new QuadraticCurve { Slope = 0.7f, YOffset = 0.3f },
            ClaimClearanceRadius = 1.5f,
        };

        System.Threading.Tasks.Task<(bool Valid, Dictionary<string, object>? Effects)> planTask = default!;

        await Pair.Server.WaitPost(() =>
        {
            entManager.EntitySysManager.DependencyCollection.InjectDependencies(tacticalPositionOperator, oneOff: true);

            var blackboard = new NPCBlackboard();
            blackboard.SetValue(NPCBlackboard.Owner, walkerUid);
            blackboard.SetValue("VisionRadius", 17.5f);
            blackboard.SetValue("KsTestReference", new EntityCoordinates(gridUid, standing));
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
            var lineOfSightSystem = entManager.System<NpcLineOfSightSystem>();
            var transformSystem = entManager.System<SharedTransformSystem>();
            var chosen = ((EntityCoordinates) effects![tacticalPositionOperator.KeyCoordinates]).Position;
            var threatMapCoordinates = transformSystem.ToMapCoordinates(new EntityCoordinates(gridUid, ThreatPosition));

            Assert.That(lineOfSightSystem.InLineOfSight(transformSystem.ToMapCoordinates(new EntityCoordinates(gridUid, chosen)), threatMapCoordinates, 9.5f),
                $"{chosen} should see the threat");

            // Floor next to it, out of the threat's sight. The wall is the only thing on the grid.
            var coverAStepAway = false;
            var chosenTile = new Vector2i((int) MathF.Floor(chosen.X), (int) MathF.Floor(chosen.Y));
            for (var x = -1; x <= 1; x++)
            {
                for (var y = -1; y <= 1; y++)
                {
                    var tile = chosenTile + new Vector2i(x, y);
                    if (tile == chosenTile || tile.Y == 0 && tile.X is >= -9 and <= 0)
                        continue;

                    var tileCentre = new Vector2(tile.X + 0.5f, tile.Y + 0.5f);
                    coverAStepAway |= !lineOfSightSystem.InLineOfSight(
                        transformSystem.ToMapCoordinates(new EntityCoordinates(gridUid, tileCentre)), threatMapCoordinates, 15f);
                }
            }

            if (coverStep)
                Assert.That(coverAStepAway, $"weighing cover a step away, it should take the corner, not {chosen}");
            else
                Assert.That(coverAStepAway, Is.False, $"without it, nothing near is much better than where it stands, yet it picked {chosen}");
        });
    }

    /// <summary>
    ///     <see cref="TacticalPositionOperator.ExclusiveClaims"/>: every spot within reach lies inside one claim. A
    ///         squadmate's claim rules them all out, so no spot is found; a soft claim only marks them down, and one is.
    ///         The searcher's own claim never rules out its own spots.
    /// </summary>
    [TestCase(true, false, false)]
    [TestCase(false, false, true)]
    [TestCase(true, true, true)]
    public async Task TestExclusiveClaimRulesOutClaimedSpots(bool exclusive, bool ownClaim, bool expectSpot)
    {
        var (entManager, gridUid, walkerUid) = await SetUpWall();
        var tacticalPositionOperator = new TacticalPositionOperator
        {
            ReferenceCoordinatesKey = "KsTestReference",
            MaxRange = 1.5f, // a few tiles round the walker, all inside the claim
            AvoidFireLanes = false,
            ClaimClearanceRadius = 3f,
            ExclusiveClaims = exclusive,
            DistanceCurve = new QuadraticCurve { Slope = -1f, Exponent = 1f, YOffset = 1f },
        };

        System.Threading.Tasks.Task<(bool Valid, Dictionary<string, object>? Effects)> planTask = default!;

        await Pair.Server.WaitPost(() =>
        {
            entManager.EntitySysManager.DependencyCollection.InjectDependencies(tacticalPositionOperator, oneOff: true);

            var walkerCoordinates = entManager.GetComponent<TransformComponent>(walkerUid).Coordinates;
            var claimantUid = ownClaim ? walkerUid : SpawnAt(entManager, SyndicateMob, gridUid, 6, 6);
            entManager.System<NpcTacticalPositionClaimSystem>().Claim(claimantUid, walkerCoordinates, System.TimeSpan.FromSeconds(30), clearanceRadius: 3f);

            var blackboard = new NPCBlackboard();
            blackboard.SetValue(NPCBlackboard.Owner, walkerUid);
            blackboard.SetValue("VisionRadius", 10f);
            blackboard.SetValue("KsTestReference", walkerCoordinates);
            planTask = tacticalPositionOperator.Plan(blackboard, default);
        });

        for (var i = 0; i < 120 && !planTask.IsCompleted; i++)
        {
            await Pair.RunTicksSync(1);
        }

        Assert.That(planTask.IsCompletedSuccessfully, "the position search never finished");
        var (valid, _) = await planTask;
        Assert.That(valid, Is.EqualTo(expectSpot), expectSpot
            ? "a spot should be found: only marked down, or claimed by the searcher itself"
            : "every spot in reach is inside a squadmate's exclusive claim, so none should be found");
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
    /// <summary>
    ///     The shadow of the wall runs on across the floor, north-east of its end. <see cref="TacticalPositionOperator.CoverStepCurve"/>
    ///         likes the edge of that as much as the wall's end, being a step from out of sight, with nothing there to stand
    ///         behind. <see cref="TacticalPositionOperator.CoverOnlyKey"/> does not, and with no better spot in reach finds
    ///         nothing rather than settle for open floor.
    /// </summary>
    [TestCase(false)]
    [TestCase(true)]
    public async Task TestCoverOnlyTurnsDownTheEdgeOfAShadow(bool coverOnly)
    {
        var (entManager, gridUid, walkerUid) = await SetUpWall();
        var searchOperator = CornerSearch(maxRange: 3f);

        var effects = await Search(entManager, gridUid, walkerUid, searchOperator, new Vector2(6.5f, 5.5f), blackboard =>
        {
            if (coverOnly)
                blackboard.SetValue("KsTestCoverOnly", true);
        });

        Assert.That(effects is not null, Is.Not.EqualTo(coverOnly), "only a search that will take open floor should find anything");
    }

    /// <summary>
    ///     With a real corner in reach, that is what <see cref="TacticalPositionOperator.CoverOnlyKey"/> finds: a spot beside
    ///         the wall's end.
    /// </summary>
    [Test]
    public async Task TestCoverOnlyTakesACorner()
    {
        var (entManager, gridUid, walkerUid) = await SetUpWall();
        var searchOperator = CornerSearch(maxRange: 6f);

        var effects = await Search(entManager, gridUid, walkerUid, searchOperator, new Vector2(4.5f, -3.5f),
            blackboard => blackboard.SetValue("KsTestCoverOnly", true));

        Assert.That(effects, Is.Not.Null, "the wall's end is in reach");
        var chosen = ((EntityCoordinates) effects![searchOperator.KeyCoordinates]).Position;
        Assert.That(MathF.Max(MathF.Abs(chosen.X - 0.5f), MathF.Abs(chosen.Y - 0.5f)), Is.LessThanOrEqualTo(1.5f), $"{chosen} is not beside the wall's end");
    }

    /// <summary>
    ///     <see cref="TacticalPositionOperator.LosRadiusKey"/> reaches further than the task's own radius, for an owner that
    ///         has it.
    /// </summary>
    [TestCase(false)]
    [TestCase(true)]
    public async Task TestLosRadiusGoesByTheMob(bool keyed)
    {
        var (entManager, gridUid, walkerUid) = await SetUpWall();
        var searchOperator = CornerSearch(maxRange: 3f);
        searchOperator.LosRadius = 2f;
        searchOperator.LosRadiusKey = "KsTestLosRadius";

        var effects = await Search(entManager, gridUid, walkerUid, searchOperator, new Vector2(6.5f, -3.5f), blackboard =>
        {
            if (keyed)
                blackboard.SetValue("KsTestLosRadius", 15f);
        });

        Assert.That(effects is not null, Is.EqualTo(keyed), "the threat is further off than the task's own radius");
    }

    /// <summary>
    ///     <see cref="TacticalPositionOperator.MaxCandidatesKey"/> sets how many spots are weighed, for an owner that has it.
    ///         Limited to the nearest, the furthest spot in reach is not among them.
    /// </summary>
    [TestCase(false)]
    [TestCase(true)]
    public async Task TestCandidatesGoByTheMob(bool keyed)
    {
        var (entManager, gridUid, walkerUid) = await SetUpWall();
        var standing = new Vector2(4.5f, -3.5f);
        var searchOperator = new TacticalPositionOperator
        {
            ReferenceCoordinatesKey = "KsTestReference",
            MaxRange = 4f,
            MaxCandidates = 64,
            MaxCandidatesKey = "KsTestCandidates",
            AvoidFireLanes = false,
            DistanceCurve = new QuadraticCurve { Slope = 1f, Exponent = 1f },
        };

        var effects = await Search(entManager, gridUid, walkerUid, searchOperator, standing, blackboard =>
        {
            if (keyed)
                blackboard.SetValue("KsTestCandidates", 2f);
        });

        Assert.That(effects, Is.Not.Null);
        var travelled = (((EntityCoordinates) effects![searchOperator.KeyCoordinates]).Position - standing).Length();
        Assert.That(travelled < 2f, Is.EqualTo(keyed), $"it picked a spot {travelled} away");
    }

    /// <summary>
    ///     <see cref="TacticalPositionOperator.WallCurve"/> needs no threat: open floor scores nothing, beside a wall does, and
    ///         a corner of two walls does better. Here the corner is beside the wall's end, and the distance is left out of it.
    /// </summary>
    [Test]
    public async Task TestWallCurveTakesTheCorner()
    {
        var (entManager, gridUid, walkerUid) = await SetUpWall();

        await Pair.Server.WaitPost(() => SpawnAt(entManager, TestWall, gridUid, 0, 1));
        await Pair.RunTicksSync(90); // for the navmesh to take it in

        var searchOperator = WallSearch(maxRange: 6f);
        var effects = await Search(entManager, gridUid, walkerUid, searchOperator, new Vector2(-2.5f, 3.5f));

        Assert.That(effects, Is.Not.Null);
        var chosen = ((EntityCoordinates) effects![searchOperator.KeyCoordinates]).Position;
        Assert.That(chosen, Is.EqualTo(new Vector2(-0.5f, 1.5f)), "the corner of the two walls");
    }

    /// <summary>
    ///     With only open floor in reach there is nothing to pick.
    /// </summary>
    [Test]
    public async Task TestWallCurveTurnsDownOpenFloor()
    {
        var (entManager, gridUid, walkerUid) = await SetUpWall();

        var effects = await Search(entManager, gridUid, walkerUid, WallSearch(maxRange: 2f), new Vector2(5.5f, 5.5f));

        Assert.That(effects, Is.Null);
    }

    /// <summary>
    ///     A firing-spot search as in the operative's HTN, with its switches for the mob to set.
    /// </summary>
    private static TacticalPositionOperator CornerSearch(float maxRange)
    {
        return new TacticalPositionOperator
        {
            ReferenceCoordinatesKey = "KsTestReference",
            MaxRange = maxRange,
            MaxCandidates = 128,
            AvoidFireLanes = false,
            DistanceCurve = new QuadraticCurve { Slope = -1f, Exponent = 1f, YOffset = 1f },
            LosReferenceCoordinatesKey = "KsTestThreat",
            LosRadius = 15f,
            LosCurve = new BoolCurve(),
            CoverOnlyKey = "KsTestCoverOnly",
            CoverStepCurve = new QuadraticCurve { Slope = 0.7f, YOffset = 0.3f },
            ClaimClearanceRadius = 1.5f,
        };
    }

    private static TacticalPositionOperator WallSearch(float maxRange)
    {
        return new TacticalPositionOperator
        {
            ReferenceCoordinatesKey = "KsTestReference",
            MaxRange = maxRange,
            MaxCandidates = 256,
            AvoidFireLanes = false,
            DistanceCurve = new QuadraticCurve { Slope = 0f, YOffset = 1f },
            WallCurve = new QuadraticCurve { Slope = 1f },
        };
    }

    /// <summary>
    ///     Runs <paramref name="searchOperator"/> from <paramref name="standing"/>, with the threat where the scene has it.
    ///         Returns its effects, or null if it found nothing.
    /// </summary>
    private async Task<Dictionary<string, object>?> Search(IEntityManager entManager,
        EntityUid gridUid,
        EntityUid walkerUid,
        TacticalPositionOperator searchOperator,
        Vector2 standing,
        System.Action<NPCBlackboard>? stage = null)
    {
        System.Threading.Tasks.Task<(bool Valid, Dictionary<string, object>? Effects)> planTask = default!;

        await Pair.Server.WaitPost(() =>
        {
            entManager.EntitySysManager.DependencyCollection.InjectDependencies(searchOperator, oneOff: true);

            var blackboard = new NPCBlackboard();
            blackboard.SetValue(NPCBlackboard.Owner, walkerUid);
            blackboard.SetValue("VisionRadius", 17.5f);
            blackboard.SetValue("KsTestReference", new EntityCoordinates(gridUid, standing));
            blackboard.SetValue("KsTestThreat", new EntityCoordinates(gridUid, ThreatPosition));
            stage?.Invoke(blackboard);
            planTask = searchOperator.Plan(blackboard, default);
        });

        for (var i = 0; i < 120 && !planTask.IsCompleted; i++)
        {
            await Pair.RunTicksSync(1);
        }

        Assert.That(planTask.IsCompletedSuccessfully, "the position search never finished");
        var (valid, effects) = await planTask;
        return valid ? effects : null;
    }

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
                SpawnAt(entManager, TestWall, gridUid, x, 0);
            }

            walkerUid = SpawnAt(entManager, SyndicateMob, gridUid, 1, 1);
        });

        // For the navmesh to build.
        await Pair.RunTicksSync(90);

        return (entManager, gridUid, walkerUid);
    }
}
