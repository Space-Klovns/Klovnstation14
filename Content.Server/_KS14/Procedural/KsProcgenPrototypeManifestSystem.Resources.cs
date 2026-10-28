using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using Content.Shared._KS14.Procedural;
using Robust.Shared.ContentPack;
using Robust.Shared.Utility;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;

namespace Content.Server._KS14.Procedural;

public sealed partial class KsProcgenPrototypeManifestSystem
{
    [Dependency] private IResourceManager _resources = default!;

    private sealed class ResourceWork(KsProcgenResourceCaptureLimits limits)
    {
        public readonly SortedSet<KsProcgenResourceReference> References = new(
            Comparer<KsProcgenResourceReference>.Create((first, second) =>
            {
                var path = StringComparer.Ordinal.Compare(first.Path, second.Path);
                if (path != 0) return path;
                var kind = first.Kind.CompareTo(second.Kind);
                return kind != 0 ? kind : StringComparer.Ordinal.Compare(first.State, second.State);
            }));
        public KsProcgenIssue? Issue;
        public int Occurrences;
        public void Observe(KsProcgenResourceReference reference)
        {
            if (Issue != null) return;
            if (Occurrences++ >= limits.MaximumReferences)
            { Issue = new("PrototypeResourceReferenceBudget", "Resource references exceed the shared occurrence allowance."); return; }
            if (!TryResourcePath(reference.Path, out var path) ||
                reference.Kind is not (KsProcgenResourceReferenceKind.Path or KsProcgenResourceReferenceKind.Rsi) ||
                reference.State is { Length: > 512 } || reference.State?.Any(char.IsControl) == true ||
                reference.Kind == KsProcgenResourceReferenceKind.Path && reference.State != null)
            {
                var summary = reference.Path is { Length: > 128 } ? reference.Path[..128] : reference.Path;
                Issue = new("InvalidPrototypeResourceReference",
                    $"Resources need bounded canonical content paths and state IDs: {reference.Kind}/{summary}");
                return;
            }
            References.Add(reference with { Path = path! });
        }
    }

    private bool TryCaptureResources(ResourceWork work, KsProcgenResourceCaptureLimits limits,
        out IReadOnlyList<KsProcgenCapturedResource>? resources, out KsProcgenIssue? issue)
    {
        resources = null;
        try { return TryCaptureResourceFiles(work, limits, out resources, out issue); }
        catch (Exception exception)
        {
            // Fail atomically, without reporting an unrelated prototype serialization error.
            resources = null;
            issue = new("InvalidPrototypeResource", $"Content resource capture failed: {exception.GetType().Name}.");
            Log.Debug($"Content resource capture failed: {exception}");
            return false;
        }
    }

    private bool TryCaptureResourceFiles(ResourceWork work, KsProcgenResourceCaptureLimits limits,
        out IReadOnlyList<KsProcgenCapturedResource>? resources, out KsProcgenIssue? issue)
    {
        resources = null; issue = work.Issue;
        if (issue != null) return false;
        var files = new SortedDictionary<string, KsProcgenCapturedResource>(StringComparer.Ordinal);
        var rsiStates = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var totalBytes = 0; var totalStates = 0; var directoryEntries = 0;
        KsProcgenIssue? failure = null;
        bool Read(string path, out KsProcgenCapturedResource? file)
        {
            if (files.TryGetValue(path, out file)) return true;
            file = null;
            if (files.Count >= limits.MaximumFiles)
            { failure = new("PrototypeResourceFileBudget", "Resource files exceed the shared count allowance."); return false; }
            if (!_resources.TryContentFileRead(new ResPath(path), out var input))
            { failure = new("MissingPrototypeResource", $"Referenced content resource is unavailable: {path}"); return false; }
            using (input)
            using (var output = new MemoryStream())
            {
                var allowance = Math.Min(limits.MaximumFileBytes, limits.MaximumTotalBytes - totalBytes);
                var buffer = new byte[8192];
                while (true)
                {
                    var count = input.Read(buffer, 0, Math.Min(buffer.Length, allowance - (int) output.Length + 1));
                    if (count == 0) break;
                    if (output.Length + count > allowance)
                    { failure = new(allowance < limits.MaximumFileBytes ? "PrototypeResourceTotalByteBudget" : "PrototypeResourceFileByteBudget",
                        "Resource bytes exceed the shared file or aggregate allowance."); return false; }
                    output.Write(buffer, 0, count);
                }
                var bytes = output.ToArray(); totalBytes += bytes.Length;
                file = new(path, bytes, Convert.ToHexString(SHA256.HashData(bytes)));
                files.Add(path, file);
            }
            return true;
        }

        foreach (var reference in work.References)
        {
            if (reference.Kind == KsProcgenResourceReferenceKind.Path)
            {
                if (reference.Path.EndsWith('/'))
                {
                    var any = false;
                    foreach (var entry in _resources.ContentFindFiles(new ResPath(reference.Path)))
                    {
                        // Charge visible entries even when overlapping directories repeat a file.
                        // VFS enumeration itself is trusted engine preprocessing, not a resumable job.
                        if (directoryEntries++ >= limits.MaximumDirectoryEntries)
                        { issue = new("PrototypeResourceDirectoryBudget", "Directory entries exceed the shared allowance."); return false; }
                        if (!TryResourcePath(entry.ToString(), out var path) || !path!.StartsWith(reference.Path, StringComparison.Ordinal))
                        { issue = new("InvalidPrototypeResourceDirectory", "Directory members must remain inside the captured content root."); return false; }
                        any = true;
                        if (!Read(path, out _)) { issue = failure; return false; }
                    }
                    if (!any) { issue = new("MissingPrototypeResource", $"Resource directory is empty or unavailable: {reference.Path}"); return false; }
                }
                else if (!Read(reference.Path, out _)) { issue = failure; return false; }
                continue;
            }
            if (!rsiStates.TryGetValue(reference.Path, out var states))
            {
                var metadataPath = reference.Path + "/meta.json";
                KsProcgenCapturedResource? metadata;
                string json;
                if (_resources.ContentFileExists(new ResPath(metadataPath)))
                {
                    if (!Read(metadataPath, out metadata)) { issue = failure; return false; }
                    using var stream = metadata!.OpenRead(); using var reader = new StreamReader(stream);
                    json = reader.ReadToEnd();
                }
                else
                {
                    var compiledPath = new ResPath(reference.Path).WithExtension("rsic").ToString();
                    if (!Read(compiledPath, out metadata)) { issue = failure; return false; }
                    // Read metadata only; pixel/atlas decoding belongs to native client validation.
                    using var stream = metadata!.OpenRead();
                    var image = Image.Identify(stream);
                    var text = image.Metadata.GetPngMetadata().TextData.FirstOrDefault(item => item.Keyword == "robusttoolbox_rsic_meta");
                    if (text.Keyword == null) { issue = new("InvalidPrototypeRsi", "Compiled RSI has no native metadata entry."); return false; }
                    json = text.Value;
                }
                if (json.Length > limits.MaximumFileBytes)
                { issue = new("PrototypeResourceMetadataBudget", "Expanded RSI metadata exceeds its character allowance."); return false; }
                using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = limits.MaximumJsonDepth });
                var root = document.RootElement;
                if (!root.TryGetProperty("version", out var version) || version.GetInt32() != 1 ||
                    !root.TryGetProperty("size", out var size) || size.GetProperty("x").GetInt32() < 1 || size.GetProperty("y").GetInt32() < 1 ||
                    !root.TryGetProperty("states", out var entries) || entries.ValueKind != JsonValueKind.Array)
                { issue = new("InvalidPrototypeRsi", "RSI metadata needs native version one, positive dimensions and a state array."); return false; }
                states = new(StringComparer.Ordinal);
                foreach (var entry in entries.EnumerateArray())
                {
                    if (totalStates++ >= limits.MaximumRsiStates)
                    { issue = new("PrototypeResourceRsiStateBudget", "RSI states exceed the shared allowance."); return false; }
                    var name = entry.GetProperty("name").GetString();
                    if (string.IsNullOrEmpty(name) || name.Length > 512 || !states.Add(name))
                    { issue = new("InvalidPrototypeRsi", "RSI state IDs must be bounded and unique."); return false; }
                    if (metadata!.Path == metadataPath)
                    {
                        var framePath = reference.Path + "/" + name + ".png";
                        if (!TryResourcePath(framePath, out var canonical) || !canonical!.StartsWith(reference.Path + "/", StringComparison.Ordinal))
                        { issue = new("InvalidPrototypeRsi", "RSI frame paths must stay inside their resource directory."); return false; }
                        if (!Read(canonical, out _)) { issue = failure; return false; }
                    }
                }
                rsiStates.Add(reference.Path, states);
            }
            if (reference.State != null && !states.Contains(reference.State))
            { issue = new("MissingPrototypeRsiState", $"RSI {reference.Path} does not define state {reference.State}."); return false; }
        }
        resources = Array.AsReadOnly(files.Values.ToArray());
        return true;
    }

    private static bool TryResourcePath(string? value, out string? path)
    {
        path = null;
        if (string.IsNullOrWhiteSpace(value) || value.Length > 1024 || value.Any(character => char.IsControl(character) || character is '\\' or ':')) return false;
        var rooted = value.StartsWith('/') ? value : value.Split('/')[0].Equals("Textures", StringComparison.OrdinalIgnoreCase) ? "/" + value : "/Textures/" + value;
        if (rooted.EndsWith("//", StringComparison.Ordinal)) return false;
        var segments = rooted[1..].TrimEnd('/').Split('/');
        if (segments.Any(segment => segment.Length == 0 || segment is "." or "..")) return false;
        path = rooted;
        return true;
    }

    internal static bool ValidResourceLimits(KsProcgenResourceCaptureLimits limits) =>
        limits.MaximumReferences is >= 1 and <= 262_144 && limits.MaximumFiles is >= 1 and <= 16_384 &&
        limits.MaximumDirectoryEntries is >= 1 and <= 65_536 &&
        limits.MaximumFileBytes is >= 1 and <= 67_108_864 && limits.MaximumTotalBytes is >= 1 and <= 536_870_912 &&
        limits.MaximumRsiStates is >= 1 and <= 65_536 && limits.MaximumJsonDepth is >= 1 and <= 64;
}
