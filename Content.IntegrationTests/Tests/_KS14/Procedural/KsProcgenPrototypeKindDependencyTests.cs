#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Content.Server._KS14.Procedural;
using Content.Shared._KS14.Procedural;
using Content.Shared.Materials;
using Content.Shared.Maps;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager.Attributes;
using Robust.Shared.Serialization.Markdown.Value;
using Robust.UnitTesting;

namespace Content.IntegrationTests.Tests._KS14.Procedural;

[RegisterComponent]
public sealed partial class KsProcgenKindDependencyProbeComponent : Component
{
    [DataField] public ProtoId<MaterialPrototype>? Material;
    [DataField] public ProtoId<EntityCategoryPrototype>? Category;
    [DataField] public ProtoId<ContentTileDefinition>? Tile;
    [DataField] public EntProtoId? Child;
    [DataField] public List<ProtoId<MaterialPrototype>> Materials = new();
    [DataField] public Dictionary<ProtoId<MaterialPrototype>, ProtoId<EntityCategoryPrototype>> Kinds = new();
}

[TestOf(typeof(KsProcgenPrototypeManifestSystem))]
public sealed class KsProcgenPrototypeKindDependencyTests : KsProcgenGameTest
{
    private const string Root = "KsProcgenKindRoot";
    private const string Bridge = "KsProcgenKindBridge";
    private const string Leaf = "KsProcgenKindLeaf";
    private const string FirstMaterial = "KsProcgenKindMaterialA";
    private const string SecondMaterial = "KsProcgenKindMaterialB";
    private const string SharedId = "KsProcgenKindSharedId";
    private const string AbstractMaterial = "KsProcgenKindAbstractMaterial";
    private const string AbstractEntity = "KsProcgenKindAbstractEntity";

    [TestPrototypes]
    private const string Prototypes = """
- type: entity
  id: KsProcgenKindRoot
  components:
  - type: KsProcgenKindDependencyProbe
    material: KsProcgenKindMaterialA

- type: entity
  id: KsProcgenKindBridge
  components:
  - type: KsProcgenKindDependencyProbe
    material: KsProcgenKindMaterialB

- type: entity
  id: KsProcgenKindLeaf

- type: entity
  abstract: true
  id: KsProcgenKindAbstractEntity
  components:
  - type: KsProcgenKindDependencyProbe
    material: KsProcgenKindMaterialB

- type: material
  id: KsProcgenKindMaterialA
  price: 1
  stackEntity: KsProcgenKindBridge

- type: material
  id: KsProcgenKindMaterialB
  price: 2
  stackEntity: KsProcgenKindLeaf

- type: material
  id: KsProcgenKindSharedId
  price: 3

- type: entityCategory
  id: KsProcgenKindSharedId

- type: material
  abstract: true
  id: KsProcgenKindAbstractMaterial
  price: 4
  stackEntity: KsProcgenKindLeaf
""";

    private KsProcgenKindDependencyProbeComponent Probe(string id = Root) =>
        (KsProcgenKindDependencyProbeComponent) SProtoMan.Index(new EntProtoId(id)).Components["KsProcgenKindDependencyProbe"].Component;
    private MaterialPrototype Material(string id) => SProtoMan.Index<MaterialPrototype>(id);
    private string MaterialKind()
    {
        Assert.That(SProtoMan.TryGetKindFrom<MaterialPrototype>(out var kind), Is.True);
        return kind!;
    }

    [Test]
    public async Task StandardOfficeCatalogClosureHasMeasuredBoundedNativeContent()
    {
        var diagnostics = new List<string>();
        await AssertOnServer(() =>
        {
            Assert.That(KsProcgenThemeCatalog.TryCapture(SProtoMan, "KsProcgenSimpleOffice", out var catalog, out var issue), Is.True, issue?.ToString());
            var limits = new KsProcgenPrototypeManifestLimits { MaximumDefinitions = 4096, MaximumReferences = 131072,
                MaximumNodes = 1048576, MaximumCharacters = 16777216 };
            Assert.That(Pair.Server.System<KsProcgenPrototypeManifestSystem>().TryCapture(catalog!, out var manifest, out issue, limits: limits), Is.True, issue?.ToString());
            diagnostics.Add("Native office closure: entities/tiles=" + manifest!.Bodies.Count + "; other=" + manifest.Dependencies.Count +
                "; references=" + manifest.ReferenceOccurrences + "; nodes=" + manifest.Nodes + "; characters=" + manifest.Characters);
            diagnostics.Add("Native abstract inspection gaps: " + manifest.AbstractTemplates.Count(template => template.HasDeclaredInspectionGaps));
            foreach (var template in manifest.AbstractTemplates.Where(template => template.HasDeclaredInspectionGaps))
                diagnostics.Add("Native inspection gap: " + template.Kind + "/" + template.PrototypeId + " " + template.CanonicalInspection);
            foreach (var group in manifest.Dependencies.GroupBy(body => body.Kind).OrderByDescending(group => group.Count()).Take(10))
                diagnostics.Add(group.Key + "=" + group.Count());
            Assert.That(manifest.Bodies.Count + manifest.Dependencies.Count, Is.LessThanOrEqualTo(limits.MaximumDefinitions));
            Assert.That(manifest.ReferenceOccurrences, Is.LessThanOrEqualTo(limits.MaximumReferences));
            Assert.That(manifest.FullContentClosureVerified, Is.False);
            var system = Pair.Server.System<KsProcgenPrototypeManifestSystem>();
            Assert.That(system.TryCapture(catalog!, out var standard, out issue), Is.True, issue?.ToString());
            Assert.That(standard!.ContentHash, Is.EqualTo(manifest.ContentHash));
            Assert.That(standard.Bodies, Is.EqualTo(manifest.Bodies)); Assert.That(standard.Dependencies, Is.EqualTo(manifest.Dependencies));
            Assert.That(system.TryCapture(catalog!, out var denied, out issue,
                limits: new() { MaximumDefinitions = 512 }), Is.False);
            Assert.That(denied, Is.Null); Assert.That(issue!.Code, Is.EqualTo("PrototypeManifestDefinitionBudget"));
        });
        foreach (var diagnostic in diagnostics) TestContext.Out.WriteLine(diagnostic);
    }

    [Test]
    public async Task TypedKindsCloseTransitivelyBackIntoEntitiesWithoutBecomingSpawnRoots()
    {
        await AssertOnServer(() =>
        {
            var system = Pair.Server.System<KsProcgenPrototypeManifestSystem>(); var before = SEntMan.GetEntities().ToHashSet();
            Assert.That(system.TryCapture(new[] { Root }, [], out var captured, out var issue), Is.True, issue?.ToString());
            var manifest = captured!;
            Assert.That(manifest.SchemaVersion, Is.EqualTo(11)); Assert.That(manifest.FullContentClosureVerified, Is.False);
            Assert.That(manifest.Roots.Select(root => root.PrototypeId), Is.EqualTo(new[] { Root }));
            Assert.That(manifest.DependencyRoots, Is.Empty);
            Assert.That(manifest.Bodies.Select(body => body.PrototypeId), Is.EquivalentTo(new[] { Root, Bridge, Leaf }));
            Assert.That(manifest.Dependencies.Select(body => body.PrototypeId), Is.EquivalentTo(new[] { FirstMaterial, SecondMaterial }));
            Assert.That(manifest.Dependencies.All(body => body.Kind == MaterialKind() && !body.DeclarationOnly &&
                body.CanonicalBody.Length > 0 && body.CanonicalDeclaration.Length > 0), Is.True);
            Assert.That(system.Matches(manifest, out issue), Is.True, issue?.ToString());
            Assert.That(system.MatchesBody(manifest, KsProcgenPrototypeBodyKind.Entity, Root, out issue), Is.True, issue?.ToString());
            Assert.That(system.TryCreatePrototypeSnapshot(manifest, Root, out var snapshot, out issue), Is.True, issue?.ToString());
            Assert.That(((KsProcgenKindDependencyProbeComponent) snapshot!.Components["KsProcgenKindDependencyProbe"].Component).Material,
                Is.EqualTo(Probe().Material));
            Assert.Throws<NotSupportedException>(() => ((IList<KsProcgenPrototypeDependencyBody>) manifest.Dependencies).Clear());
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    [Test]
    public async Task ExplicitKindRootsRetainProvenanceWithoutGrantingEntityPlacementAuthority()
    {
        await AssertOnServer(() =>
        {
            var system = Pair.Server.System<KsProcgenPrototypeManifestSystem>(); var root = new KsProcgenPrototypeReference(MaterialKind(), FirstMaterial);
            Assert.That(system.TryCapture(new[] { Root }, [], out var implicitRoots, out var issue), Is.True, issue?.ToString());
            Assert.That(system.TryCapture(new[] { Root }, [], out var explicitRoots, out issue,
                dependencyRoots: new[] { root }), Is.True, issue?.ToString());
            Assert.That(explicitRoots!.Bodies, Is.EqualTo(implicitRoots!.Bodies)); Assert.That(explicitRoots.Dependencies, Is.EqualTo(implicitRoots.Dependencies));
            Assert.That(explicitRoots.DependencyRoots, Is.EqualTo(new[] { root }));
            Assert.That(explicitRoots.ContentHash, Is.Not.EqualTo(implicitRoots.ContentHash));
            Assert.That(system.Matches(explicitRoots, out issue), Is.True, issue?.ToString());
            Assert.That(system.TryCapture([], [], out var onlyDependencies, out issue, dependencyRoots: new[] { root }), Is.True, issue?.ToString());
            Assert.That(onlyDependencies!.Roots, Is.Empty);
            Assert.That(onlyDependencies.Bodies.Select(body => body.PrototypeId), Is.EquivalentTo(new[] { Bridge, Leaf }));
            Assert.That(system.TryCreatePrototypeSnapshot(onlyDependencies, FirstMaterial, out var snapshot, out issue), Is.False);
            Assert.That(snapshot, Is.Null); Assert.That(issue!.Code, Is.EqualTo("UncapturedPrototypeReference"));
        });
    }

    [TestCase(false)] [TestCase(true)]
    public async Task SameIdInDifferentKindsIsNeverConflatedAndNestedReferencesAreDiscovered(bool dictionary)
    {
        await AssertOnServer(() =>
        {
            var system = Pair.Server.System<KsProcgenPrototypeManifestSystem>(); var probe = Probe();
            var category = SProtoMan.Index<EntityCategoryPrototype>(SharedId); var hide = category.HideSpawnMenu;
            try
            {
                probe.Material = null;
                if (dictionary) probe.Kinds.Add(new(SharedId), new(SharedId));
                else { probe.Materials.Add(new(SharedId)); probe.Category = new(SharedId); }
                Assert.That(system.TryCapture(new[] { Root }, [], out var manifest, out var issue), Is.True, issue?.ToString());
                Assert.That(manifest!.Bodies.Select(body => body.PrototypeId), Is.EqualTo(new[] { Root }));
                Assert.That(manifest.Dependencies.Count, Is.EqualTo(2));
                Assert.That(manifest.Dependencies.All(body => body.PrototypeId == SharedId), Is.True);
                Assert.That(manifest.Dependencies.Select(body => body.Kind).Distinct().Count(), Is.EqualTo(2));
                var material = manifest.Dependencies.Single(body => body.Kind == MaterialKind());
                category.HideSpawnMenu = !hide;
                Assert.That(system.Matches(manifest, out issue), Is.False); Assert.That(issue!.Code, Is.EqualTo("PrototypeManifestChanged"));
                Assert.That(system.TryCapture(new[] { Root }, [], out var changed, out issue), Is.True, issue?.ToString());
                Assert.That(changed!.Dependencies.Single(body => body.Kind == MaterialKind()), Is.EqualTo(material));
            }
            finally
            { probe.Material = new(FirstMaterial); probe.Materials.Clear(); probe.Kinds.Clear(); probe.Category = null; category.HideSpawnMenu = hide; }
        });
    }

    [Test]
    public async Task CrossKindCyclesAndOccurrenceBudgetsAreExactRatherThanDeduplicatedWork()
    {
        await AssertOnServer(() =>
        {
            var system = Pair.Server.System<KsProcgenPrototypeManifestSystem>(); var material = Material(FirstMaterial); var saved = material.StackEntity;
            try
            {
                material.StackEntity = new EntProtoId(Root);
                var exact = new KsProcgenPrototypeManifestLimits { MaximumDefinitions = 2, MaximumReferences = 3 };
                Assert.That(system.TryCapture(new[] { Root }, [], out var manifest, out var issue, limits: exact), Is.True, issue?.ToString());
                Assert.That(manifest!.Bodies.Count, Is.EqualTo(1)); Assert.That(manifest.Dependencies.Count, Is.EqualTo(1));
                Assert.That(system.Matches(manifest, out issue), Is.True, issue?.ToString());
                Assert.That(system.TryCapture(new[] { Root }, [], out var denied, out issue,
                    limits: exact with { MaximumReferences = 2 }), Is.False);
                Assert.That(denied, Is.Null); Assert.That(issue!.Code, Is.EqualTo("PrototypeManifestReferenceBudget"));
            }
            finally { material.StackEntity = saved; }
        });
    }

    [TestCase(false)] [TestCase(true)]
    public async Task AbstractDeclarationsDiscoverTypedReferencesButCannotAuthorizeSpawning(bool entity)
    {
        await AssertOnServer(() =>
        {
            var system = Pair.Server.System<KsProcgenPrototypeManifestSystem>(); var probe = Probe();
            try
            {
                probe.Material = entity ? null : new(AbstractMaterial); probe.Child = entity ? new(AbstractEntity) : null;
                Assert.That(system.TryCapture(new[] { Root }, [], out var manifest, out var issue), Is.True, issue?.ToString());
                Assert.That(manifest!.Bodies.Any(body => body.PrototypeId == Leaf), Is.True);
                var id = entity ? AbstractEntity : AbstractMaterial;
                var template = manifest.AbstractTemplates.Single(entry => entry.PrototypeId == id);
                Assert.That(template.CanonicalFields, Does.Contain(entity ? SecondMaterial : Leaf));
                Assert.Throws<NotSupportedException>(() => ((IList<KsProcgenPrototypeAbstractTemplate>) manifest.AbstractTemplates).Clear());
                if (entity) Assert.That(manifest.Bodies.Single(body => body.PrototypeId == id).Kind, Is.EqualTo(KsProcgenPrototypeBodyKind.EntityDeclaration));
                else
                {
                    var body = manifest.Dependencies.Single(body => body.PrototypeId == id);
                    Assert.That(body.DeclarationOnly, Is.True); Assert.That(body.CanonicalBody, Is.Empty); Assert.That(body.CanonicalDeclaration, Is.Not.Empty);
                }
                Assert.That(system.TryCreatePrototypeSnapshot(manifest, id, out _, out issue), Is.False);
                Assert.That(system.Matches(manifest, out issue), Is.True, issue?.ToString());
                Assert.That(SProtoMan.TryGetKindType(entity ? "entity" : MaterialKind(), out var type), Is.True);
                Assert.That(SProtoMan.TryGetMapping(type!, id, out var declaration), Is.True);
                try
                {
                    declaration!.Add("ksTypedDependencyMutation", new ValueDataNode("changed"));
                    Assert.That(system.Matches(manifest, out issue), Is.False); Assert.That(issue!.Code, Is.EqualTo("PrototypeManifestChanged"));
                }
                finally { declaration!.Remove("ksTypedDependencyMutation"); }
            }
            finally { probe.Material = new(FirstMaterial); probe.Child = null; }
        });
    }

    [TestCase("resolved")] [TestCase("declared")] [TestCase("edge")] [TestCase("leaf")]
    public async Task AnyTransitiveDriftInvalidatesTheManifestAndParentPlacement(string mutation)
    {
        await AssertOnServer(() =>
        {
            var system = Pair.Server.System<KsProcgenPrototypeManifestSystem>(); var material = Material(SecondMaterial);
            var price = material.Price; var stack = material.StackEntity; var leaf = SProtoMan.Index(new EntProtoId(Leaf)); var save = leaf.MapSavable;
            Assert.That(SProtoMan.TryGetMapping<MaterialPrototype>(SecondMaterial, out var declaration), Is.True);
            Assert.That(system.TryCapture(new[] { Root }, [], out var manifest, out var issue), Is.True, issue?.ToString());
            var hash = manifest!.ContentHash; var bodies = manifest.Dependencies.ToArray();
            try
            {
                switch (mutation)
                {
                    case "resolved": material.Price += 1; break;
                    case "declared": declaration!.Add("ksMutation", new ValueDataNode("changed")); break;
                    case "edge": material.StackEntity = new(Root); break;
                    default: leaf.MapSavable = !save; break;
                }
                Assert.That(system.Matches(manifest, out issue), Is.False); Assert.That(issue!.Code, Is.EqualTo("PrototypeManifestChanged"));
                Assert.That(system.MatchesBody(manifest, KsProcgenPrototypeBodyKind.Entity, Root, out issue), Is.False);
                Assert.That(issue!.Code, Is.EqualTo("PrototypeManifestChanged"));
                Assert.That(manifest.ContentHash, Is.EqualTo(hash)); Assert.That(manifest.Dependencies, Is.EqualTo(bodies));
            }
            finally { material.Price = price; material.StackEntity = stack; leaf.MapSavable = save; declaration!.Remove("ksMutation"); }
            Assert.That(system.Matches(manifest, out issue), Is.True, issue?.ToString());
        });
    }

    [TestCase("definitions")] [TestCase("references")] [TestCase("nodes")] [TestCase("characters")] [TestCase("depth")]
    public async Task AllDependencyWorkSharesTheOriginalHardAllowances(string budget)
    {
        await AssertOnServer(() =>
        {
            var system = Pair.Server.System<KsProcgenPrototypeManifestSystem>(); var before = SEntMan.GetEntities().ToHashSet();
            var limits = budget switch
            {
                "definitions" => new KsProcgenPrototypeManifestLimits { MaximumDefinitions = 4 },
                "references" => new KsProcgenPrototypeManifestLimits { MaximumReferences = 4 },
                "nodes" => new KsProcgenPrototypeManifestLimits { MaximumNodes = 1 },
                "characters" => new KsProcgenPrototypeManifestLimits { MaximumCharacters = 1 },
                _ => new KsProcgenPrototypeManifestLimits { MaximumDepth = 1 },
            };
            Assert.That(system.TryCapture(new[] { Root }, [], out var manifest, out var issue, limits: limits), Is.False);
            Assert.That(manifest, Is.Null); Assert.That(issue!.Code, Does.EndWith("Budget"));
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    [Test]
    public async Task ExactCombinedBodyBudgetsAndCanonicalReplayIncludeEveryKind()
    {
        await AssertOnServer(() =>
        {
            var system = Pair.Server.System<KsProcgenPrototypeManifestSystem>();
            Assert.That(system.TryCapture(new[] { Root }, [], out var complete, out var issue), Is.True, issue?.ToString());
            var limits = new KsProcgenPrototypeManifestLimits { MaximumDefinitions = 5, MaximumReferences = 5,
                MaximumNodes = complete!.Nodes, MaximumCharacters = complete.Characters };
            Assert.That(system.TryCapture(new[] { Root }, [], out var exact, out issue, limits: limits), Is.True, issue?.ToString());
            Assert.That(exact!.ContentHash, Is.EqualTo(complete.ContentHash)); Assert.That(exact.Nodes, Is.EqualTo(complete.Nodes));
            Assert.That(exact.ReferenceOccurrences, Is.EqualTo(5));
            Assert.That(exact.Characters, Is.EqualTo(complete.Characters)); Assert.That(system.Matches(exact, out issue), Is.True, issue?.ToString());
            Assert.That(system.TryCapture(new[] { Root }, [], out var denied, out issue,
                limits: limits with { MaximumNodes = limits.MaximumNodes - 1 }), Is.False);
            Assert.That(denied, Is.Null); Assert.That(issue!.Code, Is.EqualTo("PrototypeManifestNodeBudget"));
            Assert.That(system.TryCapture(new[] { Root }, [], out denied, out issue,
                limits: limits with { MaximumCharacters = limits.MaximumCharacters - 1 }), Is.False);
            Assert.That(denied, Is.Null); Assert.That(issue!.Code, Is.EqualTo("PrototypeManifestCharacterBudget"));
        });
    }

    [TestCase(null)] [TestCase("")] [TestCase("UnknownKsKind")] [TestCase("entity")] [TestCase("tile")]
    public async Task InvalidOrMisclassifiedExplicitRootsFailBeforeAnyAllocation(string? kind)
    {
        await AssertOnServer(() =>
        {
            var before = SEntMan.GetEntities().ToHashSet(); var system = Pair.Server.System<KsProcgenPrototypeManifestSystem>();
            Assert.That(system.TryCapture([], [], out var manifest, out var issue,
                dependencyRoots: new KsProcgenPrototypeReference[] { kind == null ? null! : new(kind, FirstMaterial) }), Is.False);
            Assert.That(manifest, Is.Null); Assert.That(issue!.Code, Is.EqualTo("UnsupportedPrototypeManifestKind"));
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    [Test]
    public async Task ExternalTypedSeedRolesRemainDistinctFromExplicitRootsAndReplayTheirClosure()
    {
        await AssertOnServer(() =>
        {
            var system = Pair.Server.System<KsProcgenPrototypeManifestSystem>(); var seed = new KsProcgenPrototypeReference(MaterialKind(), FirstMaterial);
            Assert.That(system.TryCapture([], [], out var referenced, out var issue, typedReferenceSeeds: new[] { seed }), Is.True, issue?.ToString());
            Assert.That(system.TryCapture([], [], out var rooted, out issue, dependencyRoots: new[] { seed }), Is.True, issue?.ToString());
            Assert.That(referenced!.Roots, Is.Empty); Assert.That(referenced.DependencyRoots, Is.Empty);
            Assert.That(referenced.TypedReferenceSeeds, Is.EqualTo(new[] { seed }));
            Assert.That(referenced.Bodies, Is.EqualTo(rooted!.Bodies)); Assert.That(referenced.Dependencies, Is.EqualTo(rooted.Dependencies));
            Assert.That(referenced.ContentHash, Is.Not.EqualTo(rooted.ContentHash));
            Assert.That(system.Matches(referenced, out issue), Is.True, issue?.ToString());
            Assert.That(system.TryCapture([], [], out var reversed, out issue,
                typedReferenceSeeds: new[] { seed, new KsProcgenPrototypeReference("entity", Leaf) }), Is.True, issue?.ToString());
            Assert.That(system.TryCapture([], [], out var reordered, out issue,
                typedReferenceSeeds: new[] { new KsProcgenPrototypeReference("entity", Leaf), seed }), Is.True, issue?.ToString());
            Assert.That(reversed!.ContentHash, Is.EqualTo(reordered!.ContentHash)); Assert.That(reversed.Roots, Is.Empty);
            Assert.That(system.Matches(reversed, out issue), Is.True, issue?.ToString());
        });
    }

    [Test]
    public async Task RepeatedExternalKindSeedsSpendTheOccurrenceAllowanceBeforeDeduplication()
    {
        await AssertOnServer(() =>
        {
            var system = Pair.Server.System<KsProcgenPrototypeManifestSystem>(); var seed = new KsProcgenPrototypeReference(MaterialKind(), FirstMaterial);
            Assert.That(system.TryCapture([], [], out var denied, out var issue,
                limits: new() { MaximumReferences = 3 }, typedReferenceSeeds: Enumerable.Repeat(seed, 4)), Is.False);
            Assert.That(denied, Is.Null); Assert.That(issue!.Code, Is.EqualTo("PrototypeManifestReferenceBudget"));
        });
    }

    [Test]
    public async Task MissingTypedDefinitionFailsInsteadOfFallingBackToAnUncapturedScalar()
    {
        await AssertOnServer(() =>
        {
            var probe = Probe(); var system = Pair.Server.System<KsProcgenPrototypeManifestSystem>();
            try
            {
                probe.Material = new("KsProcgenMissingMaterial");
                Assert.That(system.TryCapture(new[] { Root }, [], out var manifest, out var issue), Is.False);
                Assert.That(manifest, Is.Null); Assert.That(issue!.Code, Is.EqualTo("MissingPrototypeManifestDependency"));
            }
            finally { probe.Material = new(FirstMaterial); }
        });
    }

    [Test]
    public async Task TypedTileReferencesUseTheExistingRegisteredTileBindingAudit()
    {
        await AssertOnServer(() =>
        {
            var probe = Probe(); var system = Pair.Server.System<KsProcgenPrototypeManifestSystem>();
            try
            {
                probe.Material = null; probe.Tile = new("FloorSteel");
                Assert.That(system.TryCapture(new[] { Root }, [], out var manifest, out var issue), Is.True, issue?.ToString());
                Assert.That(manifest!.Bodies.Single(body => body.PrototypeId == "FloorSteel").Kind, Is.EqualTo(KsProcgenPrototypeBodyKind.Tile));
                Assert.That(manifest.Roots.Any(root => root.PrototypeId == "FloorSteel"), Is.False);
                Assert.That(manifest.Dependencies.All(body => body.PrototypeId != "FloorSteel"), Is.True);
                Assert.That(system.Matches(manifest, out issue), Is.True, issue?.ToString());
            }
            finally { probe.Material = new(FirstMaterial); probe.Tile = null; }
        });
    }

    [TestCase(false)] [TestCase(true)]
    public async Task OwnedStageCapturesSelectedAndUnselectedKindDependenciesAndRetiresOnDrift(bool required)
    {
        await AssertOnServer(() =>
        {
            var stages = Pair.Server.System<KsProcgenOwnedStageSystem>(); var material = Material(SecondMaterial); var price = material.Price;
            var before = SEntMan.GetEntities().ToHashSet();
            try
            {
                var spec = new KsProcgenContentDraftSpec { DocumentId = "KindDependencies",
                    Themes = new() { ["KindDependencies"] = new() { Parent = "KsProcgenNoDoorThemeFixture",
                        Goals = new() { FurnishingDensity = 0f, LightingCoverage = 0f }, EntityPacks = new() { new() { Pack = "Contents" } } } },
                    Entities = new() { ["Contents"] = new() { Assemblies = new() { new() { Id = "Core", Assembly = "Core", MinimumCount = required ? 1 : 0 } } } },
                    Assemblies = new() { ["Core"] = new() { AnchorMember = "Root", Members = new() { new() { Id = "Root", Entity = Root } } } } };
                Assert.That(KsProcgenContentDraft.TrySnapshot(spec, out var draft, out var issue), Is.True, issue?.ToString());
                Assert.That(KsProcgenThemeCatalog.TryCapture(SProtoMan, "KindDependencies", out var catalog, out issue, assets: draft), Is.True, issue?.ToString());
                var generated = stages.TryGenerate(new() { RequestId = "KindDependencies", Seed = 17,
                    Shape = new() { Cells = new() { new(0, 0), new(1, 0), new(2, 0) } }, ConnectivityPolicy = KsProcgenConnectivityPolicy.PerIsland },
                    "KindDependencies", content: catalog, planningBudgets: new() { MinimumRoomCells = 1, PreferredMaximumRoomCells = 64 });
                Assert.That(generated.Stage, Is.Not.Null, generated.Issue?.ToString());
                var stage = generated.Stage!;
                Assert.That(stage.Prototypes.Dependencies.Any(body => body.PrototypeId == SecondMaterial), Is.True);
                Assert.That(stage.Scene.Assemblies.SelectMany(assembly => assembly.Poses.Members).Any(member => member.Support.EntityPrototypeId == Root), Is.EqualTo(required));
                material.Price += 1;
                Assert.That(stages.Verify(stage), Is.False);
                var initialized = stages.TryInitialize(stage);
                Assert.That(initialized.Status, Is.EqualTo(KsProcgenOwnedStageStatus.Rejected));
                Assert.That(initialized.Issue!.Code, Is.EqualTo("OwnedStagePrototypeDrift"));
                Assert.That(stage.Active, Is.False); Assert.That(stages.RetainedStageCount, Is.Zero);
            }
            finally { material.Price = price; stages.DiscardAll(); }
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }
}
