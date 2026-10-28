#nullable enable
using System;
using System.Linq;
using Content.Server._KS14.Procedural;
using Content.Shared._KS14.Procedural;
using Content.Shared.Maps;
using Content.Shared.Destructible;
using Content.Shared.Destructible.Thresholds.Behaviors;
using Content.Shared.Destructible.Thresholds.Triggers;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.GameObjects;
using Robust.Shared.Serialization.Manager.Attributes;
using Robust.Shared.Serialization.Markdown.Mapping;
using Robust.Shared.Serialization.Markdown.Sequence;
using Robust.Shared.Serialization.Markdown.Value;
using Robust.UnitTesting;

namespace Content.IntegrationTests.Tests._KS14.Procedural;

[RegisterComponent]
public sealed partial class KsProcgenPartialInheritanceProbeComponent : Component
{
    [DataField(required: true)] public EntProtoId RequiredEntity;
    [DataField] public EntProtoId DeclaredEntity;
    [DataField] public ProtoId<Content.Shared.Materials.MaterialPrototype>? DeclaredMaterial;
}

[TestOf(typeof(KsProcgenPrototypeManifestSystem))]
public sealed class KsProcgenPrototypeInheritanceTests : KsProcgenGameTest
{
    private const string Root = "KsProcgenInheritanceRoot";
    private const string Leaf = "KsProcgenInheritanceLeaf";

    [TestPrototypes]
    private const string Prototypes = """
- type: entity
  id: KsProcgenInheritanceRoot
  components:
  - type: KsProcgenKindDependencyProbe
    child: KsProcgenInheritanceEntityChild
    material: KsProcgenInheritanceMaterialChild
    tile: KsProcgenInheritanceTileChild

- type: entity
  id: KsProcgenInheritanceLeaf

- type: entity
  parent: CarpetBase
  id: KsProcgenInheritanceCarpet
  components:
  - type: Sprite
    sprite: Structures/Furniture/Carpets/red_carpet.rsi
  - type: Icon
    sprite: Structures/Furniture/Carpets/red_carpet.rsi

- type: entity
  abstract: true
  id: KsProcgenInheritanceEntityGrand
  name: grand
  components:
  - type: KsProcgenKindDependencyProbe
    child: KsProcgenInheritanceLeaf

- type: entity
  abstract: true
  parent: KsProcgenInheritanceEntityGrand
  id: KsProcgenInheritanceEntityParent
  name: parent

- type: entity
  parent: KsProcgenInheritanceEntityParent
  id: KsProcgenInheritanceEntityChild
  name: child

- type: entity
  abstract: true
  id: KsProcgenInheritanceSideParent
  name: side

- type: entity
  parent: [ KsProcgenInheritanceEntityParent, KsProcgenInheritanceSideParent ]
  id: KsProcgenInheritanceDiamond
  name: diamond

- type: entity
  id: KsProcgenInheritanceParentOnlyLeaf

- type: entity
  abstract: true
  id: KsProcgenInheritancePartialParent
  components:
  - type: KsProcgenPartialInheritanceProbe
    declaredEntity: KsProcgenInheritanceParentOnlyLeaf
    declaredMaterial: KsProcgenInheritanceParentOnlyMaterial

- type: entity
  parent: KsProcgenInheritancePartialParent
  id: KsProcgenInheritancePartialChild
  components:
  - type: KsProcgenPartialInheritanceProbe
    requiredEntity: KsProcgenInheritanceLeaf
    declaredEntity: KsProcgenInheritanceLeaf
    declaredMaterial: null

- type: material
  id: KsProcgenInheritanceParentOnlyMaterial
  icon: /Textures/Tiles/plating.png
  price: 1

- type: entity
  id: KsProcgenInheritanceConcreteRoot
  components:
  - type: KsProcgenKindDependencyProbe
    child: KsProcgenInheritanceConcreteEntityChild
    material: KsProcgenInheritanceConcreteMaterialChild

- type: entity
  id: KsProcgenInheritanceConcreteEntityParent
  name: concrete parent

- type: entity
  parent: KsProcgenInheritanceConcreteEntityParent
  id: KsProcgenInheritanceConcreteEntityChild
  name: concrete child

- type: material
  id: KsProcgenInheritanceConcreteMaterialParent
  icon: /Textures/Tiles/plating.png
  price: 1

- type: material
  parent: KsProcgenInheritanceConcreteMaterialParent
  id: KsProcgenInheritanceConcreteMaterialChild
  icon: /Textures/Tiles/plating.png
  price: 3

- type: material
  abstract: true
  id: KsProcgenInheritanceMaterialGrand
  price: 1
  stackEntity: KsProcgenInheritanceLeaf

- type: material
  abstract: true
  parent: KsProcgenInheritanceMaterialGrand
  id: KsProcgenInheritanceMaterialParent
  price: 2

- type: material
  abstract: false
  parent: KsProcgenInheritanceMaterialParent
  id: KsProcgenInheritanceMaterialChild
  icon: /Textures/Tiles/plating.png
  price: 3

- type: tile
  abstract: true
  id: KsProcgenInheritanceTileGrand
  name: test tile
  sprite: /Textures/Tiles/plating.png
  itemDrop: KsProcgenInheritanceLeaf
  footstepSounds: null
  barestepSounds: null
  friction: 1

- type: tile
  abstract: true
  parent: KsProcgenInheritanceTileGrand
  id: KsProcgenInheritanceTileParent
  friction: 2

- type: tile
  abstract: true
  parent: KsProcgenInheritanceTileParent
  id: KsProcgenInheritanceTileChild
  friction: 3
""";

    private static string Id(string kind, string role) => "KsProcgenInheritance" + kind + role;
    private Type KindType(string kind)
    {
        Assert.That(SProtoMan.TryGetKindType(kind.ToLowerInvariant(), out var type), Is.True);
        return type!;
    }
    private MappingDataNode Declaration(string kind, string role)
    {
        Assert.That(SProtoMan.TryGetMapping(KindType(kind), Id(kind, role), out var declaration), Is.True);
        return declaration!;
    }
    private KsProcgenPrototypeManifest CaptureAll(KsProcgenPrototypeManifestLimits? limits = null)
    {
        Assert.That(Pair.Server.System<KsProcgenPrototypeManifestSystem>().TryCapture(new[] { Root }, [], out var manifest,
            out var issue, limits: limits), Is.True, issue?.ToString());
        return manifest!;
    }
    private static string Body(KsProcgenPrototypeManifest manifest, string kind, string role) => kind == "Material"
        ? manifest.Dependencies.Single(body => body.PrototypeId == Id(kind, role)).CanonicalBody
        : kind == "Tile" ? manifest.AbstractTemplates.Single(body => body.PrototypeId == Id(kind, role)).CanonicalFields
        : manifest.Bodies.Single(body => body.PrototypeId == Id(kind, role)).CanonicalBody;

    [TestCase("Entity")] [TestCase("Material")] [TestCase("Tile")]
    public async Task TransitiveAbstractAncestorsHaveDeclarationAuthorityWithoutBecomingRoots(string kind)
    {
        await AssertOnServer(() =>
        {
            var before = SEntMan.GetEntities().ToHashSet(); var system = Pair.Server.System<KsProcgenPrototypeManifestSystem>();
            var manifest = CaptureAll();
            Assert.That(manifest.SchemaVersion, Is.EqualTo(11)); Assert.That(manifest.FullContentClosureVerified, Is.False);
            Assert.That(manifest.Roots, Is.EqualTo(new[] { new KsProcgenPrototypeRoot(KsProcgenPrototypeBodyKind.Entity, Root) }));
            Assert.That(manifest.DependencyRoots, Is.Empty); Assert.That(manifest.TypedReferenceSeeds, Is.Empty);
            foreach (var role in new[] { "Grand", "Parent" })
            {
                var id = Id(kind, role);
                if (kind == "Material")
                {
                    var body = manifest.Dependencies.Single(entry => entry.PrototypeId == id);
                    Assert.That(body.DeclarationOnly, Is.True); Assert.That(body.CanonicalBody, Is.Empty);
                }
                else
                {
                    var body = manifest.Bodies.Single(entry => entry.PrototypeId == id);
                    Assert.That(body.Kind, Is.EqualTo(kind == "Entity" ? KsProcgenPrototypeBodyKind.EntityDeclaration : KsProcgenPrototypeBodyKind.TileDeclaration));
                    Assert.That(body.CanonicalBody, Is.Empty); Assert.That(body.CanonicalDeclaration, Is.Not.Empty);
                    Assert.That(body.RuntimeTileId, Is.Zero);
                    Assert.That(system.MatchesBody(manifest, body.Kind, id, out var denied), Is.False);
                    Assert.That(denied!.Code, Is.EqualTo("UnsupportedPrototypeManifestPlacementKind"));
                }
                Assert.That(manifest.AbstractTemplates.Count(entry => entry.PrototypeId == id), Is.EqualTo(1));
                Assert.That(system.TryCreatePrototypeSnapshot(manifest, id, out _, out _), Is.False);
            }
            Assert.That(manifest.Bodies.Any(body => body.PrototypeId == Leaf), Is.True);
            Assert.That(system.Matches(manifest, out var issue), Is.True, issue?.ToString());
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    [TestCase("Entity")] [TestCase("Material")] [TestCase("Tile")]
    public async Task AncestorDeclarationDriftInvalidatesPlacementEvenWhenChildFieldsStayEqual(string kind)
    {
        await AssertOnServer(() =>
        {
            var system = Pair.Server.System<KsProcgenPrototypeManifestSystem>(); var manifest = CaptureAll();
            var child = Body(manifest, kind, "Child"); var declaration = Declaration(kind, "Grand");
            var field = kind == "Entity" ? "name" : kind == "Material" ? "price" : "friction";
            var original = declaration[field];
            try
            {
                declaration[field] = new ValueDataNode(kind == "Entity" ? "changed grand" : "41");
                var changed = CaptureAll();
                Assert.That(Body(changed, kind, "Child"), Is.EqualTo(child), "The child overrides the altered ancestor field.");
                Assert.That(system.Matches(manifest, out var issue), Is.False); Assert.That(issue!.Code, Is.EqualTo("PrototypeManifestChanged"));
                Assert.That(system.MatchesBody(manifest, KsProcgenPrototypeBodyKind.Entity, Root, out issue), Is.False);
                Assert.That(issue!.Code, Is.EqualTo("PrototypeManifestChanged"));
                Assert.That(Body(manifest, kind, "Child"), Is.EqualTo(child));
                Assert.That(changed.ContentHash, Is.Not.EqualTo(manifest.ContentHash));
            }
            finally { declaration[field] = original; }
            Assert.That(system.Matches(manifest, out var restored), Is.True, restored?.ToString());
        });
    }

    [TestCase("Entity")] [TestCase("Material")]
    public async Task RuntimeParentArrayDriftIsIndependentOfTheRetainedDeclaration(string kind)
    {
        await AssertOnServer(() =>
        {
            var manifest = CaptureAll(); var declaration = Declaration(kind, "Child");
            Assert.That(SProtoMan.TryIndex(KindType(kind), Id(kind, "Child"), out var prototype), Is.True);
            var parents = ((IInheritingPrototype) prototype!).Parents!; var original = parents[0];
            var parentNode = declaration["parent"];
            try
            {
                parents[0] = Id(kind, "Grand");
                Assert.That(declaration["parent"], Is.SameAs(parentNode));
                Assert.That(Pair.Server.System<KsProcgenPrototypeManifestSystem>().Matches(manifest, out var issue), Is.False);
                Assert.That(issue!.Code, Is.EqualTo("PrototypeManifestChanged"));
            }
            finally { parents[0] = original; }
        });
    }

    [TestCase("Entity")] [TestCase("Material")] [TestCase("Tile")]
    public async Task CyclicRepeatedParentEdgesDeduplicateDefinitionsButConsumeExactReferenceWork(string kind)
    {
        await AssertOnServer(() =>
        {
            var declaration = Declaration(kind, "Grand");
            declaration.Add("parent", new SequenceDataNode { new ValueDataNode(Id(kind, "Child")), new ValueDataNode(Id(kind, "Child")) });
            try
            {
                var manifest = CaptureAll(); var system = Pair.Server.System<KsProcgenPrototypeManifestSystem>();
                Assert.That(manifest.Bodies.Count + manifest.Dependencies.Count, Is.LessThan(32));
                Assert.That(manifest.AbstractTemplates.Count(entry => entry.PrototypeId == Id(kind, "Grand")), Is.EqualTo(1));
                Assert.That(CaptureAll(limits: manifest.Limits with { MaximumReferences = manifest.ReferenceOccurrences }).ContentHash,
                    Is.EqualTo(manifest.ContentHash));
                Assert.That(system.TryCapture(new[] { Root }, [], out var denied, out var issue,
                    limits: manifest.Limits with { MaximumReferences = manifest.ReferenceOccurrences - 1 }), Is.False);
                Assert.That(denied, Is.Null); Assert.That(issue!.Code, Is.EqualTo("PrototypeManifestReferenceBudget"));
                Assert.That(system.Matches(manifest, out issue), Is.True, issue?.ToString());
            }
            finally { declaration.Remove("parent"); }
        });
    }

    [TestCase("Entity")] [TestCase("Material")] [TestCase("Tile")]
    public async Task MissingAncestorsRejectAtomicallyWithoutAllocatingWorldEntities(string kind)
    {
        await AssertOnServer(() =>
        {
            var declaration = Declaration(kind, "Grand"); var before = SEntMan.GetEntities().ToHashSet();
            declaration.Add("parent", new ValueDataNode("KsProcgenMissingAncestor"));
            try
            {
                Assert.That(Pair.Server.System<KsProcgenPrototypeManifestSystem>().TryCapture(new[] { Root }, [], out var denied, out var issue), Is.False);
                Assert.That(denied, Is.Null);
                Assert.That(issue!.Code, Is.EqualTo(kind == "Entity" ? "MissingPrototypeManifestEntity" :
                    kind == "Tile" ? "MissingPrototypeManifestTile" : "MissingPrototypeManifestDependency"));
                Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
            }
            finally { declaration.Remove("parent"); }
        });
    }

    [TestCase("Entity")] [TestCase("Material")] [TestCase("Tile")]
    public async Task MalformedParentShapesAreRejectedInsteadOfBeingTreatedAsOrdinaryStrings(string kind)
    {
        await AssertOnServer(() =>
        {
            var declaration = Declaration(kind, "Child"); var original = declaration["parent"];
            try
            {
                declaration["parent"] = new MappingDataNode { { "id", new ValueDataNode(Id(kind, "Grand")) } };
                Assert.That(Pair.Server.System<KsProcgenPrototypeManifestSystem>().TryCapture(new[] { Root }, [], out var denied, out var issue), Is.False);
                Assert.That(denied, Is.Null); Assert.That(issue!.Code, Is.EqualTo("InvalidPrototypeManifestInheritance"));
            }
            finally { declaration["parent"] = original; }
        });
    }

    [Test]
    public async Task CombinedDefinitionAllowanceIncludesEveryAbstractAncestor()
    {
        await AssertOnServer(() =>
        {
            var complete = CaptureAll(); var count = complete.Bodies.Count + complete.Dependencies.Count;
            Assert.That(count, Is.GreaterThanOrEqualTo(11));
            Assert.That(CaptureAll(limits: complete.Limits with { MaximumDefinitions = count }).ContentHash, Is.EqualTo(complete.ContentHash));
            Assert.That(Pair.Server.System<KsProcgenPrototypeManifestSystem>().TryCapture(new[] { Root }, [], out var denied, out var issue,
                limits: complete.Limits with { MaximumDefinitions = count - 1 }), Is.False);
            Assert.That(denied, Is.Null); Assert.That(issue!.Code, Is.EqualTo("PrototypeManifestDefinitionBudget"));
        });
    }

    [TestCase("Entity")] [TestCase("Tile")]
    public async Task AbstractAncestorsCannotBeRequestedAsConcreteRoots(string kind)
    {
        await AssertOnServer(() =>
        {
            var id = Id(kind, "Grand");
            Assert.That(Pair.Server.System<KsProcgenPrototypeManifestSystem>().TryCapture(kind == "Entity" ? new[] { id } : [],
                kind == "Tile" ? new[] { id } : [], out var denied, out var issue), Is.False);
            Assert.That(denied, Is.Null); Assert.That(issue!.Code, Is.EqualTo(kind == "Entity" ? "MissingPrototypeManifestEntity" : "MissingPrototypeManifestTile"));
        });
    }
    [TestCase(false)] [TestCase(true)]
    public async Task OwnedStagesRetireOnAncestorDriftInSelectedOrUnselectedCandidates(bool required)
    {
        await AssertOnServer(() =>
        {
            var stages = Pair.Server.System<KsProcgenOwnedStageSystem>(); var declaration = Declaration("Material", "Grand");
            var original = declaration["price"]; var before = SEntMan.GetEntities().ToHashSet();
            try
            {
                var spec = new KsProcgenContentDraftSpec { DocumentId = "Inheritance",
                    Themes = new() { ["Inheritance"] = new() { Parent = "KsProcgenNoDoorThemeFixture",
                        Goals = new() { FurnishingDensity = 0f, LightingCoverage = 0f }, EntityPacks = new() { new() { Pack = "Contents" } } } },
                    Entities = new() { ["Contents"] = new() { Assemblies = new() { new() { Id = "Core", Assembly = "Core", MinimumCount = required ? 1 : 0 } } } },
                    Assemblies = new() { ["Core"] = new() { AnchorMember = "Root", Members = new() { new() { Id = "Root", Entity = Root } } } } };
                Assert.That(KsProcgenContentDraft.TrySnapshot(spec, out var draft, out var issue), Is.True, issue?.ToString());
                Assert.That(KsProcgenThemeCatalog.TryCapture(SProtoMan, "Inheritance", out var catalog, out issue, assets: draft), Is.True, issue?.ToString());
                var result = stages.TryGenerate(new() { RequestId = "Inheritance", Seed = 17,
                    Shape = new() { Cells = new() { new(0, 0), new(1, 0), new(2, 0) } }, ConnectivityPolicy = KsProcgenConnectivityPolicy.PerIsland },
                    "Inheritance", content: catalog, planningBudgets: new() { MinimumRoomCells = 1, PreferredMaximumRoomCells = 64 });
                Assert.That(result.Stage, Is.Not.Null, result.Issue?.ToString());
                var stage = result.Stage!;
                Assert.That(stage.Prototypes.Dependencies.Any(body => body.PrototypeId == Id("Material", "Grand")), Is.True);
                Assert.That(stage.Scene.Assemblies.SelectMany(assembly => assembly.Poses.Members)
                    .Any(member => member.Support.EntityPrototypeId == Root), Is.EqualTo(required));
                declaration["price"] = new ValueDataNode("23");
                Assert.That(stages.Verify(stage), Is.False);
                Assert.That(stages.TryInitialize(stage).Status, Is.EqualTo(KsProcgenOwnedStageStatus.Rejected));
                Assert.That(stage.Active, Is.False); Assert.That(stages.RetainedStageCount, Is.Zero);
            }
            finally { declaration["price"] = original; stages.DiscardAll(); }
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    [Test]
    public async Task ConcreteTileAncestorDriftRetainsRuntimeRegistrationButInvalidatesPlacement()
    {
        await AssertOnServer(() =>
        {
            var system = Pair.Server.System<KsProcgenPrototypeManifestSystem>();
            var tile = SProtoMan.Index(new ProtoId<ContentTileDefinition>("FloorSteel")); var runtimeId = tile.TileId;
            Assert.That(system.TryCapture([], new[] { tile.ID }, out var manifest, out var issue), Is.True, issue?.ToString());
            var child = manifest!.Bodies.Single(body => body.PrototypeId == tile.ID);
            Assert.That(SProtoMan.TryGetMapping<ContentTileDefinition>(tile.Parents![0], out var declaration), Is.True);
            var original = declaration!["heatCapacity"];
            try
            {
                declaration["heatCapacity"] = new ValueDataNode("19");
                Assert.That(tile.TileId, Is.EqualTo(runtimeId));
                Assert.That(Pair.Server.ResolveDependency<ITileDefinitionManager>()[tile.ID].TileId, Is.EqualTo(runtimeId));
                Assert.That(system.TryCapture([], new[] { tile.ID }, out var changed, out issue), Is.True, issue?.ToString());
                Assert.That(changed!.Bodies.Single(body => body.PrototypeId == tile.ID), Is.EqualTo(child));
                Assert.That(system.MatchesBody(manifest, KsProcgenPrototypeBodyKind.Tile, tile.ID, out issue), Is.False);
                Assert.That(issue!.Code, Is.EqualTo("PrototypeManifestChanged"));
            }
            finally { declaration["heatCapacity"] = original; }
        });
    }

    [Test]
    public async Task CarpetAncestorKeepsDestructionBehaviorsOnTheThreshold()
    {
        await AssertOnServer(() =>
        {
            var prototype = SProtoMan.Index(new EntProtoId("KsProcgenInheritanceCarpet"));
            Assert.That(prototype.TryComp<DestructibleComponent>(out var destructible, SEntMan.ComponentFactory), Is.True);
            Assert.That(destructible!.Thresholds.Count, Is.EqualTo(1));
            var threshold = destructible.Thresholds.Single();
            Assert.That(threshold.Trigger, Is.TypeOf<DamageTrigger>());
            Assert.That(threshold.Behaviors.Count, Is.EqualTo(1));
            Assert.That(threshold.Behaviors.Single(), Is.TypeOf<DoActsBehavior>());
            Assert.That(((DoActsBehavior) threshold.Behaviors.Single()).HasAct(ThresholdActs.Destruction), Is.True);
            Assert.That(Pair.Server.System<KsProcgenPrototypeManifestSystem>().TryCapture(new[] { prototype.ID }, [],
                out var manifest, out var issue), Is.True, issue?.ToString());
            Assert.That(manifest!.Bodies.Single(body => body.PrototypeId == "CarpetBase").Kind, Is.EqualTo(KsProcgenPrototypeBodyKind.EntityDeclaration));
        });
    }

    [Test]
    public async Task PartialAbstractFieldsDiscoverMaskedReferencesWithoutRequiringCompleteInstances()
    {
        await AssertOnServer(() =>
        {
            const string parent = "KsProcgenInheritancePartialParent";
            const string child = "KsProcgenInheritancePartialChild";
            const string parentLeaf = "KsProcgenInheritanceParentOnlyLeaf";
            var prototype = SProtoMan.Index(new EntProtoId(child));
            Assert.That(prototype.TryComp<KsProcgenPartialInheritanceProbeComponent>(out var probe, SEntMan.ComponentFactory), Is.True);
            Assert.That(probe!.DeclaredEntity.Id, Is.EqualTo(Leaf));
            Assert.That(SProtoMan.TryGetMapping<EntityPrototype>(parent, out var declaration), Is.True);
            Assert.That(declaration!.Get<SequenceDataNode>("components").Cast<MappingDataNode>()
                .Single(component => component.Get<ValueDataNode>("type").Value == "KsProcgenPartialInheritanceProbe")
                .Has("requiredEntity"), Is.False);
            var before = SEntMan.GetEntities().ToHashSet();
            Assert.That(Pair.Server.System<KsProcgenPrototypeManifestSystem>().TryCapture(new[] { child }, [], out var manifest,
                out var issue), Is.True, issue?.ToString());
            Assert.That(manifest!.Bodies.Any(body => body.PrototypeId == parentLeaf), Is.True);
            Assert.That(manifest.Dependencies.Any(body => body.PrototypeId == "KsProcgenInheritanceParentOnlyMaterial"), Is.True);
            Assert.That(manifest.AbstractTemplates.Single(template => template.PrototypeId == parent).CanonicalFields,
                Does.Contain(parentLeaf));
            Assert.That(Pair.Server.System<KsProcgenPrototypeManifestSystem>().TryCreatePrototypeSnapshot(manifest, parent, out _, out _), Is.False);
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    [Test]
    public async Task DiamondAncestorsAreUniqueAndDeclaredParentOrderStillChangesIdentity()
    {
        await AssertOnServer(() =>
        {
            const string diamond = "KsProcgenInheritanceDiamond";
            var system = Pair.Server.System<KsProcgenPrototypeManifestSystem>();
            Assert.That(system.TryCapture(new[] { diamond, Id("Entity", "Child") }, [], out var manifest, out var issue), Is.True, issue?.ToString());
            Assert.That(manifest!.Bodies.Count(body => body.PrototypeId == Id("Entity", "Grand")), Is.EqualTo(1));
            Assert.That(manifest.Bodies.Count(body => body.PrototypeId == "KsProcgenInheritanceSideParent"), Is.EqualTo(1));
            Assert.That(SProtoMan.TryGetMapping<EntityPrototype>(diamond, out var declaration), Is.True);
            var original = declaration!["parent"]; var childBody = manifest.Bodies.Single(body => body.PrototypeId == diamond);
            try
            {
                declaration["parent"] = new SequenceDataNode { new ValueDataNode("KsProcgenInheritanceSideParent"), new ValueDataNode(Id("Entity", "Parent")) };
                Assert.That(system.TryCapture(new[] { diamond, Id("Entity", "Child") }, [], out var changed, out issue), Is.True, issue?.ToString());
                Assert.That(changed!.Bodies.Single(body => body.PrototypeId == diamond).CanonicalBody, Is.EqualTo(childBody.CanonicalBody));
                Assert.That(changed.ContentHash, Is.Not.EqualTo(manifest.ContentHash));
                Assert.That(system.MatchesBody(manifest, KsProcgenPrototypeBodyKind.Entity, diamond, out issue), Is.False);
                Assert.That(issue!.Code, Is.EqualTo("PrototypeManifestChanged"));
            }
            finally { declaration["parent"] = original; }
        });
    }

    [Test]
    public async Task OpaqueAncestorFieldsRetainBoundedInspectionGapsWithoutGuessingPrototypeIds()
    {
        await AssertOnServer(() =>
        {
            var declaration = Declaration("Entity", "Grand"); var before = CaptureAll();
            declaration.Add("procgenUnknownField", new ValueDataNode("KsProcgenUnknownUnregisteredValue"));
            try
            {
                var manifest = CaptureAll();
                var template = manifest.AbstractTemplates.Single(entry => entry.PrototypeId == Id("Entity", "Grand"));
                Assert.That(template.HasDeclaredInspectionGaps, Is.True);
                Assert.That(template.CanonicalInspection, Does.Contain("UnrecognizedField|"));
                Assert.That(template.CanonicalInspection, Does.Contain("procgenUnknownField"));
                Assert.That(template.CanonicalFields, Does.Contain("KsProcgenUnknownUnregisteredValue"));
                Assert.That(manifest.FullContentClosureVerified, Is.False);
                Assert.That(manifest.ContentHash, Is.Not.EqualTo(before.ContentHash));
                Assert.That(Pair.Server.System<KsProcgenPrototypeManifestSystem>().Matches(manifest, out var issue), Is.True, issue?.ToString());
                Assert.That(Pair.Server.System<KsProcgenPrototypeManifestSystem>().TryCreatePrototypeSnapshot(manifest,
                    Id("Entity", "Grand"), out _, out _), Is.False);
                Assert.That(CaptureAll(limits: manifest.Limits with { MaximumCharacters = manifest.Characters }).ContentHash,
                    Is.EqualTo(manifest.ContentHash));
                Assert.That(Pair.Server.System<KsProcgenPrototypeManifestSystem>().TryCapture(new[] { Root }, [], out var denied, out issue,
                    limits: manifest.Limits with { MaximumCharacters = manifest.Characters - 1 }), Is.False);
                Assert.That(denied, Is.Null); Assert.That(issue!.Code, Is.EqualTo("PrototypeManifestCharacterBudget"));
            }
            finally { declaration.Remove("procgenUnknownField"); }
        });
    }

    [TestCase("Entity")] [TestCase("Material")]
    public async Task ConcreteAncestorRuntimeBodyDriftIsDetectedBeneathAnUnchangedChild(string kind)
    {
        await AssertOnServer(() =>
        {
            const string root = "KsProcgenInheritanceConcreteRoot";
            var parentId = "KsProcgenInheritanceConcrete" + kind + "Parent";
            var childId = "KsProcgenInheritanceConcrete" + kind + "Child";
            var system = Pair.Server.System<KsProcgenPrototypeManifestSystem>();
            Assert.That(system.TryCapture(new[] { root }, [], out var manifest, out var issue), Is.True, issue?.ToString());
            Assert.That(SProtoMan.TryIndex(KindType(kind), parentId, out var parent), Is.True);
            var oldSave = parent is EntityPrototype entity ? entity.MapSavable : false;
            var oldPrice = parent is Content.Shared.Materials.MaterialPrototype material ? material.Price : 0;
            try
            {
                if (parent is EntityPrototype entityParent) entityParent.MapSavable = !oldSave;
                else ((Content.Shared.Materials.MaterialPrototype) parent!).Price += 1;
                Assert.That(system.TryCapture(new[] { root }, [], out var changed, out issue), Is.True, issue?.ToString());
                if (kind == "Entity")
                    Assert.That(changed!.Bodies.Single(body => body.PrototypeId == childId), Is.EqualTo(manifest!.Bodies.Single(body => body.PrototypeId == childId)));
                else
                    Assert.That(changed!.Dependencies.Single(body => body.PrototypeId == childId), Is.EqualTo(manifest!.Dependencies.Single(body => body.PrototypeId == childId)));
                Assert.That(system.Matches(manifest!, out issue), Is.False); Assert.That(issue!.Code, Is.EqualTo("PrototypeManifestChanged"));
                Assert.That(system.MatchesBody(manifest!, KsProcgenPrototypeBodyKind.Entity, root, out issue), Is.False);
                Assert.That(issue!.Code, Is.EqualTo("PrototypeManifestChanged"));
            }
            finally
            {
                if (parent is EntityPrototype entityParent) entityParent.MapSavable = oldSave;
                else ((Content.Shared.Materials.MaterialPrototype) parent!).Price = oldPrice;
            }
        });
    }

    [TestCase("targetUid")] [TestCase("targetNetEntity")] [TestCase("targetMapId")]
    public async Task KnownWorldReferencesInAbstractFieldsRemainHardFailuresInsteadOfOpaqueGaps(string field)
    {
        await AssertOnServer(() =>
        {
            var components = Declaration("Entity", "Grand").Get<SequenceDataNode>("components");
            var worldReference = new MappingDataNode { { "type", new ValueDataNode("KsProcgenManifestReferenceProbe") },
                { field, new ValueDataNode("1") } };
            var before = SEntMan.GetEntities().ToHashSet(); components.Add(worldReference);
            try
            {
                Assert.That(Pair.Server.System<KsProcgenPrototypeManifestSystem>().TryCapture(new[] { Root }, [], out var denied, out var issue), Is.False);
                Assert.That(denied, Is.Null); Assert.That(issue!.Code, Is.EqualTo("UnsupportedPrototypeManifestBody"));
                Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
            }
            finally { components.Remove(worldReference); }
        });
    }

}
