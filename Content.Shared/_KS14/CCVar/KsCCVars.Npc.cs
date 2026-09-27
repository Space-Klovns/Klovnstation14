using Content.Shared.Administration;
using Content.Shared.CCVar.CVarAccess;
using Robust.Shared.Configuration;

namespace Content.Shared._KS14.CCVar;

public sealed partial class KsCCVars
{
    /// <summary>
    ///     Whether squad NPCs pick positions that keep them out of each other's lines of fire: they avoid spots
    ///         whose line to what they cover passes through a teammate, and spots on a teammate's line.
    /// </summary>
    [CVarControl(AdminFlags.Debug)]
    public static readonly CVarDef<bool> NpcSquadFireLanes =
        CVarDef.Create("klovn.npc.squad_fire_lanes", true, CVar.SERVERONLY);
}
