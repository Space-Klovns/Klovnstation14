using System.Linq;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.Procedural;

/// <summary>
/// Stable per-room content choice. These IDs are planning commitments, not spawned or verified fixtures.
/// </summary>
public sealed record KsProcgenThemeSelection(
    string ThemeId,
    string TilePackId,
    string TilePaletteId,
    string WallPackId,
    string WallFamilyId,
    string? LightingPackId,
    string? LightFixtureId,
    string? DominantEntityPackId,
    IReadOnlyList<string> SupportingEntityPackIds,
    float FurnishingDensity = 0.2f)
{
    public KsProcgenSelectedContent? Content { get; init; }
}

public static class KsProcgenThemeSelector
{
    public static bool TrySelect(
        IPrototypeManager prototypeManager,
        string themeId,
        int seed,
        string roomId,
        out KsProcgenThemeSelection? selection,
        out KsProcgenIssue? issue)
    {
        selection = null;
        if (string.IsNullOrWhiteSpace(roomId))
        {
            issue = new("InvalidRoomId", "Theme selection needs a stable room ID.");
            return false;
        }
        if (!KsProcgenThemeCatalog.TryCapture(prototypeManager, themeId, out var catalog, out issue)) return false;
        return TrySelect(catalog!, seed, roomId, out selection, out issue);
    }

    /// <summary>Choose only from retained candidate pools; no prototype manager lookup occurs.</summary>
    public static bool TrySelect(KsProcgenThemeCatalog catalog, int seed, string roomId,
        out KsProcgenThemeSelection? selection, out KsProcgenIssue? issue)
        => TrySelectCore(catalog, seed, roomId, out selection, out issue, retainAtmosphere: false, retainElectrical: false);

    internal static bool TrySelectForGasPlanning(KsProcgenThemeCatalog catalog, int seed, string roomId,
        out KsProcgenThemeSelection? selection, out KsProcgenIssue? issue)
        => TrySelectCore(catalog, seed, roomId, out selection, out issue, retainAtmosphere: true, retainElectrical: false);

    internal static bool TrySelectForUtilityPlanning(KsProcgenThemeCatalog catalog, int seed, string roomId,
        out KsProcgenThemeSelection? selection, out KsProcgenIssue? issue, bool retainAtmosphere, bool retainElectrical)
        => TrySelectCore(catalog, seed, roomId, out selection, out issue, retainAtmosphere, retainElectrical);

    private static bool TrySelectCore(KsProcgenThemeCatalog catalog, int seed, string roomId,
        out KsProcgenThemeSelection? selection, out KsProcgenIssue? issue, bool retainAtmosphere, bool retainElectrical)
    {
        selection = null;
        issue = null;
        if (catalog == null || string.IsNullOrWhiteSpace(roomId))
        {
            issue = new("InvalidThemeCatalogSelection", "Selection needs retained content and a stable room ID.");
            return false;
        }
        var theme = catalog.Theme;
        var themeId = theme.Id;

        if (theme!.Atmosphere != null && !retainAtmosphere)
        {
            issue = new("AtmospherePlanningUnsupported", "This content planner cannot yet retain or realize a theme's atmosphere contract.");
            return false;
        }

        if (theme.Electrical != null && !retainElectrical)
        {
            issue = new("ElectricalPlanningUnsupported", "This content planner cannot yet retain or realize a theme's electrical contract.");
            return false;
        }

        var tilePackRef = Choose(theme.TilePacks.Select(reference => (reference.Pack, reference.Weight)),
            seed, roomId, "tile-pack");
        var tilePack = catalog.TilePacks.Single(pack => pack.Id == tilePackRef);
        var paletteId = Choose(tilePack.Choices.Select(palette => (palette.Id, palette.Weight)),
            seed, roomId, "tile-palette");

        var wallPackRef = Choose(theme.WallPacks.Select(reference => (reference.Pack, reference.Weight)),
            seed, roomId, "wall-pack");
        var wallPack = catalog.WallPacks.Single(pack => pack.Id == wallPackRef);
        var wallFamilyId = Choose(wallPack.Choices.Select(family => (family.Id, family.Weight)),
            seed, roomId, "wall-family");

        string? lightingPackId = null;
        string? lightFixtureId = null;
        if (theme.LightingPacks.Count > 0)
        {
            lightingPackId = Choose(theme.LightingPacks.Select(reference => (reference.Pack, reference.Weight)),
                seed, roomId, "lighting-pack");
            var lightingPack = catalog.LightingPacks.Single(pack => pack.Id == lightingPackId);
            lightFixtureId = Choose(lightingPack.Choices.Select(fixture => (fixture.Id, fixture.Weight)),
                seed, roomId, "light-fixture");
        }

        string? dominantId = null;
        var supportingIds = new List<string>();
        if (theme.EntityPacks.Count > 0)
        {
            var activityReferences = theme.EntityPacks.Where(reference => reference.Weight > 0f &&
                catalog.EntityPacks.Single(pack => pack.Id == reference.Pack).ActivityTags.Count > 0)
                .ToArray();
            if (activityReferences.Length == 0)
                activityReferences = theme.EntityPacks.ToArray();

            dominantId = Choose(activityReferences.Select(reference => (reference.Pack, reference.Weight)),
                seed, roomId, "dominant-activity");
            var dominantPack = catalog.EntityPacks.Single(pack => pack.Id == dominantId);
            foreach (var reference in theme.EntityPacks.OrderBy(reference => reference.Pack, StringComparer.Ordinal))
            {
                if (reference.Pack == dominantId || reference.Weight == 0f)
                    continue;

                var candidatePack = catalog.EntityPacks.Single(pack => pack.Id == reference.Pack);
                var compatible = dominantPack.CompatibleSupportTags.Any(tag => candidatePack.Tags.Contains(tag));
                if (!compatible)
                {
                    if (reference.MinimumCount > 0)
                    {
                        issue = new KsProcgenIssue("IncompatibleMandatoryPack",
                            $"Mandatory entity pack {reference.Pack} is incompatible with {dominantId}.");
                        return false;
                    }

                    continue;
                }

                supportingIds.Add(reference.Pack);
            }
        }

        var chosen = new KsProcgenThemeSelection(themeId, tilePackRef, paletteId,
            wallPackRef, wallFamilyId, lightingPackId, lightFixtureId, dominantId, KsProcgenReadOnly.Copy(supportingIds),
            FurnishingDensity: theme.Goals.FurnishingDensity);
        var selectedIds = supportingIds.ToHashSet(StringComparer.Ordinal);
        if (dominantId != null) selectedIds.Add(dominantId);
        var content = new KsProcgenSelectedContent(themeId,
            tilePack.Choices.Single(choice => choice.Id == paletteId).Declaration,
            wallPack.Choices.Single(choice => choice.Id == wallFamilyId).Declaration,
            lightingPackId == null ? null : catalog.LightingPacks.Single(pack => pack.Id == lightingPackId)
                .Choices.Single(choice => choice.Id == lightFixtureId).Declaration,
            theme.Goals, catalog.EntityPacks.Where(pack => selectedIds.Contains(pack.Id))
                .Select(pack => new KsProcgenSelectedEntityPack(pack.Id, pack.Cores)), atmosphere: theme.Atmosphere);
        selection = chosen with { Content = content };
        issue = null;
        return true;
    }

    private static string Choose(
        IEnumerable<(string Id, float Weight)> options,
        int seed,
        string roomId,
        string stage)
    {
        var sorted = options.Where(option => option.Weight > 0f)
            .OrderBy(option => option.Id, StringComparer.Ordinal).ToArray();
        var total = sorted.Sum(option => (double) option.Weight);
        var random = KsProcgenRandom.ForStage(seed, $"theme/{stage}", roomId);
        var unit = (random.NextUInt64() >> 11) / 9_007_199_254_740_992.0;
        var point = unit * total;
        foreach (var (id, weight) in sorted)
        {
            point -= weight;
            if (point < 0)
                return id;
        }

        return sorted[^1].Id;
    }
}
