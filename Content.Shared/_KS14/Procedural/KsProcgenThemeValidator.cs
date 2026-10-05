using System.Linq;
using Content.Shared.Maps;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.Procedural;

public sealed record KsProcgenResolvedTheme(
    string Id,
    IReadOnlyList<KsProcgenWeightedPackReference> TilePacks,
    IReadOnlyList<KsProcgenWeightedPackReference> WallPacks,
    IReadOnlyList<KsProcgenWeightedPackReference> LightingPacks,
    IReadOnlyList<KsProcgenWeightedPackReference> EntityPacks,
    KsProcgenThemeGoals Goals);

/// <summary>
/// Validates theme references before generation. Operational fixture, hull, and entity-fit checks follow later.
/// </summary>
public static class KsProcgenThemeValidator
{
    public static bool TryResolve(
        IPrototypeManager prototypeManager,
        string themeId,
        out KsProcgenResolvedTheme? resolved,
        out KsProcgenIssue? issue)
    {
        resolved = null;
        issue = null;
        var chain = new List<KsProcgenRoomThemePrototype>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var currentId = themeId;
        while (true)
        {
            if (!seen.Add(currentId))
            {
                issue = new KsProcgenIssue("ThemeInheritanceCycle", $"Theme {currentId} repeats in its parent chain.");
                return false;
            }

            if (!prototypeManager.TryIndex<KsProcgenRoomThemePrototype>(currentId, out var theme))
            {
                issue = new KsProcgenIssue("UnknownTheme", $"Theme {currentId} does not exist.");
                return false;
            }

            chain.Add(theme);
            if (string.IsNullOrWhiteSpace(theme.Parent))
                break;
            currentId = theme.Parent;
        }

        foreach (var theme in chain)
        {
            var fallbackSeen = new HashSet<string>(StringComparer.Ordinal) { theme.ID };
            var fallbackId = theme.FallbackTheme;
            while (!string.IsNullOrWhiteSpace(fallbackId))
            {
                if (!fallbackSeen.Add(fallbackId))
                {
                    issue = new KsProcgenIssue("ThemeFallbackCycle", $"Theme {theme.ID} has a fallback cycle.");
                    return false;
                }

                if (!prototypeManager.TryIndex<KsProcgenRoomThemePrototype>(fallbackId, out var fallback))
                {
                    issue = new KsProcgenIssue("UnknownFallbackTheme", $"Fallback theme {fallbackId} does not exist.");
                    return false;
                }

                fallbackId = fallback.FallbackTheme;
            }
        }

        IReadOnlyList<KsProcgenWeightedPackReference>? tiles = null;
        IReadOnlyList<KsProcgenWeightedPackReference>? walls = null;
        IReadOnlyList<KsProcgenWeightedPackReference>? lights = null;
        IReadOnlyList<KsProcgenWeightedPackReference>? entities = null;
        KsProcgenThemeGoals? goals = null;
        foreach (var theme in chain.AsEnumerable().Reverse())
        {
            tiles = theme.TilePacks ?? tiles;
            walls = theme.WallPacks ?? walls;
            lights = theme.LightingPacks ?? lights;
            entities = theme.EntityPacks ?? entities;
            goals = theme.Goals ?? goals;
        }

        if (tiles == null || walls == null || tiles.Count == 0 || walls.Count == 0)
        {
            issue = new KsProcgenIssue("IncompleteTheme", "A resolved theme needs tile and wall packs.");
            return false;
        }

        lights ??= [];
        entities ??= [];
        goals ??= new KsProcgenThemeGoals();
        if (!float.IsFinite(goals.FurnishingDensity) || goals.FurnishingDensity < 0f ||
            goals.FurnishingDensity > 1f || !float.IsFinite(goals.LightingCoverage) ||
            goals.LightingCoverage < 0f || goals.LightingCoverage > 1f)
        {
            issue = new KsProcgenIssue("InvalidThemeGoal", "Density and lighting coverage must be finite fractions.");
            return false;
        }

        if (!ValidateReferences(tiles, id => prototypeManager.TryIndex<KsProcgenTilePackPrototype>(id, out _),
                "tile", out issue) ||
            !ValidateReferences(walls, id => prototypeManager.TryIndex<KsProcgenWallPackPrototype>(id, out _),
                "wall", out issue) ||
            !ValidateReferences(lights, id => prototypeManager.TryIndex<KsProcgenLightingPackPrototype>(id, out _),
                "lighting", out issue) ||
            !ValidateReferences(entities, id => prototypeManager.TryIndex<KsProcgenEntityPackPrototype>(id, out _),
                "entity", out issue))
            return false;

        foreach (var reference in tiles)
        {
            var pack = prototypeManager.Index<KsProcgenTilePackPrototype>(reference.Pack);
            if (!ValidateTilePack(pack, prototypeManager, out issue)) return false;
        }

        foreach (var reference in walls)
        {
            var pack = prototypeManager.Index<KsProcgenWallPackPrototype>(reference.Pack);
            if (!ValidateWallPack(pack, prototypeManager, out issue)) return false;
        }

        foreach (var reference in lights)
        {
            var pack = prototypeManager.Index<KsProcgenLightingPackPrototype>(reference.Pack);
            if (!ValidateLightingPack(pack, prototypeManager, out issue)) return false;
        }

        foreach (var reference in entities)
        {
            var pack = prototypeManager.Index<KsProcgenEntityPackPrototype>(reference.Pack);
            if (!ValidateEntityPack(pack, prototypeManager, out issue)) return false;
        }

        resolved = new KsProcgenResolvedTheme(themeId, tiles, walls, lights, entities, goals);
        return true;
    }

    private static bool ValidateReferences(
        IReadOnlyList<KsProcgenWeightedPackReference> references,
        Func<string, bool> exists,
        string kind,
        out KsProcgenIssue? issue)
    {
        issue = null;
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var positive = false;
        foreach (var reference in references)
        {
            if (reference == null || string.IsNullOrWhiteSpace(reference.Pack) ||
                !ids.Add(reference.Pack) || !float.IsFinite(reference.Weight) ||
                reference.Weight < 0f || reference.MinimumCount < 0 ||
                (reference.MinimumCount > 0 && reference.Weight == 0f))
            {
                issue = new KsProcgenIssue("InvalidPackReference", $"The {kind} pack list has an invalid reference.");
                return false;
            }

            if (!exists(reference.Pack))
            {
                issue = new KsProcgenIssue("UnknownPack", $"The {kind} pack {reference.Pack} does not exist.");
                return false;
            }

            positive |= reference.Weight > 0f;
        }

        if (references.Count > 0 && !positive)
        {
            issue = new KsProcgenIssue("EmptyPackPool", $"The {kind} pack list has no enabled choices.");
            return false;
        }

        return true;
    }

    private static bool ValidateTilePack(
        KsProcgenTilePackPrototype pack,
        IPrototypeManager manager,
        out KsProcgenIssue? issue)
    {
        issue = null;
        if (!ValidateChoices(pack.Palettes.Select(p => (p.Id, p.Weight)), out issue)) return false;
        foreach (var palette in pack.Palettes)
        {
            if (string.IsNullOrWhiteSpace(palette.PrimaryTile) ||
                !manager.TryIndex<ContentTileDefinition>(palette.PrimaryTile, out _) ||
                (palette.AccentTile != null &&
                 !manager.TryIndex<ContentTileDefinition>(palette.AccentTile, out _)) ||
                !float.IsFinite(palette.AccentFraction) || palette.AccentFraction < 0f ||
                palette.AccentFraction > 1f)
            {
                issue = new KsProcgenIssue("InvalidTilePalette", $"Tile pack {pack.ID} has an invalid palette.");
                return false;
            }
        }

        return true;
    }

    private static bool ValidateWallPack(
        KsProcgenWallPackPrototype pack,
        IPrototypeManager manager,
        out KsProcgenIssue? issue)
    {
        issue = null;
        if (!ValidateChoices(pack.Families.Select(f => (f.Id, f.Weight)), out issue)) return false;
        foreach (var family in pack.Families)
        {
            if (!ExistsEntity(manager, family.InteriorWall) ||
                (family.HullWall != null && !ExistsEntity(manager, family.HullWall)) ||
                (family.Door != null && !ExistsEntity(manager, family.Door)) ||
                (family.Window != null && !ExistsEntity(manager, family.Window)))
            {
                issue = new KsProcgenIssue("InvalidWallFamily", $"Wall pack {pack.ID} has an unresolved entity.");
                return false;
            }
        }

        return true;
    }

    private static bool ValidateLightingPack(
        KsProcgenLightingPackPrototype pack,
        IPrototypeManager manager,
        out KsProcgenIssue? issue)
    {
        issue = null;
        if (!ValidateChoices(pack.Fixtures.Select(f => (f.Id, f.Weight)), out issue)) return false;
        foreach (var fixture in pack.Fixtures)
        {
            if (!ExistsEntity(manager, fixture.Entity) || fixture.PreferredSpacing <= 0 ||
                !Enum.IsDefined(fixture.Supply))
            {
                issue = new KsProcgenIssue("InvalidLightingFixture", $"Lighting pack {pack.ID} has an invalid fixture.");
                return false;
            }
        }

        return true;
    }

    private static bool ValidateEntityPack(
        KsProcgenEntityPackPrototype pack,
        IPrototypeManager manager,
        out KsProcgenIssue? issue)
    {
        return KsProcgenAssemblyCompiler.TryResolvePack(manager, pack, out _, out issue);
    }

    private static bool ValidateChoices(
        IEnumerable<(string Id, float Weight)> choices,
        out KsProcgenIssue? issue)
    {
        issue = null;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var any = false;
        var positive = false;
        foreach (var (id, weight) in choices)
        {
            any = true;
            if (string.IsNullOrWhiteSpace(id) || !seen.Add(id) ||
                !float.IsFinite(weight) || weight < 0f)
            {
                issue = new KsProcgenIssue("InvalidPackChoice", "Pack choices need unique IDs and finite nonnegative weights.");
                return false;
            }

            positive |= weight > 0f;
        }

        if (!any || !positive)
        {
            issue = new KsProcgenIssue("EmptyPackChoice", "A pack needs at least one enabled choice.");
            return false;
        }

        return true;
    }

    private static bool ExistsEntity(IPrototypeManager manager, string id) =>
        !string.IsNullOrWhiteSpace(id) && manager.TryIndex<EntityPrototype>(id, out _);
}
