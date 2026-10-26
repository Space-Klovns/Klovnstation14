#nullable enable
using System.Linq;
using Content.Server._KS14.Procedural;
using Content.Shared._KS14.Procedural;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._KS14.Procedural;

[TestOf(typeof(KsProcgenFurnishingPlanner))]
public sealed class KsProcgenFurnishingDensityTests : KsProcgenGameTest
{
    [TestCase(false)] [TestCase(true)]
    public async Task ZeroDensitySkipsOptionalFillAndPreservesMandatoryCopies(bool required)
    {
        await AssertOnServer(() =>
        {
            var stages = Pair.Server.System<KsProcgenOwnedStageSystem>(); var before = SEntMan.GetEntities().ToHashSet();
            var spec = new KsProcgenContentDraftSpec
            {
                DocumentId = "Density",
                Themes = new() { ["Density"] = new() { Parent = "KsProcgenNoDoorThemeFixture",
                    Goals = new() { FurnishingDensity = 0f, LightingCoverage = 0f },
                    EntityPacks = new() { new() { Pack = "Contents" } } } },
                Entities = new() { ["Contents"] = new() { Assemblies = new() { new()
                    { Id = "Paper", Assembly = "Paper", MinimumCount = required ? 1 : 0 } } } },
                Assemblies = new() { ["Paper"] = new() { AnchorMember = "Paper", Members = new() { new()
                    { Id = "Paper", Entity = "Paper", Movement = KsProcgenMovementClass.Clear } } } },
            };
            Assert.That(KsProcgenContentDraft.TrySnapshot(spec, out var draft, out var issue), Is.True, issue?.Code);
            Assert.That(KsProcgenThemeCatalog.TryCapture(SProtoMan, "Density", out var catalog, out issue, assets: draft), Is.True, issue?.Code);
            try
            {
                var request = new KsProcgenRequest { RequestId = "Density", Seed = 17,
                    ConnectivityPolicy = KsProcgenConnectivityPolicy.PerIsland,
                    Shape = new() { Cells = Enumerable.Range(0, 3).Select(x => new Vector2i(x, 0)).ToList() } };
                var generated = stages.TryGenerate(request, "Density", content: catalog,
                    planningBudgets: new() { MinimumRoomCells = 1, PreferredMaximumRoomCells = 64 });
                Assert.That(generated.Status, Is.EqualTo(KsProcgenOwnedStageStatus.PositionedPreview), generated.Issue?.Code);
                Assert.That(generated.Proposal!.Furnishings.Sum(region => region.Proposal.Entities.Count), Is.EqualTo(required ? 1 : 0));
                if (!required) Assert.That(generated.Proposal.Furnishings.Sum(region => region.Proposal.CandidateProbes), Is.Zero);
                Assert.That(stages.Verify(generated.Stage!), Is.True);
            }
            finally { stages.DiscardAll(); }
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }
}
