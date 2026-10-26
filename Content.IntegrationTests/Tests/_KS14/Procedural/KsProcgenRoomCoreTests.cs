#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.Procedural;
using Content.Shared._KS14.Procedural;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.Procedural;

[TestOf(typeof(KsProcgenRoomCoreSearch))]
public sealed class KsProcgenRoomCoreTests : GameTest
{
    [Test]
    public async Task PackResolverIncludesEveryMinimumAndAllowsExplicitOptionalPolicy()
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var manager = Pair.Server.ResolveDependency<IPrototypeManager>();
            var packs = new[] { "KsProcgenImpossibleSupportCoreFixture", "KsProcgenWeightedCoresFixture" };
            var defaults = KsProcgenRoomCoreSearch.SearchPacks(manager, SEntMan.ComponentFactory, "Room", packs, Room(), 17);
            Assert.That(defaults.Status, Is.EqualTo(KsProcgenSupportedAccessStatus.Candidate), defaults.Issue?.Code);
            var bindings = defaults.Composition!.Bindings;
            Assert.That(bindings.Count(binding => binding.CoreId == "RequiredStorage"), Is.EqualTo(2));
            Assert.That(bindings.Count(binding => binding.CoreId != "RequiredStorage"), Is.EqualTo(1));
            var replay = KsProcgenRoomCoreSearch.SearchPacks(manager, SEntMan.ComponentFactory, "Room", packs.Reverse().ToArray(), Room(), 17);
            Assert.That(replay.Placement!.Access!.AccessHash, Is.EqualTo(defaults.Placement!.Access!.AccessHash));
            var onlyRequired = KsProcgenRoomCoreSearch.SearchPacks(manager, SEntMan.ComponentFactory, "Room", packs, Room(), 17,
                optionalCores: System.Array.Empty<KsProcgenRoomOptionalCore>());
            Assert.That(onlyRequired.Status, Is.EqualTo(KsProcgenSupportedAccessStatus.Candidate));
            Assert.That(onlyRequired.Composition!.Bindings.Count, Is.EqualTo(2));
            var explicitOptional = KsProcgenRoomCoreSearch.SearchPacks(manager, SEntMan.ComponentFactory, "Room", packs, Room(), 17,
                optionalCores: new[] { new KsProcgenRoomOptionalCore("KsProcgenWeightedCoresFixture", "AlphaLight", 2) });
            Assert.That(explicitOptional.Status, Is.EqualTo(KsProcgenSupportedAccessStatus.Candidate), explicitOptional.Issue?.Code);
            Assert.That(explicitOptional.Composition!.Bindings.Count(binding => binding.CoreId == "RequiredStorage"), Is.EqualTo(2));
            Assert.That(explicitOptional.Composition.Bindings.Count(binding => binding.CoreId == "AlphaLight"), Is.EqualTo(2));
            var bad = KsProcgenRoomCoreSearch.SearchPacks(manager, SEntMan.ComponentFactory, "Room", packs, Room(), 17,
                optionalCores: new[] { new KsProcgenRoomOptionalCore(packs[0], "RequiredStorage", 1) });
            Assert.That(bad.Status, Is.EqualTo(KsProcgenSupportedAccessStatus.InvalidInput));
            Assert.That(bad.Composition, Is.Null);
        });
    }

    [Test]
    public async Task RepeatedSurfaceCoresSearchJointlyAndInitializeInOneOwnedPreview()
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var system = Pair.Server.System<KsProcgenAssemblyStageSystem>();
            var before = SEntMan.GetEntities().ToHashSet();
            try
            {
                var core = new KsProcgenResolvedEntityCore("Workstation", 1f, 2, new[] { Surface("Base", "KsProcgenTrackedSurfaceFixture") });
                var selections = new[] { new KsProcgenRoomCoreSelection("Pack", core, 0), new("Pack", core, 1) };
                var room = Room();
                var manager = Pair.Server.ResolveDependency<IPrototypeManager>();
                var result = KsProcgenRoomCoreSearch.Search(manager, SEntMan.ComponentFactory, "Room", selections, room, 17);
                Assert.That(result.Status, Is.EqualTo(KsProcgenSupportedAccessStatus.Candidate), result.Issue?.Code);
                Assert.That(result.Composition!.Bindings.Count, Is.EqualTo(4));
                Assert.That(result.VariantProbes, Is.EqualTo(1));
                Assert.That(result.Placement!.FloorRoots.Select(root => root.Position).Distinct().Count(), Is.EqualTo(2));
                var exact = KsProcgenRoomCoreSearch.Search(manager, SEntMan.ComponentFactory, "Room", selections.Reverse().ToArray(), room, 17,
                    maximumVariantProbes: result.VariantProbes, maximumPlacementProbes: result.PlacementProbes,
                    maximumExpandedCells: result.ExpandedCells, maximumLandingProbes: result.LandingProbes);
                Assert.That(exact.Status, Is.EqualTo(KsProcgenSupportedAccessStatus.Candidate), exact.Issue?.Code);
                Assert.That(exact.Placement!.Access!.AccessHash, Is.EqualTo(result.Placement.Access!.AccessHash));
                Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
                var staged = system.TrySearchRoomStage("Room", selections, room, 17);
                Assert.That(staged.Status, Is.EqualTo(KsProcgenAssemblyStageStatus.PositionedPreview), staged.Issue?.Code);
                var stage = staged.Stage!;
                Assert.That(stage.Members.Count, Is.EqualTo(4));
                foreach (var pose in stage.Poses.Members.Where(pose => pose.Support.Layer == KsProcgenPlacementLayer.Surface))
                {
                    var bindings = staged.Search!.Composition!.Bindings;
                    var subject = bindings.Single(binding => binding.MemberId == pose.Support.MemberId);
                    var parent = bindings.Single(binding => binding.MemberId == pose.Support.ParentMemberId);
                    Assert.That(subject.CopyIndex, Is.EqualTo(parent.CopyIndex));
                }
                Assert.That(system.TryInitialize(stage).Status, Is.EqualTo(KsProcgenAssemblyStageStatus.InitializedPreview));
                Assert.That(system.Verify(stage), Is.True);
                Assert.That(stage.PlacementVerified, Is.False);
                Assert.That(system.Discard(stage), Is.True);
                Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
            }
            finally { system.DiscardAll(); }
        });
    }

    [Test]
    public async Task RoomVariantSearchSkipsWholeUnsupportedSurfaceAlternative()
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var core = new KsProcgenResolvedEntityCore("Core", 1f, 1,
                new[] { Surface("Bad", "KitchenMicrowave"), Surface("Good", "KsProcgenTrackedSurfaceFixture") });
            var before = SEntMan.GetEntities().ToHashSet();
            var result = KsProcgenRoomCoreSearch.Search(Pair.Server.ResolveDependency<IPrototypeManager>(), SEntMan.ComponentFactory,
                "Room", new[] { new KsProcgenRoomCoreSelection("Pack", core, 0) }, Room(), 17);
            Assert.That(result.Status, Is.EqualTo(KsProcgenSupportedAccessStatus.Candidate), result.Issue?.Code);
            Assert.That(result.VariantProbes, Is.EqualTo(2));
            Assert.That(result.Composition!.Bindings.All(binding => binding.VariantId == "Good"), Is.True);
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task RoomSearchBudgetFailureAllocatesNothing(bool variantBudget)
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var core = new KsProcgenResolvedEntityCore("Core", 1f, 1, new[] { Surface("Base", "KsProcgenTrackedSurfaceFixture") });
            var before = SEntMan.GetEntities().ToHashSet();
            var result = Pair.Server.System<KsProcgenAssemblyStageSystem>().TrySearchRoomStage("Room",
                new[] { new KsProcgenRoomCoreSelection("Pack", core, 0) }, Room(), 17,
                maximumVariantProbes: variantBudget ? 0 : 64, maximumPlacementProbes: variantBudget ? 4096 : 0);
            Assert.That(result.Status, Is.EqualTo(KsProcgenAssemblyStageStatus.BudgetExceeded), result.Issue?.Code);
            Assert.That(result.Stage, Is.Null);
            Assert.That(result.Search!.Composition, Is.Null);
            Assert.That(result.Search.Placement, Is.Null);
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    [Test]
    public async Task RequiredCopiesCannotBeOmitted()
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var core = new KsProcgenResolvedEntityCore("Core", 1f, 2, new[] { Surface("Base", "KsProcgenTrackedSurfaceFixture") });
            var result = KsProcgenRoomCoreSearch.Search(Pair.Server.ResolveDependency<IPrototypeManager>(), SEntMan.ComponentFactory,
                "Room", new[] { new KsProcgenRoomCoreSelection("Pack", core, 0) }, Room(), 17);
            Assert.That(result.Status, Is.EqualTo(KsProcgenSupportedAccessStatus.InvalidInput));
            Assert.That(result.Issue!.Code, Is.EqualTo("RoomCoreSelectionCountMismatch"));
            Assert.That(result.VariantProbes, Is.Zero);
        });
    }

    [Test]
    public async Task FailedRoomVariantsKeepTheirPlacementCost()
    {
        await Pair.Server.WaitAssertion(() =>
        {
            var manager = Pair.Server.ResolveDependency<IPrototypeManager>();
            var before = SEntMan.GetEntities().ToHashSet();
            var oversized = Surface("Oversized", "KsProcgenTrackedSurfaceFixture");
            oversized = oversized with { Members = oversized.Members.Select(member => member.Id == "Root"
                ? member with { Entry = member.Entry with { Footprint = Enumerable.Range(0, 4)
                    .SelectMany(x => Enumerable.Range(0, 4).Select(y => new Vector2i(x, y))).ToArray() } } : member).ToArray() };
            var firstCore = new KsProcgenResolvedEntityCore("Core", 1f, 1, new[] { oversized });
            var first = KsProcgenRoomCoreSearch.Search(manager, SEntMan.ComponentFactory, "Room",
                new[] { new KsProcgenRoomCoreSelection("Pack", firstCore, 0) }, Room(), 17);
            Assert.That(first.Status, Is.EqualTo(KsProcgenSupportedAccessStatus.Rejected));
            Assert.That(first.PlacementProbes, Is.GreaterThan(0));
            var core = firstCore with { Variants = new[] { oversized, Surface("Good", "KsProcgenTrackedSurfaceFixture") } };
            var selections = new[] { new KsProcgenRoomCoreSelection("Pack", core, 0) };
            var full = KsProcgenRoomCoreSearch.Search(manager, SEntMan.ComponentFactory, "Room", selections, Room(), 17);
            Assert.That(full.Status, Is.EqualTo(KsProcgenSupportedAccessStatus.Candidate), full.Issue?.Code);
            Assert.That(full.VariantProbes, Is.EqualTo(2));
            Assert.That(full.PlacementProbes, Is.EqualTo(first.PlacementProbes + full.Placement!.PlacementProbes));
            var exhausted = KsProcgenRoomCoreSearch.Search(manager, SEntMan.ComponentFactory, "Room", selections, Room(), 17,
                maximumPlacementProbes: first.PlacementProbes);
            Assert.That(exhausted.Status, Is.EqualTo(KsProcgenSupportedAccessStatus.BudgetExceeded));
            Assert.That(exhausted.PlacementProbes, Is.EqualTo(first.PlacementProbes));
            Assert.That(exhausted.Composition, Is.Null);
            Assert.That(exhausted.Placement, Is.Null);
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    private KsProcgenResolvedAssembly Surface(string variantId, string surfacePrototype)
    {
        var manager = Pair.Server.ResolveDependency<IPrototypeManager>();
        var definition = new KsProcgenAssemblyVariant
        {
            Id = variantId, AnchorMember = "Root",
            Members = new List<KsProcgenAssemblyMember>
            {
                new() { Id = "Root", Entity = surfacePrototype, Movement = KsProcgenMovementClass.VaultRequired,
                    RotationMode = KsProcgenMemberRotation.Independent },
                new() { Id = "Device", Entity = "CoordinatesDisk", Movement = KsProcgenMovementClass.Clear,
                    RotationMode = KsProcgenMemberRotation.Independent },
            },
            Relations = new List<KsProcgenAssemblyRelation>
                { new() { Id = "Top", Subject = "Device", Target = "Root", Kind = KsProcgenRelationKind.OnSurface } },
        };
        Assert.That(KsProcgenAssemblyCompiler.TryCompileVariant("Workstation", definition, new Dictionary<string, string>(),
            id => manager.TryIndex<EntityPrototype>(id, out _), out var assembly, out var issue), Is.True, issue?.Code);
        return assembly!;
    }

    private static KsProcgenRoomAccessMask Room() => new(
        Enumerable.Range(0, 7).SelectMany(x => Enumerable.Range(0, 3).Select(y => new Vector2i(x, y))).ToHashSet(),
        new HashSet<Vector2i>(), new HashSet<Vector2i>(), new HashSet<Vector2i>(),
        new HashSet<Vector2i> { new(0, 0) }, Vector2.Zero);
}
