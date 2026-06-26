using Content.Shared.Administration;
using Content.Shared.CCVar.CVarAccess;
using Robust.Shared.Configuration;

namespace Content.Shared._KS14.CCVar;

public sealed partial class KsCCVars
{
    /// <summary>
    ///     Whether being able to respawn as a ghost after death is allowed.
    /// </summary>
    [CVarControl(AdminFlags.Server)]
    public static readonly CVarDef<bool> GhostRespawnEnabled =
        CVarDef.Create("klovn.ghostrespawn.enabled", false, CVar.ARCHIVE | CVar.REPLICATED | CVar.SERVER);

    /// <summary>
    ///     If false, then you your respawn timer may only start when your attached entity is
    ///         sufficiently dead. Otherwise, this restriction is removed and your respawn timer will
    ///         always start when you leave your body.
    /// </summary>
    [CVarControl(AdminFlags.Server)]
    public static readonly CVarDef<bool> GhostRespawnAlwaysStartTimer =
        CVarDef.Create("klovn.ghostrespawn.always_start_respawn_timer", true, CVar.SERVERONLY);

    /// <summary>
    ///     Amount of time (in seconds) that must be waited between last respawn (or first death)
    /// </summary>
    [CVarControl(AdminFlags.Server)]
    public static readonly CVarDef<float> GhostRespawnCooldownSeconds =
        CVarDef.Create("klovn.ghostrespawn.cooldown_seconds", 0f, CVar.SERVERONLY);

    /// <summary>
    ///     Amount of time (in seconds) added to subsequent cooldowns after every death.
    /// </summary>
    [CVarControl(AdminFlags.Server)]
    public static readonly CVarDef<float> GhostRespawnPenaltySeconds =
        CVarDef.Create("klovn.ghostrespawn.penalty_seconds", 0f, CVar.SERVERONLY);
}
