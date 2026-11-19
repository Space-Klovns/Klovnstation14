using Content.Shared.Administration;
using Content.Shared.CCVar.CVarAccess;
using Robust.Shared.Configuration;

namespace Content.Shared._KS14.CCVar;

public sealed partial class KsCCVars
{
    /// <summary>
    ///     Whether a Field Commander defender who heals another is paid only for the share of the patient's damage that
    ///         the attacking side dealt: the commander's units, their bullets and their explosions. Off, they are paid for
    ///         everything but the defenders' share, including what the world did: barbed wire, fire, a fall.
    /// </summary>
    [CVarControl(AdminFlags.Server)]
    public static readonly CVarDef<bool> FieldCommanderHealAttackerDamageOnly =
        CVarDef.Create("klovn.fieldcommander.heal_attacker_damage_only", false, CVar.SERVERONLY);
}
