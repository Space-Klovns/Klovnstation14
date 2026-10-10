using System.Numerics;
using Robust.Shared.Map;

namespace Content.Server._KS14.NPC.Squad;

/// <summary>
///     A squad's worked-out room-cover plan: the room its leader is in, that room's thresholds, and which
///         member covers which threshold from where. Built and cached by <see cref="NpcSquadCoverSystem"/>.
/// </summary>
public sealed class NpcSquadCoverPlan
{
    /// <summary>
    ///     False when the leader is not in anything recognisable as a room, in which case every other field is
    ///         empty and members should fall back to non-specific tactical positions.
    /// </summary>
    public bool HasRoom;

    public EntityUid GridUid;

    public bool IsHallway;

    /// <summary>
    ///     True when the room is the one the squad's threat is in, rather than the one its leader is in, so the
    ///         squad is heading there rather than already holding it.
    /// </summary>
    public bool SeededFromThreat;

    public readonly HashSet<Vector2i> RoomTiles = new();

    public readonly List<NpcSquadThreshold> Thresholds = new();

    public readonly Dictionary<EntityUid, NpcSquadCoverAssignment> Assignments = new();

    /// <summary>
    ///     <see cref="NpcSquadComponent.Revision"/> at the time this was built.
    /// </summary>
    public int SquadRevision;

    public EntityCoordinates? ThreatCoordinates;

    public TimeSpan ExpiresAt;
}

/// <summary>
///     A way into a room: a door, a cluster of adjacent doors, or a narrow opening.
/// </summary>
/// <param name="Tiles">The threshold's own tiles, just outside the room.</param>
/// <param name="Center">Grid-local centre of <paramref name="Tiles"/>.</param>
/// <param name="InwardNormal">Unit vector pointing from the threshold into the room.</param>
public sealed record NpcSquadThreshold(List<Vector2i> Tiles, Vector2 Center, Vector2 InwardNormal)
{
    /// <summary>
    ///     The point just inside the room in front of the threshold. Cover positions need line of sight to
    ///         this rather than to <see cref="Center"/>, because a closed airlock occludes its own tile.
    /// </summary>
    public Vector2 AimPoint => Center + InwardNormal * 0.6f;
}

/// <param name="ThresholdIndex">Index into <see cref="NpcSquadCoverPlan.Thresholds"/>.</param>
/// <param name="Coordinates">Where the member stands.</param>
/// <param name="Facing">World rotation the member faces to cover its threshold.</param>
public readonly record struct NpcSquadCoverAssignment(int ThresholdIndex, EntityCoordinates Coordinates, Angle Facing);
