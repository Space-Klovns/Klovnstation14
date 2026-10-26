#nullable enable
using System;
using System.Globalization;
using System.Linq;
using Content.Server._KS14.Procedural;
using Content.Shared._KS14.Procedural;
using Content.Shared.Maps;
using Content.Shared.Placeable;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Serialization.Manager.Attributes;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.Procedural;

[RegisterComponent]
public sealed partial class KsProcgenManifestReferenceProbeComponent : Component
{
    [DataField] public EntityUid TargetUid;
    [DataField] public NetEntity TargetNetEntity;
    [DataField] public MapId TargetMapId;
}

[TestOf(typeof(KsProcgenPrototypeManifestSystem))]
public sealed partial class KsProcgenPrototypeManifestTests : KsProcgenGameTest
{
    [Test]
    public async Task ResolvedBodiesAreDetachedDeterministicAndDoNotSpawnEntities()
    {
        await AssertOnServer(() =>
        {
            var system = Pair.Server.System<KsProcgenPrototypeManifestSystem>();
            var before = SEntMan.GetEntities().ToHashSet();
            Assert.That(system.TryCapture(new[] { "KitchenMicrowave", "Table", "Table" }, new[] { "FloorSteel", "Space" },
                out var first, out var issue), Is.True, message: issue?.ToString());
            Assert.That(first!.Roots.Count, Is.EqualTo(4));
            Assert.That(first.Bodies.Count, Is.GreaterThanOrEqualTo(first.Roots.Count));
            Assert.That(first.ContentHash.Length, Is.EqualTo(64));
            Assert.That(first.Bodies.Where(body => body.Kind is KsProcgenPrototypeBodyKind.Entity or KsProcgenPrototypeBodyKind.Tile)
                .All(body => body.CanonicalBody.Length > 0), Is.True);
            var culture = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
                Assert.That(system.TryCapture(new[] { "Table", "KitchenMicrowave" }, new[] { "Space", "FloorSteel" },
                    out var replay, out issue), Is.True, message: issue?.Code);
                Assert.That(replay!.ContentHash, Is.EqualTo(first.ContentHash));
                Assert.That(replay.Bodies, Is.EqualTo(first.Bodies));
            }
            finally { CultureInfo.CurrentCulture = culture; }
            Assert.That(system.Matches(first, out issue), Is.True, message: issue?.Code);
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    [TestCase("component")] [TestCase("tile")]
    public async Task MutableResolvedComponentAndTileFieldsInvalidateCapturedBodies(string kind)
    {
        await AssertOnServer(() =>
        {
            var system = Pair.Server.System<KsProcgenPrototypeManifestSystem>();
            var manager = Pair.Server.ResolveDependency<IPrototypeManager>();
            Assert.That(system.TryCapture(new[] { "KsProcgenTaggedSurfaceFixture" }, new[] { "FloorSteel" }, out var manifest, out var issue), Is.True, message: issue?.ToString());
            var originalHash = manifest!.ContentHash;
            var originalBody = manifest.Bodies.Single(body => body.PrototypeId == "KsProcgenTaggedSurfaceFixture").CanonicalBody;
            var prototype = manager.Index(new EntProtoId("KsProcgenTaggedSurfaceFixture"));
            Assert.That(prototype.TryComp<ItemPlacerComponent>(out var placer, SEntMan.ComponentFactory), Is.True);
            var whitelist = placer!.Whitelist!;
            var requireAll = whitelist.RequireAll;
            var tile = manager.Index(new ProtoId<ContentTileDefinition>("FloorSteel")); var mass = tile.Mass;
            try
            {
                if (kind == "component") whitelist.RequireAll = !requireAll;
                else tile.Mass += 1f;
                Assert.That(system.Matches(manifest, out issue), Is.False);
                Assert.That(issue?.Code, Is.EqualTo("PrototypeManifestChanged"));
                Assert.That(manifest.ContentHash, Is.EqualTo(originalHash));
                Assert.That(manifest.Bodies.Single(body => body.PrototypeId == "KsProcgenTaggedSurfaceFixture").CanonicalBody, Is.EqualTo(originalBody));
            }
            finally { whitelist.RequireAll = requireAll; tile.Mass = mass; }
            Assert.That(system.Matches(manifest, out issue), Is.True, message: issue?.Code);
        });
    }

    [TestCase("definitions")] [TestCase("nodes")] [TestCase("characters")] [TestCase("depth")] [TestCase("components")]
    public async Task ExhaustedCaptureBudgetsReturnNoPartialManifest(string budget)
    {
        await AssertOnServer(() =>
        {
            var system = Pair.Server.System<KsProcgenPrototypeManifestSystem>();
            var limits = new KsProcgenPrototypeManifestLimits();
            limits = budget switch
            {
                "definitions" => limits with { MaximumDefinitions = 1 },
                "nodes" => limits with { MaximumNodes = 1 },
                "characters" => limits with { MaximumCharacters = 1 },
                "depth" => limits with { MaximumDepth = 1 },
                _ => limits with { MaximumComponentsPerEntity = 1 },
            };
            var before = SEntMan.GetEntities().ToHashSet();
            Assert.That(system.TryCapture(new[] { "Table", "Paper" }, new[] { "FloorSteel" }, out var manifest, out var issue, limits: limits), Is.False);
            Assert.That(manifest, Is.Null); Assert.That(issue!.Code, Does.EndWith("Budget"));
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    [Test]
    public async Task DuplicateReferencesConsumeAnOccurrenceBudget()
    {
        await AssertOnServer(() =>
        {
            var system = Pair.Server.System<KsProcgenPrototypeManifestSystem>();
            Assert.That(system.TryCapture(Enumerable.Repeat("Table", 3), [], out var manifest, out var issue,
                limits: new() { MaximumDefinitions = 1, MaximumReferences = 2 }), Is.False);
            Assert.That(manifest, Is.Null); Assert.That(issue?.Code, Is.EqualTo("PrototypeManifestReferenceBudget"));
        });
    }

    [Test]
    public async Task ComponentRegistryInsertionOrderDoesNotChangeBodyIdentity()
    {
        await AssertOnServer(() =>
        {
            var system = Pair.Server.System<KsProcgenPrototypeManifestSystem>();
            var prototype = Pair.Server.ResolveDependency<IPrototypeManager>().Index(new EntProtoId("Table"));
            var components = prototype.Components.ToArray();
            Assert.That(components.Length, Is.GreaterThan(1));
            Assert.That(system.TryCapture(new[] { "Table" }, [], out var original, out var issue), Is.True, message: issue?.ToString());
            try
            {
                prototype.Components.Clear();
                foreach (var (name, component) in components.Reverse()) prototype.Components.Add(name, component);
                Assert.That(system.Matches(original!, out issue), Is.True, message: issue?.ToString());
            }
            finally
            {
                prototype.Components.Clear();
                foreach (var (name, component) in components) prototype.Components.Add(name, component);
            }
        });
    }

    [TestCase("entity")] [TestCase("network")] [TestCase("map")]
    public async Task LiveWorldReferencesCannotBeSmuggledIntoPrototypeBodies(string kind)
    {
        await AssertOnServer(() =>
        {
            var system = Pair.Server.System<KsProcgenPrototypeManifestSystem>(); var maps = Pair.Server.System<SharedMapSystem>();
            var prototype = Pair.Server.ResolveDependency<IPrototypeManager>().Index(new EntProtoId("KsProcgenRoomEmptyFixture"));
            var mapUid = maps.CreateMap(out var mapId, runMapInit: false);
            var probe = new KsProcgenManifestReferenceProbeComponent();
            if (kind == "entity") probe.TargetUid = mapUid;
            if (kind == "network") probe.TargetNetEntity = SEntMan.GetNetEntity(mapUid);
            if (kind == "map") probe.TargetMapId = mapId;
            try
            {
                prototype.Components.Add("KsProcgenManifestReferenceProbe", new EntityPrototype.ComponentRegistryEntry(probe));
                var before = SEntMan.GetEntities().ToHashSet();
                Assert.That(system.TryCapture(new[] { prototype.ID }, [], out var manifest, out var issue), Is.False);
                Assert.That(manifest, Is.Null); Assert.That(issue?.Code, Is.EqualTo("UnsupportedPrototypeManifestBody"));
                Assert.That(issue!.Message, Does.Contain("live"));
                Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
            }
            finally { prototype.Components.Remove("KsProcgenManifestReferenceProbe"); maps.DeleteMap(mapId); }
        });
    }

    [Test]
    public async Task UnselectedVariantBodiesRemainPartOfOwnedStageIdentity()
    {
        await AssertOnServer(() =>
        {
            var stages = Pair.Server.System<KsProcgenOwnedStageSystem>(); var manager = Pair.Server.ResolveDependency<IPrototypeManager>();
            var prototype = manager.Index(new EntProtoId("KsProcgenRoomEmptyFixture")); var save = prototype.MapSavable;
            var before = SEntMan.GetEntities().ToHashSet();
            try
            {
                var result = stages.TryGenerate(Request(), "Manifest", planningBudgets: Budgets(), content: Catalog());
                Assert.That(result.Status, Is.EqualTo(KsProcgenOwnedStageStatus.PositionedPreview), message: result.Issue?.Code);
                var stage = result.Stage!;
                Assert.That(stage.Entities, Is.Empty); Assert.That(stage.Assemblies, Is.Empty);
                Assert.That(stage.Prototypes.Bodies.Any(body => body.PrototypeId == prototype.ID), Is.True);
                prototype.MapSavable = !save;
                Assert.That(stages.Verify(stage), Is.False);
                var initialized = stages.TryInitialize(stage);
                Assert.That(initialized.Status, Is.EqualTo(KsProcgenOwnedStageStatus.Rejected));
                Assert.That(initialized.Issue?.Code, Is.EqualTo("OwnedStagePrototypeDrift"));
                Assert.That(stage.Active, Is.False);
            }
            finally { prototype.MapSavable = save; stages.DiscardAll(); }
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    [TestCase(false)] [TestCase(true)]
    public async Task TileAndInitializationCallbacksCannotRetainChangedPrototypeBodies(bool initialization)
    {
        await AssertOnServer(() =>
        {
            var stages = Pair.Server.System<KsProcgenOwnedStageSystem>(); var tileHook = Pair.Server.System<KsProcgenTileAllocationTestSystem>();
            var lifecycleHook = Pair.Server.System<KsProcgenLifecycleProbeSystem>(); var manager = Pair.Server.ResolveDependency<IPrototypeManager>();
            var prototype = manager.Index(new EntProtoId("WallSolid")); var save = prototype.MapSavable;
            var before = SEntMan.GetEntities().ToHashSet(); var callbackCount = 0;
            try
            {
                void Change() { callbackCount++; prototype.MapSavable = !save; }
                if (!initialization) tileHook.TileAction = () => { tileHook.TileAction = null; Change(); };
                var result = stages.TryGenerate(Request(), "KsProcgenNoDoorThemeFixture", planningBudgets: Budgets());
                if (initialization)
                {
                    Assert.That(result.Status, Is.EqualTo(KsProcgenOwnedStageStatus.PositionedPreview), message: result.Issue?.Code);
                    SEntMan.AddComponent<KsProcgenLifecycleProbeComponent>(result.Stage!.GridUid);
                    lifecycleHook.OnInitialize = Change;
                    result = stages.TryInitialize(result.Stage);
                }
                Assert.That(callbackCount, Is.EqualTo(1));
                Assert.That(result.Status, Is.EqualTo(KsProcgenOwnedStageStatus.Rejected), message: result.Issue?.Code);
                Assert.That(result.Stage, Is.Null);
                Assert.That(stages.RetainedStageCount, Is.Zero);
                Assert.That(Pair.Server.System<KsProcgenPreviewOperationSystem>().Active, Is.False);
            }
            finally { tileHook.TileAction = null; lifecycleHook.OnInitialize = null; prototype.MapSavable = save; stages.DiscardAll(); }
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    [Test]
    public async Task SharedAssemblyPlacementRejectsPrototypeChangesDuringStartupAndCleansEscapedAllocations()
    {
        await AssertOnServer(() =>
        {
            var stages = Pair.Server.System<KsProcgenOwnedStageSystem>(); var hook = Pair.Server.System<KsProcgenInsertionTestSystem>();
            var manager = Pair.Server.ResolveDependency<IPrototypeManager>(); var prototype = manager.Index(new EntProtoId("CoordinatesDisk"));
            var save = prototype.MapSavable; var before = SEntMan.GetEntities().ToHashSet(); var callbacks = 0;
            try
            {
                hook.TargetPrototypeId = prototype.ID; hook.SpawnExtraOnStartup = 1;
                hook.StartupAction = () => { callbacks++; prototype.MapSavable = !save; };
                var request = Request(); request.Shape.Cells = Enumerable.Range(0, 7).SelectMany(x => Enumerable.Range(0, 7).Select(y => new Robust.Shared.Maths.Vector2i(x, y))).ToList();
                var result = stages.TryGenerate(request, "Manifest", planningBudgets: Budgets(), content: Catalog(contained: true));
                Assert.That(callbacks, Is.EqualTo(1));
                Assert.That(result.Status, Is.EqualTo(KsProcgenOwnedStageStatus.Rejected), message: result.Issue?.Code);
                Assert.That(hook.ItemSlotAttempts, Is.Zero, message: "Stop before placing/inserting dependent members.");
                Assert.That(hook.ContainerAttempts, Is.Zero);
                Assert.That(result.Issue?.Code, Is.EqualTo("PrototypeManifestChanged"));
                Assert.That(hook.ExtraSpawnedEntities.Count, Is.EqualTo(1));
                Assert.That(hook.ExtraSpawnedEntities.Any(SEntMan.EntityExists), Is.False);
                Assert.That(result.Stage, Is.Null);
            }
            finally { hook.Reset(); prototype.MapSavable = save; stages.DiscardAll(); }
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    [Test]
    public async Task OwnedCaptureBudgetFailsBeforeMapOrTileAllocation()
    {
        await AssertOnServer(() =>
        {
            var before = SEntMan.GetEntities().ToHashSet(); var hook = Pair.Server.System<KsProcgenTileAllocationTestSystem>(); var callbacks = 0;
            try
            {
                hook.TileAction = () => callbacks++;
                var result = Pair.Server.System<KsProcgenOwnedStageSystem>().TryGenerate(Request(), "KsProcgenNoDoorThemeFixture", planningBudgets: Budgets(),
                    stageBudgets: new() { PrototypeCapture = new() { MaximumDefinitions = 1 } });
                Assert.That(result.Status, Is.EqualTo(KsProcgenOwnedStageStatus.BudgetExceeded));
                Assert.That(result.Issue?.Code, Is.EqualTo("PrototypeManifestDefinitionBudget"));
                Assert.That(result.Stage, Is.Null); Assert.That(callbacks, Is.Zero);
            }
            finally { hook.TileAction = null; }
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        });
    }

    private KsProcgenThemeCatalog Catalog(bool contained = false)
    {
        var spec = new KsProcgenContentDraftSpec
        {
            DocumentId = "Manifest",
            Themes = new() { ["Manifest"] = new() { Parent = "KsProcgenNoDoorThemeFixture",
                Goals = contained ? null : new() { FurnishingDensity = 0f, LightingCoverage = 0f },
                EntityPacks = new() { new() { Pack = "Contents" } } } },
            Entities = new() { ["Contents"] = new() { Assemblies = new() { new() { Id = "Core", Assembly = "Core", MinimumCount = contained ? 1 : 0 } } } },
            Assemblies = new() { ["Core"] = new()
            {
                AnchorMember = "Root", Members = new() { new() { Id = "Root", Entity = contained ? "ComputerShuttle" : "Paper", AllowedQuarterTurns = new() { 0 } } },
                Variants = contained ? new() : new() { new() { Id = "Unused", AnchorMember = "Root", Members = new() { new() { Id = "Root", Entity = "KsProcgenRoomEmptyFixture" } } } },
            } },
        };
        if (contained)
        {
            spec.Assemblies["Core"].Members.Add(new() { Id = "Disk", Entity = "CoordinatesDisk", AllowedQuarterTurns = new() { 0 } });
            spec.Assemblies["Core"].Relations.Add(new() { Id = "Stored", Subject = "Disk", Target = "Root", Kind = KsProcgenRelationKind.InContainer, ContainerId = "disk_slot" });
        }
        Assert.That(KsProcgenContentDraft.TrySnapshot(spec, out var draft, out var issue), Is.True, message: issue?.Code);
        Assert.That(KsProcgenThemeCatalog.TryCapture(Pair.Server.ResolveDependency<IPrototypeManager>(), "Manifest", out var catalog, out issue, assets: draft),
            Is.True, message: issue?.Code);
        return catalog!;
    }
    private static KsProcgenRequest Request() => new()
    {
        RequestId = "Manifest", Shape = new() { Cells = new() { new(0, 0), new(1, 0), new(2, 0) } },
        ConnectivityPolicy = KsProcgenConnectivityPolicy.PerIsland, Seed = 17,
    };
    private static KsProcgenOwnedPlanBudgets Budgets() => new() { MinimumRoomCells = 1, PreferredMaximumRoomCells = 64 };
}
