namespace Content.Server._KS14.NPC.Squad;

/// <summary>
///     Tuning for <see cref="NpcSquadCoverSystem"/>'s room detection and cover position scoring.
/// </summary>
[DataDefinition]
public sealed partial class NpcSquadCoverSettings
{
    /// <summary>
    ///     Anything larger than this many walkable tiles, bounded by doors and narrow gaps, is not a room.
    /// </summary>
    [DataField]
    public int MaxRoomTiles = 150;

    /// <summary>
    ///     A room whose bounding box is no wider than this on its short side is a hallway, where standing off
    ///         the threshold's axis is not possible and is not asked for.
    /// </summary>
    [DataField]
    public int HallwayWidth = 3;

    [DataField]
    public float MinStandoff = 1.5f;

    [DataField]
    public float IdealStandoff = 3f;

    [DataField]
    public float MaxStandoff = 6f;

    /// <summary>
    ///     Within this many degrees of a threshold's axis is the fatal funnel: anyone coming through sees you
    ///         first.
    /// </summary>
    [DataField]
    public float FunnelAngle = 15f;

    /// <summary>
    ///     Off-axis angles in this range, in degrees, are ideal.
    /// </summary>
    [DataField]
    public float MinPreferredAngle = 25f;

    [DataField]
    public float MaxPreferredAngle = 65f;

    /// <summary>
    ///     Positions closer than this to a window or open space are penalised.
    /// </summary>
    [DataField]
    public float ExposureAvoidRange = 2.5f;

    /// <summary>
    ///     How long a cover plan lasts before being worked out again, in seconds.
    /// </summary>
    [DataField]
    public float PlanLifetime = 15f;

    /// <summary>
    ///     How far, in tiles, the reported threat has to move before the plan is worked out again.
    /// </summary>
    [DataField]
    public float ThreatMoveTolerance = 3f;

    [DataField]
    public float ClaimClearanceRadius = 1.5f;
}
