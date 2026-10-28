#nullable enable
using System.Linq;
using Content.Server._KS14.Procedural;
using Content.Shared._KS14.Procedural;
using Content.Tests.Shared._KS14.Procedural;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.IntegrationTests.Tests._KS14.Procedural;

public sealed class KsProcgenHoleGenerationTests : KsProcgenGameTest
{
    [TestCase(false, false, 1)] [TestCase(true, false, 1)] [TestCase(false, true, 1)] [TestCase(false, true, 3)]
    public async Task PrimaryGeneratorBuildsExactRoomSizesAroundCourtyards(bool transpose, bool threeBands, int smallRoomCount)
    {
        await AssertOnServer(() =>
        {
            var request = KsProcgenHoleFootprintFixtures.Create(transpose: transpose, threeBands: threeBands, smallRoomCount: smallRoomCount);
            request.FallbackPolicy.AllowMergedPartition = false;
            request.FallbackPolicy.AllowRoomSizeShortfall = false;
            var stages = Server.System<KsProcgenOwnedStageSystem>();
            var before = SEntMan.GetEntities().ToHashSet();
            var result = stages.TryGenerate(request, "KsProcgenEmptyOfficeThemeFixture",
                planningBudgets: new() { UseSizeGoals = true, PreferredMaximumRoomCells = 128, MaximumPartitionMerges = 0 });
            try
            {
                Assert.That(result.Stage, Is.Not.Null, result.Issue?.Code);
                Assert.That(result.Proposal!.Partition!.SizeGoalSubdivisionUsed, Is.True);
                Assert.That(result.Proposal.SizeMix.All(goal => goal.AchievedCount == goal.RequestedCount), Is.True);
                Assert.That(result.Proposal.Fallbacks.Any(fallback => fallback.Code is "MergedPartition" or "RoomSizeDeviation"), Is.False);
                var sizes = result.Proposal.Themes!.Regions.Select(region => region.FloorCells.Count).Order().ToArray();
                Assert.That(sizes, Is.EqualTo(threeBands ? Enumerable.Repeat(16, smallRoomCount).Concat(new[] { 23, 35 }).Order().ToArray() : new[] { 28, 33 }));
                var initialized = stages.TryInitialize(result.Stage!);
                Assert.That(initialized.Status, Is.EqualTo(KsProcgenOwnedStageStatus.InitializedPreview), initialized.Issue?.Code);
                Assert.That(stages.Verify(result.Stage!), Is.True);
                var maps = Server.System<SharedMapSystem>();
                var grid = SEntMan.GetComponent<MapGridComponent>(result.Stage!.GridUid);
                foreach (var cell in request.Shape.VoidCells)
                    Assert.That(maps.GetTileRef(result.Stage.GridUid, grid, cell).Tile.TypeId, Is.Zero, cell.ToString());
                Assert.That(result.Stage.Scene.Tiles.Select(tile => tile.Cell).Intersect(request.Shape.VoidCells), Is.Empty);
            }
            finally { stages.DiscardAll(); }
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    [Test]
    public async Task SeparateCourtyardsMeetGlobalRoomCountsThroughThePrimaryGenerator()
    {
        await AssertOnServer(() =>
        {
            var request = KsProcgenHoleFootprintFixtures.Create(separateNetworks: true);
            request.FallbackPolicy.AllowMergedPartition = false;
            request.FallbackPolicy.AllowRoomSizeShortfall = false;
            var stages = Server.System<KsProcgenOwnedStageSystem>();
            var before = SEntMan.GetEntities().ToHashSet();
            var result = stages.TryGenerate(request, "KsProcgenEmptyOfficeThemeFixture",
                planningBudgets: new() { UseSizeGoals = true, PreferredMaximumRoomCells = 128, MaximumPartitionMerges = 0 });
            try
            {
                Assert.That(result.Stage, Is.Not.Null, result.Issue?.Code);
                Assert.That(result.Proposal!.SizeMix.All(goal => goal.AchievedCount == goal.RequestedCount), Is.True);
                Assert.That(result.Proposal.Themes!.Regions.Select(region => region.FloorCells.Count).Order(),
                    Is.EqualTo(new[] { 28, 28, 33, 33 }));
                Assert.That(stages.TryInitialize(result.Stage!).Status, Is.EqualTo(KsProcgenOwnedStageStatus.InitializedPreview));
                Assert.That(stages.Verify(result.Stage!), Is.True);
            }
            finally { stages.DiscardAll(); }
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }
}
