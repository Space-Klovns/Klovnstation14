#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Content.Server._KS14.Procedural;
using Content.Shared._KS14.Procedural;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager.Attributes;
using Robust.Shared.Serialization.Markdown.Value;
using Robust.UnitTesting;

namespace Content.IntegrationTests.Tests._KS14.Procedural;

[RegisterComponent]
public sealed partial class KsProcgenDependencyProbeComponent : Component
{
    [DataField] public EntProtoId? Child;
    [DataField] public EntProtoId<TransformComponent>? RequiredChild;
    [DataField] public ProtoId<EntityPrototype>? TypedChild;
    [DataField] public Dictionary<string, List<EntProtoId>> Children = new();
    [DataField] public ProtoId<EntityCategoryPrototype>? OtherKind;
}

[TestOf(typeof(KsProcgenPrototypeManifestSystem))]
public sealed class KsProcgenPrototypeDependencyTests : KsProcgenGameTest
{
    private const string Root = "KsProcgenDependencyRoot";
    private const string Child = "KsProcgenDependencyChild";
    private const string Leaf = "KsProcgenDependencyLeaf";
    private const string CycleA = "KsProcgenDependencyCycleA";
    private const string CycleB = "KsProcgenDependencyCycleB";
    private const string Abstract = "KsProcgenDependencyAbstract";

    [TestPrototypes]
    private const string Prototypes = """
- type: entity
  id: KsProcgenDependencyRoot
  components:
  - type: KsProcgenDependencyProbe
    child: KsProcgenDependencyChild

- type: entity
  id: KsProcgenDependencyChild
  components:
  - type: KsProcgenDependencyProbe
    child: KsProcgenDependencyLeaf

- type: entity
  id: KsProcgenDependencyLeaf

- type: entity
  id: KsProcgenDependencyCycleA
  components:
  - type: KsProcgenDependencyProbe
    child: KsProcgenDependencyCycleB

- type: entity
  id: KsProcgenDependencyCycleB
  components:
  - type: KsProcgenDependencyProbe
    child: KsProcgenDependencyCycleA

- type: entity
  abstract: true
  id: KsProcgenDependencyAbstract
""";

    private KsProcgenDependencyProbeComponent Probe(string id) =>
        (KsProcgenDependencyProbeComponent) SProtoMan.Index(new EntProtoId(id)).Components["KsProcgenDependencyProbe"].Component;

    [Test]
    public async Task ExternalReferenceSeedsPreserveAbstractDeclarationsWithoutCreatingSpawnRoots()
    {
        await AssertOnServer(() =>
        {
            var system = Pair.Server.System<KsProcgenPrototypeManifestSystem>();
            Assert.That(system.TryCaptureWithReferences(new[] { Leaf }, [], new[] { Abstract }, out var manifest, out var issue), Is.True, message: issue?.ToString());
            Assert.That(manifest!.Roots.Select(root => root.PrototypeId), Is.EqualTo(new[] { Leaf }));
            Assert.That(manifest.EntityReferenceSeeds, Is.EqualTo(new[] { Abstract }));
            Assert.That(manifest.Bodies.Single(body => body.PrototypeId == Abstract).Kind, Is.EqualTo(KsProcgenPrototypeBodyKind.EntityDeclaration));
            Assert.That(system.Matches(manifest, out issue), Is.True, message: issue?.ToString());
            Assert.That(system.TryCreatePrototypeSnapshot(manifest, Abstract, out _, out issue), Is.False);
            Assert.That(SProtoMan.TryGetMapping<EntityPrototype>(Abstract, out var declaration), Is.True);
            try
            {
                declaration!.Add("ksSeedProbe", new ValueDataNode("changed"));
                Assert.That(system.Matches(manifest, out issue), Is.False); Assert.That(issue?.Code, Is.EqualTo("PrototypeManifestChanged"));
            }
            finally { declaration!.Remove("ksSeedProbe"); }
        });
    }

    [Test]
    public async Task ReferenceSeedProvenanceChangesIdentityAndReplaysWithinItsOriginalBudget()
    {
        await AssertOnServer(() =>
        {
            var system = Pair.Server.System<KsProcgenPrototypeManifestSystem>();
            Assert.That(system.TryCapture(new[] { Root }, [], out var original, out var issue), Is.True, message: issue?.ToString());
            Assert.That(system.TryCaptureWithReferences(new[] { Root }, [], new[] { Leaf, Child }, out var seeded, out issue,
                limits: new() { MaximumReferences = 5 }), Is.True, message: issue?.ToString());
            Assert.That(seeded!.Bodies, Is.EqualTo(original!.Bodies)); Assert.That(seeded.Roots, Is.EqualTo(original.Roots));
            Assert.That(seeded.ContentHash, Is.Not.EqualTo(original.ContentHash));
            Assert.That(system.Matches(seeded, out issue), Is.True, message: issue?.ToString());
            Assert.That(system.TryCaptureWithReferences(new[] { Root }, [], new[] { Child, Leaf }, out var reordered, out issue), Is.True, message: issue?.ToString());
            Assert.That(reordered!.ContentHash, Is.EqualTo(seeded.ContentHash));
        });
    }

    [Test]
    public async Task RepeatedExternalReferenceSeedsConsumeOccurrencesBeforeDeduplication()
    {
        await AssertOnServer(() =>
        {
            var system = Pair.Server.System<KsProcgenPrototypeManifestSystem>();
            Assert.That(system.TryCaptureWithReferences(new[] { Leaf }, [], new[] { Abstract, Abstract, Abstract }, out var manifest, out var issue,
                limits: new() { MaximumReferences = 3 }), Is.False);
            Assert.That(manifest, Is.Null); Assert.That(issue?.Code, Is.EqualTo("PrototypeManifestReferenceBudget"));
        });
    }

    [Test]
    public async Task TransitiveBodiesAreCapturedWithoutSpawningAndOnlyOriginalRootsAreReplayed()
    {
        await AssertOnServer(() =>
        {
            var system = Pair.Server.System<KsProcgenPrototypeManifestSystem>();
            var before = SEntMan.GetEntities().ToHashSet();
            Assert.That(system.TryCapture(new[] { Root }, [], out var manifest, out var issue,
                limits: new() { MaximumDefinitions = 3, MaximumReferences = 3 }), Is.True, message: issue?.ToString());
            Assert.That(manifest!.Roots, Is.EqualTo(new[] { new KsProcgenPrototypeRoot(KsProcgenPrototypeBodyKind.Entity, Root) }));
            Assert.That(manifest.Bodies.Select(body => body.PrototypeId), Is.EquivalentTo(new[] { Root, Child, Leaf }));
            Assert.That(manifest.Bodies.All(body => body.Kind == KsProcgenPrototypeBodyKind.Entity && body.CanonicalBody.Length > 0), Is.True);
            Assert.That(system.Matches(manifest, out issue), Is.True, message: issue?.ToString());
            Assert.That(system.MatchesBody(manifest, KsProcgenPrototypeBodyKind.Entity, Root, out issue), Is.True, message: issue?.ToString());
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    [TestCase("entity")] [TestCase("component")] [TestCase("typed")] [TestCase("nested")]
    public async Task EachTypedWrapperAndNestedCollectionEnqueuesDependencies(string kind)
    {
        await AssertOnServer(() =>
        {
            var system = Pair.Server.System<KsProcgenPrototypeManifestSystem>(); var probe = Probe(Root);
            try
            {
                probe.Child = null;
                switch (kind)
                {
                    case "entity": probe.Child = new EntProtoId(Leaf); break;
                    case "component": probe.RequiredChild = new EntProtoId<TransformComponent>(Leaf); break;
                    case "typed": probe.TypedChild = new ProtoId<EntityPrototype>(Leaf); break;
                    default: probe.Children.Add("nested", new List<EntProtoId> { new(Leaf), new(Leaf) }); break;
                }
                Assert.That(system.TryCapture(new[] { Root }, [], out var manifest, out var issue), Is.True, message: issue?.ToString());
                Assert.That(manifest!.Bodies.Select(body => body.PrototypeId), Is.EquivalentTo(new[] { Root, Leaf }));
                Assert.That(system.Matches(manifest, out issue), Is.True, message: issue?.ToString());
                if (kind != "nested") return;
                Assert.That(system.TryCapture(new[] { Root }, [], out var rejected, out issue,
                    limits: new() { MaximumReferences = 2 }), Is.False);
                Assert.That(rejected, Is.Null); Assert.That(issue?.Code, Is.EqualTo("PrototypeManifestReferenceBudget"));
            }
            finally
            {
                probe.Child = new EntProtoId(Child); probe.RequiredChild = null; probe.TypedChild = null; probe.Children.Clear();
            }
        });
    }

    [Test]
    public async Task CyclesTerminateAndReplayWithinTheOriginalOccurrenceBudget()
    {
        await AssertOnServer(() =>
        {
            var system = Pair.Server.System<KsProcgenPrototypeManifestSystem>();
            Assert.That(system.TryCapture(new[] { CycleA }, [], out var manifest, out var issue,
                limits: new() { MaximumDefinitions = 2, MaximumReferences = 3 }), Is.True, message: issue?.ToString());
            Assert.That(manifest!.Bodies.Select(body => body.PrototypeId), Is.EquivalentTo(new[] { CycleA, CycleB }));
            Assert.That(system.Matches(manifest, out issue), Is.True, message: issue?.ToString());
            Assert.That(system.TryCapture(new[] { CycleA }, [], out var rejected, out issue,
                limits: new() { MaximumReferences = 2 }), Is.False);
            Assert.That(rejected, Is.Null); Assert.That(issue?.Code, Is.EqualTo("PrototypeManifestReferenceBudget"));
        });
    }

    [TestCase("definitions")] [TestCase("references")] [TestCase("missing")]
    public async Task DependencyFailuresReturnNoPartialManifestOrWorldMutation(string failure)
    {
        await AssertOnServer(() =>
        {
            var system = Pair.Server.System<KsProcgenPrototypeManifestSystem>(); var probe = Probe(Child);
            var before = SEntMan.GetEntities().ToHashSet();
            try
            {
                if (failure == "missing") probe.Child = new EntProtoId("KsProcgenDependencyMissing");
                var limits = failure switch
                {
                    "definitions" => new KsProcgenPrototypeManifestLimits { MaximumDefinitions = 2 },
                    "references" => new KsProcgenPrototypeManifestLimits { MaximumReferences = 2 },
                    _ => new KsProcgenPrototypeManifestLimits(),
                };
                Assert.That(system.TryCapture(new[] { Root }, [], out var manifest, out var issue, limits: limits), Is.False);
                Assert.That(manifest, Is.Null);
                Assert.That(issue?.Code, Is.EqualTo(failure switch
                {
                    "definitions" => "PrototypeManifestDefinitionBudget",
                    "references" => "PrototypeManifestReferenceBudget",
                    _ => "MissingPrototypeManifestEntity",
                }));
            }
            finally { probe.Child = new EntProtoId(Leaf); }
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    [Test]
    public async Task ChildDriftInvalidatesWholeManifestAndParentPlacementChecks()
    {
        await AssertOnServer(() =>
        {
            var system = Pair.Server.System<KsProcgenPrototypeManifestSystem>();
            Assert.That(system.TryCapture(new[] { Root }, [], out var manifest, out var issue), Is.True, message: issue?.ToString());
            var leaf = SProtoMan.Index(new EntProtoId(Leaf)); var original = leaf.MapSavable;
            try
            {
                leaf.MapSavable = !original;
                Assert.That(system.Matches(manifest!, out issue), Is.False); Assert.That(issue?.Code, Is.EqualTo("PrototypeManifestChanged"));
                Assert.That(system.MatchesBody(manifest!, KsProcgenPrototypeBodyKind.Entity, Root, out issue), Is.False);
                Assert.That(issue?.Code, Is.EqualTo("PrototypeManifestChanged"));
            }
            finally { leaf.MapSavable = original; }
            Assert.That(system.Matches(manifest!, out issue), Is.True, message: issue?.ToString());
        });
    }

    [Test]
    public async Task AbstractDependenciesAreDeclarationOnlyAndCannotBecomeSpawnableRoots()
    {
        await AssertOnServer(() =>
        {
            var system = Pair.Server.System<KsProcgenPrototypeManifestSystem>(); var probe = Probe(Root);
            try
            {
                probe.Child = new EntProtoId(Abstract);
                Assert.That(system.TryCapture(new[] { Root }, [], out var manifest, out var issue), Is.True, message: issue?.ToString());
                var body = manifest!.Bodies.Single(entry => entry.PrototypeId == Abstract);
                Assert.That(body.Kind, Is.EqualTo(KsProcgenPrototypeBodyKind.EntityDeclaration));
                Assert.That(body.CanonicalBody, Is.Empty); Assert.That(body.CanonicalMetadata, Is.Empty);
                Assert.That(body.CanonicalDeclaration, Is.Not.Empty);
                Assert.That(system.Matches(manifest, out issue), Is.True, message: issue?.ToString());
                Assert.That(system.MatchesBody(manifest, KsProcgenPrototypeBodyKind.EntityDeclaration, Abstract, out issue), Is.False);
                Assert.That(issue?.Code, Is.EqualTo("UnsupportedPrototypeManifestPlacementKind"));
                Assert.That(system.TryCreatePrototypeSnapshot(manifest, Root, out var rootSnapshot, out issue), Is.True, message: issue?.ToString());
                Assert.That(rootSnapshot, Is.Not.Null);
                Assert.That(SProtoMan.TryGetMapping<EntityPrototype>(Abstract, out var declaration), Is.True);
                try
                {
                    declaration!.Add("ksDependencyProbe", new ValueDataNode("changed"));
                    Assert.That(system.Matches(manifest, out issue), Is.False);
                    Assert.That(issue?.Code, Is.EqualTo("PrototypeManifestChanged"));
                }
                finally { declaration!.Remove("ksDependencyProbe"); }
                Assert.That(system.TryCreatePrototypeSnapshot(manifest, Abstract, out var snapshot, out issue), Is.False);
                Assert.That(snapshot, Is.Null);
                Assert.That(system.TryCapture(new[] { Abstract }, [], out var rejected, out issue), Is.False);
                Assert.That(rejected, Is.Null); Assert.That(issue?.Code, Is.EqualTo("MissingPrototypeManifestEntity"));
            }
            finally { probe.Child = new EntProtoId(Child); }
        });
    }

    [Test]
    public async Task OwnedStageAutomaticallyCapturesCandidateDependenciesAndRetiresOnTheirDrift()
    {
        await AssertOnServer(() =>
        {
            var stages = Pair.Server.System<KsProcgenOwnedStageSystem>();
            var leaf = SProtoMan.Index(new EntProtoId(Leaf)); var original = leaf.MapSavable;
            var before = SEntMan.GetEntities().ToHashSet();
            try
            {
                var spec = new KsProcgenContentDraftSpec
                {
                    DocumentId = "Dependencies",
                    Themes = new() { ["Dependencies"] = new() { Parent = "KsProcgenNoDoorThemeFixture",
                        Goals = new() { FurnishingDensity = 0f, LightingCoverage = 0f },
                        EntityPacks = new() { new() { Pack = "Contents" } } } },
                    Entities = new() { ["Contents"] = new() { Assemblies = new() { new() { Id = "Core", Assembly = "Core", MinimumCount = 0 } } } },
                    Assemblies = new() { ["Core"] = new()
                    {
                        AnchorMember = "Root", Members = new() { new() { Id = "Root", Entity = Root } },
                    } },
                };
                Assert.That(KsProcgenContentDraft.TrySnapshot(spec, out var draft, out var issue), Is.True, message: issue?.ToString());
                Assert.That(KsProcgenThemeCatalog.TryCapture(SProtoMan, "Dependencies", out var catalog, out issue, assets: draft), Is.True, message: issue?.ToString());
                var request = new KsProcgenRequest
                {
                    RequestId = "Dependencies", Shape = new() { Cells = new() { new(0, 0), new(1, 0), new(2, 0) } },
                    ConnectivityPolicy = KsProcgenConnectivityPolicy.PerIsland, Seed = 17,
                };
                var result = stages.TryGenerate(request, "Dependencies", content: catalog,
                    planningBudgets: new() { MinimumRoomCells = 1, PreferredMaximumRoomCells = 64 });
                Assert.That(result.Status, Is.EqualTo(KsProcgenOwnedStageStatus.PositionedPreview), message: result.Issue?.ToString());
                var stage = result.Stage!;
                Assert.That(stage.Prototypes.Roots.Any(root => root.PrototypeId == Leaf), Is.False);
                Assert.That(stage.Prototypes.Bodies.Any(body => body.PrototypeId == Leaf), Is.True);
                leaf.MapSavable = !original;
                Assert.That(stages.Verify(stage), Is.False);
                result = stages.TryInitialize(stage);
                Assert.That(result.Status, Is.EqualTo(KsProcgenOwnedStageStatus.Rejected));
                Assert.That(result.Issue?.Code, Is.EqualTo("OwnedStagePrototypeDrift"));
                Assert.That(stage.Active, Is.False); Assert.That(stages.RetainedStageCount, Is.Zero);
            }
            finally { leaf.MapSavable = original; stages.DiscardAll(); }
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    [Test]
    public async Task RootProvenanceChangesHashEvenWhenDependencyBodiesAreIdentical()
    {
        await AssertOnServer(() =>
        {
            var system = Pair.Server.System<KsProcgenPrototypeManifestSystem>();
            Assert.That(system.TryCapture(new[] { Root }, [], out var single, out var issue), Is.True, message: issue?.ToString());
            Assert.That(system.TryCapture(new[] { Leaf, Root }, [], out var multiple, out issue), Is.True, message: issue?.ToString());
            Assert.That(multiple!.Bodies, Is.EqualTo(single!.Bodies));
            Assert.That(multiple.Roots.Count, Is.EqualTo(2)); Assert.That(multiple.ContentHash, Is.Not.EqualTo(single.ContentHash));
            Assert.That(system.TryCapture(new[] { Root, Leaf }, [], out var reordered, out issue), Is.True, message: issue?.ToString());
            Assert.That(reordered!.ContentHash, Is.EqualTo(multiple.ContentHash));
        });
    }

    [Test]
    public async Task OtherPrototypeKindsKeepTheirScalarRepresentationWithoutEntityLookup()
    {
        await AssertOnServer(() =>
        {
            var system = Pair.Server.System<KsProcgenPrototypeManifestSystem>(); var probe = Probe(Root);
            try
            {
                probe.Child = null; probe.OtherKind = new ProtoId<EntityCategoryPrototype>("HideSpawnMenu");
                Assert.That(system.TryCapture(new[] { Root }, [], out var manifest, out var issue), Is.True, message: issue?.ToString());
                Assert.That(manifest!.Bodies.Select(body => body.PrototypeId), Is.EqualTo(new[] { Root }));
                Assert.That(manifest.Bodies.Single().CanonicalBody, Does.Contain("HideSpawnMenu"));
                Assert.That(system.TryCreatePrototypeSnapshot(manifest, Root, out var snapshot, out issue), Is.True, message: issue?.ToString());
                var restored = (KsProcgenDependencyProbeComponent) snapshot!.Components["KsProcgenDependencyProbe"].Component;
                Assert.That(restored.OtherKind, Is.EqualTo(probe.OtherKind));
            }
            finally { probe.Child = new EntProtoId(Child); probe.OtherKind = null; }
        });
    }
}
