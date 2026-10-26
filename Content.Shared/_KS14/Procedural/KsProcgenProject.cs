using System.Linq;
using Content.Shared.Atmos.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.Procedural;

[DataDefinition]
public sealed partial class KsProcgenProjectAreaSpec
{
    [DataField(required: true)] public KsProcgenRequest Request = new();
    [DataField(required: true)] public string Theme = string.Empty;
    [DataField] public string? Library;
}

/// <summary>Explicit project data only; no live UIDs, filesystem destinations or component payloads.</summary>
[DataDefinition]
public sealed partial class KsProcgenProjectSpec
{
    [DataField] public int SchemaVersion = 1;
    [DataField(required: true)] public string DocumentId = string.Empty;
    [DataField] public int Revision;
    [DataField] public KsProcgenContentDraftSpec? Content;
    [DataField] public KsProcgenUtilityDraftSpec? Utilities;
    [DataField] public KsProcgenLayoutDraftSpec? Layouts;
    [DataField] public Dictionary<string, KsProcgenProjectAreaSpec> Areas = new(StringComparer.Ordinal);
}

public sealed record KsProcgenProjectArea(string Id, KsProcgenRequestSnapshot Request,
    KsProcgenThemeCatalog Content, KsProcgenCompiledLibrary? Library)
{
    public KsProcgenThemeCatalog? UtilityContent { get; init; }
}

/// <summary>
/// Immutable planning project with validated area catalogs. Imported theme/pack/assembly/profile bodies
/// travel with it; entity components, actual room maps and live context still need the R15 manifest.
/// Replacement is a value operation, not a server writer lease, persistence service or permission.
/// </summary>
public sealed class KsProcgenProject
{
    public const int MaximumAreas = 64;
    public const int MaximumAssets = 256;
    public const int MaximumAreaCells = 65_536;
    private readonly KsProcgenContentDraft _content;
    private readonly KsProcgenUtilityDraft _utilities;
    private readonly KsProcgenLayoutDraft? _layouts;

    private KsProcgenProject(string documentId, int revision, KsProcgenContentDraft content,
        KsProcgenUtilityDraft utilities, KsProcgenLayoutDraft? layouts, IReadOnlyList<KsProcgenProjectArea> areas)
    {
        DocumentId = documentId; Revision = revision; _content = content; _utilities = utilities; _layouts = layouts; Areas = areas;
        var hash = KsProcgenStableHash.Create(); hash.AddString("ks-procgen-project-v1");
        Add(ref hash, content.DeclarationHash); Add(ref hash, utilities.ContentHash);
        hash.AddInt(layouts == null ? 0 : 1); if (layouts != null) Add(ref hash, layouts.DeclarationHash);
        hash.AddInt(areas.Count);
        foreach (var area in areas)
        {
            hash.AddString(area.Id); Add(ref hash, area.Request.DeclarationHash); Add(ref hash, area.Content.DeclarationHash);
            hash.AddInt(area.Library == null ? 0 : 1); if (area.Library != null) hash.AddString(area.Library.Id);
        }
        DeclarationHash = hash.Value;
    }

    public int SchemaVersion => 1;
    public string DocumentId { get; }
    public int Revision { get; }
    public ulong DeclarationHash { get; }
    public IReadOnlyList<KsProcgenProjectArea> Areas { get; }
    public IReadOnlyList<KsProcgenLayoutRoomBinding> RoomBindings => _layouts?.RoomBindings ?? [];
    public IReadOnlyList<KsProcgenRoomSourceReference> RoomSources => _layouts?.RoomSources ?? [];
    public bool HasUnverifiedRoomSources => _layouts?.HasUnverifiedRoomSources == true;

    public bool TryArea(string id, out KsProcgenProjectArea? area)
    { area = Areas.FirstOrDefault(item => item.Id == id); return area != null; }

    public KsProcgenProjectSpec CopySpec()
    {
        var content = _content.CopySpec(); content.Revision = Revision;
        var utilities = _utilities.CopySpec(); utilities.Revision = Revision;
        var layouts = _layouts?.CopySpec(); if (layouts != null) layouts.Revision = Revision;
        return new()
        {
            DocumentId = DocumentId, Revision = Revision, Content = content, Utilities = utilities, Layouts = layouts,
            Areas = Areas.ToDictionary(area => area.Id, area => new KsProcgenProjectAreaSpec
                { Request = area.Request.CopyRequest(), Theme = area.Content.Theme.Id, Library = area.Library?.Id }, StringComparer.Ordinal),
        };
    }

    public bool TryRevise(IPrototypeManager manager, KsProcgenProjectSpec replacement, out KsProcgenProject? revised, out KsProcgenIssue? issue)
    {
        revised = null;
        if (replacement == null || replacement.DocumentId != DocumentId || replacement.Revision != Revision || Revision == int.MaxValue)
        { issue = new("ProjectRevisionConflict", "Replacement must target this document and its current incrementable revision."); return false; }
        if (!TryCapture(manager, replacement, out var validated, out issue)) return false;
        revised = new(DocumentId, Revision + 1, validated!._content, validated._utilities, validated._layouts, validated.Areas);
        return true;
    }

    public static bool TryCapture(IPrototypeManager manager, KsProcgenProjectSpec spec, out KsProcgenProject? project, out KsProcgenIssue? issue)
    {
        project = null; issue = null;
        if (spec == null || spec.SchemaVersion != 1)
        { issue = new("UnsupportedProjectSchema", "Planning project schema version 1 is required."); return false; }
        if (manager == null || !KsProcgenAreaNormalizer.ValidId(spec.DocumentId) || spec.Revision < 0 || spec.Areas == null || spec.Areas.Count > MaximumAreas ||
            spec.Content != null && (spec.Content.DocumentId != spec.DocumentId || spec.Content.Revision != spec.Revision) ||
            spec.Utilities != null && (spec.Utilities.DocumentId != spec.DocumentId || spec.Utilities.Revision != spec.Revision) ||
            spec.Layouts != null && (spec.Layouts.DocumentId != spec.DocumentId || spec.Layouts.Revision != spec.Revision))
        { issue = new("InvalidProject", "Project metadata, child revisions and bounded area collections must agree."); return false; }
        KsProcgenContentDraft? content = null;
        KsProcgenUtilityDraft? utilities = null;
        KsProcgenLayoutDraft? layouts = null;
        if (spec.Content != null && !KsProcgenContentDraft.TrySnapshot(spec.Content, out content, out issue)) return false;
        if (spec.Utilities != null && !KsProcgenUtilityDraft.TrySnapshot(spec.Utilities,
                id => manager.HasIndex<GasPrototype>(id), id => manager.HasIndex<EntityPrototype>(id), out utilities, out issue)) return false;
        if (spec.Layouts != null && !KsProcgenLayoutDraft.TrySnapshot(spec.Layouts, out layouts, out issue)) return false;
        if (spec.Areas.Any(pair => !KsProcgenAreaNormalizer.ValidId(pair.Key) || pair.Value == null || pair.Value.Request == null ||
                pair.Value.Request.RequestId != pair.Key || !KsProcgenAreaNormalizer.ValidId(pair.Value.Theme) ||
                pair.Value.Library != null && !KsProcgenAreaNormalizer.ValidId(pair.Value.Library)))
        { issue = new("InvalidProjectArea", "Area keys must match request IDs and use explicit theme/library references."); return false; }
        var referencedThemes = spec.Areas.Values.Select(area => area.Theme).ToHashSet(StringComparer.Ordinal);
        var closetThemes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var area in spec.Areas.Values)
        {
            if (!KsProcgenElectricalProfiles.TryResolveSelection(manager, area.Request.ElectricalProfile, area.Request.Electrical,
                    null, out var electrical, out issue, draft: utilities)) return false;
            if (electrical?.Distribution is { } distribution)
            { referencedThemes.Add(distribution.ClosetTheme); closetThemes.Add(distribution.ClosetTheme); }
        }
        if (!KsProcgenProjectAssetCollector.TryCollect(manager, spec.DocumentId, spec.Revision, content, utilities,
                referencedThemes, out content, out utilities, out issue)) return false;
        foreach (var electrical in utilities!.Electrical)
            if (electrical.Profile.Distribution is { } distribution) closetThemes.Add(distribution.ClosetTheme);
        var utilityCatalogs = new Dictionary<string, KsProcgenThemeCatalog>(StringComparer.Ordinal);
        foreach (var closetTheme in closetThemes.OrderBy(id => id, StringComparer.Ordinal))
        {
            if (!KsProcgenThemeCatalog.TryCapture(manager, closetTheme, out var utilityCatalog, out issue, draft: utilities, assets: content)) return false;
            utilityCatalogs.Add(closetTheme, utilityCatalog!);
        }
        var assetCount = content!.AssetCount + utilities!.Atmosphere.Count + utilities.Electrical.Count + (layouts?.AssetCount ?? 0);
        if (assetCount > MaximumAssets)
        { issue = new("ProjectAssetBudget", "Combined project assets exceed 256."); return false; }
        var catalogs = new Dictionary<string, KsProcgenThemeCatalog>(StringComparer.Ordinal);
        var areas = new List<KsProcgenProjectArea>();
        var cells = 0;
        foreach (var (id, area) in spec.Areas.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (!catalogs.TryGetValue(area.Theme, out var catalog))
            {
                if (!KsProcgenThemeCatalog.TryCapture(manager, area.Theme, out catalog, out issue, draft: utilities, assets: content)) return false;
                catalogs.Add(area.Theme, catalog!);
            }
            if (!KsProcgenRequestSnapshot.TryCapture(manager, area.Request, out var request, out issue, draft: utilities)) return false;
            var canonical = request!.CopyRequest();
            var areaCells = canonical.Shape.Cells.Count + canonical.Shape.EnvelopeCells.Count + canonical.Shape.VoidCells.Count;
            if (areaCells > MaximumAreaCells - cells)
            { issue = new("ProjectAreaCellBudget", "Combined normalized area declarations exceed 65,536 cells."); return false; }
            cells += areaCells;
            KsProcgenCompiledLibrary? library = null;
            if (area.Library != null && (layouts == null || !layouts.TryLibrary(area.Library, out library)))
            { issue = new("UnknownProjectLibrary", $"Area {id} references unknown library {area.Library}."); return false; }
            if (canonical.Mode == KsProcgenMode.Procedural && library != null || canonical.Mode != KsProcgenMode.Procedural && library == null)
            { issue = new("ProjectModeLibraryMismatch", "Procedural areas omit libraries; prefab/hybrid areas require an explicit library."); return false; }
            var effectiveElectrical = request.Electrical ?? catalog!.Theme.Electrical;
            var utilityContent = effectiveElectrical?.Distribution is { } distribution ? utilityCatalogs[distribution.ClosetTheme] : null;
            areas.Add(new(id, request, catalog!, library) { UtilityContent = utilityContent });
        }
        project = new(spec.DocumentId, spec.Revision, content, utilities, layouts, areas.ToReadOnlyList());
        return true;
    }

    private static void Add(ref KsProcgenStableHash hash, ulong value)
    { hash.AddInt(unchecked((int) value)); hash.AddInt(unchecked((int) (value >> 32))); }
}

/// <summary>Imports only known authoring grammar, retaining private copies rather than prototype aliases.</summary>
internal static class KsProcgenProjectAssetCollector
{
    internal static bool TryCollect(IPrototypeManager manager, string documentId, int revision,
        KsProcgenContentDraft? initialContent, KsProcgenUtilityDraft? initialUtilities, IEnumerable<string> themes,
        out KsProcgenContentDraft? content, out KsProcgenUtilityDraft? utilities, out KsProcgenIssue? issue)
    {
        content = null; utilities = null; issue = null;
        var assets = initialContent?.CopySpec() ?? new() { DocumentId = documentId, Revision = revision };
        var profiles = initialUtilities?.CopySpec() ?? new() { DocumentId = documentId, Revision = revision };
        var source = initialContent?.Source(manager) ?? new KsProcgenContentSource(manager);
        var pending = new Queue<string>(themes.Distinct(StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal));
        foreach (var electrical in initialUtilities?.Electrical ?? [])
            if (electrical.Profile.Distribution is { } distribution) pending.Enqueue(distribution.ClosetTheme);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (pending.TryDequeue(out var id))
        {
            if (!seen.Add(id)) continue;
            if (seen.Count > KsProcgenContentDraft.MaximumAssets)
            { issue = new("ProjectThemeGraphBudget", "Referenced theme graph exceeds 128 assets."); return false; }
            var theme = source.Theme(id);
            if (theme == null) { issue = new("UnknownProjectTheme", $"Theme {id} cannot be captured."); return false; }
            if (theme.TilePacks?.Count > 64 || theme.WallPacks?.Count > 64 || theme.LightingPacks?.Count > 64 || theme.EntityPacks?.Count > 64)
            { issue = new("ProjectContentReferenceBudget", "Referenced theme pools exceed 64 references."); return false; }
            assets.Themes.TryAdd(id, theme);
            if (!string.IsNullOrWhiteSpace(theme.Parent)) pending.Enqueue(theme.Parent);
            if (!string.IsNullOrWhiteSpace(theme.FallbackTheme)) pending.Enqueue(theme.FallbackTheme);
            if (theme.AtmosphereProfile is { } gasId)
            {
                if (!KsProcgenAtmosphereProfiles.TryResolve(manager, gasId, out var gas, out issue, draft: initialUtilities)) return false;
                profiles.Atmosphere.TryAdd(gasId, gas!.CopySpec());
            }
            if (theme.ElectricalProfile is { } powerId)
            {
                if (!KsProcgenElectricalProfiles.TryResolve(manager, powerId, out var power, out issue, draft: initialUtilities)) return false;
                profiles.Electrical.TryAdd(powerId, power!.CopySpec());
                if (power.Distribution is { } distribution) pending.Enqueue(distribution.ClosetTheme);
            }
            foreach (var reference in theme.TilePacks ?? [])
            {
                if (reference == null || !KsProcgenAreaNormalizer.ValidId(reference.Pack) || source.Tile(reference.Pack) is not { } pack)
                { issue = new("UnknownProjectTilePack", "Referenced tile pack cannot be captured."); return false; }
                assets.Tiles.TryAdd(reference.Pack, pack);
            }
            foreach (var reference in theme.WallPacks ?? [])
            {
                if (reference == null || !KsProcgenAreaNormalizer.ValidId(reference.Pack) || source.Wall(reference.Pack) is not { } pack)
                { issue = new("UnknownProjectWallPack", "Referenced wall pack cannot be captured."); return false; }
                assets.Walls.TryAdd(reference.Pack, pack);
            }
            foreach (var reference in theme.LightingPacks ?? [])
            {
                if (reference == null || !KsProcgenAreaNormalizer.ValidId(reference.Pack) || source.Light(reference.Pack) is not { } pack)
                { issue = new("UnknownProjectLightPack", "Referenced lighting pack cannot be captured."); return false; }
                assets.Lights.TryAdd(reference.Pack, pack);
            }
            foreach (var reference in theme.EntityPacks ?? [])
            {
                if (reference == null || !KsProcgenAreaNormalizer.ValidId(reference.Pack) || source.Entity(reference.Pack) is not { } pack || pack.Assemblies == null || pack.Assemblies.Count > 64)
                { issue = new("UnknownProjectEntityPack", "Referenced entity pack cannot be captured."); return false; }
                assets.Entities.TryAdd(reference.Pack, pack);
                foreach (var assemblyReference in pack.Assemblies)
                {
                    if (assemblyReference == null || !KsProcgenAreaNormalizer.ValidId(assemblyReference.Assembly) || source.Assembly(assemblyReference.Assembly) is not { } assembly)
                    { issue = new("UnknownProjectAssembly", "Referenced assembly cannot be captured."); return false; }
                    assets.Assemblies.TryAdd(assemblyReference.Assembly, assembly);
                }
            }
            if (assets.Themes.Count + assets.Tiles.Count + assets.Walls.Count + assets.Lights.Count + assets.Entities.Count + assets.Assemblies.Count > KsProcgenContentDraft.MaximumAssets)
            { issue = new("ProjectContentAssetBudget", "Imported interior assets exceed 128."); return false; }
        }
        if (!KsProcgenContentDraft.TrySnapshot(assets, out content, out issue) ||
            !KsProcgenUtilityDraft.TrySnapshot(profiles, id => manager.HasIndex<GasPrototype>(id),
                id => manager.HasIndex<EntityPrototype>(id), out utilities, out issue))
        { content = null; utilities = null; return false; }
        return true;
    }
}
