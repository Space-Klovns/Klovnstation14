#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._KS14.Procedural;
using Content.Shared._KS14.Procedural;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._KS14.Procedural;

[TestOf(typeof(KsProcgenSupportedAssemblySearch))]
public sealed class KsProcgenPreferenceSearchTests : KsProcgenGameTest
{
    [Test]
    public async Task SearchImprovesAPreferredCornerWithoutLosingHardAccess()
    {
        await AssertOnServer(() =>
        {
            var before = SEntMan.GetEntities().ToHashSet();
            var assembly = Corner();
            var seed = InteriorSeed(assembly);
            var first = Search(assembly, seed, new(maximumPlacementProbes: 0));
            Assert.That(first.PreferenceScore.MissedRelations, Is.EqualTo(1));
            var improved = Search(assembly, seed, Generous());
            Assert.That(improved.Status, Is.EqualTo(KsProcgenSupportedAccessStatus.Candidate), improved.Issue?.Code);
            Assert.That(improved.PreferenceScore.Satisfied, Is.True);
            Assert.That(improved.PreferenceSearchTruncated, Is.False);
            Assert.That(improved.PlacementProbes, Is.GreaterThan(first.PlacementProbes));
            Assert.That(improved.Access!.Relations.Single().State, Is.EqualTo(KsProcgenConstraintState.Satisfied));
            Assert.That(improved.Access.Access.Count, Is.EqualTo(1));
            var repeated = Search(assembly, seed, Generous());
            Assert.That(repeated.Access!.AccessHash, Is.EqualTo(improved.Access.AccessHash));
            Assert.That(repeated.PlacementProbes, Is.EqualTo(improved.PlacementProbes));
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    [TestCase("placement")] [TestCase("graph")] [TestCase("landing")]
    public async Task ExhaustedPreferenceWorkRetainsAnImmutableFullyCheckedWitness(string domain)
    {
        await AssertOnServer(() =>
        {
            var assembly = Corner();
            var seed = InteriorSeed(assembly);
            var budget = new KsProcgenPreferenceBudget(maximumPlacementProbes: domain == "placement" ? 0 : 512,
                maximumExpandedCells: domain == "graph" ? 0 : 65_536, maximumLandingProbes: domain == "landing" ? 0 : 512);
            var result = Search(assembly, seed, budget);
            Assert.That(result.Status, Is.EqualTo(KsProcgenSupportedAccessStatus.Candidate), result.Issue?.Code);
            Assert.That(result.PreferenceSearchTruncated, Is.True);
            Assert.That(result.PreferenceScore.MissedRelations, Is.EqualTo(1));
            Assert.That(result.FloorRoots.Count, Is.EqualTo(1));
            Assert.That(result.Orientations.Count, Is.EqualTo(1));
            var pose = result.Access!.Poses!.Members.Single();
            Assert.That(result.FloorRoots.Single().Position, Is.EqualTo(pose.Position));
            Assert.That(result.Orientations.Single().QuarterTurns, Is.EqualTo(pose.QuarterTurns));
            var capabilities = Capabilities(assembly);
            var verified = KsProcgenSupportedAccessPlanner.Plan(assembly, new[] { "Root" }, capabilities,
                result.FloorRoots, result.Orientations, result.AssemblyQuarterTurns, Room(), maximumExpandedCells: 65_536);
            Assert.That(verified.Status, Is.EqualTo(KsProcgenSupportedAccessStatus.Candidate), verified.Issue?.Code);
            Assert.That(verified.AccessHash, Is.EqualTo(result.Access.AccessHash));
            Assert.That(budget.PlacementProbes, Is.LessThanOrEqualTo(budget.MaximumPlacementProbes));
            Assert.That(budget.ExpandedCells, Is.LessThanOrEqualTo(budget.MaximumExpandedCells));
            Assert.That(budget.LandingProbes, Is.LessThanOrEqualTo(budget.MaximumLandingProbes));
        });
    }

    [Test]
    public async Task PreferenceFallbackCannotInventAHardWitness()
    {
        await AssertOnServer(() =>
        {
            var assembly = Corner();
            var result = KsProcgenSupportedAssemblySearch.Search(assembly, new[] { "Root" }, Capabilities(assembly), Room(), 17,
                maximumPlacementProbes: 0, preferenceBudget: new());
            Assert.That(result.Status, Is.EqualTo(KsProcgenSupportedAccessStatus.BudgetExceeded));
            Assert.That(result.Access, Is.Null);
            Assert.That(result.FloorRoots, Is.Empty);
        });
    }

    [TestCase(1, false)] [TestCase(16, true)]
    public async Task FixedMachinePosesCanImproveOperatingLandingsAndRevalidateTheExactChoice(int landingAllowance, bool improved)
    {
        await AssertOnServer(() =>
        {
            var assembly = Compile(new() { Id = "Landings", AnchorMember = "Machine", Members = new() {
                new() { Id = "Machine", Entity = "KsProcgenFilteredComputerFixture", Role = KsProcgenEntityRole.Equipment,
                    RequiresInteractionApproach = true, Footprint = new() { new(0, 0), new(1, 0) },
                    RotationMode = KsProcgenMemberRotation.Independent, AllowedQuarterTurns = new() { 0 } },
                new() { Id = "Target", Entity = "KsProcgenRoomEmptyFixture", Movement = KsProcgenMovementClass.Clear,
                    RotationMode = KsProcgenMemberRotation.Independent, AllowedQuarterTurns = new() { 0 } } },
                Relations = new() { new() { Id = "OperatingDistance", Subject = "Machine", Target = "Target", Kind = KsProcgenRelationKind.Near,
                    Severity = KsProcgenRelationSeverity.Preferred, MinimumDistance = 1, MaximumDistance = 1 } } });
            var room = Room();
            var roots = new[] { new KsProcgenFloorRootPose("Machine", new(2.5f, 3.5f)), new("Target", new(4.5f, 2.5f)) };
            var budget = new KsProcgenPreferenceBudget(maximumPlacementProbes: 0, maximumExpandedCells: 65_536,
                maximumLandingProbes: landingAllowance);
            var capabilities = Capabilities(assembly);
            var result = KsProcgenSupportedAssemblySearch.Search(assembly, new[] { "Machine", "Target" }, capabilities, room, 17,
                fixedFloorRoots: roots, maximumExpandedCells: 65_536, requireConnectedCleanFloor: true, preferenceBudget: budget);
            Assert.That(result.Status, Is.EqualTo(KsProcgenSupportedAccessStatus.Candidate), result.Issue?.Code);
            Assert.That(result.PreferenceScore.Satisfied, Is.EqualTo(improved));
            Assert.That(result.PreferenceSearchTruncated, Is.EqualTo(!improved));
            Assert.That(result.Access!.Access.Single().Approach, Is.EqualTo(improved ? new Vector2i(3, 2) : new Vector2i(2, 2)));
            var verified = KsProcgenSupportedAccessPlanner.Plan(assembly, new[] { "Machine", "Target" }, capabilities,
                result.FloorRoots, result.Orientations, result.AssemblyQuarterTurns, room, maximumExpandedCells: 65_536, fixedAccess: result.Access.Access);
            Assert.That(verified.Status, Is.EqualTo(KsProcgenSupportedAccessStatus.Candidate), verified.Issue?.Code);
            Assert.That(verified.AccessHash, Is.EqualTo(result.Access.AccessHash));
            var forged = result.Access.Access.Select(access => access with { Approach = new(9, 9) }).ToArray();
            var invalid = KsProcgenSupportedAccessPlanner.Plan(assembly, new[] { "Machine", "Target" }, capabilities,
                result.FloorRoots, result.Orientations, result.AssemblyQuarterTurns, room, maximumExpandedCells: 65_536, fixedAccess: forged);
            Assert.That(invalid.Status, Is.Not.EqualTo(KsProcgenSupportedAccessStatus.Candidate));
            Assert.That(budget.LandingProbes, Is.LessThanOrEqualTo(landingAllowance));
        });
    }

    [TestCase(false)] [TestCase(true)]
    public async Task WholeVariantsAndChoicesCanImproveAnEarlierValidAlternative(bool choices)
    {
        await AssertOnServer(() =>
        {
            var before = SEntMan.GetEntities().ToHashSet();
            var bad = PairAssembly("Bad", 2); var good = PairAssembly("Good", 1);
            var room = Strip();
            KsProcgenRoomCoreSearchResult result;
            KsProcgenRoomCoreSearchResult limited;
            if (choices)
            {
                var options = new[] { new KsProcgenRoomCoreChoice(new[] {
                    new KsProcgenRoomCoreSelection("Pack", new("Bad", 1f, 0, new[] { bad }), 0),
                    new KsProcgenRoomCoreSelection("Pack", new("Good", 1f, 0, new[] { good }), 0) }) };
                result = KsProcgenRoomCoreSearch.SearchChoices(SProtoMan, SEntMan.ComponentFactory, "Preferences", options, room, 17,
                    maximumExpandedCells: 65_536, requireConnectedCleanFloor: true);
                limited = KsProcgenRoomCoreSearch.SearchChoices(SProtoMan, SEntMan.ComponentFactory, "Preferences", options, room, 17,
                    maximumChoiceProbes: 1, maximumExpandedCells: 65_536, requireConnectedCleanFloor: true);
                Assert.That(result.ChoiceProbes, Is.EqualTo(2));
                Assert.That(result.Composition!.Bindings.All(binding => binding.CoreId == "Good"), Is.True);
            }
            else
            {
                var selections = new[] { new KsProcgenRoomCoreSelection("Pack", new("Core", 1f, 1, new[] { bad, good }), 0) };
                result = KsProcgenRoomCoreSearch.Search(SProtoMan, SEntMan.ComponentFactory, "Preferences", selections, room, 17,
                    maximumExpandedCells: 65_536, requireConnectedCleanFloor: true);
                limited = KsProcgenRoomCoreSearch.Search(SProtoMan, SEntMan.ComponentFactory, "Preferences", selections, room, 17,
                    maximumVariantProbes: 1, maximumExpandedCells: 65_536, requireConnectedCleanFloor: true);
                Assert.That(result.VariantProbes, Is.EqualTo(2));
            }
            Assert.That(result.Status, Is.EqualTo(KsProcgenSupportedAccessStatus.Candidate), result.Issue?.Code);
            Assert.That(result.Placement!.PreferenceScore.Satisfied, Is.True);
            Assert.That(result.Composition!.Bindings.Count, Is.EqualTo(2));
            Assert.That(result.Composition.Bindings.All(binding => binding.VariantId == "Good"), Is.True);
            Assert.That(limited.Status, Is.EqualTo(KsProcgenSupportedAccessStatus.Candidate), limited.Issue?.Code);
            Assert.That(limited.PreferenceSearchTruncated, Is.True);
            Assert.That(limited.Placement!.PreferenceScore.MissedRelations, Is.EqualTo(1));
            Assert.That(limited.Composition!.Bindings.All(binding => binding.VariantId == "Bad"), Is.True);
            Assert.That(result.PlacementProbes, Is.GreaterThan(limited.PlacementProbes));
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    [TestCase(false)] [TestCase(true)]
    public async Task UnsupportedAlternativeCannotBeHiddenByAnEarlierWitnessOrLaterValidCore(bool choices)
    {
        await AssertOnServer(() =>
        {
            var before = SEntMan.GetEntities().ToHashSet();
            var earlier = PairAssembly("Earlier", 2);
            var unsupported = Compile(new() { Id = "Fractional", AnchorMember = "Root", Members = new() {
                new() { Id = "Root", Entity = "KsProcgenTrackedSurfaceFixture", Movement = KsProcgenMovementClass.VaultRequired,
                    RotationMode = KsProcgenMemberRotation.Independent },
                new() { Id = "Device", Entity = "CoordinatesDisk", Movement = KsProcgenMovementClass.Clear,
                    RotationMode = KsProcgenMemberRotation.Independent } },
                Relations = new() {
                    new() { Id = "Top", Subject = "Device", Target = "Root", Kind = KsProcgenRelationKind.OnSurface },
                    new() { Id = "Adjacent", Subject = "Device", Target = "Root", Kind = KsProcgenRelationKind.AdjacentTo } } });
            var later = PairAssembly("Later", 1);
            KsProcgenRoomCoreSearchResult result;
            if (choices)
            {
                var options = new[] { earlier, unsupported, later }.Select(assembly =>
                    new KsProcgenRoomCoreSelection("Pack", new(assembly.VariantId, 1f, 0, new[] { assembly }), 0)).ToArray();
                result = KsProcgenRoomCoreSearch.SearchChoices(SProtoMan, SEntMan.ComponentFactory, "Unsupported",
                    new[] { new KsProcgenRoomCoreChoice(options) }, Strip(), 17, maximumExpandedCells: 65_536);
                Assert.That(result.ChoiceProbes, Is.EqualTo(2));
            }
            else
                result = KsProcgenRoomCoreSearch.Search(SProtoMan, SEntMan.ComponentFactory, "Unsupported",
                    new[] { new KsProcgenRoomCoreSelection("Pack", new("Core", 1f, 1, new[] { earlier, unsupported, later }), 0) },
                    Strip(), 17, maximumExpandedCells: 65_536);
            Assert.That(result.Status, Is.EqualTo(KsProcgenSupportedAccessStatus.UnsupportedContent), result.Issue?.Code);
            Assert.That(result.Issue!.Code, Is.EqualTo("FractionalSpatialRelationUnsupported"));
            Assert.That(result.VariantProbes, Is.EqualTo(2));
            Assert.That(result.PlacementProbes, Is.GreaterThan(0));
            Assert.That(result.Composition, Is.Null);
            Assert.That(result.Placement, Is.Null);
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    [TestCase(false)] [TestCase(true)]
    public async Task PrimaryRoomAppliesCoherenceIntentAndKeepsMeasuredWholeGroupOutcomes(bool coherent)
    {
        await AssertOnServer(() =>
        {
            var stages = Pair.Server.System<KsProcgenOwnedStageSystem>();
            var before = SEntMan.GetEntities().ToHashSet();
            var spec = new KsProcgenContentDraftSpec
            {
                DocumentId = "Coherence", Themes = new() { ["Coherence"] = new() { Parent = "KsProcgenSimpleOffice", LightingPacks = new(),
                    Goals = new() { CoherentContents = coherent, FurnishingDensity = 0.04f, LightingCoverage = 0f },
                    EntityPacks = new() { new() { Pack = "Contents" } } } },
                Entities = new() { ["Contents"] = new() { Assemblies = new() {
                    new() { Id = "Alpha", Assembly = "Table", MinimumCount = 1 }, new() { Id = "Beta", Assembly = "Table", MinimumCount = 1 } } } },
                Assemblies = new() { ["Table"] = new() { AnchorMember = "Root", Members = new() {
                    new() { Id = "Root", Entity = "Table", Movement = KsProcgenMovementClass.VaultRequired } } } },
            };
            Assert.That(KsProcgenContentDraft.TrySnapshot(spec, out var draft, out var issue), Is.True, issue?.Code);
            Assert.That(KsProcgenThemeCatalog.TryCapture(SProtoMan, "Coherence", out var catalog, out issue, assets: draft), Is.True, issue?.Code);
            try
            {
                var result = stages.TryGenerate(new() { RequestId = "Coherence", Seed = 17, ConnectivityPolicy = KsProcgenConnectivityPolicy.PerIsland,
                    Shape = new() { AddRectangles = new() { new() { Min = new(0, 0), Max = new(10, 10) } } } }, "Coherence", content: catalog,
                    planningBudgets: new() { PreferredMaximumRoomCells = 128, MaximumContentRoomCells = 128 });
                Assert.That(result.Status, Is.EqualTo(KsProcgenOwnedStageStatus.PositionedPreview), result.Issue?.Code);
                var furnishing = result.Proposal!.Furnishings.Single().Proposal;
                Assert.That(furnishing.PlacedDominantClusters, Is.EqualTo(4));
                var layered = furnishing.Layered!;
                Assert.That(layered.Bindings.Count, Is.EqualTo(4));
                if (coherent)
                {
                    Assert.That(layered.Placement.RootPreferenceOutcomes, Is.Not.Empty);
                    Assert.That(layered.Placement.RootPreferenceOutcomes.All(outcome => outcome.Penalty == 0), Is.True);
                    foreach (var outcome in layered.Placement.RootPreferenceOutcomes)
                    {
                        var first = layered.Placement.FloorRoots.Single(root => root.MemberId == outcome.Subject).Position;
                        var second = layered.Placement.FloorRoots.Single(root => root.MemberId == outcome.Target).Position;
                        Assert.That(outcome.Distance, Is.EqualTo((long) Math.Abs(first.X - second.X) + (long) Math.Abs(first.Y - second.Y)));
                    }
                }
                else Assert.That(layered.Placement.RootPreferenceOutcomes, Is.Empty);
                Assert.That(stages.TryInitialize(result.Stage!).Status, Is.EqualTo(KsProcgenOwnedStageStatus.InitializedPreview));
                Assert.That(stages.Verify(result.Stage!), Is.True);
            }
            finally { stages.DiscardAll(); }
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    private KsProcgenAssemblyCapabilityReport Capabilities(KsProcgenResolvedAssembly assembly)
    {
        Assert.That(KsProcgenPrototypeCapabilityInspector.TryInspectAssembly(SProtoMan, SEntMan.ComponentFactory, assembly,
            out var capabilities, out var issue), Is.True, issue?.Code);
        return capabilities!;
    }
    private KsProcgenSupportedAssemblySearchResult Search(KsProcgenResolvedAssembly assembly, int seed, KsProcgenPreferenceBudget budget) =>
        KsProcgenSupportedAssemblySearch.Search(assembly, new[] { "Root" }, Capabilities(assembly), Room(), seed,
            maximumExpandedCells: 65_536, requireConnectedCleanFloor: true, preferenceBudget: budget);
    private int InteriorSeed(KsProcgenResolvedAssembly assembly)
    {
        for (var seed = 0; seed < 32; seed++)
        {
            var result = Search(assembly, seed, new(maximumPlacementProbes: 0));
            Assert.That(result.Status, Is.EqualTo(KsProcgenSupportedAccessStatus.Candidate), result.Issue?.Code);
            if (result.PreferenceScore.MissedRelations == 1) return seed;
        }
        Assert.Fail("Fixture requires an initially non-corner candidate."); return -1;
    }
    private static KsProcgenPreferenceBudget Generous() => new(maximumPlacementProbes: 512, maximumExpandedCells: 65_536, maximumLandingProbes: 512);
    private KsProcgenResolvedAssembly Corner() => Compile(new() { Id = "Corner", AnchorMember = "Root", Members = new() {
        new() { Id = "Root", Entity = "KsProcgenFilteredComputerFixture", Role = KsProcgenEntityRole.Equipment,
            RequiresInteractionApproach = true, RotationMode = KsProcgenMemberRotation.Independent, AllowedQuarterTurns = new() { 0 } } },
        Relations = new() { new() { Id = "Corner", Subject = "Root", Kind = KsProcgenRelationKind.AtCorner, Severity = KsProcgenRelationSeverity.Preferred } } });
    private KsProcgenResolvedAssembly PairAssembly(string id, int distance) => Compile(new() { Id = id, AnchorMember = "First", Members = new() {
        new() { Id = "First", Entity = "KsProcgenRoomEmptyFixture", Movement = KsProcgenMovementClass.Clear,
            RotationMode = KsProcgenMemberRotation.Independent, AllowedQuarterTurns = new() { 0 } },
        new() { Id = "Second", Entity = "KsProcgenRoomEmptyFixture", Movement = KsProcgenMovementClass.Clear,
            RotationMode = KsProcgenMemberRotation.Independent, AllowedQuarterTurns = new() { 0 } } },
        Relations = new() { new() { Id = "Distance", Subject = "First", Target = "Second", Kind = KsProcgenRelationKind.Near,
            Severity = KsProcgenRelationSeverity.Preferred, MinimumDistance = distance, MaximumDistance = distance } } });
    private KsProcgenResolvedAssembly Compile(KsProcgenAssemblyVariant variant)
    {
        Assert.That(KsProcgenAssemblyCompiler.TryCompileVariant("PreferenceFixture", variant, new Dictionary<string, string>(),
            id => SProtoMan.TryIndex<Robust.Shared.Prototypes.EntityPrototype>(id, out _), out var assembly, out var issue), Is.True, issue?.Code);
        return assembly!;
    }
    private static KsProcgenRoomAccessMask Room()
    {
        var floor = Enumerable.Range(0, 10).SelectMany(x => Enumerable.Range(0, 10).Select(y => new Vector2i(x, y))).ToHashSet();
        var walls = floor.SelectMany(cell => new[] { cell + new Vector2i(1, 0), cell + new Vector2i(-1, 0),
            cell + new Vector2i(0, 1), cell + new Vector2i(0, -1) }).Where(cell => !floor.Contains(cell)).ToHashSet();
        return new(floor, walls, new HashSet<Vector2i>(), new HashSet<Vector2i>(), new HashSet<Vector2i> { Vector2i.Zero }, new Vector2(0.5f));
    }
    private static KsProcgenRoomAccessMask Strip() => new(new HashSet<Vector2i> { new(0, 0), new(1, 0), new(2, 0) },
        new HashSet<Vector2i>(), new HashSet<Vector2i>(), new HashSet<Vector2i>(), new HashSet<Vector2i> { Vector2i.Zero }, Vector2.Zero);
}
