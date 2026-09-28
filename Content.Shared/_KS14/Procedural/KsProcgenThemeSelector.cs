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
    IReadOnlyList<string> SupportingEntityPackIds);

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
        issue = null;
        if (string.IsNullOrWhiteSpace(roomId) ||
            !KsProcgenThemeValidator.TryResolve(prototypeManager, themeId, out var theme, out issue))
        {
            issue ??= new KsProcgenIssue("InvalidRoomId", "Theme selection needs a stable room ID.");
            return false;
        }

        var tilePackRef = Choose(theme!.TilePacks.Select(reference => (reference.Pack, reference.Weight)),
            seed, roomId, "tile-pack");
        var tilePack = prototypeManager.Index<KsProcgenTilePackPrototype>(tilePackRef);
        var paletteId = Choose(tilePack.Palettes.Select(palette => (palette.Id, palette.Weight)),
            seed, roomId, "tile-palette");

        var wallPackRef = Choose(theme.WallPacks.Select(reference => (reference.Pack, reference.Weight)),
            seed, roomId, "wall-pack");
        var wallPack = prototypeManager.Index<KsProcgenWallPackPrototype>(wallPackRef);
        var wallFamilyId = Choose(wallPack.Families.Select(family => (family.Id, family.Weight)),
            seed, roomId, "wall-family");

        string? lightingPackId = null;
        string? lightFixtureId = null;
        if (theme.LightingPacks.Count > 0)
        {
            lightingPackId = Choose(theme.LightingPacks.Select(reference => (reference.Pack, reference.Weight)),
                seed, roomId, "lighting-pack");
            var lightingPack = prototypeManager.Index<KsProcgenLightingPackPrototype>(lightingPackId);
            lightFixtureId = Choose(lightingPack.Fixtures.Select(fixture => (fixture.Id, fixture.Weight)),
                seed, roomId, "light-fixture");
        }

        string? dominantId = null;
        var supportingIds = new List<string>();
        if (theme.EntityPacks.Count > 0)
        {
            var activityReferences = theme.EntityPacks.Where(reference => reference.Weight > 0f &&
                prototypeManager.Index<KsProcgenEntityPackPrototype>(reference.Pack).ActivityTags.Count > 0)
                .ToArray();
            if (activityReferences.Length == 0)
                activityReferences = theme.EntityPacks.ToArray();

            dominantId = Choose(activityReferences.Select(reference => (reference.Pack, reference.Weight)),
                seed, roomId, "dominant-activity");
            var dominantPack = prototypeManager.Index<KsProcgenEntityPackPrototype>(dominantId);
            foreach (var reference in theme.EntityPacks.OrderBy(reference => reference.Pack, StringComparer.Ordinal))
            {
                if (reference.Pack == dominantId || reference.Weight == 0f)
                    continue;

                var candidatePack = prototypeManager.Index<KsProcgenEntityPackPrototype>(reference.Pack);
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

        selection = new KsProcgenThemeSelection(themeId, tilePackRef, paletteId,
            wallPackRef, wallFamilyId, lightingPackId, lightFixtureId, dominantId, supportingIds);
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
