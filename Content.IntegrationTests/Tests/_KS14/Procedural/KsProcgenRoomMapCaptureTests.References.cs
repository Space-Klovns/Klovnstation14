#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using Content.Server._KS14.Procedural;
using Content.Shared.Materials;
using Content.Shared.Maps;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager;
using Robust.Shared.Serialization.Manager.Attributes;
using Robust.Shared.Serialization.Markdown.Validation;
using Robust.Shared.Serialization.Markdown.Mapping;
using Robust.Shared.Serialization.Markdown.Value;
using Robust.Shared.Serialization.TypeSerializers.Interfaces;

namespace Content.IntegrationTests.Tests._KS14.Procedural;

[RegisterComponent]
public sealed partial class KsProcgenMapReferenceProbeComponent : Component
{
    [DataField] public EntityUid Target;
    [DataField] public NetEntity NetworkTarget;
    [DataField] public MapId MapTarget;
    [DataField] public Dictionary<EntityUid, List<NetEntity>> Nested = new();
    [DataField] public string NumberText = "";
    [DataField] public int Count;
    [DataField] public EntProtoId? EntityPrototype;
    [DataField] public EntProtoId<TransformComponent>? RequiredPrototype;
    [DataField] public ProtoId<EntityPrototype>? TypedPrototype;
    [DataField] public List<EntProtoId> PrototypeList = new();
    [DataField] public ProtoId<MaterialPrototype>? MaterialPrototype;
    [DataField] public ProtoId<EntityCategoryPrototype>? CategoryPrototype;
    [DataField] public ProtoId<ContentTileDefinition>? TilePrototype;
    [DataField] public List<ProtoId<MaterialPrototype>> MaterialPrototypes = new();
    [DataField] public Dictionary<ProtoId<MaterialPrototype>, ProtoId<EntityCategoryPrototype>> KindReferences = new();
    [DataField(customTypeSerializer: typeof(KsProcgenMapReferenceTriggerValidator))] public string Trigger = "";
}

public sealed partial class KsProcgenMapReferenceTestSystem : EntitySystem
{
    public Action<string>? ValidationAction;
}

public sealed class KsProcgenMapReferenceTriggerValidator : ITypeValidator<string, ValueDataNode>
{
    public ValidationNode Validate(ISerializationManager serializationManager, ValueDataNode node,
        IDependencyCollection dependencies, ISerializationContext? context = null)
    {
        dependencies.Resolve<IEntitySystemManager>().GetEntitySystem<KsProcgenMapReferenceTestSystem>().ValidationAction?.Invoke(node.Value);
        return new ValidatedValueNode(node);
    }
}

public sealed partial class KsProcgenRoomMapCaptureTests
{
    private static string ReferencedRows(string fields, string targetPosition = "1.75,0.5") =>
        "- proto: Paper\n  entities:\n  - uid: 10\n    components:\n    - type: Transform\n      parent: 1\n      pos: 1.25,0.25\n    - type: KsProcgenMapReferenceProbe\n" + fields +
        "  - uid: 11\n    components:\n    - type: Transform\n      parent: 1\n      pos: " + targetPosition + "\n";

    [TestCase("reference")] [TestCase("opacity")] [TestCase("pose")] [TestCase("mask")]
    public async Task ExtractionIdentityBindsReturnedWitnessesIndependentlyOfItsInputHashes(string change)
    {
        await AssertOnServer(() => WithSource(GridYaml() + ReferencedRows("      target: 11\n"), (system, project, bindings, root) =>
        {
            var spec = project.CopySpec(); spec.Layouts!.Rooms["Room"].Cells = new() { new(1, 0) }; project = Project(spec);
            var source = Capture(system, project, bindings);
            Assert.That(system.TryExtractRoomEntities(project, source, out var original, out var issue), Is.True, message: issue?.ToString());
            var room = original!.Rooms.Single(); var owner = room.Entities.Single(entity => entity.YamlUid == 10);
            var references = owner.References.Select(reference => change == "reference" && reference.ComponentType == "KsProcgenMapReferenceProbe"
                ? reference with { TargetYamlUid = 10 } : reference);
            var modified = new KsProcgenExtractedMapEntity(owner.YamlUid, owner.PrototypeId, owner.ParentYamlUid,
                owner.LocalPosition, owner.LocalRotationRadians,
                change == "pose" ? owner.GridPosition + Vector2.UnitX : owner.GridPosition,
                owner.RoomPosition, owner.GridRotationRadians, owner.Anchored, owner.NoLocalRotation,
                owner.OverrideComponentTypes, owner.RemovedComponentTypes, references,
                change == "opacity" ? new[] { "Sprite" } : owner.UninspectedComponentTypes);
            var mask = change == "mask" ? new KsProcgenRoomMapNativeMask(room.Room.RoomAssetId, room.Room.ResourcePath,
                room.Room.GridYamlUid, room.Room.SourceOrigin, room.Room.Cells.Append(new Vector2i(1, 1))) : room.Room;
            var changedRoom = new KsProcgenExtractedRoomEntities(mask, room.Entities.Select(entity => entity.YamlUid == owner.YamlUid ? modified : entity));
            var changed = new KsProcgenRoomEntityExtraction(original.SourceContentHash, original.Definitions,
                new[] { changedRoom }, original.MaskCellProbes, original.GraphWork);
            Assert.That(changed.SourceContentHash, Is.EqualTo(original.SourceContentHash));
            Assert.That(changed.Definitions.ContentHash, Is.EqualTo(original.Definitions.ContentHash));
            Assert.That(changed.ContentHash, Is.Not.EqualTo(original.ContentHash));
            Assert.That(owner.UninspectedComponentTypes, Is.Empty);
            Assert.That(owner.References, Does.Contain(new KsProcgenMapEntityReference("KsProcgenMapReferenceProbe", KsProcgenMapReferenceKind.Entity, 11)));
            var culture = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
                var replay = new KsProcgenRoomEntityExtraction(original.SourceContentHash, original.Definitions,
                    original.Rooms, original.MaskCellProbes, original.GraphWork);
                Assert.That(replay.ContentHash, Is.EqualTo(original.ContentHash));
            }
            finally { CultureInfo.CurrentCulture = culture; }
        }));
    }

    [Test]
    public async Task RepeatedSourceReferencesConsumeBothInspectionAndClosureWorkBeforeOutputDeduplication()
    {
        await AssertOnServer(() =>
        {
            KsProcgenRoomEntityExtraction? few = null;
            foreach (var count in new[] { 2, 20 })
            WithSource(GridYaml() + ReferencedRows("      nested: {10: [" + string.Join(",", Enumerable.Repeat("11", count)) + "]}\n"), (system, project, bindings, root) =>
            {
                var spec = project.CopySpec(); spec.Layouts!.Rooms["Room"].Cells = new() { new(1, 0) }; project = Project(spec);
                var source = Capture(system, project, bindings);
                Assert.That(system.TryExtractRoomEntities(project, source, out var extraction, out var issue), Is.True, message: issue?.ToString());
                if (count == 2) { few = extraction; return; }
                Assert.That(extraction!.GraphWork, Is.EqualTo(few!.GraphWork + 36));
                Assert.That(extraction.Rooms.Single().Entities.First().References, Is.EqualTo(few.Rooms.Single().Entities.First().References));
                Assert.That(system.TryExtractRoomEntities(project, source, out var rejected, out issue,
                    limits: new() { MaximumGraphWork = extraction.GraphWork - 1 }), Is.False);
                Assert.That(rejected, Is.Null); Assert.That(issue?.Code, Is.EqualTo("RoomEntityExtractionGraphBudget"));
            });
        });
    }

    [Test]
    public async Task OverrideAbstractFiltersRemainReferencesAndNeverBecomeSpawnableRoots()
    {
        await AssertOnServer(() => WithSource(GridYaml() + ReferencedRows("      entityPrototype: BaseItem\n"), (system, project, bindings, root) =>
        {
            var spec = project.CopySpec(); spec.Layouts!.Rooms["Room"].Cells = new() { new(1, 0) }; project = Project(spec);
            var source = Capture(system, project, bindings);
            Assert.That(system.TryExtractRoomEntities(project, source, out var extraction, out var issue), Is.True, message: issue?.ToString());
            Assert.That(extraction!.Definitions.EntityReferenceSeeds, Does.Contain("BaseItem"));
            Assert.That(extraction.Definitions.Roots.Any(root => root.PrototypeId == "BaseItem"), Is.False);
            Assert.That(extraction.Definitions.Bodies.Single(body => body.PrototypeId == "BaseItem").Kind, Is.EqualTo(KsProcgenPrototypeBodyKind.EntityDeclaration));
            var manifests = Pair.Server.System<KsProcgenPrototypeManifestSystem>();
            Assert.That(manifests.Matches(extraction.Definitions, out issue), Is.True, message: issue?.ToString());
            Assert.That(manifests.TryCreatePrototypeSnapshot(extraction.Definitions, "BaseItem", out _, out issue), Is.False);
        }));
    }

    [Test]
    public async Task TypedSourceReferencesIncludeNestedKeysAndValuesWithoutTreatingNumbersAsEntityIds()
    {
        await AssertOnServer(() => WithSource(GridYaml() + ReferencedRows("      target: 11\n      nested: {11: [10, 11]}\n      count: 99999\n      numberText: '99999'\n"), (system, project, bindings, root) =>
        {
            var spec = project.CopySpec(); spec.Layouts!.Rooms["Room"].Cells = new() { new(1, 0) }; project = Project(spec);
            var source = Capture(system, project, bindings); root.Available = false;
            var before = SEntMan.GetEntities().ToHashSet();
            Assert.That(system.TryExtractRoomEntities(project, source, out var extraction, out var issue), Is.True, message: issue?.ToString());
            var owner = extraction!.Rooms.Single().Entities.Single(entity => entity.YamlUid == 10);
            Assert.That(owner.References, Is.EquivalentTo(new[]
            {
                new KsProcgenMapEntityReference("Transform", KsProcgenMapReferenceKind.Entity, 1),
                new KsProcgenMapEntityReference("KsProcgenMapReferenceProbe", KsProcgenMapReferenceKind.Entity, 11),
                new KsProcgenMapEntityReference("KsProcgenMapReferenceProbe", KsProcgenMapReferenceKind.NetworkEntity, 10),
                new KsProcgenMapEntityReference("KsProcgenMapReferenceProbe", KsProcgenMapReferenceKind.NetworkEntity, 11),
            }));
            Assert.That(owner.UninspectedComponentTypes, Is.Empty);
            Assert.That(extraction.SchemaVersion, Is.EqualTo(2)); Assert.That(extraction.ContentHash.Length, Is.EqualTo(64));
            Assert.That(extraction.ComponentReferencesVerified, Is.False); Assert.That(extraction.CopyReady, Is.False);
            Assert.That(system.TryExtractRoomEntities(project, source, out var replay, out issue), Is.True, message: issue?.ToString());
            Assert.That(replay!.ContentHash, Is.EqualTo(extraction.ContentHash));
            Assert.That(system.TryExtractRoomEntities(project, source, out var rejected, out issue,
                limits: new() { MaximumGraphWork = extraction.GraphWork - 1 }), Is.False);
            Assert.That(rejected, Is.Null); Assert.That(issue?.Code, Is.EqualTo("RoomEntityExtractionGraphBudget"));
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        }));
    }

    [TestCase("target: 11")] [TestCase("networkTarget: 11")]
    [TestCase("nested: {11: [10]}")] [TestCase("nested: {10: [11]}")]
    public async Task ExtractionRejectsObservedReferencesAcrossTheSelectedRoomBoundary(string field)
    {
        await AssertOnServer(() => WithSource(GridYaml() + ReferencedRows("      " + field + "\n", targetPosition: "0.25,0.25"), (system, project, bindings, root) =>
        {
            var spec = project.CopySpec(); spec.Layouts!.Rooms["Room"].Cells = new() { new(1, 0) }; project = Project(spec);
            var source = Capture(system, project, bindings); var before = SEntMan.GetEntities().ToHashSet();
            Assert.That(system.TryExtractRoomEntities(project, source, out var result, out var issue), Is.False);
            Assert.That(result, Is.Null); Assert.That(issue?.Code, Is.EqualTo("RoomMapExtractionCutsComponentReference"));
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        }));
    }

    [TestCase("target: 99", "MissingRoomMapComponentReference")]
    [TestCase("target: 0", "InvalidRoomMapComponentReference")]
    [TestCase("networkTarget: -1", "InvalidRoomMapComponentReference")]
    [TestCase("target: nonsense", "InvalidRoomMapComponentReference")]
    [TestCase("target: 2147483648", "InvalidRoomMapComponentReference")]
    public async Task MissingOrMalformedTypedReferencesYieldNoExtraction(string field, string code)
    {
        await AssertOnServer(() => WithSource(GridYaml() + ReferencedRows("      " + field + "\n"), (system, project, bindings, root) =>
        {
            var spec = project.CopySpec(); spec.Layouts!.Rooms["Room"].Cells = new() { new(1, 0) }; project = Project(spec);
            var source = Capture(system, project, bindings);
            Assert.That(system.TryExtractRoomEntities(project, source, out var result, out var issue), Is.False);
            Assert.That(result, Is.Null); Assert.That(issue?.Code, Is.EqualTo(code));
        }));
    }

    [TestCase(50, null)] [TestCase(1, "RoomMapExtractionExternalMapReference")]
    [TestCase(51, "RoomMapExtractionExternalMapReference")] [TestCase(99, "MissingRoomMapComponentReference")]
    public async Task MapReferencesMustNameTheSelectedGridsOwningMap(int target, string? code)
    {
        var yaml = GridYaml().Replace("    - type: MapGrid", "    - type: Transform\n      parent: 50\n    - type: MapGrid") +
            ReferencedRows("      mapTarget: " + target + "\n") +
            "- proto: ''\n  entities:\n  - uid: 50\n    components:\n    - type: Map\n  - uid: 51\n    components:\n    - type: Map\n";
        await AssertOnServer(() => WithSource(yaml, (system, project, bindings, root) =>
        {
            var spec = project.CopySpec(); spec.Layouts!.Rooms["Room"].Cells = new() { new(1, 0) }; project = Project(spec);
            var source = Capture(system, project, bindings);
            Assert.That(system.TryExtractRoomEntities(project, source, out var result, out var issue), Is.EqualTo(code == null), message: issue?.ToString());
            if (code != null) { Assert.That(result, Is.Null); Assert.That(issue?.Code, Is.EqualTo(code)); return; }
            Assert.That(result!.Rooms.Single().Entities.Single(entity => entity.YamlUid == 10).References,
                Does.Contain(new KsProcgenMapEntityReference("KsProcgenMapReferenceProbe", KsProcgenMapReferenceKind.Map, 50)));
        }));
    }

    [TestCase("entityPrototype: CoordinatesDisk")]
    [TestCase("requiredPrototype: CoordinatesDisk")]
    [TestCase("typedPrototype: CoordinatesDisk")]
    [TestCase("prototypeList: [CoordinatesDisk, CoordinatesDisk]")]
    public async Task OverrideEntityPrototypeReferencesAreFrozenAndChangeExtractionIdentityOnDrift(string field)
    {
        await AssertOnServer(() => WithSource(GridYaml() + ReferencedRows("      " + field + "\n"), (system, project, bindings, root) =>
        {
            var spec = project.CopySpec(); spec.Layouts!.Rooms["Room"].Cells = new() { new(1, 0) }; project = Project(spec);
            var source = Capture(system, project, bindings); var manifests = Pair.Server.System<KsProcgenPrototypeManifestSystem>();
            Assert.That(source.Definitions.Roots.Any(entry => entry.PrototypeId == "CoordinatesDisk"), Is.False);
            Assert.That(system.TryExtractRoomEntities(project, source, out var original, out var issue), Is.True, message: issue?.ToString());
            Assert.That(original!.Definitions.Roots.Any(entry => entry.PrototypeId == "CoordinatesDisk"), Is.False);
            Assert.That(original.Definitions.EntityReferenceSeeds, Does.Contain("CoordinatesDisk"));
            var prototype = SProtoMan.Index(new EntProtoId("CoordinatesDisk")); var save = prototype.MapSavable;
            try
            {
                prototype.MapSavable = !save;
                Assert.That(manifests.Matches(source.Definitions, out issue), Is.True, message: issue?.ToString());
                Assert.That(manifests.Matches(original.Definitions, out issue), Is.False);
                Assert.That(system.TryExtractRoomEntities(project, source, out var changed, out issue), Is.True, message: issue?.ToString());
                Assert.That(changed!.SourceContentHash, Is.EqualTo(original.SourceContentHash));
                Assert.That(changed.ContentHash, Is.Not.EqualTo(original.ContentHash));
            }
            finally { prototype.MapSavable = save; }
            Assert.That(manifests.Matches(original.Definitions, out issue), Is.True, message: issue?.ToString());
        }));
    }

    [TestCase(false)] [TestCase(true)]
    public async Task OverridePrototypeCaptureRejectsMissingDefinitionsAndExhaustedSharedAllowances(bool budget)
    {
        var field = budget ? "entityPrototype: CoordinatesDisk" : "entityPrototype: KsProcgenMapReferenceMissing";
        await AssertOnServer(() => WithSource(GridYaml() + ReferencedRows("      " + field + "\n"), (system, project, bindings, root) =>
        {
            var spec = project.CopySpec(); spec.Layouts!.Rooms["Room"].Cells = new() { new(1, 0) }; project = Project(spec);
            var source = Capture(system, project, bindings);
            if (budget)
            {
                Assert.That(system.TryCapture(project, bindings, out var tight, out var captureIssue,
                    limits: new() { Definitions = new() { MaximumDefinitions = source.Definitions.Bodies.Count + source.Definitions.Dependencies.Count } }), Is.True, message: captureIssue?.ToString());
                source = tight!;
            }
            var before = SEntMan.GetEntities().ToHashSet();
            Assert.That(system.TryExtractRoomEntities(project, source, out var result, out var issue), Is.False);
            Assert.That(result, Is.Null); Assert.That(issue?.Code, Is.EqualTo(budget ? "PrototypeManifestDefinitionBudget" : "MissingPrototypeManifestEntity"));
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        }));
    }

    [TestCase("    - type: Sprite\n      sprite: Objects/Misc/paper.rsi\n", "Sprite")]
    [TestCase("      unknownReferenceField: 99999\n", "KsProcgenMapReferenceProbe")]
    public async Task UninspectedClientAndUnknownFieldsStayExplicitlyOpaque(string extra, string opaque)
    {
        await AssertOnServer(() => WithSource(GridYaml() + ReferencedRows("      numberText: '99'\n" + extra), (system, project, bindings, root) =>
        {
            var spec = project.CopySpec(); spec.Layouts!.Rooms["Room"].Cells = new() { new(1, 0) }; project = Project(spec);
            var source = Capture(system, project, bindings);
            Assert.That(system.TryExtractRoomEntities(project, source, out var extraction, out var issue), Is.True, message: issue?.ToString());
            Assert.That(extraction!.Rooms.Single().Entities.Single(entity => entity.YamlUid == 10).UninspectedComponentTypes, Does.Contain(opaque));
            Assert.That(extraction.ComponentReferencesVerified, Is.False); Assert.That(extraction.CopyReady, Is.False);
        }));
    }

    [Test]
    public async Task SourceTransformDefaultsComeFromTheCapturedImageDuringTransientLiveEdits()
    {
        const string beforeRow = "- proto: ''\n  entities:\n  - uid: 20\n    components:\n    - type: Transform\n      parent: 1\n      pos: 1.25,0.25\n    - type: KsProcgenMapReferenceProbe\n      trigger: change\n";
        const string tableRow = "- proto: Table\n  entities:\n  - uid: 10\n    components:\n    - type: Transform\n      parent: 1\n      pos: 1.25,0.25\n      rot: 90\n";
        var afterRow = beforeRow.Replace("uid: 20", "uid: 21").Replace("trigger: change", "trigger: restore");
        await AssertOnServer(() => WithSource(GridYaml() + beforeRow + tableRow + afterRow, (system, project, bindings, root) =>
        {
            var spec = project.CopySpec(); spec.Layouts!.Rooms["Room"].Cells = new() { new(1, 0) }; project = Project(spec);
            var source = Capture(system, project, bindings); var before = SEntMan.GetEntities().ToHashSet();
            var prototype = SProtoMan.Index(new EntProtoId("Table")); var original = prototype.Components["Transform"];
            var serialization = Pair.Server.ResolveDependency<ISerializationManager>();
            var replacement = serialization.Read<TransformComponent>(new MappingDataNode { { "noRot", new ValueDataNode("true") } }, skipHook: true, notNullableOverride: true);
            var hook = Pair.Server.System<KsProcgenMapReferenceTestSystem>(); var callbacks = 0;
            try
            {
                hook.ValidationAction = value =>
                {
                    if (value == "change") { callbacks++; prototype.Components["Transform"] = new EntityPrototype.ComponentRegistryEntry(replacement); }
                    if (value == "restore") { callbacks++; prototype.Components["Transform"] = original; }
                };
                Assert.That(system.TryExtractRoomEntities(project, source, out var extraction, out var issue), Is.True, message: issue?.ToString());
                Assert.That(callbacks, Is.EqualTo(2));
                Assert.That(extraction!.Rooms.Single().Entities.Single(entity => entity.YamlUid == 10).NoLocalRotation, Is.False);
                Assert.That(prototype.Components["Transform"].Component, Is.SameAs(original.Component));
            }
            finally { hook.ValidationAction = null; prototype.Components["Transform"] = original; }
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        }));
    }
}
