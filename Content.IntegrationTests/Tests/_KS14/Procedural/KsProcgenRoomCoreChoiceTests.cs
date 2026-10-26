#nullable enable
using System.Linq;
using System.Numerics;
using Content.Server._KS14.Procedural;
using Content.Shared._KS14.Procedural;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._KS14.Procedural;

[TestOf(typeof(KsProcgenRoomCoreSearch))]
public sealed class KsProcgenRoomCoreChoiceTests : KsProcgenGameTest
{
    [TestCase(false)] [TestCase(true)]
    public async Task OwnedRoomRetriesWholeWeightedCoresBeforeDroppingOrRejectingAPack(bool required)
    {
        await AssertOnServer(() =>
        {
            var stages = Pair.Server.System<KsProcgenOwnedStageSystem>(); var before = SEntMan.GetEntities().ToHashSet();
            try
            {
                var result = stages.TryGenerate(new() { RequestId = "CoreChoices", Seed = 17,
                    ConnectivityPolicy = KsProcgenConnectivityPolicy.PerIsland,
                    Shape = new() { AddRectangles = new() { new() { Min = new(0, 0), Max = new(6, 6) } } } },
                    "Choices", content: Catalog(required), planningBudgets: new() { PreferredMaximumRoomCells = 64 });
                Assert.That(result.Status, Is.EqualTo(KsProcgenOwnedStageStatus.PositionedPreview), result.Issue?.Code);
                Assert.That(result.Proposal!.Furnishings.Count(region => region.Proposal.Layered != null), Is.EqualTo(1));
                var room = result.Proposal!.Furnishings.Single(region => region.Proposal.Layered != null).Proposal.Layered!;
                Assert.That(room.Bindings.Count, Is.EqualTo(2));
                Assert.That(room.Bindings.All(binding => binding.CoreId == "Good" && binding.AssemblyId == "GoodAssembly"), Is.True);
                Assert.That(result.Stage!.Assemblies.Single().Count, Is.EqualTo(2));
                Assert.That(stages.TryInitialize(result.Stage).Status, Is.EqualTo(KsProcgenOwnedStageStatus.InitializedPreview));
                Assert.That(stages.Verify(result.Stage), Is.True);
            }
            finally { stages.DiscardAll(); }
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    [Test]
    public async Task WholeChoiceSearchSharesItsAllowanceAndCannotPublishAnEarlierFailedBranch()
    {
        await AssertOnServer(() =>
        {
            var before = SEntMan.GetEntities().ToHashSet(); var cores = Catalog(true).EntityPacks.Single().Cores;
            var choices = new[] { new KsProcgenRoomCoreChoice(cores.OrderBy(core => core.Id)
                .Select(core => new KsProcgenRoomCoreSelection("Contents", core, 0)).ToArray()) };
            var result = KsProcgenRoomCoreSearch.SearchChoices(SProtoMan, SEntMan.ComponentFactory, "ChoiceBudget", choices, Room(), 17,
                maximumChoiceProbes: 2, requireConnectedCleanFloor: true);
            Assert.That(result.Status, Is.EqualTo(KsProcgenSupportedAccessStatus.Candidate), result.Issue?.Code);
            Assert.That(result.ChoiceProbes, Is.EqualTo(2)); Assert.That(result.VariantProbes, Is.GreaterThan(1));
            Assert.That(result.Composition!.Bindings.All(binding => binding.CoreId == "Good"), Is.True);
            var shortResult = KsProcgenRoomCoreSearch.SearchChoices(SProtoMan, SEntMan.ComponentFactory, "ChoiceBudget", choices, Room(), 17,
                maximumChoiceProbes: 1, requireConnectedCleanFloor: true);
            Assert.That(shortResult.Status, Is.EqualTo(KsProcgenSupportedAccessStatus.BudgetExceeded));
            Assert.That(shortResult.Issue?.Code, Is.EqualTo("RoomCoreChoiceProbeBudget"));
            Assert.That(shortResult.ChoiceProbes, Is.EqualTo(1));
            Assert.That(shortResult.Composition, Is.Null); Assert.That(shortResult.Placement, Is.Null);
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    [TestCase(2, false)] [TestCase(3, true)]
    public async Task DuplicatePrefixesConsumeWorkWithoutMixingOrDuplicatingWholeInstances(int allowance, bool fits)
    {
        await AssertOnServer(() =>
        {
            var before = SEntMan.GetEntities().ToHashSet();
            var core = Catalog(true).EntityPacks.Single().Cores.Single(core => core.Id == "Good");
            var first = new KsProcgenRoomCoreSelection("Contents", core, 0);
            var second = new KsProcgenRoomCoreSelection("Contents", core, 1);
            var choices = new[] { new KsProcgenRoomCoreChoice(new[] { first }),
                new KsProcgenRoomCoreChoice(new[] { first, second }) };
            var result = KsProcgenRoomCoreSearch.SearchChoices(SProtoMan, SEntMan.ComponentFactory, "DuplicatePrefix", choices, Room(), 17,
                maximumChoiceProbes: allowance, requireConnectedCleanFloor: true);
            Assert.That(result.ChoiceProbes, Is.EqualTo(allowance));
            Assert.That(result.Status, Is.EqualTo(fits ? KsProcgenSupportedAccessStatus.Candidate : KsProcgenSupportedAccessStatus.BudgetExceeded));
            if (fits)
            {
                var bindings = result.Composition!.Bindings;
                Assert.That(bindings.Count, Is.EqualTo(4));
                Assert.That(bindings.All(binding => binding.CoreId == "Good" && binding.AssemblyId == "GoodAssembly"), Is.True);
                Assert.That(bindings.Count(binding => binding.CopyIndex == 0), Is.EqualTo(2));
                Assert.That(bindings.Count(binding => binding.CopyIndex == 1), Is.EqualTo(2));
            }
            else
            {
                Assert.That(result.Issue?.Code, Is.EqualTo("RoomCoreChoiceProbeBudget"));
                Assert.That(result.Composition, Is.Null); Assert.That(result.Placement, Is.Null);
                Assert.That(result.VariantProbes + result.PlacementProbes + result.ExpandedCells + result.LandingProbes, Is.Zero);
            }
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    [TestCase(false)] [TestCase(true)]
    public async Task AChoiceCannotDuplicateAnOptionOrChangeItsPackScope(bool otherPack)
    {
        await AssertOnServer(() =>
        {
            var core = Catalog(true).EntityPacks.Single().Cores.Single(core => core.Id == "Good");
            var choices = new[] { new KsProcgenRoomCoreChoice(new[] { new KsProcgenRoomCoreSelection("Contents", core, 0),
                new KsProcgenRoomCoreSelection(otherPack ? "Other" : "Contents", core, 0) }) };
            var result = KsProcgenRoomCoreSearch.SearchChoices(SProtoMan, SEntMan.ComponentFactory, "InvalidChoice", choices, Room(), 17);
            Assert.That(result.Status, Is.EqualTo(KsProcgenSupportedAccessStatus.InvalidInput));
            Assert.That(result.ChoiceProbes + result.VariantProbes + result.PlacementProbes, Is.Zero);
            Assert.That(result.Composition, Is.Null);
        });
    }

    private KsProcgenThemeCatalog Catalog(bool required)
    {
        KsProcgenAssemblySpec Assembly(bool wide) => new()
        {
            AnchorMember = "Table", Members = new()
            {
                new() { Id = "Table", Entity = "Table", Movement = KsProcgenMovementClass.VaultRequired,
                    Footprint = Enumerable.Range(0, wide ? 9 : 1).Select(x => new Vector2i(x, 0)).ToList() },
                new() { Id = "Microwave", Entity = "KitchenMicrowave", Role = KsProcgenEntityRole.Equipment,
                    RequiresInteractionApproach = true },
            }, Relations = new() { new() { Id = "Top", Subject = "Microwave", Target = "Table", Kind = KsProcgenRelationKind.OnSurface } },
        };
        var spec = new KsProcgenContentDraftSpec
        {
            DocumentId = "CoreChoices", Themes = new() { ["Choices"] = new() { Parent = "KsProcgenNoDoorThemeFixture",
                Goals = new() { FurnishingDensity = 0.01f, LightingCoverage = 0f },
                EntityPacks = new() { new() { Pack = "Contents", MinimumCount = required ? 1 : 0 } } } },
            Entities = new() { ["Contents"] = new() { Assemblies = new()
                { new() { Id = "Bad", Assembly = "BadAssembly", Weight = 1000f }, new() { Id = "Good", Assembly = "GoodAssembly" } } } },
            Assemblies = new() { ["BadAssembly"] = Assembly(true), ["GoodAssembly"] = Assembly(false) },
        };
        Assert.That(KsProcgenContentDraft.TrySnapshot(spec, out var draft, out var issue), Is.True, issue?.Code);
        Assert.That(KsProcgenThemeCatalog.TryCapture(SProtoMan, "Choices", out var catalog, out issue, assets: draft), Is.True, issue?.Code);
        return catalog!;
    }

    private static KsProcgenRoomAccessMask Room() => new(
        Enumerable.Range(0, 6).SelectMany(x => Enumerable.Range(0, 6).Select(y => new Vector2i(x, y))).ToHashSet(),
        new System.Collections.Generic.HashSet<Vector2i>(), new System.Collections.Generic.HashSet<Vector2i>(),
        new System.Collections.Generic.HashSet<Vector2i>(), new System.Collections.Generic.HashSet<Vector2i> { Vector2i.Zero }, new Vector2(0.5f, 0.5f));
}
