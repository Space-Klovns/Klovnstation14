using Robust.Shared.Map;

namespace Content.Server._KS14.NPC.Squad;

/// <summary>
///     A squad of NPCs. Lives on its own nullspace entity, created and deleted by <see cref="NpcSquadSystem"/>.
/// </summary>
[RegisterComponent]
[Access(typeof(NpcSquadSystem), typeof(NpcSquadCoverSystem))]
public sealed partial class NpcSquadComponent : Component
{
    /// <summary>
    ///     Every member, the leader included.
    /// </summary>
    [ViewVariables]
    public List<EntityUid> Members = new();

    /// <summary>
    ///     Always one of <see cref="Members"/> while the squad has any.
    /// </summary>
    [ViewVariables]
    public EntityUid? Leader;

    /// <summary>
    ///     The most recently reported position of a hostile, shared by the whole squad.
    /// </summary>
    [ViewVariables]
    public EntityCoordinates? ThreatCoordinates;

    /// <summary>
    ///     When <see cref="ThreatCoordinates"/> was last reported. An old threat stops being worth going to.
    /// </summary>
    [ViewVariables]
    public TimeSpan ThreatReportedAt;

    /// <summary>
    ///     When a member last had a hostile in its sights. Unlike <see cref="ThreatReportedAt"/>, this is only
    ///         ever set by an actual sighting, never by a member repeating a threat it already knew about.
    /// </summary>
    [ViewVariables]
    public TimeSpan? LastContactAt;

    /// <summary>
    ///     Bumped on every membership or leader change, so cached work keyed on the squad's makeup can tell
    ///         when it has gone stale.
    /// </summary>
    [ViewVariables]
    public int Revision;

    /// <summary>
    ///     The squad's current room-cover plan, if one has been worked out. See <see cref="NpcSquadCoverSystem"/>.
    /// </summary>
    [ViewVariables]
    public NpcSquadCoverPlan? CoverPlan;
}
