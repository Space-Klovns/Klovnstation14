namespace Content.Server._KS14.Atmos.TileFire;

/// <summary>
///     Raised on a grid to ask whether anything is currently burning one of its tiles.
/// </summary>
/// <remarks>
///     Every kind of tile fire answers this for itself, which is what lets <see cref="KsTileFireSystem"/> tell
///         a tile that has stopped burning from one that merely lost one of the fires on it, without any of
///         them knowing the others exist.
/// </remarks>
/// <param name="Tile">The tile being asked about.</param>
/// <param name="IgnoredSourceUid">
///     A source to leave out of the answer, so that one can ask "is anything <em>else</em> burning this tile?".
///     For a hotspot this is the grid itself, since hotspots are tile data rather than entities.
/// </param>
[ByRefEvent]
public record struct KsGetTileFireSourcesEvent(Vector2i Tile, EntityUid IgnoredSourceUid)
{
    /// <summary>Whether anything has reported itself as burning the tile.</summary>
    public bool AnySources { get; private set; }

    /// <summary>Reports that something is burning the tile.</summary>
    public void Report()
        => AnySources = true;
}

/// <summary>
///     Raised on a grid to put out everything burning one of its tiles, as a fire extinguisher does.
/// </summary>
/// <remarks>
///     Sources are asked to stop rather than told they have stopped: each one ends on its own terms - and
///         announces the end itself, through <see cref="KsTileFireSystem.RaiseTileExtinguish"/> - which is what
///         keeps a tile with several fires on it from reading as extinguished several times over.
/// </remarks>
[ByRefEvent]
public readonly record struct KsExtinguishTileFireSourcesEvent(Vector2i Tile);
