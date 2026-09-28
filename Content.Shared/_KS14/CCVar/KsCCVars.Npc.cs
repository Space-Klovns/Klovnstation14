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

    /// <summary>
    ///     Whether NPCs judge whether they can see a target by how well lit it is: a target in the dark goes
    ///         unnoticed unless it is close, and a dimly lit one takes longer to react to.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Needs <c>lookup.enable_server_light_tree = true</c>
    ///         (<see cref="Robust.Shared.CVars.LookupEnableServerLightTree"/>) as well. The server keeps no light
    ///         tree without it, so it cannot compute light levels, and this then treats every target as fully lit.
    ///         That cvar is only read at startup, so it has to be set in the server config rather than at runtime;
    ///         this one can be flipped live.
    ///     </para>
    ///     <para>
    ///         Measured cost, on box.yml (989 lights): about 27us per light level (p99 78us), cached per target
    ///             per tick and shared by every NPC; plus keeping the light tree current, about 1.5us per light
    ///             that moved since the last query. A few dozen NPCs come to well under 1ms of a 33ms tick.
    ///     </para>
    /// </remarks>
    [CVarControl(AdminFlags.Debug)]
    public static readonly CVarDef<bool> NpcLightDetection =
        CVarDef.Create("klovn.npc.light_detection", false, CVar.SERVERONLY);
}
