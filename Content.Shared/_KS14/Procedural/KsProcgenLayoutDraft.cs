using System.Linq;
using Robust.Shared.Maths;

namespace Content.Shared._KS14.Procedural;

[DataDefinition]
public sealed partial class KsProcgenRoomAssetSpec
{
    [DataField] public string? SourceKey;
    [DataField] public string? ContentFingerprint;
    /// <summary>Optional map-local grid UID. Omit only when the occupied mask identifies one grid.</summary>
    [DataField] public int? SourceGridUid;
    /// <summary>Native source tile corresponding to room-local (0, 0); does not affect layout fitting.</summary>
    [DataField] public Vector2i SourceOrigin;
    [DataField] public List<Vector2i> Cells = new();
    [DataField] public List<KsProcgenFacadeSpec> ExteriorFacades = new();
    [DataField] public List<KsProcgenRoomPortSpec> Ports = new();
}

[DataDefinition]
public sealed partial class KsProcgenFacadeSpec
{
    [DataField] public Vector2i Cell;
    [DataField] public Vector2i OutwardNormal;
}

[DataDefinition]
public sealed partial class KsProcgenRoomPortSpec
{
    [DataField(required: true)] public string Id = string.Empty;
    [DataField] public Vector2i Threshold;
    [DataField] public Vector2i OutwardNormal;
}

[DataDefinition]
public sealed partial class KsProcgenLayoutMemberSpec
{
    [DataField(required: true)] public string Id = string.Empty;
    [DataField(required: true)] public string Room = string.Empty;
    [DataField] public Vector2i Origin;
    [DataField] public int QuarterTurns;
}

[DataDefinition]
public sealed partial class KsProcgenLayoutOptionSpec
{
    [DataField(required: true)] public string Id = string.Empty;
    [DataField] public List<Vector2i> ReservationCells = new();
    [DataField] public List<Vector2i> GenerateCells = new();
    [DataField] public List<KsProcgenLayoutMemberSpec> Members = new();
}

[DataDefinition]
public sealed partial class KsProcgenLayoutFamilySpec
{
    [DataField] public List<int> AllowedQuarterTurns = new() { 0, 1, 2, 3 };
    [DataField] public List<KsProcgenLayoutOptionSpec> Options = new();
}

[DataDefinition]
public sealed partial class KsProcgenLibraryRoomSpec
{
    [DataField(required: true)] public string Room = string.Empty;
    [DataField] public List<int> AllowedQuarterTurns = new() { 0, 1, 2, 3 };
}

[DataDefinition]
public sealed partial class KsProcgenRoomLibrarySpec
{
    [DataField] public List<string> Families = new();
    [DataField] public List<KsProcgenLibraryRoomSpec> Rooms = new();
}

/// <summary>Portable geometry declarations, never copied/inspected room-map contents.</summary>
[DataDefinition]
public sealed partial class KsProcgenLayoutDraftSpec
{
    [DataField] public int SchemaVersion = 2;
    [DataField(required: true)] public string DocumentId = string.Empty;
    [DataField] public int Revision;
    [DataField] public Dictionary<string, KsProcgenRoomAssetSpec> Rooms = new(StringComparer.Ordinal);
    [DataField] public Dictionary<string, KsProcgenLayoutFamilySpec> Families = new(StringComparer.Ordinal);
    [DataField] public Dictionary<string, KsProcgenRoomLibrarySpec> Libraries = new(StringComparer.Ordinal);
}

public sealed record KsProcgenCompiledLibrary(string Id, IReadOnlyList<KsProcgenLayoutFamily> Families);
public sealed record KsProcgenLayoutRoomBinding(string FamilyId, string OptionId, string MemberId,
    string RoomAssetId, Vector2i Origin, int QuarterTurns);
public sealed record KsProcgenRoomSourceReference(string RoomAssetId, string? SourceKey, string? ContentFingerprint,
    int? SourceGridUid, Vector2i SourceOrigin);

/// <summary>
/// Validated, immutable layout declarations. Family alternatives compile atomically, with local
/// member transforms; fitting still discovers global placement. Source fingerprints are authored
/// expectations only. No actual map source, hull, doors, gas, power or access is certified here.
/// </summary>
public sealed class KsProcgenLayoutDraft
{
    public const int MaximumAssets = 256;
    public const int MaximumCells = 65_536;
    public const int MaximumElements = 32_768;
    private readonly KsProcgenLayoutDraftSpec _spec;
    private KsProcgenLayoutDraft(KsProcgenLayoutDraftSpec spec, IReadOnlyList<KsProcgenCompiledLibrary> libraries,
        IReadOnlyList<KsProcgenLayoutRoomBinding> bindings, ulong hash)
    {
        _spec = spec; Libraries = libraries; RoomBindings = bindings; DeclarationHash = hash;
        RoomSources = spec.Rooms.Select(pair => new KsProcgenRoomSourceReference(pair.Key, pair.Value.SourceKey,
            pair.Value.ContentFingerprint, pair.Value.SourceGridUid, pair.Value.SourceOrigin)).ToReadOnlyList();
    }
    public int SchemaVersion => 2;
    public string DocumentId => _spec.DocumentId;
    public int Revision => _spec.Revision;
    public int AssetCount => _spec.Rooms.Count + _spec.Families.Count + _spec.Libraries.Count;
    public ulong DeclarationHash { get; }
    public IReadOnlyList<KsProcgenCompiledLibrary> Libraries { get; }
    public IReadOnlyList<KsProcgenLayoutRoomBinding> RoomBindings { get; }
    public IReadOnlyList<KsProcgenRoomSourceReference> RoomSources { get; }
    public bool HasUnverifiedRoomSources => _spec.Rooms.Values.Any(room => room.SourceKey != null);
    public KsProcgenLayoutDraftSpec CopySpec() => new Copier().Copy(_spec);
    public bool TryLibrary(string id, out KsProcgenCompiledLibrary? library)
    { library = Libraries.FirstOrDefault(item => item.Id == id); return library != null; }

    public static bool TrySnapshot(KsProcgenLayoutDraftSpec spec, out KsProcgenLayoutDraft? draft, out KsProcgenIssue? issue)
    {
        draft = null; issue = null;
        if (spec == null || spec.SchemaVersion is < 1 or > 2)
        { issue = new("UnsupportedLayoutDraftSchema", "Layout draft schema version 1 or 2 is required."); return false; }
        if (spec.SchemaVersion == 1 && spec.Rooms != null && spec.Rooms.Values.Any(room =>
                room != null && (room.SourceGridUid != null || room.SourceOrigin != Vector2i.Zero)))
        { issue = new("UnsupportedLayoutDraftSchema", "Explicit source extraction coordinates require layout schema version 2."); return false; }
        if (!KsProcgenAreaNormalizer.ValidId(spec.DocumentId) || spec.Revision < 0)
        { issue = new("InvalidLayoutDraft", "A bounded document ID and nonnegative revision are required."); return false; }
        var copier = new Copier(); var copy = copier.Copy(spec);
        if (!copier.Valid)
        { issue = new("InvalidLayoutDraftStructure", "Layout assets need bounded nonnull data, stable IDs, unique cell sets and valid transforms."); return false; }
        var rooms = new Dictionary<string, KsProcgenRoomShape>(StringComparer.Ordinal);
        foreach (var (id, room) in copy.Rooms)
        {
            var shape = new KsProcgenRoomShape(id, room.Cells,
                exteriorFacades: room.ExteriorFacades.Select(edge => new KsProcgenBoundaryEdge(edge.Cell, edge.OutwardNormal)),
                ports: room.Ports.Select(port => new KsProcgenRoomPort(port.Id, port.Threshold, port.OutwardNormal)));
            var singleton = new KsProcgenLayoutFamily(id, new[] { new KsProcgenLayoutOption("Room", room.Cells, new[] { shape }) });
            if (!singleton.TryValidate(out issue)) return false;
            rooms.Add(id, shape);
        }
        var families = new Dictionary<string, KsProcgenLayoutFamily>(StringComparer.Ordinal);
        var bindings = new List<KsProcgenLayoutRoomBinding>();
        var expandedCells = 0;
        foreach (var (id, family) in copy.Families)
        {
            var options = new List<KsProcgenLayoutOption>();
            foreach (var option in family.Options)
            {
                var members = new List<KsProcgenRoomShape>();
                foreach (var member in option.Members)
                {
                    if (!rooms.TryGetValue(member.Room, out var room))
                    { issue = new("UnknownLayoutRoom", $"Layout {id}/{option.Id} references unknown room {member.Room}."); return false; }
                    if (room.Cells.Count > MaximumCells - expandedCells)
                    { issue = new("LayoutExpansionBudget", "Expanded layout instances exceed the cumulative cell budget."); return false; }
                    expandedCells += room.Cells.Count;
                    bindings.Add(new(id, option.Id, member.Id, member.Room, member.Origin, member.QuarterTurns));
                    Vector2i Cell(Vector2i cell) => KsProcgenGeometry.TransformCell(cell, default, member.Origin, member.QuarterTurns);
                    Vector2i Normal(Vector2i cell) => KsProcgenGeometry.TransformCell(cell, default, default, member.QuarterTurns);
                    var cells = room.Cells.Select(Cell).ToArray();
                    if (cells.Any(cell => !Copier.Coordinate(cell)))
                    { issue = new("LayoutCoordinateRange", "Transformed room cells exceed the supported coordinate range."); return false; }
                    members.Add(new(member.Id, cells,
                        exteriorFacades: room.ExteriorFacades.Select(edge => new KsProcgenBoundaryEdge(Cell(edge.Cell), Normal(edge.OutwardNormal))),
                        ports: room.Ports.Select(port => new KsProcgenRoomPort(port.Id, Cell(port.Threshold), Normal(port.OutwardNormal)))));
                }
                options.Add(new(option.Id, option.ReservationCells, members, generateCells: option.GenerateCells));
            }
            var compiled = new KsProcgenLayoutFamily(id, options, allowedQuarterTurns: family.AllowedQuarterTurns);
            if (!compiled.TryValidate(out issue)) return false;
            families.Add(id, compiled);
        }
        var libraries = new List<KsProcgenCompiledLibrary>();
        foreach (var (id, library) in copy.Libraries)
        {
            var compiled = new List<KsProcgenLayoutFamily>();
            foreach (var familyId in library.Families)
            {
                if (!families.TryGetValue(familyId, out var family))
                { issue = new("UnknownLibraryFamily", $"Library {id} references unknown family {familyId}."); return false; }
                compiled.Add(family);
            }
            foreach (var reference in library.Rooms)
            {
                if (!rooms.TryGetValue(reference.Room, out var room))
                { issue = new("UnknownLibraryRoom", $"Library {id} references unknown room {reference.Room}."); return false; }
                // Separate namespaces make singleton IDs unambiguous even when a family has the same name.
                compiled.Add(new($"Room/{reference.Room}", new[] { new KsProcgenLayoutOption("Room", room.Cells, new[] { room }) },
                    allowedQuarterTurns: reference.AllowedQuarterTurns));
                bindings.Add(new($"Room/{reference.Room}", "Room", room.Id, reference.Room, default, 0));
            }
            // Authored family IDs cannot contain '/' (see the copier), reserving this namespace.
            libraries.Add(new(id, compiled.OrderBy(family => family.Id, StringComparer.Ordinal).ToReadOnlyList()));
        }
        draft = new(copy, libraries.ToReadOnlyList(), bindings.Distinct().ToReadOnlyList(), copier.Hash);
        return true;
    }

    private sealed class Copier
    {
        private KsProcgenStableHash _hash = KsProcgenStableHash.Create();
        private int _cells;
        private int _elements;
        public bool Valid { get; private set; } = true;
        public ulong Hash => _hash.Value;
        public static bool Coordinate(Vector2i cell) => cell.X is >= -1_000_000 and <= 1_000_000 && cell.Y is >= -1_000_000 and <= 1_000_000;
        private bool Spend(int count, int maximum)
        { if (!Valid || count < 0 || count > maximum || count > MaximumElements - _elements) { Valid = false; return false; } _elements += count; _hash.AddInt(count); return true; }
        private string Id(string? id)
        { if (id == null || !KsProcgenAreaNormalizer.ValidId(id) || id.Contains('/')) { Valid = false; return string.Empty; } _hash.AddString(id); return id; }
        private string? OptionalId(string? id)
        {
            _hash.AddInt(id == null ? 0 : 1);
            if (id == null) return null;
            if (!KsProcgenAreaNormalizer.ValidId(id)) { Valid = false; return string.Empty; }
            _hash.AddString(id); return id;
        }
        private Vector2i Cell(Vector2i cell)
        { if (!Coordinate(cell)) Valid = false; _hash.AddInt(cell.X); _hash.AddInt(cell.Y); return cell; }
        private int Turn(int turn) { if (turn is < 0 or > 3) Valid = false; _hash.AddInt(turn); return turn; }
        private List<int> Turns(List<int>? turns)
        {
            if (turns == null || turns.Count == 0 || turns.Distinct().Count() != turns.Count) { Valid = false; return new(); }
            return Items(turns.OrderBy(turn => turn).ToList(), 4, Turn);
        }
        private List<Vector2i> Cells(List<Vector2i>? cells)
        {
            if (cells == null || cells.Count > MaximumCells - _cells) { Valid = false; return new(); }
            _cells += cells.Count;
            if (cells.Distinct().Count() != cells.Count) { Valid = false; return new(); }
            if (!Valid) return new();
            _hash.AddInt(cells.Count);
            return KsProcgenGeometry.SortCells(cells).Select(Cell).ToList();
        }
        private List<T> Items<T>(List<T>? items, int maximum, Func<T, T> copy)
        {
            if (items == null || !Spend(items.Count, maximum)) { Valid = false; return new(); }
            var result = new List<T>();
            foreach (var item in items) { result.Add(copy(item)); if (!Valid) break; }
            return result;
        }
        private Dictionary<string, T> Assets<T>(Dictionary<string, T>? assets, Func<T, T> copy)
        {
            var result = new Dictionary<string, T>(StringComparer.Ordinal);
            if (assets == null || !Spend(assets.Count, MaximumAssets)) { Valid = false; return result; }
            foreach (var (id, asset) in assets.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            { var key = Id(id); if (!Valid) break; result.Add(key, copy(asset)); if (!Valid) break; }
            return result;
        }
        public KsProcgenLayoutDraftSpec Copy(KsProcgenLayoutDraftSpec spec)
        {
            _hash.AddString("ks-procgen-layout-assets-v2");
            var copy = new KsProcgenLayoutDraftSpec
            {
                DocumentId = spec.DocumentId, Revision = spec.Revision,
                Rooms = Assets(spec.Rooms, Room), Families = Assets(spec.Families, Family), Libraries = Assets(spec.Libraries, Library),
            };
            if (copy.Rooms.Count + copy.Families.Count + copy.Libraries.Count > MaximumAssets) Valid = false;
            return copy;
        }
        private KsProcgenRoomAssetSpec Room(KsProcgenRoomAssetSpec value)
        {
            if (value == null || (value.SourceKey == null) != (value.ContentFingerprint == null) ||
                value.SourceGridUid is <= 0 || !Coordinate(value.SourceOrigin) ||
                value.SourceKey == null && (value.SourceGridUid != null || value.SourceOrigin != Vector2i.Zero))
            { Valid = false; return new(); }
            _hash.AddInt(value.SourceGridUid == null ? 0 : 1);
            if (value.SourceGridUid is { } gridUid) _hash.AddInt(gridUid);
            var copy = new KsProcgenRoomAssetSpec { SourceKey = OptionalId(value.SourceKey), ContentFingerprint = OptionalId(value.ContentFingerprint), Cells = Cells(value.Cells),
                SourceGridUid = value.SourceGridUid, SourceOrigin = Cell(value.SourceOrigin),
                ExteriorFacades = Items(value.ExteriorFacades, 4096, edge =>
                { if (edge == null) { Valid = false; return new(); } return new() { Cell = Cell(edge.Cell), OutwardNormal = Cell(edge.OutwardNormal) }; }),
                Ports = Items(value.Ports, 128, port =>
                { if (port == null) { Valid = false; return new(); } return new() { Id = Id(port.Id), Threshold = Cell(port.Threshold), OutwardNormal = Cell(port.OutwardNormal) }; }) };
            if (copy.Cells.Count == 0) Valid = false;
            // Long arithmetic prevents an authored origin/mask from wrapping into another source cell.
            foreach (var cell in copy.Cells)
                if ((long) cell.X + copy.SourceOrigin.X is < -1_000_000 or > 1_000_000 ||
                    (long) cell.Y + copy.SourceOrigin.Y is < -1_000_000 or > 1_000_000) Valid = false;
            return copy;
        }
        private KsProcgenLayoutFamilySpec Family(KsProcgenLayoutFamilySpec value)
        {
            if (value == null) { Valid = false; return new(); }
            var copy = new KsProcgenLayoutFamilySpec { AllowedQuarterTurns = Turns(value.AllowedQuarterTurns), Options = Items(value.Options, 64, option =>
            {
                if (option == null) { Valid = false; return new(); }
                return new() { Id = Id(option.Id), ReservationCells = Cells(option.ReservationCells), GenerateCells = Cells(option.GenerateCells),
                    Members = Items(option.Members, 64, member =>
                    { if (member == null) { Valid = false; return new(); } return new() { Id = Id(member.Id), Room = Id(member.Room), Origin = Cell(member.Origin), QuarterTurns = Turn(member.QuarterTurns) }; }) };
            }) };
            if (copy.Options.Count == 0) Valid = false;
            return copy;
        }
        private KsProcgenRoomLibrarySpec Library(KsProcgenRoomLibrarySpec value)
        {
            if (value == null) { Valid = false; return new(); }
            var copy = new KsProcgenRoomLibrarySpec { Families = Items(value.Families, 128, Id), Rooms = Items(value.Rooms, 128, room =>
            { if (room == null) { Valid = false; return new(); } return new() { Room = Id(room.Room), AllowedQuarterTurns = Turns(room.AllowedQuarterTurns) }; }) };
            if (copy.Families.Distinct().Count() != copy.Families.Count || copy.Rooms.Select(room => room.Room).Distinct().Count() != copy.Rooms.Count || copy.Families.Count + copy.Rooms.Count == 0) Valid = false;
            return copy;
        }
    }
}
