using Robust.Shared.Configuration;

namespace Content.Shared._KS14.CCVar;

public sealed partial class KsCCVars
{
    /// <summary>
    ///     Show tgstation's throw and resist buttons beside the hotbar. Read by the client UI only, which shows or hides
    ///         them live when it changes.
    /// </summary>
    public static readonly CVarDef<bool> CombatHudButtons =
        CVarDef.Create("klovn.hud.combat_buttons", true, CVar.CLIENTONLY | CVar.ARCHIVE);
}
