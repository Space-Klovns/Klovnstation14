#nullable enable
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text;
using Content.Server._KS14.Procedural;
using Content.Shared._KS14.Procedural;
using Content.Shared._KS14.Serialization;
using Robust.Shared.ContentPack;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager.Attributes;
using Robust.Shared.Utility;
using Robust.UnitTesting;

namespace Content.IntegrationTests.Tests._KS14.Procedural;

[RegisterComponent]
public sealed partial class KsProcgenResourceProbeComponent : Component
{
    [DataField] public ResPath? Path;
    [DataField] public SpriteSpecifier? Icon;
    [DataField(customTypeSerializer: typeof(KsResourcePathStringSerializer))] public string? LegacySpritePath;
}

[TestOf(typeof(KsProcgenPrototypeManifestSystem))]
public sealed class KsProcgenResourceManifestTests : KsProcgenGameTest
{
    private const string ProbeId = "KsProcgenResourceProbeFixture";
    private const string LeafId = "KsProcgenResourceLeafFixture";
    private const string Metadata = """{"version":1,"size":{"x":1,"y":1},"states":[{"name":"idle"}]}""";
    private static readonly byte[] Pixel = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+ip1sAAAAASUVORK5CYII=");

    [TestPrototypes]
    private const string Prototypes = """
- type: entity
  id: KsProcgenResourceProbeFixture
  components:
  - type: KsProcgenResourceProbe
- type: entity
  id: KsProcgenResourceLeafFixture
""";

    [Test]
    public async Task CapturedBytesAreDetachedAndSameLengthDriftChangesIdentity()
    {
        await AssertOnServer(() => WithResources((system, root, prefix) =>
        {
            root.Files["a.bin"] = [1, 2, 3];
            var reference = new KsProcgenResourceReference(KsProcgenResourceReferenceKind.Path, prefix + "/a.bin");
            var manifest = Capture(system, [reference]);
            Assert.That(manifest.Resources.Count, Is.EqualTo(1));
            Assert.That(manifest.CapturedResourceBytes, Is.EqualTo(3));
            Assert.That(manifest.FullContentClosureVerified, Is.False);
            var file = manifest.Resources.Single();
            var copy = file.CopyBytes(); copy[0] = 99;
            Assert.That(file.CopyBytes(), Is.EqualTo(new byte[] { 1, 2, 3 }));
            using (var stream = file.OpenRead())
            {
                Assert.Throws<NotSupportedException>(() => stream.WriteByte(9));
                Assert.Throws<UnauthorizedAccessException>(() => ((MemoryStream) stream).GetBuffer());
            }
            Assert.That(system.Matches(manifest, out var issue), Is.True, issue?.ToString());
            root.Files["a.bin"][0] = 4;
            Assert.That(file.CopyBytes(), Is.EqualTo(new byte[] { 1, 2, 3 }));
            var changed = Capture(system, [reference]);
            Assert.That(changed.ContentHash, Is.Not.EqualTo(manifest.ContentHash));
            Assert.That(system.Matches(manifest, out issue), Is.False);
            Assert.That(issue!.Code, Is.EqualTo("PrototypeManifestChanged"));
            root.Files.Clear();
            Assert.That(system.Matches(manifest, out issue), Is.False);
            Assert.That(issue!.Code, Is.EqualTo("PrototypeManifestChanged"));
        }));
    }

    [TestCase("path")]
    [TestCase("rsi")]
    [TestCase("entity-icon")]
    [TestCase("legacy-string")]
    public async Task RegisteredSerializersDiscoverResourcesAndEntityIcons(string kind)
    {
        await AssertOnServer(() => WithResources((system, root, prefix) =>
        {
            var prototypeManager = Pair.Server.ResolveDependency<IPrototypeManager>();
            Assert.That(prototypeManager.Index<EntityPrototype>(ProbeId)
                .TryComp<KsProcgenResourceProbeComponent>(out var component, SEntMan.ComponentFactory), Is.True);
            var oldPath = component!.Path; var oldIcon = component.Icon; var oldLegacyPath = component.LegacySpritePath;
            try
            {
                root.Files["a.bin"] = [1, 2, 3];
                AddRsi(root);
                if (kind == "path") component.Path = new ResPath(prefix + "/a.bin");
                else if (kind == "rsi") component.Icon = new SpriteSpecifier.Rsi(new(prefix + "/test.rsi"), "idle");
                else if (kind == "legacy-string") component.LegacySpritePath = prefix + "/test.rsi";
                else component.Icon = new SpriteSpecifier.EntityPrototype(LeafId);
                Assert.That(system.TryCapture([ProbeId], [], out var manifest, out var issue), Is.True, issue?.ToString());
                Assert.That(manifest!.ResourceReferenceSeeds, Is.Empty);
                if (kind == "entity-icon")
                {
                    Assert.That(manifest.Bodies.Any(body => body.PrototypeId == LeafId), Is.True);
                    Assert.That(manifest.Roots.Any(body => body.PrototypeId == LeafId), Is.False);
                }
                else
                {
                    Assert.That(manifest.Resources.Count, Is.EqualTo(kind == "path" ? 1 : 2));
                    Assert.That(system.MatchesBody(manifest, KsProcgenPrototypeBodyKind.Entity, ProbeId, out issue), Is.True, issue?.ToString());
                    var file = kind == "path" ? "a.bin" : "test.rsi/idle.png";
                    root.Files[file] = [9, 8, 7];
                    Assert.That(system.MatchesBody(manifest, KsProcgenPrototypeBodyKind.Entity, ProbeId, out issue), Is.False);
                    Assert.That(issue!.Code, Is.EqualTo("PrototypeManifestChanged"));
                }
            }
            finally { component.Path = oldPath; component.Icon = oldIcon; component.LegacySpritePath = oldLegacyPath; }
        }));
    }

    [TestCase("relative")]
    [TestCase("texture-prefix")]
    [TestCase("rooted")]
    public async Task NativeTexturePathFormsShareCanonicalIdentity(string form)
    {
        await AssertOnServer(() => WithResources((system, root, prefix) =>
        {
            var texturePrefix = "/Textures" + prefix;
            Pair.Server.ResolveDependency<IResourceManager>().AddRoot(new(texturePrefix), root);
            root.Files["a.bin"] = [1, 2, 3];
            var path = (form == "relative" ? prefix.TrimStart('/') : form == "texture-prefix" ? "Textures" + prefix : texturePrefix) + "/a.bin";
            var first = Capture(system, [new(KsProcgenResourceReferenceKind.Path, path)]);
            var canonical = Capture(system, [new(KsProcgenResourceReferenceKind.Path, texturePrefix + "/a.bin")]);
            Assert.That(first.ContentHash, Is.EqualTo(canonical.ContentHash));
            Assert.That(first.Resources.Single().Path, Is.EqualTo(texturePrefix + "/a.bin"));
        }));
    }

    [TestCase("")]
    [TestCase(" ")]
    public async Task EmptyEntitySpriteIdsCannotDisappearFromDependencyObservation(string id)
    {
        await AssertOnServer(() => WithResources((system, _, _) =>
        {
            Assert.That(SProtoMan.Index<EntityPrototype>(ProbeId)
                .TryComp<KsProcgenResourceProbeComponent>(out var component, SEntMan.ComponentFactory), Is.True);
            var original = component!.Icon;
            try
            {
                component.Icon = new SpriteSpecifier.EntityPrototype(id);
                Assert.That(system.TryCapture([ProbeId], [], out var manifest, out var issue), Is.False);
                Assert.That(manifest, Is.Null); Assert.That(issue!.Code, Is.EqualTo("UnsupportedPrototypeManifestBody"));
            }
            finally { component.Icon = original; }
        }));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task OwnedStagesRetireWhenSelectedOrUnselectedCandidateResourcesDrift(bool required)
    {
        await AssertOnServer(() => WithResources((_, root, prefix) =>
        {
            Assert.That(SProtoMan.Index<EntityPrototype>(ProbeId)
                .TryComp<KsProcgenResourceProbeComponent>(out var component, SEntMan.ComponentFactory), Is.True);
            var oldPath = component!.Path;
            var stages = Pair.Server.System<KsProcgenOwnedStageSystem>();
            var before = SEntMan.GetEntities().ToHashSet();
            try
            {
                root.Files["a.bin"] = [1, 2, 3]; component.Path = new ResPath(prefix + "/a.bin");
                var spec = new KsProcgenContentDraftSpec { DocumentId = "ResourceDependencies",
                    Themes = new() { ["ResourceDependencies"] = new() { Parent = "KsProcgenNoDoorThemeFixture",
                        Goals = new() { FurnishingDensity = 0f, LightingCoverage = 0f },
                        EntityPacks = new() { new() { Pack = "Contents" } } } },
                    Entities = new() { ["Contents"] = new() { Assemblies = new() { new()
                        { Id = "Core", Assembly = "Core", MinimumCount = required ? 1 : 0 } } } },
                    Assemblies = new() { ["Core"] = new() { AnchorMember = "Root", Members = new()
                        { new() { Id = "Root", Entity = ProbeId } } } } };
                Assert.That(KsProcgenContentDraft.TrySnapshot(spec, out var draft, out var issue), Is.True, issue?.ToString());
                Assert.That(KsProcgenThemeCatalog.TryCapture(SProtoMan, "ResourceDependencies", out var catalog, out issue,
                    assets: draft), Is.True, issue?.ToString());
                var generated = stages.TryGenerate(new() { RequestId = "ResourceDependencies", Seed = 17,
                    Shape = new() { Cells = new() { new(0, 0), new(1, 0), new(2, 0) } },
                    ConnectivityPolicy = KsProcgenConnectivityPolicy.PerIsland }, "ResourceDependencies",
                    content: catalog, planningBudgets: new() { MinimumRoomCells = 1, PreferredMaximumRoomCells = 64 });
                Assert.That(generated.Stage, Is.Not.Null, generated.Issue?.ToString());
                var stage = generated.Stage!;
                Assert.That(stage.Prototypes.Resources.Any(resource => resource.Path == prefix + "/a.bin"), Is.True);
                Assert.That(stage.Scene.Assemblies.SelectMany(assembly => assembly.Poses.Members)
                    .Any(member => member.Support.EntityPrototypeId == ProbeId), Is.EqualTo(required));
                root.Files["a.bin"][0] = 9;
                Assert.That(stages.Verify(stage), Is.False);
                var initialized = stages.TryInitialize(stage);
                Assert.That(initialized.Status, Is.EqualTo(KsProcgenOwnedStageStatus.Rejected));
                Assert.That(initialized.Issue!.Code, Is.EqualTo("OwnedStagePrototypeDrift"));
                Assert.That(stage.Active, Is.False); Assert.That(stages.RetainedStageCount, Is.Zero);
            }
            finally { component.Path = oldPath; stages.DiscardAll(); }
            Assert.That(SEntMan.GetEntities(), Is.EquivalentTo(before));
        }));
    }

    [Test]
    public async Task DirectoryOrderIsCanonicalButRepeatedEntryWorkStillConsumesAllowance()
    {
        await AssertOnServer(() => WithResources((system, root, prefix) =>
        {
            root.Files["folder/deep/a.bin"] = [1]; root.Files["folder/b.bin"] = [2];
            var reference = new KsProcgenResourceReference(KsProcgenResourceReferenceKind.Path, prefix + "/folder/");
            var first = Capture(system, [reference]);
            root.Listing = [new("folder/b.bin"), new("folder/deep/a.bin")];
            Assert.That(Capture(system, [reference]).ContentHash, Is.EqualTo(first.ContentHash));
            root.Listing = null;
            Assert.That(system.TryCapture([], [], out var manifest, out var issue,
                limits: new() { Resources = new() { MaximumDirectoryEntries = 2 } },
                resourceReferenceSeeds: [reference, reference with { Path = prefix + "/folder/deep/" }]), Is.False);
            Assert.That(manifest, Is.Null); Assert.That(issue!.Code, Is.EqualTo("PrototypeResourceDirectoryBudget"));
        }));
    }

    [TestCase("path-state")]
    [TestCase("unknown-kind")]
    [TestCase("long-state")]
    [TestCase("control-state")]
    [TestCase("null")]
    public async Task InvalidResourceContractsRejectBeforeIo(string kind)
    {
        await AssertOnServer(() => WithResources((system, root, prefix) =>
        {
            var reference = new KsProcgenResourceReference(KsProcgenResourceReferenceKind.Rsi, prefix + "/test.rsi");
            reference = kind switch
            {
                "path-state" => reference with { Kind = KsProcgenResourceReferenceKind.Path, State = "idle" },
                "unknown-kind" => reference with { Kind = (KsProcgenResourceReferenceKind) 255 },
                "long-state" => reference with { State = new string('a', 513) },
                "control-state" => reference with { State = "bad\0state" },
                _ => null!,
            };
            Assert.That(system.TryCapture([], [], out var manifest, out var issue,
                resourceReferenceSeeds: [reference]), Is.False);
            Assert.That(manifest, Is.Null); Assert.That(issue!.Code, Is.EqualTo("InvalidPrototypeResourceReference"));
            Assert.That(root.Streams, Is.Empty);
        }));
    }

    [TestCase("file-bytes", "PrototypeResourceFileByteBudget")]
    [TestCase("total-bytes", "PrototypeResourceTotalByteBudget")]
    [TestCase("file-count", "PrototypeResourceFileBudget")]
    [TestCase("references", "PrototypeResourceReferenceBudget")]
    [TestCase("directory", "PrototypeResourceDirectoryBudget")]
    public async Task ResourceAllowancesFailAtomically(string kind, string code)
    {
        await AssertOnServer(() => WithResources((system, root, prefix) =>
        {
            root.Files["folder/a.bin"] = [1, 2, 3]; root.Files["folder/b.bin"] = [4];
            var first = new KsProcgenResourceReference(KsProcgenResourceReferenceKind.Path, prefix + "/folder/a.bin");
            var second = first with { Path = prefix + "/folder/b.bin" };
            var limits = new KsProcgenResourceCaptureLimits();
            KsProcgenResourceReference[] references = [first, second];
            switch (kind)
            {
                case "file-bytes": limits = limits with { MaximumFileBytes = 2 }; break;
                case "total-bytes": limits = limits with { MaximumTotalBytes = 3 }; break;
                case "file-count": limits = limits with { MaximumFiles = 1 }; break;
                case "references": limits = limits with { MaximumReferences = 1 }; references = [first, first]; break;
                case "directory": limits = limits with { MaximumDirectoryEntries = 1 }; references = [first with { Path = prefix + "/folder/" }]; break;
            }
            Assert.That(system.TryCapture([], [], out var manifest, out var issue,
                limits: new() { Resources = limits }, resourceReferenceSeeds: references), Is.False);
            Assert.That(manifest, Is.Null); Assert.That(issue!.Code, Is.EqualTo(code));
            Assert.That(root.Streams.All(stream => stream.WasDisposed), Is.True);
            if (kind == "file-bytes") Assert.That(root.Streams.Sum(stream => stream.BytesRead), Is.EqualTo(3));
        }));
    }

    [Test]
    public async Task ExactByteAndFileAllowancesAcceptChunkedNonseekableStreams()
    {
        await AssertOnServer(() => WithResources((system, root, prefix) =>
        {
            root.Files["a.bin"] = [1, 2, 3]; root.ChunkSize = 1;
            var manifest = Capture(system, [new(KsProcgenResourceReferenceKind.Path, prefix + "/a.bin")],
                new() { MaximumFiles = 1, MaximumFileBytes = 3, MaximumTotalBytes = 3, MaximumReferences = 1 });
            Assert.That(manifest.Resources.Single().CopyBytes(), Is.EqualTo(new byte[] { 1, 2, 3 }));
            Assert.That(root.Streams.Single().BytesRead, Is.EqualTo(3));
            Assert.That(root.Streams.Single().WasDisposed, Is.True);
        }));
    }

    [TestCase("../escape")]
    [TestCase("/Audio//invalid")]
    [TestCase("/bad\\path")]
    [TestCase("C:/absolute")]
    [TestCase("/")]
    public async Task InvalidResourcePathsRejectBeforeOpeningContent(string path)
    {
        await AssertOnServer(() => WithResources((system, root, _) =>
        {
            Assert.That(system.TryCapture([], [], out var manifest, out var issue,
                resourceReferenceSeeds: [new(KsProcgenResourceReferenceKind.Path, path)]), Is.False);
            Assert.That(manifest, Is.Null); Assert.That(issue!.Code, Is.EqualTo("InvalidPrototypeResourceReference"));
            Assert.That(root.Streams, Is.Empty);
        }));
    }

    [TestCase("missing", "MissingPrototypeResource")]
    [TestCase("throwing", "InvalidPrototypeResource")]
    [TestCase("empty-directory", "MissingPrototypeResource")]
    [TestCase("escaping-directory", "InvalidPrototypeResourceDirectory")]
    public async Task ResourceIoFailuresRemainDistinctAndDisposeStreams(string kind, string code)
    {
        await AssertOnServer(() => WithResources((system, root, prefix) =>
        {
            root.Files["a.bin"] = [1, 2, 3];
            root.ThrowOnRead = kind == "throwing";
            if (kind == "escaping-directory") root.Listing = [new("../escape.bin")];
            var path = prefix + (kind == "missing" ? "/missing.bin" : kind.EndsWith("directory", StringComparison.Ordinal) ? "/empty/" : "/a.bin");
            Assert.That(system.TryCapture([], [], out var manifest, out var issue,
                resourceReferenceSeeds: [new(KsProcgenResourceReferenceKind.Path, path)]), Is.False);
            Assert.That(manifest, Is.Null); Assert.That(issue!.Code, Is.EqualTo(code));
            Assert.That(root.Streams.All(stream => stream.WasDisposed), Is.True);
        }));
    }

    [TestCase("folder")]
    [TestCase("compiled")]
    public async Task RsiCaptureRetainsMetadataAndFramesAndChecksRequestedState(string kind)
    {
        await AssertOnServer(() => WithResources((system, root, prefix) =>
        {
            if (kind == "folder") AddRsi(root);
            else root.Files["test.rsic"] = CompiledRsi(Metadata);
            var reference = new KsProcgenResourceReference(KsProcgenResourceReferenceKind.Rsi, prefix + "/test.rsi", "idle");
            var manifest = Capture(system, [reference]);
            Assert.That(manifest.Resources.Count, Is.EqualTo(kind == "folder" ? 2 : 1));
            Assert.That(system.Matches(manifest, out var issue), Is.True, issue?.ToString());
            Assert.That(system.TryCapture([], [], out var invalid, out issue,
                resourceReferenceSeeds: [reference with { State = "absent" }]), Is.False);
            Assert.That(invalid, Is.Null); Assert.That(issue!.Code, Is.EqualTo("MissingPrototypeRsiState"));
        }));
    }

    [TestCase("invalid-json", "InvalidPrototypeResource")]
    [TestCase("missing-frame", "MissingPrototypeResource")]
    [TestCase("duplicate-state", "InvalidPrototypeRsi")]
    [TestCase("state-limit", "PrototypeResourceRsiStateBudget")]
    [TestCase("depth-limit", "InvalidPrototypeResource")]
    [TestCase("compiled-no-metadata", "InvalidPrototypeRsi")]
    public async Task RsiFailuresReturnNoPartialManifest(string kind, string code)
    {
        await AssertOnServer(() => WithResources((system, root, prefix) =>
        {
            AddRsi(root);
            var limits = new KsProcgenResourceCaptureLimits();
            if (kind == "invalid-json") root.Files["test.rsi/meta.json"] = Encoding.UTF8.GetBytes("{");
            if (kind == "missing-frame") root.Files.Remove("test.rsi/idle.png");
            if (kind is "duplicate-state" or "state-limit")
                root.Files["test.rsi/meta.json"] = Encoding.UTF8.GetBytes(Metadata.Replace("[{\"name\":\"idle\"}]", "[{\"name\":\"idle\"},{\"name\":\"idle\"}]"));
            if (kind == "state-limit") limits = limits with { MaximumRsiStates = 1 };
            if (kind == "depth-limit") limits = limits with { MaximumJsonDepth = 1 };
            if (kind == "compiled-no-metadata") { root.Files.Clear(); root.Files["test.rsic"] = Pixel; }
            Assert.That(system.TryCapture([], [], out var manifest, out var issue, limits: new() { Resources = limits },
                resourceReferenceSeeds: [new(KsProcgenResourceReferenceKind.Rsi, prefix + "/test.rsi", "idle")]), Is.False);
            Assert.That(manifest, Is.Null); Assert.That(issue!.Code, Is.EqualTo(code));
            Assert.That(root.Streams.All(stream => stream.WasDisposed), Is.True);
        }));
    }

    private static KsProcgenPrototypeManifest Capture(KsProcgenPrototypeManifestSystem system,
        IEnumerable<KsProcgenResourceReference> references, KsProcgenResourceCaptureLimits? limits = null)
    {
        Assert.That(system.TryCapture([], [], out var manifest, out var issue,
            limits: new() { Resources = limits ?? new() }, resourceReferenceSeeds: references), Is.True, issue?.ToString());
        return manifest!;
    }

    private void WithResources(Action<KsProcgenPrototypeManifestSystem, ResourceRoot, string> action)
    {
        var prefix = "/KsProcgenResources/" + Guid.NewGuid().ToString("N");
        var root = new ResourceRoot();
        Pair.Server.ResolveDependency<IResourceManager>().AddRoot(new(prefix), root);
        try { action(Pair.Server.System<KsProcgenPrototypeManifestSystem>(), root, prefix); }
        finally { root.Available = false; root.Files.Clear(); root.Streams.Clear(); }
    }

    private static void AddRsi(ResourceRoot root)
    {
        root.Files["test.rsi/meta.json"] = Encoding.UTF8.GetBytes(Metadata);
        root.Files["test.rsi/idle.png"] = (byte[]) Pixel.Clone();
    }

    private static byte[] CompiledRsi(string json)
    {
        // Real PNG text chunk, without adding a second image library to the test project.
        var text = Encoding.UTF8.GetBytes("robusttoolbox_rsic_meta\0" + json);
        byte[] payload = [.. Encoding.ASCII.GetBytes("tEXt"), .. text];
        var crc = uint.MaxValue;
        foreach (var value in payload)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0 : 0xedb88320u);
        }
        var length = new byte[4]; var checksum = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, text.Length);
        BinaryPrimitives.WriteUInt32BigEndian(checksum, ~crc);
        return [.. Pixel[..^12], .. length, .. payload, .. checksum, .. Pixel[^12..]];
    }

    private sealed class ResourceRoot : IContentRoot
    {
        public readonly Dictionary<string, byte[]> Files = new(StringComparer.Ordinal);
        public readonly List<ResourceStream> Streams = [];
        public bool Available = true;
        public bool ThrowOnRead;
        public int ChunkSize = int.MaxValue;
        public ResPath[]? Listing;
        public void Mount() { }
        public bool FileExists(ResPath path) => Available && Files.ContainsKey(path.CanonPath);
        public IEnumerable<ResPath> FindFiles(ResPath path) => !Available ? [] : Listing ?? Files.Keys
            .Where(key => key.StartsWith(path.CanonPath, StringComparison.Ordinal)).Select(key => new ResPath(key)).ToArray();
        public IEnumerable<string> GetRelativeFilePaths() => Available ? Files.Keys : [];
        public bool TryGetFile(ResPath path, [NotNullWhen(true)] out Stream? stream)
        {
            stream = null;
            if (!Available || !Files.TryGetValue(path.CanonPath, out var bytes)) return false;
            var resourceStream = new ResourceStream(this, bytes); Streams.Add(resourceStream); stream = resourceStream;
            return true;
        }
    }

    private sealed class ResourceStream(ResourceRoot owner, byte[] bytes) : Stream
    {
        private readonly MemoryStream _stream = new(bytes, writable: false);
        public int BytesRead;
        public bool WasDisposed;
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (owner.ThrowOnRead) throw new IOException("Deliberate resource read failure.");
            var read = _stream.Read(buffer, offset, Math.Min(count, owner.ChunkSize)); BytesRead += read; return read;
        }
        protected override void Dispose(bool disposing) { WasDisposed = true; if (disposing) _stream.Dispose(); base.Dispose(disposing); }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
