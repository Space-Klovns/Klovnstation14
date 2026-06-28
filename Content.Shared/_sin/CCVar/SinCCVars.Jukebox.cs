using Robust.Shared.Configuration;

namespace Content.Shared._sin.CCVar;

public static partial class SinCCVars
{
    /// <summary>
    /// Favorite jukebox/boombox track IDs, stored client-side.
    /// </summary>
    public static readonly CVarDef<string> SinJukeboxFavorites =
        CVarDef.Create("sin_jukebox_favorites", "", CVar.CLIENTONLY | CVar.ARCHIVE);
}
