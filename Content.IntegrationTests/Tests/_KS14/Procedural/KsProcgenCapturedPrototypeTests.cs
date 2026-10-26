#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._KS14.Procedural;
using Content.Shared._KS14.Procedural;
using Content.Shared.Containers.ItemSlots;
using Robust.Shared.Audio.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager.Attributes;
using Robust.Shared.Serialization.Markdown.Mapping;
using Robust.Shared.Serialization.Markdown.Sequence;
using Robust.Shared.Serialization.Markdown.Value;
using Robust.UnitTesting;

namespace Content.IntegrationTests.Tests._KS14.Procedural;

[DataDefinition, CopyByRef]
public sealed partial class KsProcgenCapturedSharedPayload
{
    [DataField] public List<int> Numbers = new() { 3, 4 };
}

[RegisterComponent]
public sealed partial class KsProcgenCapturedDefaultsProbeComponent : Component
{
    [DataField] public int Value = 17;
    [DataField] public List<int> Numbers = new() { 1, 2 };
    [DataField] public KsProcgenCapturedSharedPayload Shared = new();
}

public sealed partial class KsProcgenCapturedDefaultsTestSystem : EntitySystem
{
    public Action<EntityUid>? StartupAction;
    public override void Initialize()
    {
        base.Initialize();
        // The integration project does not import the subscription generator.
        SubscribeLocalEvent<KsProcgenCapturedDefaultsProbeComponent, ComponentStartup>(OnStartup);
    }
    private void OnStartup(Entity<KsProcgenCapturedDefaultsProbeComponent> entity, ref ComponentStartup args) => StartupAction?.Invoke(entity.Owner);
}

public sealed class KsProcgenCapturedPrototypeTests : KsProcgenGameTest
{
    private const string Probe = "KsProcgenCapturedDefaultsFixture";
    private const string Wall = "KsProcgenCapturedWallFixture";
    [TestPrototypes]
    private const string Prototypes = """
- type: entity
  parent: CoordinatesDisk
  id: KsProcgenCapturedDefaultsFixture
  categories: [ HideSpawnMenu ]
  components:
  - type: Appearance
    netsync: false
  - type: KsProcgenCapturedDefaultsProbe
  - type: KsProcgenManifestReferenceProbe
- type: entity
  parent: WallSolid
  id: KsProcgenCapturedWallFixture
  categories: [ HideSpawnMenu ]
  components:
  - type: KsProcgenCapturedDefaultsProbe
""";

    [Test]
    public async Task OwnedSlotLoadingRetainsDeclaredSoundsWithoutPlayingAnInteraction()
    {
        await AssertOnServer(() =>
        {
            var stages = Pair.Server.System<KsProcgenOwnedStageSystem>(); var before = SEntMan.GetEntities().ToHashSet();
            try
            {
                var generated = stages.TryGenerate(Request(), "Captured", planningBudgets: Budgets(), content: Catalog(true));
                Assert.That(generated.Status, Is.EqualTo(KsProcgenOwnedStageStatus.PositionedPreview), generated.Issue?.Code);
                var stage = generated.Stage!;
                var host = stage.Assemblies.SelectMany(assembly => assembly.Values)
                    .Single(uid => SEntMan.HasComponent<ItemSlotsComponent>(uid));
                var slot = SEntMan.GetComponent<ItemSlotsComponent>(host).Slots["disk_slot"];
                Assert.That(slot.InsertSound, Is.Not.Null);
                Assert.That(slot.ContainerSlot!.ContainedEntities.Count, Is.EqualTo(1));
                Assert.That(stage.SpawnedEntities.Any(uid => SEntMan.HasComponent<AudioComponent>(uid)), Is.False);
                Assert.That(stages.Verify(stage), Is.True);
            }
            finally { stages.DiscardAll(); }
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    [Test]
    public async Task CapturedAllocationIgnoresCurrentDefaultsAndSeparatesInstancesAndMetadata()
    {
        await AssertOnServer(() =>
        {
            var system = Pair.Server.System<KsProcgenPrototypeManifestSystem>(); var original = Capture(Probe);
            var live = SProtoMan.Index(new EntProtoId(Probe)); var defaults = (KsProcgenCapturedDefaultsProbeComponent) live.Components["KsProcgenCapturedDefaultsProbe"].Component;
            var maps = Pair.Server.System<SharedMapSystem>(); var before = SEntMan.GetEntities().ToHashSet();
            var map = maps.CreateMap(out var mapId, runMapInit: false); maps.SetPaused(mapId, true);
            try
            {
                defaults.Value = 99; defaults.Numbers[0] = 99; defaults.Shared.Numbers[0] = 99;
                var first = system.SpawnCaptured(original, Probe, new EntityCoordinates(map, Vector2.Zero));
                var actual = SEntMan.GetComponent<KsProcgenCapturedDefaultsProbeComponent>(first);
                Assert.That(actual.Value, Is.EqualTo(17)); Assert.That(actual.Numbers, Is.EqualTo(new[] { 1, 2 }));
                Assert.That(actual.Shared.Numbers, Is.EqualTo(new[] { 3, 4 }));
                Assert.That(SEntMan.GetComponent<AppearanceComponent>(first).NetSyncEnabled, Is.False);
                var metadata = SEntMan.GetComponent<MetaDataComponent>(first).EntityPrototype!;
                Assert.That(metadata, Is.Not.SameAs(live)); Assert.That(metadata.HideSpawnMenu, Is.True);
                var metadataDefaults = (KsProcgenCapturedDefaultsProbeComponent) metadata.Components["KsProcgenCapturedDefaultsProbe"].Component;
                Assert.That(metadataDefaults.Numbers, Is.Not.SameAs(actual.Numbers)); Assert.That(actual.Numbers, Is.Not.SameAs(defaults.Numbers));
                Assert.That(metadataDefaults.Shared, Is.Not.SameAs(actual.Shared)); Assert.That(actual.Shared, Is.Not.SameAs(defaults.Shared));
                metadataDefaults.Numbers[0] = 88; actual.Numbers[0] = 77;
                metadataDefaults.Shared.Numbers[0] = 88;
                Assert.That(actual.Shared.Numbers[0], Is.EqualTo(3)); actual.Shared.Numbers[0] = 77;
                var second = system.SpawnCaptured(original, Probe, new EntityCoordinates(map, new Vector2(1f, 0f)));
                Assert.That(SEntMan.GetComponent<KsProcgenCapturedDefaultsProbeComponent>(second).Numbers, Is.EqualTo(new[] { 1, 2 }));
                Assert.That(SEntMan.GetComponent<KsProcgenCapturedDefaultsProbeComponent>(second).Shared.Numbers, Is.EqualTo(new[] { 3, 4 }));
                Assert.That(defaults.Numbers[0], Is.EqualTo(99));
                Assert.That(system.MatchesSnapshot(original, metadata, out var issue), Is.False);
                Assert.That(issue?.Code, Is.EqualTo("CapturedPrototypeRoundtripDrift"));
            }
            finally { defaults.Value = 17; defaults.Numbers[0] = 1; defaults.Shared.Numbers[0] = 3; maps.DeleteMap(mapId); }
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    [TestCase(false)] [TestCase(true)]
    public async Task OwnedDirectAndAssemblyPlacementUseCapturedDefaultsAcrossTransientLiveEdits(bool assembly)
    {
        await AssertOnServer(() =>
        {
            var stages = Pair.Server.System<KsProcgenOwnedStageSystem>(); var hook = Pair.Server.System<KsProcgenCapturedDefaultsTestSystem>();
            var prototype = SProtoMan.Index(new EntProtoId(assembly ? Probe : Wall));
            var defaults = (KsProcgenCapturedDefaultsProbeComponent) prototype.Components["KsProcgenCapturedDefaultsProbe"].Component;
            var allocations = 0; var starts = 0; var observed = new List<int>(); var before = SEntMan.GetEntities().ToHashSet();
            void OnAdded(Entity<MetaDataComponent> entity) { if (++allocations == (assembly ? 4 : 3)) defaults.Value = 99; }
            try
            {
                hook.StartupAction = uid => { starts++; observed.Add(SEntMan.GetComponent<KsProcgenCapturedDefaultsProbeComponent>(uid).Value); defaults.Value = 17; };
                SEntMan.EntityAdded += OnAdded;
                var result = stages.TryGenerate(Request(assembly: assembly), "Captured", planningBudgets: Budgets(), content: Catalog(assembly));
                Assert.That(result.Status, Is.EqualTo(KsProcgenOwnedStageStatus.PositionedPreview), message: result.Issue?.Code);
                Assert.That(starts, Is.GreaterThan(0)); Assert.That(observed.All(value => value == 17), Is.True);
                Assert.That(stages.Verify(result.Stage!), Is.True);
                Assert.That(assembly ? result.Stage!.Assemblies.Count : result.Stage!.Entities.Count, Is.GreaterThan(0));
            }
            finally { SEntMan.EntityAdded -= OnAdded; hook.StartupAction = null; defaults.Value = 17; stages.DiscardAll(); }
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    [Test]
    public async Task ExposedMetadataCannotChangeThePrivateComponentLoadingSource()
    {
        await AssertOnServer(() =>
        {
            var system = Pair.Server.System<KsProcgenPrototypeManifestSystem>(); var manifest = Capture(Probe);
            var maps = Pair.Server.System<SharedMapSystem>(); var map = maps.CreateMap(out var mapId, runMapInit: false); var changed = 0;
            void OnAdded(AddedComponentEventArgs args)
            {
                if (args.BaseArgs.Component is not AppearanceComponent) return;
                var metadata = SEntMan.GetComponent<MetaDataComponent>(args.BaseArgs.Owner).EntityPrototype;
                if (metadata?.ID != Probe) return;
                ((KsProcgenCapturedDefaultsProbeComponent) metadata.Components["KsProcgenCapturedDefaultsProbe"].Component).Value = 99;
                changed++;
            }
            try
            {
                SEntMan.ComponentAdded += OnAdded;
                var uid = system.SpawnCaptured(manifest, Probe, new EntityCoordinates(map, Vector2.Zero));
                Assert.That(changed, Is.EqualTo(1)); Assert.That(SEntMan.GetComponent<KsProcgenCapturedDefaultsProbeComponent>(uid).Value, Is.EqualTo(17));
            }
            finally { SEntMan.ComponentAdded -= OnAdded; maps.DeleteMap(mapId); }
        });
    }

    [TestCase(false)] [TestCase(true)]
    public async Task OwnedVerificationRejectsMutatedPrivateMetadataWithoutGlobalPrototypeDrift(bool assembly)
    {
        await AssertOnServer(() =>
        {
            var stages = Pair.Server.System<KsProcgenOwnedStageSystem>();
            try
            {
                var result = stages.TryGenerate(Request(assembly: assembly), "Captured", planningBudgets: Budgets(), content: Catalog(assembly));
                Assert.That(result.Status, Is.EqualTo(KsProcgenOwnedStageStatus.PositionedPreview), message: result.Issue?.Code);
                var stage = result.Stage!; var candidates = assembly ? stage.Assemblies.SelectMany(members => members.Values) : stage.Entities;
                var uid = candidates.First(candidate => SEntMan.HasComponent<KsProcgenCapturedDefaultsProbeComponent>(candidate));
                var metadata = SEntMan.GetComponent<MetaDataComponent>(uid).EntityPrototype!;
                ((KsProcgenCapturedDefaultsProbeComponent) metadata.Components["KsProcgenCapturedDefaultsProbe"].Component).Value = 99;
                Assert.That(Pair.Server.System<KsProcgenPrototypeManifestSystem>().Matches(stage.Prototypes, out var issue), Is.True, message: issue?.Code);
                Assert.That(stages.Verify(stage), Is.False);
                var initialized = stages.TryInitialize(stage);
                Assert.That(initialized.Status, Is.EqualTo(KsProcgenOwnedStageStatus.Rejected));
                Assert.That(initialized.Issue?.Code, Is.EqualTo("OwnedStageInstancePrototypeDrift")); Assert.That(stage.Active, Is.False);
            }
            finally { stages.DiscardAll(); }
        });
    }

    [TestCase(false)] [TestCase(true)]
    public async Task ImageBudgetRejectsBeforeMapOrTerrainAllocation(bool assembly)
    {
        await AssertOnServer(() =>
        {
            var before = SEntMan.GetEntities().ToHashSet(); var allocated = 0;
            var stages = Pair.Server.System<KsProcgenOwnedStageSystem>();
            void OnAdded(Entity<MetaDataComponent> entity) => allocated++;
            try
            {
                SEntMan.EntityAdded += OnAdded;
                var result = stages.TryGenerate(Request(assembly: assembly), "Captured", planningBudgets: Budgets(),
                    content: Catalog(assembly), stageBudgets: new() { MaximumPrototypeImageCharacters = 0 });
                Assert.That(result.Status, Is.EqualTo(KsProcgenOwnedStageStatus.BudgetExceeded)); Assert.That(result.Issue?.Code, Is.EqualTo("OwnedStagePrototypeImageBudget"));
                Assert.That(allocated, Is.Zero); Assert.That(result.Stage, Is.Null);
            }
            finally { SEntMan.EntityAdded -= OnAdded; stages.DiscardAll(); }
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    [Test]
    public async Task FloorOnlyTinyScenesDoNotConsumePrototypeImages()
    {
        await AssertOnServer(() =>
        {
            var stages = Pair.Server.System<KsProcgenOwnedStageSystem>();
            try
            {
                var request = Request(); request.Shape.Cells = new() { new(0, 0), new(1, 0), new(2, 0) };
                var result = stages.TryGenerate(request, "KsProcgenNoDoorThemeFixture", planningBudgets: Budgets(), stageBudgets: new() { MaximumPrototypeImageCharacters = 0 });
                Assert.That(result.Status, Is.EqualTo(KsProcgenOwnedStageStatus.PositionedPreview), message: result.Issue?.Code);
                Assert.That(result.Stage!.Entities, Is.Empty); Assert.That(result.Stage.Assemblies, Is.Empty);
            }
            finally { stages.DiscardAll(); }
        });
    }

    [TestCase("category")] [TestCase("network")]
    public async Task ResolvedCategoryAndNetworkDefaultsRemainPinned(string kind)
    {
        await AssertOnServer(() =>
        {
            var system = Pair.Server.System<KsProcgenPrototypeManifestSystem>(); var manifest = Capture(Probe);
            var prototype = SProtoMan.Index(new EntProtoId(Probe)); var category = prototype.Categories.First(); var name = category.Name;
            var probe = prototype.Components["Appearance"].Component; var networked = probe.NetSyncEnabled;
            try
            {
                if (kind == "category") category.Name = "captured-category-drift"; else probe.NetSyncEnabled = !networked;
                var changed = Capture(Probe);
                if (kind == "category") Assert.That(changed.Bodies.Single(entry => entry.PrototypeId == Probe).CanonicalBody, Is.EqualTo(manifest.Bodies.Single(entry => entry.PrototypeId == Probe).CanonicalBody));
                else Assert.That(changed.Bodies.Single(entry => entry.PrototypeId == Probe).CanonicalBody, Is.Not.EqualTo(manifest.Bodies.Single(entry => entry.PrototypeId == Probe).CanonicalBody));
                Assert.That(changed.Bodies.Single(entry => entry.PrototypeId == Probe).CanonicalMetadata, Is.Not.EqualTo(manifest.Bodies.Single(entry => entry.PrototypeId == Probe).CanonicalMetadata));
                Assert.That(changed.ContentHash, Is.Not.EqualTo(manifest.ContentHash)); Assert.That(system.Matches(manifest, out var issue), Is.False);
                Assert.That(issue?.Code, Is.EqualTo("PrototypeManifestChanged"));
                Assert.That(system.TryCreatePrototypeSnapshot(manifest, Probe, out var snapshot, out issue), Is.True, message: issue?.Code);
                var capturedCategory = snapshot!.Categories.Single(copy => copy.ID == category.ID);
                Assert.That(capturedCategory.Name, Is.EqualTo(name)); Assert.That(capturedCategory, Is.Not.SameAs(category));
            }
            finally { category.Name = name; probe.NetSyncEnabled = networked; }
            Assert.That(system.Matches(manifest, out var restored), Is.True, message: restored?.Code);
        });
    }

    [Test]
    public async Task ResolvedCategoryAllowanceIsEnforcedBeforeAnyAllocation()
    {
        await AssertOnServer(() =>
        {
            var before = SEntMan.GetEntities().ToHashSet();
            Assert.That(Pair.Server.System<KsProcgenPrototypeManifestSystem>().TryCapture(new[] { Probe }, [], out var manifest,
                out var issue, limits: new() { MaximumCategoriesPerEntity = 0 }), Is.False);
            Assert.That(manifest, Is.Null); Assert.That(issue?.Code, Is.EqualTo("PrototypeManifestCategoryBudget"));
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    [TestCase("metadata")] [TestCase("identity")] [TestCase("world-reference")]
    public async Task InvalidCapturedImagesNeverAllocateEntities(string kind)
    {
        await AssertOnServer(() =>
        {
            var system = Pair.Server.System<KsProcgenPrototypeManifestSystem>(); var manifest = Capture(Probe); var body = manifest.Bodies.Single(entry => entry.PrototypeId == Probe);
            Assert.That(KsProcgenPrototypeManifestSystem.TryDecodeCanonicalBody(body.CanonicalBody, manifest.Limits, out var node, out _), Is.True);
            if (kind == "identity") ((MappingDataNode) node!)["id"] = new ValueDataNode("Other");
            if (kind == "world-reference")
                ((MappingDataNode) node!).Get<SequenceDataNode>("components").Cast<MappingDataNode>()
                    .Single(component => component.Get<ValueDataNode>("type").Value == "KsProcgenManifestReferenceProbe")["targetUid"] = new ValueDataNode("42");
            Assert.That(KsProcgenPrototypeManifestSystem.TryCanonicalBody(node!, manifest.Limits, out var canonical, out _, out _, out _), Is.True);
            var invalid = new KsProcgenPrototypeManifest(new[] { body with { CanonicalBody = canonical!, CanonicalMetadata = kind == "metadata" ? string.Empty : body.CanonicalMetadata } },
                manifest.Limits, manifest.Nodes, manifest.Characters, manifest.ReferenceOccurrences,
                roots: manifest.Roots, entityReferenceSeeds: manifest.EntityReferenceSeeds,
                dependencyRoots: manifest.DependencyRoots, dependencies: manifest.Dependencies,
                abstractTemplates: manifest.AbstractTemplates, typedReferenceSeeds: manifest.TypedReferenceSeeds);
            var before = SEntMan.GetEntities().ToHashSet();
            Assert.That(system.TryCreatePrototypeSnapshot(invalid, Probe, out var snapshot, out var issue), Is.False);
            Assert.That(snapshot, Is.Null); Assert.That(issue, Is.Not.Null); Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    private KsProcgenPrototypeManifest Capture(string id)
    {
        Assert.That(Pair.Server.System<KsProcgenPrototypeManifestSystem>().TryCapture(new[] { id }, [], out var manifest, out var issue), Is.True, message: issue?.Code);
        return manifest!;
    }

    private KsProcgenThemeCatalog Catalog(bool assembly)
    {
        var spec = new KsProcgenContentDraftSpec
        {
            DocumentId = "Captured",
            Themes = new() { ["Captured"] = new() { Parent = "KsProcgenNoDoorThemeFixture", Goals = new() { FurnishingDensity = 0f },
                EntityPacks = assembly ? new() { new() { Pack = "Captured" } } : new(),
                WallPacks = assembly ? null : new() { new() { Pack = "CapturedWall" } } } },
            Walls = new() { ["CapturedWall"] = new() { Families = new() { new() { Id = "Captured", InteriorWall = Wall, HullWall = Wall } } } },
            Entities = new() { ["Captured"] = new() {
                Assemblies = assembly ? new() { new() { Id = "Core", Assembly = "Core", MinimumCount = 1 } } : new() } },
            Assemblies = new() { ["Core"] = new() { AnchorMember = "Root", Members = new()
                { new() { Id = "Root", Entity = "ComputerShuttle", AllowedQuarterTurns = new() { 0 } },
                  new() { Id = "Stored", Entity = Probe, AllowedQuarterTurns = new() { 0 } } },
                Relations = new() { new() { Id = "Stored", Subject = "Stored", Target = "Root", Kind = KsProcgenRelationKind.InContainer, ContainerId = "disk_slot" } } } },
        };
        Assert.That(KsProcgenContentDraft.TrySnapshot(spec, out var draft, out var issue), Is.True, message: issue?.Code);
        Assert.That(KsProcgenThemeCatalog.TryCapture(SProtoMan, "Captured", out var catalog, out issue, assets: draft), Is.True, message: issue?.Code);
        return catalog!;
    }

    private static KsProcgenRequest Request(bool assembly = true) => new()
    { RequestId = "Captured", Seed = 17, ConnectivityPolicy = KsProcgenConnectivityPolicy.PerIsland,
        Shape = new() { Cells = Enumerable.Range(0, 7).SelectMany(x => Enumerable.Range(0, 7).Select(y => new Vector2i(x, y))).ToList(),
            EnvelopeCells = assembly ? new() : new() { new(-1, 0) } } };
    private static KsProcgenOwnedPlanBudgets Budgets() => new() { MinimumRoomCells = 1, PreferredMaximumRoomCells = 64 };
}
