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

    /// <summary>
    ///     How many line of sight checks NPCs may spend per tick weighing how exposed a spot is (see
    ///         <c>NpcExposureSystem</c>), across every NPC. A check costs about 2µs, so the default 600 is about 1.2ms, under
    ///         4% of a 30 TPS tick. A search that would go over waits for a later tick, or does without, as it is set up
    ///         to. 0 turns exposure off.
    /// </summary>
    [CVarControl(AdminFlags.Debug)]
    public static readonly CVarDef<int> NpcExposureRayBudget =
        CVarDef.Create("klovn.npc.exposure_ray_budget", 600, CVar.SERVERONLY);

    /// <summary>
    ///     How many navmesh polys an NPC's path search may expand before giving up and reporting no path. Steered by
    ///         <see cref="NpcPathHierarchical"/>, a search walks almost straight down its path: on Box, no path took more
    ///         than 238, at any distance. What the limit still bounds is a search the coarse maps steer badly - an NPC
    ///         going round doors it has found it cannot get through, which the maps do not know about. Without
    ///         <see cref="NpcPathHierarchical"/>, upstream's 512 finds most paths up to 15 tiles, about half from 15 to
    ///         30, a quarter from 30 to 50 and none further, and a search for somewhere that cannot be reached runs to the
    ///         limit every time.
    /// </summary>
    [CVarControl(AdminFlags.Debug)]
    public static readonly CVarDef<int> NpcPathNodeLimit =
        CVarDef.Create("klovn.npc.path_node_limit", 512, CVar.SERVERONLY);

    /// <summary>
    ///     Whether NPC path searches are steered by coarse maps of the navmesh, one per 8x8 chunk: what each part of the
    ///         station costs to walk to the goal from, worked out before the search. With it, a search walks almost
    ///         straight down the path, so long paths fit in <see cref="NpcPathNodeLimit"/>, and a goal that cannot be
    ///         reached is given up on at once. Off, searches go by straight-line distance alone, as upstream. The paths
    ///         are the same either way, where both find one.
    /// </summary>
    [CVarControl(AdminFlags.Debug)]
    public static readonly CVarDef<bool> NpcPathHierarchical =
        CVarDef.Create("klovn.npc.path_hierarchical", true, CVar.SERVERONLY);
}
