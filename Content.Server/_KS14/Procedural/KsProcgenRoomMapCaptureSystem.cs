using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Content.Shared._KS14.Procedural;
using Robust.Shared.ContentPack;
using Robust.Shared.Serialization.Manager;

namespace Content.Server._KS14.Procedural;

/// <summary>Read-only static content capture. Source bindings are trusted server data, never editor paths.</summary>
public sealed partial class KsProcgenRoomMapCaptureSystem : EntitySystem
{
    [Dependency] private IResourceManager _resources = default!;
    [Dependency] private ISerializationManager _serialization = default!;
    [Dependency] private KsProcgenPrototypeManifestSystem _definitions = default!;

    public bool TryCapture(KsProcgenProject project, IReadOnlyDictionary<string, string> sourceBindings,
        out KsProcgenRoomMapManifest? manifest, out KsProcgenIssue? issue,
        KsProcgenRoomMapCaptureLimits? limits = null, CancellationToken cancellationToken = default)
    {
        manifest = null; issue = null; limits ??= new();
        if (project == null || sourceBindings == null || !Valid(limits) || sourceBindings.Count > 256)
        { issue = new("InvalidRoomMapCaptureInput", "Capture requires bounded project data and explicit server content bindings."); return false; }
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sources = sourceBindings.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            if (sources.Count > 256 || sources.Any(pair => !ValidKey(pair.Key) || !ValidPath(pair.Value)))
            { issue = new("InvalidRoomMapCaptureInput", "Source bindings require bounded symbolic keys and canonical content map paths."); return false; }
            // Validate every expected reference before opening any resource. Old opaque fingerprints
            // remain editable declarations; they cannot serve as observed source integrity evidence.
            var references = project.RoomSources.Where(source => source.SourceKey != null)
                .OrderBy(source => source.RoomAssetId, StringComparer.Ordinal).ToArray();
            foreach (var source in references)
            {
                if (!sources.ContainsKey(source.SourceKey!))
                { issue = new("UnboundRoomMapSource", $"Room {source.RoomAssetId} has no server content binding."); return false; }
                if (!Fingerprint(source.ContentFingerprint, out _))
                { issue = new("UnsupportedRoomMapFingerprint", "Static room capture requires sha256:<64 hexadecimal digits>."); return false; }
            }
            var paths = sources.Where(pair => references.Any(source => source.SourceKey == pair.Key))
                .Select(pair => pair.Value).Distinct(StringComparer.Ordinal).OrderBy(path => path, StringComparer.Ordinal).ToArray();
            if (paths.Length > limits.MaximumFiles)
            { issue = new("RoomMapFileBudget", "Bound map resources exceed the file allowance."); return false; }
            var projectNode = _serialization.WriteValue(project.CopySpec(), alwaysWrite: true, notNullableOverride: true);
            var projectLimits = new KsProcgenPrototypeManifestLimits
            { MaximumNodes = limits.MaximumProjectNodes, MaximumCharacters = limits.MaximumProjectCharacters, MaximumDepth = limits.MaximumProjectDepth };
            if (!KsProcgenPrototypeManifestSystem.TryCanonicalBody(projectNode, projectLimits,
                    out var canonicalProject, out var code, out var projectNodes, out var projectCharacters))
            { issue = new(code!, "Canonical project declarations exceed the structural allowance."); return false; }
            var maps = new List<KsProcgenCapturedRoomMap>();
            var bytesRead = 0; var nodes = 0; var entities = 0;
            var decodeWork = new DecodeWork(limits);
            foreach (var path in paths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!_resources.TryContentFileRead(path, out var stream))
                { issue = new("MissingRoomMapSource", $"Bound content resource {path} is unavailable."); return false; }
                byte[] bytes;
                using (stream)
                using (var output = new MemoryStream())
                {
                    var allowance = Math.Min(limits.MaximumFileBytes, limits.MaximumTotalBytes - bytesRead);
                    var buffer = new byte[8192];
                    while (true)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var read = stream.Read(buffer, 0, Math.Min(buffer.Length, allowance - (int) output.Length + 1));
                        if (read == 0) break;
                        bytesRead += read;
                        if (output.Length + read > allowance)
                        { issue = new(bytesRead > limits.MaximumTotalBytes ? "RoomMapTotalByteBudget" : "RoomMapFileByteBudget",
                            "Map resource bytes exceed the capture allowance."); return false; }
                        output.Write(buffer, 0, read);
                    }
                    bytes = output.ToArray();
                }
                var hash = Convert.ToHexString(SHA256.HashData(bytes));
                foreach (var source in references.Where(source => sources[source.SourceKey!] == path))
                {
                    Fingerprint(source.ContentFingerprint, out var expected);
                    if (!string.Equals(hash, expected, StringComparison.OrdinalIgnoreCase))
                    { issue = new("RoomMapFingerprintMismatch", $"Room {source.RoomAssetId} does not match its expected map bytes."); return false; }
                }
                if (!TryInspect(path, bytes, hash, limits, cancellationToken, ref nodes, ref entities, decodeWork, out var map, out issue)) return false;
                maps.Add(map!);
            }
            var entityIds = project.Areas.SelectMany(area => KsProcgenPrototypeManifestSystem.CandidateEntityIds(area.Content))
                .Concat(maps.SelectMany(map => map.EntityReferences));
            var tileIds = project.Areas.SelectMany(area => KsProcgenPrototypeManifestSystem.CandidateTileIds(area.Content))
                .Concat(maps.SelectMany(map => map.TileReferences));
            if (!_definitions.TryCapture(entityIds, tileIds, out var definitions, out issue, limits: limits.Definitions)) return false;
            cancellationToken.ThrowIfCancellationRequested();
            var byPath = maps.ToDictionary(map => map.ResourcePath, StringComparer.Ordinal);
            var bindings = references.Select(source => new KsProcgenCapturedRoomMapBinding(source.RoomAssetId,
                source.SourceKey!, sources[source.SourceKey!], byPath[sources[source.SourceKey!]].Sha256)).ToArray();
            var identity = new StringBuilder("ks-procgen-room-map-manifest-v8;");
            Add(canonicalProject!); Add(definitions!.ContentHash);
            foreach (var binding in bindings) { Add(binding.RoomAssetId); Add(binding.SourceKey); Add(binding.ResourcePath); Add(binding.Sha256); }
            var contentHash = Convert.ToHexString(SHA256.HashData(new UTF8Encoding(false, true).GetBytes(identity.ToString())));
            manifest = new(project, canonicalProject!, maps, bindings, definitions, limits, nodes, projectNodes, projectCharacters, contentHash);
            return true;
            void Add(string value) => identity.Append(value.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(value);
        }
        catch (OperationCanceledException)
        { issue = new("RoomMapCaptureCancelled", "Static room-map capture was cancelled."); return false; }
        catch (Exception exception)
        {
            Log.Debug($"Static room-map capture failed: {exception}");
            issue = new("UnsupportedRoomMapSource", "The bound map could not be captured as supported static content."); return false;
        }
    }

    public bool Matches(KsProcgenRoomMapManifest captured, KsProcgenProject project,
        IReadOnlyDictionary<string, string> sourceBindings, out KsProcgenIssue? issue, CancellationToken cancellationToken = default)
    {
        if (!TryCapture(project, sourceBindings, out var fresh, out issue, limits: captured.Limits, cancellationToken: cancellationToken)) return false;
        if (captured.CanonicalProject == fresh!.CanonicalProject && captured.Bindings.SequenceEqual(fresh.Bindings) &&
            captured.Definitions.Roots.SequenceEqual(fresh.Definitions.Roots) &&
            captured.Definitions.EntityReferenceSeeds.SequenceEqual(fresh.Definitions.EntityReferenceSeeds) &&
            captured.Definitions.Bodies.SequenceEqual(fresh.Definitions.Bodies) && captured.Maps.Count == fresh.Maps.Count &&
            captured.Maps.Zip(fresh.Maps).All(pair => pair.First.ResourcePath == pair.Second.ResourcePath && pair.First.SameBytes(pair.Second)))
            return true;
        issue = new("RoomMapManifestChanged", "Project declarations, source bindings, map bytes or direct definitions changed."); return false;
    }

    private static bool Valid(KsProcgenRoomMapCaptureLimits limits) =>
        limits.MaximumFiles is >= 1 and <= 256 && limits.MaximumFileBytes is >= 1 and <= 16_777_216 &&
        limits.MaximumTotalBytes is >= 1 and <= 67_108_864 && limits.MaximumYamlNodes is >= 1 and <= 1_048_576 &&
        limits.MaximumYamlDepth is >= 1 and <= 128 && limits.MaximumScalarCharacters is >= 1 and <= 4_194_304 &&
        limits.MaximumEntities is >= 1 and <= 131_072 && limits.MaximumProjectNodes is >= 1 and <= 1_048_576 &&
        limits.MaximumProjectCharacters is >= 1 and <= 16_777_216 && limits.MaximumProjectDepth is >= 1 and <= 128 &&
        limits.MaximumChunks is >= 1 and <= 4096 && limits.MaximumDecodedTiles is >= 1 and <= 1_048_576 &&
        limits.MaximumDecodedBytes is >= 1 and <= 8_388_608 && limits.Definitions != null &&
        KsProcgenPrototypeManifestSystem.ValidLimits(limits.Definitions);

    private static bool ValidKey(string value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 128 &&
        value.All(character => character is >= '!' and <= '~');

    private static bool ValidPath(string? path) => path != null && path.Length <= 512 && path.StartsWith("/Maps/", StringComparison.Ordinal) &&
        (path.EndsWith(".yml", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase)) &&
        !path.Any(character => character is < ' ' or > '~' or '\\' or ':') &&
        path[1..].Split('/').All(segment => segment.Length > 0 && segment is not "." and not "..");

    private static bool Fingerprint(string? fingerprint, out string? hash)
    {
        hash = null;
        if (fingerprint == null || !fingerprint.StartsWith("sha256:", StringComparison.Ordinal) || fingerprint.Length != 71 ||
            fingerprint[7..].Any(character => !Uri.IsHexDigit(character))) return false;
        hash = fingerprint[7..]; return true;
    }
}
