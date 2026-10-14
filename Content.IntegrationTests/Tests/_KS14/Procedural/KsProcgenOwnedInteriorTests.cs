#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Shared._KS14.Procedural;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.Procedural;

[TestOf(typeof(KsProcgenOwnedInteriorPlanner))]
public sealed class KsProcgenOwnedInteriorTests : GameTest
{
    private static readonly KsProcgenOwnedPlanBudgets Budgets = new() { PreferredMaximumRoomCells = 64 };

    [Test]
    public async Task LShapeCompletesStrictSizeGoalsWithoutAddingMissingCornerTiles()
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var fixture = Fixture(tiny: true);
            fixture.Request.Shape.Cells = Enumerable.Range(0, 8).SelectMany(x => Enumerable.Range(0, x >= 6 ? 2 : 4)
                .Select(y => new Vector2i(x, y))).ToList();
            fixture.Request.SizeMix = new List<KsProcgenRoomSizeGoal>
            {
                new() { Id = "Large", MinCells = 16, MaxCells = 16 },
                new() { Id = "Small", MinCells = 9, MaxCells = 9 },
            };
            fixture.Request.FallbackPolicy.AllowMergedPartition = false;
            fixture.Request.FallbackPolicy.AllowRoomSizeShortfall = false;
            var manager = Pair.Server.ResolveDependency<IPrototypeManager>();
            var before = SEntMan.GetEntities().ToHashSet();
            var themeId = "KsProcgenEmptyOfficeThemeFixture";
            var budgets = Budgets with { UseSizeGoals = true, MaximumPartitionMerges = 0,
                MaximumConfigurationProbes = 1, MaximumLayoutProbes = 1 };
            var result = Plan(manager, fixture, budgets: budgets, themeId: themeId);
            AssertCandidate(result);
            Assert.That(result.Partition!.SizeGoalSubdivisionUsed, Is.True);
            Assert.That(result.Themes!.Regions.Select(region => region.FloorCells.Count).Order(), Is.EqualTo(new[] { 9, 16 }));
            Assert.That(result.SizeMix.All(outcome => outcome.AchievedCount == outcome.RequestedCount), Is.True);
            Assert.That(result.Partition.FloorCells.Concat(result.Partition.WallCells).ToHashSet()
                .SetEquals(fixture.Request.Shape.Cells), Is.True);
            var exact = Plan(manager, fixture, budgets: budgets with
            {
                MaximumReportedGraphExpansions = result.ExpandedCells,
                MaximumPartitionSeamProbes = result.PartitionSeamProbes,
                MaximumOrderProbes = result.OrderProbes,
                MaximumFurnishingProbes = Math.Max(1, result.FurnishingProbes),
                MaximumLightingFixtures = result.FixtureSelections,
            }, themeId: themeId);
            AssertCandidate(exact);
            Assert.That(exact.SemanticHash, Is.EqualTo(result.SemanticHash));
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    [Test]
    public async Task ReflectedSizeGoalsCompleteStrictInteriorAroundProtectedSeams()
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var fixture = Fixture(tiny: true);
            fixture.Request.Shape.Cells = Enumerable.Range(0, 10).SelectMany(x => Enumerable.Range(0, 4)
                .Select(y => new Vector2i(x, y))).ToList();
            fixture.Request.Shape.PreservedCells = new List<Vector2i>
                { new(6, 0), new(6, 1), new(7, 0), new(7, 1), new(0, 2), new(1, 2) };
            fixture.Request.EntranceDomain!.Configurations[0].Groups[0].RootCells = new List<Vector2i>
                { new(6, 0), new(7, 0), new(0, 2), new(1, 2) };
            fixture.Request.SizeMix = new List<KsProcgenRoomSizeGoal>
            {
                new() { Id = "Large", MinCells = 20, MaxCells = 28 },
                new() { Id = "Small", MinCells = 5, MaxCells = 13 },
            };
            fixture.Request.FallbackPolicy.AllowMergedPartition = false;
            fixture.Request.FallbackPolicy.AllowRoomSizeShortfall = false;
            fixture = fixture with { Context = fixture.Context with
                { ExistingCleanDomainCells = fixture.Request.Shape.PreservedCells.ToHashSet() } };
            var manager = Pair.Server.ResolveDependency<IPrototypeManager>();
            var before = SEntMan.GetEntities().ToHashSet();
            var themeId = "KsProcgenEmptyOfficeThemeFixture";
            var budgets = Budgets with { UseSizeGoals = true, MaximumPartitionMerges = 0,
                MaximumConfigurationProbes = 1, MaximumLayoutProbes = 1 };
            var result = Plan(manager, fixture, budgets: budgets, themeId: themeId);
            AssertCandidate(result);
            Assert.That(result.Partition!.SizeGoalAttempts.Last().Reflected, Is.True);
            Assert.That(result.Partition.SizeGoalAttempts.Last().AcrossX, Is.True);
            Assert.That(result.Partition.SizeGoalAttempts.Where(attempt => !attempt.Reflected)
                .All(attempt => attempt.Status == KsProcgenPartitionStatus.OpenFallback), Is.True);
            Assert.That(result.Themes!.Regions.Select(region => region.FloorCells.Count).Order(), Is.EqualTo(new[] { 12, 25 }));
            Assert.That(result.SizeMix.All(outcome => outcome.AchievedCount == outcome.RequestedCount), Is.True);
            Assert.That(fixture.Request.Shape.PreservedCells.All(result.Partition.FloorCells.Contains), Is.True);
            var exact = Plan(manager, fixture, budgets: budgets with
            {
                MaximumReportedGraphExpansions = result.ExpandedCells,
                MaximumPartitionSeamProbes = result.PartitionSeamProbes,
                MaximumOrderProbes = result.OrderProbes,
                MaximumFurnishingProbes = Math.Max(1, result.FurnishingProbes),
                MaximumLightingFixtures = result.FixtureSelections,
            }, themeId: themeId);
            AssertCandidate(exact);
            Assert.That(exact.SemanticHash, Is.EqualTo(result.SemanticHash));
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    [Test]
    public async Task ProtectedNetworkReassignsRoomQuotasBeforeStrictInteriorAudit()
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var fixture = Fixture(tiny: true);
            fixture.Request.Shape.Cells = Enumerable.Range(0, 8).Concat(Enumerable.Range(20, 8))
                .SelectMany(x => Enumerable.Range(0, 4).Select(y => new Vector2i(x, y))).ToList();
            fixture.Request.Shape.PreservedCells = fixture.Request.Shape.Cells.Where(cell => cell.X >= 20).ToList();
            fixture.Request.EntranceDomain!.Entrances.Add(new()
                { PortId = "B1", ThresholdCells = new List<Vector2i> { new(19, 0) }, InwardNormal = new(1, 0) });
            fixture.Request.EntranceDomain.Configurations[0].Groups.Add(new()
                { Id = "B", Ports = new List<string> { "B1" } });
            fixture.Request.SizeMix = new List<KsProcgenRoomSizeGoal>
                { new() { Id = "Room", MinCells = 4, MaxCells = 64, TargetCount = 3 } };
            fixture.Request.FallbackPolicy.AllowMergedPartition = false;
            fixture.Request.FallbackPolicy.AllowRoomSizeShortfall = false;
            fixture = fixture with { Context = fixture.Context with
            {
                ExistingCleanDomainCells = fixture.Request.Shape.PreservedCells.ToHashSet(),
                CleanHostCells = new HashSet<Vector2i> { new(-1, 0), new(-2, 0), new(19, 0), new(18, 0) },
            } };
            var before = SEntMan.GetEntities().ToHashSet();
            var manager = Pair.Server.ResolveDependency<IPrototypeManager>();
            var themeId = "KsProcgenEmptyOfficeThemeFixture";
            var budgets = Budgets with { UseSizeGoals = true, MaximumPartitionMerges = 0,
                MaximumConfigurationProbes = 1, MaximumLayoutProbes = 1 };
            var result = Plan(manager, fixture, budgets: budgets, themeId: themeId);
            AssertCandidate(result);
            Assert.That(result.LayoutProbes, Is.EqualTo(1));
            Assert.That(result.Partition!.SizeGoalAllocationAttempts, Is.EqualTo(2));
            Assert.That(result.Partition.SizeGoalAttempts.Any(attempt => attempt.AllocationOrdinal == 0 &&
                attempt.Status == KsProcgenPartitionStatus.OpenFallback), Is.True);
            Assert.That(result.Partition.Regions.All(region => region.Partition.Status == KsProcgenPartitionStatus.Proposed), Is.True);
            Assert.That(result.Partition.Analysis!.Components.Count, Is.EqualTo(2));
            Assert.That(result.SizeMix.Single().AchievedCount, Is.EqualTo(3));
            Assert.That(result.Themes!.Regions.Select(region => region.FloorCells.Count).Order(), Is.EqualTo(new[] { 5, 24, 32 }));
            Assert.That(fixture.Request.Shape.PreservedCells.All(result.Partition.FloorCells.Contains), Is.True);
            var exact = Plan(manager, fixture, budgets: budgets with
            {
                MaximumReportedGraphExpansions = result.ExpandedCells,
                MaximumPartitionSeamProbes = result.PartitionSeamProbes,
                MaximumOrderProbes = result.OrderProbes,
                MaximumFurnishingProbes = Math.Max(1, result.FurnishingProbes),
                MaximumLightingFixtures = result.FixtureSelections,
            }, themeId: themeId);
            AssertCandidate(exact);
            Assert.That(exact.SemanticHash, Is.EqualTo(result.SemanticHash));
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    [Test]
    public async Task GlobalSizeQuotasCompleteAcrossSeparateEntranceNetworks()
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var fixture = Fixture(tiny: true);
            fixture.Request.Shape.Cells = Enumerable.Range(0, 9).Concat(Enumerable.Range(20, 4))
                .SelectMany(x => Enumerable.Range(0, 4).Select(y => new Vector2i(x, y))).ToList();
            fixture.Request.EntranceDomain!.Entrances.Add(new()
                { PortId = "B1", ThresholdCells = new List<Vector2i> { new(19, 0) }, InwardNormal = new(1, 0) });
            fixture.Request.EntranceDomain.Configurations[0].Groups.Add(new()
                { Id = "B", Ports = new List<string> { "B1" } });
            fixture.Request.SizeMix = new List<KsProcgenRoomSizeGoal>
            {
                new() { Id = "Large", MinCells = 16, MaxCells = 16, TargetCount = 2 },
                new() { Id = "Medium", MinCells = 9, MaxCells = 9 },
                new() { Id = "Small", MinCells = 5, MaxCells = 5 },
            };
            fixture.Request.FallbackPolicy.AllowMergedPartition = false;
            fixture.Request.FallbackPolicy.AllowRoomSizeShortfall = false;
            fixture = fixture with { Context = fixture.Context with
                { CleanHostCells = new HashSet<Vector2i> { new(-1, 0), new(-2, 0), new(19, 0), new(18, 0) } } };
            var manager = Pair.Server.ResolveDependency<IPrototypeManager>();
            var before = SEntMan.GetEntities().ToHashSet();
            var themeId = "KsProcgenEmptyOfficeThemeFixture";
            var budgets = Budgets with { UseSizeGoals = true, MaximumPartitionMerges = 0,
                MaximumConfigurationProbes = 1, MaximumLayoutProbes = 1 };
            var result = Plan(manager, fixture, budgets: budgets, themeId: themeId);
            AssertCandidate(result);
            Assert.That(result.Partition!.SizeGoalSubdivisionUsed, Is.True);
            Assert.That(result.Partition.Analysis!.Components.Count, Is.EqualTo(2));
            Assert.That(result.Partition.SizeGoalAttempts.Select(attempt => attempt.NetworkId).Distinct().Count(), Is.EqualTo(2));
            Assert.That(result.SizeMix.All(outcome => outcome.AchievedCount == outcome.RequestedCount), Is.True);
            Assert.That(result.Themes!.Regions.Select(region => region.FloorCells.Count).Order(), Is.EqualTo(new[] { 5, 9, 16, 16 }));
            var exact = Plan(manager, fixture, budgets: budgets with
            {
                MaximumReportedGraphExpansions = result.ExpandedCells,
                MaximumPartitionSeamProbes = result.PartitionSeamProbes,
                MaximumOrderProbes = result.OrderProbes,
                MaximumFurnishingProbes = Math.Max(1, result.FurnishingProbes),
                MaximumLightingFixtures = result.FixtureSelections,
            }, themeId: themeId);
            AssertCandidate(exact);
            Assert.That(exact.SemanticHash, Is.EqualTo(result.SemanticHash));
            fixture.Request.SizeMix = new List<KsProcgenRoomSizeGoal>
                { new() { Id = "OnlyOne", MinCells = 4, MaxCells = 64 } };
            var denied = Plan(manager, fixture, budgets: budgets with { MaximumLayoutProbes = 2 }, themeId: themeId);
            AssertFailure(denied, KsProcgenOwnedPlanStatus.FallbackDisallowed, code: "RoomSizeShortfallDisallowed");
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    [Test]
    public async Task GoalWidthRetryMovesBoundaryAndCompletesStrictSizeBands()
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var fixture = Fixture(tiny: true);
            fixture.Request.Shape.Cells = Enumerable.Range(0, 10).SelectMany(x => Enumerable.Range(0, 4)
                .Select(y => new Vector2i(x, y))).ToList();
            fixture.Request.Shape.PreservedCells = fixture.Request.Shape.Cells.Where(cell => cell.X == 7 && cell.Y <= 1).ToList();
            fixture.Request.EntranceDomain!.Configurations[0].Groups[0].RootCells.Add(new(7, 0));
            fixture.Request.SizeMix = new List<KsProcgenRoomSizeGoal>
            {
                new() { Id = "Large", MinCells = 20, MaxCells = 28 },
                new() { Id = "Small", MinCells = 5, MaxCells = 13 },
            };
            fixture.Request.FallbackPolicy.AllowMergedPartition = false;
            fixture.Request.FallbackPolicy.AllowRoomSizeShortfall = false;
            fixture = fixture with { Context = fixture.Context with
                { ExistingCleanDomainCells = fixture.Request.Shape.PreservedCells.ToHashSet() } };
            var manager = Pair.Server.ResolveDependency<IPrototypeManager>();
            var before = SEntMan.GetEntities().ToHashSet();
            var themeId = "KsProcgenEmptyOfficeThemeFixture";
            var budgets = Budgets with { UseSizeGoals = true, MaximumPartitionMerges = 0, MaximumConfigurationProbes = 1 };
            var result = Plan(manager, fixture, budgets: budgets, themeId: themeId);
            AssertCandidate(result);
            Assert.That(result.LayoutProbes, Is.EqualTo(1));
            Assert.That(result.Partition!.SizeGoalAttempts.Select(attempt => attempt.WidthVariant), Is.EqualTo(new[] { 0, 1 }));
            Assert.That(result.Partition.SizeGoalAttempts.All(attempt => attempt.AcrossX), Is.True);
            Assert.That(result.SizeMix.All(outcome => outcome.AchievedCount == outcome.RequestedCount), Is.True);
            Assert.That(result.Themes!.Regions.Select(region => region.FloorCells.Count).Order(), Is.EqualTo(new[] { 13, 24 }));
            Assert.That(result.Partition.WallCells.All(cell => cell.X == 6), Is.True);
            Assert.That(fixture.Request.Shape.PreservedCells.All(result.Partition.FloorCells.Contains), Is.True);
            Assert.That(result.Fallbacks.Select(fallback => fallback.Code), Does.Not.Contain("MergedPartition"));
            var exact = Plan(manager, fixture, budgets: budgets with
            {
                MaximumReportedGraphExpansions = result.ExpandedCells,
                MaximumPartitionSeamProbes = result.PartitionSeamProbes,
                MaximumOrderProbes = result.OrderProbes,
                MaximumFurnishingProbes = Math.Max(1, result.FurnishingProbes),
                MaximumLightingFixtures = result.FixtureSelections,
                MaximumLayoutProbes = 1,
            }, themeId: themeId);
            AssertCandidate(exact);
            Assert.That(exact.SemanticHash, Is.EqualTo(result.SemanticHash));
            var limited = Plan(manager, fixture, budgets: budgets with
                { MaximumPartitionSeamProbes = result.Partition.SizeGoalAttempts[0].SeamProbes + 1 }, themeId: themeId);
            AssertFailure(limited, KsProcgenOwnedPlanStatus.BudgetExceeded, code: "PartitionSeamProbeBudget");
            Assert.That(limited.Stages.Last().Stage, Is.EqualTo(KsProcgenOwnedPlanStage.Partition));
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    [Test]
    public async Task GoalOrientationRetryPreservesDeclaredFloorAndCompletesStrictSizeGoals()
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var fixture = Fixture(tiny: true);
            fixture.Request.Shape.Cells = Enumerable.Range(0, 6).SelectMany(x => Enumerable.Range(0, 6)
                .Select(y => new Vector2i(x, y))).ToList();
            fixture.Request.Shape.PreservedCells = fixture.Request.Shape.Cells.Where(cell => cell.X == 3 && cell.Y <= 2 || cell.Y == 0).ToList();
            fixture.Request.SizeMix = new List<KsProcgenRoomSizeGoal>
            {
                new() { Id = "Large", MinCells = 18, MaxCells = 18 },
                new() { Id = "Small", MinCells = 13, MaxCells = 13 },
            };
            fixture.Request.FallbackPolicy.AllowMergedPartition = false;
            fixture.Request.FallbackPolicy.AllowRoomSizeShortfall = false;
            fixture = fixture with { Context = fixture.Context with
                { ExistingCleanDomainCells = fixture.Request.Shape.PreservedCells.ToHashSet() } };
            var manager = Pair.Server.ResolveDependency<IPrototypeManager>();
            var before = SEntMan.GetEntities().ToHashSet();
            var budgets = Budgets with { UseSizeGoals = true, MaximumPartitionMerges = 1, MaximumConfigurationProbes = 1 };
            var themeId = "KsProcgenEmptyOfficeThemeFixture";
            var result = Plan(manager, fixture, budgets: budgets, themeId: themeId);
            Assert.That(result.Status, Is.EqualTo(KsProcgenOwnedPlanStatus.Candidate),
                string.Join(",", result.Attempts.Select(attempt => attempt.ReasonCode)));
            Assert.That(result.LayoutProbes, Is.EqualTo(1));
            Assert.That(result.Partition!.SizeGoalAttempts.Select(attempt => attempt.Status),
                Is.EqualTo(new[] { KsProcgenPartitionStatus.OpenFallback, KsProcgenPartitionStatus.Proposed }));
            Assert.That(result.PartitionMerges, Is.EqualTo(1));
            Assert.That(result.SizeMix.All(outcome => outcome.AchievedCount == outcome.RequestedCount), Is.True);
            Assert.That(result.Themes!.Regions.Select(region => region.FloorCells.Count).Order(), Is.EqualTo(new[] { 13, 18 }));
            Assert.That(result.Fallbacks.Select(fallback => fallback.Code), Does.Not.Contain("MergedPartition"));
            Assert.That(result.Fallbacks.Select(fallback => fallback.Code), Does.Not.Contain("RoomSizeDeviation"));
            Assert.That(fixture.Request.Shape.PreservedCells.All(result.Partition.FloorCells.Contains), Is.True);
            var exact = Plan(manager, fixture, budgets: budgets with
            {
                MaximumReportedGraphExpansions = result.ExpandedCells,
                MaximumPartitionSeamProbes = result.PartitionSeamProbes,
                MaximumOrderProbes = result.OrderProbes,
                MaximumFurnishingProbes = Math.Max(1, result.FurnishingProbes),
                MaximumLightingFixtures = result.FixtureSelections,
                MaximumLayoutProbes = 1,
            }, themeId: themeId);
            AssertCandidate(exact);
            Assert.That(exact.SemanticHash, Is.EqualTo(result.SemanticHash));
            var exhausted = Plan(manager, fixture, budgets: budgets with
                { MaximumPartitionSeamProbes = result.Partition.SizeGoalAttempts[0].SeamProbes + 1 }, themeId: themeId);
            AssertFailure(exhausted, KsProcgenOwnedPlanStatus.BudgetExceeded, code: "PartitionSeamProbeBudget");
            Assert.That(exhausted.LayoutProbes, Is.EqualTo(1));
            Assert.That(exhausted.Stages.Last().Stage, Is.EqualTo(KsProcgenOwnedPlanStage.Partition));
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    [Test]
    public async Task ProtectedConstantFloorRejectsGoalSeamsThenRetriesScalarWithoutRefunds()
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var fixture = Fixture(tiny: true);
            fixture.Request.Shape.Cells = Enumerable.Range(0, 9).SelectMany(x => Enumerable.Range(0, 4)
                .Select(y => new Vector2i(x, y))).ToList();
            fixture.Request.Shape.PreservedCells = fixture.Request.Shape.Cells.ToList();
            fixture.Request.SizeMix = new List<KsProcgenRoomSizeGoal>
            {
                new() { Id = "Large", MinCells = 16, MaxCells = 16 },
                new() { Id = "Medium", MinCells = 9, MaxCells = 9 },
                new() { Id = "Small", MinCells = 5, MaxCells = 5 },
            };
            fixture.Request.FallbackPolicy.AllowMergedPartition = false;
            fixture = fixture with { Context = fixture.Context with
                { ExistingCleanDomainCells = fixture.Request.Shape.Cells.ToHashSet() } };
            var manager = Pair.Server.ResolveDependency<IPrototypeManager>();
            var budgets = Budgets with { UseSizeGoals = true, MaximumPartitionMerges = 0, MaximumConfigurationProbes = 1 };
            var result = Plan(manager, fixture, budgets: budgets);
            AssertCandidate(result);
            Assert.That(result.ConfigurationProbes, Is.EqualTo(1));
            Assert.That(result.LayoutProbes, Is.EqualTo(2));
            Assert.That(result.Attempts.Select(attempt => attempt.SizeGoalsRequested), Is.EqualTo(new[] { true, false }));
            Assert.That(result.Attempts[0].ReasonCode, Is.EqualTo("MergedPartitionDisallowed"));
            Assert.That(result.Partition!.SizeGoalSubdivisionUsed, Is.False);
            Assert.That(result.Partition.WallCells, Is.Empty);
            Assert.That(result.Partition.FloorCells.Count, Is.EqualTo(36));
            Assert.That(result.ExpandedCells, Is.EqualTo(result.Attempts.Sum(attempt => attempt.ExpandedCells)));
            Assert.That(result.PartitionSeamProbes, Is.EqualTo(result.Attempts.Sum(attempt => attempt.PartitionSeamProbes)));
            var direct = Plan(manager, fixture, budgets: budgets with { UseSizeGoals = false });
            AssertCandidate(direct);
            Assert.That(result.SemanticHash, Is.EqualTo(direct.SemanticHash));
            Assert.That(result.ExpandedCells, Is.GreaterThan(direct.ExpandedCells));
            var capped = Plan(manager, fixture, budgets: budgets with { MaximumLayoutProbes = 1 });
            AssertFailure(capped, KsProcgenOwnedPlanStatus.BudgetExceeded, code: "OwnedLayoutProbeBudget");
            Assert.That(capped.ExpandedCells, Is.EqualTo(result.Attempts[0].ExpandedCells));
            Assert.That(capped.Stages.Last().SizeGoalsRequested, Is.False);
        });
    }

    [Test]
    public async Task DeniedSubdivisionRetriesInsideOneConfigurationWithoutRefundingWork()
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var manager = Pair.Server.ResolveDependency<IPrototypeManager>();
            var fixture = Fixture();
            fixture.Request.FallbackPolicy.AllowMergedPartition = false;
            var budgets = Budgets with
            {
                PreferredMaximumRoomCells = 4, AlternativeMaximumRoomCells = [64],
                MaximumPartitionMerges = 0, MaximumConfigurationProbes = 1,
            };
            var result = Plan(manager, fixture, budgets: budgets);
            AssertCandidate(result);
            Assert.That(result.ConfigurationProbes, Is.EqualTo(1));
            Assert.That(result.LayoutProbes, Is.EqualTo(2));
            Assert.That(result.Attempts.Select(attempt => attempt.ConfigurationId), Is.EqualTo(new[] { "Separate", "Separate" }));
            Assert.That(result.Attempts.Select(attempt => attempt.PreferredMaximumRoomCells), Is.EqualTo(new[] { 4, 64 }));
            Assert.That(result.Attempts[0].ReasonCode, Is.EqualTo("MergedPartitionDisallowed"));
            Assert.That(result.Attempts[0].PartitionSeamProbes, Is.GreaterThan(0));
            Assert.That(result.ExpandedCells, Is.EqualTo(result.Attempts.Sum(attempt => attempt.ExpandedCells)));
            Assert.That(result.PartitionSeamProbes, Is.EqualTo(result.Attempts.Sum(attempt => attempt.PartitionSeamProbes)));
            Assert.That(result.Stages.Where(stage => stage.LayoutOrdinal == 0).Last().ReasonCode,
                Is.EqualTo("MergedPartitionDisallowed"));
            Assert.That(result.Stages.Where(stage => stage.LayoutOrdinal == 1)
                .All(stage => stage.PreferredMaximumRoomCells == 64), Is.True);
            var direct = Plan(manager, fixture, budgets: Budgets with { MaximumPartitionMerges = 0 });
            AssertCandidate(direct);
            Assert.That(result.SemanticHash, Is.EqualTo(direct.SemanticHash));
            Assert.That(result.Partition!.PartitionHash, Is.EqualTo(direct.Partition!.PartitionHash));
            Assert.That(result.Fallbacks, Is.Empty);
            var exact = Plan(manager, fixture, budgets: budgets with
            {
                MaximumReportedGraphExpansions = result.ExpandedCells,
                MaximumPartitionSeamProbes = result.PartitionSeamProbes,
                MaximumOrderProbes = result.OrderProbes,
                MaximumLayoutProbes = result.LayoutProbes,
            });
            AssertCandidate(exact);
            Assert.That(exact.SemanticHash, Is.EqualTo(result.SemanticHash));
            Assert.That(exact.ExpandedCells, Is.EqualTo(result.ExpandedCells));
            var capped = Plan(manager, fixture, budgets: budgets with { MaximumLayoutProbes = 1 });
            AssertFailure(capped, KsProcgenOwnedPlanStatus.BudgetExceeded, code: "OwnedLayoutProbeBudget");
            Assert.That(capped.ConfigurationProbes, Is.EqualTo(1));
            Assert.That(capped.LayoutProbes, Is.EqualTo(1));
            Assert.That(capped.ExpandedCells, Is.EqualTo(result.Attempts[0].ExpandedCells));
            Assert.That(capped.Stages.Last().PreferredMaximumRoomCells, Is.EqualTo(64));
            var seamLimit = result.PartitionSeamProbes - 1;
            var exhausted = Plan(manager, fixture, budgets: budgets with { MaximumPartitionSeamProbes = seamLimit });
            AssertFailure(exhausted, KsProcgenOwnedPlanStatus.BudgetExceeded, code: "PartitionSeamProbeBudget");
            Assert.That(exhausted.PartitionSeamProbes, Is.EqualTo(seamLimit));
            Assert.That(exhausted.LayoutProbes, Is.EqualTo(2));
        });
    }

    [Test]
    public async Task FloorFailureSkipsSubdivisionAlternativesAndKeepsConfigurationBudgetSeparate()
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var fixture = Alternatives();
            fixture.Request.EntranceDomain!.Configurations[0].Groups[0].RootCells.Add(new(2, 3));
            var result = Plan(Pair.Server.ResolveDependency<IPrototypeManager>(), fixture,
                budgets: Budgets with { AlternativeMaximumRoomCells = [32, 16], MaximumConfigurationProbes = 2 },
                themeId: "KsProcgenMandatoryOfficeFixture");
            AssertCandidate(result);
            Assert.That(result.ConfigurationProbes, Is.EqualTo(2));
            Assert.That(result.LayoutProbes, Is.EqualTo(2));
            Assert.That(result.Attempts.All(attempt => attempt.PreferredMaximumRoomCells == 64), Is.True);
        });
    }

    [Test]
    public async Task SubdivisionProfilesRetainAuthoredOrderAndStopAtTheFirstCompleteLayout()
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var manager = Pair.Server.ResolveDependency<IPrototypeManager>();
            var fixture = Fixture();
            fixture.Request.FallbackPolicy.AllowMergedPartition = false;
            var result = Plan(manager, fixture, budgets: Budgets with
            {
                PreferredMaximumRoomCells = 4, AlternativeMaximumRoomCells = [8, 64, 128],
                MaximumPartitionMerges = 0, MaximumConfigurationProbes = 1,
            });
            AssertCandidate(result);
            Assert.That(result.Attempts.Select(attempt => attempt.PreferredMaximumRoomCells), Is.EqualTo(new[] { 4, 8, 64 }));
            Assert.That(result.Attempts.Select(attempt => attempt.Ordinal), Is.EqualTo(new[] { 0, 1, 2 }));
            Assert.That(result.Attempts.Take(2).All(attempt => attempt.ReasonCode == "MergedPartitionDisallowed"), Is.True);
            Assert.That(result.ConfigurationProbes, Is.EqualTo(1));
            Assert.That(result.LayoutProbes, Is.EqualTo(3));
            Assert.That(result.Stages.Any(stage => stage.PreferredMaximumRoomCells == 128), Is.False);
            Assert.That(result.PartitionSeamProbes, Is.EqualTo(result.Attempts.Sum(attempt => attempt.PartitionSeamProbes)));
        });
    }

    [Test]
    public async Task LayoutAllowanceRemainsSharedWhenTheSearchAdvancesToAnotherConfiguration()
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var fixture = Alternatives();
            fixture.Request.EntranceDomain!.Configurations[0].Groups[0].RootCells.Add(new(2, 3));
            var result = Plan(Pair.Server.ResolveDependency<IPrototypeManager>(), fixture,
                budgets: Budgets with { MaximumLayoutProbes = 1, AlternativeMaximumRoomCells = [32] });
            AssertFailure(result, KsProcgenOwnedPlanStatus.BudgetExceeded, code: "OwnedLayoutProbeBudget");
            Assert.That(result.ConfigurationProbes, Is.EqualTo(1));
            Assert.That(result.LayoutProbes, Is.EqualTo(1));
            Assert.That(result.Stages.Last().ConfigurationId, Is.EqualTo("Joined"));
            Assert.That(result.Stages.Last().LayoutOrdinal, Is.EqualTo(1));
            Assert.That(result.Stages.Last().ExpandedCells, Is.Zero);
        });
    }

    [Test]
    public async Task AllowedDegradationDoesNotSpendAlternativeLayoutAllowances()
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var result = Plan(Pair.Server.ResolveDependency<IPrototypeManager>(), Fixture(), budgets: Budgets with
            {
                PreferredMaximumRoomCells = 4, AlternativeMaximumRoomCells = [64], MaximumPartitionMerges = 0,
            });
            AssertCandidate(result);
            Assert.That(result.LayoutProbes, Is.EqualTo(1));
            Assert.That(result.ConfigurationProbes, Is.EqualTo(1));
            Assert.That(result.Fallbacks.Select(fallback => fallback.Code), Does.Contain("MergedPartition"));
            Assert.That(result.Stages.All(stage => stage.PreferredMaximumRoomCells == 4), Is.True);
        });
    }

    [TestCase(-1)]
    [TestCase(65)]
    public async Task InvalidLayoutAllowancesAreRejectedWithoutAnAttempt(int maximum)
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var result = Plan(Pair.Server.ResolveDependency<IPrototypeManager>(), Fixture(),
                budgets: Budgets with { MaximumLayoutProbes = maximum });
            AssertFailure(result, KsProcgenOwnedPlanStatus.InvalidInput, code: "InvalidOwnedInteriorInput");
            Assert.That(result.LayoutProbes + result.ConfigurationProbes + result.ExpandedCells, Is.Zero);
            Assert.That(result.Stages, Is.Empty);
        });
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    [TestCase(4)]
    [TestCase(5)]
    public async Task InvalidSubdivisionProfilesAreRejectedBeforeWork(int variant)
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var sizes = variant switch
            {
                0 => null,
                1 => new[] { 64 },
                2 => new[] { 16, 16 },
                3 => new[] { 3 },
                4 => new[] { 4_097 },
                _ => Enumerable.Range(4, 64).ToArray(),
            };
            var result = Plan(Pair.Server.ResolveDependency<IPrototypeManager>(), Fixture(),
                budgets: Budgets with { AlternativeMaximumRoomCells = sizes! });
            AssertFailure(result, KsProcgenOwnedPlanStatus.InvalidInput, code: "InvalidOwnedInteriorInput");
            Assert.That(result.LayoutProbes + result.ConfigurationProbes + result.ExpandedCells, Is.Zero);
            Assert.That(result.Stages, Is.Empty);
        });
    }

    [Test]
    public async Task ZeroLayoutAllowanceStopsBeforeEnteringAConfiguration()
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var result = Plan(Pair.Server.ResolveDependency<IPrototypeManager>(), Fixture(),
                budgets: Budgets with { MaximumLayoutProbes = 0 });
            AssertFailure(result, KsProcgenOwnedPlanStatus.BudgetExceeded, code: "OwnedLayoutProbeBudget");
            Assert.That(result.LayoutProbes + result.ConfigurationProbes + result.ExpandedCells, Is.Zero);
            Assert.That(result.Stages.Count, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task PartitionSeamWorkCannotBeRefundedWhenACompleteConfigurationIsRejected()
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var manager = Pair.Server.ResolveDependency<IPrototypeManager>();
            var fixture = Alternatives();
            var full = Plan(manager, fixture, themeId: "KsProcgenMandatoryOfficeFixture");
            AssertCandidate(full);
            Assert.That(full.Attempts.Count, Is.EqualTo(2));
            Assert.That(full.Attempts.All(attempt => attempt.PartitionSeamProbes > 0), Is.True);
            Assert.That(full.PartitionSeamProbes, Is.EqualTo(full.Attempts.Sum(attempt => attempt.PartitionSeamProbes)));
            Assert.That(full.PartitionSeamProbes, Is.EqualTo(full.Stages.Sum(stage => stage.PartitionSeamProbes)));
            Assert.That(full.PartitionSeamProbes, Is.GreaterThan(full.Partition!.SeamProbes));
            var replay = Plan(manager, fixture, budgets: Budgets with
            {
                MaximumPartitionSeamProbes = full.PartitionSeamProbes,
                MaximumReportedGraphExpansions = full.ExpandedCells,
            }, themeId: "KsProcgenMandatoryOfficeFixture");
            AssertCandidate(replay);
            Assert.That(replay.SemanticHash, Is.EqualTo(full.SemanticHash));
            Assert.That(replay.PartitionSeamProbes, Is.EqualTo(full.PartitionSeamProbes));
            var limit = full.PartitionSeamProbes - 1;
            var shortfall = Plan(manager, fixture,
                budgets: Budgets with { MaximumPartitionSeamProbes = limit }, themeId: "KsProcgenMandatoryOfficeFixture");
            Assert.That(shortfall.PartitionSeamProbes, Is.EqualTo(limit));
            AssertFailure(shortfall, KsProcgenOwnedPlanStatus.BudgetExceeded, code: "PartitionSeamProbeBudget");
            Assert.That(shortfall.Attempts.Count, Is.EqualTo(2));
            Assert.That(shortfall.Stages.Last().Stage, Is.EqualTo(KsProcgenOwnedPlanStage.Partition));
            var zero = Plan(manager, Fixture(), budgets: Budgets with { MaximumPartitionSeamProbes = 0 });
            AssertFailure(zero, KsProcgenOwnedPlanStatus.BudgetExceeded, code: "PartitionSeamProbeBudget");
            Assert.That(zero.PartitionSeamProbes, Is.Zero);
            Assert.That(zero.Stages.Last().Stage, Is.EqualTo(KsProcgenOwnedPlanStage.Partition));
        });
    }

    [Test]
    public async Task SeamConnectivityValidationUsesTheRemainingRequestGraphAllowance()
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var manager = Pair.Server.ResolveDependency<IPrototypeManager>();
            var fixture = Fixture();
            var full = Plan(manager, fixture);
            AssertCandidate(full);
            var beforeSeams = full.Stages.First().ExpandedCells + full.Floor!.Accepted!.Analysis!.ExpandedCells +
                full.Floor.Accepted.FloorCells.Count;
            var limit = beforeSeams + 1;
            var shortfall = Plan(manager, fixture, budgets: Budgets with { MaximumReportedGraphExpansions = limit });
            AssertFailure(shortfall, KsProcgenOwnedPlanStatus.BudgetExceeded, code: "PartitionExpansionBudget");
            Assert.That(shortfall.ExpandedCells, Is.EqualTo(limit));
            Assert.That(shortfall.PartitionSeamProbes, Is.Zero);
            Assert.That(shortfall.Stages.Last().Stage, Is.EqualTo(KsProcgenOwnedPlanStage.Partition));
        });
    }

    [TestCase(-1)]
    [TestCase(1_000_001)]
    public async Task InvalidSeamWorkLimitsAreRejectedBeforeAnAttempt(int limit)
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var result = Plan(Pair.Server.ResolveDependency<IPrototypeManager>(), Fixture(),
                budgets: Budgets with { MaximumPartitionSeamProbes = limit });
            AssertFailure(result, KsProcgenOwnedPlanStatus.InvalidInput);
            Assert.That(result.Attempts, Is.Empty);
            Assert.That(result.Stages, Is.Empty);
            Assert.That(result.ExpandedCells + result.PartitionSeamProbes, Is.Zero);
        });
    }

    [Test]
    public async Task ExhaustedPartitionStatusCannotAuthorizeDownstreamContent()
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var manager = Pair.Server.ResolveDependency<IPrototypeManager>();
            var fixture = Fixture();
            var full = Plan(manager, fixture);
            AssertCandidate(full);
            var network = full.Partition!.Regions[0];
            var rejected = new KsProcgenPartitionResult
            {
                Status = KsProcgenPartitionStatus.BudgetExceeded, FloorCells = network.Partition.FloorCells,
                WallCells = network.Partition.WallCells, DoorOpenings = network.Partition.DoorOpenings,
                MergedZoneGroups = network.Partition.MergedZoneGroups,
            };
            var region = full.Themes!.Regions.First(room => room.EntranceGroupIds.SequenceEqual(network.GroupIds));
            var assignment = new KsProcgenThemeAssignmentResult { Status = KsProcgenThemeAssignmentStatus.Selected,
                Regions = [region] };
            var themed = KsProcgenThemeAssignmentPlanner.Plan(manager, "KsProcgenSimpleOffice", fixture.Request.Seed,
                network.Fill, rejected);
            Assert.That(themed.Status, Is.EqualTo(KsProcgenThemeAssignmentStatus.InvalidInput));
            Assert.That(themed.Issue?.Code, Is.EqualTo("InvalidThemeAssignmentInput"));
            Assert.That(themed.Regions, Is.Empty);
            var material = KsProcgenMaterialPlanner.Plan(manager, assignment, rejected, fixture.Request.Seed);
            Assert.That(material.Status, Is.EqualTo(KsProcgenMaterialStatus.InvalidInput));
            Assert.That(material.Issue?.Code, Is.EqualTo("InvalidMaterialInput"));
            Assert.That(material.Tiles, Is.Empty);
            var furnishings = KsProcgenFurnishingPlanner.Plan(manager, region, rejected, fixture.Request.Seed);
            Assert.That(furnishings.Status, Is.EqualTo(KsProcgenFurnishingStatus.InvalidInput));
            Assert.That(furnishings.Issue?.Code, Is.EqualTo("InvalidFurnishingInput"));
            Assert.That(furnishings.Entities, Is.Empty);
            var forged = new KsProcgenGroupPartitionResult
            {
                Status = full.Partition.Status, FloorCells = full.Partition.FloorCells,
                WallCells = full.Partition.WallCells, ProtectedCells = full.Partition.ProtectedCells,
                Scene = full.Partition.Scene, Analysis = full.Partition.Analysis, PartitionHash = full.Partition.PartitionHash,
                Regions = full.Partition.Regions.Select(candidate => candidate.Id == network.Id
                    ? candidate with { Partition = rejected } : candidate).ToArray(),
            };
            var grouped = KsProcgenGroupMaterialPlanner.Plan(manager, fixture.Request, forged, full.Themes);
            Assert.That(grouped.Status, Is.EqualTo(KsProcgenGroupMaterialStatus.InvalidInput));
            Assert.That(grouped.Issue?.Code, Is.EqualTo("InvalidMaterialNetwork"));
            Assert.That(grouped.Tiles, Is.Empty);
        });
    }

    [Test]
    public async Task RejectedFloorConfigurationCannotPreventACompleteLaterCandidate()
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var fixture = Alternatives();
            fixture.Request.EntranceDomain!.Configurations[0].Groups[0].RootCells.Add(new(2, 3));
            var result = Plan(Pair.Server.ResolveDependency<IPrototypeManager>(), fixture, themeId: "KsProcgenMandatoryOfficeFixture");
            AssertCandidate(result);
            Assert.That(result.Attempts.Count, Is.EqualTo(2));
            Assert.That(result.Attempts[0].Status, Is.EqualTo(KsProcgenOwnedPlanStatus.Rejected));
            Assert.That(result.Stages.Where(stage => stage.ConfigurationId == "Separate")
                .All(stage => stage.Stage == KsProcgenOwnedPlanStage.Floor && !stage.Completed), Is.True);
            Assert.That(result.Floor!.Accepted!.Analysis!.ConfigurationId, Is.EqualTo("Joined"));
        });
    }

    [Test]
    public async Task DisabledAlternativesConsumeNoSearchAttemptOrAllowance()
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var fixture = Alternatives();
            fixture.Request.EntranceDomain!.Configurations.Single(configuration => configuration.Id == "Joined").Weight = 0;
            var result = Plan(Pair.Server.ResolveDependency<IPrototypeManager>(), fixture,
                budgets: Budgets with { MaximumConfigurationProbes = 1 }, themeId: "KsProcgenMandatoryOfficeFixture");
            AssertFailure(result, KsProcgenOwnedPlanStatus.Rejected, code: "MandatoryPackInPassage");
            Assert.That(result.Attempts.Count, Is.EqualTo(1));
            Assert.That(result.Stages.All(stage => stage.ConfigurationId == "Separate"), Is.True);
        });
    }

    [Test]
    public async Task DiscardedLightingAndFurnishingWorkRemainChargedToTheSearch()
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var manager = Pair.Server.ResolveDependency<IPrototypeManager>();
            var fixture = LightingAlternatives();
            var result = Plan(manager, fixture);
            AssertCandidate(result);
            Assert.That(result.Attempts.Count, Is.EqualTo(2));
            Assert.That(result.Attempts[0].ReasonCode, Is.EqualTo("SparseLightingDisallowed"));
            Assert.That(result.Attempts[0].FixtureSelections, Is.GreaterThan(0));
            Assert.That(result.Attempts[0].FurnishingProbes, Is.GreaterThan(0));
            Assert.That(result.FixtureSelections, Is.EqualTo(result.Attempts.Sum(attempt => attempt.FixtureSelections)));
            Assert.That(result.FixtureSelections, Is.GreaterThan(result.Lighting!.FixtureCount));
            Assert.That(result.FurnishingProbes, Is.EqualTo(result.Attempts.Sum(attempt => attempt.FurnishingProbes)));
            Assert.That(result.Attempts[0].Fallbacks.Select(item => item.Code), Does.Contain("SparseFurnishing"));
            Assert.That(result.Fallbacks.Select(item => item.Code), Does.Not.Contain("SparseFurnishing"));
            var replay = Plan(manager, fixture, budgets: Budgets with
            {
                MaximumReportedGraphExpansions = result.ExpandedCells,
                MaximumFurnishingProbes = result.FurnishingProbes, MaximumLightingFixtures = result.FixtureSelections,
            });
            AssertCandidate(replay);
            Assert.That(replay.SemanticHash, Is.EqualTo(result.SemanticHash));
        });
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task ExhaustingDiscardedContentWorkStopsBeforeUnfundedContent(bool furnishing)
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var manager = Pair.Server.ResolveDependency<IPrototypeManager>();
            var fixture = LightingAlternatives();
            var full = Plan(manager, fixture);
            AssertCandidate(full);
            var first = full.Attempts[0];
            var limited = Plan(manager, fixture, budgets: furnishing ? Budgets with { MaximumFurnishingProbes = first.FurnishingProbes }
                : Budgets with { MaximumLightingFixtures = first.FixtureSelections });
            Assert.That(limited.FixtureSelections, Is.EqualTo(first.FixtureSelections));
            AssertFailure(limited, KsProcgenOwnedPlanStatus.BudgetExceeded,
                code: furnishing ? "OwnedFurnishingProbeBudget" : "GroupLightingFixtureBudget");
            Assert.That(limited.Attempts.Count, Is.EqualTo(furnishing ? 1 : 2));
            Assert.That(limited.FurnishingProbes, Is.EqualTo(furnishing ? first.FurnishingProbes : full.FurnishingProbes));
        });
    }

    private static Input LightingAlternatives()
    {
        var fixture = Alternatives(narrow: false);
        fixture.Request.Shape.Cells = Enumerable.Range(0, 6).SelectMany(x => Enumerable.Range(0, 6)
            .Select(y => new Vector2i(x, y))).ToList();
        fixture.Request.EntranceDomain!.Entrances.Single(port => port.PortId == "B1").ThresholdCells[0] = new(-1, 5);
        var separate = fixture.Request.EntranceDomain.Configurations.Single(configuration => configuration.Id == "Separate");
        separate.Groups[0].RootCells = fixture.Request.Shape.Cells.Where(cell => cell.Y <= 1).ToList();
        separate.Groups[1].RootCells = fixture.Request.Shape.Cells.Where(cell => cell.Y == 3).ToList();
        fixture.Request.FallbackPolicy.AllowSparseLighting = false;
        return new(fixture.Request, new(new HashSet<Vector2i>(),
            new HashSet<Vector2i> { new(-1, 0), new(-2, 0), new(-1, 5), new(-2, 5) }, new HashSet<Vector2i>(), true, true));
    }

    [Test]
    public async Task CompleteSearchRevisitsAValidFloorAfterMandatoryThemeFailure()
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var manager = Pair.Server.ResolveDependency<IPrototypeManager>();
            var fixture = Alternatives();
            var firstFloor = KsProcgenGroupFloorPlanner.Select(fixture.Request, fixture.Context);
            Assert.That(firstFloor.Status, Is.EqualTo(KsProcgenEntranceSceneStatus.Candidate));
            Assert.That(firstFloor.Accepted!.Analysis!.ConfigurationId, Is.EqualTo("Separate"));
            var result = Plan(manager, fixture, themeId: "KsProcgenMandatoryOfficeFixture");
            AssertCandidate(result);
            Assert.That(result.Attempts.Select(attempt => attempt.ConfigurationId), Is.EqualTo(new[] { "Separate", "Joined" }));
            Assert.That(result.Attempts[0].Status, Is.EqualTo(KsProcgenOwnedPlanStatus.Rejected));
            Assert.That(result.Attempts[0].ReasonCode, Is.EqualTo("MandatoryPackInPassage"));
            Assert.That(result.Floor!.Accepted!.Analysis!.ConfigurationId, Is.EqualTo("Joined"));
            Assert.That(result.Partition!.Regions.Count, Is.EqualTo(1));
            Assert.That(result.Themes!.Regions.All(room => room.EntranceGroupIds.SequenceEqual(new[] { "All" })), Is.True);
            Assert.That(result.Furnishings.SelectMany(room => room.Proposal.Entities), Is.Not.Empty);
            Assert.That(result.ExpandedCells, Is.EqualTo(result.Attempts.Sum(attempt => attempt.ExpandedCells)));
            Assert.That(result.ExpandedCells, Is.EqualTo(result.Stages.Sum(stage => stage.ExpandedCells)));
            Assert.That(result.Stages.Where(stage => stage.Stage == KsProcgenOwnedPlanStage.Furnishing)
                .All(stage => stage.ConfigurationId == "Joined"), Is.True);
        });
    }

    [Test]
    public async Task RetryWorkCannotBeRefundedAtTheFinalAudit()
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var manager = Pair.Server.ResolveDependency<IPrototypeManager>();
            var fixture = Alternatives();
            var full = Plan(manager, fixture, themeId: "KsProcgenMandatoryOfficeFixture");
            AssertCandidate(full);
            var limited = Plan(manager, fixture, budgets: Budgets with { MaximumReportedGraphExpansions = full.ExpandedCells - 1 },
                themeId: "KsProcgenMandatoryOfficeFixture");
            AssertFailure(limited, KsProcgenOwnedPlanStatus.BudgetExceeded);
            Assert.That(limited.Attempts.Count, Is.EqualTo(2));
            Assert.That(limited.ExpandedCells, Is.EqualTo(full.ExpandedCells - 1));
            Assert.That(limited.Stages.Last().Stage, Is.EqualTo(KsProcgenOwnedPlanStage.InteriorAudit));
            Assert.That(limited.Stages.Last().ConfigurationId, Is.EqualTo("Joined"));
            Assert.That(limited.FurnishingProbes, Is.EqualTo(full.FurnishingProbes));
        });
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task ConfigurationAndOrderLimitsRemainSharedAcrossRetries(bool configurations)
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var manager = Pair.Server.ResolveDependency<IPrototypeManager>();
            var fixture = Alternatives();
            var full = Plan(manager, fixture, themeId: "KsProcgenMandatoryOfficeFixture");
            AssertCandidate(full);
            var limited = Plan(manager, fixture, budgets: configurations ? Budgets with { MaximumConfigurationProbes = 1 }
                : Budgets with { MaximumOrderProbes = full.Attempts[0].OrderProbes }, themeId: "KsProcgenMandatoryOfficeFixture");
            AssertFailure(limited, KsProcgenOwnedPlanStatus.BudgetExceeded);
            Assert.That(limited.Attempts.Count, Is.EqualTo(configurations ? 1 : 2));
            Assert.That(limited.OrderProbes, Is.EqualTo(full.Attempts[0].OrderProbes));
            Assert.That(limited.Stages.Last().Stage, Is.EqualTo(KsProcgenOwnedPlanStage.Floor));
        });
    }

    [Test]
    public async Task DeniedSizeDeviationAdvancesToAnAllowedWholeConfiguration()
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var fixture = Alternatives(narrow: false);
            fixture.Request.SizeMix.Add(new() { Id = "Room", MinCells = 4, MaxCells = 64, TargetCount = 1 });
            fixture.Request.FallbackPolicy.AllowRoomSizeShortfall = false;
            var result = Plan(Pair.Server.ResolveDependency<IPrototypeManager>(), fixture);
            AssertCandidate(result);
            Assert.That(result.Attempts.Count, Is.EqualTo(2));
            Assert.That(result.Attempts[0].Status, Is.EqualTo(KsProcgenOwnedPlanStatus.FallbackDisallowed));
            Assert.That(result.Attempts[0].ReasonCode, Is.EqualTo("RoomSizeShortfallDisallowed"));
            Assert.That(result.Fallbacks.Select(item => item.Code), Does.Not.Contain("RoomSizeDeviation"));
            Assert.That(result.SizeMix.Single().AchievedCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task RetryReplayRetainsCanonicalOutputAndImmutableAttemptSnapshots()
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var manager = Pair.Server.ResolveDependency<IPrototypeManager>();
            var fixture = Alternatives();
            var first = Plan(manager, fixture, themeId: "KsProcgenMandatoryOfficeFixture");
            AssertCandidate(first);
            fixture.Request.EntranceDomain!.Configurations.Reverse();
            fixture.Request.Shape.Cells.Reverse();
            foreach (var group in fixture.Request.EntranceDomain.Configurations.SelectMany(configuration => configuration.Groups))
                group.RootCells.Reverse();
            var replay = Plan(manager, fixture, budgets: Budgets with
            {
                MaximumConfigurationProbes = first.Attempts.Count, MaximumOrderProbes = first.OrderProbes,
                MaximumReportedGraphExpansions = first.ExpandedCells, MaximumFurnishingProbes = first.FurnishingProbes,
            }, themeId: "KsProcgenMandatoryOfficeFixture");
            AssertCandidate(replay);
            Assert.That(replay.SemanticHash, Is.EqualTo(first.SemanticHash));
            Assert.That(replay.Attempts.Select(attempt => attempt.ConfigurationId), Is.EqualTo(first.Attempts.Select(attempt => attempt.ConfigurationId)));
            Assert.That(replay.Attempts, Is.Not.InstanceOf<List<KsProcgenOwnedConfigurationAttempt>>());
            fixture.Request.EntranceDomain.Configurations.Clear();
            Assert.That(replay.Attempts.Count, Is.EqualTo(2));
        });
    }

    [Test]
    public async Task ExhaustedAlternativesExposeOnlyFailureDiagnostics()
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var result = Plan(Pair.Server.ResolveDependency<IPrototypeManager>(), Alternatives(), themeId: "KsMissingOwnedTheme");
            AssertFailure(result, KsProcgenOwnedPlanStatus.Rejected);
            Assert.That(result.Attempts.Count, Is.EqualTo(2));
            Assert.That(result.Attempts.All(attempt => attempt.Status == KsProcgenOwnedPlanStatus.Rejected), Is.True);
            Assert.That(result.Stages.Select(stage => stage.ConfigurationId).Distinct().Count(), Is.EqualTo(2));
        });
    }

    [Test]
    public async Task MalformedExteriorContextStopsWithoutTryingAnotherAlternative()
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var facts = new KsProcgenExteriorWindowFact[]
                { new(new(2, 5), new(0, 1), true, true), new(new(2, 5), new(0, 1), true, true) };
            var result = Plan(Pair.Server.ResolveDependency<IPrototypeManager>(), Alternatives(narrow: false), exteriorFacts: facts);
            AssertFailure(result, KsProcgenOwnedPlanStatus.InvalidInput, code: "InvalidOwnedWindowFacts");
            Assert.That(result.Attempts.Count, Is.EqualTo(1));
        });
    }

    private static Input Alternatives(bool narrow = true)
    {
        var fixture = Fixture();
        var separate = fixture.Request.EntranceDomain!.Configurations[0];
        separate.Weight = 1_000_000;
        if (narrow)
        {
            separate.Groups[0].RootCells = fixture.Request.Shape.Cells.Where(cell => cell.Y <= 2).ToList();
            separate.Groups[1].RootCells = fixture.Request.Shape.Cells.Where(cell => cell.Y == 4).ToList();
        }
        fixture.Request.EntranceDomain.Configurations.Add(new() { Id = "Joined", Weight = 0.000001f,
            Groups = new List<KsProcgenEntranceGroupSpec> { new() { Id = "All", Ports = new List<string> { "A1", "B1" } } } });
        return fixture;
    }

    [Test]
    public async Task RequiredNearRelationsConsumeTheSameRemainingGraphAllowance()
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var manager = Pair.Server.ResolveDependency<IPrototypeManager>();
            var fixture = Fixture();
            var full = Plan(manager, fixture, themeId: "KsProcgenNearThemeFixture");
            AssertCandidate(full);
            Assert.That(full.Furnishings.Sum(room => room.Proposal.RelationPathExpandedCells), Is.GreaterThan(0));
            Assert.That(full.Furnishings.SelectMany(room => room.Proposal.RelationWitnesses).Where(witness => witness.Kind == KsProcgenRelationKind.Near)
                .All(witness => witness.CleanPath.Cells.Count > 0), Is.True);
            var beforeFurnishing = full.Stages.TakeWhile(stage => stage.Stage != KsProcgenOwnedPlanStage.Furnishing)
                .Sum(stage => stage.ExpandedCells);
            var limited = Plan(manager, fixture, budgets: Budgets with { MaximumReportedGraphExpansions = beforeFurnishing },
                themeId: "KsProcgenNearThemeFixture");
            AssertFailure(limited, KsProcgenOwnedPlanStatus.BudgetExceeded, code: "FurnishingCleanPathBudget");
            Assert.That(limited.Stages.Last().Stage, Is.EqualTo(KsProcgenOwnedPlanStage.Furnishing));
            Assert.That(limited.ExpandedCells, Is.EqualTo(beforeFurnishing));
        });
    }

    [Test]
    public async Task CleanFurnishingAccessConsumesTheSharedRequestAllowance()
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var manager = Pair.Server.ResolveDependency<IPrototypeManager>();
            var fixture = Fixture();
            var full = Plan(manager, fixture);
            AssertCandidate(full);
            var proposals = full.Furnishings.Select(room => room.Proposal).ToArray();
            Assert.That(proposals.Sum(proposal => proposal.CleanPathExpandedCells), Is.GreaterThan(0));
            Assert.That(full.Stages.Where(stage => stage.Stage == KsProcgenOwnedPlanStage.Furnishing)
                .Sum(stage => stage.ExpandedCells), Is.EqualTo(proposals.Sum(proposal =>
                    proposal.CleanPathExpandedCells + proposal.RelationPathExpandedCells)));
            var before = full.Stages.TakeWhile(stage => stage.Stage != KsProcgenOwnedPlanStage.Furnishing)
                .Sum(stage => stage.ExpandedCells);
            var first = proposals[0].CleanPathExpandedCells + proposals[0].RelationPathExpandedCells;
            Assert.That(first, Is.GreaterThan(1));
            var allowance = first / 2;
            var limited = Plan(manager, fixture,
                budgets: Budgets with { MaximumReportedGraphExpansions = before + allowance });
            AssertFailure(limited, KsProcgenOwnedPlanStatus.BudgetExceeded, code: "FurnishingCleanPathBudget");
            Assert.That(limited.ExpandedCells, Is.EqualTo(before + allowance));
            Assert.That(limited.Stages.Last().Stage, Is.EqualTo(KsProcgenOwnedPlanStage.Furnishing));
            Assert.That(limited.Stages.Last().ExpandedCells, Is.EqualTo(allowance));
        });
    }

    [Test]
    public async Task ExteriorWindowsAreComposedBeforeContentsAndChangeReplayIdentity()
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var manager = Pair.Server.ResolveDependency<IPrototypeManager>();
            var fixture = Fixture(envelope: true);
            var wallOnly = Plan(manager, fixture);
            AssertCandidate(wallOnly);
            fixture.Request.WindowGoal.ExteriorWindowFraction = 1;
            fixture.Request.WindowGoal.MinimumCount = 1;
            var facts = new KsProcgenExteriorWindowFact[] { new(new(2, 5), new(0, 1), true, true) };
            var result = Plan(manager, fixture, exteriorFacts: facts);
            AssertCandidate(result);
            Assert.That(result.Structure!.Windows.Select(window => window.Cell), Is.EqualTo(new[] { new Vector2i(2, 5) }));
            Assert.That(result.Structure.RemainingWalls.Select(wall => wall.Cell), Does.Not.Contain(new Vector2i(2, 5)));
            Assert.That(result.SemanticHash, Is.Not.EqualTo(wallOnly.SemanticHash));
            Assert.That(result.Structure.GasClosureVerified, Is.False);
            Assert.That(Plan(manager, fixture, exteriorFacts: facts).SemanticHash, Is.EqualTo(result.SemanticHash));
        });
    }

    [Test]
    public async Task CompleteTwoNetworkInteriorReplaysWithExactSharedAllowances()
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var manager = Pair.Server.ResolveDependency<IPrototypeManager>();
            var fixture = Fixture();
            var result = Plan(manager, fixture);
            AssertCandidate(result);
            Assert.That(result.Partition!.Regions.Count, Is.EqualTo(2));
            Assert.That(result.Furnishings.SelectMany(room => room.Proposal.Entities), Is.Not.Empty);
            Assert.That(result.Lighting!.FixtureCount, Is.GreaterThan(0));
            Assert.That(result.Stages.Select(stage => stage.Stage).Distinct(), Is.EquivalentTo(Enum.GetValues<KsProcgenOwnedPlanStage>()));
            Assert.That(result.Stages.All(stage => stage.Completed), Is.True);
            Assert.That(result.ExpandedCells, Is.EqualTo(result.Stages.Sum(stage => stage.ExpandedCells)));
            Assert.That(result.FurnishingProbes, Is.EqualTo(result.Stages.Where(stage => stage.Stage == KsProcgenOwnedPlanStage.Furnishing)
                .Sum(stage => stage.CandidateProbes)));
            fixture.Request.Shape.Cells.Reverse();
            fixture.Request.EntranceDomain!.Entrances.Reverse();
            var replay = Plan(manager, fixture, budgets: Budgets with
            {
                MaximumReportedGraphExpansions = result.ExpandedCells,
                MaximumFurnishingProbes = result.FurnishingProbes,
                MaximumLightingFixtures = result.Lighting.FixtureCount,
            });
            AssertCandidate(replay);
            Assert.That(replay.SemanticHash, Is.EqualTo(result.SemanticHash));
            fixture.Request.Shape.Cells.Clear();
            Assert.That(result.Floor!.Accepted!.FloorCells, Is.Not.Empty);
            Assert.That(result.Themes!.Regions.All(region => region.Theme.SupportingEntityPackIds is not List<string>), Is.True);
        });
    }

    [Test]
    public async Task FinalAuditExhaustionPublishesNoPartialStageArtifacts()
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var manager = Pair.Server.ResolveDependency<IPrototypeManager>();
            var fixture = Fixture();
            var full = Plan(manager, fixture);
            AssertCandidate(full);
            var limited = Plan(manager, fixture, budgets: Budgets with { MaximumReportedGraphExpansions = full.ExpandedCells - 1 });
            AssertFailure(limited, KsProcgenOwnedPlanStatus.BudgetExceeded);
            Assert.That(limited.ExpandedCells, Is.EqualTo(full.ExpandedCells - 1));
            Assert.That(limited.Stages.Last().Stage, Is.EqualTo(KsProcgenOwnedPlanStage.InteriorAudit));
            Assert.That(limited.Stages.Last().Completed, Is.False);
            Assert.That(limited.FurnishingProbes, Is.EqualTo(full.FurnishingProbes));
        });
    }

    [Test]
    public async Task SharedFixtureExhaustionRetainsWorkWithoutArtifacts()
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var result = Plan(Pair.Server.ResolveDependency<IPrototypeManager>(), Fixture(),
                budgets: Budgets with { MaximumLightingFixtures = 0 });
            AssertFailure(result, KsProcgenOwnedPlanStatus.BudgetExceeded, code: "GroupLightingFixtureBudget");
            Assert.That(result.Stages.Last().Stage, Is.EqualTo(KsProcgenOwnedPlanStage.Lighting));
            Assert.That(result.FurnishingProbes, Is.GreaterThan(0));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task PreservedAndConstantFloorCannotReceiveAnyContentWrite(bool constant)
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var result = Plan(Pair.Server.ResolveDependency<IPrototypeManager>(), Fixture(preserved: true, constant: constant));
            AssertCandidate(result);
            var cell = new Vector2i(0, 0);
            Assert.That(result.Structure!.SourceHull!.InteriorMaterials!.Tiles.Select(tile => tile.Cell), Does.Not.Contain(cell));
            Assert.That(result.Furnishings.SelectMany(room => room.Proposal.Entities).SelectMany(entity => entity.OccupiedCells), Does.Not.Contain(cell));
            Assert.That(result.Lighting!.Regions.SelectMany(room => room.Proposal.Lights).Select(light => light.Cell), Does.Not.Contain(cell));
            Assert.That(result.Furnishings.SelectMany(room => room.Proposal.ProtectedPassageCells), Does.Contain(cell));
        });
    }

    [Test]
    public async Task SparseFurnishingPermissionIsAnAdmissionGate()
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var manager = Pair.Server.ResolveDependency<IPrototypeManager>();
            var fixture = Fixture();
            var budget = Budgets with { MaximumContentRoomCells = 1 };
            var allowed = Plan(manager, fixture, budgets: budget);
            AssertCandidate(allowed);
            Assert.That(allowed.Fallbacks.Select(item => item.Code), Does.Contain("SparseFurnishing"));
            fixture.Request.FallbackPolicy.AllowSparseFurnishing = false;
            var denied = Plan(manager, fixture, budgets: budget);
            AssertFailure(denied, KsProcgenOwnedPlanStatus.FallbackDisallowed, code: "SparseFurnishingDisallowed");
            Assert.That(denied.Fallbacks.Select(item => item.Code), Does.Not.Contain("SparseFurnishing"));
            Assert.That(denied.Stages.Last().Completed, Is.False);
            Assert.That(denied.Stages.Last().ReasonCode, Is.EqualTo("SparseFurnishingDisallowed"));
        });
    }

    [Test]
    public async Task SizeGoalDefinitionsAffectReplayAndDeniedDeviationIsAtomic()
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var manager = Pair.Server.ResolveDependency<IPrototypeManager>();
            var fixture = Fixture();
            fixture.Request.SizeMix.Add(new() { Id = "Large", MinCells = 100, MaxCells = 200, TargetCount = 1 });
            var first = Plan(manager, fixture);
            AssertCandidate(first);
            Assert.That(first.Fallbacks, Does.Contain(new KsProcgenOwnedFallback("RoomSizeDeviation", "Large")));
            fixture.Request.SizeMix[0].MaxCells = 201;
            var second = Plan(manager, fixture);
            AssertCandidate(second);
            Assert.That(second.SizeMix, Is.EqualTo(first.SizeMix));
            Assert.That(second.SemanticHash, Is.Not.EqualTo(first.SemanticHash));
            fixture.Request.FallbackPolicy.AllowRoomSizeShortfall = false;
            var denied = Plan(manager, fixture);
            AssertFailure(denied, KsProcgenOwnedPlanStatus.FallbackDisallowed, code: "RoomSizeShortfallDisallowed");
            Assert.That(denied.Stages.Last().Stage, Is.EqualTo(KsProcgenOwnedPlanStage.Themes));
        });
    }

    [Test]
    public async Task TinyOneByThreeCanRemainAnUnfurnishedConnectedPassage()
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var result = Plan(Pair.Server.ResolveDependency<IPrototypeManager>(), Fixture(tiny: true));
            AssertCandidate(result);
            Assert.That(result.Partition!.FloorCells.Count, Is.EqualTo(3));
            Assert.That(result.Furnishings.SelectMany(room => room.Proposal.Entities), Is.Empty);
            Assert.That(result.Access!.Analysis!.Status, Is.EqualTo(KsProcgenEntranceSceneStatus.Candidate));
        });
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task ZeroSearchAllowancesStopBeforeContent(bool graph)
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var result = Plan(Pair.Server.ResolveDependency<IPrototypeManager>(), Fixture(), budgets: graph
                ? Budgets with { MaximumReportedGraphExpansions = 0 }
                : Budgets with { MaximumConfigurationProbes = 0 });
            AssertFailure(result, KsProcgenOwnedPlanStatus.BudgetExceeded);
            Assert.That(result.Stages.Count, Is.EqualTo(1));
            Assert.That(result.FurnishingProbes, Is.Zero);
        });
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task UnsupportedGeometryOrModeIsRejectedBeforeWork(bool hybrid)
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var fixture = Fixture();
            if (hybrid) fixture.Request.Mode = KsProcgenMode.Hybrid;
            else fixture.Request.GeometryMode = KsProcgenGeometryMode.InteriorFill;
            var result = Plan(Pair.Server.ResolveDependency<IPrototypeManager>(), fixture);
            AssertFailure(result, KsProcgenOwnedPlanStatus.InvalidInput, code: "InvalidOwnedInteriorInput");
            Assert.That(result.Stages, Is.Empty);
        });
    }

    [Test]
    public async Task MissingThemeAndImpossibleWindowsFailAtTheirOwnStages()
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var manager = Pair.Server.ResolveDependency<IPrototypeManager>();
            var fixture = Fixture();
            var theme = Plan(manager, fixture, themeId: "KsMissingOwnedTheme");
            AssertFailure(theme, KsProcgenOwnedPlanStatus.Rejected);
            Assert.That(theme.Stages.Last().Stage, Is.EqualTo(KsProcgenOwnedPlanStage.Themes));
            fixture.Request.WindowGoal.MinimumCount = 1;
            var window = Plan(manager, fixture);
            AssertFailure(window, KsProcgenOwnedPlanStatus.Rejected);
            Assert.That(window.Stages.Last().Stage, Is.EqualTo(KsProcgenOwnedPlanStage.Structure));
            Assert.That(window.FurnishingProbes, Is.Zero);
        });
    }

    [Test]
    public async Task FailedMandatoryFurnishingRetainsCandidateProbeWork()
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var result = Plan(Pair.Server.ResolveDependency<IPrototypeManager>(), Fixture(),
                budgets: Budgets with { MaximumFurnishingProbes = 1 }, themeId: "KsProcgenMandatoryOfficeFixture");
            Assert.That(result.Status, Is.Not.EqualTo(KsProcgenOwnedPlanStatus.Candidate));
            AssertFailure(result, result.Status);
            Assert.That(result.FurnishingProbes, Is.EqualTo(1));
            Assert.That(result.Stages.Last().Stage, Is.EqualTo(KsProcgenOwnedPlanStage.Furnishing));
        });
    }

    private sealed record Input(KsProcgenRequest Request, KsProcgenGroupRouteContext Context);

    private static Input Fixture(bool preserved = false, bool constant = false, bool tiny = false, bool envelope = false)
    {
        var request = new KsProcgenRequest
        {
            RequestId = "OwnedInterior", Seed = 23, ConnectivityPolicy = KsProcgenConnectivityPolicy.PerIsland,
            Shape = new() { Cells = Enumerable.Range(0, tiny ? 3 : 6)
                .SelectMany(x => Enumerable.Range(0, tiny ? 1 : 5).Select(y => new Vector2i(x, y))).ToList() },
            EntranceDomain = new()
            {
                Entrances = new List<KsProcgenEntranceSpec>
                    { new() { PortId = "A1", ThresholdCells = new List<Vector2i> { new(-1, 0) }, InwardNormal = new(1, 0) } },
                Configurations = new List<KsProcgenEntranceConfigurationSpec> { new() { Id = "Separate",
                    Groups = new List<KsProcgenEntranceGroupSpec> { new() { Id = "A", Ports = new List<string> { "A1" } } } } },
            },
        };
        var host = new HashSet<Vector2i> { new(-1, 0), new(-2, 0) };
        if (envelope)
        {
            var target = request.Shape.Cells.ToHashSet();
            request.Shape.EnvelopeCells = target.SelectMany(cell => new[] { cell + new Vector2i(1, 0), cell + new Vector2i(-1, 0),
                cell + new Vector2i(0, 1), cell + new Vector2i(0, -1) }).Where(cell => !target.Contains(cell)).Distinct().ToList();
        }
        if (!tiny)
        {
            request.EntranceDomain.Entrances.Add(new() { PortId = "B1", ThresholdCells = new List<Vector2i> { new(-1, 4) }, InwardNormal = new(1, 0) });
            request.EntranceDomain.Configurations[0].Groups.Add(new() { Id = "B", Ports = new List<string> { "B1" } });
            host.UnionWith(new[] { new Vector2i(-1, 4), new Vector2i(-2, 4) });
        }
        if (preserved)
        {
            if (constant) request.ConstantRegions.Add(new() { Id = "Entry", SourceId = "Fixture", ContentFingerprint = "Declared",
                LocalCells = new List<Vector2i> { new(0, 0) } });
            else request.Shape.PreservedCells.Add(new(0, 0));
        }
        return new(request, new(preserved ? new HashSet<Vector2i> { new(0, 0) } : new HashSet<Vector2i>(),
            host, new HashSet<Vector2i>(), true, true));
    }

    private static KsProcgenOwnedInteriorResult Plan(IPrototypeManager manager, Input fixture,
        KsProcgenOwnedPlanBudgets? budgets = null, string themeId = "KsProcgenSimpleOffice",
        IReadOnlyList<KsProcgenExteriorWindowFact>? exteriorFacts = null) =>
        KsProcgenOwnedInteriorPlanner.Plan(manager, fixture.Request, themeId, fixture.Context, exteriorFacts ?? [], budgets: budgets ?? Budgets);

    private static void AssertCandidate(KsProcgenOwnedInteriorResult result)
    {
        Assert.That(result.Status, Is.EqualTo(KsProcgenOwnedPlanStatus.Candidate), result.Issue?.Code);
        Assert.That(result.SemanticHash, Is.Not.Zero);
        Assert.That(result.Access, Is.Not.Null);
        Assert.That(result.Published || result.EngineAccessVerified || result.WorkingLightingVerified ||
            result.GasClosureVerified || result.GraphWorkFullyMetered, Is.False);
    }

    private static void AssertFailure(KsProcgenOwnedInteriorResult result, KsProcgenOwnedPlanStatus status, string? code = null)
    {
        Assert.That(result.Status, Is.EqualTo(status), result.Issue?.Code);
        if (code != null) Assert.That(result.Issue!.Code, Is.EqualTo(code));
        Assert.That(result.Floor, Is.Null); Assert.That(result.Partition, Is.Null); Assert.That(result.Themes, Is.Null);
        Assert.That(result.Structure, Is.Null); Assert.That(result.Furnishings, Is.Empty); Assert.That(result.Lighting, Is.Null);
        Assert.That(result.Access, Is.Null); Assert.That(result.SizeMix, Is.Empty); Assert.That(result.SemanticHash, Is.Zero);
        Assert.That(result.Published, Is.False);
    }
}
