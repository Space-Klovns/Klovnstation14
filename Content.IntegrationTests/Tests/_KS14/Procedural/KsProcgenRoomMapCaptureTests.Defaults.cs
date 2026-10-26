#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Content.Server._KS14.Procedural;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.Manager;
using Robust.Shared.Serialization.Manager.Attributes;
using Robust.Shared.Serialization.Markdown.Validation;
using Robust.Shared.Serialization.Markdown.Value;
using Robust.Shared.Serialization.TypeSerializers.Interfaces;

namespace Content.IntegrationTests.Tests._KS14.Procedural;

[RegisterComponent]
public sealed partial class KsProcgenDefaultReadProbeComponent : Component
{
    [DataField(customTypeSerializer: typeof(KsProcgenDefaultReadCounter))] public int Value = 17;
}

public sealed class KsProcgenDefaultReadCounter : ITypeReader<int, ValueDataNode>
{
    public ValidationNode Validate(ISerializationManager serializationManager, ValueDataNode node,
        IDependencyCollection dependencies, ISerializationContext? context = null) => serializationManager.ValidateNode<int>(node);

    public int Read(ISerializationManager serializationManager, ValueDataNode node, IDependencyCollection dependencies,
        SerializationHookContext hookCtx, ISerializationContext? context = null,
        ISerializationManager.InstantiationDelegate<int>? instanceProvider = null)
    {
        dependencies.Resolve<IEntitySystemManager>().GetEntitySystem<KsProcgenMapReferenceTestSystem>().ValidationAction?.Invoke("default-image");
        return int.Parse(node.Value, CultureInfo.InvariantCulture);
    }
}

public sealed partial class KsProcgenRoomMapCaptureTests
{
    private const string DefaultRowOne = "  - uid: 10\n    components:\n    - type: Transform\n      parent: 1\n      pos: 1.25,0.25\n";
    private const string DefaultRowTwo = "  - uid: 11\n    components:\n    - type: Transform\n      parent: 1\n      pos: 1.75,0.25\n";
    private const string DefaultGroup = "- proto: Paper\n  entities:\n";

    private void WithDefaultReadProbe(Action<Action, Func<int>> action)
    {
        var prototype = SProtoMan.Index(new EntProtoId("Paper"));
        const string component = "KsProcgenDefaultReadProbe";
        Assert.That(prototype.Components.ContainsKey(component), Is.False);
        var hook = Pair.Server.System<KsProcgenMapReferenceTestSystem>(); var reads = 0;
        prototype.Components.Add(component, new EntityPrototype.ComponentRegistryEntry(new KsProcgenDefaultReadProbeComponent()));
        try
        {
            hook.ValidationAction = value => { if (value == "default-image") reads++; };
            action(() => reads = 0, () => reads);
        }
        finally { hook.ValidationAction = null; prototype.Components.Remove(component); }
    }

    [Test]
    public async Task DefaultImagesAreReadOnceAcrossRepeatedGroupsAndIndependentSourceFiles()
    {
        await AssertOnServer(() => WithDefaultReadProbe((reset, reads) =>
        {
            var baselineReads = 0; var baselineCharacters = 0;
            foreach (var mode in new[] { "single", "groups", "files" })
            {
                var rows = mode == "groups" ? DefaultGroup + DefaultRowOne + DefaultGroup + DefaultRowTwo : DefaultGroup + DefaultRowOne + DefaultRowTwo;
                WithSource(GridYaml() + rows, (system, project, bindings, root) =>
                {
                    var spec = project.CopySpec(); spec.Layouts!.Rooms["Room"].Cells = new() { new(1, 0) };
                    if (mode == "files")
                    {
                        root.Paths.Add("second.yml"); bindings.Add("second-source", bindings["test-room"].Replace("/room.yml", "/second.yml"));
                        spec.Layouts.Rooms["Second"] = new() { SourceKey = "second-source", ContentFingerprint = Fingerprint(root.Bytes), Cells = new() { new(1, 0) } };
                    }
                    project = Project(spec); var source = Capture(system, project, bindings);
                    var body = source.Definitions.Bodies.Single(body => body.Kind == KsProcgenPrototypeBodyKind.Entity && body.PrototypeId == "Paper");
                    var characters = body.CanonicalBody.Length + body.CanonicalMetadata.Length;
                    if (mode == "single") baselineCharacters = characters;
                    reset(); var before = SEntMan.GetEntities().ToHashSet();
                    Assert.That(system.TryExtractRoomEntities(project, source, out var extraction, out var issue), Is.True, message: issue?.ToString());
                    if (mode == "single") { baselineReads = reads(); Assert.That(baselineReads, Is.GreaterThan(0)); }
                    else Assert.That(reads(), Is.EqualTo(baselineReads));
                    Assert.That(extraction!.DefaultPrototypeImages, Is.EqualTo(1));
                    Assert.That(extraction.DefaultImageCharacters, Is.EqualTo(baselineCharacters));
                    Assert.That(extraction.Rooms.Count, Is.EqualTo(mode == "files" ? 2 : 1));
                    Assert.That(extraction.Rooms.All(room => room.Entities.Count == 2), Is.True);
                    Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
                    reset();
                    Assert.That(system.TryExtractRoomEntities(project, source, out var replay, out issue,
                        limits: new() { MaximumDefaultImageCharacters = characters }), Is.True, message: issue?.ToString());
                    Assert.That(reads(), Is.EqualTo(baselineReads), message: "Each extraction owns its cache and reconstructs its own image.");
                    Assert.That(replay!.ContentHash, Is.EqualTo(extraction.ContentHash));
                });
            }
        }));
    }

    [Test]
    public async Task DefaultImageAllowanceIsCumulativeAcrossDistinctSourcePrototypes()
    {
        await AssertOnServer(() => WithSource(GridYaml() + DefaultGroup + DefaultRowOne +
            "- proto: Table\n  entities:\n" + DefaultRowTwo, (system, project, bindings, root) =>
        {
            var spec = project.CopySpec(); spec.Layouts!.Rooms["Room"].Cells = new() { new(1, 0) }; project = Project(spec);
            var source = Capture(system, project, bindings);
            var characters = source.Definitions.Bodies.Where(body => body.Kind == KsProcgenPrototypeBodyKind.Entity && body.PrototypeId is "Paper" or "Table")
                .Sum(body => body.CanonicalBody.Length + body.CanonicalMetadata.Length);
            var before = SEntMan.GetEntities().ToHashSet();
            Assert.That(system.TryExtractRoomEntities(project, source, out var rejected, out var issue,
                limits: new() { MaximumDefaultImageCharacters = characters - 1 }), Is.False);
            Assert.That(rejected, Is.Null); Assert.That(issue?.Code, Is.EqualTo("RoomEntityExtractionDefaultImageBudget"));
            Assert.That(system.TryExtractRoomEntities(project, source, out var extraction, out issue,
                limits: new() { MaximumDefaultImageCharacters = characters }), Is.True, message: issue?.ToString());
            Assert.That(extraction!.DefaultPrototypeImages, Is.EqualTo(2));
            Assert.That(extraction.DefaultImageCharacters, Is.EqualTo(characters));
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        }));
    }

    [TestCase(0)] [TestCase(1)]
    public async Task DefaultImageAllowanceRejectsBeforeAnyPrototypeFieldReaderRuns(int shortfallMode)
    {
        await AssertOnServer(() => WithDefaultReadProbe((reset, reads) =>
            WithSource(GridYaml() + DefaultGroup + DefaultRowOne, (system, project, bindings, root) =>
            {
                var spec = project.CopySpec(); spec.Layouts!.Rooms["Room"].Cells = new() { new(1, 0) }; project = Project(spec);
                var source = Capture(system, project, bindings);
                var body = source.Definitions.Bodies.Single(body => body.Kind == KsProcgenPrototypeBodyKind.Entity && body.PrototypeId == "Paper");
                var allowance = shortfallMode == 0 ? 0 : body.CanonicalBody.Length + body.CanonicalMetadata.Length - 1;
                reset(); var before = SEntMan.GetEntities().ToHashSet();
                Assert.That(system.TryExtractRoomEntities(project, source, out var result, out var issue,
                    limits: new() { MaximumDefaultImageCharacters = allowance }), Is.False);
                Assert.That(result, Is.Null); Assert.That(issue?.Code, Is.EqualTo("RoomEntityExtractionDefaultImageBudget"));
                Assert.That(reads(), Is.Zero); Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
            })));
    }

    [Test]
    public async Task ImageCreationConsumesGraphWorkBeforeAnyPrototypeFieldReaderRuns()
    {
        var yaml = GridYaml();
        var groups = yaml.IndexOf("entities:\n", StringComparison.Ordinal) + "entities:\n".Length;
        yaml = yaml.Insert(groups, DefaultGroup + DefaultRowOne);
        await AssertOnServer(() => WithDefaultReadProbe((reset, reads) => WithSource(yaml, (system, project, bindings, root) =>
        {
            var spec = project.CopySpec(); spec.Layouts!.Rooms["Room"].Cells = new() { new(1, 0) }; project = Project(spec);
            var source = Capture(system, project, bindings); reset();
            Assert.That(system.TryExtractRoomEntities(project, source, out var result, out var issue,
                limits: new() { MaximumGraphWork = 1 }), Is.False);
            Assert.That(result, Is.Null); Assert.That(issue?.Code, Is.EqualTo("RoomEntityExtractionGraphBudget"));
            Assert.That(reads(), Is.Zero);
        })));
    }

    [Test]
    public async Task EmptyGroupsUseNoImagesButConsumeGroupWorkAndRespectZeroImageAllowance()
    {
        await AssertOnServer(() => WithDefaultReadProbe((reset, reads) =>
        {
            var baselineWork = 0;
            foreach (var count in new[] { 0, 20 })
            WithSource(GridYaml() + string.Concat(Enumerable.Repeat("- proto: Paper\n  entities: []\n", count)), (system, project, bindings, root) =>
            {
                var spec = project.CopySpec(); spec.Layouts!.Rooms["Room"].Cells = new() { new(1, 0) }; project = Project(spec);
                var source = Capture(system, project, bindings); reset();
                Assert.That(system.TryExtractRoomEntities(project, source, out var extraction, out var issue,
                    limits: new() { MaximumDefaultImageCharacters = 0 }), Is.True, message: issue?.ToString());
                Assert.That(reads(), Is.Zero); Assert.That(extraction!.DefaultPrototypeImages, Is.Zero);
                Assert.That(extraction.DefaultImageCharacters, Is.Zero); Assert.That(extraction.Rooms.Single().Entities, Is.Empty);
                if (count == 0) baselineWork = extraction.GraphWork;
                else
                {
                    Assert.That(extraction.GraphWork, Is.EqualTo(baselineWork + count));
                    Assert.That(system.TryExtractRoomEntities(project, source, out var rejected, out issue,
                        limits: new() { MaximumGraphWork = extraction.GraphWork - 1, MaximumDefaultImageCharacters = 0 }), Is.False);
                    Assert.That(rejected, Is.Null); Assert.That(issue?.Code, Is.EqualTo("RoomEntityExtractionGraphBudget"));
                }
            });
        }));
    }

    [TestCase(-1)] [TestCase(16777217)]
    public async Task InvalidDefaultImageAllowancesAreRejectedBeforeReadingDefaults(int allowance)
    {
        await AssertOnServer(() => WithSource(GridYaml(), (system, project, bindings, root) =>
        {
            var source = Capture(system, project, bindings);
            Assert.That(system.TryExtractRoomEntities(project, source, out var result, out var issue,
                limits: new() { MaximumDefaultImageCharacters = allowance }), Is.False);
            Assert.That(result, Is.Null); Assert.That(issue?.Code, Is.EqualTo("InvalidRoomEntityExtractionInput"));
        }));
    }
}
