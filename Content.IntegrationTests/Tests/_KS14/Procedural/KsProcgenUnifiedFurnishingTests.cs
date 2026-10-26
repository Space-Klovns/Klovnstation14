#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.Server._KS14.Procedural;
using Content.Shared._KS14.Procedural;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._KS14.Procedural;

[TestOf(typeof(KsProcgenOwnedInteriorPlanner))]
public sealed class KsProcgenUnifiedFurnishingTests : KsProcgenGameTest
{
    [TestCase("APCBasic", true)]
    [TestCase("SubstationWallBasic", true)]
    [TestCase("SubstationBasic", false)]
    public async Task OrdinaryPackUsesItsNativeMountLayerWithoutAnElectricalProfile(string prototype, bool wall)
    {
        await AssertOnServer(() =>
        {
            var stages = Pair.Server.System<KsProcgenOwnedStageSystem>();
            var before = SEntMan.GetEntities().ToHashSet();
            try
            {
                var generated = stages.TryGenerate(Request(), "Unified", planningBudgets: Budgets(),
                    content: Catalog(wall ? 0.5f : 0f, new Core("Machine", prototype, Minimum: 1, Approach: true)));
                Assert.That(generated.Status, Is.EqualTo(KsProcgenOwnedStageStatus.PositionedPreview), generated.Issue?.Code);
                var layered = generated.Proposal!.Furnishings.Single().Proposal.Layered!;
                Assert.That(layered, Is.Not.Null);
                var pose = layered.Access.Poses!.Members.Single();
                Assert.That(pose.Support.Layer, Is.EqualTo(wall ? KsProcgenPlacementLayer.Wall : KsProcgenPlacementLayer.Floor));
                var cell = new Vector2i((int) (pose.Position.X - 0.5f), (int) (pose.Position.Y - 0.5f));
                Assert.That(wall ? generated.Proposal.Structure!.RemainingWalls.Any(item => item.Cell == cell) :
                    generated.Proposal.Partition!.FloorCells.Contains(cell), Is.True);
                Assert.That(layered.Access.Access.Count, Is.EqualTo(1));
                Assert.That(stages.TryInitialize(generated.Stage!).Status, Is.EqualTo(KsProcgenOwnedStageStatus.InitializedPreview));
                Assert.That(stages.Verify(generated.Stage!), Is.True);
            }
            finally { stages.DiscardAll(); }
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    [TestCase(false)] [TestCase(true)]
    public async Task ZeroDensitySuppressesOptionalOrdinaryCoresAndPreservesMinima(bool required)
    {
        await AssertOnServer(() =>
        {
            var before = SEntMan.GetEntities().ToHashSet();
            var result = Propose(Catalog(0f, new Core("Table", "Table", Minimum: required ? 1 : 0)));
            Assert.That(result.Status, Is.EqualTo(KsProcgenOwnedPlanStatus.Candidate), result.Issue?.Code);
            var furnishing = result.Furnishings.Single().Proposal;
            Assert.That(furnishing.Entities.Count, Is.EqualTo(required ? 1 : 0));
            if (required) Assert.That(furnishing.Layered!.Bindings.Single().CoreId, Is.EqualTo("Table"));
            else Assert.That(furnishing.CandidateProbes, Is.Zero);
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    [TestCase(0.01f, 1, 2)] [TestCase(0.25f, 4, 4)]
    public async Task InitialCoresAndDensityRepeatsRemainCompleteAndDeterministic(float density, int target, int placed)
    {
        await AssertOnServer(() =>
        {
            var before = SEntMan.GetEntities().ToHashSet();
            var catalog = Catalog(density, new("Alpha", "Table"), new("Beta", "Table"));
            var result = Propose(catalog);
            Assert.That(result.Status, Is.EqualTo(KsProcgenOwnedPlanStatus.Candidate), result.Issue?.Code);
            var furnishing = result.Furnishings.Single().Proposal;
            Assert.That(furnishing.TargetDominantClusters, Is.EqualTo(target));
            Assert.That(furnishing.PlacedDominantClusters, Is.EqualTo(placed));
            var bindings = furnishing.Layered!.Bindings;
            Assert.That(bindings.Select(binding => binding.CoreId).Distinct(), Is.EquivalentTo(new[] { "Alpha", "Beta" }));
            Assert.That(bindings.Count, Is.EqualTo(placed));
            Assert.That(bindings.Select(binding => (binding.CoreId, binding.CopyIndex)).Distinct().Count(), Is.EqualTo(placed));
            Assert.That(furnishing.Entities.Select(entity => entity.ClusterIndex).Distinct().Count(), Is.EqualTo(placed));
            var replay = Propose(catalog);
            Assert.That(replay.SemanticHash, Is.EqualTo(result.SemanticHash));
            Assert.That(replay.Furnishings.Single().Proposal.CandidateProbes, Is.EqualTo(furnishing.CandidateProbes));
            Assert.That(furnishing.CandidateProbes, Is.LessThanOrEqualTo(4096));
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    [TestCase(false)] [TestCase(true)]
    public async Task FailedOptionalAdditionRetainsTheRequiredWholeRoomAndItsWork(bool shortBudget)
    {
        await AssertOnServer(() =>
        {
            var before = SEntMan.GetEntities().ToHashSet();
            var cores = new[] { new Core("Small", "Table", Minimum: 1), new Core("Oversized", "Table", Width: 9) };
            var initial = Propose(Catalog(0f, cores));
            Assert.That(initial.Status, Is.EqualTo(KsProcgenOwnedPlanStatus.Candidate), initial.Issue?.Code);
            var baselineWork = initial.Furnishings.Single().Proposal.CandidateProbes;
            var budget = new KsProcgenOwnedPlanBudgets { PreferredMaximumRoomCells = 64,
                MaximumFurnishingProbes = shortBudget ? baselineWork + 6 : 4096 };
            var result = Propose(Catalog(0.01f, cores), budgets: budget);
            Assert.That(result.Status, Is.EqualTo(KsProcgenOwnedPlanStatus.Candidate), result.Issue?.Code);
            var furnishing = result.Furnishings.Single().Proposal;
            Assert.That(furnishing.Status, Is.EqualTo(KsProcgenFurnishingStatus.Sparse));
            Assert.That(furnishing.Layered, Is.Not.Null);
            Assert.That(furnishing.Layered!.Bindings.Single().CoreId, Is.EqualTo("Small"));
            Assert.That(furnishing.CandidateProbes, Is.GreaterThan(baselineWork).And.LessThanOrEqualTo(budget.MaximumFurnishingProbes));
            Assert.That(furnishing.PreferenceSearchTruncated, Is.EqualTo(shortBudget));
            Assert.That(furnishing.Omissions, Has.Count.EqualTo(1));
            Assert.That(furnishing.Omissions.Single().Reason, shortBudget ? Does.Contain("Budget") : Is.EqualTo("CoreCannotFit/Oversized"));
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    [Test]
    public async Task SupportedChildrenDoNotMultiplyTheirRootsDensityArea()
    {
        await AssertOnServer(() =>
        {
            var spec = Spec(0.125f, new[] { new Core("Station", "Table", Minimum: 1) });
            spec.Assemblies["Station"].Members.Add(new() { Id = "Microwave", Entity = "KitchenMicrowave",
                Role = KsProcgenEntityRole.Equipment, RequiresInteractionApproach = true });
            spec.Assemblies["Station"].Relations.Add(new() { Id = "Top", Subject = "Microwave", Target = "Root", Kind = KsProcgenRelationKind.OnSurface });
            var result = Propose(Capture(spec));
            Assert.That(result.Status, Is.EqualTo(KsProcgenOwnedPlanStatus.Candidate), result.Issue?.Code);
            var furnishing = result.Furnishings.Single().Proposal;
            Assert.That(furnishing.TargetDominantClusters, Is.EqualTo(2));
            Assert.That(furnishing.PlacedDominantClusters, Is.EqualTo(2));
            Assert.That(furnishing.Entities.Count, Is.EqualTo(2));
            Assert.That(furnishing.Layered!.Bindings.Count, Is.EqualTo(4));
            Assert.That(furnishing.Layered.Access.Poses!.Members.Count(member => member.Support.Layer == KsProcgenPlacementLayer.Surface), Is.EqualTo(2));
        });
    }

    [Test]
    public async Task WallGroupsDoNotSatisfyTheFloorDensityDemandOfAMixedPack()
    {
        await AssertOnServer(() =>
        {
            var result = Propose(Catalog(0.125f, new("Wall", "APCBasic", Minimum: 1, Approach: true), new("Desk", "Table", Minimum: 1)));
            Assert.That(result.Status, Is.EqualTo(KsProcgenOwnedPlanStatus.Candidate), result.Issue?.Code);
            var furnishing = result.Furnishings.Single().Proposal;
            Assert.That(furnishing.TargetDominantClusters, Is.EqualTo(3));
            Assert.That(furnishing.PlacedDominantClusters, Is.EqualTo(3));
            Assert.That(furnishing.Entities.Count, Is.EqualTo(2));
            Assert.That(furnishing.Layered!.Bindings.Count(binding => binding.CoreId == "Wall"), Is.EqualTo(1));
            Assert.That(furnishing.Layered.Bindings.Count(binding => binding.CoreId == "Desk"), Is.EqualTo(2));
        });
    }

    [TestCase(false)] [TestCase(true)]
    public async Task OptionalSupportingPackDependsOnAnActuallyPlacedDominantCore(bool fits)
    {
        await AssertOnServer(() =>
        {
            var spec = Spec(0.01f, new[] { new Core("Activity", "Table", Width: fits ? 1 : 9) });
            spec.Entities["Contents"].ActivityTags.Add("Activity");
            spec.Entities["Contents"].CompatibleSupportTags.Add("Support");
            spec.Themes["Unified"].EntityPacks!.Add(new() { Pack = "Support" });
            spec.Entities.Add("Support", new() { Tags = new() { "Support" },
                Entries = new() { new() { Id = "Extra", Entity = "Table", Movement = KsProcgenMovementClass.VaultRequired } } });
            var result = Propose(Capture(spec));
            Assert.That(result.Status, Is.EqualTo(KsProcgenOwnedPlanStatus.Candidate), result.Issue?.Code);
            var furnishing = result.Furnishings.Single().Proposal;
            Assert.That(result.Themes!.Regions.Single().Theme.DominantEntityPackId, Is.EqualTo("Contents"));
            Assert.That(furnishing.Entities.Count, Is.EqualTo(fits ? 2 : 0));
            if (!fits) Assert.That(furnishing.Omissions.Any(item => item.Reason == "DominantPackUnavailable"), Is.True);
        });
    }

    [Test]
    public async Task WallContentCannotUseAnotherIslandsSupportWalls()
    {
        await AssertOnServer(() =>
        {
            var result = Propose(Catalog(0f, new Core("Apc", "APCBasic", Minimum: 1)), request: Request(islands: true));
            Assert.That(result.Status, Is.EqualTo(KsProcgenOwnedPlanStatus.Candidate), result.Issue?.Code);
            Assert.That(result.Furnishings.Count, Is.EqualTo(2));
            foreach (var furnished in result.Furnishings)
            {
                var floor = result.Themes!.Regions.Single(region => region.Id == furnished.RegionId).FloorCells;
                var pose = furnished.Proposal.Layered!.Access.Poses!.Members.Single();
                var cell = new Vector2i((int) (pose.Position.X - 0.5f), (int) (pose.Position.Y - 0.5f));
                Assert.That(floor.Any(tile => Math.Abs(tile.X - cell.X) + Math.Abs(tile.Y - cell.Y) == 1), Is.True);
            }
        });
    }

    private sealed record Core(string Id, string Entity, int Minimum = 0, int Width = 1, bool Approach = false);
    private KsProcgenThemeCatalog Catalog(float density, params Core[] cores) => Capture(Spec(density, cores));
    private KsProcgenThemeCatalog Capture(KsProcgenContentDraftSpec spec)
    {
        Assert.That(KsProcgenContentDraft.TrySnapshot(spec, out var draft, out var issue), Is.True, issue?.Code);
        Assert.That(KsProcgenThemeCatalog.TryCapture(SProtoMan, "Unified", out var catalog, out issue, assets: draft), Is.True, issue?.Code);
        return catalog!;
    }
    private static KsProcgenContentDraftSpec Spec(float density, Core[] cores) => new()
    {
        DocumentId = "UnifiedFurnishing", Themes = new() { ["Unified"] = new()
        {
            Parent = "KsProcgenSimpleOffice", LightingPacks = new(),
            Goals = new() { FurnishingDensity = density, LightingCoverage = 0f },
            EntityPacks = new() { new() { Pack = "Contents" } },
        } },
        Entities = new() { ["Contents"] = new() { Assemblies = cores.Select(core => new KsProcgenAssemblyReference
            { Id = core.Id, Assembly = core.Id, MinimumCount = core.Minimum }).ToList() } },
        Assemblies = cores.ToDictionary(core => core.Id, core => new KsProcgenAssemblySpec
        {
            AnchorMember = "Root", Members = new() { new() { Id = "Root", Entity = core.Entity,
                Movement = KsProcgenMovementClass.VaultRequired, RequiresInteractionApproach = core.Approach,
                Role = core.Approach ? KsProcgenEntityRole.Equipment : KsProcgenEntityRole.Decoration,
                Footprint = Enumerable.Range(0, core.Width).Select(x => new Vector2i(x, 0)).ToList() } },
        }),
    };
    private KsProcgenOwnedInteriorResult Propose(KsProcgenThemeCatalog catalog, KsProcgenOwnedPlanBudgets? budgets = null,
        KsProcgenRequest? request = null) => Pair.Server.System<KsProcgenPlanningSystem>().Propose(request ?? Request(), "Unified",
        new(new HashSet<Vector2i>(), new HashSet<Vector2i>(), new HashSet<Vector2i>(), true, true)
            { ExteriorIsEmptySpace = true }, [], budgets: budgets ?? Budgets(), content: catalog);
    private static KsProcgenOwnedPlanBudgets Budgets() => new() { PreferredMaximumRoomCells = 64, MaximumFurnishingProbes = 4096 };
    private static KsProcgenRequest Request(bool islands = false)
    {
        var floor = Enumerable.Range(0, 4).SelectMany(x => Enumerable.Range(0, 4).Select(y => new Vector2i(x, y)))
            .SelectMany(cell => islands ? new[] { cell, cell + new Vector2i(12, 0) } : new[] { cell }).ToHashSet();
        var envelope = floor.SelectMany(cell => Enumerable.Range(-1, 3).SelectMany(x => Enumerable.Range(-1, 3)
            .Select(y => cell + new Vector2i(x, y)))).Where(cell => !floor.Contains(cell)).ToHashSet();
        return new() { RequestId = "Unified", Seed = 17, ConnectivityPolicy = KsProcgenConnectivityPolicy.PerIsland,
            EntranceDomain = new(), Shape = new() { Cells = floor.ToList(), EnvelopeCells = envelope.ToList() } };
    }
}
