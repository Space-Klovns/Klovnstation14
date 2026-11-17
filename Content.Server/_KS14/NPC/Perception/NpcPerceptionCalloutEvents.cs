using Content.Server._KS14.NPC.Squad;
using Robust.Shared.Map;

namespace Content.Server._KS14.NPC.Perception;

/// <summary>
///     Raised on each of <paramref name="CallerUid"/>'s squadmates (<see cref="NpcSquadSystem.CallOut{TEvent}"/>): it
///         saw <paramref name="TargetUid"/> at <paramref name="Coordinates"/>, last at <paramref name="LastSeen"/>: now, for one in
///         sight, or a moment ago, for one it only just reacted to after it slipped out of sight.
/// </summary>
/// <seealso cref="NpcPerceptionComponent.CallsOutContacts"/>
/// <seealso cref="NpcPerceptionComponent.HearsCallouts"/>
[ByRefEvent]
public readonly record struct NpcContactCalloutEvent(EntityUid CallerUid, EntityUid TargetUid, EntityCoordinates Coordinates, TimeSpan LastSeen);

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
