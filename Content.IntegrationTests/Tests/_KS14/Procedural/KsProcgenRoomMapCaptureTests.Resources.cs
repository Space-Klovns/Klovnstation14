#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using Content.Server._KS14.Procedural;
using Robust.Shared.ContentPack;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._KS14.Procedural;

public sealed partial class KsProcgenRoomMapCaptureTests
{
    [TestCase("path")]
    [TestCase("icon")]
    [TestCase("legacySpritePath")]
    public async Task SourceOverrideResourcesJoinExtractionIdentityWithoutLoadingEntities(string field)
    {
        await AssertOnServer(() =>
        {
            var prefix = "/KsProcgenSourceResources/" + Guid.NewGuid().ToString("N");
            var assets = new OverrideResourceRoot();
            Pair.Server.ResolveDependency<IResourceManager>().AddRoot(new(prefix), assets);
            try
            {
                var rows = "- proto: Paper\n  entities:\n  - uid: 10\n    components:\n" +
                    "    - type: Transform\n      parent: 1\n      pos: 1.25,0.25\n" +
                    "    - type: KsProcgenResourceProbe\n      " + field + ": " + prefix + "/asset.bin\n";
                WithSource(GridYaml() + rows, (system, project, bindings, sourceResource) =>
                {
                    var spec = project.CopySpec(); spec.Layouts!.Rooms["Room"].Cells = new() { new(1, 0) };
                    project = Project(spec);
                    var source = Capture(system, project, bindings); sourceResource.Available = false;
                    Assert.That(source.Definitions.ResourceReferences.Any(reference => reference.Path.StartsWith(prefix, StringComparison.Ordinal)), Is.False);
                    var before = SEntMan.GetEntities().ToHashSet();
                    Assert.That(system.TryExtractRoomEntities(project, source, out var extraction, out var issue), Is.True, issue?.ToString());
                    Assert.That(extraction!.Definitions.ResourceReferenceSeeds.Any(reference => reference.Path == prefix + "/asset.bin"), Is.True);
                    Assert.That(extraction.Definitions.Resources.Single(resource => resource.Path == prefix + "/asset.bin").CopyBytes(), Is.EqualTo(new byte[] { 1, 2, 3 }));
                    Assert.That(extraction.Definitions.Roots, Is.EqualTo(source.Definitions.Roots));
                    var definitions = Pair.Server.System<KsProcgenPrototypeManifestSystem>();
                    Assert.That(definitions.Matches(extraction.Definitions, out issue), Is.True, issue?.ToString());
                    assets.Bytes[0] = 9;
                    Assert.That(definitions.Matches(extraction.Definitions, out issue), Is.False);
                    Assert.That(issue!.Code, Is.EqualTo("PrototypeManifestChanged"));
                    Assert.That(system.TryExtractRoomEntities(project, source, out var changed, out issue), Is.True, issue?.ToString());
                    Assert.That(changed!.ContentHash, Is.Not.EqualTo(extraction.ContentHash));
                    Assert.That(changed.CopyReady || changed.ComponentReferencesVerified, Is.False);
                    assets.Available = false;
                    Assert.That(system.TryExtractRoomEntities(project, source, out var denied, out issue), Is.False);
                    Assert.That(denied, Is.Null); Assert.That(issue!.Code, Is.EqualTo("MissingPrototypeResource"));
                    Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
                });
            }
            finally { assets.Available = false; }
        });
    }

    private sealed class OverrideResourceRoot : IContentRoot
    {
        public readonly byte[] Bytes = [1, 2, 3];
        public bool Available = true;
        public void Mount() { }
        public bool FileExists(ResPath path) => Available && path.CanonPath == "asset.bin";
        public IEnumerable<ResPath> FindFiles(ResPath path) => [];
        public IEnumerable<string> GetRelativeFilePaths() => [];
        public bool TryGetFile(ResPath path, [NotNullWhen(true)] out Stream? stream)
        {
            stream = FileExists(path) ? new MemoryStream(Bytes, writable: false) : null;
            return stream != null;
        }
    }
}
