#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.Server._KS14.Procedural;
using Content.Shared.Materials;
using Robust.Shared.Prototypes;
using Robust.UnitTesting;
using Robust.Shared.Serialization.Markdown.Value;

namespace Content.IntegrationTests.Tests._KS14.Procedural;

public sealed partial class KsProcgenRoomMapCaptureTests
{
    private const string SourceMaterial = "KsProcgenSourceKindMaterial";
    private const string SourceLeaf = "KsProcgenSourceKindLeaf";

    [TestPrototypes]
    private const string KindPrototypes = """
- type: material
  abstract: true
  id: KsProcgenSourceKindAncestor
  price: 1

- type: material
  abstract: false
  parent: KsProcgenSourceKindAncestor
  id: KsProcgenSourceKindMaterial
  price: 2
  stackEntity: KsProcgenSourceKindLeaf

- type: entityCategory
  id: KsProcgenSourceKindMaterial

- type: entity
  id: KsProcgenSourceKindLeaf
""";

    [TestCase(false)] [TestCase(true)]
    public async Task SourceOnlyTypedKindsAndTheirEntityDependenciesAreCapturedWithoutLoadingTheMap(bool nested)
    {
        var fields = nested ? "      kindReferences: {" + SourceMaterial + ": " + SourceMaterial + "}\n" :
            "      materialPrototype: " + SourceMaterial + "\n      categoryPrototype: " + SourceMaterial + "\n";
        await AssertOnServer(() => WithSource(GridYaml() + ReferencedRows(fields), (system, project, bindings, resource) =>
        {
            var spec = project.CopySpec(); spec.Layouts!.Rooms["Room"].Cells = new() { new(1, 0) }; project = Project(spec);
            var source = Capture(system, project, bindings); resource.Available = false;
            Assert.That(source.Definitions.Dependencies.Any(body => body.PrototypeId == SourceMaterial), Is.False);
            var before = SEntMan.GetEntities().ToHashSet();
            Assert.That(system.TryExtractRoomEntities(project, source, out var extraction, out var issue), Is.True, issue?.ToString());
            var definitions = extraction!.Definitions;
            Assert.That(definitions.TypedReferenceSeeds.Count(seed => seed.PrototypeId == SourceMaterial), Is.EqualTo(2));
            Assert.That(definitions.DependencyRoots, Is.EqualTo(source.Definitions.DependencyRoots));
            Assert.That(definitions.Roots, Is.EqualTo(source.Definitions.Roots));
            Assert.That(definitions.Dependencies.Count(body => body.PrototypeId == SourceMaterial), Is.EqualTo(2));
            Assert.That(definitions.Bodies.Any(body => body.PrototypeId == SourceLeaf), Is.True);
            Assert.That(definitions.Roots.Any(root => root.PrototypeId == SourceLeaf), Is.False);
            Assert.That(extraction.Rooms.Single().Entities.Single(entity => entity.YamlUid == 10).UninspectedComponentTypes, Is.Empty);
            var manifests = Pair.Server.System<KsProcgenPrototypeManifestSystem>();
            Assert.That(manifests.Matches(definitions, out issue), Is.True, issue?.ToString());
            Assert.That(system.TryExtractRoomEntities(project, source, out var replay, out issue), Is.True, issue?.ToString());
            Assert.That(replay!.ContentHash, Is.EqualTo(extraction.ContentHash));
            var material = SProtoMan.Index<MaterialPrototype>(SourceMaterial); var price = material.Price;
            try
            {
                material.Price += 1;
                Assert.That(manifests.Matches(definitions, out issue), Is.False); Assert.That(issue!.Code, Is.EqualTo("PrototypeManifestChanged"));
            }
            finally { material.Price = price; }
            Assert.That(extraction.CopyReady || extraction.ComponentReferencesVerified, Is.False);
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        }));
    }

    [Test]
    public async Task RepeatedSourceKindSeedsAreChargedBeforeDeduplicationAndGraphShortfallIsAtomic()
    {
        await AssertOnServer(() =>
        {
            KsProcgenRoomEntityExtraction? few = null;
            foreach (var count in new[] { 2, 20 })
            WithSource(GridYaml() + ReferencedRows("      materialPrototypes: [" + string.Join(",", Enumerable.Repeat(SourceMaterial, count)) + "]\n"),
                (system, project, bindings, unusedResource) =>
                {
                    var spec = project.CopySpec(); spec.Layouts!.Rooms["Room"].Cells = new() { new(1, 0) }; project = Project(spec);
                    var source = Capture(system, project, bindings);
                    Assert.That(system.TryExtractRoomEntities(project, source, out var extraction, out var issue), Is.True, issue?.ToString());
                    if (count == 2) { few = extraction; return; }
                    Assert.That(extraction!.Definitions.TypedReferenceSeeds, Is.EqualTo(few!.Definitions.TypedReferenceSeeds));
                    Assert.That(extraction.Definitions.ReferenceOccurrences, Is.EqualTo(few.Definitions.ReferenceOccurrences + 18));
                    Assert.That(extraction.GraphWork, Is.GreaterThanOrEqualTo(few.GraphWork + 18));
                    Assert.That(system.TryExtractRoomEntities(project, source, out var denied, out issue,
                        limits: new() { MaximumGraphWork = extraction.GraphWork - 1 }), Is.False);
                    Assert.That(denied, Is.Null); Assert.That(issue!.Code, Is.EqualTo("RoomEntityExtractionGraphBudget"));
                });
        });
    }

    [Test]
    public async Task SourceKindSeedsShareTheOriginalDefinitionOccurrenceLimit()
    {
        await AssertOnServer(() =>
        {
            var occurrences = 0;
            WithSource(GridYaml() + ReferencedRows(""), (system, project, bindings, unusedResource) =>
                occurrences = Capture(system, project, bindings).Definitions.ReferenceOccurrences);
            var fields = "      materialPrototypes: [" + string.Join(",", Enumerable.Repeat(SourceMaterial, occurrences + 1)) + "]\n";
            WithSource(GridYaml() + ReferencedRows(fields),
            (system, project, bindings, unusedResource) =>
            {
                var spec = project.CopySpec(); spec.Layouts!.Rooms["Room"].Cells = new() { new(1, 0) }; project = Project(spec);
                var source = Capture(system, project, bindings);
                Assert.That(system.TryExtractRoomEntities(project, source, out var complete, out var issue), Is.True, issue?.ToString());
                Assert.That(complete!.Definitions.ReferenceOccurrences, Is.GreaterThan(1));
                // The source capture fits this same allowance. Extraction's new closure exceeds it.
                var maximum = complete.Definitions.ReferenceOccurrences - 1;
                Assert.That(system.TryCapture(project, bindings, out var limitedSource, out issue,
                    limits: source.Limits with { Definitions = source.Definitions.Limits with { MaximumReferences = maximum } }), Is.True, issue?.ToString());
                Assert.That(system.TryExtractRoomEntities(project, limitedSource!, out var denied, out issue), Is.False);
                Assert.That(denied, Is.Null); Assert.That(issue!.Code, Is.EqualTo("PrototypeManifestReferenceBudget"));
            });
        });
    }

    [Test]
    public async Task MissingSourceKindDefinitionIsRejectedRatherThanMarkedAsAnOpaqueSuccessfulReference()
    {
        await AssertOnServer(() => WithSource(GridYaml() + ReferencedRows("      materialPrototype: KsProcgenMissingSourceMaterial\n"),
            (system, project, bindings, unusedResource) =>
            {
                var spec = project.CopySpec(); spec.Layouts!.Rooms["Room"].Cells = new() { new(1, 0) }; project = Project(spec);
                var source = Capture(system, project, bindings); var before = SEntMan.GetEntities().ToHashSet();
                Assert.That(system.TryExtractRoomEntities(project, source, out var denied, out var issue), Is.False);
                Assert.That(denied, Is.Null); Assert.That(issue!.Code, Is.EqualTo("MissingPrototypeManifestDependency"));
                Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
            }));
    }

    [Test]
    public async Task TypedSourceTileSeedsRetainTheirRoleAndUseRegisteredTileAudit()
    {
        await AssertOnServer(() => WithSource(GridYaml() + ReferencedRows("      tilePrototype: Space\n"),
            (system, project, bindings, unusedResource) =>
            {
                var spec = project.CopySpec(); spec.Layouts!.Rooms["Room"].Cells = new() { new(1, 0) }; project = Project(spec);
                var source = Capture(system, project, bindings);
                Assert.That(system.TryExtractRoomEntities(project, source, out var extraction, out var issue), Is.True, issue?.ToString());
                Assert.That(extraction!.Definitions.TypedReferenceSeeds, Does.Contain(new KsProcgenPrototypeReference("tile", "Space")));
                Assert.That(extraction.Definitions.Roots, Is.EqualTo(source.Definitions.Roots));
                Assert.That(Pair.Server.System<KsProcgenPrototypeManifestSystem>().Matches(extraction.Definitions, out issue), Is.True, issue?.ToString());
            }));
    }
    [Test]
    public async Task SourceOverrideAncestorsInvalidateDetachedDefinitionsWithoutBecomingRoots()
    {
        await AssertOnServer(() => WithSource(GridYaml() + ReferencedRows("      materialPrototype: " + SourceMaterial + "\n"),
            (system, project, bindings, resource) =>
            {
                var spec = project.CopySpec(); spec.Layouts!.Rooms["Room"].Cells = new() { new(1, 0) }; project = Project(spec);
                var source = Capture(system, project, bindings); resource.Available = false;
                Assert.That(system.TryExtractRoomEntities(project, source, out var extraction, out var issue), Is.True, issue?.ToString());
                const string ancestor = "KsProcgenSourceKindAncestor";
                Assert.That(extraction!.Definitions.Dependencies.Single(body => body.PrototypeId == ancestor).DeclarationOnly, Is.True);
                Assert.That(extraction.Definitions.Roots, Is.EqualTo(source.Definitions.Roots));
                Assert.That(extraction.Definitions.TypedReferenceSeeds.Any(seed => seed.PrototypeId == ancestor), Is.False);
                Assert.That(SProtoMan.TryGetMapping<MaterialPrototype>(ancestor, out var declaration), Is.True);
                var original = declaration!["price"]; var hash = extraction.ContentHash;
                var price = SProtoMan.Index<MaterialPrototype>(SourceMaterial).Price;
                try
                {
                    declaration["price"] = new ValueDataNode("17");
                    Assert.That(SProtoMan.Index<MaterialPrototype>(SourceMaterial).Price, Is.EqualTo(price));
                    Assert.That(Pair.Server.System<KsProcgenPrototypeManifestSystem>().Matches(extraction.Definitions, out issue), Is.False);
                    Assert.That(issue!.Code, Is.EqualTo("PrototypeManifestChanged"));
                    Assert.That(system.TryExtractRoomEntities(project, source, out var changed, out issue), Is.True, issue?.ToString());
                    Assert.That(changed!.ContentHash, Is.Not.EqualTo(hash));
                    Assert.That(extraction.ContentHash, Is.EqualTo(hash));
                    Assert.That(extraction.CopyReady || extraction.ComponentReferencesVerified, Is.False);
                }
                finally { declaration["price"] = original; }
            }));
    }

}
