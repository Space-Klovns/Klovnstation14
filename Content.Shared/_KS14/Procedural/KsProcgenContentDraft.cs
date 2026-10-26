using System.Linq;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.Procedural;

/// <summary>Editable interior assets. Names are scoped by document and asset kind, never globally registered.</summary>
[DataDefinition]
public sealed partial class KsProcgenContentDraftSpec
{
    [DataField] public int SchemaVersion = 1;
    [DataField(required: true)] public string DocumentId = string.Empty;
    [DataField] public int Revision;
    [DataField] public Dictionary<string, KsProcgenRoomThemeSpec> Themes = new(StringComparer.Ordinal);
    [DataField] public Dictionary<string, KsProcgenTilePackSpec> Tiles = new(StringComparer.Ordinal);
    [DataField] public Dictionary<string, KsProcgenWallPackSpec> Walls = new(StringComparer.Ordinal);
    [DataField] public Dictionary<string, KsProcgenLightingPackSpec> Lights = new(StringComparer.Ordinal);
    [DataField] public Dictionary<string, KsProcgenEntityPackSpec> Entities = new(StringComparer.Ordinal);
    [DataField] public Dictionary<string, KsProcgenAssemblySpec> Assemblies = new(StringComparer.Ordinal);
}

/// <summary>
/// Detached, bounded authoring revision. Broken references may be retained for editing; capture a
/// theme catalog to validate its complete candidate pools before planning. This is not a manifest,
/// permission, persisted project, entity component override or runtime verification receipt.
/// </summary>
public sealed class KsProcgenContentDraft
{
    public const int MaximumAssets = 128;
    public const int MaximumElements = 32_768;
    public const int MaximumTextCharacters = 1_048_576;
    private readonly KsProcgenContentDraftSpec _spec;

    private KsProcgenContentDraft(KsProcgenContentDraftSpec spec, ulong declarationHash)
    { _spec = spec; DeclarationHash = declarationHash; }

    public int SchemaVersion => 1;
    public string DocumentId => _spec.DocumentId;
    public int Revision => _spec.Revision;
    public ulong DeclarationHash { get; }
    public int AssetCount => _spec.Themes.Count + _spec.Tiles.Count + _spec.Walls.Count + _spec.Lights.Count + _spec.Entities.Count + _spec.Assemblies.Count;
    public KsProcgenContentDraftSpec CopySpec() => new KsProcgenContentDraftCopier().Copy(_spec);
    internal KsProcgenContentSource Source(IPrototypeManager manager) => new(manager, _spec);

    public static bool TrySnapshot(KsProcgenContentDraftSpec spec, out KsProcgenContentDraft? draft, out KsProcgenIssue? issue)
    {
        draft = null; issue = null;
        if (spec == null || spec.SchemaVersion != 1)
        { issue = new("UnsupportedContentDraftSchema", "Interior content draft schema version 1 is required."); return false; }
        if (!KsProcgenAreaNormalizer.ValidId(spec.DocumentId) || spec.Revision < 0)
        { issue = new("InvalidContentDraft", "A bounded document ID and nonnegative revision are required."); return false; }
        var copier = new KsProcgenContentDraftCopier();
        var detached = copier.Copy(spec);
        if (!copier.Valid)
        { issue = new("InvalidContentDraftStructure", "Draft assets must have bounded nonnull data, stable names and finite numeric values."); return false; }
        draft = new(detached, copier.Hash);
        return true;
    }

    public bool TryRevise(KsProcgenContentDraftSpec replacement, out KsProcgenContentDraft? revised, out KsProcgenIssue? issue)
    {
        revised = null;
        if (replacement == null || replacement.DocumentId != DocumentId || replacement.Revision != Revision || Revision == int.MaxValue)
        { issue = new("ContentDraftRevisionConflict", "Replacement must target this document and its current incrementable revision."); return false; }
        if (!TrySnapshot(replacement, out var validated, out issue)) return false;
        var spec = validated!.CopySpec(); spec.Revision++;
        revised = new(spec, validated.DeclarationHash);
        return true;
    }
}

/// <summary>Transient source used only while validating/capturing; returned catalogs retain no source aliases.</summary>
internal sealed class KsProcgenContentSource(IPrototypeManager manager, KsProcgenContentDraftSpec? draft = null)
{
    public KsProcgenRoomThemeSpec? Theme(string id) => draft != null && draft.Themes.TryGetValue(id, out var asset) ? asset : manager.TryIndex<KsProcgenRoomThemePrototype>(id, out var loaded) ? loaded : null;
    public KsProcgenTilePackSpec? Tile(string id) => draft != null && draft.Tiles.TryGetValue(id, out var asset) ? asset : manager.TryIndex<KsProcgenTilePackPrototype>(id, out var loaded) ? loaded : null;
    public KsProcgenWallPackSpec? Wall(string id) => draft != null && draft.Walls.TryGetValue(id, out var asset) ? asset : manager.TryIndex<KsProcgenWallPackPrototype>(id, out var loaded) ? loaded : null;
    public KsProcgenLightingPackSpec? Light(string id) => draft != null && draft.Lights.TryGetValue(id, out var asset) ? asset : manager.TryIndex<KsProcgenLightingPackPrototype>(id, out var loaded) ? loaded : null;
    public KsProcgenEntityPackSpec? Entity(string id) => draft != null && draft.Entities.TryGetValue(id, out var asset) ? asset : manager.TryIndex<KsProcgenEntityPackPrototype>(id, out var loaded) ? loaded : null;
    public KsProcgenAssemblySpec? Assembly(string id) => draft != null && draft.Assemblies.TryGetValue(id, out var asset) ? asset : manager.TryIndex<KsProcgenAssemblyPrototype>(id, out var loaded) ? loaded : null;
}

/// <summary>
/// Copies the explicit authoring grammar only. Collection/text bounds apply cumulatively before
/// descent. Hash includes ordered authoring lists and all fields, excluding document ID/revision.
/// No arbitrary component deserialization or reflection is part of importing these assets.
/// </summary>
internal sealed class KsProcgenContentDraftCopier
{
    private KsProcgenStableHash _hash = KsProcgenStableHash.Create();
    private int _elements;
    private int _characters;
    public bool Valid { get; private set; } = true;
    public ulong Hash => _hash.Value;

    public KsProcgenContentDraftSpec Copy(KsProcgenContentDraftSpec spec)
    {
        _hash.AddString("ks-procgen-interior-assets-v1");
        var copy = new KsProcgenContentDraftSpec
        {
            DocumentId = spec.DocumentId, Revision = spec.Revision,
            Themes = Assets(spec.Themes, Theme), Tiles = Assets(spec.Tiles, Tile), Walls = Assets(spec.Walls, Wall),
            Lights = Assets(spec.Lights, Light), Entities = Assets(spec.Entities, Entity), Assemblies = Assets(spec.Assemblies, Assembly),
        };
        if (copy.Themes.Count + copy.Tiles.Count + copy.Walls.Count + copy.Lights.Count + copy.Entities.Count + copy.Assemblies.Count > KsProcgenContentDraft.MaximumAssets) Valid = false;
        return copy;
    }

    private bool Spend(int count, int maximum)
    {
        if (!Valid || count < 0 || count > maximum || count > KsProcgenContentDraft.MaximumElements - _elements)
        { Valid = false; return false; }
        _elements += count; _hash.AddInt(count); return true;
    }
    private T Present<T>(T? value, Func<T> empty) where T : class
    { if (value != null) return value; Valid = false; return empty(); }
    private Dictionary<string, T> Assets<T>(Dictionary<string, T>? assets, Func<T, T> copy)
    {
        var result = new Dictionary<string, T>(StringComparer.Ordinal);
        if (assets == null || !Spend(assets.Count, KsProcgenContentDraft.MaximumAssets)) { Valid = false; return result; }
        foreach (var (id, asset) in assets.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (!KsProcgenAreaNormalizer.ValidId(id)) { Valid = false; break; }
            Text(id); result.Add(id, copy(asset));
            if (!Valid) break;
        }
        return result;
    }
    private List<T> Items<T>(List<T>? values, int maximum, Func<T, T> copy)
    {
        if (values == null || !Spend(values.Count, maximum)) { Valid = false; return new(); }
        var result = new List<T>();
        foreach (var value in values) { result.Add(copy(value)); if (!Valid) break; }
        return result;
    }
    private string Text(string? value)
    {
        if (value == null || value.Length > 512 || value.Length > KsProcgenContentDraft.MaximumTextCharacters - _characters)
        { Valid = false; return string.Empty; }
        _characters += value.Length; _hash.AddString(value); return value;
    }
    private string? Optional(string? value) { _hash.AddInt(value == null ? 0 : 1); return value == null ? null : Text(value); }
    private int Integer(int value) { _hash.AddInt(value); return value; }
    private bool Flag(bool value) { _hash.AddInt(value ? 1 : 0); return value; }
    private float Number(float value)
    {
        if (!float.IsFinite(value)) Valid = false;
        value = value == 0f ? 0f : value; _hash.AddInt(BitConverter.SingleToInt32Bits(value)); return value;
    }
    private T EnumValue<T>(T value) where T : struct, Enum
    { if (!Enum.IsDefined(value)) Valid = false; _hash.AddString(value.ToString()); return value; }
    private Robust.Shared.Maths.Vector2i Cell(Robust.Shared.Maths.Vector2i cell)
    { _hash.AddInt(cell.X); _hash.AddInt(cell.Y); return cell; }
    private List<string> Tags(List<string> tags) => Items(tags, 64, Text);
    private List<KsProcgenWeightedPackReference>? References(List<KsProcgenWeightedPackReference>? references)
    {
        _hash.AddInt(references == null ? 0 : 1);
        return references == null ? null : Items(references, 64, reference =>
        {
            reference = Present(reference, static () => new());
            return new() { Pack = Text(reference.Pack), Weight = Number(reference.Weight), MinimumCount = Integer(reference.MinimumCount) };
        });
    }
    private KsProcgenRoomThemeSpec Theme(KsProcgenRoomThemeSpec value)
    {
        value = Present(value, static () => new());
        _hash.AddInt(value.Goals == null ? 0 : 1);
        return new()
        {
            Parent = Optional(value.Parent), FallbackTheme = Optional(value.FallbackTheme),
            AtmosphereProfile = Optional(value.AtmosphereProfile), ElectricalProfile = Optional(value.ElectricalProfile),
            Tags = Tags(value.Tags), CompatibleRegionTags = Tags(value.CompatibleRegionTags),
            TilePacks = References(value.TilePacks), WallPacks = References(value.WallPacks),
            LightingPacks = References(value.LightingPacks), EntityPacks = References(value.EntityPacks),
            Goals = value.Goals == null ? null : new()
            {
                CoherentContents = Flag(value.Goals.CoherentContents), KeepCenterOpen = Flag(value.Goals.KeepCenterOpen),
                AllowLooseItems = Flag(value.Goals.AllowLooseItems), FurnishingDensity = Number(value.Goals.FurnishingDensity), LightingCoverage = Number(value.Goals.LightingCoverage),
            },
        };
    }
    private KsProcgenTilePackSpec Tile(KsProcgenTilePackSpec value)
    {
        value = Present(value, static () => new());
        return new() { Tags = Tags(value.Tags), Palettes = Items(value.Palettes, 64, item =>
        {
            item = Present(item, static () => new());
            return new() { Id = Text(item.Id), PrimaryTile = Text(item.PrimaryTile), AccentTile = Optional(item.AccentTile), AccentFraction = Number(item.AccentFraction), Weight = Number(item.Weight) };
        }) };
    }
    private KsProcgenWallPackSpec Wall(KsProcgenWallPackSpec value)
    {
        value = Present(value, static () => new());
        return new() { Tags = Tags(value.Tags), Families = Items(value.Families, 64, item =>
        {
            item = Present(item, static () => new());
            return new() { Id = Text(item.Id), InteriorWall = Text(item.InteriorWall), HullWall = Optional(item.HullWall), Door = Optional(item.Door), Window = Optional(item.Window), Weight = Number(item.Weight) };
        }) };
    }
    private KsProcgenLightingPackSpec Light(KsProcgenLightingPackSpec value)
    {
        value = Present(value, static () => new());
        return new() { Tags = Tags(value.Tags), Fixtures = Items(value.Fixtures, 64, item =>
        {
            item = Present(item, static () => new());
            return new() { Id = Text(item.Id), Entity = Text(item.Entity), Weight = Number(item.Weight), PreferredSpacing = Integer(item.PreferredSpacing), Supply = EnumValue(item.Supply) };
        }) };
    }
    private KsProcgenEntityPackSpec Entity(KsProcgenEntityPackSpec value)
    {
        value = Present(value, static () => new());
        return new()
        {
            Tags = Tags(value.Tags), ActivityTags = Tags(value.ActivityTags), CompatibleSupportTags = Tags(value.CompatibleSupportTags),
            Entries = Items(value.Entries, 64, item =>
            {
                item = Present(item, static () => new());
                return new() { Id = Text(item.Id), Entity = Text(item.Entity), Weight = Number(item.Weight), MinimumCount = Integer(item.MinimumCount),
                    Role = EnumValue(item.Role), Movement = EnumValue(item.Movement), RequiresInteractionApproach = Flag(item.RequiresInteractionApproach),
                    Footprint = Items(item.Footprint, 16, Cell), AllowedQuarterTurns = Items(item.AllowedQuarterTurns, 4, Integer) };
            }),
            Assemblies = Items(value.Assemblies, 64, item =>
            {
                item = Present(item, static () => new());
                return new() { Id = Text(item.Id), Assembly = Text(item.Assembly), Weight = Number(item.Weight), MinimumCount = Integer(item.MinimumCount),
                    Bindings = Assets(item.Bindings, Text) };
            }),
        };
    }
    private KsProcgenAssemblySpec Assembly(KsProcgenAssemblySpec value)
    {
        value = Present(value, static () => new());
        return new() { AnchorMember = Text(value.AnchorMember), Members = Members(value.Members), Relations = Relations(value.Relations),
            Variants = Items(value.Variants, 16, variant =>
            {
                variant = Present(variant, static () => new());
                return new() { Id = Text(variant.Id), AnchorMember = Text(variant.AnchorMember), Members = Members(variant.Members), Relations = Relations(variant.Relations) };
            }) };
    }
    private List<KsProcgenAssemblyMember> Members(List<KsProcgenAssemblyMember> values) => Items(values, 64, member =>
    {
        member = Present(member, static () => new()); _hash.AddInt(member.ApproachLanding.HasValue ? 1 : 0);
        return new() { Id = Text(member.Id), Entity = Optional(member.Entity), Binding = Optional(member.Binding),
            MinimumCount = Integer(member.MinimumCount), MaximumCount = Integer(member.MaximumCount), Role = EnumValue(member.Role), Movement = EnumValue(member.Movement),
            Footprint = Items(member.Footprint, 16, Cell), AllowedQuarterTurns = Items(member.AllowedQuarterTurns, 4, Integer), RotationMode = EnumValue(member.RotationMode),
            LocalQuarterTurns = Integer(member.LocalQuarterTurns), RequiresInteractionApproach = Flag(member.RequiresInteractionApproach),
            ApproachLanding = member.ApproachLanding is { } cell ? Cell(cell) : null };
    });
    private List<KsProcgenAssemblyRelation> Relations(List<KsProcgenAssemblyRelation> values) => Items(values, 256, relation =>
    {
        relation = Present(relation, static () => new());
        return new() { Id = Text(relation.Id), Subject = Text(relation.Subject), Kind = EnumValue(relation.Kind), Target = Optional(relation.Target),
            Severity = EnumValue(relation.Severity), MinimumDistance = Integer(relation.MinimumDistance), MaximumDistance = Integer(relation.MaximumDistance),
            Slot = Optional(relation.Slot), ContainerId = Optional(relation.ContainerId), ApproachPolicy = EnumValue(relation.ApproachPolicy) };
    });
}
