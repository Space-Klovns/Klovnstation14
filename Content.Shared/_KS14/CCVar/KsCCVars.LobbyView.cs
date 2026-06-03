using Content.Shared.Administration;
using Content.Shared.CCVar.CVarAccess;
using Robust.Shared.Configuration;

namespace Content.Shared._KS14.CCVar;

public sealed partial class KsCCVars
{
    /// <summary>
    ///     Is the lobbyview mechanic enabled?
    /// </summary>
    [CVarControl(AdminFlags.Fun)]
    public static readonly CVarDef<bool> LobbyViewEnabled =
        CVarDef.Create("klovn.lobbyview.enabled", true, CVar.SERVERONLY);
}
