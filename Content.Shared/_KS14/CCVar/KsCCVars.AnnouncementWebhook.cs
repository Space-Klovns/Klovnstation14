using Content.Shared.Administration;
using Content.Shared.CCVar.CVarAccess;
using Robust.Shared.Configuration;

namespace Content.Shared._KS14.CCVar;

public sealed partial class KsCCVars
{
    /// <summary>
    ///     Is the external in-game announcement webhook open?
    ///         Can be changed during runtime.
    /// </summary>
    [CVarControl(AdminFlags.Debug)]
    public static readonly CVarDef<bool> AnnouncementWebhookEnabled =
        CVarDef.Create("klovn.announcementwebhook.enabled", false, CVar.SERVERONLY);

    /// <summary>
    ///     Interface to listen on. For example, a value of `http://localhost:8065/` means that
    ///         the server responds to requests directed at `localhost`, on port `8065`.
    ///         Not able to be changed during runtime; you have to restart the server.
    /// </summary>
    [CVarControl(AdminFlags.Debug)]
    public static readonly CVarDef<string> AnnouncementWebhookInterface =
        CVarDef.Create("klovn.announcementwebhook.interface", "http://localhost:8065/", CVar.SERVERONLY);

    /// <summary>
    ///     Should overlay stains be drawn more expensively?
    ///         Can be changed during runtime.
    /// </summary>
    [CVarControl(AdminFlags.Debug)]
    public static readonly CVarDef<string> AnnouncementWebhookToken =
        CVarDef.Create("klovn.announcementwebhook.token", DateTime.Now.ToString() /* dont get trolled */, CVar.SERVERONLY | CVar.CONFIDENTIAL);
}
