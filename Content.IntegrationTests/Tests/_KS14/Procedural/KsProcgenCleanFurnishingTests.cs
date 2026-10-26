#nullable enable
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Shared._KS14.Procedural;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.Procedural;

[TestOf(typeof(KsProcgenFurnishingPlanner))]
public sealed class KsProcgenCleanFurnishingTests : KsProcgenGameTest
{
    [TestCase("KsProcgenSimpleOffice")]
    [TestCase("KsProcgenNearThemeFixture")]
    public async Task AdequateCleanPathAccountingPreservesPlacementsAndReplaysAtTheExactAllowance(string themeId)
    {
        await AssertOnServer(() =>
        {
            var manager = Pair.Server.ResolveDependency<IPrototypeManager>();
            Assert.That(KsProcgenThemeSelector.TrySelect(manager, themeId, 17, "BudgetRoom", out var theme, out _), Is.True);
            var floor = Enumerable.Range(0, 6).SelectMany(x => Enumerable.Range(0, 5)
                .Select(y => new Vector2i(x, y))).ToArray();
            var region = new KsProcgenThemedRegion("BudgetRoom", KsProcgenZoneKind.RoomProposal,
                ["BudgetRoom"], floor, [new(0, 0)], theme!, []);
            var partition = new KsProcgenPartitionResult { Status = KsProcgenPartitionStatus.Proposed, FloorCells = floor };
            var legacy = KsProcgenFurnishingPlanner.Plan(manager, region, partition, 17);
            var metered = KsProcgenFurnishingPlanner.Plan(manager, region, partition, 17,
                maxRelationPathExpandedCells: 65_536, meterCleanPaths: true);
            Assert.That(metered.Status, Is.AnyOf(KsProcgenFurnishingStatus.Proposed, KsProcgenFurnishingStatus.Sparse), metered.Issue?.Code);
            Assert.That(metered.Entities, Is.Not.Empty);
            Assert.That(metered.Entities, Is.EqualTo(legacy.Entities));
            Assert.That(metered.RelationWitnesses, Is.EqualTo(legacy.RelationWitnesses));
            Assert.That(metered.ProtectedPassageCells, Is.EqualTo(legacy.ProtectedPassageCells));
            Assert.That(metered.CleanPathExpandedCells, Is.GreaterThan(1));
            Assert.That(legacy.CleanPathExpandedCells, Is.Zero);
            var total = metered.CleanPathExpandedCells + metered.RelationPathExpandedCells;
            var replay = KsProcgenFurnishingPlanner.Plan(manager, region, partition, 17,
                maxRelationPathExpandedCells: total, meterCleanPaths: true);
            Assert.That(replay.Status, Is.EqualTo(metered.Status));
            Assert.That(replay.Entities, Is.EqualTo(metered.Entities));
            Assert.That(replay.CleanPathExpandedCells, Is.EqualTo(metered.CleanPathExpandedCells));
            Assert.That(replay.RelationPathExpandedCells, Is.EqualTo(metered.RelationPathExpandedCells));
            Assert.That(replay.CleanPathSearchTruncated || replay.RelationPathSearchTruncated, Is.False);
            var empty = KsProcgenFurnishingPlanner.Plan(manager, region, partition, 17,
                maxRelationPathExpandedCells: 0, meterCleanPaths: true);
            Assert.That(empty.Status, Is.EqualTo(KsProcgenFurnishingStatus.BudgetExceeded));
            Assert.That(empty.Issue?.Code, Is.EqualTo("FurnishingCleanPathBudget"));
            Assert.That(empty.Entities, Is.Empty);
            Assert.That(empty.ProtectedPassageCells, Is.Empty);
            Assert.That(empty.CandidateProbes, Is.Zero);
            Assert.That(empty.CleanPathSearchTruncated, Is.True);
            Assert.That(empty.RelationPathSearchTruncated, Is.False);
        });
    }

    [Test]
    public async Task MandatoryBacktrackingChargesDiscardedAccessPathsAndFailsAtomicallyOneExpansionShort()
    {
        await AssertOnServer(() =>
        {
            var manager = Pair.Server.ResolveDependency<IPrototypeManager>();
            Assert.That(KsProcgenThemeSelector.TrySelect(manager, "KsProcgenWeightedCoresThemeFixture", 17,
                "MandatoryBacktracking", out var theme, out _), Is.True);
            var floor = Enumerable.Range(0, 3).SelectMany(x => Enumerable.Range(0, 3)
                .Select(y => new Vector2i(x, y))).Where(cell => cell != new Vector2i(2, 0)).ToArray();
            var walls = new[] { new Vector2i(2, 0), new Vector2i(3, 1) };
            var region = new KsProcgenThemedRegion("MandatoryBacktracking", KsProcgenZoneKind.RoomProposal,
                ["MandatoryBacktracking"], floor, walls, theme! with
                {
                    Content = null,
                    DominantEntityPackId = "KsProcgenMandatoryFlexiblePackFixture",
                    SupportingEntityPackIds = ["KsProcgenRequiredCornerPackFixture"],
                }, []);
            var partition = new KsProcgenPartitionResult
            {
                Status = KsProcgenPartitionStatus.Proposed, FloorCells = floor, WallCells = walls,
            };
            var full = KsProcgenFurnishingPlanner.Plan(manager, region, partition, 17,
                maxRelationPathExpandedCells: 65_536, meterCleanPaths: true);
            Assert.That(full.Status, Is.EqualTo(KsProcgenFurnishingStatus.Proposed), full.Issue?.Code);
            Assert.That(full.MandatoryBacktracks, Is.GreaterThan(0));
            Assert.That(full.CleanPathExpandedCells, Is.GreaterThan(1));
            var total = full.CleanPathExpandedCells + full.RelationPathExpandedCells;
            var replay = KsProcgenFurnishingPlanner.Plan(manager, region, partition, 17,
                maxRelationPathExpandedCells: total, meterCleanPaths: true);
            Assert.That(replay.Entities, Is.EqualTo(full.Entities));
            Assert.That(replay.CleanPathExpandedCells, Is.EqualTo(full.CleanPathExpandedCells));
            Assert.That(replay.MandatoryBacktracks, Is.EqualTo(full.MandatoryBacktracks));
            var shortfall = KsProcgenFurnishingPlanner.Plan(manager, region, partition, 17,
                maxRelationPathExpandedCells: total - 1, meterCleanPaths: true);
            Assert.That(shortfall.Status, Is.EqualTo(KsProcgenFurnishingStatus.BudgetExceeded));
            Assert.That(shortfall.Issue?.Code, Is.EqualTo("FurnishingCleanPathBudget"));
            Assert.That(shortfall.CleanPathExpandedCells + shortfall.RelationPathExpandedCells, Is.EqualTo(total - 1));
            Assert.That(shortfall.CleanPathSearchTruncated, Is.True);
            Assert.That(shortfall.Entities, Is.Empty);
            Assert.That(shortfall.RelationWitnesses, Is.Empty);
            Assert.That(shortfall.ProtectedPassageCells, Is.Empty);
            Assert.That(shortfall.MandatoryBacktracks, Is.GreaterThan(0));
        });
    }
}
