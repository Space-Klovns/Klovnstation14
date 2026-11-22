using Content.Shared.Administration;
using Content.Shared.CCVar.CVarAccess;
using Robust.Shared.Configuration;

namespace Content.Shared._KS14.CCVar;

public sealed partial class KsCCVars
{
    /// <summary>
    ///     Whether a Field Commander defender who heals or revives another is paid only for the share of the patient's
    ///         damage (for a revive, of what killed them) that the attacking side dealt: the commander's units, their
    ///         bullets and their explosions. Off, healing pays for everything but every defender's share, and a revive for
    ///         everything but the reviver's own, including what the world did: barbed wire, fire, a fall.
    /// </summary>
    [CVarControl(AdminFlags.Server)]
    public static readonly CVarDef<bool> FieldCommanderHealAttackerDamageOnly =
        CVarDef.Create("klovn.fieldcommander.heal_attacker_damage_only", false, CVar.SERVERONLY);
}
