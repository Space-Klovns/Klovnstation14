using Content.Shared.Administration;
using Content.Shared.CCVar.CVarAccess;
using Robust.Shared.Configuration;

namespace Content.Shared._KS14.CCVar;

public sealed partial class KsCCVars
{
    /// <summary>
    ///     Is the external in-game announcement webhook open?
    /// </summary>
    [CVarControl(AdminFlags.Debug)]
    public static readonly CVarDef<bool> AnnouncementWebhookEnabled =
        CVarDef.Create("klovn.announcementwebhook.enabled", false, CVar.SERVERONLY);

    /// <summary>
    ///     Port to listen on, for HTTPS.
    /// </summary>
    [CVarControl(AdminFlags.Debug)]
    public static readonly CVarDef<int> AnnouncementWebhookPort =
        CVarDef.Create("klovn.announcementwebhook.port", 2200, CVar.SERVERONLY);

    /// <summary>
    ///     Should overlay stains be drawn more expensively?
    /// </summary>
    [CVarControl(AdminFlags.Debug)]
    public static readonly CVarDef<string> AnnouncementWebhookToken =
        CVarDef.Create("klovn.announcementwebhook.token", string.Empty, CVar.SERVERONLY | CVar.CONFIDENTIAL);
}
