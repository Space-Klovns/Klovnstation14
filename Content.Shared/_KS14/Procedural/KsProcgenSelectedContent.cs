using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.Procedural;

public sealed record KsProcgenSelectedTile(string PackId, string Id, string PrimaryTile, string? AccentTile, float AccentFraction);
public sealed record KsProcgenSelectedWall(string PackId, string Id, string InteriorWall, string? HullWall, string? Door, string? Window);
public sealed record KsProcgenSelectedLight(string PackId, string Id, string Entity, int PreferredSpacing, KsProcgenLightingSupply Supply);
public sealed record KsProcgenSelectedEntityPack(string Id, IReadOnlyList<KsProcgenResolvedEntityCore> Cores);

/// <summary>
/// Detached selected planning declarations, not a full library/content manifest or engine prototype
/// snapshot. Entity component bodies and inspected context remain separate verification obligations.
/// </summary>
public sealed class KsProcgenSelectedContent
{
    internal KsProcgenSelectedContent(string themeId, KsProcgenSelectedTile tile, KsProcgenSelectedWall wall,
        KsProcgenSelectedLight? light, KsProcgenResolvedThemeGoals goals, IEnumerable<KsProcgenSelectedEntityPack> packs,
        KsProcgenAtmosphereProfile? atmosphere = null)
    {
        ThemeId = themeId; Tile = tile; Wall = wall; Light = light; Goals = goals;
        EntityPacks = KsProcgenReadOnly.Copy(packs);
        Atmosphere = atmosphere;
    }

    public string ThemeId { get; }
    public KsProcgenSelectedTile Tile { get; }
    public KsProcgenSelectedWall Wall { get; }
    public KsProcgenSelectedLight? Light { get; }
    public KsProcgenResolvedThemeGoals Goals { get; }
    public IReadOnlyList<KsProcgenSelectedEntityPack> EntityPacks { get; }
    public KsProcgenAtmosphereProfile? Atmosphere { get; }

    public static bool TryTile(IPrototypeManager manager, KsProcgenThemeSelection selection, [NotNullWhen(true)] out KsProcgenSelectedTile? tile)
    {
        tile = null;
        if (selection.Content is { } content)
        {
            if (content.ThemeId != selection.ThemeId || content.Tile.PackId != selection.TilePackId || content.Tile.Id != selection.TilePaletteId) return false;
            tile = content.Tile;
            return true;
        }
        if (!manager.TryIndex<KsProcgenTilePackPrototype>(selection.TilePackId, out var pack) ||
            pack.Palettes.SingleOrDefault(candidate => candidate.Id == selection.TilePaletteId) is not { } palette) return false;
        tile = new(pack.ID, palette.Id, palette.PrimaryTile, palette.AccentTile, palette.AccentFraction);
        return true;
    }

    public static bool TryWall(IPrototypeManager manager, KsProcgenThemeSelection selection, [NotNullWhen(true)] out KsProcgenSelectedWall? wall)
    {
        wall = null;
        if (selection.Content is { } content)
        {
            if (content.ThemeId != selection.ThemeId || content.Wall.PackId != selection.WallPackId || content.Wall.Id != selection.WallFamilyId) return false;
            wall = content.Wall;
            return true;
        }
        if (!manager.TryIndex<KsProcgenWallPackPrototype>(selection.WallPackId, out var pack) ||
            pack.Families.SingleOrDefault(candidate => candidate.Id == selection.WallFamilyId) is not { } family) return false;
        wall = new(pack.ID, family.Id, family.InteriorWall, family.HullWall, family.Door, family.Window);
        return true;
    }

    public static bool TryLight(IPrototypeManager manager, KsProcgenThemeSelection selection,
        [NotNullWhen(true)] out KsProcgenSelectedLight? light, out float coverage)
    {
        light = null;
        coverage = 0f;
        if (selection.Content is { } content)
        {
            if (content.ThemeId != selection.ThemeId || content.Light is not { } captured || captured.PackId != selection.LightingPackId ||
                captured.Id != selection.LightFixtureId) return false;
            light = captured; coverage = content.Goals.LightingCoverage;
            return true;
        }
        if (!TryLightDefinition(manager, selection, out var definition) ||
            !KsProcgenThemeValidator.TryResolve(manager, selection.ThemeId, out var theme, out _)) return false;
        light = definition;
        coverage = theme!.Goals.LightingCoverage;
        return true;
    }

    private static bool TryLightDefinition(IPrototypeManager manager, KsProcgenThemeSelection selection,
        [NotNullWhen(true)] out KsProcgenSelectedLight? light)
    {
        light = null;
        if (selection.LightingPackId == null || !manager.TryIndex<KsProcgenLightingPackPrototype>(selection.LightingPackId, out var pack) ||
            pack.Fixtures.SingleOrDefault(candidate => candidate.Id == selection.LightFixtureId) is not { } fixture) return false;
        light = new(pack.ID, fixture.Id, fixture.Entity, fixture.PreferredSpacing, fixture.Supply);
        return true;
    }
}
