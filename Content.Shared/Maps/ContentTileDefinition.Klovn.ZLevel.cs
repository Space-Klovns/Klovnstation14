// KS14: added in this fork
namespace Content.Shared.Maps;

// ContentTileDefinition is already partial upstream, so nothing had to be done to it to hang this here.
public sealed partial class ContentTileDefinition
{
    /// <summary>
    ///     Whether light and sound from a neighbouring z-level carry through this tile as though it were not
    ///         there - a grating, a catwalk, a glass floor.
    /// </summary>
    /// <remarks>
    ///     Only ever about what carries through, never about what falls through: something standing on a
    ///         transparent tile is standing on solid floor and stays on it. The two questions are deliberately
    ///         separate, which is why this is not simply read off the tile being empty.
    /// </remarks>
    [DataField]
    public bool KsZLevelTransparent;
}
