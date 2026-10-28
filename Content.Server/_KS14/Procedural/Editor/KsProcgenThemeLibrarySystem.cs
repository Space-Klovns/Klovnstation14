using System.Globalization;
using System.IO;
using System.Text.Json;
using Content.Shared._KS14.Procedural;
using Content.Shared._KS14.Procedural.Editor;
using Robust.Shared.ContentPack;
using Robust.Shared.Utility;

namespace Content.Server._KS14.Procedural.Editor;

/// <summary>Persistent, immutable room-theme revisions consumed by the normal generator catalog API.</summary>
public sealed partial class KsProcgenThemeLibrarySystem : EntitySystem
{
    [Dependency] private IResourceManager _resources = default!;
    private static readonly ResPath Directory = new("/Procgen/RoomThemes");
    private static readonly JsonSerializerOptions Options = new() { IncludeFields = true, MaxDepth = 8 };
    private const int MaximumFileBytes = 65_536;

    private sealed class StoredTheme
    {
        public StoredTheme() { }
        public int SchemaVersion = 1;
        public Guid DocumentId;
        public int Revision;
        public KsProcgenThemeEditorValues Values = new();
    }

    public bool TrySave(Guid documentId, int revision, KsProcgenThemeEditorValues values,
        out string? key, out KsProcgenIssue? issue)
    {
        key = null;
        if (documentId == Guid.Empty || revision < 1 || values == null ||
            !values.TryBuild(documentId, revision, out var draft, out issue))
        { issue = new("InvalidThemeEditorValues", "A valid theme revision is required."); return false; }
        if (!KsProcgenThemeCatalog.TryCapture(ProtoMan, "Theme", out _, out issue, assets: draft)) return false;
        var saved = new StoredTheme { DocumentId = documentId, Revision = revision, Values = values.Copy() };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(saved, Options);
        if (bytes.Length > MaximumFileBytes)
        { issue = new("ThemeEditorFileBudget", "The saved theme exceeds the file allowance."); return false; }
        var candidate = documentId.ToString("N") + "-" + revision.ToString(CultureInfo.InvariantCulture);
        var destination = Directory / (candidate + ".json");
        var temporary = Directory / (Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            _resources.UserData.CreateDir(Directory);
            if (_resources.UserData.Exists(destination))
            { issue = new("ThemeEditorSaveFailed", "The theme revision already exists."); return false; }
            using (var output = _resources.UserData.Open(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                output.Write(bytes);
            _resources.UserData.Rename(temporary, destination);
            key = candidate;
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        { issue = new("ThemeEditorSaveFailed", "The theme revision could not be saved."); return false; }
        finally
        {
            if (_resources.UserData.Exists(temporary)) _resources.UserData.Delete(temporary);
        }
    }

    public bool TryLoad(string key, out KsProcgenThemeEditorValues? values, out KsProcgenContentDraft? draft,
        out KsProcgenIssue? issue)
    {
        values = null; draft = null;
        var parts = key?.Split('-');
        if (parts is not { Length: 2 } || !Guid.TryParseExact(parts[0], "N", out var documentId) ||
            documentId == Guid.Empty || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var revision) || revision < 1)
        { issue = new("InvalidThemeEditorKey", "A server-issued saved theme key is required."); return false; }
        try
        {
            using var input = _resources.UserData.OpenRead(Directory / (documentId.ToString("N") + "-" + revision.ToString(CultureInfo.InvariantCulture) + ".json"));
            using var buffer = new MemoryStream();
            var chunk = new byte[4096];
            while (true)
            {
                var count = input.Read(chunk, 0, Math.Min(chunk.Length, MaximumFileBytes - (int) buffer.Length + 1));
                if (count == 0) break;
                if (buffer.Length + count > MaximumFileBytes)
                { issue = new("ThemeEditorFileBudget", "The saved theme exceeds the file allowance."); return false; }
                buffer.Write(chunk, 0, count);
            }
            var saved = JsonSerializer.Deserialize<StoredTheme>(buffer.ToArray(), Options);
            if (saved == null || saved.SchemaVersion != 1 || saved.DocumentId != documentId || saved.Revision != revision || saved.Values == null)
            { issue = new("InvalidThemeEditorFile", "The saved theme identity is invalid."); return false; }
            if (!saved.Values.TryBuild(documentId, revision, out var candidateDraft, out issue) ||
                !KsProcgenThemeCatalog.TryCapture(ProtoMan, "Theme", out _, out issue, assets: candidateDraft)) return false;
            draft = candidateDraft;
            values = saved.Values.Copy();
            return true;
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        { issue = new("ThemeEditorLoadFailed", "The saved theme could not be loaded."); return false; }
    }

    public bool TryCaptureCatalog(string key, out KsProcgenThemeCatalog? catalog, out KsProcgenIssue? issue)
    {
        catalog = null;
        return TryLoad(key, out _, out var draft, out issue) &&
            KsProcgenThemeCatalog.TryCapture(ProtoMan, "Theme", out catalog, out issue, assets: draft);
    }
}
