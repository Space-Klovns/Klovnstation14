using System.Linq;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.Procedural;

public sealed record KsProcgenWeightedDeclaration<T>(string Id, float Weight, T Declaration);
public sealed record KsProcgenCapturedPack<T>(string Id, IReadOnlyList<KsProcgenWeightedDeclaration<T>> Choices);
public sealed record KsProcgenCapturedEntityPack(string Id, IReadOnlyList<string> Tags,
    IReadOnlyList<string> ActivityTags, IReadOnlyList<string> CompatibleSupportTags,
    IReadOnlyList<KsProcgenResolvedEntityCore> Cores);

/// <summary>
/// One resolved theme's complete candidate pools, detached before choosing any room contents.
/// DeclarationHash identifies these planning declarations, including unselected candidates and
/// ordered assembly alternatives. It does not hash entity components, room libraries or live context.
/// </summary>
public sealed class KsProcgenThemeCatalog
{
    private KsProcgenThemeCatalog(KsProcgenResolvedTheme theme,
        IEnumerable<KsProcgenCapturedPack<KsProcgenSelectedTile>> tiles,
        IEnumerable<KsProcgenCapturedPack<KsProcgenSelectedWall>> walls,
        IEnumerable<KsProcgenCapturedPack<KsProcgenSelectedLight>> lights,
        IEnumerable<KsProcgenCapturedEntityPack> entities)
    {
        Theme = theme;
        TilePacks = KsProcgenReadOnly.Copy(tiles);
        WallPacks = KsProcgenReadOnly.Copy(walls);
        LightingPacks = KsProcgenReadOnly.Copy(lights);
        EntityPacks = KsProcgenReadOnly.Copy(entities);
        DeclarationHash = Hash();
    }

    public int SchemaVersion => 1;
    public KsProcgenResolvedTheme Theme { get; }
    public IReadOnlyList<KsProcgenCapturedPack<KsProcgenSelectedTile>> TilePacks { get; }
    public IReadOnlyList<KsProcgenCapturedPack<KsProcgenSelectedWall>> WallPacks { get; }
    public IReadOnlyList<KsProcgenCapturedPack<KsProcgenSelectedLight>> LightingPacks { get; }
    public IReadOnlyList<KsProcgenCapturedEntityPack> EntityPacks { get; }
    public ulong DeclarationHash { get; }

    /// <summary>All structural, lighting and furnishing candidates, including unselected alternatives.</summary>
    public IEnumerable<string> CandidateEntityIds() =>
        WallPacks.SelectMany(pack => pack.Choices).SelectMany(choice =>
                new[] { choice.Declaration.InteriorWall, choice.Declaration.HullWall, choice.Declaration.Door, choice.Declaration.Window })
            .Where(id => id != null).Select(id => id!)
            .Concat(LightingPacks.SelectMany(pack => pack.Choices).Select(choice => choice.Declaration.Entity))
            .Concat(EntityPacks.SelectMany(pack => pack.Cores).SelectMany(core => core.Variants)
                .SelectMany(variant => variant.Members).Select(member => member.Entry.Entity));

    public IEnumerable<string> CandidateTileIds() => TilePacks.SelectMany(pack => pack.Choices).SelectMany(choice =>
        new[] { choice.Declaration.PrimaryTile, choice.Declaration.AccentTile }).Where(id => id != null).Select(id => id!);

    public static bool TryCapture(IPrototypeManager manager, string themeId, out KsProcgenThemeCatalog? catalog,
        out KsProcgenIssue? issue, KsProcgenUtilityDraft? draft = null, KsProcgenContentDraft? assets = null)
    {
        catalog = null;
        if (manager == null)
        {
            issue = new("InvalidThemeCatalogInput", "Theme capture needs a loaded prototype manager.");
            return false;
        }
        if (!KsProcgenThemeValidator.TryResolve(manager, themeId, out var theme, out issue, draft: draft, assets: assets)) return false;
        var source = assets?.Source(manager) ?? new KsProcgenContentSource(manager);
        var tiles = new List<KsProcgenCapturedPack<KsProcgenSelectedTile>>();
        var walls = new List<KsProcgenCapturedPack<KsProcgenSelectedWall>>();
        var lights = new List<KsProcgenCapturedPack<KsProcgenSelectedLight>>();
        var entities = new List<KsProcgenCapturedEntityPack>();
        foreach (var reference in theme!.TilePacks.OrderBy(reference => reference.Pack, StringComparer.Ordinal))
        {
            var pack = source.Tile(reference.Pack)!;
            tiles.Add(new(reference.Pack, pack.Palettes.OrderBy(item => item.Id, StringComparer.Ordinal)
                .Select(item => new KsProcgenWeightedDeclaration<KsProcgenSelectedTile>(item.Id, item.Weight,
                    new(reference.Pack, item.Id, item.PrimaryTile, item.AccentTile, item.AccentFraction))).ToReadOnlyList()));
        }
        foreach (var reference in theme.WallPacks.OrderBy(reference => reference.Pack, StringComparer.Ordinal))
        {
            var pack = source.Wall(reference.Pack)!;
            walls.Add(new(reference.Pack, pack.Families.OrderBy(item => item.Id, StringComparer.Ordinal)
                .Select(item => new KsProcgenWeightedDeclaration<KsProcgenSelectedWall>(item.Id, item.Weight,
                    new(reference.Pack, item.Id, item.InteriorWall, item.HullWall, item.Door, item.Window))).ToReadOnlyList()));
        }
        foreach (var reference in theme.LightingPacks.OrderBy(reference => reference.Pack, StringComparer.Ordinal))
        {
            var pack = source.Light(reference.Pack)!;
            lights.Add(new(reference.Pack, pack.Fixtures.OrderBy(item => item.Id, StringComparer.Ordinal)
                .Select(item => new KsProcgenWeightedDeclaration<KsProcgenSelectedLight>(item.Id, item.Weight,
                    new(reference.Pack, item.Id, item.Entity, item.PreferredSpacing, item.Supply))).ToReadOnlyList()));
        }
        foreach (var reference in theme.EntityPacks.OrderBy(reference => reference.Pack, StringComparer.Ordinal))
        {
            var pack = source.Entity(reference.Pack)!;
            if (pack.Tags == null || pack.ActivityTags == null || pack.CompatibleSupportTags == null ||
                pack.Tags.Concat(pack.ActivityTags).Concat(pack.CompatibleSupportTags).Any(tag => tag == null))
            {
                issue = new("InvalidEntityPackTags", "Entity pack tag collections must contain nonnull strings.");
                return false;
            }
            if (!KsProcgenAssemblyCompiler.TryResolvePack(manager, pack, out var cores, out issue, assemblyResolver: source.Assembly)) return false;
            entities.Add(new(reference.Pack, Tags(pack.Tags), Tags(pack.ActivityTags), Tags(pack.CompatibleSupportTags), cores));
        }
        catalog = new(theme, tiles, walls, lights, entities);
        return true;
    }

    private static IReadOnlyList<string> Tags(IEnumerable<string> tags) =>
        tags.Distinct(StringComparer.Ordinal).OrderBy(tag => tag, StringComparer.Ordinal).ToReadOnlyList();

    private ulong Hash()
    {
        var hash = KsProcgenStableHash.Create();
        hash.AddString("ks-procgen-theme-catalog-v1"); hash.AddInt(SchemaVersion); hash.AddString(Theme.Id);
        AddReferences(ref hash, Theme.TilePacks); AddReferences(ref hash, Theme.WallPacks);
        AddReferences(ref hash, Theme.LightingPacks); AddReferences(ref hash, Theme.EntityPacks);
        hash.AddInt(Theme.Goals.CoherentContents ? 1 : 0); hash.AddInt(Theme.Goals.KeepCenterOpen ? 1 : 0);
        hash.AddInt(Theme.Goals.AllowLooseItems ? 1 : 0);
        AddFloat(ref hash, Theme.Goals.FurnishingDensity); AddFloat(ref hash, Theme.Goals.LightingCoverage);
        hash.AddInt(Theme.Atmosphere == null ? 0 : 1);
        if (Theme.Atmosphere != null) Theme.Atmosphere.AddHash(ref hash);
        hash.AddInt(Theme.Electrical == null ? 0 : 1);
        if (Theme.Electrical != null) Theme.Electrical.AddHash(ref hash);
        hash.AddInt(TilePacks.Count);
        foreach (var pack in TilePacks)
        {
            hash.AddString(pack.Id); hash.AddInt(pack.Choices.Count);
            foreach (var choice in pack.Choices)
            {
                hash.AddString(choice.Id); AddFloat(ref hash, choice.Weight);
                var item = choice.Declaration;
                hash.AddString(item.PrimaryTile); AddNullable(ref hash, item.AccentTile); AddFloat(ref hash, item.AccentFraction);
            }
        }
        hash.AddInt(WallPacks.Count);
        foreach (var pack in WallPacks)
        {
            hash.AddString(pack.Id); hash.AddInt(pack.Choices.Count);
            foreach (var choice in pack.Choices)
            {
                hash.AddString(choice.Id); AddFloat(ref hash, choice.Weight);
                var item = choice.Declaration;
                hash.AddString(item.InteriorWall); AddNullable(ref hash, item.HullWall);
                AddNullable(ref hash, item.Door); AddNullable(ref hash, item.Window);
            }
        }
        hash.AddInt(LightingPacks.Count);
        foreach (var pack in LightingPacks)
        {
            hash.AddString(pack.Id); hash.AddInt(pack.Choices.Count);
            foreach (var choice in pack.Choices)
            {
                hash.AddString(choice.Id); AddFloat(ref hash, choice.Weight);
                var item = choice.Declaration;
                hash.AddString(item.Entity); hash.AddInt(item.PreferredSpacing); hash.AddInt((int) item.Supply);
            }
        }
        hash.AddInt(EntityPacks.Count);
        foreach (var pack in EntityPacks)
        {
            hash.AddString(pack.Id); AddStrings(ref hash, pack.Tags); AddStrings(ref hash, pack.ActivityTags);
            AddStrings(ref hash, pack.CompatibleSupportTags); hash.AddInt(pack.Cores.Count);
            foreach (var core in pack.Cores)
            {
                hash.AddString(core.Id); AddFloat(ref hash, core.Weight); hash.AddInt(core.MinimumCount);
                hash.AddInt(core.Variants.Count);
                // Variant sequence is authored preference order, unlike the unordered pack pools.
                foreach (var variant in core.Variants)
                {
                    hash.AddString(variant.AssemblyId); hash.AddString(variant.VariantId); hash.AddString(variant.AnchorMember);
                    hash.AddInt(variant.Members.Count);
                    foreach (var member in variant.Members)
                    {
                        hash.AddString(member.Id); hash.AddString(member.SourceMemberId); hash.AddInt(member.Required ? 1 : 0);
                        hash.AddInt((int) member.RotationMode); hash.AddInt(member.LocalQuarterTurns);
                        hash.AddInt(member.ApproachLanding.HasValue ? 1 : 0);
                        if (member.ApproachLanding is { } landing) AddCell(ref hash, landing);
                        var entry = member.Entry;
                        hash.AddString(entry.Id); hash.AddString(entry.Entity); AddFloat(ref hash, entry.Weight);
                        hash.AddInt(entry.MinimumCount); hash.AddInt((int) entry.Role); hash.AddInt((int) entry.Movement);
                        hash.AddInt(entry.RequiresInteractionApproach ? 1 : 0); hash.AddInt(entry.Footprint.Count);
                        foreach (var cell in entry.Footprint) AddCell(ref hash, cell);
                        hash.AddInt(entry.AllowedQuarterTurns.Count);
                        foreach (var turn in entry.AllowedQuarterTurns) hash.AddInt(turn);
                    }
                    hash.AddInt(variant.Relations.Count);
                    foreach (var relation in variant.Relations)
                    {
                        hash.AddString(relation.Id); hash.AddString(relation.Subject); AddNullable(ref hash, relation.Target);
                        hash.AddInt((int) relation.Kind); hash.AddInt((int) relation.Severity);
                        hash.AddInt(relation.MinimumDistance); hash.AddInt(relation.MaximumDistance);
                        AddNullable(ref hash, relation.Slot); AddNullable(ref hash, relation.ContainerId);
                        hash.AddInt((int) relation.ApproachPolicy);
                    }
                }
            }
        }
        return hash.Value;
    }

    private static void AddReferences(ref KsProcgenStableHash hash, IReadOnlyList<KsProcgenResolvedPackReference> references)
    {
        hash.AddInt(references.Count);
        foreach (var reference in references.OrderBy(reference => reference.Pack, StringComparer.Ordinal))
        {
            hash.AddString(reference.Pack); AddFloat(ref hash, reference.Weight); hash.AddInt(reference.MinimumCount);
        }
    }

    private static void AddStrings(ref KsProcgenStableHash hash, IReadOnlyList<string> values)
    {
        hash.AddInt(values.Count);
        foreach (var value in values) hash.AddString(value);
    }

    private static void AddNullable(ref KsProcgenStableHash hash, string? value)
    {
        hash.AddInt(value == null ? 0 : 1);
        if (value != null) hash.AddString(value);
    }

    private static void AddFloat(ref KsProcgenStableHash hash, float value) => hash.AddInt(BitConverter.SingleToInt32Bits(value == 0f ? 0f : value));
    private static void AddCell(ref KsProcgenStableHash hash, Vector2i cell) { hash.AddInt(cell.X); hash.AddInt(cell.Y); }
}
