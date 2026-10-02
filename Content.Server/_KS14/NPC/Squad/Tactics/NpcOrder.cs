using Content.Shared._KS14.NPC;
using Robust.Shared.Map;

namespace Content.Server._KS14.NPC.Squad.Tactics;

/// <summary>
///     One order to one member, from <see cref="NpcSquadTacticsSystem"/>. HTN acts on it through
///         <c>GetOrderOperator</c>, and drops whatever it was doing for it the moment <see cref="Id"/> changes.
/// </summary>
/// <param name="Id">Unique to this order. A new order gets a new one; the same order restated keeps it.</param>
/// <param name="IssuerUid">The squad, or the NPC itself when it is on its own.</param>
/// <param name="Coordinates">Where to go, or for <see cref="NpcOrderKind.Watch"/>, where to stand.</param>
/// <param name="Facing">Which way to face once there, relative to whatever <paramref name="Coordinates"/> are relative
///     to - the grid, usually - and not to the world, so that it still means "into the room" on a grid that has
///     turned since. See <c>RotateToOrderFacingOperator</c>.</param>
/// <param name="Range">How close to <paramref name="Coordinates"/> counts as there.</param>
/// <param name="StorageUid">The locker to open, for a <see cref="NpcOrderKind.Search"/> of one.</param>
public readonly record struct NpcOrder(
    int Id,
    NpcOrderKind Kind,
    EntityUid IssuerUid,
    EntityCoordinates Coordinates,
    Angle Facing,
    float Range,
    EntityUid? StorageUid,
    TimeSpan IssuedAt);
