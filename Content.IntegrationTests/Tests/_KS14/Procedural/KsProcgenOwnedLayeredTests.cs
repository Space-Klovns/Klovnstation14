using System.Collections.Generic;
using System.Linq;
using Content.Server._KS14.Procedural;
using Content.Shared._KS14.Procedural;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.Procedural;

[TestOf(typeof(KsProcgenOwnedInteriorPlanner))]
public sealed class KsProcgenOwnedLayeredTests : KsProcgenGameTest
{
    [Test]
    public async Task OwnedCandidateRetainsSupportedDeviceAndProtectsItsOperatingPathFromLights()
    {
        await AssertOnServer(() =>
        {
            var manager = Pair.Server.ResolveDependency<IPrototypeManager>();
            var catalog = Catalog(manager); var request = Request();
            var before = SEntMan.GetEntities().ToHashSet();
            var result = Pair.Server.System<KsProcgenPlanningSystem>().Propose(request, "Layered", Context(), [], budgets: Budgets(), content: catalog);
            Assert.That(result.Status, Is.EqualTo(KsProcgenOwnedPlanStatus.Candidate), result.Issue?.Code);
            var furnished = result.Furnishings.Single(region => region.Proposal.Layered != null);
            var layered = furnished.Proposal.Layered!;
            Assert.That(layered.Assembly.Members.Count, Is.EqualTo(2));
            Assert.That(furnished.Proposal.Entities.Count, Is.EqualTo(1));
            var deviceId = layered.Bindings.Single(binding => binding.SourceMemberId == "Device").MemberId;
            Assert.That(layered.Access.Poses!.Members.Single(pose => pose.Support.MemberId == deviceId).Support.Layer,
                Is.EqualTo(KsProcgenPlacementLayer.Surface));
            Assert.That(layered.Access.Access.Count, Is.GreaterThan(0));
            Assert.That(layered.BlockedCells.Any(cell => layered.ApproachCells.Contains(cell)), Is.False);
            var lights = result.Lighting!.Regions.Single(region => region.RegionId == furnished.RegionId).Proposal.Lights;
            Assert.That(lights.Any(light => layered.OccupiedCells.Contains(light.Cell) ||
                layered.Access.Access.Any(access => access.CleanPath.Cells.Contains(light.Cell))), Is.False);
            Assert.That(result.Access!.Status, Is.EqualTo(KsProcgenEntranceSceneStatus.Candidate));
            Assert.That(result.EngineAccessVerified || result.WorkingLightingVerified || result.Published || layered.EnginePlacementVerified, Is.False);
            Assert.That(result.FurnishingProbes, Is.LessThanOrEqualTo(Budgets().MaximumFurnishingProbes));
            var replay = Pair.Server.System<KsProcgenPlanningSystem>().Propose(request, "Layered", Context(), [], budgets: Budgets(), content: catalog);
            Assert.That(replay.SemanticHash, Is.EqualTo(result.SemanticHash));
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    [Test]
    public async Task RequiredSupportedCoreCannotDisappearWhenTheProbeBudgetRunsOut()
    {
        await AssertOnServer(() =>
        {
            var manager = Pair.Server.ResolveDependency<IPrototypeManager>(); var catalog = Catalog(manager);
            var result = Pair.Server.System<KsProcgenPlanningSystem>().Propose(Request(), "Layered", Context(), [],
                budgets: Budgets() with { MaximumFurnishingProbes = 1 }, content: catalog);
            Assert.That(result.Status, Is.EqualTo(KsProcgenOwnedPlanStatus.BudgetExceeded));
            Assert.That(result.Furnishings, Is.Empty);
            Assert.That(result.FurnishingProbes, Is.LessThanOrEqualTo(1));
            Assert.That(result.SemanticHash, Is.Zero);
            Assert.That(result.Published, Is.False);
        });
    }

    [Test]
    public async Task IndependentAuditRejectsDetachedOrModifiedFloorProjection()
    {
        await AssertOnServer(() =>
        {
            var manager = Pair.Server.ResolveDependency<IPrototypeManager>(); var request = Request();
            var result = Pair.Server.System<KsProcgenPlanningSystem>().Propose(request, "Layered", Context(), [], budgets: Budgets(), content: Catalog(manager));
            Assert.That(result.Status, Is.EqualTo(KsProcgenOwnedPlanStatus.Candidate), result.Issue?.Code);
            var damaged = result.Furnishings.Select(region => region.Proposal.Layered == null ? region : new KsProcgenFurnishedRegion(region.RegionId,
                new KsProcgenFurnishingResult { Status = KsProcgenFurnishingStatus.Proposed, Layered = region.Proposal.Layered,
                    ProtectedPassageCells = region.Proposal.ProtectedPassageCells })).ToArray();
            var audit = KsProcgenGroupInteriorAnalyzer.Analyze(request, result.Partition!, result.Themes!, damaged, result.Lighting!.Regions, structure: result.Structure);
            Assert.That(audit.Status, Is.EqualTo(KsProcgenEntranceSceneStatus.InvalidInput));
            Assert.That(audit.Issue?.Code, Is.EqualTo("InvalidLayeredInteriorProjection"));
            var noStructure = KsProcgenGroupInteriorAnalyzer.Analyze(request, result.Partition!, result.Themes!, result.Furnishings, result.Lighting.Regions);
            Assert.That(noStructure.Issue?.Code, Is.EqualTo("InvalidLayeredInteriorProjection"));
        });
    }

    [Test]
    public async Task WholeVariantSearchReplacesAnImpossibleSupportedBaseWithoutMixingMembers()
    {
        await AssertOnServer(() =>
        {
            var manager = Pair.Server.ResolveDependency<IPrototypeManager>();
            var result = Pair.Server.System<KsProcgenPlanningSystem>().Propose(Request(), "Layered", Context(), [],
                budgets: Budgets(), content: Catalog(manager, alternative: true));
            Assert.That(result.Status, Is.EqualTo(KsProcgenOwnedPlanStatus.Candidate), result.Issue?.Code);
            var layered = result.Furnishings.Single(region => region.Proposal.Layered != null).Proposal.Layered!;
            Assert.That(layered.Bindings.Select(binding => binding.VariantId), Is.All.EqualTo("Compact"));
            Assert.That(layered.Assembly.Members.Count, Is.EqualTo(2));
            Assert.That(layered.FloorEntities.Single().OccupiedCells.Count, Is.EqualTo(1));
        });
    }

    [TestCase(false)] [TestCase(true)]
    public async Task ImpossibleCoreIsOmittedOnlyWhenTheWholeInstanceIsOptional(bool required)
    {
        await AssertOnServer(() =>
        {
            var manager = Pair.Server.ResolveDependency<IPrototypeManager>();
            var before = SEntMan.GetEntities().ToHashSet();
            var result = Pair.Server.System<KsProcgenPlanningSystem>().Propose(Request(), "Layered", Context(), [],
                budgets: Budgets(), content: Catalog(manager, impossible: true, required: required));
            if (required)
            {
                Assert.That(result.Status, Is.EqualTo(KsProcgenOwnedPlanStatus.Rejected), result.Issue?.Code);
                Assert.That(result.Furnishings, Is.Empty);
                Assert.That(result.SemanticHash, Is.Zero);
            }
            else
            {
                Assert.That(result.Status, Is.EqualTo(KsProcgenOwnedPlanStatus.Candidate), result.Issue?.Code);
                Assert.That(result.Fallbacks.Any(fallback => fallback.Code == "SparseFurnishing"), Is.True);
                Assert.That(result.Furnishings.All(room => room.Proposal.Layered == null && room.Proposal.Entities.Count == 0), Is.True);
            }
            Assert.That(result.FurnishingProbes, Is.GreaterThan(0));
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    [Test]
    public async Task NestedContainerContentsRemainAttachedAndDoNotBecomeFloorEntities()
    {
        await AssertOnServer(() =>
        {
            var manager = Pair.Server.ResolveDependency<IPrototypeManager>();
            var result = Pair.Server.System<KsProcgenPlanningSystem>().Propose(Request(), "Layered", Context(), [],
                budgets: Budgets(), content: Catalog(manager, contained: true));
            Assert.That(result.Status, Is.EqualTo(KsProcgenOwnedPlanStatus.Candidate), result.Issue?.Code);
            var layered = result.Furnishings.Single(room => room.Proposal.Layered != null).Proposal.Layered!;
            var stored = layered.Bindings.Single(binding => binding.SourceMemberId == "Stored").MemberId;
            var device = layered.Bindings.Single(binding => binding.SourceMemberId == "Device").MemberId;
            var pose = layered.Access.Poses!.Members.Single(member => member.Support.MemberId == stored);
            Assert.That(pose.Support.Layer, Is.EqualTo(KsProcgenPlacementLayer.Container));
            Assert.That(pose.Support.Exposed, Is.False);
            Assert.That(pose.CoordinateParentMemberId, Is.EqualTo(device));
            Assert.That(layered.FloorEntities.Count, Is.EqualTo(1));
            Assert.That(layered.Access.Footprints.Any(footprint => footprint.MemberId == stored), Is.False);
        });
    }

    [Test]
    public async Task LayeredAuditAndRoomSizeLimitsCannotBypassCallerBudgets()
    {
        await AssertOnServer(() =>
        {
            var manager = Pair.Server.ResolveDependency<IPrototypeManager>(); var request = Request(); var catalog = Catalog(manager);
            var result = Pair.Server.System<KsProcgenPlanningSystem>().Propose(request, "Layered", Context(), [], budgets: Budgets(), content: catalog);
            Assert.That(result.Status, Is.EqualTo(KsProcgenOwnedPlanStatus.Candidate), result.Issue?.Code);
            var audit = KsProcgenGroupInteriorAnalyzer.Analyze(request, result.Partition!, result.Themes!, result.Furnishings,
                result.Lighting!.Regions, structure: result.Structure, maximumLandingProbes: 0);
            Assert.That(audit.Status, Is.EqualTo(KsProcgenEntranceSceneStatus.BudgetExceeded));
            Assert.That(audit.LandingProbes, Is.Zero);
            var small = Pair.Server.System<KsProcgenPlanningSystem>().Propose(request, "Layered", Context(), [],
                budgets: Budgets() with { MaximumContentRoomCells = 1 }, content: catalog);
            Assert.That(small.Status, Is.EqualTo(KsProcgenOwnedPlanStatus.BudgetExceeded));
            Assert.That(small.Furnishings, Is.Empty);
        });
    }

    internal static KsProcgenThemeCatalog Catalog(IPrototypeManager manager, bool alternative = false, bool impossible = false, bool required = true, bool contained = false, bool lighting = true)
    {
        var spec = new KsProcgenContentDraftSpec
        {
            DocumentId = "LayeredDraft",
            Themes = new() { ["Layered"] = new() { Parent = "KsProcgenSimpleOffice", Goals = new() { FurnishingDensity = 0.01f },
                EntityPacks = new() { new() { Pack = "Supported" } } } },
            Entities = new() { ["Supported"] = new() { Assemblies = new() { new() { Id = "Desk", Assembly = "Desk", MinimumCount = required ? 1 : 0 } } } },
            Assemblies = new() { ["Desk"] = new()
            {
                AnchorMember = "Table",
                Members = new()
                {
                    new() { Id = "Table", Entity = "Table", Movement = KsProcgenMovementClass.VaultRequired },
                    new() { Id = "Device", Entity = "KitchenMicrowave", Role = KsProcgenEntityRole.Equipment, RequiresInteractionApproach = true },
                },
                Relations = new() { new() { Id = "OnTable", Subject = "Device", Target = "Table", Kind = KsProcgenRelationKind.OnSurface } },
            } },
        };
        if (!lighting) spec.Themes["Layered"].LightingPacks = new();
        var assembly = spec.Assemblies["Desk"];
        if (alternative)
            assembly.Variants.Add(new()
            {
                Id = "Compact", AnchorMember = "Table",
                Members = assembly.Members.Select(member => new KsProcgenAssemblyMember
                    { Id = member.Id, Entity = member.Entity, Role = member.Role, Movement = member.Movement,
                        RequiresInteractionApproach = member.RequiresInteractionApproach }).ToList(),
                Relations = assembly.Relations.ToList(),
            });
        if (alternative || impossible) assembly.Members[0].Footprint = Enumerable.Range(0, 9).Select(x => new Vector2i(x, 0)).ToList();
        if (contained)
        {
            assembly.Members.Add(new() { Id = "Stored", Entity = "Paper", Role = KsProcgenEntityRole.Consumable, Movement = KsProcgenMovementClass.Clear });
            assembly.Relations.Add(new() { Id = "Inside", Subject = "Stored", Target = "Device", Kind = KsProcgenRelationKind.InContainer, ContainerId = "microwave_entity_container" });
        }
        Assert.That(KsProcgenContentDraft.TrySnapshot(spec, out var draft, out var issue), Is.True, issue?.Code);
        Assert.That(KsProcgenThemeCatalog.TryCapture(manager, "Layered", out var catalog, out issue, assets: draft), Is.True, issue?.Code);
        return catalog!;
    }
    private static KsProcgenOwnedPlanBudgets Budgets() => new() { PreferredMaximumRoomCells = 64, MaximumFurnishingProbes = 4096 };
    private static KsProcgenGroupRouteContext Context() => new(new HashSet<Vector2i>(),
        new HashSet<Vector2i> { new(-1, 3), new(-2, 3) }, new HashSet<Vector2i>(), true, true);
    private static KsProcgenRequest Request() => new()
    {
        RequestId = "Supported", Seed = 17, ConnectivityPolicy = KsProcgenConnectivityPolicy.PerIsland,
        Shape = new() { AddRectangles = new() { new() { Min = new(0, 0), Max = new(6, 6) } } },
        EntranceDomain = new() { Entrances = new() { new() { PortId = "West", ThresholdCells = new() { new(-1, 3) }, InwardNormal = new(1, 0) } } },
    };
}
