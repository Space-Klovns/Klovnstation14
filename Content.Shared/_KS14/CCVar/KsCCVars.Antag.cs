using Robust.Shared.Configuration;

namespace Content.Shared._KS14.CCVar;

public sealed partial class KsCCVars
{
    /// <summary>
    ///     Whether players who haven't rolled an antag in a while are likelier to roll one.
    /// </summary>
    public static readonly CVarDef<bool> AntagPityEnabled =
        CVarDef.Create("klovn.antag.pity_enabled", true, CVar.SERVERONLY);

    /// <summary>
    ///     Selection weight of a player who hasn't rolled an antag for <see cref="AntagPityHoursToMax"/> hours of
    ///         playtime, or longer. A player who has just rolled one has a weight of 1.
    /// </summary>
    public static readonly CVarDef<float> AntagPityMaxWeight =
        CVarDef.Create("klovn.antag.pity_max_weight", 3f, CVar.SERVERONLY);

    /// <summary>
    ///     Hours of overall playtime since a player last rolled an antag before their weight reaches
    ///         <see cref="AntagPityMaxWeight"/>.
    /// </summary>
    public static readonly CVarDef<float> AntagPityHoursToMax =
        CVarDef.Create("klovn.antag.pity_hours_to_max", 10f, CVar.SERVERONLY);
}
