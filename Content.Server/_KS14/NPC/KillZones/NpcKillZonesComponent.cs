using Content.Shared.NPC.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Server._KS14.NPC.KillZones;

/// <summary>
///     The kill zones on this grid: places NPCs saw their own go down, which they stay out of for a while. Kept on the
///         grid as its tiles, so a zone on a shuttle stays where it happened as the shuttle moves. See
///         <see cref="NpcKillZoneSystem"/>.
/// </summary>
[RegisterComponent]
[Access(typeof(NpcKillZoneSystem))]
public sealed partial class NpcKillZonesComponent : Component
{
    [ViewVariables]
    public List<NpcKillZone> Zones = new();
}

/// <summary>
///     One kill zone: the floor around where someone went down, flooded out from there once when it happened, never
///         through a wall, so it covers the room or corridor they fell in and not the one next door.
/// </summary>
public sealed class NpcKillZone
{
    /// <summary>
    ///     The tile they went down on.
    /// </summary>
    public required Vector2i Center;

    /// <summary>
    ///     How dangerous each tile in the zone is, from 1 where they fell to near 0 at the zone's edge.
    /// </summary>
    public required Dictionary<Vector2i, float> Tiles;

    /// <summary>
    ///     When NPCs stop avoiding it. Pushed back when someone else goes down in the same spot.
    /// </summary>
    public required TimeSpan ExpiresAt;

    /// <summary>
    ///     The factions of whoever went down there: only NPCs sharing one avoid it.
    /// </summary>
    public required HashSet<ProtoId<NpcFactionPrototype>> Factions;
}
