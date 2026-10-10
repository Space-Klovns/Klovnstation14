using Content.Shared.Mobs;

namespace Content.Server._KS14.NPC.Squad;

/// <summary>
///     Raised (broadcast) by <see cref="NpcSquadSystem"/> when a squad member goes critical or dies, before it is taken
///         out of the squad, so <paramref name="SquadUid"/>'s members are still the people it went down with. A member
///         that dies after going critical has already left the squad, and is reported against the squad it was in.
/// </summary>
/// <remarks>
///     Its own event because the squad system already handles <see cref="MobStateChangedEvent"/> for squad members,
///         and only one system may.
/// </remarks>
[ByRefEvent]
public readonly record struct NpcSquadMemberDownedEvent(EntityUid SquadUid, EntityUid MemberUid, MobState NewMobState);
