namespace Content.Server._KS14.Atmos.TileFire;

/// <summary>
///     How strong a claim a fire source has on the tile it is burning. The highest claim on a tile is the one
///         that gets to speak for it.
/// </summary>
/// <remarks>
///     Values are spaced out so that a new kind of fire can be slotted between two existing ones without
///         renumbering anything.
/// </remarks>
public enum KsTileFireSourcePriority
{
    /// <summary>An atmospherics hotspot - the gas on the tile is burning.</summary>
    Hotspot = 0,

    /// <summary>A chemfire sitting on the tile.</summary>
    ChemicalFire = 100,
}

/// <summary>
///     Raised on a grid to ask what is currently burning one of its tiles.
/// </summary>
/// <remarks>
///     Every kind of tile fire answers this for itself, which is what lets
///         <see cref="KsTileFireSystem"/> arbitrate between them without any of them knowing the others exist.
/// </remarks>
/// <param name="Tile">The tile being asked about.</param>
/// <param name="IgnoredSourceUid">
///     A source to leave out of the answer, so that one can ask "is anything <em>else</em> burning this tile?".
///     For a hotspot this is the grid itself, since hotspots are tile data rather than entities.
/// </param>
[ByRefEvent]
public record struct KsGetTileFireSourceEvent(Vector2i Tile, EntityUid IgnoredSourceUid)
{
    /// <summary>The strongest claim reported so far, or null if nothing is burning the tile.</summary>
    public KsTileFireSourcePriority? HighestPriority { get; private set; }

    /// <summary>Reports that something of the given kind is burning the tile.</summary>
    public void Report(KsTileFireSourcePriority priority)
    {
        if (HighestPriority is not { } highestPriority || priority > highestPriority)
            HighestPriority = priority;
    }
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
