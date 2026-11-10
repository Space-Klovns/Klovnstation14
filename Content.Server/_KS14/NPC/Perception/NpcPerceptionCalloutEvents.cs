using Content.Server._KS14.NPC.Squad;
using Robust.Shared.Map;

namespace Content.Server._KS14.NPC.Perception;

/// <summary>
///     Raised on each of <paramref name="CallerUid"/>'s squadmates (<see cref="NpcSquadSystem.CallOut{TEvent}"/>): it
///         sees <paramref name="TargetUid"/> at <paramref name="Coordinates"/>.
/// </summary>
/// <seealso cref="NpcPerceptionComponent.CallsOutContacts"/>
/// <seealso cref="NpcPerceptionComponent.HearsCallouts"/>
[ByRefEvent]
public readonly record struct NpcContactCalloutEvent(EntityUid CallerUid, EntityUid TargetUid, EntityCoordinates Coordinates);

/// <summary>
///     Raised on each of <paramref name="CallerUid"/>'s squadmates (<see cref="NpcSquadSystem.CallOut{TEvent}"/>): it
///         knows <paramref name="TargetUid"/> is dead.
/// </summary>
/// <seealso cref="NpcPerceptionComponent.SharesKills"/>
[ByRefEvent]
public record struct NpcKillCalloutEvent(EntityUid CallerUid, EntityUid TargetUid)
{
    /// <summary>
    ///     Set by any squadmate that believed the hostile alive until now.
    /// </summary>
    public bool News;
}
