#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Content.Server._KS14.Procedural;
using Robust.Shared.GameObjects;
using Robust.Shared.Serialization.Manager;
using Robust.Shared.Serialization.Markdown.Mapping;
using Robust.Shared.Serialization.Markdown.Sequence;
using Robust.Shared.Serialization.Markdown.Value;

namespace Content.IntegrationTests.Tests._KS14.Procedural;

public sealed partial class KsProcgenRoomMapCaptureTests
{
    private const string AuthoredOverrides = "    - type: MetaData\n      name: Authored table\n      desc: Not a prototype default\n    - type: PointLight\n      radius: 3.25\n      energy: 0.75\n";

    [Test]
    public async Task ExtractedOverridesRetainAuthoredDataWithoutReopeningOrAliasingTheSource()
    {
        await AssertOnServer(() => WithSource(GridYaml() + EntityBranch.Replace("- proto: Paper", AuthoredOverrides + "- proto: Paper"),
            (system, project, bindings, root) =>
        {
            var spec = project.CopySpec(); spec.Layouts!.Rooms["Room"].Cells = new() { new(1, 0) }; project = Project(spec);
            var source = Capture(system, project, bindings); var before = SEntMan.GetEntities().ToHashSet();
            root.Available = false;
            Assert.That(system.TryExtractRoomEntities(project, source, out var result, out var issue), Is.True, message: issue?.Code);
            var entity = result!.Rooms.Single().Entities.First();
            Assert.That(entity.ComponentOverrides.Select(image => image.ComponentType), Is.EqualTo(entity.OverrideComponentTypes));
            Assert.That(entity.UninspectedComponentTypes, Is.Empty);
            var metadata = DecodeOverride(entity, "MetaData");
            Assert.That(metadata.Get<ValueDataNode>("name").Value, Is.EqualTo("Authored table"));
            Assert.That(metadata.Get<ValueDataNode>("desc").Value, Is.EqualTo("Not a prototype default"));
            var light = DecodeOverride(entity, "PointLight");
            Assert.That(light.Get<ValueDataNode>("radius").Value, Is.EqualTo("3.25"));
            Assert.That(light.Get<ValueDataNode>("energy").Value, Is.EqualTo("0.75"));
            light.Remove("type");
            var serialization = Pair.Server.ResolveDependency<ISerializationManager>();
            var lightComponent = serialization.Read<Robust.Server.GameObjects.PointLightComponent>(light, notNullableOverride: true);
            Assert.That(lightComponent.Radius, Is.EqualTo(3.25f));
            Assert.That(lightComponent.Energy, Is.EqualTo(0.75f));
            metadata.Get<ValueDataNode>("name").Value = "mutated scratch node";
            Assert.That(DecodeOverride(entity, "MetaData").Get<ValueDataNode>("name").Value, Is.EqualTo("Authored table"));
            Assert.Throws<NotSupportedException>(() => ((IList<KsProcgenMapComponentOverride>) entity.ComponentOverrides).Clear());
            Array.Fill(root.Bytes, (byte) 0);
            Assert.That(system.TryExtractRoomEntities(project, source, out var replay, out issue), Is.True, message: issue?.Code);
            Assert.That(replay!.ContentHash, Is.EqualTo(result.ContentHash));
            Assert.That(result.OverrideCharacters, Is.EqualTo(result.Rooms.SelectMany(room => room.Entities)
                .SelectMany(member => member.ComponentOverrides).Sum(image => image.CanonicalBody.Length)));
            Assert.That(result.OverrideNodes, Is.GreaterThan(0));
            Assert.That(result.CopyReady, Is.False); Assert.That(result.ComponentReferencesVerified, Is.False);
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        }));
    }

    [TestCase("characters")] [TestCase("nodes")]
    public async Task SelectedOverrideAllowancesAcceptExactTotalsAndRejectOneLess(string allowance)
    {
        await AssertOnServer(() => WithSource(GridYaml() + EntityBranch, (system, project, bindings, unusedRoot) =>
        {
            var spec = project.CopySpec(); spec.Layouts!.Rooms["Room"].Cells = new() { new(1, 0) }; project = Project(spec);
            var source = Capture(system, project, bindings); var before = SEntMan.GetEntities().ToHashSet();
            Assert.That(system.TryExtractRoomEntities(project, source, out var measured, out var issue), Is.True, message: issue?.Code);
            var exact = new KsProcgenRoomEntityExtractionLimits
            { MaximumOverrideNodes = measured!.OverrideNodes, MaximumOverrideCharacters = measured.OverrideCharacters };
            Assert.That(system.TryExtractRoomEntities(project, source, out var replay, out issue, limits: exact), Is.True, message: issue?.Code);
            Assert.That(replay!.ContentHash, Is.EqualTo(measured.ContentHash));
            var shortLimit = allowance == "characters" ? exact with { MaximumOverrideCharacters = exact.MaximumOverrideCharacters - 1 }
                : exact with { MaximumOverrideNodes = exact.MaximumOverrideNodes - 1 };
            Assert.That(system.TryExtractRoomEntities(project, source, out var rejected, out issue, limits: shortLimit), Is.False);
            Assert.That(rejected, Is.Null); Assert.That(issue?.Code, Is.EqualTo("RoomEntityOverrideImageBudget"));
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        }));
    }

    [Test]
    public async Task OverrideImageBudgetIsSharedAcrossOverlappingRoomSelections()
    {
        await AssertOnServer(() => WithSource(GridYaml() + EntityBranch, (system, project, bindings, root) =>
        {
            var spec = project.CopySpec(); spec.Layouts!.Rooms["Room"].Cells = new() { new(1, 0) }; project = Project(spec);
            var source = Capture(system, project, bindings);
            Assert.That(system.TryExtractRoomEntities(project, source, out var single, out var issue), Is.True, message: issue?.Code);
            spec.Layouts.Rooms["Other"] = new() { SourceKey = "test-room", ContentFingerprint = Fingerprint(root.Bytes), Cells = new() { new(1, 0) } };
            project = Project(spec); source = Capture(system, project, bindings);
            var limits = new KsProcgenRoomEntityExtractionLimits { MaximumOverrideCharacters = single!.OverrideCharacters };
            Assert.That(system.TryExtractRoomEntities(project, source, out var rejected, out issue, limits: limits), Is.False);
            Assert.That(rejected, Is.Null); Assert.That(issue?.Code, Is.EqualTo("RoomEntityOverrideImageBudget"));
            Assert.That(system.TryExtractRoomEntities(project, source, out var result, out issue,
                limits: limits with { MaximumOverrideCharacters = 2 * single.OverrideCharacters }), Is.True, message: issue?.Code);
            Assert.That(result!.OverrideCharacters, Is.EqualTo(2 * single.OverrideCharacters));
            Assert.That(result.OverrideNodes, Is.EqualTo(2 * single.OverrideNodes));
        }));
    }

    [Test]
    public async Task OverrideBodiesBindExtractionIdentityEvenWhenAllWitnessesAndSourceHashesAreReused()
    {
        await AssertOnServer(() => WithSource(GridYaml() + EntityBranch, (system, project, bindings, unusedRoot) =>
        {
            var spec = project.CopySpec(); spec.Layouts!.Rooms["Room"].Cells = new() { new(1, 0) }; project = Project(spec);
            var source = Capture(system, project, bindings);
            Assert.That(system.TryExtractRoomEntities(project, source, out var original, out var issue), Is.True, message: issue?.Code);
            var room = original!.Rooms.Single(); var member = room.Entities.First();
            var images = member.ComponentOverrides.Select(image => image with { CanonicalBody = image.CanonicalBody + "different" });
            var changed = new KsProcgenExtractedMapEntity(member.YamlUid, member.PrototypeId, member.ParentYamlUid,
                member.LocalPosition, member.LocalRotationRadians, member.GridPosition, member.RoomPosition, member.GridRotationRadians,
                member.Anchored, member.NoLocalRotation, member.OverrideComponentTypes, member.RemovedComponentTypes,
                member.References, member.UninspectedComponentTypes, componentOverrides: images);
            var result = new KsProcgenRoomEntityExtraction(original.SourceContentHash, original.Definitions,
                new[] { new KsProcgenExtractedRoomEntities(room.Room, new[] { changed }.Concat(room.Entities.Skip(1))) },
                original.MaskCellProbes, original.GraphWork);
            Assert.That(result.ContentHash, Is.Not.EqualTo(original.ContentHash));
        }));
    }

    [TestCase(-1, 10, 10)] [TestCase(10, -1, 10)] [TestCase(10, 10, 0)]
    [TestCase(1_048_577, 10, 10)] [TestCase(10, 16_777_217, 10)] [TestCase(10, 10, 129)]
    public async Task InvalidOverrideAllowancesRejectBeforeSourceInspection(int nodes, int characters, int depth)
    {
        await AssertOnServer(() => WithSource(GridYaml() + EntityBranch, (system, project, bindings, unusedRoot) =>
        {
            var source = Capture(system, project, bindings);
            Assert.That(system.TryExtractRoomEntities(project, source, out var rejected, out var issue,
                limits: new() { MaximumOverrideNodes = nodes, MaximumOverrideCharacters = characters, MaximumOverrideDepth = depth }), Is.False);
            Assert.That(rejected, Is.Null); Assert.That(issue?.Code, Is.EqualTo("InvalidRoomEntityExtractionInput"));
        }));
    }

    [Test]
    public async Task OverrideImagesPreserveNestedSymbolicLinksAndEnforceDepthBeforePublication()
    {
        var fields = "      target: 11\n      nested: {11: [10, 11]}\n      numberText: '99'\n";
        await AssertOnServer(() => WithSource(GridYaml() + ReferencedRows(fields), (system, project, bindings, unusedRoot) =>
        {
            var spec = project.CopySpec(); spec.Layouts!.Rooms["Room"].Cells = new() { new(1, 0) }; project = Project(spec);
            var source = Capture(system, project, bindings); var before = SEntMan.GetEntities().ToHashSet();
            Assert.That(system.TryExtractRoomEntities(project, source, out var rejected, out var issue,
                limits: new() { MaximumOverrideDepth = 2 }), Is.False);
            Assert.That(rejected, Is.Null); Assert.That(issue?.Code, Is.EqualTo("RoomEntityOverrideDepthBudget"));
            Assert.That(system.TryExtractRoomEntities(project, source, out var result, out issue,
                limits: new() { MaximumOverrideDepth = 3 }), Is.True, message: issue?.Code);
            var owner = result!.Rooms.Single().Entities.First();
            var probe = DecodeOverride(owner, "KsProcgenMapReferenceProbe");
            Assert.That(probe.Get<ValueDataNode>("target").Value, Is.EqualTo("11"));
            Assert.That(probe.Get<MappingDataNode>("nested").Get<SequenceDataNode>("11").Cast<ValueDataNode>()
                .Select(node => node.Value), Is.EqualTo(new[] { "10", "11" }));
            Assert.That(probe.Get<ValueDataNode>("numberText").Value, Is.EqualTo("99"));
            Assert.That(owner.References.Any(reference => reference.TargetYamlUid == 99), Is.False);
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        }));
    }

    [Test]
    public async Task ExcludedSourceBranchesDoNotConsumeSelectedImageAllowance()
    {
        await AssertOnServer(() => WithSource(GridYaml() + EntityBranch.Replace("pos: 1.25,0.25", "pos: 1.25,1.25"),
            (system, project, bindings, unusedRoot) =>
        {
            var spec = project.CopySpec(); spec.Layouts!.Rooms["Room"].Cells = new() { new(1, 0) }; project = Project(spec);
            var source = Capture(system, project, bindings);
            Assert.That(system.TryExtractRoomEntities(project, source, out var result, out var issue,
                limits: new() { MaximumOverrideNodes = 0, MaximumOverrideCharacters = 0 }), Is.True, message: issue?.Code);
            Assert.That(result!.Rooms.Single().Entities, Is.Empty);
            Assert.That(result.OverrideNodes, Is.Zero); Assert.That(result.OverrideCharacters, Is.Zero);
        }));
    }

    private static MappingDataNode DecodeOverride(KsProcgenExtractedMapEntity entity, string type)
    {
        var image = entity.ComponentOverrides.Single(image => image.ComponentType == type);
        Assert.That(KsProcgenPrototypeManifestSystem.TryDecodeCanonicalBody(image.CanonicalBody, new(), out var node, out var code),
            Is.True, message: code);
        return (MappingDataNode) node!;
    }
}
